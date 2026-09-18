// SPDX-License-Identifier: GPL-3.0-only
// Taskbar host discovery adapted from m417z's Taskbar tray system icon tweaks.
// See README.md and LICENSE for attribution and source details.
#include <windows.h>
#include <dbghelp.h>
#include <ocidl.h>
#include <xamlom.h>
#undef GetCurrentTime
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.UI.Xaml.h>
#include <winrt/Windows.UI.Xaml.Controls.h>
#include <winrt/Windows.UI.Xaml.Media.h>
#include <atomic>
#include <algorithm>
#include <memory>
#include <mutex>
#include <vector>

using namespace winrt;
using namespace Windows::UI::Xaml;
using namespace Windows::UI::Xaml::Controls;
constexpr GUID ClassId{0x56c9cbd1,0x7e71,0x4929,{0x93,0x15,0x0c,0xaa,0x4d,0x52,0xb7,0x61}};
HMODULE module;
struct IconAppearance { wchar_t text[16], font[64]; };
struct Shared {
    LONG version, owner;
    volatile LONG requested, stop, found, hidden, ticks, error, alive, contexts, requestId, responseId;
    ULONG timestamp, imageSize;
    LONG reserved[2];
    ULONG64 symbols[4];
    IconAppearance icons[12];
};
struct Session {
    HANDLE mapping{}, owner{};
    Shared* data{};
    ~Session() { if(data) UnmapViewOfFile(data); if(mapping) CloseHandle(mapping); if(owner) CloseHandle(owner); }
    bool stopped() { return data->stop || WaitForSingleObject(owner,0) != WAIT_TIMEOUT; }
};
struct Original {
    weak_ref<FrameworkElement> element;
    Windows::Foundation::IInspectable local{nullptr};
    bool touched=true;
};
FrameworkElement TaskbarRoot(HWND window,Shared* data);
struct Context {
    std::shared_ptr<Session> session;
    weak_ref<FrameworkElement> root;
    std::vector<Original> originals;
    UINT_PTR timer{};
    bool updating{};
    bool sharedMicrophoneLocation{};
    HWND window{};
    IconAppearance icons[12]{};
    bool ReadAppearance(DependencyObject const& node, LONG kind, IconAppearance& result, int depth=0) {
        if(depth>12 || kind==8 || kind==2048) return false;
        if(auto text=node.try_as<TextBlock>()) {
            auto value=text.Text();
            if(!value.empty() && value.size()<16 && (Glyph(value)&kind || (kind==256 && value.size()<=4))) {
                auto font=text.FontFamily().Source();
                if(font.size()<64) {
                    if(!result.text[0]) { wcscpy_s(result.text,value.c_str()); wcscpy_s(result.font,font.c_str()); }
                    else if(value.size()==1 && Glyph(value) && font==result.font && !wcschr(result.text,value[0]) && wcslen(result.text)<15)
                        wcscat_s(result.text,value.c_str());
                }
            }
        }
        int count=Media::VisualTreeHelper::GetChildrenCount(node);
        for(int i=0;i<count;i++) ReadAppearance(Media::VisualTreeHelper::GetChild(node,i),kind,result,depth+1);
        return result.text[0]!=0;
    }
    void Set(FrameworkElement const& element, bool hide) {
        auto it = std::find_if(originals.begin(), originals.end(), [&](auto const& o) { return o.element.get() == element; });
        if(hide) {
            if(it == originals.end()) originals.push_back({make_weak(element), element.ReadLocalValue(UIElement::VisibilityProperty())});
            else it->touched=true;
            if(element.Visibility() != Visibility::Collapsed) element.Visibility(Visibility::Collapsed);
        } else if(it != originals.end()) {
            if(it->local == DependencyProperty::UnsetValue()) element.ClearValue(UIElement::VisibilityProperty());
            else element.SetValue(UIElement::VisibilityProperty(), it->local);
            originals.erase(it);
        }
    }
    void Restore() {
        for(auto const& original : originals) {
            try {
                if(auto element = original.element.get()) {
                    if(original.local == DependencyProperty::UnsetValue()) element.ClearValue(UIElement::VisibilityProperty());
                    else element.SetValue(UIElement::VisibilityProperty(), original.local);
                }
            } catch(...) { session->data->error = to_hresult(); }
        }
        originals.clear();
    }
    // Glyphs are Segoe Fluent / taskbar font identities, independent of locale.
    static LONG Glyph(hstring const& text) {
        if(text.size()!=1) return 0;
        wchar_t c=text[0];
        if(c==0xE74F || (c>=0xE992 && c<=0xE995) || c==0xEA85 || c==0xEBC5) return 1;
        if(c==0xE709 || c==0xE7F4 || c==0xE839 || (c>=0xE86C && c<=0xE870) ||
           (c>=0xEAA1 && c<=0xEAA5) || c==0xEAA8 || c==0xEC1E ||
           (c>=0xEC3C && c<=0xEC3F) || c==0xF384 || (c>=0xF8C0 && c<=0xF8CC)) return 2;
        if((c>=0xE3C1 && c<=0xE3CB) || (c>=0xE408 && c<=0xE41D) ||
           (c>=0xEBA0 && c<=0xEBC0) || c==0xEB17 || c==0xEC02 || c==0xF1E8) return 4;
        if(c==0xE361 || c==0xE720 || c==0xEC71) return 16;
        if(c==0xE37A) return 32;
        if(c==0xF47F) return 16|32;
        if(c==0xEABC) return 64;
        if(c==0xEC83 || c==0xEADD || c==0xEB16 || c==0xEF97 || c==0xF1C6) return 128;
        if(c==0xE4D7 || c==0xE4D8 || c==0xE5BF || (c>=0xE97E && c<=0xE980) || c==0xE982 || c==0xE983 ||
           (c>=0xE986 && c<=0xE988) || c==0xEB90 || (c>=0xEE41 && c<=0xEE45) || c==0xEE75 || c==0xEE76) return 512;
        if(c==0xF2A3 || c==0xF285 || c==0xF2A5 || c==0xF2A8) return 1024;
        return 0;
    }
    LONG ContentKind(DependencyObject const& node, int depth=0) {
        if(depth>12) return 0;
        if(get_class_name(node)==L"SystemTray.BatteryIconContent") return 4;
        if(get_class_name(node)==L"SystemTray.LanguageTextIconContent" || get_class_name(node)==L"SystemTray.LanguageImageIconContent") return 256;
        if(get_class_name(node)==L"SystemTray.ImageIconContent") return 512;
        if(auto text=node.try_as<TextBlock>()) if(LONG kind=Glyph(text.Text())) return kind;
        int count=Media::VisualTreeHelper::GetChildrenCount(node);
        for(int i=0;i<count;i++) if(LONG kind=ContentKind(Media::VisualTreeHelper::GetChild(node,i),depth+1)) return kind;
        return 0;
    }
    void Walk(DependencyObject const& node, LONG mask, LONG& found, LONG& visible, int depth=0, int region=0) {
        if(depth>32) return;
        auto element=node.try_as<FrameworkElement>();
        if(!element) return;
        auto name=element.Name(); auto type=get_class_name(element);
        if(name==L"ControlCenterButton") region=1;
        else if(name==L"MainStack") region=2;
        else if(name==L"NonActivatableStack") region=3;
        else if(name==L"NotificationCenterButton") region=4;
        LONG kind=0;
        if(name==L"Clock" || type==L"SystemTray.DateTimeIconContent") kind=8;
        if(region==1 && (type==L"SystemTray.TextIconContent" || type==L"SystemTray.BatteryIconContent")) kind=ContentKind(element)&7;
        if(region==2 && name==L"SystemTrayIcon") kind=ContentKind(element)&(16|32|64|128);
        if(region==3 && name==L"SystemTrayIcon") kind=ContentKind(element)&(256|512);
        if(region==4 && type==L"SystemTray.TextIconContent") kind=1024;
        if(name==L"ShowDesktopStack") kind=2048;
        if(kind) {
            found|=kind;
            IconAppearance appearance{};
            try { ReadAppearance(element,kind,appearance); } catch(...) { /* Appearance is optional. */ }
            for(int i=0;i<12;i++) if(kind&(1<<i)) icons[i]=appearance;
            if(kind==(16|32)) sharedMicrophoneLocation=true;
            // A combined privacy indicator must remain unless both are requested.
            Set(element,(mask&kind)==kind);
            if(element.Visibility()!=Visibility::Collapsed) visible|=kind;
            return;
        }
        int count=Media::VisualTreeHelper::GetChildrenCount(node);
        for(int i=0;i<count;i++) Walk(Media::VisualTreeHelper::GetChild(node,i),mask,found,visible,depth+1,region);
    }
    void Tick() noexcept {
        if(updating) return;
        updating=true;
        try {
            auto data=session->data;
            if(session->stopped()) {
                Restore(); KillTimer(nullptr,timer); timer=0;
                InterlockedDecrement(&data->contexts);
                if(data->contexts==0) data->alive=2;
            } else {
                auto frame=root.get();
                if(!frame || !frame.XamlRoot()) {
                    Restore(); frame=TaskbarRoot(window,data); root=make_weak(frame);
                }
                if(!frame) { data->found=0; data->hidden=0; throw hresult_error(E_NOTIMPL); }
                LONG request=data->requestId, mask=data->requested, found=0, visible=0;
                for(auto& original:originals) original.touched=false;
                sharedMicrophoneLocation=false;
                ZeroMemory(icons,sizeof(icons));
                Walk(frame,mask,found,visible);
                // If an input method switches between text and image content,
                // restore emptied/reclassified containers so XAML can repopulate them.
                for(size_t i=0;i<originals.size();) {
                    if(originals[i].touched) { i++; continue; }
                    if(auto stale=originals[i].element.get()) Set(stale,false);
                    else originals.erase(originals.begin()+i);
                }
                data->found=found; data->hidden=found&~visible; data->reserved[0]=sharedMicrophoneLocation;
                InterlockedIncrement(&data->reserved[1]);
                memcpy(data->icons,icons,sizeof(icons));
                InterlockedIncrement(&data->reserved[1]);
                data->responseId=request; InterlockedIncrement(&data->ticks); data->alive=1;
            }
        } catch(...) { session->data->error=to_hresult(); }
        updating=false;
    }
};
thread_local std::vector<std::unique_ptr<Context>> contexts;
void CALLBACK Tick(HWND,UINT,UINT_PTR id,DWORD) {
    for(auto& context:contexts) if(context->timer==id) { context->Tick(); break; }
    std::erase_if(contexts,[](auto const& c){return c->timer==0;});
}

