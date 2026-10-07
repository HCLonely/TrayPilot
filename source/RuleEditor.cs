namespace TrayPilot;

internal sealed class RuleEditor : Form
{
    readonly Controller controller;
    readonly ListBox targets = new() { Dock = DockStyle.Fill, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 54, IntegralHeight = false };
    readonly RadioButton single = new() { AutoSize = true, Checked = true };
    readonly RadioButton program = new() { AutoSize = true };
    readonly Label preview = new() { Dock = DockStyle.Fill, ForeColor = UiTheme.Muted };
    readonly Label error = new() { Dock = DockStyle.Fill, ForeColor = Color.Firebrick, AutoEllipsis = true };
    readonly Button save, cancel;
    readonly TrayImageCache cache = new();
    readonly Func<RuleReference?, TrayEntry?, bool, Task<RuleReference>> commit;
    RuleReference? original;
    bool saving;
    internal RuleEditor(Controller controller, RuleReference? original, List<TrayEntry> entries, TrayEntry? selected,
        Func<RuleReference?, TrayEntry?, bool, Task<RuleReference>> commit, Font font)
    {
        this.controller = controller; this.original = original; this.commit = commit;
        Text = L.T(original == null ? "ruleCreate" : "ruleEdit"); Size = new(650, 660); MinimumSize = new(600, 600);
        StartPosition = FormStartPosition.CenterParent; Font = font; AutoScaleMode = AutoScaleMode.Dpi;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new(24), ColumnCount = 1, RowCount = 7 };
        root.ColumnStyles.Add(new(SizeType.Percent, 100)); root.RowStyles.Add(new(SizeType.Absolute, 76)); root.RowStyles.Add(new(SizeType.Absolute, 32));
        root.RowStyles.Add(new(SizeType.Percent, 100)); root.RowStyles.Add(new(SizeType.Absolute, 66)); root.RowStyles.Add(new(SizeType.Absolute, 76)); root.RowStyles.Add(new(SizeType.Absolute, 58)); root.RowStyles.Add(new(SizeType.Absolute, 44));
        root.Controls.Add(new Label { Text = L.T(original == null ? "ruleCreateIntro" : "ruleEditIntro"), Dock = DockStyle.Fill, Font = new(font.FontFamily, 12, FontStyle.Bold) }, 0, 0);
        root.Controls.Add(new Label { Text = L.T("ruleChooseTarget"), Dock = DockStyle.Fill }, 0, 1);
        foreach (var entry in entries.Where(e => original == null || Controller.SamePath(e.Path, original.Path))) targets.Items.Add(entry);
        targets.AccessibleName = L.T("ruleChooseTarget"); targets.BorderStyle = BorderStyle.None;
        targets.DrawItem += (_, e) =>
        {
            if (e.Index < 0 || targets.Items[e.Index] is not TrayEntry entry) return;
            bool chosen = (e.State & DrawItemState.Selected) != 0;
            using var fill = new SolidBrush(UiTheme.Surface); e.Graphics.FillRectangle(fill, e.Bounds);
            if (chosen)
            {
                var saved = e.Graphics.Save(); e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                var bounds = Rectangle.Inflate(e.Bounds, -4, -3);
                using var path = UiTheme.RoundedRectangle(bounds, 8 * DeviceDpi / 96);
                using var tint = new System.Drawing.Drawing2D.LinearGradientBrush(bounds, UiTheme.SelectedSurface, UiTheme.SelectedSurfaceEnd, 90f);
                using var outline = new Pen((e.State & DrawItemState.Focus) != 0 ? UiTheme.Accent : UiTheme.SelectedOutline);
                e.Graphics.FillPath(tint, path); e.Graphics.DrawPath(outline, path); e.Graphics.Restore(saved);
            }
            using var image = cache.Create(entry with { State = 0 }, 32); e.Graphics.DrawImage(image, e.Bounds.Left + 10, e.Bounds.Top + 10, 32, 32);
            var name = new Rectangle(e.Bounds.Left + 54, e.Bounds.Top + 5, e.Bounds.Width - 64, 22);
            var hint = new Rectangle(name.Left, name.Bottom, name.Width, 22);
            TextRenderer.DrawText(e.Graphics, entry.Name, Font, name, UiTheme.Ink, TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
            string identity = entry.Guid == Guid.Empty ? "UID " + entry.Id : entry.Guid.ToString();
            TextRenderer.DrawText(e.Graphics, (string.IsNullOrWhiteSpace(entry.Tooltip) ? Path.GetFileName(entry.Path) : entry.Tooltip.Replace('\n', ' ')) + " · " + identity, Font, hint, UiTheme.Muted, TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
        };
        root.Controls.Add(targets, 0, 2);
        var scopes = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new(0, 10, 0, 0) };
        single.Text = L.T("dashboardSingleRule"); program.Text = L.T("dashboardProgramRule");
        foreach (var radio in new[] { single, program }) { radio.Appearance = Appearance.Button; UiTheme.Toggle(radio); radio.Margin = new(0, 0, 10, 0); radio.MinimumSize = new(160, 40); scopes.Controls.Add(radio); }
        root.Controls.Add(scopes, 0, 3);
        var previewSurface = new SurfacePanel { Dock = DockStyle.Fill, Padding = new(12, 8, 12, 8), Margin = Padding.Empty };
        previewSurface.Controls.Add(preview); root.Controls.Add(previewSurface, 0, 4); root.Controls.Add(error, 0, 5);
        var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        save = UiTheme.Button(L.T("ruleSave"), true); cancel = UiTheme.Button(L.T("cancel")); cancel.DialogResult = DialogResult.Cancel; footer.Controls.Add(save); footer.Controls.Add(cancel); root.Controls.Add(footer, 0, 6);
        Controls.Add(root); CancelButton = cancel; AcceptButton = save;
        targets.SelectedIndexChanged += (_, _) => UpdatePreview(); single.CheckedChanged += (_, _) => UpdatePreview(); program.CheckedChanged += (_, _) => UpdatePreview();
        if (original != null) program.Checked = original.Program;
        var desired = selected ?? (original == null ? entries.FirstOrDefault() : entries.FirstOrDefault(original.Matches));
        if (desired != null) targets.SelectedItem = targets.Items.Cast<TrayEntry>().FirstOrDefault(e => e.Key == desired.Key);
        if (targets.SelectedIndex < 0 && targets.Items.Count > 0) targets.SelectedIndex = 0;
        // Editing an offline single-icon rule may retain its identity; converting an
        // offline program rule to a single icon must wait for a real target.
        if (targets.Items.Count == 0 && original is { Program: true }) single.Enabled = false;
        save.Click += async (_, _) => await SaveAsync(); FormClosing += (_, e) => { if (saving) e.Cancel = true; };
        UiTheme.Apply(this); error.ForeColor = UiTheme.Dark ? Color.FromArgb(255, 158, 158) : Color.Firebrick;
        UpdatePreview(); Shown += (_, _) => { if (targets.Items.Count > 0) targets.Focus(); else program.Focus(); };
    }
    void UpdatePreview()
    {
        string path = original?.Path ?? (targets.SelectedItem as TrayEntry)?.Path ?? "";
        preview.Text = (program.Checked ? L.T("ruleProgramPreview") : L.T("ruleSinglePreview")) + "\n" + path + "\n" + L.T(controller.Saved.RulesPaused ? "ruleSavedWhilePaused" : "ruleApplyOnSave");
        if (save == null || saving) return;
        try { RuleEditing.Validate(controller, original, targets.SelectedItem as TrayEntry, program.Checked); error.Text = ""; save.Enabled = true; }
        catch (InvalidOperationException ex) { error.Text = ex.Message; save.Enabled = false; }
        error.AccessibleName = error.Text;
    }
    async Task SaveAsync()
    {
        if (saving) return; saving = true; save.Enabled = cancel.Enabled = targets.Enabled = single.Enabled = program.Enabled = false; error.Text = "";
        try { original = await commit(original, targets.SelectedItem as TrayEntry, program.Checked); saving = false; DialogResult = DialogResult.OK; Close(); }
        catch (RuleSavedException ex) { original = ex.Rule; error.Text = L.T("ruleSavedApplyFailed") + " " + ex.InnerException?.Message; }
        catch (Exception ex) { error.Text = ex.Message; }
        finally
        {
            if (!IsDisposed) { saving = false; save.Enabled = cancel.Enabled = targets.Enabled = program.Enabled = true; single.Enabled = targets.Items.Count > 0 || original is { Program: false }; error.AccessibleName = error.Text; }
        }
    }
    protected override void Dispose(bool disposing) { if (disposing) cache.Dispose(); base.Dispose(disposing); }
}
