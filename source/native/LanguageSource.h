// Retain the last real visible IconData while remote focus disables a language model.
// No language-bar state, active input profile, or synthetic UI is changed.
using LanguageDataGetter=HRESULT(__stdcall*)(void*,void**);
std::mutex languageSourceMutex;
std::vector<std::weak_ptr<Session>> languageSourceSessions;
std::atomic<LanguageDataGetter> languageOriginalGetters[2];
void** languageDataSlots[2]{};
void* languageIconDataTable{};
void* languageMetadataTable{};
void* languageSeedSymbols[4]{};
std::unordered_map<void*,weak_ref<Windows::Foundation::IInspectable>> languageSeededViews;
std::atomic<LONG> languageDataReads{},languageDataRetained{},languageDataUnknown{},languageDataPrimed{};
struct LanguageDataCache {
    weak_ref<Windows::Foundation::IInspectable> model;
    com_ptr<::IInspectable> data;
};
std::unordered_map<void*,LanguageDataCache> languageDataCache;

bool LanguageForceActive(LONG kind) {
    for(auto const& weak:languageSourceSessions) {
        auto session=weak.lock();
        if(session && !session->stopped() && ((session->data->requested>>16)&kind)) return true;
    }
    return false;
}
HRESULT ReadLanguageData(void* instance,void** output,int index) noexcept {
    if(!output) return E_POINTER;
    *output=nullptr;
    auto original=languageOriginalGetters[index].load();
    if(!original) return E_UNEXPECTED;
    com_ptr<::IInspectable> current;
    HRESULT result=original(instance,current.put_void());
    if(FAILED(result)) return result;
    languageDataReads.fetch_add(1);
    try {
        bool known=false,visible=false;
        if(current && *reinterpret_cast<void***>(current.get())==languageIconDataTable) {
            // IIconData.ContentMetadata.RequestingDisplay controls tray membership.
            // Both interfaces and methods are verified against the matching PDB.
            auto getter=reinterpret_cast<LanguageDataGetter>((*reinterpret_cast<void***>(current.get()))[11]);
            Windows::Foundation::IInspectable metadata{nullptr};
            if(SUCCEEDED(getter(current.get(),put_abi(metadata)))) {
                if(metadata && *reinterpret_cast<void***>(get_abi(metadata))==languageMetadataTable) {
                    using VisibilityGetter=HRESULT(__stdcall*)(void*,bool*);
                    auto visibilityGetter=reinterpret_cast<VisibilityGetter>((*reinterpret_cast<void***>(get_abi(metadata)))[6]);
                    known=SUCCEEDED(visibilityGetter(get_abi(metadata),&visible));
                }
            }
        }
        std::lock_guard lock(languageSourceMutex);
        std::erase_if(languageDataCache,[](auto const& entry) { return !entry.second.model.get(); });
        if(known && visible) {
            if(languageDataCache.size()<16 || languageDataCache.contains(instance)) {
                Windows::Foundation::IInspectable model{nullptr};
                copy_from_abi(model,instance);
                languageDataCache[instance]={make_weak(model),current};
            }
        } else if(known && LanguageForceActive(index==0 ? 512 : 256)) {
            auto saved=languageDataCache.find(instance);
            if(saved!=languageDataCache.end() && saved->second.model.get() && saved->second.data) {
                current=saved->second.data;
                languageDataRetained.fetch_add(1);
            }
        } else if(!known) languageDataUnknown.fetch_add(1);
    } catch(...) { languageDataUnknown.fetch_add(1); }
    if(current) { current->AddRef(); *output=current.get(); }
    return result;
}
HRESULT __stdcall ImeDataOverride(void* instance,void** output) noexcept { return ReadLanguageData(instance,output,0); }
HRESULT __stdcall LanguageDataOverride(void* instance,void** output) noexcept { return ReadLanguageData(instance,output,1); }

com_ptr<::IInspectable> FindLanguageInterface(Windows::Foundation::IInspectable const& object,void* table) {
    if(!object || !table) return {};
    auto inspectable=object.as<::IInspectable>();
    ULONG count{}; IID* ids{};
    check_hresult(inspectable->GetIids(&count,&ids));
    std::unique_ptr<IID,decltype(&CoTaskMemFree)> holder(ids,CoTaskMemFree);
    if(count>64) return {};
    for(ULONG i=0;i<count;i++) {
        com_ptr<::IInspectable> candidate;
        if(SUCCEEDED(inspectable->QueryInterface(ids[i],candidate.put_void())) &&
            *reinterpret_cast<void***>(candidate.get())==table) return candidate;
    }
    return {};
}
Windows::Foundation::IInspectable ReadLanguageProperty(com_ptr<::IInspectable> const& object,void* getter) {
    Windows::Foundation::IInspectable result{nullptr};
    if(!object || !getter) return result;
    auto table=*reinterpret_cast<void***>(object.get());
    // The function address comes from the exact PDB; never invoke an unknown slot.
    for(int i=6;i<20;i++) if(table[i]==getter) {
        check_hresult(reinterpret_cast<LanguageDataGetter>(getter)(object.get(),put_abi(result)));
        break;
    }
    return result;
}
void PrimeLanguageSource(FrameworkElement const& icon) noexcept {
    try {
        auto view=icon.DataContext();
        if(!view || !languageSeedSymbols[0]) return;
        {
            std::lock_guard lock(languageSourceMutex);
            std::erase_if(languageSeededViews,[](auto const& entry) { return !entry.second.get(); });
            auto saved=languageSeededViews.find(get_abi(view));
            if(saved!=languageSeededViews.end() && saved->second.get()) return;
        }
        auto vm=FindLanguageInterface(view,languageSeedSymbols[0]);
        auto configuration=ReadLanguageProperty(vm,languageSeedSymbols[1]);
        auto config=FindLanguageInterface(configuration,languageSeedSymbols[2]);
        auto model=ReadLanguageProperty(config,languageSeedSymbols[3]);
        for(int i=0;i<2;i++) {
            if(!languageDataSlots[i]) continue;
            auto modelInterface=FindLanguageInterface(model,reinterpret_cast<BYTE*>(languageDataSlots[i])-14*sizeof(void*));
            if(!modelInterface) continue;
            com_ptr<::IInspectable> current;
            check_hresult(ReadLanguageData(modelInterface.get(),current.put_void(),i));
            std::lock_guard lock(languageSourceMutex);
            if(languageDataCache.contains(modelInterface.get())) {
                languageSeededViews[get_abi(view)]=make_weak(view);
                languageDataPrimed.fetch_add(1);
            }
            break;
        }
    } catch(...) { /* Optional priming must not interfere with taskbar discovery. */ }
}