FrameworkElement TrayFrame(DependencyObject const& node,int depth=0) {
    if(!node || depth>20) return nullptr;
    if(get_class_name(node)==L"SystemTray.SystemTrayFrame") return node.try_as<FrameworkElement>();
    int count=Media::VisualTreeHelper::GetChildrenCount(node);
    for(int i=0;i<count;i++) if(auto result=TrayFrame(Media::VisualTreeHelper::GetChild(node,i),depth+1)) return result;
    return nullptr;
}
FrameworkElement TaskbarRoot(HWND window,Shared* data) {
    auto base=reinterpret_cast<BYTE*>(GetModuleHandleW(L"taskbar.dll"));
    if(!base) throw hresult_error(E_NOTIMPL);
    auto headers=ImageNtHeader(base);
    if(!headers || headers->FileHeader.TimeDateStamp!=data->timestamp || headers->OptionalHeader.SizeOfImage!=data->imageSize)
        throw hresult_error(HRESULT_FROM_WIN32(ERROR_REVISION_MISMATCH));
    for(auto offset:data->symbols) if(!offset || offset>=data->imageSize) throw hresult_error(E_INVALIDARG);
    auto bandWindow=reinterpret_cast<HWND>(GetPropW(window,L"TaskbandHWND"));
    auto band=reinterpret_cast<void**>(GetWindowLongPtrW(bandWindow,0));
    if(!band) throw hresult_error(E_NOTIMPL);
    int index=0;
    while(index<20 && band[index]!=base+data->symbols[0]) index++;
    if(index==20) throw hresult_error(E_NOTIMPL);
    auto bytes=base+data->symbols[2];
    // Refuse unknown layouts rather than using a guessed object offset.
    if(bytes[0]!=0x48 || bytes[1]!=0x83 || bytes[2]!=0xEC || bytes[4]!=0x48 ||
       bytes[5]!=0x83 || bytes[6]!=0xC1 || bytes[7]>0x7F) throw hresult_error(E_NOTIMPL);
    void* shared[2]{};
    reinterpret_cast<void*(__stdcall*)(void*,void**)>(base+data->symbols[1])(band+index,shared);
    if(!shared[0] || !shared[1]) throw hresult_error(E_NOTIMPL);
    FrameworkElement result{nullptr};
    try {
        auto object=*reinterpret_cast<IUnknown**>(static_cast<BYTE*>(shared[0])+bytes[7]);
        if(object) check_hresult(object->QueryInterface(guid_of<FrameworkElement>(),put_abi(result)));
    } catch(...) {
        reinterpret_cast<void(__stdcall*)(void*)>(base+data->symbols[3])(shared[1]); throw;
    }
    reinterpret_cast<void(__stdcall*)(void*)>(base+data->symbols[3])(shared[1]);
    return result ? TrayFrame(result.XamlRoot().Content()) : nullptr;
}
struct BootstrapRequest { std::shared_ptr<Session> session; HWND window; HRESULT result=E_FAIL; };
std::mutex bootstrapMutex;
std::atomic<BootstrapRequest*> pendingBootstrap;
UINT BootstrapMessage() { static UINT id=RegisterWindowMessageW(L"TrayPilot.Xaml.Bootstrap.56c9cbd1"); return id; }
LRESULT CALLBACK BootstrapHook(int code,WPARAM w,LPARAM l) {
    if(code==HC_ACTION && reinterpret_cast<CWPSTRUCT*>(l)->message==BootstrapMessage()) {
        if(auto request=pendingBootstrap.exchange(nullptr)) {
            try {
                auto frame=TaskbarRoot(request->window,request->session->data);
                if(!frame) throw hresult_error(E_NOTIMPL);
                auto context=std::make_unique<Context>();
                context->session=request->session; context->root=make_weak(frame); context->window=request->window;
                context->timer=SetTimer(nullptr,0,250,Tick);
                if(!context->timer) throw_last_error();
                InterlockedIncrement(&request->session->data->contexts);
                contexts.push_back(std::move(context));
                request->result=S_OK;
            } catch(...) { request->result=to_hresult(); }
        }
    }
    return CallNextHookEx(nullptr,code,w,l);
}
HRESULT Bootstrap(std::shared_ptr<Session> session) {
    std::lock_guard lock(bootstrapMutex);
    HWND window=FindWindowW(L"Shell_TrayWnd",nullptr); DWORD pid{};
    DWORD thread=GetWindowThreadProcessId(window,&pid);
    if(pid!=GetCurrentProcessId()) return E_ACCESSDENIED;
    BootstrapRequest request{session,window};
    HHOOK hook=SetWindowsHookExW(WH_CALLWNDPROC,BootstrapHook,nullptr,thread);
    if(!hook) return HRESULT_FROM_WIN32(GetLastError());
    pendingBootstrap=&request;
    SendMessageW(window,BootstrapMessage(),0,0);
    pendingBootstrap=nullptr; UnhookWindowsHookEx(hook);
    return request.result;
}
struct Site : implements<Site,IObjectWithSite> {
    com_ptr<IUnknown> site;
    HRESULT __stdcall SetSite(IUnknown* value) noexcept override {
        try {
            site.copy_from(value); if(!value) return S_OK;
            auto diagnostics=site.as<IXamlDiagnostics>();
            BSTR name{}; check_hresult(diagnostics->GetInitializationData(&name));
            auto session=std::make_shared<Session>();
            session->mapping=OpenFileMappingW(FILE_MAP_ALL_ACCESS,FALSE,name); SysFreeString(name);
            if(!session->mapping) return HRESULT_FROM_WIN32(GetLastError());
            session->data=static_cast<Shared*>(MapViewOfFile(session->mapping,FILE_MAP_ALL_ACCESS,0,0,sizeof(Shared)));
            if(!session->data || session->data->version!=1) return E_INVALIDARG;
            session->owner=OpenProcess(SYNCHRONIZE,FALSE,session->data->owner);
            if(!session->owner) return HRESULT_FROM_WIN32(GetLastError());
            auto raw=new std::shared_ptr<Session>(session);
            HANDLE worker=CreateThread(nullptr,0,[](void* param)->DWORD {
                std::unique_ptr<std::shared_ptr<Session>> holder(static_cast<std::shared_ptr<Session>*>(param));
                auto session=*holder;
                try {
                    init_apartment(apartment_type::multi_threaded);
                    check_hresult(Bootstrap(session));
                } catch(...) { session->data->error=to_hresult(); }
                return 0;
            },raw,0,nullptr);
            if(!worker) { delete raw; return HRESULT_FROM_WIN32(GetLastError()); }
            CloseHandle(worker); return S_OK;
        } catch(...) { return to_hresult(); }
    }
    HRESULT __stdcall GetSite(REFIID id,void** result) noexcept override { return site?site->QueryInterface(id,result):E_FAIL; }
};
struct Factory : implements<Factory,IClassFactory> {
    HRESULT __stdcall CreateInstance(IUnknown* outer,REFIID id,void** result) noexcept override {
        if(outer) return CLASS_E_NOAGGREGATION;
        try { return make<Site>().as<IUnknown>()->QueryInterface(id,result); } catch(...) { return to_hresult(); }
    }
    HRESULT __stdcall LockServer(BOOL) noexcept override { return S_OK; }
};
extern "C" HRESULT __stdcall DllGetClassObject(REFCLSID clsid,REFIID iid,void** result) {
    *result=nullptr; if(clsid!=ClassId) return CLASS_E_CLASSNOTAVAILABLE;
    try { return make<Factory>().as<IUnknown>()->QueryInterface(iid,result); } catch(...) { return to_hresult(); }
}
extern "C" HRESULT __stdcall DllCanUnloadNow() { return S_FALSE; }
extern "C" __declspec(dllexport) LONG __stdcall IdentifyGlyph(wchar_t glyph) noexcept {
    try { return Context::Glyph(hstring(std::wstring(1,glyph))); } catch(...) { return 0; }
}
HRESULT ResolveSymbols(Shared* shared) {
    wchar_t system[MAX_PATH]; GetSystemDirectoryW(system,MAX_PATH);
    std::wstring path=std::wstring(system)+L"\\taskbar.dll";
    wchar_t local[32768]; if(!GetEnvironmentVariableW(L"LOCALAPPDATA",local,32768)) return E_FAIL;
    std::wstring search=L"srv*"+std::wstring(local)+L"\\TrayPilot\\symbols*https://msdl.microsoft.com/download/symbols";
    HANDLE process=GetCurrentProcess();
    SymSetOptions(SYMOPT_DEFERRED_LOADS|SYMOPT_FAIL_CRITICAL_ERRORS|SYMOPT_EXACT_SYMBOLS);
    if(!SymInitializeW(process,search.c_str(),FALSE)) return HRESULT_FROM_WIN32(GetLastError());
    auto base=SymLoadModuleExW(process,nullptr,path.c_str(),L"TrayPilotTaskbar",0,0,nullptr,0);
    IMAGEHLP_MODULEW64 info{sizeof(info)};
    if(base && SymGetModuleInfoW64(process,base,&info)) { shared->timestamp=info.TimeDateStamp; shared->imageSize=info.ImageSize; }
    struct Search { ULONG64 base; Shared* data; } query{base,shared};
    BOOL success=base && SymEnumSymbolsW(process,base,L"*",[](PSYMBOL_INFOW symbol,ULONG,void* parameter)->BOOL {
        auto q=static_cast<Search*>(parameter);
        std::wstring_view name(symbol->Name,symbol->NameLen);
        const wchar_t* identifiers[]={L"??_7CTaskBand@@6BITaskListWndSite@@",L"?GetTaskbarHost@CTaskBand@@",L"?FrameHeight@TaskbarHost@@",L"?_Decref@_Ref_count_base@std@@"};
        for(int i=0;i<4;i++) if(name.starts_with(identifiers[i])) q->data->symbols[i]=symbol->Address-q->base;
        return TRUE;
    },&query);
    DWORD error=GetLastError(); SymCleanup(process);
    if(!success) return HRESULT_FROM_WIN32(error?error:ERROR_NOT_FOUND);
    for(auto offset:shared->symbols) if(!offset) return HRESULT_FROM_WIN32(ERROR_NOT_FOUND);
    return S_OK;
}
extern "C" __declspec(dllexport) HRESULT __stdcall Attach(DWORD pid,PCWSTR mapping) noexcept {
    HANDLE map=OpenFileMappingW(FILE_MAP_ALL_ACCESS,FALSE,mapping);
    if(!map) return HRESULT_FROM_WIN32(GetLastError());
    auto data=static_cast<Shared*>(MapViewOfFile(map,FILE_MAP_ALL_ACCESS,0,0,sizeof(Shared)));
    HRESULT resolved=data?ResolveSymbols(data):E_FAIL;
    if(data) UnmapViewOfFile(data); CloseHandle(map);
    if(FAILED(resolved)) return resolved;
    wchar_t path[32768]; if(!GetModuleFileNameW(module,path,32768)) return HRESULT_FROM_WIN32(GetLastError());
    HMODULE xaml=LoadLibraryExW(L"Windows.UI.Xaml.dll",nullptr,LOAD_LIBRARY_SEARCH_SYSTEM32);
    if(!xaml) return HRESULT_FROM_WIN32(GetLastError());
    auto initialize=reinterpret_cast<decltype(&InitializeXamlDiagnosticsEx)>(GetProcAddress(xaml,"InitializeXamlDiagnosticsEx"));
    if(!initialize) { FreeLibrary(xaml); return E_NOTIMPL; }
    HRESULT result=E_FAIL;
    for(int i=1;i<=64;i++) {
        wchar_t endpoint[64]; swprintf_s(endpoint,L"VisualDiagConnection%d",i);
        result=initialize(endpoint,pid,nullptr,path,ClassId,mapping);
        if(SUCCEEDED(result) || result!=HRESULT_FROM_WIN32(ERROR_NOT_FOUND)) break;
    }
    FreeLibrary(xaml); return result;
}
BOOL WINAPI DllMain(HINSTANCE instance,DWORD reason,void*) { if(reason==DLL_PROCESS_ATTACH) module=instance; return TRUE; }
