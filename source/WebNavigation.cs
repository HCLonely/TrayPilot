namespace TrayPilot;

internal sealed partial class MainForm
{
    bool TryLeaveSettings(string? destination = null)
    {
        if (!WebSettingsDirty) return true;
        if (webSettingsSaving) return false;
        return destination != null && RequestWebSettingsLeave(destination);
    }
    void NavigateWebPage(string page)
    {
        if (busy || closing || webSystemWorking || !TryLeaveSettings(page)) return;
        HideWebTray();
        if (page != "system") DeactivateSystemPage();
        if (page != "about") CancelWebUpdate();
        SetWebPageVisibility(true, page);
        if (page == "settings") StartWebSettingsPage();
        if (page == "system") StartWebSystemPage();
        PublishWebState(force: true);
        if (initialized && !ListVisible) OpenMainWindow();
        webInterface?.Focus(); UpdateTimer();
    }
    void ShowIconsPage() => NavigateWebPage("icons");
    void ShowRulesPage() => NavigateWebPage("rules");
    void ShowSystemIcons() => NavigateWebPage("system");
    void ShowSettings() => NavigateWebPage("settings");
    void ShowAbout() => NavigateWebPage("about");
}
