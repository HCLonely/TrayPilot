using Microsoft.Web.WebView2.Core;
using System.Text.Json;

namespace TrayPilot;

internal static partial class Diagnostics
{
    static void SeedWebRulesPreview(Controller controller, List<TrayEntry> entries)
    {
        var candidates = entries.Where(e => !Scanner.IsShellEntry(e)).DistinctBy(e => e.Path, StringComparer.OrdinalIgnoreCase).ToList();
        var targets = new List<TrayEntry>();
        if (candidates.Count > 0) targets.Add(candidates.FirstOrDefault(e => e.State == 1) ?? candidates[0]);
        if (candidates.Count > 1) targets.Add(candidates.FirstOrDefault(e => e.State == 0 && !Controller.SamePath(e.Path, targets[0].Path))
            ?? candidates.First(e => !Controller.SamePath(e.Path, targets[0].Path)));
        if (candidates.Count > 2) targets.Add(candidates.First(e => targets.All(t => !Controller.SamePath(t.Path, e.Path))));
        if (targets.Count == 0) return;
        controller.Saved.HiddenPaths.Add(targets[0].Path);
        if (targets.Count > 1)
        {
            controller.Saved.HiddenIcons.Add(IconRule.From(targets[1]));
            controller.SetManualVisibility(targets[1], false);
        }
        if (targets.Count > 2)
        {
            controller.Saved.HiddenPaths.Add(targets[2].Path);
            // Only the preview snapshot omits this live process, to demonstrate
            // a saved rule waiting for a future scan. No native state is changed.
            entries.RemoveAll(e => Controller.SamePath(e.Path, targets[2].Path));
        }
    }

    static async Task RunWebRulesScript(CoreWebView2 core, string body)
    {
        await core.ExecuteScriptAsync("void(async()=>{window.ruleDiagnosticResult=null;try{" + body
            + ";window.ruleDiagnosticResult={ok:true}}catch(e){window.ruleDiagnosticResult={ok:false,error:e.message}}})()");
        for (int i = 0; i < 150; i++)
        {
            await Task.Delay(100);
            var result = JsonSerializer.Deserialize<JsonElement>(await core.ExecuteScriptAsync("window.ruleDiagnosticResult"));
            if (result.ValueKind == JsonValueKind.Null) continue;
            if (!result.GetProperty("ok").GetBoolean()) throw new IOException(result.GetProperty("error").GetString());
            return;
        }
        throw new TimeoutException("Web rules script did not complete.");
    }

