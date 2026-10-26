#include <shcore.h>
#include <winrt/Windows.UI.Xaml.Data.h>
#include <winrt/Windows.Graphics.Imaging.h>

struct __declspec(uuid("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D")) ApplicationMemoryBytes : IUnknown {
    virtual HRESULT STDMETHODCALLTYPE GetBuffer(BYTE**,UINT32*)=0;
};
struct ApplicationBitmapLock {
    Windows::Graphics::Imaging::BitmapBuffer buffer{nullptr};
    Windows::Foundation::IMemoryBufferReference reference{nullptr};
    ~ApplicationBitmapLock() {
        try { if(reference) reference.Close(); } catch(...) { }
        try { if(buffer) buffer.Close(); } catch(...) { }
    }
};

constexpr int ApplicationImageLimit=128, ApplicationImageBytes=65536;
struct ApplicationImageRecord {
    ULONG64 window;
    UINT id;
    GUID guid;
    HRESULT error;
    ULONG length, reserved;
    wchar_t model[128], imageType[128];
    BYTE bytes[ApplicationImageBytes];
};
static_assert(offsetof(ApplicationImageRecord,bytes)==552);
static_assert(sizeof(ApplicationImageRecord)==66088);
struct ApplicationImageShared {
    LONG version;
    volatile LONG sequence;
    LONG count, error, ticks, reserved[3];
    ULONG64 symbols[8];
    ULONG64 viewSymbols[12];
    ULONGLONG updated;
    ApplicationImageRecord records[ApplicationImageLimit];
};
static_assert(offsetof(ApplicationImageShared,records)==200);
static_assert(sizeof(ApplicationImageShared)==8459464);

struct ApplicationImageState {
    HANDLE mapping{};
    ApplicationImageShared* data{};
    ULONGLONG lastRead{};
    ~ApplicationImageState() { if(data) UnmapViewOfFile(data); if(mapping) CloseHandle(mapping); }

    static std::unique_ptr<ApplicationImageState> Open(PCWSTR sessionName) {
        std::wstring name=std::wstring(sessionName)+L".Applications";
        auto state=std::make_unique<ApplicationImageState>();
        state->mapping=OpenFileMappingW(FILE_MAP_ALL_ACCESS,FALSE,name.c_str());
        if(!state->mapping) return nullptr;
        state->data=static_cast<ApplicationImageShared*>(MapViewOfFile(state->mapping,FILE_MAP_ALL_ACCESS,0,0,sizeof(ApplicationImageShared)));
        if(!state->data || state->data->version!=3) throw hresult_error(E_INVALIDARG);
        return state;
    }

    // Find a specific interface using the PDB-resolved vtable, not a guessed IID
    // or an arbitrary slot in an unknown interface. All calls stay on the XAML STA.
    com_ptr<::IInspectable> FindInterface(Windows::Foundation::IInspectable const& model, BYTE* vtable) {
        if(!model) return {};
        auto inspectable=model.as<::IInspectable>();
        ULONG count{};
        IID* ids{};
        check_hresult(inspectable->GetIids(&count,&ids));
        std::unique_ptr<IID,decltype(&CoTaskMemFree)> holder(ids,CoTaskMemFree);
        if(count>64) throw hresult_error(E_BOUNDS);
        for(ULONG i=0;i<count;i++) {
            com_ptr<::IInspectable> candidate;
            if(SUCCEEDED(inspectable->QueryInterface(ids[i],candidate.put_void())) &&
                *reinterpret_cast<void***>(candidate.get())==reinterpret_cast<void**>(vtable)) return candidate;
        }
        return {};
    }

