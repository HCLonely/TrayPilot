using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System.Text.Json;

namespace TrayPilot;

internal static partial class Diagnostics
{
    static async Task<string[]> CheckWebFeedbackUi(CoreWebView2 core, MainForm form)
    {
        var checks = new List<string>();
        async Task Check(string js, string label) { if (await core.ExecuteScriptAsync(js) != "true") throw new IOException(label + " · " + await core.ExecuteScriptAsync("JSON.stringify({dialogs:[...document.querySelectorAll('dialog[open]')].map(d=>d.id),focus:document.activeElement.id,error:$('#feedbackTerminateError').textContent})")); checks.Add(label); }
        async Task Act(string js) => await RunWebRulesScript(core, js);
        await Act("await send('navigate',{page:'icons'});await showWebProperties(apps.find(a=>a.canTerminate)?.id||apps[0].id)");
        await Check("$('#feedbackPropertiesDialog').open&&$('#feedbackPropertiesRows').textContent.includes(apps.find(a=>a.canTerminate)?.path||apps[0].path)&&document.activeElement.id==='feedbackPropertiesClose'", "HTML properties contain the actual program path and a reachable close action.");
        await Act("closeFeedbackDialog('feedbackPropertiesDialog');await openTerminate(apps.find(a=>a.canTerminate).id)");
        await Check("$('#feedbackTerminateDialog').open&&document.activeElement.id==='feedbackTerminateCancel'&&$('#feedbackTerminateProgram').textContent.includes(String(feedbackTerminate.pid))", "End-task confirmation uses actual program identity and defaults to Cancel.");
        await Act("await send('terminate',{id:feedbackTerminate.id,token:feedbackTerminate.token}).then(()=>{throw Error('Readonly must reject')},()=>{});closeFeedbackDialog('feedbackTerminateDialog');await openWebExit()");
        await Check("$('#feedbackExitDialog').open&&document.activeElement.id==='feedbackExitCancel'&&$('#feedbackExitHelp').textContent.includes('不恢复')", "Exit confirmation explains restore-off behavior and defaults to Cancel.");
        await Act("closeFeedbackDialog('feedbackExitDialog');openWebWelcome()");
        await Check("$('#feedbackWelcomeDialog').open&&document.querySelectorAll('.feedback-step').length===3&&document.activeElement.id==='feedbackWelcomeSkip'", "Getting started has three steps and a skip action without forced rules.");
        await Act("closeFeedbackDialog('feedbackWelcomeDialog');window.feedbackBaseline=structuredClone(state);receive({...state,entries:[]})");
        await Check("!!$('#entries .empty .feedback-empty-system')&&!$('#detail').querySelector('.detailhero')", "No application icons offers refresh and System icons without a stale detail.");
        await Act("receive(window.feedbackBaseline);$('#search').value='__no_feedback_match__';render()");
        await Check("!!$('#entries .empty button')&&$('#entries .empty').textContent.includes('清空搜索')", "No search matches offers clear search.");
        await Act("clearSearch();window.feedbackFirstRow=$('#entries .entry');window.feedbackImage=window.feedbackFirstRow.querySelector('img');window.feedbackBefore=window.feedbackFirstRow.getBoundingClientRect().top;receive({...state,busy:true,operation:{serial:999,phase:'working',total:3,completed:0,failed:[],canRetry:false,canUndo:false}})");
        await Check("!$('#feedbackPanel').hidden&&$('#feedbackPanel').classList.contains('working')&&$('#entries .entry')===window.feedbackFirstRow&&$('#entries .entry img')===window.feedbackImage&&$('#entries .entry').getBoundingClientRect().top===window.feedbackBefore&&$('#entries .rowaction').disabled", "Progress floats above the retained list without shifting rows or replacing images.");
        await Act("receive({...state,busy:false,operation:{serial:999,phase:'failed',total:2,completed:1,failed:[{id:apps[0].id,name:apps[0].name,error:'测试：图标在操作时发生变化'}],canRetry:true,canUndo:true}})");
        await Check("!$('#feedbackRetry').hidden&&!$('#feedbackUndo').hidden&&$('#feedbackTitle').textContent.includes('1 项已完成')&&$('#feedbackFailureList').textContent.includes(apps[0].name)", "Partial failure separates completed and failed counts and offers retry plus undo.");
        await Act("dismissFeedback();receive({...state})");
        await Check("$('#feedbackPanel').hidden&&!$('#visibilityUndoButton').hidden", "Dismissing the result preserves the persistent Undo entry.");
        await Act("receive(window.feedbackBaseline);document.documentElement.setAttribute('data-theme',state.theme)");
        // A real second WebView, with the main window hidden, exercises independent tray interaction.
        form.WebTrayKeepOpenForDiagnostics = true;
        form.Hide();
        var show = (Task)typeof(MainForm).GetMethod("ShowWebTrayAsync", DashboardFlags)!.Invoke(form, new object[] { new Point(Screen.PrimaryScreen!.WorkingArea.Right - 20, Screen.PrimaryScreen.WorkingArea.Bottom - 10) })!;
        await show;
        var tray = DashboardField<WebView2>(form, "webTrayView");
        for (int i = 0; i < 100 && !DashboardField<bool>(form, "webTrayReady"); i++) await Task.Delay(100);
        if (!DashboardField<bool>(form, "webTrayReady")) throw new TimeoutException("Tray web page not ready");
        var trayCore = tray.CoreWebView2; await Task.Delay(250);
        if (!tray.Visible || DashboardField<Form>(form,"webTrayPanel").Controls["trayLoading"]?.Visible == true) throw new IOException("The rendered tray frame must replace its loading surface.");
        checks.Add("The tray panel reveals its rendered frame only after icons are ready.");
        async Task TrayCheck(string js, string label) { if (await trayCore.ExecuteScriptAsync(js) != "true") throw new IOException(label); checks.Add(label); }
        await TrayCheck("document.images.length>0&&[...document.images].every(i=>i.complete&&i.naturalWidth>0)&&document.documentElement.scrollWidth<=innerWidth", "The independent tray panel loads actual program images within its monitor width.");
        if (form.Visible) throw new IOException("Tray panel unexpectedly showed the main window"); checks.Add("The tray panel works while the main window stays hidden.");
        await trayCore.ExecuteScriptAsync("window.trayBaseline=structuredClone(state);window.trayFirst=$('#trayApps .popup-app');window.trayImage=window.trayFirst?.querySelector('img');window.trayMutations=0;window.trayObserver=new MutationObserver(items=>window.trayMutations+=items.length);window.trayObserver.observe(document.body,{subtree:true,attributes:true,childList:true,characterData:true})");
        typeof(MainForm).GetMethod("PublishTrayState", DashboardFlags)!.Invoke(form,new object[]{true}); await Task.Delay(100);
        await TrayCheck("window.trayMutations===0&&$('#trayApps .popup-app')===window.trayFirst&&$('#trayApps .popup-app img')===window.trayImage", "An unchanged tray snapshot produces zero DOM updates and preserves program images.");
        await trayCore.ExecuteScriptAsync("window.trayObserver.disconnect()");
        foreach (string language in new[] { "zh-CN", "en-US" })
        {
            foreach (string phase in new[] { "done", "failed", "undone" })
            {
                await trayCore.ExecuteScriptAsync($$$"""
                    state={...state,language:'{{{language}}}',busy:true,operation:{serial:1001,phase:'working',total:1,completed:0,failed:[],canRetry:false,canUndo:false}};render();
                    """);
                await TrayCheck("!$('#trayFeedback').hidden&&/正在处理 1|Updating 1/.test($('#trayFeedbackText').textContent)&&$('#trayApps').getAttribute('aria-busy')==='true'", "Tray displays progress before " + phase + " in " + language + ".");
                await trayCore.ExecuteScriptAsync($$$"""
                    state={...state,busy:false,operation:{...state.operation,phase:'{{{phase}}}',completed:{{{(phase == "failed" ? 0 : 1)}}},failed:{{{(phase == "failed" ? "[{id:'test',name:'test',error:'test failure'}]" : "[]")}}},canRetry:{{{(phase == "failed" ? "true" : "false")}}},canUndo:{{{(phase == "done" ? "true" : "false")}}}}};render();window.trayFeedbackTimer=feedbackTimer;
                    """);
                string expected = phase == "failed" ? "/1 项未完成|1 failed/" : phase == "undone" ? "/已撤销上次操作|Last action undone/" : "/已完成 1 项|1 actions completed/";
                await TrayCheck("!$('#trayFeedback').hidden&&" + expected + ".test($('#trayFeedbackText').textContent)&&$('#trayApps').getAttribute('aria-busy')==='false'", "Tray replaces progress with " + phase + " for the same operation serial in " + language + ".");
                await trayCore.ExecuteScriptAsync("render()");
                await TrayCheck("feedbackTimer===window.trayFeedbackTimer", "Unchanged " + phase + " feedback does not restart its dismissal timer in " + language + ".");
            }
        }
        await trayCore.ExecuteScriptAsync("state={...window.trayBaseline,capacity:2};render();movePage(1)");
        await TrayCheck("page===1&&$('#trayApps').children.length<=2", "Tray pagination uses host capacity rather than the six-row prototype limit.");
        await trayCore.ExecuteScriptAsync("document.dispatchEvent(new KeyboardEvent('keydown',{key:'ArrowLeft',bubbles:true}))");
        await TrayCheck("page===0", "Keyboard arrows change tray pages.");
        await trayCore.ExecuteScriptAsync("document.dispatchEvent(new WheelEvent('wheel',{deltaY:30,bubbles:true,cancelable:true}));document.dispatchEvent(new WheelEvent('wheel',{deltaY:30,bubbles:true,cancelable:true}))");
        await TrayCheck("page===0", "High-resolution wheel deltas accumulate without premature paging.");
        await trayCore.ExecuteScriptAsync("document.dispatchEvent(new WheelEvent('wheel',{deltaY:50,bubbles:true,cancelable:true}))");
        await TrayCheck("page===1", "Accumulated wheel movement advances the tray page.");
        await trayCore.ExecuteScriptAsync("$('#traySearch').value='__unmatched__';$('#traySearch').dispatchEvent(new Event('input'))");
        await TrayCheck("page===0&&!!$('#trayApps .tray-empty')&&$('#trayPrevious').disabled&&$('#trayNext').disabled", "Tray search resets pagination and gives a clear empty state.");
        await trayCore.ExecuteScriptAsync("$('#traySearch').value='';state=window.trayBaseline;const entry=state.entries[0];state={...state,entries:[{...entry,hidden:false},{...entry,id:'preview-second-icon',hidden:true,identity:'UID 42'}]};render();openGroup(entry.path)");
        await TrayCheck("$('#trayGroupDialog').open&&$('#trayGroupEntries').children.length===2&&$('#trayApps .status').textContent.includes('部分隐藏')&&document.activeElement.id==='trayGroupClose'", "Multi-icon groups show partial visibility and individually addressable icons.");
        await trayCore.ExecuteScriptAsync("$('#trayGroupDialog').close();groupPath='';state=window.trayBaseline;render()");
        await RunWebRulesScript(trayCore, "await openExit()");
        await TrayCheck("$('#trayExitDialog').open&&document.activeElement.id==='trayExitCancel'", "Tray exit confirmation defaults to Cancel.");
        await trayCore.ExecuteScriptAsync("$('#trayExitDialog').close()");
        typeof(MainForm).GetMethod("HideWebTray", DashboardFlags)!.Invoke(form, null); form.Show();
        return checks.ToArray();
    }

