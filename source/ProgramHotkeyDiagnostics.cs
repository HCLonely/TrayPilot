using Microsoft.Web.WebView2.Core;
using System.Text.Json;

namespace TrayPilot;

internal static partial class Diagnostics
{
    static async Task CheckProgramHotkeysLive(CoreWebView2 core, MainForm form, Controller controller, List<TrayEntry> own,
        Func<string, object, Task> request, Action<bool, string> check)
    {
        string path = own[0].Path;
        check(controller.Saved.ProgramHotkeys.Count == 0, "Program shortcuts are unassigned by default.");
        // Reserve a free combination first, so diagnostics also work on customized desktops.
        int keys = 0;
        using (var probe = new GlobalHotkey(form.Handle, 0x7000))
        {
            for (Keys key = Keys.A; key <= Keys.Z; key++)
                if (probe.TrySet(true, Keys.Control | Keys.Alt | Keys.Shift | key))
                {
                    keys = (int)(Keys.Control | Keys.Alt | Keys.Shift | key);
                    break;
                }
        }
        if (keys == 0) throw new IOException("No free shortcut for isolated diagnostics.");
        await request("programHotkey", new { path, keys });
        check(new Controller(controller.SettingsFolder).Saved.ProgramHotkeys[path] == keys, "Program shortcut persists and reloads independently of hide rules.");
        await request("programHotkey", new { path = path.ToUpperInvariant(), keys });
        check(controller.Saved.ProgramHotkeys.Count == 1, "Program shortcut identity is case-insensitive.");
        using (var occupied = new GlobalHotkey(form.Handle, 0x7000))
        {
            int taken = (int)(Keys.Control | Keys.Alt | Keys.Shift | Keys.F10);
            if (occupied.TrySet(true, (Keys)taken))
            {
                bool rejected = false;
                try { await request("programHotkey", new { path, keys = taken }); } catch (IOException) { rejected = true; }
                check(rejected && controller.Saved.ProgramHotkeys[path] == keys, "Occupied program shortcut is rejected and the prior binding restored.");
            }
        }
        var previousPreferences = SettingsPreferences.Read(controller.Saved, null);
        bool globalRejected = false;
        try
        {
            SettingsTransaction.Save(controller, previousPreferences with { MainEnabled = true, MainKeys = (Keys)keys }, false,
                new StartupRegistration(), () => true);
        }
        catch (IOException) { globalRejected = true; }
        check(globalRejected && !controller.Saved.MainHotkeyEnabled, "Global settings reject a program shortcut conflict without changing preferences.");
        controller.Saved.MainHotkeyEnabled = true;
        controller.Saved.MainHotkey = (int)(Keys.Control | Keys.Alt | Keys.F8);
        bool programRejected = false;
        try { await request("programHotkey", new { path, keys = controller.Saved.MainHotkey }); }
        catch (IOException) { programRejected = true; }
        finally { previousPreferences.Write(controller.Saved); }
        check(programRejected && controller.Saved.ProgramHotkeys[path] == keys, "Program shortcut rejects enabled global bindings and preserves the old shortcut.");
        string blocker = Path.Combine(controller.SettingsFolder, "settings.json.tmp");
        Directory.CreateDirectory(blocker);
        bool saveRejected = false;
        try { await request("programHotkey", new { path, keys = 0 }); }
        catch (IOException) { saveRejected = true; }
        finally { Directory.Delete(blocker); }
        check(saveRejected && controller.Saved.ProgramHotkeys[path] == keys, "Settings write failure restores the program shortcut registration and saved value.");
        await request("ruleSave", new { originalId = (string?)null, targetId = own[0].Key, scope = "program" });
        form.Hide();
        Native.SendMessageW(form.Handle, 0x0312, 0x5000, 0);
        for (int i = 0; i < 100 && own.Any(e => Native.State(e) != 0); i++) await Task.Delay(50);
        for (int i = 0; i < 100 && (bool)typeof(MainForm).GetField("busy", DashboardFlags)!.GetValue(form)!; i++) await Task.Delay(50);
        form.Show();
        check(own.All(e => Native.State(e) == 0) && controller.IsTemporarilyShown(path), "Program shortcut restores all test icons and suspends matching rules for the session.");
        await controller.ApplyAsync(own);
        check(own.All(e => Native.State(e) == 0), "Auto-hide leaves shortcut-restored icons visible.");
        await (Task)typeof(MainForm).GetMethod("ToggleProgramIconsAsync", DashboardFlags)!.Invoke(form, new object[] { path })!;
        check(own.All(e => Native.State(e) == 1) && !controller.IsTemporarilyShown(path), "A second program toggle hides all matching icons and clears the exception.");
        await request("navigate", new { page = "rules" });
        await RunWebRulesScript(core, "ruleNodes.get(ruleCatalog()[0].id).dispatchEvent(new MouseEvent('contextmenu',{bubbles:true,cancelable:true,clientX:innerWidth-2,clientY:innerHeight-2}))");
        check(await core.ExecuteScriptAsync("!ruleMenu.hidden && ruleMenu.getBoundingClientRect().right<=innerWidth && ruleMenu.getBoundingClientRect().bottom<=innerHeight") == "true", "Rule context menu opens inside viewport edges.");
        await core.ExecuteScriptAsync("ruleMenu.dispatchEvent(new KeyboardEvent('keydown',{key:'Escape',bubbles:true}));ruleNodes.get(ruleCatalog()[0].id).dispatchEvent(new KeyboardEvent('keydown',{key:'F10',shiftKey:true,bubbles:true,cancelable:true}))");
        check(await core.ExecuteScriptAsync("!ruleMenu.hidden && ruleMenu.contains(document.activeElement)") == "true", "Shift+F10 opens the focused rule menu with keyboard focus.");
        await core.ExecuteScriptAsync("$('#ruleMenu [data-action=hotkey]').click()");
        check(await core.ExecuteScriptAsync("$('#prefHotkeyDialog').open && programHotkeyPath!==null && !programHotkeyClear.hidden") == "true", "Rule context menu opens the program shortcut editor.");
        string previewFolder = Path.Combine(controller.SettingsFolder, "shortcut-preview");
        Directory.CreateDirectory(previewFolder);
        async Task Capture(string name)
        {
            await Task.Delay(180);
            using var stream = File.Create(Path.Combine(previewFolder, name + ".png"));
            await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream);
        }
        await core.ExecuteScriptAsync("applyTheme('light')");
        await Capture("shortcut-light");
        await core.ExecuteScriptAsync("applyTheme('dark')");
        await Capture("shortcut-dark");
        await core.ExecuteScriptAsync("applyTheme(state.theme);$('#prefHotkeyDialog').close()");
        await Task.Delay(120);
        await request("navigate", new { page = "icons" });
        await core.ExecuteScriptAsync("choose(" + JsonSerializer.Serialize(own[0].Key) + ",{ctrlKey:false,shiftKey:false})");
        await Capture("shortcut-entry");
        await core.ExecuteScriptAsync("$('#detail .program-hotkey').click()");
        check(await core.ExecuteScriptAsync("$('#prefHotkeyDialog').open && $('#prefCapture').getBoundingClientRect().width>300 && !$('#programHotkeyIdentity').hidden") == "true", "The shortcut card opens a labeled program editor with a full-width recording area.");
        form.ClientSize = new Size(1004, 701);
        check(await core.ExecuteScriptAsync("$('#prefHotkeyDialog').getBoundingClientRect().bottom<=innerHeight && $('#prefHotkeyDialog').getBoundingClientRect().top>=0 && $('#prefHotkeyForm').scrollWidth<=$('#prefHotkeyDialog').clientWidth") == "true", "Shortcut editor fits the minimum supported window without horizontal overflow.");
        await Capture("shortcut-minimum");
        await core.ExecuteScriptAsync("$('#prefHotkeyDialog').close()");
        await Task.Delay(100);
        await core.ExecuteScriptAsync("state.language='en-US';render();renderPreferences();openProgramHotkey(apps.find(a=>a.id===current).path,apps.find(a=>a.id===current).name)");
        await Capture("shortcut-english");
        await core.ExecuteScriptAsync("$('#prefHotkeyDialog').close()");
        await Task.Delay(100);
        form.Size = new Size(1380, 920);
        await core.ExecuteScriptAsync("state.language='zh-CN';render();renderPreferences();openProgramHotkey(apps.find(a=>a.id===current).path,apps.find(a=>a.id===current).name)");
        await RunWebRulesScript(core, "await saveProgramHotkey(0)");
        check(controller.Saved.ProgramHotkeys.Count == 0 && await core.ExecuteScriptAsync("!$('#prefHotkeyDialog').open") == "true", "Clearing through the actual shortcut editor removes the persisted binding.");
        await request("ruleDelete", new { ids = new[] { RuleEditing.Catalog(controller).Single().Key }, restore = true });
        await request("navigate", new { page = "icons" });
    }
}
