# TrayPilot

Keep your Windows tray tidy: hide unwanted icons while their applications keep running.

[简体中文](README_CN.md) | English

![TrayPilot in action](./Example.gif)

## Download and get started

For **Windows 11 x64**, primarily tested on Windows 11 25H2.

1. Visit the [download page](https://github.com/HCLonely/TrayPilot/releases) and choose **Full (recommended)**. Choose the smaller **Lite** package if you already have .NET 8 Desktop Runtime x64 installed.
2. Extract the package to a folder you plan to keep, then run `TrayPilot.exe`. Full needs no separate runtime installation.
3. Find an unwanted icon and **double-click to hide it; double-click again to restore it**.
4. Closing the window keeps TrayPilot running in the tray by default. Choose **Exit** from its menu to quit.

Hidden icons disappear from both the taskbar and its overflow menu. **Their applications stay running.** Restored icons return to their usual location according to your Windows settings.

## Everyday controls

| I want to… | What to do |
| --- | --- |
| Hide or restore one icon | Double-click its icon or row |
| Change several icons at once | Ctrl / Shift-click to select, then choose **Hide selected** / **Restore selected** |
| Keep an icon hidden automatically | Right-click it and choose **Auto-hide only this icon** |
| Manage all icons from one application | Right-click and choose **All icons for this application** |
| Find an application | Search by name or path |
| Stop hiding an icon automatically | Open **Hide rules** and remove its rule |
| Restore icons hidden by TrayPilot | Choose **Restore all** |

Both list and grid layouts support double-clicks and multiple selection. Faded items are hidden; **✓ Matched** or a corner badge indicates an automatic hide rule.

The right-click menu also offers **Properties** and **End task**. Ending a task closes the application and may lose unsaved work. Use hiding when you only want its icon gone.

## Automatic hiding and restoration

Once you add a hide rule, TrayPilot automatically hides matching icons while it is running. Rules are saved for future launches.

- **Show an icon temporarily**: restore it manually to keep it visible for this session. Choose **Hide matching icons** to apply the rules again.
- **Restore all**: restore icons hidden by TrayPilot while keeping application hide rules.
- **Remove a rule permanently**: delete its entry in **Hide rules**.
- **Exit**: quit without restoring hidden icons by default. Enable **Restore icons on exit** in Settings to restore them when quitting. Rules and remaining recovery records are kept for the next launch.

To hide just one icon from an application that already has an application-wide rule, remove that rule first, then choose **Auto-hide only this icon** for the desired icon.

The list updates every 2.5 seconds by default. Choose **Refresh** if a new icon or state has not appeared yet. Turning off automatic refresh does not pause automatic hiding.

## Tray menu, startup and settings

Click TrayPilot's tray icon to open the main window, or right-click it to quickly show and hide other applications' icons. Use the mouse wheel or Previous / Next page when there are more icons.

In **Settings**, you can:

- Enable **Start with Windows** to run when you sign in, or toggle it directly from the tray menu.
- Choose whether closing the window keeps TrayPilot running or exits.
- Enable **Restore icons on exit** (off by default) to restore application and system icons when quitting, including shutdown or sign-out.
- Switch between light, dark or system appearance, and Simplified Chinese / English.
- Show or hide TrayPilot's own tray icon.
- Enable and customize hotkeys.

| Action | Preset hotkey (enable first) |
| --- | --- |
| Open main window | Ctrl+Alt+M |
| Show all icons | Ctrl+Alt+S |
| Hide matching icons | Ctrl+Alt+H |

**Show all icons** reveals detected hidden icons and pauses automatic hiding. Use **Hide matching icons** to resume it. If a hotkey is already in use, choose another combination in Settings.

Start with Windows is off by default. Manage it through TrayPilot's Settings or tray menu. If you move the application folder, re-enable startup from its new location.

## Volume, network, clock and other system icons

Open **System icons** to manage the primary taskbar's volume, network, battery, clock, language bar, notification bell, Show desktop button and other controls. Double-click an item to toggle its visibility; choices are saved automatically.

Keep **Updated compatibility method (recommended)** selected for normal use. The first connection, or a connection after a Windows update, may need internet access and a short wait. If it fails, try again later or try the other detection method.

System-icon choices are separate from application hide rules. **Restore all** and **Show all icons** clear saved system-icon hiding choices; set them again if you want them hidden later.

## Common questions

**Why is TrayPilot still running after I close its window?**

It stays in the tray by default so automatic hiding can continue. Choose **Exit** from the menu, or change this behavior in Settings.

**I hid TrayPilot's own icon. How do I reopen it?**

Run `TrayPilot.exe` again to bring back the existing window, or use the Open main window hotkey if you enabled it.

**An application is running but missing from the list.**

Check that it has a tray icon, then choose **Refresh**. Some applications may not be detected. Use **System icons** for volume, network, clock and similar Windows controls.

**Icons did not return after an unexpected shutdown.**

Reopen TrayPilot and choose **Restore all**, or restart the affected application. Keep TrayPilot's settings files until your icons are restored.

**What about other Windows versions or secondary monitors?**

TrayPilot primarily targets Windows 11 25H2 x64, with system-icon controls for the primary taskbar. Other versions, secondary taskbars and ARM64 are not fully verified. Windows updates may affect compatibility.

For other problems, [open an issue](https://github.com/HCLonely/TrayPilot/issues) with your TrayPilot version, Windows version and steps to reproduce the problem.

## Development and acknowledgments

See the [developer guide](docs/DEVELOPMENT.md) for building, diagnostics, language packs and implementation details.

The system-icon feature draws on Windhawk's [Taskbar tray system icon tweaks](https://github.com/ramensoftware/windhawk-mods/blob/main/mods/taskbar-tray-system-icon-tweaks.wh.cpp). Thanks to its original author.