void ReleaseLanguageSource(std::shared_ptr<Session> const& session) noexcept {
    try {
        std::lock_guard lock(languageSourceMutex);
        std::erase_if(languageSourceSessions,[&](auto const& weak) {
            auto active=weak.lock();
            return !active || active==session || active->stopped();
        });
        if(!languageSourceSessions.empty()) return;
        void* hooks[]={reinterpret_cast<void*>(&ImeDataOverride),reinterpret_cast<void*>(&LanguageDataOverride)};
        for(int i=0;i<2;i++) if(languageDataSlots[i]) {
            DWORD protection{};
            if(VirtualProtect(languageDataSlots[i],sizeof(void*),PAGE_READWRITE,&protection)) {
                InterlockedCompareExchangePointer(languageDataSlots[i],reinterpret_cast<void*>(languageOriginalGetters[i].load()),hooks[i]);
                DWORD unused{};
                VirtualProtect(languageDataSlots[i],sizeof(void*),protection,&unused);
                languageDataSlots[i]=nullptr;
            }
        }
        languageDataCache.clear();
        languageSeededViews.clear();
        for(auto& symbol:languageSeedSymbols) symbol=nullptr;
    } catch(...) { }
}
HRESULT InstallLanguageSource(std::shared_ptr<Session> const& session) noexcept {
    try {
        auto symbols=session->data->languageSourceSymbols;
        auto base=reinterpret_cast<BYTE*>(GetModuleHandleW(L"SystemTray.dll"));
        auto headers=base?ImageNtHeader(base):nullptr;
        if(!headers || headers->FileHeader.TimeDateStamp!=symbols[0] || headers->OptionalHeader.SizeOfImage!=symbols[1])
            return HRESULT_FROM_WIN32(ERROR_REVISION_MISMATCH);
        for(int i=2;i<10;i++) if(!symbols[i] || symbols[i]>=symbols[1]-120) return E_NOTIMPL;
        if(reinterpret_cast<void**>(base+symbols[6])[11]!=base+symbols[7]) return E_NOTIMPL;
        if(reinterpret_cast<void**>(base+symbols[8])[6]!=base+symbols[9]) return E_NOTIMPL;
        auto ime=reinterpret_cast<void**>(base+symbols[2]);
        auto language=reinterpret_cast<void**>(base+symbols[4]);
        void** tables[]={ime,language};
        void* hooks[]={reinterpret_cast<void*>(&ImeDataOverride),reinterpret_cast<void*>(&LanguageDataOverride)};
        std::unique_lock lock(languageSourceMutex);
        for(int i=0;i<2;i++) {
            if(tables[i][14]!=hooks[i] && tables[i][14]!=base+symbols[3+i*2]) return E_NOTIMPL;
            if(languageDataSlots[i] && languageDataSlots[i]!=tables[i]+14) return E_NOTIMPL;
        }
        languageIconDataTable=base+symbols[6];
        languageMetadataTable=base+symbols[8];
        for(int i=0;i<4;i++) languageSeedSymbols[i]=symbols[10+i] && symbols[10+i]<symbols[1]-160 ? base+symbols[10+i] : nullptr;
        languageSourceSessions.push_back(session);
        for(int i=0;i<2;i++) {
            if(tables[i][14]==hooks[i]) continue;
            auto original=reinterpret_cast<LanguageDataGetter>(tables[i][14]);
            languageOriginalGetters[i].store(original);
            DWORD protection{};
            if(!VirtualProtect(tables[i]+14,sizeof(void*),PAGE_READWRITE,&protection)) {
                HRESULT error=HRESULT_FROM_WIN32(GetLastError());
                lock.unlock(); ReleaseLanguageSource(session); return error;
            }
            auto previous=InterlockedCompareExchangePointer(tables[i]+14,hooks[i],reinterpret_cast<void*>(original));
            DWORD unused{};
            VirtualProtect(tables[i]+14,sizeof(void*),protection,&unused);
            if(previous!=reinterpret_cast<void*>(original)) {
                lock.unlock(); ReleaseLanguageSource(session); return E_UNEXPECTED;
            }
            languageDataSlots[i]=tables[i]+14;
        }
        return S_OK;
    } catch(...) { return to_hresult(); }
}
