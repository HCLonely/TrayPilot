namespace TrayPilot;

internal sealed class AboutPage : UserControl
{
    readonly Func<CancellationToken, Task<UpdateChecker.Release?>> check;
    readonly CancellationTokenSource cancellation = new();
    readonly Button checkButton, download, diagnosticToggle;
    readonly Label updateStatus, updateHelp;
    readonly Panel diagnostics;
    readonly DashboardDocument document = new();
    readonly string settingsFolder;
    bool checking;
    UpdateChecker.Release? release;
    internal bool Checking => checking;
    internal string UpdateStatus => updateStatus.Text;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal bool ReadOnlyPreview { get; set; }
    internal AboutPage(string settingsFolder, Font font, Func<CancellationToken, Task<UpdateChecker.Release?>>? check = null)
    {
        this.settingsFolder = settingsFolder; this.check = check ?? (token => UpdateChecker.CheckAsync(token));
        Dock = DockStyle.Fill; Font = font; AutoScaleMode = AutoScaleMode.Dpi;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new(28, 24, 28, 14), ColumnCount = 1, RowCount = 2 };
        root.ColumnStyles.Add(new(SizeType.Percent, 100)); root.RowStyles.Add(new(SizeType.Absolute, 78)); root.RowStyles.Add(new(SizeType.Percent, 100));
        root.Controls.Add(UiTheme.Heading(L.T("aboutDashboardTitle"), L.T("aboutDashboardSubtitle"), font), 0, 0);
        var brand = new SurfacePanel { Height = 144, Padding = new(22) };
        var mark = new PictureBox { Image = AppIcon.Draw(54), SizeMode = PictureBoxSizeMode.Zoom, Size = new(54, 54), Dock = DockStyle.Left };
        mark.Disposed += (_, _) => mark.Image?.Dispose();
        var name = new Label { Text = "TrayPilot  " + UpdateChecker.CurrentVersionText, Font = new(font.FontFamily, 20, FontStyle.Bold), Dock = DockStyle.Top, Height = 40, Padding = new(20, 0, 0, 0) };
        var description = new Label { Text = L.T("applicationDescription") + "\nHCLonely · Copyright © 2026 HCLonely", Dock = DockStyle.Fill, ForeColor = UiTheme.Muted, Padding = new(20, 4, 0, 0) };
        var brandText = new Panel { Dock = DockStyle.Fill }; brandText.Controls.Add(description); brandText.Controls.Add(name); brand.Controls.Add(brandText); brand.Controls.Add(mark); document.Add(brand);
        var update = new SurfacePanel { Height = 174, Padding = new(20) };
        var updateLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3 };
        updateLayout.ColumnStyles.Add(new(SizeType.Percent, 100)); updateLayout.ColumnStyles.Add(new(SizeType.AutoSize));
        updateLayout.RowStyles.Add(new(SizeType.Absolute, 32)); updateLayout.RowStyles.Add(new(SizeType.Absolute, 46)); updateLayout.RowStyles.Add(new(SizeType.Percent, 100));
        updateLayout.Controls.Add(new Label { Text = L.T("aboutUpdates"), UseMnemonic = false, Dock = DockStyle.Fill, Font = new(font.FontFamily, 10, FontStyle.Bold) }, 0, 0);
        updateStatus = new Label { Text = L.T("aboutNotChecked"), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        updateHelp = new Label { Text = L.T("aboutUpdateHelp"), Dock = DockStyle.Fill, ForeColor = UiTheme.Muted };
        checkButton = UiTheme.Button(L.T("checkForUpdates")); checkButton.Name = "checkForUpdates"; checkButton.Click += async (_, _) => await CheckAsync();
        download = UiTheme.Button(L.T("aboutDownloadRelease"), true); download.Visible = false; download.Click += (_, _) => { if (release != null) OpenLink(release.Url); };
        updateLayout.Controls.Add(updateStatus, 0, 1); updateLayout.Controls.Add(checkButton, 1, 1); updateLayout.Controls.Add(updateHelp, 0, 2); updateLayout.Controls.Add(download, 1, 2); update.Controls.Add(updateLayout); document.Add(update);
        var links = new SurfacePanel { Height = 138, Padding = new(20) };
        links.Controls.Add(new Label { Text = L.T("aboutHelpLinks"), UseMnemonic = false, Dock = DockStyle.Top, Height = 36, Font = new(font.FontFamily, 10, FontStyle.Bold) });
        var linkBar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 46, WrapContents = true };
        foreach (var (key, suffix) in new[] { ("openProject", ""), ("aboutUsage", "#readme"), ("aboutFeedback", "/issues"), ("aboutReleases", "/releases") })
        { var button = UiTheme.Button(L.T(key)); button.Click += (_, _) => OpenLink(UpdateChecker.ProjectUrl + suffix); linkBar.Controls.Add(button); }
        links.Controls.Add(linkBar); document.Add(links);
        var credits = new SurfacePanel { Height = 114, Padding = new(20) };
        credits.Controls.Add(new Label { Text = L.T("aboutCredits"), Dock = DockStyle.Fill, ForeColor = UiTheme.Muted });
        var windhawk = UiTheme.Button("Windhawk · Taskbar tray system icon tweaks"); windhawk.Dock = DockStyle.Bottom;
        windhawk.Click += (_, _) => OpenLink("https://github.com/ramensoftware/windhawk-mods/blob/main/mods/taskbar-tray-system-icon-tweaks.wh.cpp"); credits.Controls.Add(windhawk); document.Add(credits);
        diagnosticToggle = UiTheme.Button(L.T("aboutShowDiagnostics")); document.Add(diagnosticToggle);
        diagnostics = new SurfacePanel { Height = 252, Padding = new(20), Visible = false };
        diagnosticToggle.Click += (_, _) => { diagnostics.Visible = !diagnostics.Visible; diagnosticToggle.Text = L.T(diagnostics.Visible ? "aboutHideDiagnostics" : "aboutShowDiagnostics"); };
        var info = new TextBox { ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Text = DiagnosticText(), AccessibleName = L.T("aboutDiagnostics") };
        var copy = UiTheme.Button(L.T("copyInformation")); copy.Dock = DockStyle.Bottom; copy.Click += (_, _) =>
        { if (ReadOnlyPreview) return; try { Clipboard.SetText(DiagnosticText()); } catch (System.Runtime.InteropServices.ExternalException) { updateHelp.Text = L.T("clipboardUnavailableMessage"); } };
        diagnostics.Controls.Add(info); diagnostics.Controls.Add(copy); document.Add(diagnostics);
        root.Controls.Add(document, 0, 1); Controls.Add(root); UiTheme.Apply(this);
    }
    string DiagnosticText() => "TrayPilot " + UpdateChecker.CurrentVersionText + "\r\nHCLonely\r\n" + UpdateChecker.ProjectUrl
        + "\r\n\r\n" + L.T("operatingSystem") + ": " + Environment.OSVersion.VersionString
        + "\r\n" + L.T("applicationPath") + ": " + (Environment.ProcessPath ?? AppContext.BaseDirectory)
        + "\r\n" + L.T("settingsFolder") + ": " + settingsFolder;
    void OpenLink(string url)
    {
        if (ReadOnlyPreview) return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { updateHelp.Text = L.T("openProjectFailed") + " " + ex.Message; }
    }
    internal async Task CheckAsync()
    {
        if (checking || ReadOnlyPreview) return; checking = true; release = null;
        checkButton.Enabled = false; checkButton.Text = updateStatus.Text = L.T("checkingForUpdates"); download.Visible = false;
        try
        {
            var result = await check(cancellation.Token);
            if (IsDisposed || cancellation.IsCancellationRequested) return;
            release = result;
            updateStatus.Text = result == null ? L.T("noPublishedRelease") : result.IsNewer ? L.F("aboutNewVersion", result.Tag) : L.F("alreadyUpToDate", UpdateChecker.CurrentVersionText);
            download.Visible = result?.IsNewer == true; updateHelp.Text = L.T("aboutUpdateHelp");
        }
        catch (Exception ex)
        {
            if (IsDisposed || cancellation.IsCancellationRequested) return;
            updateStatus.Text = L.T(ex is OperationCanceledException ? "updateCheckTimedOut" : "updateCheckFailed"); updateHelp.Text = L.T("aboutRetryHelp");
        }
        finally
        {
            checking = false;
            if (!IsDisposed && !cancellation.IsCancellationRequested) { checkButton.Enabled = true; checkButton.Text = L.T("checkForUpdates"); }
        }
    }
    internal void ShowPreviewFailure() { updateStatus.Text = L.T("updateCheckFailed"); updateHelp.Text = L.T("aboutRetryHelp"); }
    protected override void Dispose(bool disposing)
    { if (disposing && !IsDisposed) { cancellation.Cancel(); cancellation.Dispose(); } base.Dispose(disposing); }
}
