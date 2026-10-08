using Microsoft.Web.WebView2.Core;
using System.Text.Json;

namespace TrayPilot;

internal static partial class Diagnostics
{
    static async Task<string[]> CheckWebPreferencesUi(CoreWebView2 core, MainForm form)
    {
        var checks = new List<string>();
        async Task Check(string script, string label)
        { if (await core.ExecuteScriptAsync(script) != "true") throw new IOException(label + " · " + await core.ExecuteScriptAsync("JSON.stringify({page:state.page,settings:state.settings,local:settingsDraft,version:settingsVersion,error:settingsOperationError,focus:document.activeElement.id})")); checks.Add(label); }
        async Task Act(string body) => await RunWebRulesScript(core, body);
        await Act("await send('navigate',{page:'settings'})");
        await Check("state.page==='settings'&&!$('#settingsPage').hidden&&$('#iconsPage').hidden&&!state.settings.startupLoading", "Settings opens inside the existing WebView with startup state ready.");
        await Check("!preferencesDirty()&&$('#prefSave').disabled&&$('#prefCancel').disabled&&settingsDraft.hotkeys.every(h=>!h.enabled)", "Saved preferences initialize a clean draft and disabled Save/Cancel actions.");
        await Act("editPreference('closeToTray',!settingsDraft.closeToTray);await settingsSync;setSettingsTab('appearance');setSettingsTab('shortcuts')");
        await Check("preferencesDirty()&&settingsDraft.closeToTray!==settingsBaseline.closeToTray&&$('#prefPreviewClose').textContent===pt(settingsDraft.closeToTray?'background':'exit')", "Tabs retain the draft and behavior preview follows the selected preference.");
        string originalTheme = await core.ExecuteScriptAsync("state.theme");
        await Act("setSettingsTab('appearance');editPreference('theme',state.theme==='dark'?'light':'dark');await settingsSync");
        await Check("JSON.stringify(state.theme)!==" + JsonSerializer.Serialize(originalTheme), "Theme preview changes the actual web palette without saving preferences.");
        await Act("await cancelSettings()");
        await Check("!preferencesDirty()&&JSON.stringify(state.theme)===" + JsonSerializer.Serialize(originalTheme), "Cancel restores the original theme and clears the draft.");
        await Act("setSettingsTab('shortcuts');editPreferenceHotkey(0,true,131072|262144|77);editPreferenceHotkey(1,true,131072|262144|77);await settingsSync;await saveSettings()");
        await Check("preferenceInvalid().length===2&&!$('#prefErrorSummary').hidden&&document.activeElement.id==='prefErrorSummary'&&!!$('#pref-key-error-0').textContent&&!!$('#pref-key-error-1').textContent", "Duplicate enabled shortcuts show both inline errors and focus the error summary.");
        await Act("focusPreferenceError(1)"); await Check("document.activeElement.id==='pref-key-edit-1'", "Error summary links focus the invalid shortcut field.");
        await Act("await cancelSettings();openPreferenceHotkey(0);$('#prefModCtrl').checked=false;$('#prefModAlt').checked=false;$('#prefModShift').checked=true;composePreferenceHotkey();$('#prefHotkeyForm').dispatchEvent(new Event('submit',{cancelable:true}))");
        await Check("$('#prefHotkeyDialog').open&&$('#prefHotkeyError').textContent.length>0", "The shortcut editor rejects combinations without Ctrl or Alt.");
        await Act("$('#prefCapture').focus();capturePreferenceKey({key:'F12',code:'F12',ctrlKey:true,altKey:false,shiftKey:false,metaKey:false,preventDefault(){}})");
        await Check("$('#prefHotkeyError').textContent.length>0", "Reserved F12 is rejected by keyboard recording.");
        await Act("capturePreferenceKey({key:'Q',code:'KeyQ',ctrlKey:true,altKey:true,shiftKey:false,metaKey:false,preventDefault(){}});$('#prefHotkeyForm').dispatchEvent(new Event('submit',{cancelable:true}));await settingsSync");
        await Check("!$('#prefHotkeyDialog').open&&settingsDraft.hotkeys[0].keys===(131072|262144|81)&&state.settings.draft.hotkeys[0].text==='Ctrl+Alt+Q'", "Keyboard recording sends the exact Windows modifier bits and applies the combination to the draft.");
        await Act("openPreferenceHotkey(0);$('#prefMainKey').value='90';$('#prefModShift').checked=true;composePreferenceHotkey();$('#prefHotkeyForm').dispatchEvent(new Event('submit',{cancelable:true}));await settingsSync");
        await Check("settingsDraft.hotkeys[0].keys===(131072|262144|65536|90)", "Mouse selection composes modifiers and primary keys.");
        await Act("await saveSettings()"); await Check("preferencesDirty()&&settingsOperationError.length>0&&document.activeElement.id==='prefErrorSummary'", "Read-only preview rejects saving and preserves the draft with a focused error.");
        await Act("await goPage('about')"); await Check("$('#prefDiscardDialog').open&&state.page==='settings'&&document.activeElement.id==='prefStay'", "Leaving settings opens the HTML discard dialog on the safe Stay action.");
        await Act("closePreferencesDialog('prefDiscardDialog')"); await Check("preferencesDirty()&&state.page==='settings'", "Dismissing the discard dialog retains the draft and page.");
        await Act("await send('navigate',{page:'icons'})"); await Task.Delay(100);
        await Check("$('#prefDiscardDialog').open&&state.page==='settings'", "Native navigation also protects an unsaved web settings draft.");
        await Act("await discardSettingsAndContinue();await send('navigate',{page:'settings'})");
        await Check("state.page==='settings'&&!preferencesDirty()", "Discard-and-continue clears the draft and allows navigation.");
        await Act("setSettingsTab('general');editPreference('showTrayIcon',false);editPreference('restoreOnExit',true);await settingsSync;$('#prefSettingsBody').scrollTop=35;$('#pref-showTrayIcon').focus({preventScroll:true})");
        var stability = JsonSerializer.Deserialize<Dictionary<string, bool>>(await core.ExecuteScriptAsync("""
            (()=>{
              const node=$('#pref-showTrayIcon'),scroll=$('#prefSettingsBody').scrollTop,checks={};
              const observer=new MutationObserver(()=>{});observer.observe(document.body,{subtree:true,attributes:true,childList:true,characterData:true});
              for(let i=0;i<30;i++)receive(structuredClone(state));
              checks['Unchanged settings refresh produces zero DOM mutations']=observer.takeRecords().length===0;observer.disconnect();
              checks['Settings refresh preserves the switch, focus, draft and scroll']=node===$('#pref-showTrayIcon')&&document.activeElement===node&&settingsDraft.showTrayIcon===false&&$('#prefSettingsBody').scrollTop===scroll;
              return checks;
            })()
            """))!;
        foreach (var check in stability) { if (!check.Value) throw new IOException(check.Key); checks.Add(check.Key); }
        await Act("await cancelSettings();$('#pref-tab-general').focus();$('#pref-tab-general').dispatchEvent(new KeyboardEvent('keydown',{key:'ArrowRight',bubbles:true}))");
        await Check("settingsTab==='appearance'&&document.activeElement.id==='pref-tab-appearance'&&$('#pref-tab-appearance').getAttribute('aria-selected')==='true'", "Settings tabs expose selected state and support arrow-key focus.");
        var controller = DashboardField<Controller>(form, "controller");
        controller.Saved.Language = "en-US"; L.Set("en-US"); typeof(MainForm).GetMethod("PublishWebState", DashboardFlags)!.Invoke(form, new object[] { true });
        form.ClientSize = new Size(1004, 701); await Task.Delay(180);
        await Check("document.documentElement.scrollWidth<=innerWidth&&$('#settingsPage h2').textContent==='Settings'&&$('#prefSave').getBoundingClientRect().bottom<=innerHeight", "English settings fit the minimum window width and keep Save visible.");
        await Act("await send('navigate',{page:'about'})");
        await Check("$('#aboutPage h2').textContent==='About & updates'&&state.about.updateState==='idle'&&$('#prefVersion').textContent===state.about.version", "About uses the running version and never checks for updates automatically.");
        await Check("!$('#aboutPage .pref-technical').open&&$('#prefDiagnostics').textContent.includes('TrayPilot')", "Actual runtime diagnostics start collapsed.");
        await Act("$('#aboutPage .pref-technical').open=true;$('#prefAboutBody').scrollTop=150");
        await Check("$('#prefDiagnostics').textContent.includes('settings')||$('#prefDiagnostics').textContent.includes('web-preview')", "Expanded diagnostics include the actual isolated settings folder.");
        await Act("await send('navigate',{page:'icons'});await send('navigate',{page:'about'})");
        await Check("$('#aboutPage .pref-technical').open&&$('#prefAboutBody').scrollTop>0", "About navigation preserves diagnostics expansion and scroll position.");
        controller.Saved.Language = "zh-CN"; L.Set("zh-CN"); typeof(MainForm).GetMethod("PublishWebState", DashboardFlags)!.Invoke(form, new object[] { true });
        form.Size = new Size(1380, 920); await Act("$('#aboutPage .pref-technical').open=false;await send('navigate',{page:'settings'});setSettingsTab('general');$('#prefSettingsBody').scrollTop=0;$('#toast').classList.remove('show')");
        return checks.ToArray();
    }

