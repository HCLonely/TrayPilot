using Microsoft.Web.WebView2.Core;
using System.Text.Json;

namespace TrayPilot;

internal static partial class Diagnostics
{
    // Real WebView2 and real scanner, isolated settings; never hide icons or install rules.
    static int PreviewWebDashboard(string output, bool dark, bool rulesPreview = false, bool systemPreview = false, bool preferencesPreview = false, bool feedbackPreview = false)
    {
        output = Path.GetFullPath(output); Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var controller = new Controller(Path.Combine(Path.GetDirectoryName(output)!, "web-preview-" + Guid.NewGuid().ToString("N")));
        controller.Saved.Theme = dark ? "dark" : "light"; controller.Saved.Language = "zh-CN";
        var snapshot = Scanner.Scan().Where(e => e.Pid != Environment.ProcessId).ToList();
        if (rulesPreview) SeedWebRulesPreview(controller, snapshot);
        using var form = new MainForm(controller, initialize: false) { WebReadOnlyPreview = true };
        if (systemPreview) form.WebSystemReadForDiagnostics = () => SystemDashboardSample();
        if (preferencesPreview) form.WebStartupReadForDiagnostics = () => Task.FromResult(false);
        form.PrepareWebStartup();
        typeof(MainForm).GetField("entries", DashboardFlags)!.SetValue(form, snapshot);
        DashboardCall(form, "RenderList");
        Exception? failure = null;
        form.Shown += async (_, _) =>
        {
            try
            {
                var startupSurface = form.Controls.OfType<WebStartupSurface>().Single();
                if (!startupSurface.Visible || form.Controls.GetChildIndex(startupSurface) != 0)
                    throw new InvalidOperationException("The first visible frame must contain the startup surface, not the legacy UI.");
                using (var startupImage = new Bitmap(startupSurface.Width, startupSurface.Height))
                {
                    startupSurface.DrawToBitmap(startupImage, startupSurface.ClientRectangle);
                    startupImage.Save(Path.Combine(Path.GetDirectoryName(output)!, "web-startup-" + (dark ? "dark" : "light") + ".png"));
                }
                // Exercise readiness while hidden too, as in startup-to-tray mode.
                if (dark) form.Hide();
                await form.EnableWebInterfaceAsync();
                await form.WebLoaded.Task.WaitAsync(TimeSpan.FromSeconds(25));
                if (dark) form.Show();
                var view = form.Controls.OfType<Microsoft.Web.WebView2.WinForms.WebView2>().Single(v => v.Name == "mainWebView");
                var core = view.CoreWebView2;
                if (!view.Visible || form.Controls.OfType<WebStartupSurface>().Any())
                    throw new InvalidOperationException("The first rendered web frame must replace the startup surface without revealing the legacy UI.");
                async Task<JsonElement> Eval(string script)
                {
                    var result = await core.ExecuteScriptAsync(script.Contains("await ") ? "void(async()=>{" + script + "})()" : script);
                    if (script.Contains("command(")) await Task.Delay(250);
                    return JsonSerializer.Deserialize<JsonElement>(result);
                }
                async Task Check(string script, string label)
                { if (!(await Eval(script)).GetBoolean()) throw new InvalidOperationException(label); }
                await Task.Delay(250);
                await Check("document.querySelectorAll('.entry').length === apps.length", "Real snapshot rendered.");
                await Check("Array.from(document.images).every(i=>i.complete && i.naturalWidth>0)", "Real program icons loaded.");
                await Check("document.documentElement.scrollWidth <= innerWidth", "No horizontal overflow.");
                string[] refreshChecks = await CheckWebRefreshStability(core);
                await Eval("document.querySelector('#search').value='__unmatched__';render()");
                await Check("document.querySelectorAll('.entry').length===0 && document.querySelector('.empty')!==null", "Search empty state.");
                await Eval("clearSearch();setView(true);setView(false);filter('hidden');filter('all')");
                await Eval("sortBy('name');sortBy('hidden');sortBy('rule')");
                await Eval("document.dispatchEvent(new KeyboardEvent('keydown',{key:'!',code:'Digit1',ctrlKey:true,shiftKey:true,bubbles:true}))");
                await Check("sortKey==='name'", "Shifted number shortcut sorts the web list.");
                if ((await Eval("apps.length>0")).GetBoolean())
                {
                    await Eval("document.querySelector('#selectAll').checked=true;document.querySelector('#selectAll').dispatchEvent(new Event('change'))");
                    await Check("selected.size===apps.length", "Select-all uses real keys.");
                    await Eval("document.querySelector('#selectAll').checked=false;document.querySelector('#selectAll').dispatchEvent(new Event('change'));window.before=JSON.stringify(apps.map(a=>a.hidden));await command('change',{ids:[apps[0].id],hidden:!apps[0].hidden})");
                    await Check("JSON.stringify(apps.map(a=>a.hidden))===window.before && document.querySelector('#toast').classList.contains('error')", "Read-only preview rejects mutations.");
                }
                await Eval("await command('unknown');");
                await Check("document.querySelector('#toast').classList.contains('error')", "Unknown native command rejected.");
                await Eval("await command('theme',{value:'invalid'})");
                await Check("document.documentElement.dataset.theme===" + JsonSerializer.Serialize(dark ? "dark" : "light"), "Invalid theme rejected.");
                await Eval("await command('theme',{value:" + JsonSerializer.Serialize(dark ? "light" : "dark") + "});");
                await Check("document.documentElement.dataset.theme===" + JsonSerializer.Serialize(dark ? "light" : "dark"), "Native theme bridge acknowledged.");
                await Eval("await command('theme',{value:" + JsonSerializer.Serialize(dark ? "dark" : "light") + "});sortKey='name';descending=false;current=apps.find(a=>a.proc.toLowerCase().includes('wechat'))?.id ?? apps.find(a=>!a.hidden&&a.proc!=='explorer.exe')?.id ?? current;document.querySelector('#toast').classList.remove('show');render()");
                await Eval("await command('navigate',{page:'rules'})");
                if (!view.Visible) throw new InvalidOperationException("Rules navigation must keep the web overlay visible.");
                await Check("state.page==='rules' && !document.querySelector('#rulesPage').hidden && document.querySelector('#iconsPage').hidden", "Rules navigation opens the web page.");
                DashboardCall(form, "ShowIconsPage");
                if (!view.Visible) throw new InvalidOperationException("Returning to icons did not restore the web overlay.");
                await Check("mode==='all' && sortKey==='name'", "Navigation preserves web page state.");
                form.ClientSize = new Size(1004, 701); await Task.Delay(100);
                await Check("document.documentElement.scrollWidth<=innerWidth", "Minimum window width fits.");
                form.Size = new Size(1380, 920); await Task.Delay(100);
                string before = core.Source; core.Navigate("https://example.com/"); await Task.Delay(150);
                if (core.Source != before) throw new InvalidOperationException("External navigation was not blocked.");
                string[] ruleChecks = rulesPreview ? await CheckWebRulesUi(core, form) : Array.Empty<string>();
                string[] systemChecks = systemPreview ? await CheckWebSystemUi(core, form) : Array.Empty<string>();
                string[] preferencesChecks = preferencesPreview ? await CheckWebPreferencesUi(core, form) : Array.Empty<string>();
                string[] feedbackChecks = feedbackPreview ? await CheckWebFeedbackUi(core, form) : Array.Empty<string>();
                await Task.Delay(200);
                using (var stream = File.Create(output)) await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream);
                if (rulesPreview)
                {
                    await Eval("openRuleEditor(null,apps.find(a=>a.proc.toLowerCase().includes('wechat')&&!ruleCatalog().some(r=>r.path.toLowerCase()===a.path.toLowerCase()))?.id??apps.find(a=>a.proc!=='explorer.exe'&&!ruleCatalog().some(r=>r.path.toLowerCase()===a.path.toLowerCase()))?.id);document.querySelector('#toast').classList.remove('show')");
                    await Task.Delay(350);
                    using (var stream = File.Create(Path.Combine(Path.GetDirectoryName(output)!, "web-02-editor-" + (dark ? "dark" : "light") + ".png")))
                        await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream);
                    await Eval("closeRuleDialog('ruleEditor')");
                }
                if (systemPreview) { await CaptureWebSystemDialogs(core, output, dark); await CaptureWebSystemStates(core, form, output, dark); }
                if (preferencesPreview) await CaptureWebPreferencesViews(core, form, output, dark);
                if (feedbackPreview) await CaptureWebFeedbackViews(core, form, output, dark);
                int realIcons = (await Eval("apps.length")).GetInt32();
                typeof(MainForm).GetMethod("ShowWebStartupError", DashboardFlags)!.Invoke(form, new object[] { new IOException("Simulated runtime failure") });
                var errorSurface = form.Controls.OfType<WebStartupSurface>().Single();
                if (view.Visible || errorSurface.Error == null || !errorSurface.Controls.OfType<Button>().Any())
                    throw new InvalidOperationException("Runtime failure must offer a retry without a legacy interface.");
                errorSurface.Controls.OfType<Button>().Single().PerformClick();
                await form.WebLoaded.Task.WaitAsync(TimeSpan.FromSeconds(25));
                if (form.Controls.OfType<WebStartupSurface>().Any() || !form.Controls.OfType<Microsoft.Web.WebView2.WinForms.WebView2>().Single(v => v.Name == "mainWebView").Visible)
                    throw new InvalidOperationException("Retry must recreate and present the web interface.");
                File.WriteAllText(output + ".json", JsonSerializer.Serialize(new { passed = true, readOnly = true, realIcons = realIcons, theme = dark ? "dark" : "light",
                    checks = new[] { "startup surface provides first visible frame", "first rendered web frame replaces startup surface", "runtime failure offers retry without legacy UI", "real WebView2", "real scanner", "real program images", "search", "grid and list", "visibility filters", "sorting", "select-all", "read-only operation guard", "unknown command rejection", "theme validation", "native theme bridge", "web rules navigation", "web state preserved on return", "minimum width", "external navigation blocked", "offline embedded assets" }.Concat(refreshChecks).Concat(ruleChecks).Concat(systemChecks).Concat(preferencesChecks).Concat(feedbackChecks).Concat(dark ? new[] { "hidden startup reaches presentation readiness before window opens" } : Array.Empty<string>()) }, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex) { failure = ex; }
            finally { if (failure != null) File.WriteAllText(output + ".error.txt", failure.ToString()); form.CloseWebDiagnosticsWindow(); }
        };
        Application.Run(form);
        if (failure != null) throw failure;
        return 0;
    }

