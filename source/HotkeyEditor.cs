namespace TrayPilot;

internal sealed class HotkeyEditor : Form
{
    readonly CheckBox ctrl, alt, shift;
    readonly ComboBox key;
    readonly TextBox capture;
    readonly Label error;
    bool updating;
    internal Keys Value => (ctrl.Checked ? Keys.Control : Keys.None) | (alt.Checked ? Keys.Alt : Keys.None)
        | (shift.Checked ? Keys.Shift : Keys.None) | (key.SelectedItem is KeyChoice choice ? choice.Key : Keys.None);
    internal sealed record KeyChoice(Keys Key) { public override string ToString() => SettingsPage.FormatKeys(Key); }
    internal HotkeyEditor(string action, Keys initial, Font font)
    {
        Text = L.T("settingsEditHotkey"); Font = font; ClientSize = new(520, 330); FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false; StartPosition = FormStartPosition.CenterParent; Padding = new(24);
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6 };
        root.ColumnStyles.Add(new(SizeType.Percent, 100));
        foreach (int height in new[] { 34, 48, 46, 50 }) root.RowStyles.Add(new(SizeType.Absolute, height));
        root.RowStyles.Add(new(SizeType.Percent, 100)); root.RowStyles.Add(new(SizeType.Absolute, 38));
        root.Controls.Add(new Label { Text = action, Dock = DockStyle.Fill, Font = new(font.FontFamily, 11, FontStyle.Bold) }, 0, 0);
        capture = new TextBox { ReadOnly = true, Dock = DockStyle.Top, AccessibleName = L.T("settingsRecordHotkey") };
        root.Controls.Add(capture, 0, 1);
        var modifiers = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        ctrl = new ThemeCheckBox { Text = "Ctrl", AutoSize = true }; alt = new ThemeCheckBox { Text = "Alt", AutoSize = true }; shift = new ThemeCheckBox { Text = "Shift", AutoSize = true };
        foreach (var toggle in new[] { ctrl, alt, shift }) { modifiers.Controls.Add(toggle); toggle.CheckedChanged += (_, _) => Compose(); } root.Controls.Add(modifiers, 0, 2);
        key = new ThemeComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = L.T("settingsMainKey") };
        var choices = Enumerable.Range((int)Keys.A, 26).Concat(Enumerable.Range((int)Keys.D0, 10)).Concat(Enumerable.Range((int)Keys.F1, 11))
            .Select(value => (Keys)value).Append(initial & Keys.KeyCode).Distinct().Where(value => GlobalHotkey.Valid(Keys.Control | value)).Select(value => new KeyChoice(value)).ToArray();
        key.Items.AddRange(choices); key.SelectedIndexChanged += (_, _) => Compose(); root.Controls.Add(key, 0, 3);
        error = new Label { Text = L.T("settingsRecordHelp"), Dock = DockStyle.Fill, ForeColor = UiTheme.Muted }; root.Controls.Add(error, 0, 4);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        var confirm = UiTheme.Button(L.T("save"), true); var cancel = UiTheme.Button(L.T("cancel")); cancel.DialogResult = DialogResult.Cancel;
        confirm.Click += (_, _) => { if (GlobalHotkey.Valid(Value)) DialogResult = DialogResult.OK; else { error.Text = L.T("settingsHotkeyFieldError"); error.ForeColor = Color.Firebrick; capture.Focus(); } };
        buttons.Controls.Add(confirm); buttons.Controls.Add(cancel); root.Controls.Add(buttons, 0, 5); Controls.Add(root); CancelButton = cancel;
        updating = true; ctrl.Checked = (initial & Keys.Control) != 0; alt.Checked = (initial & Keys.Alt) != 0; shift.Checked = (initial & Keys.Shift) != 0;
        key.SelectedItem = choices.SingleOrDefault(choice => choice.Key == (initial & Keys.KeyCode)) ?? choices[0]; updating = false; Compose();
        UiTheme.Apply(this); Shown += (_, _) => capture.Focus();
    }
    void Compose()
    {
        if (updating || key == null || error == null) return;
        capture.Text = SettingsPage.FormatKeys(Value); error.Text = L.T("settingsRecordHelp"); error.ForeColor = UiTheme.Muted;
    }
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (!capture.Focused || (keyData & Keys.KeyCode) is Keys.Tab or Keys.Escape) return base.ProcessCmdKey(ref msg, keyData);
        if ((keyData & Keys.KeyCode) is Keys.ControlKey or Keys.Menu or Keys.ShiftKey) return true;
        if (!GlobalHotkey.Valid(keyData)) { error.Text = L.T("settingsHotkeyFieldError"); error.ForeColor = Color.Firebrick; return true; }
        updating = true; ctrl.Checked = (keyData & Keys.Control) != 0; alt.Checked = (keyData & Keys.Alt) != 0; shift.Checked = (keyData & Keys.Shift) != 0;
        var choice = key.Items.Cast<KeyChoice>().FirstOrDefault(item => item.Key == (keyData & Keys.KeyCode));
        if (choice == null) { choice = new(keyData & Keys.KeyCode); key.Items.Add(choice); }
        key.SelectedItem = choice; updating = false; Compose(); return true;
    }
}