    void ReadModel(Windows::Foundation::IInspectable model, ApplicationImageRecord& record, BYTE* base, int depth=0) {
        if(!model || depth>4) return;
        auto name=get_class_name(model);
        wcsncpy_s(record.model,name.c_str(),_TRUNCATE);
        auto candidate=FindInterface(model,base+data->symbols[7]);
        if(!candidate) {
            auto viewBase=reinterpret_cast<BYTE*>(GetModuleHandleW(L"SystemTray.dll"));
            if(name==L"SystemTray.ImageIconContentViewModel") {
                auto wrapper=FindInterface(model,viewBase+data->viewSymbols[10]);
                if(!wrapper) throw hresult_error(E_NOINTERFACE);
                auto accessor=viewBase+data->viewSymbols[9];
                auto constructor=viewBase+data->viewSymbols[11];
                constexpr BYTE accessorPattern[]={0x48,0x8D,0x59,0x50,0xB8,0x60,0,0,0};
                constexpr BYTE constructorInput[]={0x48,0x8B,0xFA,0x48,0x8B,0xD9};
                constexpr BYTE constructorStore[]={0x48,0x8D,0xB3,0x88,0,0,0,0x48,0x8B,0x0F,0x48,0x89,0x0E};
                if(memcmp(accessor+0x11,accessorPattern,sizeof(accessorPattern)) ||
                    memcmp(constructor+0x15,constructorInput,sizeof(constructorInput)) ||
                    memcmp(constructor+0xE6,constructorStore,sizeof(constructorStore))) throw hresult_error(E_NOTIMPL);
                // These verified instructions establish the default interface at
                // implementation+0x10 and the constructor's IIconDataModel at +0x88.
                // Keep the owning view model alive while taking a strong COM ref.
                auto implementation=reinterpret_cast<BYTE*>(wrapper.get())-0x10;
                void* value{};
                SIZE_T read{};
                if(!ReadProcessMemory(GetCurrentProcess(),implementation+0x88,&value,sizeof(value),&read) ||
                    read!=sizeof(value) || !value) throw hresult_error(E_BOUNDS);
                BYTE* table{};
                if(!ReadProcessMemory(GetCurrentProcess(),value,&table,sizeof(table),&read) || read!=sizeof(table) ||
                    table<viewBase || table>viewBase+data->viewSymbols[1]-48) throw hresult_error(E_BOUNDS);
                com_ptr<::IInspectable> dataModel;
                dataModel.copy_from(static_cast<::IInspectable*>(value));
                ReadModel(dataModel.as<Windows::Foundation::IInspectable>(),record,base,depth+1);
                return;
            }
            int slot=name==L"SystemTray.IconViewModel"?2:name==L"SystemTray.NotificationAreaIconsDataModel"?7:0;
            if(slot) {
                auto wrapper=FindInterface(model,viewBase+data->viewSymbols[slot+1]);
                if(wrapper) {
                    com_ptr<::IInspectable> value;
                    check_hresult(reinterpret_cast<HRESULT(__stdcall*)(void*,void**)>(viewBase+data->viewSymbols[slot])(wrapper.get(),value.put_void()));
                    if(value) ReadModel(value.as<Windows::Foundation::IInspectable>(),record,base,depth+1);
                    return;
                }
            }
            record.error=E_NOINTERFACE;
            return;
        }

        // Validate the accessor instructions before deriving any object address.
        // The Identity getter explicitly copies HWND, UINT and GUID from its
        // implementation. The Icon ABI getter shows the implementation/interface
        // adjustment via its null-this member access. Unknown code => fallback.
        auto identity=base+data->symbols[5];
        constexpr BYTE identityPrefix[]={0x40,0x53,0x48,0x83,0xEC,0x30,0x48,0x8B,0x41,0x50,
            0x48,0x8B,0xDA,0x48,0x89,0x02,0x8B,0x41,0x58,0x89,0x42,0x08,
            0x0F,0x10,0x41,0x5C,0xF3,0x0F,0x7F,0x42,0x0C};
        auto getter=base+data->symbols[6];
        if(memcmp(identity,identityPrefix,sizeof(identityPrefix)) ||
            getter[0x1E]!=0x48 || getter[0x1F]!=0x8D || getter[0x20]!=0x9E || getter[0x25]!=0xB8)
            throw hresult_error(E_NOTIMPL);
        ULONG interfaceMember{}, implementationMember{};
        memcpy(&interfaceMember,getter+0x21,4);
        memcpy(&implementationMember,getter+0x26,4);
        if(implementationMember<interfaceMember || implementationMember-interfaceMember>128) throw hresult_error(E_NOTIMPL);
        auto implementation=reinterpret_cast<BYTE*>(candidate.get())-(implementationMember-interfaceMember);
        BYTE owner[28];
        SIZE_T read{};
        if(!ReadProcessMemory(GetCurrentProcess(),implementation+0x50,owner,sizeof(owner),&read) || read!=sizeof(owner))
            throw hresult_error(E_BOUNDS);
        memcpy(&record.window,owner,8);
        memcpy(&record.id,owner+8,4);
        memcpy(&record.guid,owner+12,16);
        if(!IsWindow(reinterpret_cast<HWND>(record.window))) throw hresult_error(E_BOUNDS);

        com_ptr<::IUnknown> source;
        check_hresult(reinterpret_cast<HRESULT(__stdcall*)(void*,void**)>(getter)(candidate.get(),source.put_void()));
        if(!source) throw hresult_error(E_FAIL);
        if(auto inspectable=source.try_as<Windows::Foundation::IInspectable>())
            wcsncpy_s(record.imageType,get_class_name(inspectable).c_str(),_TRUNCATE);
        if(auto bitmap=source.try_as<Windows::Graphics::Imaging::SoftwareBitmap>()) {
            using namespace Windows::Graphics::Imaging;
            int width=bitmap.PixelWidth(), height=bitmap.PixelHeight();
            if(width<=0 || height<=0 || width>128 || height>128) throw hresult_error(E_BOUNDS);
            if(bitmap.BitmapPixelFormat()!=BitmapPixelFormat::Bgra8 || bitmap.BitmapAlphaMode()!=BitmapAlphaMode::Premultiplied)
                bitmap=SoftwareBitmap::Convert(bitmap,BitmapPixelFormat::Bgra8,BitmapAlphaMode::Premultiplied);
            ApplicationBitmapLock locked{bitmap.LockBuffer(BitmapBufferAccessMode::Read)};
            locked.reference=locked.buffer.CreateReference();
            BYTE* pixels{};
            UINT32 capacity{};
            check_hresult(locked.reference.as<ApplicationMemoryBytes>()->GetBuffer(&pixels,&capacity));
            auto plane=locked.buffer.GetPlaneDescription(0);
            if(!pixels || plane.Width!=width || plane.Height!=height) throw hresult_error(E_BOUNDS);
            bool visible=false;
            for(int y=0;y<height;y++) {
                LONGLONG offset=static_cast<LONGLONG>(plane.StartIndex)+static_cast<LONGLONG>(y)*plane.Stride;
                if(offset<0 || offset+width*4>capacity) throw hresult_error(E_BOUNDS);
                memcpy(record.bytes+y*width*4,pixels+offset,width*4);
                for(int x=0;x<width;x++) if(record.bytes[(y*width+x)*4+3]) visible=true;
            }
            if(!visible) throw hresult_error(E_FAIL);
            record.reserved=width|(height<<16);
            record.length=width*height*4;
            record.error=S_OK;
            return;
        }
        auto stream=source.try_as<Windows::Storage::Streams::IRandomAccessStream>();
        if(!stream) throw hresult_error(E_NOINTERFACE);
        auto size=stream.Size();
        if(!size || size>ApplicationImageBytes) throw hresult_error(E_BOUNDS);
        // Clone before seeking, so Explorer's image resource is never modified.
        auto clone=stream.CloneStream();
        com_ptr<IStream> input;
        check_hresult(CreateStreamOverRandomAccessStream(get_unknown(clone),IID_PPV_ARGS(input.put())));
        LARGE_INTEGER start{};
        check_hresult(input->Seek(start,STREAM_SEEK_SET,nullptr));
        ULONG length{};
        check_hresult(input->Read(record.bytes,static_cast<ULONG>(size),&length));
        if(length!=size) throw hresult_error(E_FAIL);
        record.length=length;
        record.error=S_OK;
    }