    static async Task<string[]> CheckWebRulesUi(CoreWebView2 core, MainForm form)
    {
        var checks = new List<string>();
        async Task Check(string script, string label)
        { if (await core.ExecuteScriptAsync(script) != "true") throw new InvalidOperationException(label + " · " + await core.ExecuteScriptAsync("JSON.stringify({page:state.page,draft:ruleDraft(),valid:ruleEditorValid,error:$('#ruleError').textContent,focus:document.activeElement.id})")); checks.Add(label); }
        async Task Act(string script) { await core.ExecuteScriptAsync(script); await Task.Delay(220); }
        await RunWebRulesScript(core, "await send('navigate',{page:'rules'})");
        await Check("!$('#rulesPage').hidden && $('#iconsPage').hidden", "Rule navigation keeps one WebView and changes the active page.");
        await Check("document.querySelectorAll('.rule-entry').length===ruleCatalog().length && [...$('#rulesPage').querySelectorAll('img')].every(i=>i.complete&&i.naturalWidth>0)", "Rules use native catalog identities and real program images.");
        await Act("$('#ruleSearch').value='__no_matching_rule__';renderRules()");
        await Check("$('#ruleEntries .empty')!==null && !$('#ruleEntries .rule-entry')", "Rules search has a useful empty result.");
        await Act("clearRuleSearch();filterRules('single')");
        await Check("[...$('#ruleEntries').children].filter(e=>e.dataset.id).every(e=>ruleCatalog().find(r=>r.id===e.dataset.id).scope==='single')", "Rule scope filters select single-icon rules.");
        await Act("filterRules('program');sortRules('state');filterRules('all');sortRules('name')");
        await Act("$('#ruleSelectAll').checked=true;$('#ruleSelectAll').dispatchEvent(new Event('change'))");
        await Check("ruleSelected.size===ruleCatalog().length", "Rule select-all uses stable rule identities.");
        await Act("$('#ruleSelectAll').checked=false;$('#ruleSelectAll').dispatchEvent(new Event('change'))");
        var stability = JsonSerializer.Deserialize<Dictionary<string, bool>>(await core.ExecuteScriptAsync("""
            (()=>{
              const original=structuredClone(state),checks={};
              const seed=original.ruleCatalog[0]??{name:'Test',proc:'test.exe',path:'C:/test.exe',scope:'program',state:'waiting',identity:'',liveCount:0,hiddenCount:0,image:original.entries[0]?.image??'',darkPlate:false};
              const fixture=structuredClone(original);fixture.ruleCatalog=Array.from({length:40},(_,i)=>({...seed,id:'rule-fixture-'+i,name:'App '+String(i).padStart(3,'0')}));fixture.rules=40;
              receive(fixture);ruleCurrent=fixture.ruleCatalog[12].id;ruleSelected=new Set([ruleCurrent]);renderRules();
              const container=$('#ruleEntries');container.scrollTop=600;
              const row=ruleNodes.get(ruleCurrent),image=row.querySelector('img'),checkbox=row.querySelector('input'),detail=$('#ruleDetail .path');
              checkbox.focus({preventScroll:true});const scroll=container.scrollTop;
              const observer=new MutationObserver(()=>{});observer.observe(document.body,{subtree:true,childList:true,attributes:true,characterData:true});
              for(let i=0;i<20;i++)receive(structuredClone(fixture));
              checks['Unchanged rules refresh makes zero DOM mutations']=observer.takeRecords().length===0;observer.disconnect();
              const changed=structuredClone(fixture);changed.ruleCatalog[12].state='temporary';receive(changed);
              checks['Rule state updates preserve row, image, checkbox, focus and details']=ruleNodes.get(ruleCurrent)===row&&row.querySelector('img')===image&&checkbox.checked&&document.activeElement===checkbox&&$('#ruleDetail .path')===detail&&container.scrollTop===scroll;
              const anchor=[...container.children].find(e=>e.getBoundingClientRect().bottom>container.getBoundingClientRect().top),offset=()=>anchor.getBoundingClientRect().top-container.getBoundingClientRect().top,before=offset();
              const inserted=structuredClone(changed);inserted.ruleCatalog.unshift({...seed,id:'rule-added',name:'000 Added'});inserted.rules++;receive(inserted);
              checks['New rules above viewport preserve visible scroll anchor']=Math.abs(offset()-before)<1;
              receive(changed);checks['Removed rules above viewport preserve visible scroll anchor']=Math.abs(offset()-before)<1;
              const savedScroll=container.scrollTop;receive({...structuredClone(changed),page:'icons'});receive(structuredClone(changed));
              checks['Page navigation preserves rule scroll and selection']=container.scrollTop===savedScroll&&ruleSelected.has(ruleCurrent);
              const empty=structuredClone(original);empty.ruleCatalog=[];empty.rules=0;receive(empty);
              checks['No saved rules has a create-rule entry point']=$('#ruleEntries .empty button')!==null;
              receive(original);ruleSelected=new Set();ruleSortKey='name';ruleDescending=false;ruleMode='all';renderRules();container.scrollTop=0;
              return checks;
            })()
            """))!;
        foreach (var check in stability) { if (!check.Value) throw new InvalidOperationException(check.Key); checks.Add(check.Key); }

        await Act("openRuleEditor(ruleCatalog().find(r=>r.scope==='program'&&r.liveCount>0)?.id)");
        await Check("$('#ruleEditor').open && $('#ruleEditor').contains(document.activeElement) && ruleEditorValid", "Editing a live rule validates through the native backend and contains focus.");
        await Check("[...$('#ruleTarget').options].every(o=>apps.find(a=>a.id===o.value)?.path.toLowerCase()===ruleCatalog().find(r=>r.id===ruleEditing).path.toLowerCase())", "Editing locks the target program path.");
        await Act("closeRuleDialog('ruleEditor');openRuleEditor(null,apps.find(a=>ruleCatalog().some(r=>r.scope==='program'&&r.path.toLowerCase()===a.path.toLowerCase()))?.id)");
        await Check("!ruleEditorValid && $('#ruleError').textContent.length>0 && $('#ruleTarget').getAttribute('aria-invalid')==='true'", "Overlapping rules show a native inline error and preserve the draft.");
        await Act("closeRuleDialog('ruleEditor');openRuleEditor(null,apps.find(a=>!ruleCatalog().some(r=>r.path.toLowerCase()===a.path.toLowerCase()))?.id)");
        await Check("ruleEditorValid && !$('#ruleSave').disabled && [...$('#ruleChosenApp').querySelectorAll('img')].every(i=>i.naturalWidth>0)", "New-rule default is a validated single icon with its actual image.");
        await RunWebRulesScript(core, "window.editorDraftBefore=JSON.stringify(ruleDraft());await saveWebRule({preventDefault(){}})");
        await Check("$('#ruleEditor').open && JSON.stringify(ruleDraft())===window.editorDraftBefore && $('#ruleError').textContent.length>0", "Read-only save failure keeps the editor and draft instead of reporting success.");
        await Act("closeRuleDialog('ruleEditor');$('#ruleSearch').focus();openRuleDelete([ruleCatalog()[0].id])");
        await Check("$('#ruleDeleteDialog').open && !$('#ruleRestoreAfterDelete').checked && $('#ruleDeleteDialog').contains(document.activeElement)", "Delete confirmation defaults to retaining hidden icon state and contains focus.");
        await Act("closeRuleDialog('ruleDeleteDialog')");
        await Check("document.activeElement===$('#ruleSearch')", "Closing rule dialogs returns focus to the invoking control.");
        form.ClientSize = new Size(1004, 701);
        await Act("openRuleEditor(null,apps.find(a=>!ruleCatalog().some(r=>r.path.toLowerCase()===a.path.toLowerCase()))?.id)");
        await Check("document.documentElement.scrollWidth<=innerWidth && $('#ruleEditor').getBoundingClientRect().bottom<=innerHeight && $('#ruleEditor').getBoundingClientRect().top>=0", "Rules and editor fit the minimum window size.");
        await Act("closeRuleDialog('ruleEditor');receive({...structuredClone(state),language:'en-US'});renderRules()");
        await Check("$('#rulesPage .top h2').textContent==='Hide rules' && document.documentElement.scrollWidth<=innerWidth", "English rule labels fit without horizontal overflow.");
        form.Size = new Size(1380, 920);
        await Act("receive({...structuredClone(state),language:'zh-CN'});$('#ruleSearch').value='';ruleMode='all';ruleSortKey='name';ruleDescending=false;ruleSelected=new Set();ruleCurrent=ruleCatalog().find(r=>r.scope==='single')?.id??ruleCatalog()[0]?.id;renderRules();$('#toast').classList.remove('show');$('#ruleEntries').scrollTop=0;document.activeElement.blur()");
        return checks.ToArray();
    }