    static async Task CaptureWebFeedbackViews(CoreWebView2 core, MainForm form, string output, bool dark)
    {
        string suffix = dark ? "dark" : "light";
        async Task Shot(CoreWebView2 target, string name) { await Task.Delay(180); using var stream = File.Create(Path.Combine(Path.GetDirectoryName(output)!, "web-05-" + name + "-" + suffix + ".png")); await target.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream); }
        async Task Act(string js) => await RunWebRulesScript(core, js);
        await Act("window.feedbackCaptureBaseline=structuredClone(state);openWebWelcome()"); await Shot(core,"welcome");
        await Act("closeFeedbackDialog('feedbackWelcomeDialog');receive({...state,entries:[]})"); await Shot(core,"empty");
        await Act("receive(window.feedbackCaptureBaseline);$('#search').value='未找到的程序';render()"); await Shot(core,"search");
        await Act("clearSearch();receive({...state,busy:true,operation:{serial:500,phase:'working',total:3,completed:0,failed:[],canRetry:false,canUndo:false}})"); await Shot(core,"busy");
        await Act("receive({...state,busy:false,operation:{serial:500,phase:'failed',total:2,completed:1,failed:[{id:apps[0].id,name:apps[0].name,error:'演示状态：图标在操作时发生变化，请刷新后重试。'}],canRetry:true,canUndo:true}})"); await Shot(core,"partial");
        await Act("receive(window.feedbackCaptureBaseline);await showWebProperties(apps.find(a=>a.canTerminate).id)"); await Shot(core,"properties");
        await Act("closeFeedbackDialog('feedbackPropertiesDialog');await openTerminate(apps.find(a=>a.canTerminate).id)"); await Shot(core,"terminate");
        await Act("closeFeedbackDialog('feedbackTerminateDialog');await openWebExit()"); await Shot(core,"exit");
        await Act("closeFeedbackDialog('feedbackExitDialog')");
        form.Hide();
        await (Task)typeof(MainForm).GetMethod("ShowWebTrayAsync", DashboardFlags)!.Invoke(form,new object[]{new Point(Screen.PrimaryScreen!.WorkingArea.Right-20,Screen.PrimaryScreen.WorkingArea.Bottom-10)})!;
        var trayCore=DashboardField<WebView2>(form,"webTrayView").CoreWebView2;await Shot(trayCore,"tray");
        await trayCore.ExecuteScriptAsync("window.trayCaptureBaseline=structuredClone(state);const e=state.entries[0];state={...state,entries:[{...e,hidden:false},{...e,id:'preview-second-icon',hidden:true,identity:'UID 42'}]};render();openGroup(e.path)");await Shot(trayCore,"group");
        await trayCore.ExecuteScriptAsync("$('#trayGroupDialog').close();groupPath='';state=window.trayCaptureBaseline;render()");await RunWebRulesScript(trayCore,"await openExit()");await Shot(trayCore,"tray-exit");
        await trayCore.ExecuteScriptAsync("$('#trayExitDialog').close()");DashboardCall(form,"HideWebTray");form.Show();
    }

    static int TestWebFeedbackBridge(string report)
    {
        report=Path.GetFullPath(report);var folder=Path.Combine(Path.GetDirectoryName(report)!,"web-feedback-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        using var owner=StartWebTestOwner(folder);var own=Scanner.Scan().Where(e=>e.Pid==owner.Id).ToList();if(own.Count!=2)throw new IOException("Controlled test icons unavailable");
        var controller=new Controller(Path.Combine(folder,"state"));controller.Saved.WelcomeDismissed=true;controller.Saved.RestoreIconsOnExit=false;
        string startupKey=@"Software\TrayPilot\Tests\"+Path.GetFileName(folder);var startup=new StartupRegistration(startupKey);
        using var form=new MainForm(controller,initialize:false,startup:startup){WebDiagnosticsSuspendScanning=true};typeof(MainForm).GetField("entries",DashboardFlags)!.SetValue(form,own);DashboardCall(form,"RenderList");
        var checks=new List<string>();Exception? failure=null;int exits=0,terminations=0;form.WebExitForDiagnostics=()=>exits++;form.WebTerminateForDiagnostics=e=>{terminations++;return Task.CompletedTask;};form.WebTrayKeepOpenForDiagnostics=true;
        form.Shown+=async(_,_)=>
        {
            try
            {
                await form.EnableWebInterfaceAsync();var core=form.Controls.OfType<WebView2>().Single(v => v.Name == "mainWebView").CoreWebView2;
                async Task Act(string js)=>await RunWebRulesScript(core,js);
                async Task Check(string js,string label){if(await core.ExecuteScriptAsync(js)!="true")throw new IOException(label+" · "+await core.ExecuteScriptAsync("JSON.stringify(state.operation)"));checks.Add(label);}
                string ids=JsonSerializer.Serialize(own.Select(e=>e.Key));
                await Act("await send('change',{ids:"+ids+",hidden:true})");
                await Check("apps.every(a=>a.hidden)&&state.operation.completed===2&&state.operation.canUndo", "Web batch hiding verifies actual test-owned UID/GUID states and exposes Undo.");
                if(own.Any(e=>Native.State(e)!=1)||owner.HasExited)throw new IOException("Actual hide did not keep the owner running");checks.Add("Actual icon hiding keeps the test-owned process running.");
                await Act("await send('visibilityUndo')");await Check("apps.every(a=>!a.hidden)&&!state.operation.canUndo&&state.operation.phase==='undone'", "Undo restores the actual visibility and clears the consumed undo record.");
                controller.AddRule(own[0].Path);await controller.ApplyAsync(own);DashboardCall(form,"UpdateEntryStates");
                await Act("await send('restore')");await Check("apps.every(a=>!a.hidden)&&state.ruleCatalog.length===1", "Restoring managed ordinary icons keeps automatic rules.");
                if(!own.All(controller.IsTemporarilyShown))throw new IOException("Restored icons must be temporary");checks.Add("Restored rule matches stay visible for this session.");
                await Act("await send('visibilityUndo')");if(own.Any(e=>Native.State(e)!=1)||own.Any(controller.IsTemporarilyShown))throw new IOException("Undo manual state mismatch");checks.Add("Undo restores hidden states and the previous manual visibility exceptions.");
                controller.RemoveRule(own[0].Path);await controller.ChangeManyAsync(own,false);DashboardCall(form,"UpdateEntryStates");
                int firstCalls=0,secondCalls=0;bool failSecond=true;
                form.WebVisibilityChangeForDiagnostics=async(e,hidden)=>{if(e.Key==own[0].Key)firstCalls++;else {secondCalls++;if(failSecond)throw new IOException("Test-only native failure");}await controller.ChangeManyAsync(new[]{e},hidden);};
                await Act("await send('change',{ids:"+ids+",hidden:true})");await Check("state.operation.phase==='failed'&&state.operation.total===2&&state.operation.completed===1&&state.operation.failed.length===1&&state.operation.canUndo&&state.operation.canRetry", "Partial failure reports actual completed and failed items independently.");
                failSecond=false;await Act("await send('visibilityRetry')");if(firstCalls!=1||secondCalls!=2||own.Any(e=>Native.State(e)!=1))throw new IOException("Retry touched completed item");checks.Add("Retry changes only the failed icon, keeping the already-completed icon untouched.");
                await Act("await send('visibilityUndo')");if(own.Any(e=>Native.State(e)!=0))throw new IOException("Retry undo missed original states");checks.Add("Undo after retry restores both original icon states.");
                form.WebVisibilityChangeForDiagnostics=null;
                Directory.CreateDirectory(Path.Combine(controller.SettingsFolder,"settings.json.tmp"));await Act("await send('change',{ids:"+ids+",hidden:true})");if(own.Any(e=>Native.State(e)!=0))throw new IOException("Save failure changed icons");await Check("state.operation.failed.length===2&&!state.operation.canUndo", "Recovery-journal save failure prevents hiding and creates no false undo record.");Directory.Delete(Path.Combine(controller.SettingsFolder,"settings.json.tmp"));
                await Act("await send('visibilityRetry')");if(own.Any(e=>Native.State(e)!=1))throw new IOException("Failed save retry failed");checks.Add("Retry after fixing persistence uses the retained failed targets.");
                await Act("await send('visibilityUndo');await openTerminate(apps[0].id)");await Check("$('#feedbackTerminateDialog').open&&document.activeElement.id==='feedbackTerminateCancel'", "Opening a termination confirmation does not execute termination.");
                if(terminations!=0||owner.HasExited)throw new IOException("Prepare terminated owner");
                await Act("await send('terminate',{id:feedbackTerminate.id,token:'invalid'}).then(()=>{throw Error('Invalid token accepted')},()=>{});closeFeedbackDialog('feedbackTerminateDialog')");if(terminations!=0)throw new IOException("Invalid token executed");checks.Add("Native termination rejects an unissued confirmation token.");
                await Act("await openTerminate(apps[0].id);hideInsteadOfTerminate();for(let i=0;i<60&&pending;i++)await new Promise(r=>setTimeout(r,50))");if(terminations!=0||Native.State(own[0])!=1)throw new IOException("Hide alternative terminated");checks.Add("The hide alternative changes only icon visibility and keeps the process alive.");
                await Act("await send('visibilityUndo');await openWebExit();closeFeedbackDialog('feedbackExitDialog')");if(exits!=0)throw new IOException("Cancel exited");checks.Add("Canceling Exit keeps the manager running.");
                await Act("await send('exitConfirm',{token:'invalid'}).then(()=>{throw Error('Invalid exit accepted')},()=>{});await openWebExit();await confirmWebExit()");if(exits!=1)throw new IOException("Exit confirmation missing");checks.Add("Exit executes only after an issued confirmation token is submitted.");
                await Act("await send('welcomeDismiss')");if(!new Controller(controller.SettingsFolder).Saved.WelcomeDismissed)throw new IOException("Welcome dismissal not saved");checks.Add("Getting-started dismissal persists without creating rules.");
                await Act("await send('trayStartup',{value:true})");if(!startup.Enabled)throw new IOException("Startup toggle missing");checks.Add("Tray startup uses the actual isolated startup registration backend.");
                await Act("await send('trayStartup',{value:false});await send('traySelf',{value:false})");if(startup.Enabled||new Controller(controller.SettingsFolder).Saved.ShowTrayIcon)throw new IOException("Tray preferences mismatch");checks.Add("Tray quick preferences persist and reflect their actual saved state.");
                await Act("await send('navigate',{page:'settings'});editPreference('closeToTray',false);await settingsSync;await openWebExit()");await Check("$('#prefDiscardDialog').open&&!$('#feedbackExitDialog').open", "Exiting with unsaved settings first protects the draft.");
                await Act("closePreferencesDialog('prefDiscardDialog');await send('trayStartup',{value:true}).then(()=>{throw Error('Draft startup accepted')},()=>{});await cancelSettings();await send('navigate',{page:'icons'})");if(startup.Enabled)throw new IOException("Draft startup changed");checks.Add("Tray preferences cannot overwrite an outstanding settings draft.");
                // Repeat-submission protection while the actual operation is waiting.
                var entered=new TaskCompletionSource();var release=new TaskCompletionSource();
                form.WebVisibilityChangeForDiagnostics=async(e,hide)=>{entered.TrySetResult();await release.Task;await controller.ChangeManyAsync(new[]{e},hide);};
                await core.ExecuteScriptAsync("void(async()=>{window.feedbackDelayed=null;try{await send('change',{ids:[apps[0].id],hidden:true});window.feedbackDelayed=true}catch(e){window.feedbackDelayed=e.message}})()");
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));await Act("await send('restore').then(()=>{throw Error('Concurrent restore accepted')},()=>{})");checks.Add("The native bridge rejects overlapping icon operations.");release.SetResult();for(int i=0;i<60&&DashboardField<bool>(form,"busy");i++)await Task.Delay(50);
                form.WebVisibilityChangeForDiagnostics=null;await Act("await send('visibilityUndo')");
                await Act("await send('traySelf',{value:true})");DashboardCall(form,"InitializeTray");form.Hide();
                var notify=DashboardField<NotifyIcon>(form,"trayIcon");var notifyWindow=(NativeWindow)typeof(NotifyIcon).GetField("_window",DashboardFlags)!.GetValue(notify)!;
                Native.SendMessageW(notifyWindow.Handle,0x0800,0,0x0204);Native.SendMessageW(notifyWindow.Handle,0x0800,0,0x0205);
                for(int i=0;i<100&&(!DashboardField<bool>(form,"webTrayReady")||!DashboardField<Form>(form,"webTrayPanel").Visible);i++)await Task.Delay(100);
                var panel=DashboardField<Form>(form,"webTrayPanel");var trayCore=DashboardField<WebView2>(form,"webTrayView").CoreWebView2;
                if(!panel.Visible||form.Visible)throw new IOException("Native right-click did not open the web tray panel alone");checks.Add("The first native tray right-click opens the WebView panel while the main window remains hidden.");
                await RunWebRulesScript(trayCore,"openGroup(state.entries[0].path)");if(await trayCore.ExecuteScriptAsync("$('#trayGroupEntries').children.length===2")!="true")throw new IOException("Tray group missing owner icons");checks.Add("The actual UID and GUID test icons appear as separate controls in one program group.");
                await RunWebRulesScript(trayCore,"$('#trayGroupDialog').close();await send('change',{ids:[state.entries[0].id],hidden:true})");if(own.Count(e=>Native.State(e)==1)!=1)throw new IOException("Tray single control changed group");checks.Add("Tray single-icon commands hide only their selected actual icon.");
                async Task CheckTrayResult(string phase, string label)
                {
                    if (await trayCore.ExecuteScriptAsync("state.operation.phase==='" + phase + "'&&!$('#trayFeedback').hidden&&!/正在处理|Updating/.test($('#trayFeedbackText').textContent)") != "true")
                        throw new IOException(label);
                    checks.Add(label);
                }
                await CheckTrayResult("done", "Actual tray hiding replaces the progress message with completion feedback.");
                await RunWebRulesScript(trayCore,"await send('change',{ids:[state.entries.find(e=>e.hidden).id],hidden:false})");
                if(own.Any(e=>Native.State(e)!=0))throw new IOException("Tray single restore failed");
                await CheckTrayResult("done", "Actual tray showing replaces the progress message with completion feedback.");
                await RunWebRulesScript(trayCore,"await send('change',{ids:[state.entries[0].id],hidden:true})");
                await RunWebRulesScript(trayCore,"await send('visibilityUndo')");if(own.Any(e=>Native.State(e)!=0))throw new IOException("Tray Undo failed");checks.Add("Tray Undo shares the actual visibility history with the main window.");
                await CheckTrayResult("undone", "Actual tray Undo replaces the progress message with undo feedback.");
                var monitor=Screen.FromControl(panel).WorkingArea;if(!monitor.Contains(panel.Bounds)||await trayCore.ExecuteScriptAsync("state.capacity>=1&&state.capacity<=10")!="true")throw new IOException("Tray geometry is outside monitor");checks.Add("Tray placement is constrained to the selected monitor and capacity is bounded by ten groups.");
                await RunWebRulesScript(trayCore,"await send('terminate',{id:state.entries[0].id,token:'invalid'}).then(()=>{throw Error('Unknown tray command accepted')},()=>{})");checks.Add("The tray bridge rejects actions outside its command whitelist.");
                string traySource=trayCore.Source;trayCore.Navigate("https://example.com/");await Task.Delay(150);if(trayCore.Source!=traySource)throw new IOException("Tray external navigation accepted");checks.Add("The independent tray WebView blocks external navigation.");
                Native.SendMessageW(notifyWindow.Handle,0x0800,0,0x0201);Native.SendMessageW(notifyWindow.Handle,0x0800,0,0x0202);await Task.Delay(150);if(!form.Visible||panel.Visible)throw new IOException("Native left click did not open main");checks.Add("Native left-click opens the main window and hides the quick panel.");
                // Actual test-owner process termination, after all restoration checks.
                form.WebTerminateForDiagnostics=null;
                bool staleRejected=false;try{await ProgramActions.EndAsync(own[0] with{Started=own[0].Started+1});}catch(InvalidOperationException){staleRejected=true;}
                if(!staleRejected||owner.HasExited)throw new IOException("Stale process identity was accepted");checks.Add("Actual process termination rejects a stale creation time without ending the owner.");
                await Act("await openTerminate("+JsonSerializer.Serialize(own[0].Key)+")");
                if(await core.ExecuteScriptAsync("feedbackTerminate.pid==="+owner.Id)!="true")throw new IOException("Termination target must be the diagnostic owner");
                await Act("await confirmWebTerminate()");if(!owner.HasExited)throw new IOException("Confirmed test-owner termination did not finish");checks.Add("Confirmed termination ends only the verified test-owner process and removes its icons.");
                await Act("for(let i=0;i<40&&(apps.length||$('#feedbackTerminateDialog').open);i++)await new Promise(r=>setTimeout(r,50))");
                File.WriteAllText(Path.Combine(folder,"termination-ui.json"),await core.ExecuteScriptAsync("JSON.stringify({apps:apps.map(a=>({id:a.id,name:a.name,pid:a.pid})),dialog:$('#feedbackTerminateDialog').open,error:$('#feedbackTerminateError').textContent})"));
                await Check("apps.length===0&&!$('#feedbackTerminateDialog').open", "Terminated program icons disappear without a stale detail or open confirmation.");
            }
            catch(Exception ex){failure=ex;}
            finally
            {
                try{await controller.ChangeManyAsync(own,false);}catch(Exception ex){failure??=ex;}
                File.WriteAllText(Path.Combine(folder,"stop"),"stop");form.CloseWebDiagnosticsWindow();
                Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(startupKey,false);
                File.WriteAllText(report+".completed.json",JsonSerializer.Serialize(new{checks,error=failure?.ToString()},new JsonSerializerOptions{WriteIndented=true}));
            }
        };
        Application.Run(form);if(failure!=null)throw failure;File.WriteAllText(report,JsonSerializer.Serialize(new{passed=true,checks,isolation="Actual test-owned tray icons, isolated files, simulated termination/exit and update calls"},new JsonSerializerOptions{WriteIndented=true}));return 0;
    }
}