    void Walk(DependencyObject const& node, BYTE* base, int depth=0) {
        if(depth>40 || data->count>=ApplicationImageLimit) return;
        if(auto element=node.try_as<FrameworkElement>()) {
            if(get_class_name(element)==L"SystemTray.NotifyIconView") {
                auto& record=data->records[data->count++];
                ZeroMemory(&record,552);
                record.error=E_NOINTERFACE;
                try {
                    auto model=element.DataContext();
                    if(!model) if(auto content=element.try_as<ContentControl>()) model=content.Content();
                    ReadModel(model,record,base);
                } catch(...) { record.error=to_hresult(); record.length=0; }
            }
        }
        int count=Media::VisualTreeHelper::GetChildrenCount(node);
        for(int i=0;i<count;i++) Walk(Media::VisualTreeHelper::GetChild(node,i),base,depth+1);
    }

    void Read(FrameworkElement const& frame) noexcept {
        if(GetTickCount64()-lastRead<1000) return;
        lastRead=GetTickCount64();
        InterlockedIncrement(&data->sequence);
        data->count=0;
        data->error=0;
        try {
            auto base=reinterpret_cast<BYTE*>(GetModuleHandleW(L"taskbar.dll"));
            auto headers=base?ImageNtHeader(base):nullptr;
            if(!headers || headers->FileHeader.TimeDateStamp!=data->symbols[0] || headers->OptionalHeader.SizeOfImage!=data->symbols[1])
                throw hresult_error(HRESULT_FROM_WIN32(ERROR_REVISION_MISMATCH));
            for(int i=2;i<8;i++) if(!data->symbols[i] || data->symbols[i]>=data->symbols[1]-128) throw hresult_error(E_NOTIMPL);
            auto viewBase=reinterpret_cast<BYTE*>(GetModuleHandleW(L"SystemTray.dll"));
            auto viewHeaders=viewBase?ImageNtHeader(viewBase):nullptr;
            if(!viewHeaders || viewHeaders->FileHeader.TimeDateStamp!=data->viewSymbols[0] ||
                viewHeaders->OptionalHeader.SizeOfImage!=data->viewSymbols[1]) throw hresult_error(HRESULT_FROM_WIN32(ERROR_REVISION_MISMATCH));
            for(int i=2;i<12;i++) if(!data->viewSymbols[i] || data->viewSymbols[i]>=data->viewSymbols[1]-512) throw hresult_error(E_NOTIMPL);
            Walk(frame,base);
        } catch(...) { data->error=to_hresult(); }
        data->updated=GetTickCount64();
        data->ticks++;
        InterlockedIncrement(&data->sequence);
    }
};