    static async Task CheckWebRulesLive(CoreWebView2 core, MainForm form, Controller controller, List<TrayEntry> own,
        Func<string, object, Task> request, Action<bool, string> check)
    {
        async Task Reject(string action, object data, string label)
        {
            bool rejected = false;
            try { await request(action, data); } catch (IOException) { rejected = true; }
            check(rejected, label);
        }
        await request("navigate", new { page = "rules" });
        await request("ruleSave", new { originalId = (string?)null, targetId = own[0].Key, scope = "single" });
        var single = RuleEditing.Catalog(controller).Single();
        check(!single.Program && Native.State(own[0]) == 1 && Native.State(own[1]) == 0, "Creating a single-icon rule hides only the requested controlled icon.");
        check(new Controller(controller.SettingsFolder).Saved.HiddenIcons.Count == 1, "Web rule save persists the real single-icon identity.");
        await request("ruleSave", new { originalId = single.Key, targetId = own[0].Key, scope = "program" });
        var program = RuleEditing.Catalog(controller).Single();
        check(program.Program && own.All(e => Native.State(e) == 1), "Editing scope to the program matches both controlled UID/GUID icons.");
        await Reject("ruleSave", new { originalId = (string?)null, targetId = own[1].Key, scope = "single" }, "The web bridge rejects an overlapping rule without changing saved rules.");
        check(RuleEditing.Catalog(controller).Count == 1, "Rejected overlapping rules preserve the existing catalog.");
        await request("ruleTemporary", new { id = program.Key });
        check(own.All(e => Native.State(e) == 0 && controller.IsTemporarilyShown(e)), "Temporary visibility restores matching icons and retains their saved rule.");
        await request("rulePause", new { value = true });
        check(controller.Saved.RulesPaused && own.All(e => Native.State(e) == 0), "Pausing preserves the current native icon states.");
        await request("rulePause", new { value = false });
        check(!controller.Saved.RulesPaused && own.All(e => Native.State(e) == 0), "Resuming auto-hide preserves temporary visibility exceptions.");
        await request("ruleApply", new { });
        check(own.All(e => Native.State(e) == 1 && !controller.IsTemporarilyShown(e)), "Apply now clears temporary exceptions and hides matching test icons.");

        string blocker = Path.Combine(controller.SettingsFolder, "settings.json.tmp");
        Directory.CreateDirectory(blocker);
        try
        {
            await Reject("ruleSave", new { originalId = program.Key, targetId = own[0].Key, scope = "single" }, "A forced settings write failure is reported through the web bridge.");
            check(RuleEditing.Catalog(controller).Single().Program, "A settings write failure rolls back the requested rule scope.");
            await Reject("rulePause", new { value = true }, "A forced pause-save failure is reported.");
            check(!controller.Saved.RulesPaused, "A pause-save failure restores the previous pause preference.");
        }
        finally { Directory.Delete(blocker); }

        await request("ruleDelete", new { ids = new[] { program.Key }, restore = false });
        check(RuleEditing.Catalog(controller).Count == 0 && own.All(e => Native.State(e) == 1), "Default deletion removes the rule and retains current hidden icon states.");
        await request("ruleUndo", new { });
        check(RuleEditing.Catalog(controller).Count == 1 && own.All(e => Native.State(e) == 1), "Undo restores the deleted rule using the native rule snapshot.");
        await request("ruleDelete", new { ids = new[] { program.Key }, restore = true });
        check(own.All(e => Native.State(e) == 0) && controller.Saved.Recovery.Count == 0, "Optional delete-and-restore restores only matched test icons and clears recovery.");

        await request("rulePause", new { value = true });
        await request("ruleSave", new { originalId = (string?)null, targetId = own[1].Key, scope = "single" });
        single = RuleEditing.Catalog(controller).Single();
        check(own.All(e => Native.State(e) == 0), "Saving while paused persists the rule without hiding any icon.");
        typeof(MainForm).GetField("entries", DashboardFlags)!.SetValue(form, new List<TrayEntry>());
        DashboardCall(form, "UpdateDashboard");
        await request("ruleSave", new { originalId = single.Key, targetId = (string?)null, scope = "single" });
        check(RuleEditing.Catalog(controller).Single().Icon == single.Icon, "An existing single-icon rule can retain its identity without a live snapshot.");
        await request("ruleSave", new { originalId = single.Key, targetId = (string?)null, scope = "program" });
        program = RuleEditing.Catalog(controller).Single();
        await Reject("ruleSave", new { originalId = program.Key, targetId = (string?)null, scope = "single" }, "Converting an offline program rule to a single icon requires a live target.");
        await Reject("ruleSave", new { originalId = (string?)null, targetId = "stale-icon", scope = "program" }, "New web rules reject stale target identities.");
        await Reject("ruleSave", new { originalId = program.Key, targetId = (string?)null, scope = "invalid" }, "The rule bridge rejects unknown scope values.");
        typeof(MainForm).GetField("entries", DashboardFlags)!.SetValue(form, own.Select(e => e with { State = Native.State(e) }).ToList());
        DashboardCall(form, "UpdateDashboard");
        await request("ruleDelete", new { ids = new[] { program.Key }, restore = true });
        check(RuleEditing.Catalog(controller).Count == 0 && own.All(e => Native.State(e) == 0), "Rule tests finish with no saved rule or hidden test icon.");
        await request("rulePause", new { value = false });
        await RunWebRulesScript(core, "await send('navigate',{page:'icons'});await command('rule',{id:" + JsonSerializer.Serialize(own[0].Key) + "})");
        await Task.Delay(300);
        check(await core.ExecuteScriptAsync("$('#ruleEditor').open && ruleEditorValid && $('#ruleTarget').value===" + JsonSerializer.Serialize(own[0].Key)) == "true", "Creating from icon details opens the web editor with the requested real tray target.");
        await RunWebRulesScript(core, "await saveWebRule({preventDefault(){}})");
        check(RuleEditing.Catalog(controller).Count == 1 && Native.State(own[0]) == 1 && Native.State(own[1]) == 0
            && await core.ExecuteScriptAsync("!$('#ruleEditor').open") == "true", "Submitting the actual web editor saves and applies the single-icon rule.");
        single = RuleEditing.Catalog(controller).Single();
        await RunWebRulesScript(core, "openRuleEditor(" + JsonSerializer.Serialize(single.Key) + ");document.querySelector('input[name=ruleScope][value=program]').checked=true;updateRulePreview();for(let i=0;i<60&&!ruleEditorValid;i++)await new Promise(r=>setTimeout(r,50));if(!ruleEditorValid)throw Error($('#ruleError').textContent||'Native scope validation did not complete');await saveWebRule({preventDefault(){}})");
        program = RuleEditing.Catalog(controller).Single();
        check(program.Program && own.All(e => Native.State(e) == 1), "Submitting the web scope editor applies the program rule to both controlled icons.");
        await RunWebRulesScript(core, "openRuleDelete([" + JsonSerializer.Serialize(program.Key) + "]);$('#ruleRestoreAfterDelete').checked=true;await deleteWebRules({preventDefault(){}})");
        check(RuleEditing.Catalog(controller).Count == 0 && own.All(e => Native.State(e) == 0)
            && await core.ExecuteScriptAsync("!$('#ruleDeleteDialog').open") == "true", "Submitting the web delete confirmation performs the optional native restoration.");
    }
}
