using Microsoft.Web.WebView2.Core;
using System.Text.Json;

namespace TrayPilot;

internal static partial class Diagnostics
{
    static async Task<string[]> CheckWebSystemUi(CoreWebView2 core, MainForm form)
    {
        var checks = new List<string>();
        async Task Check(string script, string label)
        { if (await core.ExecuteScriptAsync(script) != "true") throw new IOException(label + " · " + await core.ExecuteScriptAsync("JSON.stringify({page:state.page,system:state.system,focus:document.activeElement.id})")); checks.Add(label); }
        async Task Act(string script) { await core.ExecuteScriptAsync(script); await Task.Delay(160); }
        void Snapshot(SystemIconsSnapshot value)
        { form.WebSystemReadForDiagnostics = () => value; typeof(MainForm).GetMethod("PublishWebState", DashboardFlags)!.Invoke(form, new object[] { true }); }
        await RunWebRulesScript(core, "await send('navigate',{page:'system'})");
        DashboardField<System.Windows.Forms.Timer>(form, "webSystemPoll").Stop();
        await Check("!$('#systemPage').hidden&&$('#iconsPage').hidden&&$('#rulesPage').hidden&&document.querySelectorAll('.nav.active')[0]===document.querySelectorAll('.nav')[2]", "System navigation stays in the WebView and selects the correct sidebar item.");
        await Check("systemNodes.size===12&&[...systemNodes.values()].every(n=>n.querySelector('img').complete&&n.querySelector('img').naturalWidth>0)", "All 12 system controls have individual icons.");
        await Check("systemNodes.get(1).querySelector('button').getAttribute('aria-checked')==='true'&&systemNodes.get(16).querySelector('button').getAttribute('aria-checked')==='false'", "Switch-on means show; absent controls retain their hide preferences.");
        await Check("systemItem(16).state==='systemNativeWaiting'&&systemItem(4).state==='systemNativeNotFound'", "Absent controls distinguish waiting-to-hide from not currently found.");
        await Act("filterSystem('common')"); await Check("[...systemNodes.values()].filter(n=>!n.hidden).length===4", "Common controls filter contains four controls.");
        await Act("filterSystem('indicators')"); await Check("[...systemNodes.values()].filter(n=>!n.hidden).length===5", "Status indicators filter contains five controls.");
        await Act("filterSystem('taskbar')"); await Check("[...systemNodes.values()].filter(n=>!n.hidden).length===3", "Taskbar controls filter contains three controls.");
        await Act("filterSystem('all');chooseSystem(16)"); await Check("!$('#systemDetail .system-missing').hidden&&!$('#systemDetail .system-shared').hidden", "Details explain absent preferences and shared microphone/location behavior.");
        var stable = JsonSerializer.Deserialize<Dictionary<string, bool>>(await core.ExecuteScriptAsync("""
            (()=>{
              const checks={},card=systemNodes.get(16),image=card.querySelector('img'),button=card.querySelector('button'),detail=$('#systemDetail .system-technical');
              $('#systemEntries').scrollTop=70;$('#systemDetail').scrollTop=35;detail.open=true;button.focus({preventScroll:true});const scroll=$('#systemEntries').scrollTop,detailScroll=$('#systemDetail').scrollTop;
              const observer=new MutationObserver(()=>{});observer.observe(document.body,{subtree:true,attributes:true,childList:true,characterData:true});
              for(let i=0;i<30;i++)receive(structuredClone(state));
              checks['Repeated system refresh makes zero DOM mutations']=observer.takeRecords().length===0;observer.disconnect();
              checks['System refresh preserves card, image, focus and both scroll positions']=systemNodes.get(16)===card&&card.querySelector('img')===image&&document.activeElement===button&&$('#systemEntries').scrollTop===scroll&&$('#systemDetail').scrollTop===detailScroll&&detail.open;
              const before=$('#systemEntries').getBoundingClientRect();pending=1;pendingAction='systemToggle';updateBusy();const after=$('#systemEntries').getBoundingClientRect();
              checks['System progress overlay does not shift cards']=before.x===after.x&&before.y===after.y&&before.height===after.height&&$('#systemBusyNote').classList.contains('show');pending=0;pendingAction='';updateBusy();
              detail.open=false;return checks;
            })()
            """))!;
        foreach (var check in stable) { if (!check.Value) throw new IOException(check.Key); checks.Add(check.Key); }
        Snapshot(SystemDashboardSample(loading: true)); await Task.Delay(150);
        await Check("$('#systemConnection').classList.contains('loading')&&[...systemNodes.values()].every(n=>n.querySelector('button').disabled)&&systemNodes.get(16).querySelector('button').getAttribute('aria-checked')==='false'", "Identification disables switches without clearing saved preferences.");
        Snapshot(SystemDashboardSample(failed: true)); await Task.Delay(150);
        await Check("$('#systemConnection').classList.contains('failed')&&$('#systemReconnect').disabled===false&&$('#systemToggleSelected').disabled&&$('#systemDetail pre').textContent.includes('Preview:')", "Connection failure retains preferences, offers retry and exposes diagnostics.");
        Snapshot(SystemDashboardSample() with { Found = 63, Hidden = 0, Requested = 16, Shared = true }); await Task.Delay(150);
        await Check("systemItem(16).state==='systemSharedIndicator'&&systemItem(32).state==='systemNativeVisible'", "A single hide preference does not claim the shared indicator is hidden.");
        Snapshot(SystemDashboardSample()); await Task.Delay(150);
        await RunWebRulesScript(core, "let rejected=false;try{await send('systemToggle',{bit:1,show:false})}catch{rejected=true}if(!rejected)throw Error('Read-only operation was accepted')"); checks.Add("Read-only preview rejects system mutations through the native bridge.");
        await Act("$('#systemReconnect').focus();openSystemDialog('systemSchemeDialog')");
        await Check("$('#systemSchemeDialog').open&&$('#systemSchemeDialog input[value=modern]').checked&&document.activeElement.hasAttribute('data-system-cancel')", "Compatibility dialog reflects saved discovery method and starts on Cancel.");
        await Act("closeSystemDialog('systemSchemeDialog')"); await Check("document.activeElement.id==='systemReconnect'", "Closing a dialog restores the invoking control's focus.");
        await Act("openSystemDialog('systemRestoreDialog')"); await Check("$('#systemRestoreDialog').open&&$('#systemRestoreDialog').textContent.includes(st('restoreHelp'))", "Restore-all confirmation includes clearing absent hide preferences.");
        await Act("closeSystemDialog('systemRestoreDialog');chooseSystem(256)");
        await Check("systemNodes.get(256).querySelector('strong').textContent===systemItem(256).name&&!systemNodes.get(256).querySelector('.system-glyph').textContent.trim()", "Language indicator uses a semantic icon instead of invented current language text.");
        var previewController = DashboardField<Controller>(form, "controller");
        previewController.Saved.Language = "en-US"; L.Set(previewController.Saved.Language); Snapshot(SystemDashboardSample()); await Act("chooseSystem(16)");
        form.ClientSize = new Size(1004, 701); await Task.Delay(160);
        await Check("document.documentElement.scrollWidth<=innerWidth&&$('#systemPage h2').textContent==='System icons'&&systemItem(512).name==='Language supplementary icons'&&systemItem(16).status==='Will hide when it appears'", "English native names, statuses and labels fit at the minimum window width.");
        previewController.Saved.Language = "zh-CN"; L.Set(previewController.Saved.Language); Snapshot(SystemDashboardSample());
        await Act("filterSystem('indicators');chooseSystem(32);systemNodes.get(32).dispatchEvent(new KeyboardEvent('keydown',{key:'ArrowLeft',bubbles:true}))");
        await Check("document.activeElement===systemNodes.get(16)", "Arrow keys move focus between visible controls.");
        await RunWebRulesScript(core, "await send('navigate',{page:'icons'});await send('navigate',{page:'system'})");
        DashboardField<System.Windows.Forms.Timer>(form, "webSystemPoll").Stop();
        await Check("systemFilter==='indicators'&&systemCurrent===16&&$('#iconsPage').hidden", "Returning to system controls preserves the filter and selection.");
        form.Size = new Size(1380, 920); await Act("filterSystem('all');chooseSystem(1024);$('#systemEntries').scrollTop=0;$('#systemDetail').scrollTop=0;$('#toast').classList.remove('show')");
        return checks.ToArray();
    }