    static async Task CaptureWebPreferencesViews(CoreWebView2 core, MainForm form, string output, bool dark)
    {
        string folder = Path.GetDirectoryName(output)!, theme = dark ? "dark" : "light";
        async Task Capture(string name)
        { await Task.Delay(180); using var stream = File.Create(Path.Combine(folder, "web-04-" + name + "-" + theme + ".png")); await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream); }
        foreach (string tab in new[] { "appearance", "shortcuts" }) { await core.ExecuteScriptAsync("setSettingsTab('" + tab + "')"); await Capture(tab); }
        await core.ExecuteScriptAsync("openPreferenceHotkey(0)"); await Capture("hotkey"); await core.ExecuteScriptAsync("closePreferencesDialog('prefHotkeyDialog')");
        await RunWebRulesScript(core, "editPreferenceHotkey(0,true,131072|262144|77);editPreferenceHotkey(1,true,131072|262144|77);await settingsSync"); await Capture("conflict");
        await core.ExecuteScriptAsync("goPage('about')"); await Capture("discard");
        await RunWebRulesScript(core, "await discardSettingsAndContinue();$('#prefAboutBody').scrollTop=0"); await Capture("about");
        typeof(MainForm).GetField("webUpdateState", DashboardFlags)!.SetValue(form, "failed");
        typeof(MainForm).GetMethod("PublishWebState", DashboardFlags)!.Invoke(form, new object[] { true }); await Capture("about-failed");
        typeof(MainForm).GetField("webUpdateState", DashboardFlags)!.SetValue(form, "idle");
        await RunWebRulesScript(core, "await send('navigate',{page:'settings'});setSettingsTab('general')");
    }

    static int TestWebPreferencesBridge(string output)
    {
        output = Path.GetFullPath(output); Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        string folder = Path.Combine(Path.GetDirectoryName(output)!, "preferences-bridge-" + Guid.NewGuid().ToString("N"));
        string registry = @"Software\TrayPilotWebPreferencesTest-" + Guid.NewGuid().ToString("N");
        var startup = new StartupRegistration(registry);
        var controller = new Controller(folder); controller.Saved.Theme = "light"; controller.Saved.Language = "zh-CN"; controller.Saved.HiddenPaths.Add(@"C:\Fixture\ordinary.exe"); controller.Saved.HiddenSystemIcons = 16; controller.Save();
        using var form = new MainForm(controller, initialize: false, startup: startup);
        var links = new List<string>(); form.WebOpenLinkForDiagnostics = links.Add;
        form.WebStartupReadForDiagnostics = () => Task.FromResult(startup.Enabled);
        UpdateChecker.Release? release = null; string updateMode = "normal"; int updateCalls = 0; bool cancellationObserved = false;
        TaskCompletionSource<UpdateChecker.Release?>? updatePending = null;
        form.WebUpdateCheckForDiagnostics = async token =>
        {
            updateCalls++;
            if (updateMode == "failed") throw new IOException("Fixture network failure");
            if (updateMode == "timeout") throw new OperationCanceledException("Fixture timeout");
            if (updateMode == "cancel") { try { await Task.Delay(Timeout.Infinite, token); } catch (OperationCanceledException) { cancellationObserved = true; throw; } }
            return updatePending != null ? await updatePending.Task : release;
        };
        TaskCompletionSource? savePending = null;
        form.WebSettingsBeforeSaveForDiagnostics = () => savePending?.Task ?? Task.CompletedTask;
        using var hotkeyHolder = new Form(); _ = hotkeyHolder.Handle;
        using var occupied = new GlobalHotkey(hotkeyHolder.Handle, 0x6501);
        var candidates = Enumerable.Range(1, 11).Select(i => Keys.Control | Keys.Alt | Keys.Shift | (Keys)((int)Keys.F1 + i - 1));
        var chosen = candidates.FirstOrDefault(key => occupied.TrySet(true, key));
        if (chosen == Keys.None) throw new IOException("No available test shortcut.");
        Exception? failure = null; var checks = new List<string>();
        form.Shown += async (_, _) =>
        {
            try
            {
                await form.EnableWebInterfaceAsync(); await form.WebLoaded.Task.WaitAsync(TimeSpan.FromSeconds(25));
                var core = form.Controls.OfType<Microsoft.Web.WebView2.WinForms.WebView2>().Single(v => v.Name == "mainWebView").CoreWebView2;
                async Task Check(string script, string label)
                { if (await core.ExecuteScriptAsync(script) != "true") throw new IOException(label + " · " + await core.ExecuteScriptAsync("JSON.stringify({page:state.page,settings:state.settings,local:settingsDraft,error:settingsOperationError,about:state.about})")); checks.Add(label); }
                async Task Act(string body) => await RunWebRulesScript(core, body);
                await Act("await send('navigate',{page:'settings'});editPreference('restoreOnExit',true);editPreference('startup',true);await settingsSync;await saveSettings()");
                await Check("!preferencesDirty()&&settingsBaseline.restoreOnExit&&settingsBaseline.startup", "The actual web form saves settings and refreshes its baseline.");
                if (!new Controller(folder).Saved.RestoreIconsOnExit || !startup.Enabled) throw new IOException("Settings or isolated startup registration not saved."); checks.Add("Settings persist and startup changes use the isolated registration backend.");
                await Act("editPreference('theme','dark');await settingsSync"); await Check("state.theme==='dark'&&preferencesDirty()", "A web theme preview updates the real palette before saving.");
                if (new Controller(folder).Saved.Theme != "light") throw new IOException("Preview persisted prematurely."); checks.Add("Theme preview leaves the saved preference unchanged.");
                await Act("await cancelSettings()"); await Check("state.theme==='light'&&!preferencesDirty()", "Cancel restores the saved native theme.");
                await Act("editPreferenceHotkey(0,true," + (int)chosen + ");await settingsSync;await saveSettings()");
                await Check("preferencesDirty()&&settingsOperationError.length>0&&!settingsBaseline.hotkeys[0].enabled", "Real RegisterHotKey occupation is reported and the draft remains editable.");
                if (controller.Saved.MainHotkeyEnabled || new Controller(folder).Saved.MainHotkeyEnabled) throw new IOException("Occupied shortcut committed."); checks.Add("Shortcut registration failure rolls back both memory and persisted preferences.");
                occupied.Dispose(); await Act("await saveSettings()"); await Check("!preferencesDirty()&&settingsBaseline.hotkeys[0].enabled", "Releasing the occupied shortcut allows the same draft to save.");
                if (!new Controller(folder).Saved.MainHotkeyEnabled) throw new IOException("Shortcut did not persist."); checks.Add("A successful save persists the actual shortcut combination.");
                Directory.CreateDirectory(Path.Combine(folder, "settings.json.tmp"));
                try
                {
                    await Act("editPreference('startup',false);editPreference('showTrayIcon',false);editPreferenceHotkey(0,false,settingsDraft.hotkeys[0].keys);await settingsSync;await saveSettings()");
                    await Check("preferencesDirty()&&settingsOperationError.length>0&&settingsBaseline.showTrayIcon&&settingsBaseline.hotkeys[0].enabled", "Failed persistence retains the draft and the last confirmed baseline.");
                    if (!startup.Enabled || !controller.Saved.ShowTrayIcon || !controller.Saved.MainHotkeyEnabled || !new Controller(folder).Saved.MainHotkeyEnabled) throw new IOException("Transaction rollback failed."); checks.Add("A settings write failure rolls back startup registration and shortcut preferences.");
                    using var probe = new GlobalHotkey(hotkeyHolder.Handle, 0x6510); if (probe.TrySet(true, chosen)) throw new IOException("Prior shortcut was not restored."); checks.Add("Rollback re-registers the previously enabled shortcut.");
                }
                finally { Directory.Delete(Path.Combine(folder, "settings.json.tmp")); }
                await Act("await cancelSettings();editPreference('language','en-US');editPreference('theme','dark');await settingsSync;await saveSettings()");
                await Check("state.page==='settings'&&state.language==='en-US'&&state.theme==='dark'&&!preferencesDirty()&&$('#settingsPage h2').textContent==='Settings'", "Saving language and theme translates the active web page and keeps navigation intact.");
                savePending = new(TaskCreationOptions.RunContinuationsAsynchronously);
                await core.ExecuteScriptAsync("editPreference('closeToTray',false);void settingsSync.then(()=>saveSettings())"); await Task.Delay(220);
                await Check("state.settings.saving&&$('#prefSave').disabled&&$('#prefCancel').disabled", "An outstanding save disables repeated submission and cancellation.");
                await Act("let rejected=false;try{await send('settingsSave',{value:settingsDraft,version:settingsVersion})}catch{rejected=true}if(!rejected)throw Error('Duplicate save accepted')"); checks.Add("The native bridge rejects concurrent settings saves.");
                savePending.SetResult(); savePending = null; await Task.Delay(260);
                await Check("!state.settings.saving&&!preferencesDirty()&&!settingsBaseline.closeToTray", "Save acknowledgement commits the draft and unlocks the form.");
                form.WebStartupReadForDiagnostics = () => Task.FromException<bool>(new IOException("Fixture startup read failure"));
                await Act("await send('settingsStartupRetry')"); await Check("settingsDraft.startup===null&&$('#pref-startup').disabled&&state.settings.startupError.length>0", "Startup read failure disables only the unavailable startup preference.");
                await Act("editPreference('restoreOnExit',false);await settingsSync;await saveSettings()"); await Check("!preferencesDirty()&&!settingsBaseline.restoreOnExit", "Other settings remain saveable when startup state is unavailable.");
                form.WebStartupReadForDiagnostics = () => Task.FromResult(startup.Enabled);
                await Act("await send('settingsStartupRetry')"); await Check("settingsDraft.startup===true&&!state.settings.startupError", "Startup retry restores the actual isolated registration state.");
                await Act("let rejected=false;try{await send('settingsDraft',{version:settingsVersion+1,value:{...settingsDraft,theme:'invalid'}})}catch{rejected=true}if(!rejected)throw Error('Unknown theme accepted')"); checks.Add("Unknown theme values are rejected before any settings operation.");
                await Act("let rejected=false;try{await send('settingsDraft',{version:settingsVersion+1,value:{...settingsDraft,hotkeys:[]}})}catch{rejected=true}if(!rejected)throw Error('Invalid shortcut list accepted')"); checks.Add("Shortcut lists must contain exactly the three supported actions.");
                await Act("editPreference('showTrayIcon',false);await settingsSync;await send('navigate',{page:'about'})"); await Task.Delay(120);
                await Check("state.page==='settings'&&$('#prefDiscardDialog').open", "Native page navigation protects unsaved preferences with the HTML discard dialog.");
                await Act("await discardSettingsAndContinue()"); await Check("state.page==='about'&&state.about.updateState==='idle'", "Discard continues to About without making an update request.");
                if (updateCalls != 0) throw new IOException("Automatic update request made."); checks.Add("About does not access the update service until requested.");
                updatePending = new(TaskCreationOptions.RunContinuationsAsynchronously);
                await core.ExecuteScriptAsync("window.prefUpdateDone=false;void send('aboutCheck').then(()=>window.prefUpdateDone=true)"); await Task.Delay(150);
                await Check("state.about.updateState==='checking'&&$('#prefCheckUpdate').disabled", "Manual update checks expose progress and disable repeated checking.");
                await Act("let rejected=false;try{await send('aboutCheck')}catch{rejected=true}if(!rejected)throw Error('Duplicate update request accepted')");
                if (updateCalls != 1) throw new IOException("Duplicate update service call."); checks.Add("Duplicate update requests do not reach the update service.");
                release = new(new Version(999, 0, 0, 0), "v999.0.0"); updatePending.SetResult(release); updatePending = null; await Task.Delay(220);
                await Check("state.about.updateState==='new'&&!$('#prefDownload').hidden&&$('#prefUpdateStatus').textContent.includes('v999.0.0')", "A newer release shows its actual tag and the separate download action.");
                await Act("await send('aboutLink',{kind:'download'})"); if (links.LastOrDefault() != release.Url) throw new IOException("Wrong release download target."); checks.Add("Download opens only the native release URL; no file is replaced or installed.");
                await Act("let rejected=false;try{await send('aboutLink',{kind:'https://untrusted.example'})}catch{rejected=true}if(!rejected)throw Error('Arbitrary URL accepted')"); checks.Add("About links accept only known native link kinds.");
                release = new(UpdateChecker.CurrentVersion, "current"); await Act("await send('aboutCheck')"); await Check("state.about.updateState==='current'&&$('#prefDownload').hidden", "An up-to-date result hides the download action.");
                release = null; await Act("await send('aboutCheck')"); await Check("state.about.updateState==='noRelease'", "No stable release has a distinct result.");
                updateMode = "failed"; await Act("await send('aboutCheck')"); await Check("state.about.updateState==='failed'&&!$('#prefCheckUpdate').disabled", "Update failure restores the retry action.");
                updateMode = "timeout"; await Act("await send('aboutCheck')"); await Check("state.about.updateState==='timeout'", "An update timeout is distinguished from a general failure.");
                updateMode = "cancel"; await core.ExecuteScriptAsync("void send('aboutCheck')"); await Task.Delay(120); await Act("await send('navigate',{page:'icons'})"); await Task.Delay(100);
                if (!cancellationObserved) throw new IOException("Update cancellation not observed."); checks.Add("Leaving About cancels the active update request.");
                await Act("await send('navigate',{page:'about'})"); await Check("state.about.updateState==='idle'", "An old cancelled request cannot overwrite the returned About page.");
                if (controller.Saved.HiddenPaths.Count != 1 || controller.Saved.HiddenSystemIcons != 16) throw new IOException("Icon preferences changed."); checks.Add("Settings and About preserve ordinary hide rules and system hide preferences.");
                File.WriteAllText(output, JsonSerializer.Serialize(new { passed = true, checks, isolation = "Real WebView2, settings transactions and RegisterHotKey; test-only startup registration; simulated updates; no network or browser launch" }, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex) { failure = ex; }
            finally
            {
                savePending?.TrySetResult(); updatePending?.TrySetCanceled();
                typeof(MainForm).GetMethod("CancelWebSettingsChanges", DashboardFlags)!.Invoke(form, null);
                controller.Saved.RestoreIconsOnExit = false;
                File.WriteAllText(output + ".completed.json", JsonSerializer.Serialize(checks)); form.CloseWebDiagnosticsWindow();
            }
        };
        try { Application.Run(form); }
        finally { occupied.Dispose(); startup.SetEnabled(false); Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(registry, false); }
        if (failure != null) throw failure; return 0;
    }
}
