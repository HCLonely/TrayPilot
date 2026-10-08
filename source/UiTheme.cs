namespace TrayPilot;

internal static class UiTheme
{
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Form, Icon> FormIcons = new();
    internal static bool Dark { get; private set; }
    internal static Color Canvas => Dark ? Color.FromArgb(16, 23, 36) : Color.FromArgb(233, 238, 247);
    internal static Color Ink => Dark ? Color.FromArgb(234, 240, 250) : Color.FromArgb(23, 36, 59);
    internal static Color Muted => Dark ? Color.FromArgb(166, 180, 202) : Color.FromArgb(82, 97, 120);
    internal static Color Header => Dark ? Color.FromArgb(37, 50, 71) : Color.FromArgb(241, 245, 250);

    internal static bool Set(string mode)
    {
        bool dark = mode == "dark";
        if (mode != "light" && mode != "dark")
        {
            try { dark = (int?)Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) == 0; }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
        }
        bool changed = dark != Dark; Dark = dark; return changed;
    }

    internal static void Apply(Form form)
    {
        form.BackColor = Canvas; form.ForeColor = Ink;
        if (!FormIcons.TryGetValue(form, out var icon))
        {
            icon = AppIcon.Create(); FormIcons.Add(form, icon); var ownedIcon = icon;
            form.Disposed += (_, _) => ownedIcon.Dispose();
        }
        form.Icon = icon;
        if (form.IsHandleCreated)
        {
            int dark = Dark ? 1 : 0; Native.DwmSetWindowAttribute(form.Handle, 20, ref dark, sizeof(int));
            if (!SystemInformation.HighContrast)
            {
                int backdrop = 2, corners = 2;
                Native.DwmSetWindowAttribute(form.Handle, 38, ref backdrop, sizeof(int));
                Native.DwmSetWindowAttribute(form.Handle, 33, ref corners, sizeof(int));
            }
        }
    }
}
