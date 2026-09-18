namespace TrayPilot;

internal sealed partial class MainForm
{
    SystemIconSession? systemIconSession;
    internal SystemIconSession? systemIconSessionForDiagnostics => systemIconSession is { Ticks: > 0 } ? systemIconSession : null;
    bool systemIconsConnecting;
    int systemIconsRequested;
    readonly System.Windows.Forms.Timer systemIconWatch = new() { Interval = 5000 };
    void SetupSystemIconWatch()
    {
        systemIconWatch.Tick += async (_, _) =>
        {
            if (systemIconsRequested == 0 || systemIconsConnecting || closing || IsDisposed ||
                systemIconSession?.Pid == SystemIconSession.ExplorerPid()) return;
            try { var session = await ConnectSystemIcons(); session.Set(systemIconsRequested); }
            catch (Exception ex) { if (!IsDisposed) status.Text = ex.Message; }
        };
        systemIconWatch.Start();
    }
    void RestoreLiveSystemIcons()
    {
        systemIconsRequested = 0;
        systemIconSession?.Set(0);
    }
    async Task<SystemIconSession> ConnectSystemIcons()
    {
        if (systemIconSession != null && systemIconSession.Pid == SystemIconSession.ExplorerPid() && systemIconSession.Ticks > 0 && systemIconSession.Error == 0) return systemIconSession;
        if (systemIconsConnecting) throw new IOException(L.T("systemNativeConnecting"));
        systemIconsConnecting = true;
        try
        {
            if (systemIconSession != null) { systemIconSession.Dispose(); systemIconSession = null; await Task.Delay(500); }
            var session = await Task.Run(() => new SystemIconSession());
            if (IsDisposed || closing) { session.Dispose(); throw new ObjectDisposedException(nameof(MainForm)); }
            systemIconSession = session;
            for (int i = 0; i < 40 && session.Ticks == 0 && session.Error == 0; i++) await Task.Delay(100);
            if (session.Ticks == 0 || session.Error != 0)
            {
                int error = session.Error; session.Dispose(); systemIconSession = null;
                throw new IOException(L.F("systemNativeFailed", $"0x{error:X8}"));
            }
            if (systemIconsRequested != 0) session.Set(systemIconsRequested);
            return session;
        }
        finally { systemIconsConnecting = false; }
    }
    void ShowSystemIcons()
    {
        var existing = Application.OpenForms.Cast<Form>().FirstOrDefault(x => x.Name == "LiveSystemIconsDialog");
        if (existing != null) { existing.Activate(); return; }
        using var dialog = new Form { Name = "LiveSystemIconsDialog", Text = L.T("systemIcons"), ClientSize = new(840, 650),
            MinimumSize = new(780, 570), StartPosition = FormStartPosition.CenterParent, Font = Font, ShowInTaskbar = false };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new(20), ColumnCount = 1, RowCount = 4 };
        layout.RowStyles.Add(new(SizeType.AutoSize)); layout.RowStyles.Add(new(SizeType.Percent, 100));
        layout.RowStyles.Add(new(SizeType.AutoSize)); layout.RowStyles.Add(new(SizeType.AutoSize));
        var help = new Label { Text = L.T("systemLiveHelp"), AutoSize = true, MaximumSize = new(720, 0), Margin = new(0, 0, 0, 15) };
        var rows = new TrayListView { Name = "SystemIconRows", Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false };
        using var icons = new ImageList { ColorDepth = ColorDepth.Depth32Bit, ImageSize = new(24 * DeviceDpi / 96, 24 * DeviceDpi / 96) };
        _ = icons.Handle; // Copy bitmaps immediately before their drawing resources are disposed.
        rows.SmallImageList = icons;
        var appearances = new (string Text, string Font)[SystemIconCatalog.Items.Length];
        rows.Columns.Add(L.T("systemIconTarget"), 270); rows.Columns.Add(L.T("iconState"), 460);
        foreach (var icon in SystemIconCatalog.Items)
        {
            using var bitmap = SystemIconImages.Create(icon.Mask, icons.ImageSize.Width);
            icons.Images.Add(bitmap);
            var row = new ListViewItem(L.T(icon.Name), icons.Images.Count - 1) { Tag = icon.Mask }; row.SubItems.Add(L.T("systemNativeConnecting")); rows.Items.Add(row);
        }
        var state = new Label { Text = L.T("systemNativeConnecting"), AutoSize = true, MaximumSize = new(720, 0), Margin = new(0, 10, 0, 10) };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        var hide = UiTheme.Button(L.T("hideSelected")); var restore = UiTheme.Button(L.T("restoreSelected"));
        var refresh = UiTheme.Button(L.T("refresh"));
        var close = UiTheme.Button(L.T("close"));
        hide.Name = "HideSystemIcon"; restore.Name = "RestoreSystemIcon";
        buttons.Controls.AddRange(new Control[] { hide, restore, refresh, close });
        layout.Controls.Add(help); layout.Controls.Add(rows); layout.Controls.Add(state); layout.Controls.Add(buttons); dialog.Controls.Add(layout);
        bool operating = false;
        int SelectedMask() => rows.SelectedItems.Count == 1 ? (int)rows.SelectedItems[0].Tag! : 0;
        bool Connected() => systemIconSession is { Ticks: > 0, Error: 0, IsDisposed: false } session && session.Pid == SystemIconSession.ExplorerPid();
        bool ShouldHide(int bit) => systemIconSession is { } session && ((session.Requested | session.Hidden) & bit) == 0;
        using var menu = new ContextMenuStrip();
        var toggleItem = new ToolStripMenuItem { Name = "ToggleSystemIcon" };
        menu.Items.Add(toggleItem);
        rows.ContextMenuStrip = menu;
        void UpdateRows()
        {
            var session = systemIconSession;
            bool connected = Connected();
            var currentAppearances = connected ? session!.ReadAppearances() : new (string Text, string Font)[appearances.Length];
            foreach (ListViewItem row in rows.Items)
            {
                int bit = (int)row.Tag!;
                int index = row.Index;
                if (appearances[index] != currentAppearances[index])
                {
                    using var bitmap = SystemIconImages.Create(bit, icons.ImageSize.Width, currentAppearances[index]);
                    icons.Images[index] = bitmap;
                    appearances[index] = currentAppearances[index];
                    rows.Invalidate(row.Bounds);
                }
                string caption = !connected ? L.T("systemNativeUnavailable") : (session!.Found & bit) == 0 ?
                    L.T((session.Requested & bit) != 0 ? "systemNativeWaiting" : "systemNativeNotFound") :
                    (session.Hidden & bit) != 0 ? L.T("systemNativeHidden") :
                    (session.Requested & bit) != 0 && session.SharedMicrophoneLocation && (bit & 48) != 0 ? L.T("systemSharedIndicator") : L.T("systemNativeVisible");
                if (row.SubItems[1].Text != caption) row.SubItems[1].Text = caption;
            }
            int selected = rows.SelectedItems.Count == 1 ? (int)rows.SelectedItems[0].Tag! : 0;
            hide.Enabled = restore.Enabled = !operating && connected && selected != 0;
            toggleItem.Enabled = hide.Enabled;
            toggleItem.Text = L.T(ShouldHide(selected) ? "hideSelected" : "restoreSelected");
            refresh.Enabled = !operating;
        }
        async Task Connect()
        {
            if (operating) return;
            operating = true; UpdateRows(); state.Text = L.T("systemNativeConnecting");
            try { await ConnectSystemIcons(); if (!dialog.IsDisposed) state.Text = L.T("systemLiveReady"); }
            catch (Exception ex) { if (!dialog.IsDisposed) state.Text = ex.Message; }
            finally { operating = false; if (!dialog.IsDisposed) UpdateRows(); }
        }
        async Task Change(int bit, bool hidden)
        {
            if (operating || bit == 0 || !Connected() || systemIconSession is not { } session) return;
            int previous = session.Requested;
            operating = true; UpdateRows();
            try
            {
                int id = session.Set(hidden ? previous | bit : previous & ~bit);
                for (int i = 0; i < 30 && session.Acknowledged != id && session.Error == 0; i++) await Task.Delay(100);
                bool sharedPending = session.SharedMicrophoneLocation && (bit & 48) != 0 && (session.Requested & 48) != 48;
                if (session.Acknowledged != id || session.Error != 0 ||
                    (hidden && (session.Found & bit) != 0 && (session.Hidden & bit) == 0 && !sharedPending))
                    throw new IOException(L.F("systemNativeFailed", $"0x{session.Error:X8}"));
                systemIconsRequested = session.Requested;
                if (!dialog.IsDisposed) state.Text = L.T("systemLiveReady");
            }
            catch (Exception ex)
            {
                if (!IsDisposed && !closing && ReferenceEquals(systemIconSession, session) && !session.IsDisposed) session.Set(previous);
                if (!dialog.IsDisposed) state.Text = ex.Message;
            }
            finally { operating = false; if (!dialog.IsDisposed) UpdateRows(); }
        }
        hide.Click += async (_, _) => await Change(SelectedMask(), true); restore.Click += async (_, _) => await Change(SelectedMask(), false);
        toggleItem.Click += async (_, _) => { int bit = SelectedMask(); await Change(bit, ShouldHide(bit)); };
        rows.MouseDoubleClick += async (_, e) =>
        {
            if (e.Button == MouseButtons.Left && rows.ItemAt(e.Location)?.Tag is int bit) await Change(bit, ShouldHide(bit));
        };
        rows.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Right) return;
            var hit = rows.ItemAt(e.Location);
            foreach (ListViewItem row in rows.Items) row.Selected = row == hit;
            if (hit != null) hit.Focused = true;
        };
        rows.KeyDown += async (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.Handled = e.SuppressKeyPress = true;
            int bit = SelectedMask(); await Change(bit, ShouldHide(bit));
        };
        menu.Opening += (_, e) => { UpdateRows(); e.Cancel = !toggleItem.Enabled; };
        refresh.Click += async (_, _) => await Connect();
        close.Click += (_, _) => dialog.Close(); rows.SelectedIndexChanged += (_, _) => UpdateRows();
        using var poll = new System.Windows.Forms.Timer { Interval = 500 };
        poll.Tick += (_, _) => UpdateRows(); dialog.Shown += async (_, _) => { poll.Start(); await Connect(); };
        dialog.FormClosing += (_, e) => { if (operating && !systemIconsConnecting) e.Cancel = true; };
        dialog.CancelButton = close; UiTheme.Apply(dialog); UiTheme.Apply(menu); UpdateRows(); dialog.ShowDialog(this);
    }
}
