// Read-only native control probe, independent of .NET callback marshalling.
struct NativeNotifyItem {
    PWSTR executable, tooltip;
    HICON icon;
    HWND window;
    DWORD preference;
    UINT id;
    GUID guid;
};
struct __declspec(uuid("D782CCBA-AFB0-43F1-94DB-FDA3779EACCB")) NativeNotifyCallback : IUnknown {
    virtual HRESULT STDMETHODCALLTYPE Notify(ULONG,NativeNotifyItem*)=0;
};
struct __declspec(uuid("D133CE13-3537-48BA-93A7-AFCD5D2053B4")) NativeTrayNotify : IUnknown {
    virtual HRESULT STDMETHODCALLTYPE RegisterCallback(NativeNotifyCallback*,ULONG*)=0;
    virtual HRESULT STDMETHODCALLTYPE UnregisterCallback(ULONG*)=0;
};
struct NativeNotifyObserver : implements<NativeNotifyObserver,NativeNotifyCallback> {
    LONG callbacks{}, images{};
    HRESULT STDMETHODCALLTYPE Notify(ULONG,NativeNotifyItem* item) noexcept override {
        callbacks++;
        if(item && item->icon) if(auto copy=CopyIcon(item->icon)) { images++; DestroyIcon(copy); }
        return S_OK;
    }
};
extern "C" __declspec(dllexport) HRESULT __stdcall ProbeApplicationNotify(ULONG duration,LONG* result) noexcept {
    if(!result || duration>10000) return E_INVALIDARG;
    ZeroMemory(result,5*sizeof(LONG));
    try {
        com_ptr<NativeTrayNotify> notify;
        GUID clsid{0x25DEAD04,0x1EAC,0x4911,{0x9E,0x3A,0xAD,0x0A,0x4A,0xB5,0x60,0xFD}};
        check_hresult(CoCreateInstance(clsid,nullptr,CLSCTX_LOCAL_SERVER,__uuidof(NativeTrayNotify),notify.put_void()));
        auto observer=make_self<NativeNotifyObserver>();
        ULONG cookie{};
        HRESULT registered=notify->RegisterCallback(observer.get(),&cookie);
        result[2]=registered;
        result[4]=cookie;
        if(FAILED(registered)) return registered;
        ULONGLONG until=GetTickCount64()+duration;
        while(GetTickCount64()<until) {
            MSG message;
            while(PeekMessageW(&message,nullptr,0,0,PM_REMOVE)) {
                TranslateMessage(&message); DispatchMessageW(&message);
            }
            MsgWaitForMultipleObjects(0,nullptr,FALSE,50,QS_ALLINPUT);
        }
        result[0]=observer->callbacks;
        result[1]=observer->images;
        result[3]=notify->UnregisterCallback(&cookie);
        return S_OK;
    } catch(...) { return to_hresult(); }
}

// Resolve only. Never invoke these private methods or dereference Explorer data
// until the model's interfaces and identity layout have been separately verified.
extern "C" __declspec(dllexport) HRESULT __stdcall ProbeApplicationSymbols(PCWSTR directory,ULONG64* result) noexcept {
    if(!directory || !result) return E_INVALIDARG;
    std::lock_guard lock(symbolsMutex);
    ZeroMemory(result,8*sizeof(ULONG64));
    wchar_t system[MAX_PATH];
    if(!GetSystemDirectoryW(system,MAX_PATH)) return HRESULT_FROM_WIN32(GetLastError());
    std::wstring path=std::wstring(system)+L"\\taskbar.dll";
    HANDLE process=GetCurrentProcess();
    SymSetOptions(SYMOPT_DEFERRED_LOADS|SYMOPT_FAIL_CRITICAL_ERRORS|SYMOPT_EXACT_SYMBOLS);
    if(!SymInitializeW(process,directory,FALSE)) return HRESULT_FROM_WIN32(GetLastError());
    auto base=SymLoadModuleExW(process,nullptr,path.c_str(),L"TrayPilotApplicationSymbols",0,0,nullptr,0);
    IMAGEHLP_MODULEW64 info{sizeof(info)};
    if(base && SymGetModuleInfoW64(process,base,&info)) { result[0]=info.TimeDateStamp; result[1]=info.ImageSize; }
    struct Query { ULONG64 base; ULONG64* result; } query{base,result};
    BOOL success=base && SymEnumSymbolsW(process,base,L"*",[](PSYMBOL_INFOW symbol,ULONG,void* context)->BOOL {
        auto query=static_cast<Query*>(context);
        std::wstring_view name(symbol->Name,symbol->NameLen);
        PCWSTR prefixes[]={
            L"?ShellNotifyIcon@NotificationAreaIconManager2@@",
            L"?VisiblePromotedIcons@NotificationAreaIconManager2@@",
            L"?VisibleOverflowIcons@NotificationAreaIconManager2@@",
            L"?Identity@NotificationAreaIcon2@implementation@Shell@UI@WindowsUdk@winrt@@",
            L"?get_Icon@?$produce@UNotificationAreaIcon2@implementation@Shell@UI@WindowsUdk@winrt@@UINotificationAreaIcon@3456@@",
            L"??_7?$produce@UNotificationAreaIcon2@implementation@Shell@UI@WindowsUdk@winrt@@UINotificationAreaIcon@3456@@"
        };
        for(int i=0;i<6;i++) if(name.starts_with(prefixes[i])) query->result[i+2]=symbol->Address-query->base;
        return TRUE;
    },&query);
    DWORD error=GetLastError();
    SymCleanup(process);
    return success?S_OK:HRESULT_FROM_WIN32(error?error:ERROR_NOT_FOUND);
}