    static async Task CaptureWebSystemDialogs(CoreWebView2 core, string output, bool dark)
    {
        foreach (var (dialog, label) in new[] { ("systemSchemeDialog", "compatibility"), ("systemRestoreDialog", "restore") })
        {
            await core.ExecuteScriptAsync("openSystemDialog('" + dialog + "')"); await Task.Delay(180);
            using var stream = File.Create(Path.Combine(Path.GetDirectoryName(output)!, "web-03-" + label + "-" + (dark ? "dark" : "light") + ".png"));
            await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream);
            await core.ExecuteScriptAsync("closeSystemDialog('" + dialog + "')"); await Task.Delay(50);
        }
    }

    static async Task CaptureWebSystemStates(CoreWebView2 core, MainForm form, string output, bool dark)
    {
        foreach (var (snapshot, label) in new[] { (SystemDashboardSample(loading: true), "loading"), (SystemDashboardSample(failed: true), "failed") })
        {
            form.WebSystemReadForDiagnostics = () => snapshot;
            typeof(MainForm).GetMethod("PublishWebState", DashboardFlags)!.Invoke(form, new object[] { true });
            await core.ExecuteScriptAsync("chooseSystem(1)"); await Task.Delay(180);
            using var stream = File.Create(Path.Combine(Path.GetDirectoryName(output)!, "web-03-" + label + "-" + (dark ? "dark" : "light") + ".png"));
            await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream);
        }
        form.WebSystemReadForDiagnostics = () => SystemDashboardSample();
        typeof(MainForm).GetMethod("PublishWebState", DashboardFlags)!.Invoke(form, new object[] { true });
    }

    // Exercise the real web/native message bridge and real settings persistence.
    // Taskbar callbacks are isolated fixtures; no user system controls are changed.
    static int TestWebSystemBridge(string output)
    {
        output = Path.GetFullPath(output); Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        string folder = Path.Combine(Path.GetDirectoryName(output)!, "system-bridge-" + Guid.NewGuid().ToString("N"));
        var controller = new Controller(folder); controller.Saved.Language = "zh-CN"; controller.Saved.RulesPaused = true; controller.Saved.HiddenPaths.Add(@"C:\Fixture\ordinary.exe"); controller.Save();
        var snapshot = SystemDashboardSample() with { Requested = 0, Hidden = 0, Diagnostic = "Isolated taskbar fixture" };
        int calls = 0, restoreMask = 0; bool failConnect = false;
        TaskCompletionSource<bool>? delayed = null;
        using var form = new MainForm(controller, initialize: false);
        form.WebSystemReadForDiagnostics = () => snapshot;
        form.WebSystemChangeForDiagnostics = async (mask, restore) =>
        {
            calls++; restoreMask = restore; if (delayed != null) await delayed.Task;
            controller.SetHiddenSystemIcons(mask);
            int hidden = mask & snapshot.Found; if (snapshot.Shared && (mask & 48) != 48) hidden &= ~48;
            return snapshot = snapshot with { Requested = mask, Hidden = hidden };
        };
        form.WebSystemConnectForDiagnostics = legacy =>
        {
            if (legacy.HasValue) { controller.Saved.UseLegacySystemIconDiscovery = legacy.Value; controller.Save(); snapshot = snapshot with { Legacy = legacy.Value }; }
            if (failConnect) { snapshot = snapshot with { Connected = false, Error = "Fixture connection failed" }; throw new IOException(snapshot.Error); }
            return Task.FromResult(snapshot = snapshot with { Connected = true, Connecting = false, Error = "" });
        };
        Exception? failure = null; var checks = new List<string>();
        form.Shown += async (_, _) =>
        {
            try
            {
                await form.EnableWebInterfaceAsync(); await form.WebLoaded.Task.WaitAsync(TimeSpan.FromSeconds(25));
                var core = form.Controls.OfType<Microsoft.Web.WebView2.WinForms.WebView2>().Single(v => v.Name == "mainWebView").CoreWebView2;
                async Task Check(string script, string label)
                { if (await core.ExecuteScriptAsync(script) != "true") throw new IOException(label + " · " + await core.ExecuteScriptAsync("JSON.stringify({system:state.system,focus:document.activeElement.id})")); checks.Add(label); }
                async Task Request(string script) => await RunWebRulesScript(core, script);
                void Publish() => typeof(MainForm).GetMethod("PublishWebState", DashboardFlags)!.Invoke(form, new object[] { true });
                await Request("await send('navigate',{page:'system'})");
                DashboardField<System.Windows.Forms.Timer>(form, "webSystemPoll").Stop();
                await Request("await send('systemToggle',{bit:1,show:false})");
                await Check("state.system.requested===1&&state.system.hidden===1&&systemNodes.get(1).querySelector('button').getAttribute('aria-checked')==='false'", "Hide request is acknowledged before the switch commits.");
                if (new Controller(folder).Saved.HiddenSystemIcons != 1) throw new IOException("System mask not persisted."); checks.Add("System hide preference persists to real isolated settings.");
                await Request("await send('systemToggle',{bit:1,show:true})");
                if (restoreMask != 1) throw new IOException("Wrong explicit restoration mask."); checks.Add("Show sends an explicit restoration mask for the selected control.");
                await Request("await send('systemToggle',{bit:16,show:false})");
                await Check("systemItem(16).state==='systemNativeWaiting'&&state.system.requested===16", "Absent control preferences are saved without claiming a current hide.");
                await Request("await send('systemUndo')"); await Check("state.system.requested===0&&!state.system.undo", "Undo restores the previous system mask once.");
                await Request("await send('systemToggle',{bit:4,show:false});await send('systemRestore')");
                if (restoreMask != SystemIconCatalog.All) throw new IOException("Restore-all must restore all supported controls.");
                await Check("state.system.requested===0&&state.system.hidden===0", "Restore-all clears both current and absent system hide preferences.");
                await Request("await send('systemUndo')"); await Check("state.system.requested===4", "Restore-all can be undone without application rules changing.");
                snapshot = snapshot with { Found = snapshot.Found | 48, Shared = true }; Publish();
                await Request("await send('systemToggle',{bit:16,show:false})"); await Check("systemItem(16).state==='systemSharedIndicator'&&(state.system.hidden&48)===0", "Hiding one shared preference keeps the shared indicator visible.");
                await Request("await send('systemToggle',{bit:32,show:false})"); await Check("(state.system.hidden&48)===48", "Hiding both shared preferences confirms their hidden state.");
                int before = snapshot.Requested; Directory.CreateDirectory(Path.Combine(folder, "settings.json.tmp"));
                try
                {
                    await Request("let rejected=false;try{await systemRequest('systemToggle',{bit:2,show:false})}catch{rejected=true}if(!rejected)throw Error('Persistence failure accepted')");
                    await Check("state.system.error.length>0&&!systemNodes.get(2).querySelector('button').disabled&&systemNodes.get(2).querySelector('button').getAttribute('aria-checked')==='true'", "Save failure displays the error and restores the truthful switch state.");
                    if (snapshot.Requested != before || controller.Saved.HiddenSystemIcons != before || new Controller(folder).Saved.HiddenSystemIcons != before) throw new IOException("Save failure changed persisted state."); checks.Add("Save failure retains both in-memory and persisted system preferences.");
                }
                finally { Directory.Delete(Path.Combine(folder, "settings.json.tmp")); }
                delayed = new(TaskCreationOptions.RunContinuationsAsynchronously);
                await core.ExecuteScriptAsync("systemNodes.get(2).querySelector('button').focus();window.pendingSystemResult=null;void systemRequest('systemToggle',{bit:2,show:false}).then(()=>window.pendingSystemResult=true).catch(e=>window.pendingSystemResult=e.message)");
                await Task.Delay(220);
                await Check("state.system.working&&[...systemNodes.values()].every(n=>n.querySelector('button').disabled)&&systemNodes.get(2).querySelector('button').getAttribute('aria-checked')==='true'", "Pending acknowledgement blocks repeat actions and keeps the previous switch state.");
                int beforeDuplicate = calls;
                await Request("let rejected=false;try{await send('systemToggle',{bit:8,show:false})}catch{rejected=true}if(!rejected)throw Error('Concurrent request accepted')");
                if (calls != beforeDuplicate) throw new IOException("Duplicate taskbar operation executed."); checks.Add("Concurrent native system commands are rejected before reaching taskbar callbacks.");
                delayed.SetResult(true); delayed = null; await Task.Delay(350);
                await Check("window.pendingSystemResult===true&&document.activeElement===systemNodes.get(2).querySelector('button')", "Successful acknowledgement restores the invoking switch focus.");
                await Request("await send('systemScheme',{legacy:true})"); await Check("state.system.legacy&&state.system.requested!==0", "Discovery method changes retain the system hide preferences.");
                if (!new Controller(folder).Saved.UseLegacySystemIconDiscovery) throw new IOException("Scheme not persisted."); checks.Add("Discovery method persists independently from application rules.");
                failConnect = true; await Request("let rejected=false;try{await send('systemReconnect')}catch{rejected=true}if(!rejected)throw Error('Failed connection accepted')");
                await Check("!state.system.connected&&$('#systemReconnect').disabled===false&&$('#systemToggleSelected').disabled&&state.system.requested!==0", "Failed reconnect disables visibility operations and preserves preferences for retry.");
                failConnect = false; await Request("await send('systemReconnect')"); await Check("state.system.connected&&state.system.error===''", "Retry clears the connection error on success.");
                await Request("let rejected=false;try{await send('systemToggle',{bit:3,show:false})}catch{rejected=true}if(!rejected)throw Error('Unknown bit accepted')"); checks.Add("Only individual supported control bits are accepted.");
                await Request("let rejected=false;try{await send('systemToggle',{bit:1,show:'false'})}catch{rejected=true}if(!rejected)throw Error('Wrong boolean type accepted')"); checks.Add("System visibility arguments require a boolean.");
                snapshot = snapshot with { Requested = 0, Hidden = 0 }; Publish(); await Task.Delay(100);
                await Check("!state.system.undo", "External system preference changes invalidate an obsolete undo.");
                await Request("await send('navigate',{page:'icons'});let rejected=false;try{await send('systemRestore')}catch{rejected=true}if(!rejected)throw Error('Inactive system mutation accepted')"); checks.Add("System commands require the active system page.");
                if (controller.Saved.HiddenPaths.Count != 1 || !controller.Saved.HiddenPaths.Contains(@"C:\Fixture\ordinary.exe") || !controller.Saved.RulesPaused) throw new IOException("Application rules changed."); checks.Add("System operations preserve application hide rules and work while those rules are paused.");
                File.WriteAllText(output, JsonSerializer.Serialize(new { passed = true, checks, isolation = "Real WebView2 bridge and settings; simulated taskbar callbacks; no user system controls changed" }, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex) { failure = ex; }
            finally { delayed?.TrySetResult(false); File.WriteAllText(output + ".completed.json", JsonSerializer.Serialize(checks)); form.CloseWebDiagnosticsWindow(); }
        };
        Application.Run(form); if (failure != null) throw failure; return 0;
    }
}
