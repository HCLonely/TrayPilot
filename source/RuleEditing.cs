namespace TrayPilot;

internal sealed record RuleReference(string Path, IconRule? Icon = null)
{
    internal bool Program => Icon == null;
    internal bool Matches(TrayEntry entry) => Program ? Controller.SamePath(Path, entry.Path) : Icon!.Matches(entry);
    internal string Key => Controller.NormalizePath(Path) + "|" + (Icon?.Label ?? "program");
}

internal sealed record RuleSnapshot(List<string> Paths, List<IconRule> Icons, string[] ManualPaths, TrayEntry[] ManualIcons);

internal sealed class RuleSavedException(RuleReference rule, Exception error) : Exception(error.Message, error)
{
    internal RuleReference Rule { get; } = rule;
}

internal static class RuleEditing
{
    internal static List<RuleReference> Catalog(Controller controller) => controller.Saved.HiddenPaths.Select(p => new RuleReference(p))
        .Concat(controller.Saved.HiddenIcons.Select(i => new RuleReference(i.Path, i))).ToList();
    internal static RuleSnapshot Capture(Controller controller)
    {
        var manual = controller.CaptureManualVisibility();
        return new(controller.Saved.HiddenPaths.ToList(), controller.Saved.HiddenIcons.ToList(), manual.Paths, manual.Icons);
    }
    internal static void Commit(Controller controller, RuleSnapshot next, bool restoreManual = false)
    {
        var before = Capture(controller);
        controller.Saved.HiddenPaths = next.Paths.ToList(); controller.Saved.HiddenIcons = next.Icons.ToList();
        try { controller.Save(); }
        catch { controller.Saved.HiddenPaths = before.Paths; controller.Saved.HiddenIcons = before.Icons; throw; }
        if (restoreManual) controller.RestoreManualVisibility(next.ManualPaths, next.ManualIcons);
    }
    internal static RuleReference Save(Controller controller, RuleReference? original, TrayEntry? target, bool program)
    {
        var next = Validate(controller, original, target, program);
        string path = next.Path;
        var snapshot = Capture(controller);
        if (original != null) { snapshot.Paths.RemoveAll(p => original.Program && Controller.SamePath(p, original.Path)); snapshot.Icons.RemoveAll(i => i == original.Icon); }
        if (next.Program) snapshot.Paths.Add(path); else snapshot.Icons.Add(next.Icon!);
        Commit(controller, snapshot);
        if (next.Program) controller.SetManualVisibility(path, hidden: true);
        else if (target != null) controller.ActivateIconRule(target);
        return next;
    }
    internal static RuleReference Validate(Controller controller, RuleReference? original, TrayEntry? target, bool program)
    {
        if (target == null && (original == null || !program && original.Program)) throw new InvalidOperationException(L.T("ruleTargetRequired"));
        string path = Controller.NormalizePath(original?.Path ?? target!.Path);
        if (target != null && original != null && !Controller.SamePath(path, target.Path)) throw new InvalidOperationException(L.T("ruleTargetLocked"));
        var next = new RuleReference(path, program ? null : original is { Program: false } && target == null ? original.Icon : IconRule.From(target!));
        var other = Catalog(controller).Where(r => r != original).ToList();
        if (other.Any(r => Controller.SamePath(r.Path, path) && (r.Program || next.Program || r.Key == next.Key)))
            throw new InvalidOperationException(L.T("ruleOverlapError"));
        return next;
    }
    internal static void Remove(Controller controller, IEnumerable<RuleReference> references)
    {
        var targets = references.ToList(); var snapshot = Capture(controller);
        snapshot.Paths.RemoveAll(p => targets.Any(r => r.Program && Controller.SamePath(r.Path, p)));
        snapshot.Icons.RemoveAll(i => targets.Any(r => r.Icon == i)); Commit(controller, snapshot);
    }
    internal static void Pause(Controller controller, bool paused)
    {
        bool previous = controller.Saved.RulesPaused; controller.Saved.RulesPaused = paused;
        try { controller.Save(); } catch { controller.Saved.RulesPaused = previous; throw; }
    }
}
