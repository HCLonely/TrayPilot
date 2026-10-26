// SPDX-License-Identifier: GPL-3.0-only
// Taskbar host discovery adapted from m417z's Taskbar tray system icon tweaks.
// See README.md and LICENSE for attribution and source details.
#define NOMINMAX
#include <windows.h>
#include <dbghelp.h>
#include <ocidl.h>
#include <xamlom.h>
#undef GetCurrentTime
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.UI.Xaml.h>
#include <winrt/Windows.UI.Xaml.Controls.h>
#include <winrt/Windows.UI.Xaml.Media.h>
#include <winrt/Windows.UI.Xaml.Media.Imaging.h>
#include <winrt/Windows.Storage.Streams.h>
#include <cmath>
#include <atomic>
#include <algorithm>
#include <memory>
#include <mutex>
#include <vector>
#include <unordered_map>

using namespace winrt;
using namespace Windows::UI::Xaml;
using namespace Windows::UI::Xaml::Controls;
std::mutex symbolsMutex;
#include "NotifyImageProbe.h"
constexpr GUID ClassId{0x56c9cbd1,0x7e71,0x4929,{0x93,0x15,0x0c,0xaa,0x4d,0x52,0xb7,0x61}};
HMODULE module;
struct IconAppearance { wchar_t text[16], font[64]; };
constexpr int IconMaxWidth=192, IconMaxHeight=64;
struct IconPixels {
    volatile LONG sequence;
    LONG width, height, error;
    ULONGLONG timestamp;
    BYTE pixels[IconMaxWidth*IconMaxHeight*4];
};
static_assert(sizeof(IconPixels)==49176);
struct Shared {
    LONG version, owner;
    // requested packs hide bits in 0..11 and forced-visible language bits in 16..27.
    volatile LONG requested, stop, found, hidden, ticks, error, alive, contexts, requestId, responseId;
    ULONG timestamp, imageSize;
    LONG reserved[2];
    ULONG64 symbols[4];
    IconAppearance icons[12];
    volatile LONG appearanceRequested;
    volatile LONG restoreRequested;
    IconPixels images[12];
    volatile LONG languageTraceSequence;
    wchar_t languageTrace[16384];
    ULONG64 languageSourceSymbols[14];
    LONG languageSourceStatus;
};
static_assert(offsetof(Shared, appearanceRequested)==2016);
static_assert(offsetof(Shared, restoreRequested)==2020);
static_assert(offsetof(Shared, images)==2024);
static_assert(offsetof(Shared,languageTraceSequence)==592136);
static_assert(offsetof(Shared,languageSourceSymbols)==624912);
static_assert(offsetof(Shared,languageSourceStatus)==625024);
static_assert(sizeof(Shared)==625032);
#include "ApplicationModelImages.h"
struct Session {
    HANDLE mapping{}, owner{};
    Shared* data{};
    std::unique_ptr<ApplicationImageState> application;
    ~Session() { if(data) UnmapViewOfFile(data); if(mapping) CloseHandle(mapping); if(owner) CloseHandle(owner); }
    bool stopped() { return data->stop || WaitForSingleObject(owner,0) != WAIT_TIMEOUT; }
};
#include "IconImages.h"
#include "LanguageSource.h"
struct Original {
    weak_ref<FrameworkElement> element;
    Windows::Foundation::IInspectable local{nullptr};
    bool touched=true;
    int64_t visibilityToken{};
    LONG visibilityKind{};
    bool forceOnly{};
};
// No active timer or owner handle is retained after an intentional no-restore exit.
// Keep only weak element identities and their original values for a later session.
thread_local std::unordered_map<HWND,std::vector<Original>> preservedOriginals;
FrameworkElement TaskbarRoot(HWND window,Shared* data);
struct Context : std::enable_shared_from_this<Context> {
    std::shared_ptr<Session> session;
    weak_ref<FrameworkElement> root;
    weak_ref<FrameworkElement> layoutRoot;
    event_token layoutToken{};
    bool watchingLayout{};
    std::vector<Original> originals;
    UINT_PTR timer{};
    bool updating{};
    bool sharedMicrophoneLocation{};
    HWND window{};
    IconAppearance icons[12]{};
    std::shared_ptr<IconImageRequest> imageRequests[12];
    std::wstring lastLanguageState;
    std::vector<std::wstring> languageTransitions;
    static constexpr LONG LanguageMask=256|512;
    static void Unwatch(Original& original) noexcept {
        if(!original.visibilityToken) return;
        try {
            if(auto element=original.element.get())
                element.UnregisterPropertyChangedCallback(UIElement::VisibilityProperty(),original.visibilityToken);
        } catch(...) { /* Weak callbacks are also safe if XAML is already shutting down. */ }
        original.visibilityToken=0;
        original.visibilityKind=0;
    }
    void UnwatchLayout() noexcept {
        if(!watchingLayout) return;
        try {
            if(auto frame=layoutRoot.get()) frame.LayoutUpdated(layoutToken);
        } catch(...) { /* The delegate holds only a weak Context reference. */ }
        watchingLayout=false;
        layoutRoot={};
    }
    ~Context() {
        UnwatchLayout();
        for(auto& original:originals) Unwatch(original);
    }
    void WatchLayout(FrameworkElement const& frame) {
        if(watchingLayout && layoutRoot.get()==frame) return;
        UnwatchLayout();
        if(!frame || session->application) return;
        auto weak=weak_from_this();
        layoutToken=frame.LayoutUpdated([weak](auto const&,auto const&) noexcept {
            if(auto context=weak.lock()) context->RefreshLanguageVisibility();
        });
        layoutRoot=make_weak(frame);
        watchingLayout=true;
    }
    void RefreshLanguageVisibility() noexcept {
        if(updating || session->application || session->stopped()) return;
        LONG request=session->data->requested;
        if(!((request|(request>>16))&LanguageMask)) return;
        updating=true;
        try {
            if(auto frame=root.get()) {
                LONG found=0,visible=0;
                Walk(frame,request,0,found,visible,0,0,true);
            }
        } catch(...) { /* The timer retries discovery and reports persistent failures. */ }
        updating=false;
    }
    void ReadImage(FrameworkElement const& element, int index) {
        auto& request=imageRequests[index];
        if(!request) request=std::make_shared<IconImageRequest>();
        if(request->busy || GetTickCount64()-request->attempted<1000) return;
        request->busy=true;
        request->attempted=GetTickCount64();
        RenderIconImage(session,request,element,index);
    }
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
    void Set(FrameworkElement const& element, bool hide, LONG kind=0, bool forceShow=false, bool forceOnly=false) {
        auto it = std::find_if(originals.begin(), originals.end(), [&](auto const& o) { return o.element.get() == element; });
        if(hide || forceShow) {
            if(it == originals.end()) {
                originals.push_back({make_weak(element), element.ReadLocalValue(UIElement::VisibilityProperty())});
                it=std::prev(originals.end());
            }
            it->touched=true;
            if(it->visibilityKind!=kind || it->forceOnly!=forceOnly) Unwatch(*it);
            if((kind&LanguageMask) && !it->visibilityToken) {
                auto weak=weak_from_this();
                it->visibilityToken=element.RegisterPropertyChangedCallback(UIElement::VisibilityProperty(),
                    [weak,kind,forceOnly](DependencyObject const& sender,DependencyProperty const&) noexcept {
                        try {
                            auto context=weak.lock();
                            if(!context || context->session->stopped()) return;
                            LONG request=context->session->data->requested;
                            Visibility target;
                            if(!forceOnly && (request&kind)==kind) target=Visibility::Collapsed;
                            else if(forceOnly ? ((request>>16)&kind)!=0 : ((request>>16)&kind)==kind) target=Visibility::Visible;
                            else return;
                            auto control=sender.try_as<UIElement>();
                            // The nested notification exits here instead of recursing.
                            if(control && control.Visibility()!=target) control.Visibility(target);
                        } catch(...) { /* Keep Explorer's property notification boundary noexcept. */ }
                    });
                it->visibilityKind=kind;
                it->forceOnly=forceOnly;
            }
            auto target=hide ? Visibility::Collapsed : Visibility::Visible;
            if(element.Visibility()!=target) element.Visibility(target);
        } else if(it != originals.end()) {
            Unwatch(*it);
            if(it->local == DependencyProperty::UnsetValue()) element.ClearValue(UIElement::VisibilityProperty());
            else element.SetValue(UIElement::VisibilityProperty(), it->local);
            originals.erase(it);
        }
    }
    void Restore() {
        for(auto& original : originals) {
            Unwatch(original);
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
    bool HasTextContent(DependencyObject const& node,int depth=0) {
        if(depth>12) return false;
        if(auto text=node.try_as<TextBlock>()) return !text.Text().empty();
        int count=Media::VisualTreeHelper::GetChildrenCount(node);
        for(int i=0;i<count;i++)
            if(HasTextContent(Media::VisualTreeHelper::GetChild(node,i),depth+1)) return true;
        return false;
    }
    LONG KnownLanguageKind(FrameworkElement const& element) {
        for(auto const& original:originals)
            if(!original.forceOnly && (original.visibilityKind==256 || original.visibilityKind==512) && original.element.get()==element)
                return original.visibilityKind;
        return 0;
    }
    bool GuardLanguageContent(DependencyObject const& node,LONG kind,int depth=0) {
        if(depth>12) return false;
        auto type=get_class_name(node);
        bool content=type==L"SystemTray.LanguageTextIconContent" || type==L"SystemTray.LanguageImageIconContent" ||
            type==L"SystemTray.ImageIconContent";
        if(auto text=node.try_as<TextBlock>()) content=!text.Text().empty();
        int count=Media::VisualTreeHelper::GetChildrenCount(node);
        for(int i=0;i<count;i++)
            content=GuardLanguageContent(Media::VisualTreeHelper::GetChild(node,i),kind,depth+1) || content;
        // Only the path to actual content is protected; dormant template layers stay untouched.
        if(content) if(auto element=node.try_as<FrameworkElement>()) Set(element,false,kind,true,true);
        return content;
    }
    void GuardLanguageAncestors(FrameworkElement const& element,LONG forced) {
        auto parent=Media::VisualTreeHelper::GetParent(element);
        for(int depth=0;parent && depth<24;depth++) {
            auto ancestor=parent.try_as<FrameworkElement>();
            if(!ancestor) break;
            // Never override the tray/taskbar root or Windows taskbar auto-hide.
            if(get_class_name(ancestor)==L"SystemTray.SystemTrayFrame" || ancestor==root.get()) break;
            Set(ancestor,false,forced,true,true);
            if(ancestor.Name()==L"NonActivatableStack") break;
            parent=Media::VisualTreeHelper::GetParent(ancestor);
        }
    }
    void DescribeLanguageTree(DependencyObject const& node,std::wstring& result,int& remaining,int depth=0,bool languageRegion=false) {
        if(depth>24 || remaining<=0) return;
        auto element=node.try_as<FrameworkElement>();
        if(!element) return;
        auto name=element.Name();
        languageRegion=languageRegion || name==L"NonActivatableStack";
        int count=Media::VisualTreeHelper::GetChildrenCount(node);
        if(languageRegion) {
            remaining--;
            result+=L"\n"+std::wstring(std::min(depth,12),L' ')+std::wstring(get_class_name(node).c_str()).substr(0,48)+
                L"#"+std::wstring(name.c_str()).substr(0,32)+L" vis="+
                (element.Visibility()==Visibility::Visible ? L"V" : L"C")+L" children="+std::to_wstring(count);
            if(name==L"SystemTrayIcon") result+=L" kind="+std::to_wstring(ContentKind(element));
            if(auto text=node.try_as<TextBlock>()) result+=L" textPresent="+std::to_wstring(!text.Text().empty());
        }
        for(int i=0;i<count && remaining>0;i++)
            DescribeLanguageTree(Media::VisualTreeHelper::GetChild(node,i),result,remaining,depth+1,languageRegion);
    }
    void RecordLanguageState(FrameworkElement const& frame,LONG request,LONG found) noexcept {
        try {
            if(!((request|(request>>16))&LanguageMask)) return;
            wchar_t foregroundClass[64]{};
            GetClassNameW(GetForegroundWindow(),foregroundClass,64);
            std::wstring state=L"hide="+std::to_wstring(request&LanguageMask)+L" force="+
                std::to_wstring((request>>16)&LanguageMask)+L" found="+std::to_wstring(found&LanguageMask)+
                L" foregroundClass="+foregroundClass+L" modelReads="+std::to_wstring(languageDataReads.load())+
                L" retained="+std::to_wstring(languageDataRetained.load())+L" unknown="+std::to_wstring(languageDataUnknown.load())+
                L" primed="+std::to_wstring(languageDataPrimed.load());
            for(auto const& original:originals) {
                if(original.forceOnly || !(original.visibilityKind&LanguageMask)) continue;
                auto element=original.element.get();
                bool attached=false;
                if(element) {
                    DependencyObject parent=element;
                    for(int depth=0;parent && depth<32;depth++) {
                        if(parent==frame) { attached=true; break; }
                        parent=Media::VisualTreeHelper::GetParent(parent);
                    }
                }
                state+=L"\nhost kind="+std::to_wstring(original.visibilityKind)+L" alive="+std::to_wstring(bool(element))+
                    L" attached="+std::to_wstring(attached);
            }
            int remaining=24;
            DescribeLanguageTree(frame,state,remaining);
            if(state.size()>1800) state.resize(1800);
            if(state==lastLanguageState) return;
            lastLanguageState=state;
            if(languageTransitions.size()>=8) languageTransitions.erase(languageTransitions.begin());
            languageTransitions.push_back(L"[tick="+std::to_wstring(GetTickCount64())+L"] "+state);
            std::wstring trace;
            for(auto const& transition:languageTransitions) trace+=transition+L"\n\n";
            auto data=session->data;
            InterlockedIncrement(&data->languageTraceSequence);
            wcsncpy_s(data->languageTrace,trace.c_str(),_TRUNCATE);
            InterlockedIncrement(&data->languageTraceSequence);
        } catch(...) { /* Diagnostics must never interfere with Explorer visibility control. */ }
    }
    void Walk(DependencyObject const& node, LONG mask, LONG restoreMask, LONG& found, LONG& visible, int depth=0, int region=0, bool languageOnly=false) {
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
        if(region==3 && name==L"SystemTrayIcon") {
            LONG contentKind=ContentKind(element);
            kind=contentKind&LanguageMask;
            if(!contentKind) {
                LONG known=KnownLanguageKind(element);
                if(known && ((mask>>16)&known)) {
                    if(HasTextContent(element)) kind=known;
                    else {
                        // Keep a known empty host available for XAML to repopulate.
                        // It remains absent in Found until real content returns.
                        Set(element,false,known,true);
                        GuardLanguageAncestors(element,(mask>>16)&LanguageMask);
                        return;
                    }
                }
            }
        }
        if(region==4 && type==L"SystemTray.TextIconContent") kind=1024;
        if(name==L"ShowDesktopStack") kind=2048;
        if(kind) {
            if(kind&LanguageMask) PrimeLanguageSource(element);
            if(languageOnly && !(kind&LanguageMask)) return;
            found|=kind;
            IconAppearance appearance{};
            try { if(!languageOnly && session->data->appearanceRequested) ReadAppearance(element,kind,appearance); } catch(...) { /* Appearance is optional. */ }
            for(int i=0;i<12;i++) if(!languageOnly && (kind&(1<<i))) {
                icons[i]=appearance;
                try { if(session->data->appearanceRequested) ReadImage(element,i); } catch(...) { /* Image fallback is independent of visibility. */ }
            }
            if(kind==(16|32)) sharedMicrophoneLocation=true;
            // A combined privacy indicator must remain unless both are requested.
            Set(element,(mask&kind)==kind,kind,(kind&LanguageMask) && ((mask>>16)&kind)==kind);
            if((kind&LanguageMask) && ((mask>>16)&kind)==kind) {
                int children=Media::VisualTreeHelper::GetChildrenCount(element);
                for(int i=0;i<children;i++) GuardLanguageContent(Media::VisualTreeHelper::GetChild(element,i),kind);
                GuardLanguageAncestors(element,(mask>>16)&LanguageMask);
            }
            // An explicit Show must also recover controls hidden before this DLL
            // instance was loaded (for example, by an earlier application version).
            // Passive scans and session cleanup still preserve the original state.
            if((restoreMask&kind) && (mask&kind)!=kind && element.Visibility()==Visibility::Collapsed)
                element.Visibility(Visibility::Visible);
            if(element.Visibility()!=Visibility::Collapsed) visible|=kind;
            return;
        }
        int count=Media::VisualTreeHelper::GetChildrenCount(node);
        for(int i=0;i<count;i++) Walk(Media::VisualTreeHelper::GetChild(node,i),mask,restoreMask,found,visible,depth+1,region,languageOnly);
    }
    void Tick() noexcept {
        if(updating) return;
        updating=true;
        try {
            auto data=session->data;
            if(session->stopped()) {
                ReleaseLanguageSource(session);
                UnwatchLayout();
                if(data->stop==2 && !session->application) {
                    for(auto& original:originals) Unwatch(original);
                    preservedOriginals[window]=std::move(originals);
                }
                else Restore();
                KillTimer(nullptr,timer); timer=0;
                InterlockedDecrement(&data->contexts);
                if(data->contexts==0) data->alive=2;
            } else {
                auto frame=root.get();
                if(!frame || !frame.XamlRoot()) {
                    Restore(); frame=TaskbarRoot(window,data); root=make_weak(frame);
                }
                if(!frame) { data->found=0; data->hidden=0; throw hresult_error(E_NOTIMPL); }
                WatchLayout(frame);
                if(session->application) {
                    session->application->Read(frame);
                    InterlockedIncrement(&data->ticks); data->alive=1;
                    updating=false;
                    return;
                }
                LONG request=data->requestId, mask=data->requested, restoreMask=data->restoreRequested, found=0, visible=0;
                for(auto& original:originals) original.touched=false;
                sharedMicrophoneLocation=false;
                ZeroMemory(icons,sizeof(icons));
                Walk(frame,mask,restoreMask,found,visible);
                RecordLanguageState(frame,mask,found);
                for(int i=0;i<12;i++) if(!(found&(1<<i)) && data->images[i].width)
                    PublishIconImage(data->images[i],0,0,E_BOUNDS,nullptr);
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
thread_local std::vector<std::shared_ptr<Context>> contexts;
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
                auto context=std::make_shared<Context>();
                context->session=request->session; context->root=make_weak(frame); context->window=request->window;
                if(!request->session->application)
                    request->session->data->languageSourceStatus=InstallLanguageSource(request->session);
                for(auto& previous:contexts)
                    if(previous->window==request->window && previous->session->stopped()) previous->Tick();
                std::erase_if(contexts,[](auto const& previous){return previous->timer==0;});
                if(auto saved=preservedOriginals.find(request->window); !request->session->application && saved!=preservedOriginals.end()) {
                    context->originals=std::move(saved->second); preservedOriginals.erase(saved);
                }
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
            session->mapping=OpenFileMappingW(FILE_MAP_ALL_ACCESS,FALSE,name);
            try { session->application=ApplicationImageState::Open(name); }
            catch(...) { SysFreeString(name); throw; }
            SysFreeString(name);
            if(!session->mapping) return HRESULT_FROM_WIN32(GetLastError());
            session->data=static_cast<Shared*>(MapViewOfFile(session->mapping,FILE_MAP_ALL_ACCESS,0,0,sizeof(Shared)));
            if(!session->data || session->data->version!=4) return E_INVALIDARG;
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
HRESULT ResolveSymbols(Shared* shared,PCWSTR symbolDirectory=nullptr) {
    std::lock_guard lock(symbolsMutex); // DbgHelp is single-threaded.
    wchar_t system[MAX_PATH]; GetSystemDirectoryW(system,MAX_PATH);
    std::wstring path=std::wstring(system)+L"\\taskbar.dll";
    wchar_t local[32768]; if(!GetEnvironmentVariableW(L"LOCALAPPDATA",local,32768)) return E_FAIL;
    std::wstring search=L"srv*"+std::wstring(local)+L"\\TrayPilot\\symbols*https://msdl.microsoft.com/download/symbols";
    if(symbolDirectory) search=symbolDirectory;
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
HRESULT ResolveLanguageSource(Shared* shared,PCWSTR symbolDirectory) {
    if(!symbolDirectory) return E_NOTIMPL;
    std::lock_guard lock(symbolsMutex);
    wchar_t windows[MAX_PATH]{};
    GetWindowsDirectoryW(windows,MAX_PATH);
    std::wstring path=std::wstring(windows)+L"\\SystemApps\\MicrosoftWindows.Client.Core_cw5n1h2txyewy\\SystemTray.dll";
    HANDLE process=GetCurrentProcess();
    SymSetOptions(SYMOPT_DEFERRED_LOADS|SYMOPT_FAIL_CRITICAL_ERRORS|SYMOPT_EXACT_SYMBOLS);
    if(!SymInitializeW(process,symbolDirectory,FALSE)) return HRESULT_FROM_WIN32(GetLastError());
    auto base=SymLoadModuleExW(process,nullptr,path.c_str(),L"TrayPilotLanguageSource",0,0,nullptr,0);
    IMAGEHLP_MODULEW64 info{sizeof(info)};
    if(base && SymGetModuleInfoW64(process,base,&info)) {
        shared->languageSourceSymbols[0]=info.TimeDateStamp;
        shared->languageSourceSymbols[1]=info.ImageSize;
    }
    struct Search { ULONG64 base; Shared* data; } query{base,shared};
    BOOL success=base && SymEnumSymbolsW(process,base,L"*",[](PSYMBOL_INFOW symbol,ULONG,void* parameter)->BOOL {
        auto q=static_cast<Search*>(parameter);
        std::wstring_view name(symbol->Name,symbol->NameLen);
        const wchar_t* identifiers[]={
            L"??_7?$produce@UImeSystemTrayIconDataModel@implementation@SystemTray@winrt@@UIIconDataModel@34@@impl@winrt@@6B@",
            L"?get_CurrentData@?$produce@UImeSystemTrayIconDataModel@implementation@SystemTray@winrt@@UIIconDataModel@34@@impl@winrt@@",
            L"??_7?$produce@ULanguageSystemTrayIconDataModel@implementation@SystemTray@winrt@@UIIconDataModel@34@@impl@winrt@@6B@",
            L"?get_CurrentData@?$produce@ULanguageSystemTrayIconDataModel@implementation@SystemTray@winrt@@UIIconDataModel@34@@impl@winrt@@",
            L"??_7?$produce@UIconData@implementation@SystemTray@winrt@@UIIconData@34@@impl@winrt@@6B@",
            L"?get_ContentMetadata@?$produce@UIconData@implementation@SystemTray@winrt@@UIIconData@34@@impl@winrt@@",
            L"??_7?$produce@UIconContentMetadata@implementation@SystemTray@winrt@@UIIconContentMetadata@34@@impl@winrt@@6B@",
            L"?get_RequestingDisplay@?$produce@UIconContentMetadata@implementation@SystemTray@winrt@@UIIconContentMetadata@34@@impl@winrt@@",
            L"??_7?$produce@UIconViewModel@implementation@SystemTray@winrt@@UIIconViewModel@34@@impl@winrt@@6B@",
            L"?get_Configuration@?$produce@UIconViewModel@implementation@SystemTray@winrt@@UIIconViewModel@34@@impl@winrt@@",
            L"??_7?$produce@UIconConfiguration@implementation@SystemTray@winrt@@UIIconConfiguration@34@@impl@winrt@@6B@",
            L"?get_DataModel@?$produce@UIconConfiguration@implementation@SystemTray@winrt@@UIIconConfiguration@34@@impl@winrt@@"
        };
        for(int i=0;i<12;i++) if(name.starts_with(identifiers[i])) q->data->languageSourceSymbols[i+2]=symbol->Address-q->base;
        return TRUE;
    },&query);
    SymCleanup(process);
    if(!success) return HRESULT_FROM_WIN32(ERROR_NOT_FOUND);
    for(auto offset:shared->languageSourceSymbols) if(!offset) return HRESULT_FROM_WIN32(ERROR_NOT_FOUND);
    return S_OK;
}
HRESULT AttachCore(DWORD pid,PCWSTR mapping,PCWSTR symbolDirectory,PCWSTR sourceSymbols=nullptr) noexcept {
    HANDLE map=OpenFileMappingW(FILE_MAP_ALL_ACCESS,FALSE,mapping);
    if(!map) return HRESULT_FROM_WIN32(GetLastError());
    auto data=static_cast<Shared*>(MapViewOfFile(map,FILE_MAP_ALL_ACCESS,0,0,sizeof(Shared)));
    HRESULT resolved=data?ResolveSymbols(data,symbolDirectory):E_FAIL;
    if(data) data->languageSourceStatus=ResolveLanguageSource(data,sourceSymbols);
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
// Keep the original symbol-server path available for existing installations.
extern "C" __declspec(dllexport) HRESULT __stdcall Attach(DWORD pid,PCWSTR mapping) noexcept {
    return AttachCore(pid,mapping,nullptr);
}
extern "C" __declspec(dllexport) HRESULT __stdcall AttachWithSymbols(DWORD pid,PCWSTR mapping,PCWSTR symbolDirectory) noexcept {
    return AttachCore(pid,mapping,symbolDirectory);
}
extern "C" __declspec(dllexport) HRESULT __stdcall AttachWithSourceSymbols(DWORD pid,PCWSTR mapping,PCWSTR symbolDirectory,PCWSTR sourceSymbols) noexcept {
    return AttachCore(pid,mapping,symbolDirectory,sourceSymbols);
}
BOOL WINAPI DllMain(HINSTANCE instance,DWORD reason,void*) { if(reason==DLL_PROCESS_ATTACH) module=instance; return TRUE; }