    static async Task<string[]> CheckWebRefreshStability(CoreWebView2 core)
    {
        // Synthetic snapshots exercise DOM updates only; native icon state is untouched.
        string result = await core.ExecuteScriptAsync("""
            (()=>{
              const original=structuredClone(state), saved={current,selected:[...selected],mode,grid,sortKey,descending};
              const checks={};
              try {
                mode='all';grid=false;sortKey='name';descending=false;$('#entries').classList.remove('grid');
                const seed=original.entries[0]??{image:'',darkPlate:false,path:'C:/test.exe',proc:'test.exe',rule:'',hidden:false,tooltip:''};
                const fixture=structuredClone(original);
                fixture.entries=Array.from({length:60},(_,i)=>({...seed,id:'refresh-test-'+i,name:'App '+String(i).padStart(3,'0')}));
                receive(fixture);current=fixture.entries[12].id;selected=new Set([current]);render();
                const container=$('#entries');container.scrollTop=600;
                const row=rowNodes.get(current), image=row.querySelector('img'), checkbox=row.querySelector('input');
                const copy=$('#detail .pathbox button');checkbox.focus({preventScroll:true});
                const scroll=container.scrollTop;
                const observer=new MutationObserver(()=>{});observer.observe(document.body,{subtree:true,childList:true,attributes:true,characterData:true});
                for(let i=0;i<20;i++)receive(structuredClone(fixture));
                const mutations=observer.takeRecords();window.refreshMutationDetails=mutations.map(m=>({type:m.type,id:m.target.id,class:m.target.className,attribute:m.attributeName}));
                checks['Unchanged refresh makes zero DOM mutations']=mutations.length===0;observer.disconnect();
                checks['Refresh preserves row and image identity']=rowNodes.get(current)===row&&row.querySelector('img')===image;
                checks['Refresh preserves checkbox, focus and scroll']=checkbox.checked&&document.activeElement===checkbox&&container.scrollTop===scroll;
                const changed=structuredClone(fixture);changed.entries[12].hidden=!changed.entries[12].hidden;receive(changed);
                checks['Changed icon patches its row without replacing controls']=rowNodes.get(current)===row&&row.querySelector('img')===image&&$('#detail .pathbox button')===copy&&checkbox.checked&&document.activeElement===checkbox;
                const firstVisible=()=>[...container.children].find(e=>e.getBoundingClientRect().bottom>container.getBoundingClientRect().top);
                const anchor=firstVisible(), offset=()=>anchor.getBoundingClientRect().top-container.getBoundingClientRect().top, before=offset();
                const inserted=structuredClone(changed);inserted.entries.unshift({...seed,id:'refresh-new-1',name:'000 First'},{...seed,id:'refresh-new-2',name:'000 Second'});receive(inserted);
                checks['New icons above viewport preserve visible scroll anchor']=Math.abs(offset()-before)<1;
                receive(changed);
                checks['Removed icons above viewport preserve visible scroll anchor']=Math.abs(offset()-before)<1;
                const bounds=()=>{const r=container.getBoundingClientRect();return JSON.stringify([r.x,r.y,r.width,r.height])};
                const normalBounds=bounds();pending=1;pendingAction='change';updateBusy();
                checks['Operation progress does not shift list layout']=bounds()===normalBounds&&$('#busyNote').classList.contains('show');
                pendingAction='refresh';updateBusy();
                checks['Refresh has no layout-changing progress banner']=bounds()===normalBounds&&!$('#busyNote').classList.contains('show');
              } finally {
                pending=0;pendingAction='';mode=saved.mode;grid=saved.grid;sortKey=saved.sortKey;descending=saved.descending;
                $('#entries').classList.toggle('grid',grid);receive(original);current=saved.current;selected=new Set(saved.selected);render();$('#entries').scrollTop=0;
              }
              return checks;
            })()
            """);
        var checks = JsonSerializer.Deserialize<Dictionary<string, bool>>(result)!;
        foreach (var check in checks) if (!check.Value) throw new InvalidOperationException(check.Key + " " + await core.ExecuteScriptAsync("JSON.stringify(window.refreshMutationDetails)"));
        return checks.Keys.ToArray();
    }

