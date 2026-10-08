using System.Text.Json;

namespace TrayPilot;

internal sealed partial class MainForm
{
    RuleSnapshot? webRuleUndo;
    string? webRuleUndoCatalog;

    static string WebRuleImageKey(RuleReference rule) => "rule:" + rule.Key;
    string WebRuleSignature() => JsonSerializer.Serialize(RuleEditing.Catalog(controller));
    bool CanUndoWebRules()
    {
        if (webRuleUndo == null) return false;
        if (webRuleUndoCatalog == WebRuleSignature()) return true;
        webRuleUndo = null; webRuleUndoCatalog = null; return false;
    }
    void RememberWebRules(RuleSnapshot snapshot)
    { webRuleUndo = snapshot; webRuleUndoCatalog = WebRuleSignature(); }

    List<object> WebRuleRecords(List<TrayEntry> snapshot)
    {
        var result = new List<object>();
        foreach (var rule in RuleEditing.Catalog(controller))
        {
            var live = snapshot.Where(rule.Matches).ToList();
            var entry = live.FirstOrDefault() ?? controller.Saved.Recovery.FirstOrDefault(rule.Matches)
                ?? new TrayEntry { Name = Path.GetFileNameWithoutExtension(rule.Path), Path = rule.Path };
            // Separate cache identities for offline rules avoid sharing an empty
            // window key across unrelated executables.
            var image = WebRuleImage(rule, entry);
            string visibility = live.Count == 0 ? "waiting" : controller.Saved.RulesPaused ? "paused"
                : live.Any(controller.IsTemporarilyShown) ? "temporary" : live.All(e => e.State == 1) ? "hidden" : "pending";
            result.Add(new { id = rule.Key, name = entry.Name, proc = Path.GetFileName(rule.Path), path = rule.Path,
                scope = rule.Program ? "program" : "single", identity = rule.Icon?.Label ?? "", state = visibility,
                liveCount = live.Count, hiddenCount = live.Count(e => e.State == 1), tooltip = entry.Tooltip,
                image = image.Image, darkPlate = image.DarkPlate, lightPlate = image.LightPlate });
        }
        return result;
    }

    (string Image, bool DarkPlate, bool LightPlate) WebRuleImage(RuleReference rule, TrayEntry entry)
    {
        string key = WebRuleImageKey(rule);
        string signature = entry.Path + ":" + entry.Started + ":" + (entry.IconSnapshot is { Length: > 0 } bytes
            ? Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)) : "file");
        if (webImages.TryGetValue(key, out var cached) && cached.Signature == signature) return (cached.Image, cached.DarkPlate, cached.LightPlate);
        var image = WebImage(entry);
        webImages[key] = (signature, image.Image, image.DarkPlate, image.LightPlate);
        return image;
    }

    RuleReference ResolveWebRule(string? key) => RuleEditing.Catalog(controller).FirstOrDefault(r => r.Key == key)
        ?? throw new IOException(L.T("ruleTargetRequired"));
    (RuleReference? Original, TrayEntry? Target, bool Program) WebRuleDraft(JsonElement root)
    {
        string? originalId = root.TryGetProperty("originalId", out var original) ? original.GetString() : null;
        string? targetId = root.TryGetProperty("targetId", out var target) ? target.GetString() : null;
        var rule = originalId == null ? null : ResolveWebRule(originalId);
        var entry = targetId == null ? null : ResolveWebEntry(targetId);
        if (entry != null && (!Scanner.SameOwner(entry) || Native.State(entry) is not (0 or 1)))
            throw new IOException(L.T("processUnavailableMessage"));
        string scope = root.GetProperty("scope").GetString() ?? "";
        if (scope is not ("program" or "single")) throw new InvalidDataException("Unknown rule scope.");
        return (rule, entry, scope == "program");
    }

    async Task<object?> ExecuteWebRuleCommand(string action, JsonElement root)
    {
        if (action == "ruleValidate")
        {
            var draft = WebRuleDraft(root);
            var next = RuleEditing.Validate(controller, draft.Original, draft.Target, draft.Program);
            return new { id = next.Key };
        }
        if (WebReadOnlyPreview) throw new InvalidOperationException("Read-only preview.");
        EnsureUiContext(); busy = true; webForegroundOperation = true; PublishWebState();
        object? result = null;
        try
        {
            if (action == "ruleSave")
            {
                var draft = WebRuleDraft(root);
                var before = RuleEditing.Capture(controller);
                var saved = RuleEditing.Save(controller, draft.Original, draft.Target, draft.Program);
                RememberWebRules(before);
                try { if (!controller.Saved.RulesPaused) await controller.ApplyAsync(WebEntries()); }
                catch (Exception ex) { throw new RuleSavedException(saved, ex); }
                result = new { id = saved.Key };
            }
            else if (action == "ruleDelete")
            {
                var ids = root.GetProperty("ids");
                if (ids.GetArrayLength() is < 1 or > 512) throw new InvalidDataException("Invalid target count.");
                var targets = ids.EnumerateArray().Select(id => ResolveWebRule(id.GetString())).DistinctBy(r => r.Key).ToList();
                var before = RuleEditing.Capture(controller);
                if (root.GetProperty("restore").GetBoolean())
                    await controller.ChangeManyAsync(WebEntries().Where(e => targets.Any(r => r.Matches(e))), false);
                RuleEditing.Remove(controller, targets); RememberWebRules(before);
            }
            else if (action == "rulePause")
            {
                RuleEditing.Pause(controller, root.GetProperty("value").GetBoolean());
                if (!controller.Saved.RulesPaused) await controller.ApplyAsync(WebEntries());
            }
            else if (action == "ruleApply")
            {
                RuleEditing.Pause(controller, false); controller.ResetManualVisibility(); await controller.ApplyAsync(WebEntries());
            }
            else if (action == "ruleTemporary")
            {
                var rule = ResolveWebRule(root.GetProperty("id").GetString());
                var targets = WebEntries().Where(rule.Matches).ToList();
                if (targets.Count == 0) throw new IOException(L.T("processUnavailableMessage"));
                await controller.ChangeManyAsync(targets, false, completed: e => controller.SetManualVisibility(e, false));
            }
            else if (action == "ruleUndo")
            {
                if (!CanUndoWebRules()) throw new IOException(L.T("ruleSelectHint"));
                RuleEditing.Commit(controller, webRuleUndo!, restoreManual: true); webRuleUndo = null; webRuleUndoCatalog = null;
                if (!controller.Saved.RulesPaused) await controller.ApplyAsync(WebEntries());
            }
            return result;
        }
        finally
        {
            UpdateEntryStates(); UpdateStatus(); webForegroundOperation = false; CompleteOperation(); UpdateTimer();
        }
    }
}