extern "C" __declspec(dllexport) HRESULT __stdcall ProbeSystemTraySymbols(PCWSTR image,PCWSTR directory,ULONG64* result) noexcept {
    if(!image || !directory || !result) return E_INVALIDARG;
    std::lock_guard lock(symbolsMutex);
    ZeroMemory(result,12*sizeof(ULONG64));
    HANDLE process=GetCurrentProcess();
    SymSetOptions(SYMOPT_DEFERRED_LOADS|SYMOPT_FAIL_CRITICAL_ERRORS|SYMOPT_EXACT_SYMBOLS);
    if(!SymInitializeW(process,directory,FALSE)) return HRESULT_FROM_WIN32(GetLastError());
    auto base=SymLoadModuleExW(process,nullptr,image,L"TrayPilotSystemTraySymbols",0,0,nullptr,0);
    IMAGEHLP_MODULEW64 info{sizeof(info)};
    if(base && SymGetModuleInfoW64(process,base,&info)) { result[0]=info.TimeDateStamp; result[1]=info.ImageSize; }
    struct Query { ULONG64 base; ULONG64* result; } query{base,result};
    BOOL success=base && SymEnumSymbolsW(process,base,L"*",[](PSYMBOL_INFOW symbol,ULONG,void* context)->BOOL {
        auto query=static_cast<Query*>(context);
        std::wstring_view name(symbol->Name,symbol->NameLen);
        PCWSTR prefixes[]={
            L"?get_ContentVM@?$produce@UIconViewModel@implementation@SystemTray@winrt@@UIIconViewModel@34@@",
            L"??_7?$produce@UIconViewModel@implementation@SystemTray@winrt@@UIIconViewModel@34@@",
            L"?get_ContentMetadata@?$produce@UImageIconContentViewModel@implementation@SystemTray@winrt@@UIIconContentViewModel@34@@",
            L"??_7?$produce@UImageIconContentViewModel@implementation@SystemTray@winrt@@UIIconContentViewModel@34@@",
            L"?OnDataModelChanged@ImageIconContentViewModel@implementation@SystemTray@winrt@@",
            L"?get_UDKObject@?$produce@UNotificationAreaIconsDataModel@implementation@SystemTray@winrt@@UINotificationAreaIconsDataModel@34@@",
            L"??_7?$produce@UNotificationAreaIconsDataModel@implementation@SystemTray@winrt@@UINotificationAreaIconsDataModel@34@@",
            L"?get_IconImageSource@?$produce@UImageIconContentViewModel@implementation@SystemTray@winrt@@UIImageIconContentViewModel@34@@",
            L"??_7?$produce@UImageIconContentViewModel@implementation@SystemTray@winrt@@UIImageIconContentViewModel@34@@",
            L"??0ImageIconContentViewModel@implementation@SystemTray@winrt@@QEAA@AEBUIIconDataModel@23@@"
        };
        for(int i=0;i<10;i++) if(name.starts_with(prefixes[i])) query->result[i+2]=symbol->Address-query->base;
        return TRUE;
    },&query);
    DWORD error=GetLastError();
    SymCleanup(process);
    return success?S_OK:HRESULT_FROM_WIN32(error?error:ERROR_NOT_FOUND);
}