    static int TestWebDashboardLive(string report, bool rulesTest = false)
    {
        report = Path.GetFullPath(report);
        var folder = Path.Combine(Path.GetDirectoryName(report)!, "web-bridge-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        using var owner = StartWebTestOwner(folder);
        var own = Scanner.Scan().Where(e => e.Pid == owner.Id).ToList();
        if (own.Count != 2) throw new InvalidOperationException("Controlled test icons unavailable.");
        var controller = new Controller(Path.Combine(folder, "state"));
        using var form = new MainForm(controller, initialize: false);
        typeof(MainForm).GetField("entries", DashboardFlags)!.SetValue(form, own);
        DashboardCall(form, "RenderList");
        Exception? failure = null;
        var checks = new List<string>();
        void Check(bool value, string label) { if (!value) throw new InvalidOperationException(label); checks.Add(label); }
        form.Shown += async (_, _) =>
        {
            try
            {
                await form.EnableWebInterfaceAsync(); await form.WebLoaded.Task.WaitAsync(TimeSpan.FromSeconds(25));
                var core = form.Controls.OfType<Microsoft.Web.WebView2.WinForms.WebView2>().Single(v => v.Name == "mainWebView").CoreWebView2;
                async Task Request(string action, object data)
                {
                    string json = JsonSerializer.Serialize(data);
                    await core.ExecuteScriptAsync("void(async()=>{window.liveResult=null;try{await send(" + JsonSerializer.Serialize(action) + "," + json + ");window.liveResult={ok:true}}catch(e){window.liveResult={ok:false,error:e.message}}})()");
                    for (int i = 0; i < 150; i++)
                    {
                        await Task.Delay(100);
                        string result = await core.ExecuteScriptAsync("window.liveResult");
                        if (result == "null") continue;
                        var root = JsonSerializer.Deserialize<JsonElement>(result);
                        if (!root.GetProperty("ok").GetBoolean()) throw new IOException(action + " " + json + ": " + root.GetProperty("error").GetString() + " · live=" + await core.ExecuteScriptAsync("JSON.stringify(apps.map(a=>({id:a.id,pid:a.pid})))"));
                        return;
                    }
                    throw new TimeoutException("Native bridge response timed out.");
                }
                await Request("change", new { ids = new[] { own[0].Key }, hidden = true });
                Check(Native.State(own[0]) == 1 && Native.State(own[1]) == 0, "Single-icon hide affects only the requested test icon.");
                Check(!owner.HasExited, "Hiding leaves the owning process running.");
                await Request("change", new { ids = new[] { own[0].Key }, hidden = false });
                Check(Native.State(own[0]) == 0, "Single-icon restore uses the native controller.");
                await Request("change", new { ids = own.Select(e => e.Key).ToArray(), hidden = true });
                Check(own.All(e => Native.State(e) == 1), "Batch hide reaches both controlled test icons.");
                await Request("restore", new { });
                Check(own.All(e => Native.State(e) == 0), "Restore managed icons restores the controlled recovery journal.");
                Check(controller.Saved.HiddenPaths.Count == 0 && controller.Saved.HiddenIcons.Count == 0, "Visibility operations do not create rules.");
                Check(controller.Saved.Recovery.Count == 0, "Successful restoration clears recovery records.");
                await Request("theme", new { value = "dark" });
                Check(new Controller(Path.Combine(folder, "state")).Saved.Theme == "dark", "Theme bridge persists the real preference to isolated settings.");
                await core.ExecuteScriptAsync("void(async()=>{window.staleRejected=false;try{await send('change',{ids:['stale-id'],hidden:true})}catch{window.staleRejected=true}})()");
                await Task.Delay(250);
                Check(await core.ExecuteScriptAsync("window.staleRejected") == "true", "Stale icon identities are rejected.");
                if (rulesTest) await CheckWebRulesLive(core, form, controller, own, Request, Check);
                File.WriteAllText(report, JsonSerializer.Serialize(new { passed = true, checks, targets = "Only test-owned UID/GUID icons" }, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex) { failure = ex; }
            finally
            {
                File.WriteAllText(Path.Combine(folder, "completed-checks.json"), JsonSerializer.Serialize(checks));
                try { await controller.ChangeManyAsync(own, false); } catch (Exception ex) { failure ??= ex; }
                File.WriteAllText(Path.Combine(folder, "stop"), "stop");
                form.CloseWebDiagnosticsWindow();
            }
        };
        Application.Run(form);
        if (failure != null) throw failure;
        return 0;
    }

    static System.Diagnostics.Process StartWebTestOwner(string folder)
    {
        if (!File.Exists(Path.ChangeExtension(Environment.ProcessPath!, ".dll"))) return StartHost(folder);
        // Give the controlled owner a distinct executable path in framework-dependent
        // builds too, so the scanner cannot assign its GUID to the manager's window.
        string directory = Path.Combine(folder, "owner"); Directory.CreateDirectory(directory);
        string original = Path.GetDirectoryName(Environment.ProcessPath!)!;
        foreach (string file in Directory.EnumerateFiles(original).Where(f => !f.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)))
            File.Copy(file, Path.Combine(directory, Path.GetFileName(file)), overwrite: true);
        string executable = Path.Combine(directory, "TrayPilot-TestOwner.exe");
        File.Copy(Environment.ProcessPath!, executable, overwrite: true);
        var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(executable)
            { UseShellExecute = false, CreateNoWindow = true, ArgumentList = { "--test-host", folder } })!;
        for (int i = 0; i < 100 && !File.Exists(Path.Combine(folder, "ready")); i++) Thread.Sleep(100);
        if (!File.Exists(Path.Combine(folder, "ready"))) throw new IOException("Test owner did not start.");
        return process;
    }
}
