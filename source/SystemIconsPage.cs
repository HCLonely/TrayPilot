using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Numerics;

namespace TrayPilot;

internal sealed record SystemIconsSnapshot(bool Connected, bool Connecting, int Requested, int Found, int Hidden,
    bool Shared, bool Legacy, string Error, string Diagnostic, (string Text, string Font)[] Appearances)
{
    internal string StateKey(int bit) => Connecting ? "systemPageIdentifying" : !Connected ? "systemNativeUnavailable"
        : (Found & bit) == 0 ? (Requested & bit) != 0 ? "systemNativeWaiting" : "systemNativeNotFound"
        : (Hidden & bit) != 0 ? "systemNativeHidden"
        : Shared && (bit & 48) != 0 && (Requested & bit) != 0 && (Requested & 48) != 48 ? "systemSharedIndicator"
        : (Requested & bit) != 0 ? "systemPagePending" : "systemNativeVisible";
}

internal sealed class SystemIconCard : Panel
{
    readonly SystemIconCatalog.Item item;
    readonly ThemeSwitch toggle = new();
    readonly Action<int> select;
    Bitmap image;
    (string Text, string Font) appearance;
    string status = "", group = "";
    bool selected, hovered;
    internal int Mask => item.Mask;
    internal ThemeSwitch Toggle => toggle;
    internal SystemIconCard(SystemIconCatalog.Item item, Font font, Action<int> select, Action<int> change)
    {
        this.item = item; this.select = select; Font = font; DoubleBuffered = true; TabStop = true; SetStyle(ControlStyles.Selectable, true);
        image = SystemIconImages.Create(item.Mask, 32); AccessibleName = L.T(item.Name); AccessibleRole = AccessibleRole.Grouping;
        toggle.AccessibleName = L.F("systemPageDisplay", L.T(item.Name)); toggle.Click += (_, _) => change(item.Mask);
        Controls.Add(toggle); Click += (_, _) => select(item.Mask);
        GotFocus += (_, _) => { select(item.Mask); Invalidate(); }; LostFocus += (_, _) => Invalidate();
        toggle.GotFocus += (_, _) => { select(item.Mask); Invalidate(); }; toggle.LostFocus += (_, _) => Invalidate();
        MouseEnter += (_, _) => { hovered = true; Invalidate(); }; MouseLeave += (_, _) => { hovered = false; Invalidate(); };
    }
    internal void UpdateState(SystemIconsSnapshot state, bool chosen, bool working)
    {
        int index = Array.FindIndex(SystemIconCatalog.Items, entry => entry.Mask == item.Mask);
        var nextAppearance = index >= 0 && index < state.Appearances.Length ? state.Appearances[index] : default;
        if (appearance != nextAppearance)
        { appearance = nextAppearance; image.Dispose(); image = SystemIconImages.Create(item.Mask, 32 * DeviceDpi / 96, appearance); }
        selected = chosen; status = L.T(state.StateKey(item.Mask)); group = L.T(GroupKey(item.Mask));
        toggle.Checked = (state.Requested & item.Mask) == 0; toggle.Enabled = state.Connected && !state.Connecting && !working;
        toggle.AccessibleDescription = status; AccessibleDescription = status + " · " + L.T("systemPageCardHelp");
        toggle.BackColor = chosen ? UiTheme.SelectedSurface : UiTheme.Surface; Invalidate();
    }
    internal static string GroupKey(int bit) => bit <= 8 ? "systemPageCommon" : bit is 256 or 512 or 2048 ? "systemPageTaskbar" : "systemPageIndicators";
    protected override void OnResize(EventArgs e)
    { base.OnResize(e); toggle.SetBounds(Math.Max(0, Width - 64 * DeviceDpi / 96), 15 * DeviceDpi / 96, 48 * DeviceDpi / 96, 30 * DeviceDpi / 96); }
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (Focused && keyData is Keys.Enter or Keys.Space) { select(item.Mask); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); e.Graphics.Clear(Parent?.BackColor ?? UiTheme.Surface); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        int S(int value) => value * DeviceDpi / 96;
        var bounds = new Rectangle(1, 1, Width - 3, Height - 3); if (bounds.Width < 20 || bounds.Height < 20) return;
        using var shape = UiTheme.RoundedRectangle(bounds, S(11));
        using var fill = new SolidBrush(selected ? UiTheme.SelectedSurface : hovered ? UiTheme.Hover : UiTheme.Surface);
        using var outline = new Pen(ContainsFocus ? UiTheme.Accent : selected ? UiTheme.SelectedOutline : UiTheme.Border, (ContainsFocus ? 1.5f : 1) * DeviceDpi / 96);
        e.Graphics.FillPath(fill, shape); e.Graphics.DrawPath(outline, shape);
        using var attributes = new ImageAttributes(); var tint = UiTheme.Ink;
        attributes.SetColorMatrix(new ColorMatrix { Matrix00 = tint.R / 255f, Matrix11 = tint.G / 255f, Matrix22 = tint.B / 255f });
        e.Graphics.DrawImage(image, new Rectangle(S(15), S(17), S(30), S(30)), 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, attributes);
        TextRenderer.DrawText(e.Graphics, L.T(item.Name), Font, new Rectangle(S(57), S(13), Math.Max(1, Width - S(123)), S(42)), UiTheme.Ink, TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(e.Graphics, status, Font, new Rectangle(S(15), S(63), Width - S(30), S(26)), UiTheme.Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(e.Graphics, group, Font, new Rectangle(S(15), S(90), Width - S(30), S(20)), UiTheme.Muted, TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
    }
    protected override void Dispose(bool disposing) { if (disposing) image.Dispose(); base.Dispose(disposing); }
}

internal sealed class SystemIconsPage : UserControl
{
    SystemIconsSnapshot state;
    readonly Func<SystemIconsSnapshot> read;
    readonly Func<Task<SystemIconsSnapshot>> connect;
    readonly Func<int, int, Task<SystemIconsSnapshot>> change;
    readonly Func<bool, Task<SystemIconsSnapshot>> discovery;
    readonly Label connection = new() { Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
    readonly Label summary = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    readonly Label feedback = new() { Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
    readonly ProgressBar identifying = new() { Dock = DockStyle.Bottom, Height = 3, Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 35, Visible = false };
    readonly Panel grid = new() { Dock = DockStyle.Fill, AutoScroll = true, Margin = Padding.Empty };
    readonly FlowLayoutPanel details = new() { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
    readonly List<SystemIconCard> cards = new();
    readonly Button reconnect, scheme, restoreAll, toggleSelected, undo;
    readonly System.Windows.Forms.Timer poll = new() { Interval = 500 };
    readonly TableLayoutPanel work;
    readonly SurfacePanel detailPanel;
    int selected = 1, filter;
    int? previous;
    bool working, reflowing, pollRequested, gridLayoutPending;
    (Size Size, int Dpi, int Filter)? gridLayout;
    string? detailSignature;
    internal bool Working => working;
    internal bool NavigationAllowed => !working || state.Connecting;
    internal SystemIconsSnapshot Snapshot => state;
    internal IReadOnlyList<SystemIconCard> Cards => cards;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal bool ReadOnlyPreview { get; set; }

    internal SystemIconsPage(SystemIconsSnapshot initial, Func<SystemIconsSnapshot> read,
        Func<Task<SystemIconsSnapshot>> connect, Func<int, int, Task<SystemIconsSnapshot>> change,
        Func<bool, Task<SystemIconsSnapshot>> discovery, Font font)
    {
        state = initial; this.read = read; this.connect = connect; this.change = change; this.discovery = discovery;
        Font = font; Dock = DockStyle.Fill; AutoScaleMode = AutoScaleMode.Dpi;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new(28, 24, 28, 14), ColumnCount = 1, RowCount = 5 };
        root.ColumnStyles.Add(new(SizeType.Percent, 100));
        foreach (int height in new[] { 78, 80, 38 }) root.RowStyles.Add(new(SizeType.Absolute, height));
        root.RowStyles.Add(new(SizeType.Percent, 100)); root.RowStyles.Add(new(SizeType.Absolute, 44));
        var heading = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        heading.ColumnStyles.Add(new(SizeType.Percent, 100)); heading.ColumnStyles.Add(new(SizeType.AutoSize)); heading.RowStyles.Add(new(SizeType.Percent, 100));
        var title = new Panel { Dock = DockStyle.Fill };
        title.Controls.Add(new Label { Text = L.T("systemIcons"), AutoSize = true, Location = new(0, 0), Font = new(font.FontFamily, 21, FontStyle.Bold) });
        title.Controls.Add(new Label { Text = L.T("systemPageSubtitle"), AutoSize = true, Location = new(0, 46), ForeColor = UiTheme.Muted }); heading.Controls.Add(title, 0, 0);
        restoreAll = UiTheme.Button(L.T("systemPageRestoreAll")); restoreAll.Click += (_, _) => ConfirmRestore(); restoreAll.Margin = new(0, 6, 0, 0); heading.Controls.Add(restoreAll, 1, 0); root.Controls.Add(heading, 0, 0);
        var connectionPanel = new SurfacePanel { Dock = DockStyle.Fill, Padding = new(14), Margin = new(0, 0, 0, 8) };
        var connectionLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
        connectionLayout.ColumnStyles.Add(new(SizeType.Percent, 100)); connectionLayout.ColumnStyles.Add(new(SizeType.AutoSize)); connectionLayout.ColumnStyles.Add(new(SizeType.AutoSize)); connectionLayout.RowStyles.Add(new(SizeType.Percent, 100));
        connectionLayout.Controls.Add(connection, 0, 0);
        scheme = UiTheme.Button(L.T("systemPageScheme")); scheme.Click += (_, _) => OpenCompatibility();
        reconnect = UiTheme.Button(L.T("systemPageReconnect")); reconnect.Click += async (_, _) => await ReconnectAsync();
        connectionLayout.Controls.Add(scheme, 1, 0); connectionLayout.Controls.Add(reconnect, 2, 0); connectionPanel.Controls.Add(connectionLayout); connectionPanel.Controls.Add(identifying); root.Controls.Add(connectionPanel, 0, 1);
        summary.ForeColor = UiTheme.Muted; root.Controls.Add(summary, 0, 2);
        work = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        work.ColumnStyles.Add(new(SizeType.Percent, 100)); work.ColumnStyles.Add(new(SizeType.Absolute, 280)); work.RowStyles.Add(new(SizeType.Percent, 100));
        var listPanel = new SurfacePanel { Dock = DockStyle.Fill, Padding = new(8), Margin = Padding.Empty };
        var listLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = Padding.Empty };
        listLayout.ColumnStyles.Add(new(SizeType.Percent, 100)); listLayout.RowStyles.Add(new(SizeType.Absolute, 44)); listLayout.RowStyles.Add(new(SizeType.Percent, 100)); listLayout.RowStyles.Add(new(SizeType.Absolute, 42));
        var filters = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty, Padding = new(2, 0, 0, 0) };
        foreach (var (key, value) in new[] { ("filterAll", 0), ("systemPageCommon", 1), ("systemPageIndicators", 2), ("systemPageTaskbar", 3) })
        {
            var radio = new RadioButton { Appearance = Appearance.Button, AutoSize = true, Text = L.T(key), Checked = value == 0 };
            UiTheme.Toggle(radio); radio.CheckedChanged += (_, _) => { if (!radio.Checked) return; filter = value; Render(); }; filters.Controls.Add(radio);
        }
        listLayout.Controls.Add(filters, 0, 0);
        foreach (var item in SystemIconCatalog.Items.OrderBy(item => SystemIconCard.GroupKey(item.Mask) == "systemPageCommon" ? 0 : SystemIconCard.GroupKey(item.Mask) == "systemPageIndicators" ? 1 : 2))
        {
            var card = new SystemIconCard(item, font, Choose, bit => _ = ToggleAsync(bit)) { Size = new(240, 116), Margin = new(4, 4, 4, 4) };
            cards.Add(card); grid.Controls.Add(card);
        }
        grid.SizeChanged += (_, _) => ReflowCards(); listLayout.Controls.Add(grid, 0, 1);
        listLayout.Controls.Add(new Label { Text = L.T("systemPageHint"), Dock = DockStyle.Fill, ForeColor = UiTheme.Muted, Padding = new(6, 7, 6, 0) }, 0, 2); listPanel.Controls.Add(listLayout); work.Controls.Add(listPanel, 0, 0);
        detailPanel = new SurfacePanel { Dock = DockStyle.Fill, Padding = new(14), Margin = new(18, 0, 0, 0) };
        var detailLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        detailLayout.ColumnStyles.Add(new(SizeType.Percent, 100)); detailLayout.RowStyles.Add(new(SizeType.Absolute, 34)); detailLayout.RowStyles.Add(new(SizeType.Percent, 100)); detailLayout.RowStyles.Add(new(SizeType.Absolute, 106));
        detailLayout.Controls.Add(new Label { Text = L.T("systemPageDetails"), Dock = DockStyle.Fill, Font = new(font.FontFamily, 10, FontStyle.Bold) }, 0, 0); detailLayout.Controls.Add(details, 0, 1);
        var detailActions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        toggleSelected = UiTheme.Button(""); toggleSelected.AutoSize = false; toggleSelected.Size = new(224, 38); toggleSelected.Click += async (_, _) => await ToggleAsync(selected); detailActions.Controls.Add(toggleSelected);
        detailActions.Controls.Add(new Label { Text = L.T("systemPageIndependent"), AutoSize = false, Size = new(224, 54), ForeColor = UiTheme.Muted });
        detailLayout.Controls.Add(detailActions, 0, 2); detailPanel.Controls.Add(detailLayout); work.Controls.Add(detailPanel, 1, 0); root.Controls.Add(work, 0, 3);
        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        footer.ColumnStyles.Add(new(SizeType.Percent, 100)); footer.ColumnStyles.Add(new(SizeType.AutoSize)); footer.RowStyles.Add(new(SizeType.Percent, 100));
        undo = UiTheme.Button(L.T("ruleUndo")); undo.Click += async (_, _) => await UndoAsync(); footer.Controls.Add(feedback, 0, 0); footer.Controls.Add(undo, 1, 0); root.Controls.Add(footer, 0, 4); Controls.Add(root);
        poll.Tick += (_, _) =>
        {
            if (working || !Visible) return;
            try { RefreshState(read()); }
            catch (Exception ex) { RefreshState(state with { Connected = false, Connecting = false, Error = ex.Message }); feedback.Text = ex.Message; }
        };
        SizeChanged += (_, _) => { bool showDetails = ClientSize.Width >= 920 * DeviceDpi / 96; detailPanel.Visible = showDetails; work.ColumnStyles[1].Width = showDetails ? 280 * DeviceDpi / 96 : 0; ReflowCards(); };
        details.SizeChanged += (_, _) => { foreach (Control child in details.Controls) child.Width = Math.Max(120, details.ClientSize.Width - 20); };
        UiTheme.Apply(this); Render();
    }
    internal void StartPolling() { pollRequested = true; poll.Start(); }
    internal void StopPolling() { pollRequested = false; poll.Stop(); }
    internal void RefreshState(SystemIconsSnapshot snapshot)
    {
        if (IsDisposed) return;
        if (!snapshot.Connected && snapshot.Error.Length == 0 && state.Error.Length > 0) snapshot = snapshot with { Error = state.Error };
        state = snapshot; Render();
    }
    internal async Task EnsureConnectedAsync() { if (!state.Connected && !working) await ReconnectAsync(); }
    internal void Choose(int bit) { selected = bit; Render(); }
    void ReflowCards()
    {
        if (reflowing || grid.ClientSize.Width < 100) return; reflowing = true;
        try
        {
            var layout = (grid.Size, DeviceDpi, filter);
            if (gridLayout == layout) return;
            int scroll = -grid.AutoScrollPosition.Y;
            // Lay out in content coordinates, never relative to the current scroll offset.
            grid.AutoScrollPosition = Point.Empty;
            grid.AutoScroll = false;
            grid.SuspendLayout();
            int gap = 8 * DeviceDpi / 96, inset = gap / 2;
            int available = grid.Width - SystemInformation.VerticalScrollBarWidth;
            int columns = available >= 660 * DeviceDpi / 96 ? 3 : available >= 420 * DeviceDpi / 96 ? 2 : 1;
            int cellWidth = available / columns, height = 116 * DeviceDpi / 96, index = 0;
            foreach (var card in cards)
            {
                int group = SystemIconCard.GroupKey(card.Mask) == "systemPageCommon" ? 1 : SystemIconCard.GroupKey(card.Mask) == "systemPageIndicators" ? 2 : 3;
                if (filter != 0 && filter != group) continue;
                card.SetBounds(inset + index % columns * cellWidth, inset + index / columns * (height + gap), Math.Max(1, cellWidth - gap), height);
                index++;
            }
            int contentHeight = (index + columns - 1) / columns * (height + gap);
            grid.AutoScrollMinSize = new(0, contentHeight);
            grid.AutoScroll = true;
            grid.ResumeLayout(true);
            grid.AutoScrollPosition = new(0, Math.Min(scroll, Math.Max(0, contentHeight - grid.ClientSize.Height)));
            gridLayout = layout;
            // Parent resize layout can finish after SizeChanged and retain its old extent.
            // Reconcile once after that layout pass, without reflowing on state polling.
            if (grid.IsHandleCreated && !gridLayoutPending)
            {
                gridLayoutPending = true;
                grid.BeginInvoke((Action)(() =>
                {
                    gridLayoutPending = false;
                    if (grid.IsDisposed) return;
                    grid.PerformLayout();
                    int limit = Math.Max(0, grid.AutoScrollMinSize.Height - grid.ClientSize.Height);
                    if (-grid.AutoScrollPosition.Y > limit) grid.AutoScrollPosition = new(0, limit);
                }));
            }
        }
        finally { reflowing = false; }
    }
    void Render()
    {
        if (IsDisposed) return;
        bool connected = state.Connected && !state.Connecting;
        identifying.Visible = state.Connecting || working && !connected;
        connection.Text = L.T(working && !connected || state.Connecting ? "systemPageIdentifying" : connected ? "systemPageConnected" : "systemPageFailed")
            + "\n" + L.T(state.Connecting ? "systemPageConnectingHelp" : state.Legacy ? "systemDiscoveryLegacy" : "systemDiscoveryDirect");
        connection.AccessibleDescription = state.Error;
        summary.Text = connected ? L.F("systemPageSummary", BitOperations.PopCount((uint)state.Found), BitOperations.PopCount((uint)state.Hidden), BitOperations.PopCount((uint)(state.Requested & ~state.Found)))
            : L.F("systemPageRetained", BitOperations.PopCount((uint)state.Requested));
        reconnect.Text = L.T(connected ? "systemPageReconnect" : "systemPageRetry"); reconnect.Enabled = scheme.Enabled = !working && !state.Connecting;
        restoreAll.Enabled = undo.Enabled = connected && !working; undo.Enabled &= previous.HasValue;
        foreach (var card in cards)
        {
            int group = SystemIconCard.GroupKey(card.Mask) == "systemPageCommon" ? 1 : SystemIconCard.GroupKey(card.Mask) == "systemPageIndicators" ? 2 : 3;
            card.Visible = filter == 0 || filter == group; card.UpdateState(state, card.Mask == selected, working);
        }
        toggleSelected.Text = L.T((state.Requested & selected) != 0 ? "restoreSelected" : "hideSelected"); toggleSelected.Enabled = connected && !working;
        UpdateDetails(); ReflowCards();
    }
    void UpdateDetails()
    {
        string signature = selected + ":" + state.StateKey(selected) + ":" + state.Error + ":" + state.Diagnostic + ":" + UiTheme.Dark;
        if (detailSignature == signature) return; detailSignature = signature; details.SuspendLayout();
        while (details.Controls.Count > 0) { var control = details.Controls[0]; details.Controls.RemoveAt(0); control.Dispose(); }
        void Line(string text, bool heading = false)
        {
            int width = Math.Max(120, details.ClientSize.Width - 20);
            var label = new Label { Text = text, Width = width, ForeColor = heading ? UiTheme.Ink : UiTheme.Muted, Margin = new(0, 5, 0, 5) };
            label.Height = Math.Max(heading ? 28 : 38, TextRenderer.MeasureText(text, Font, new Size(width, 0), TextFormatFlags.WordBreak).Height + 8);
            if (heading) { label.Font = new(Font.FontFamily, 10, FontStyle.Bold); label.Disposed += (_, _) => label.Font.Dispose(); } details.Controls.Add(label);
        }
        var item = SystemIconCatalog.Items.Single(item => item.Mask == selected);
        Line(L.T(item.Name), true); Line(L.T(state.StateKey(selected))); Line(L.T("systemPageDescription" + selected));
        if (state.Connecting) Line(L.T("systemPageLoadingHelp"));
        else if (!state.Connected) Line(L.T("systemPageUnavailableHelp"));
        else if ((state.Found & selected) == 0) { Line(L.T("systemNativeNotFound"), true); Line(L.T("systemPageMissingHelp")); }
        if ((selected & 48) != 0) { Line(L.T("systemPageSharedTitle"), true); Line(L.T("systemPageSharedHelp")); }
        Line(L.T("systemPageSaveTitle"), true); Line(L.T("systemPageSaveHelp"));
        if (state.Error.Length > 0 || state.Diagnostic.Length > 0)
        {
            var diagnostic = UiTheme.Button(L.T("systemPageDiagnostic")); diagnostic.Click += (_, _) =>
            { using var info = InfoDialog.Create(L.T("systemPageDiagnostic"), new[] { new KeyValuePair<string, string>(L.T("systemPageDiagnostic"), state.Error + "\n" + state.Diagnostic) }, Font); info.ShowDialog(FindForm()); };
            details.Controls.Add(diagnostic);
        }
        details.ResumeLayout(true);
    }
    async Task<bool> RunAsync(Func<Task<SystemIconsSnapshot>> operation, string success, bool remember = false, bool connecting = false)
    {
        if (working || ReadOnlyPreview) return false;
        Control? returnFocus = cards.Select(card => (Control)card.Toggle).Concat(new Control[] { reconnect, scheme, restoreAll, toggleSelected, undo }).FirstOrDefault(control => control.Focused);
        var focusAnchor = cards.FirstOrDefault(card => card.Mask == selected);
        int before = state.Requested; working = true; poll.Stop();
        if (returnFocus != null) focusAnchor?.Focus();
        if (connecting) state = state with { Connecting = true };
        feedback.ForeColor = UiTheme.Muted; feedback.Text = L.T(connecting ? "systemNativeConnecting" : "systemPageSaving"); Render();
        try
        {
            var result = await operation(); if (IsDisposed) return false;
            state = result; if (remember) previous = before; feedback.Text = success;
            return true;
        }
        catch (Exception ex)
        {
            if (!IsDisposed)
            {
                SystemIconsSnapshot actual;
                try { actual = read(); } catch { actual = state with { Connected = false, Connecting = false }; }
                state = actual with { Error = ex.Message };
                feedback.Text = ex.Message; feedback.ForeColor = UiTheme.Dark ? Color.FromArgb(255, 158, 158) : Color.Firebrick;
            }
            return false;
        }
        finally
        {
            working = false;
            if (!IsDisposed)
            {
                Render(); if (returnFocus is { Enabled: true, Visible: true } && focusAnchor?.Focused == true) returnFocus.Focus();
                if (Visible && pollRequested) poll.Start();
            }
        }
    }
    internal Task ToggleAsync(int bit)
    {
        if (working || !state.Connected || state.Connecting || !SystemIconCatalog.Items.Any(item => item.Mask == bit)) return Task.CompletedTask;
        selected = bit; bool hide = (state.Requested & bit) == 0;
        int mask = hide ? state.Requested | bit : state.Requested & ~bit;
        return RunAsync(() => change(mask, hide ? 0 : bit), L.T("systemPageSaved"), remember: true);
    }
    internal Task RestoreAllAsync() => !state.Connected || working ? Task.CompletedTask : RunAsync(() => change(0, SystemIconCatalog.All), L.T("systemPageRestored"), remember: true);
    internal async Task UndoAsync()
    {
        if (previous is not { } mask || !state.Connected || working) return;
        int current = state.Requested;
        bool succeeded = await RunAsync(() => change(mask, current & ~mask), L.T("ruleUndoSuccess"));
        if (succeeded) { previous = null; Render(); }
    }
    internal Task ReconnectAsync() => RunAsync(connect, L.T("systemPageConnected"), connecting: true);
    internal Task ChangeDiscoveryAsync(bool legacy) => RunAsync(() => discovery(legacy), L.T("systemPageConnected"), connecting: true);
    void ConfirmRestore()
    {
        if (working || !state.Connected) return;
        if (MessageBox.Show(FindForm(), L.T("systemPageRestoreMessage"), L.T("systemPageRestoreAll"), MessageBoxButtons.OKCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.OK) _ = RestoreAllAsync();
    }
    void OpenCompatibility()
    {
        if (working) return;
        using var dialog = new Form { Text = L.T("systemPageScheme"), ClientSize = new(570, 330), Font = Font, FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false, StartPosition = FormStartPosition.CenterParent, Padding = new(24) };
        var modern = new RadioButton { Text = L.T("systemDiscoveryDirect"), Appearance = Appearance.Button, Dock = DockStyle.Top, Height = 46, Checked = !state.Legacy };
        var legacy = new RadioButton { Text = L.T("systemDiscoveryLegacy"), Appearance = Appearance.Button, Dock = DockStyle.Top, Height = 46, Checked = state.Legacy };
        UiTheme.Toggle(modern); UiTheme.Toggle(legacy);
        var hint = new Label { Text = L.T("systemPageCompatibilityHelp"), Dock = DockStyle.Top, Height = 108, ForeColor = UiTheme.Muted };
        var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, FlowDirection = FlowDirection.RightToLeft };
        var confirm = UiTheme.Button(L.T("systemPageUseScheme"), true); confirm.DialogResult = DialogResult.OK;
        var cancel = UiTheme.Button(L.T("cancel")); cancel.DialogResult = DialogResult.Cancel; footer.Controls.Add(confirm); footer.Controls.Add(cancel);
        dialog.Controls.Add(legacy); dialog.Controls.Add(modern); dialog.Controls.Add(hint); dialog.Controls.Add(footer); dialog.AcceptButton = confirm; dialog.CancelButton = cancel; UiTheme.Apply(dialog);
        if (dialog.ShowDialog(FindForm()) == DialogResult.OK) _ = ChangeDiscoveryAsync(legacy.Checked);
    }
    protected override void Dispose(bool disposing) { if (disposing) poll.Dispose(); base.Dispose(disposing); }
}
