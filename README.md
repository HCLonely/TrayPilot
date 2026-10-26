# TrayPilot

Keep your Windows tray tidy: hide unwanted icons while their applications keep running.

[简体中文](README_CN.md) | English

![Example](./Example.gif?raw=true)

## Download and get started

For **Windows 11 x64**, primarily tested on Windows 11 25H2.

1. Visit the [download page](https://github.com/HCLonely/TrayPilot/releases) and choose **Full (recommended)**. Choose the smaller **Lite** package if you already have .NET 10 Desktop Runtime x64 installed.
2. Extract the package to a folder you plan to keep, then run `TrayPilot.exe`. Full includes .NET. The interface also requires WebView2 Evergreen Runtime, normally available on Windows 11.
3. Find an unwanted icon and **double-click to hide it; double-click again to restore it**.
4. Closing the window keeps TrayPilot running in the tray by default. Choose **Exit** from its menu to quit.

Hidden icons disappear from both the taskbar and its overflow menu. **Their applications stay running.** Restored icons return to their usual location according to your Windows settings.

## Everyday controls

| I want to… | What to do |
| --- | --- |
| Hide or restore one icon | Double-click its icon or row |
| Change several icons at once | Ctrl / Shift-click to select, then choose **Hide selected** / **Restore selected** |
| Keep an icon hidden automatically | Select it, choose **Create hide rule** in Details, and select single-icon scope |
| Manage all icons from one application | Choose program scope when creating a rule; quick controls group icons by application |
| Find an application | Search by name or path |
| Stop hiding an icon automatically | Open **Hide rules** and remove its rule |
| Restore icons hidden by TrayPilot | Choose **Restore all** |

Per-program show/hide shortcuts are unassigned by default. Set or clear one from icon details, the icon context menu, or the corresponding rule's context menu. A shortcut toggles all tray icons with the same executable path: hide all if any are visible, otherwise restore all. Restoring keeps hide rules and temporarily shows the program for this session. Bindings remain saved when the program is offline. Include Ctrl or Alt; occupied and duplicate shortcuts are rejected.

Each hide-rule item supports a context menu (right-click or Shift+F10) for editing, temporarily showing matched icons, setting its program shortcut, copying its path, and deleting the rule.

Both list and grid layouts support double-clicks and multiple selection. Faded items are hidden; rule labels indicate an automatic hide rule.

Ordinary application icons prefer the actual image stored inside Explorer, updated with page refreshes and shared by the list and tray quick panel without screen capture. Initial preparation or a Windows update may require background symbol downloads; existing icons remain available meanwhile. Overflow, hidden or inaccessible images fall back to the Windows snapshot, executable icon and generic icon, in that order. Successful retries restore the live image. The primary taskbar's instantiated icon models are currently verified; live access to every overflow or hidden icon is not guaranteed.

The selected icon’s Details area offers **Program properties** and **End task**. Ending a task closes the application and may lose unsaved work. Use hiding when you only want its icon gone.

## Automatic hiding and restoration

Once you add a hide rule, TrayPilot automatically hides matching icons while it is running. Rules are saved for future launches.

- **Show an icon temporarily**: restore it manually to keep it visible for this session. Choose **Hide matching icons** to apply the rules again.
- **Restore all**: restore only application tray icons hidden by TrayPilot while keeping application hide rules. System-icon visibility and hiding choices are preserved.
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

While TrayPilot is running, selected language and supplementary language icons are hidden again on visibility changes and taskbar layout updates to reduce flashes when returning from RDP. Periodic checks remain as a fallback. Actual RDP transitions need verification on the target Windows version; some control-recreation scenarios may still flash.

Only **Language bar** and **Language supplementary icons** offer a **Force show** switch in their details, off by default and saved independently. Clear the control's hide preference before enabling force show. While enabled, TrayPilot reverses attempts to hide an existing control, and hiding actions are disabled. When RDP focus excludes the local language icon, TrayPilot retains its last valid local data and resumes live updates upon returning locally. Turn force show off before hiding; disabling it releases data retention and visibility protection. Enforcement continues with the main window closed and resumes after restarting TrayPilot. Startup primes the cache from existing icons; an icon that has never supplied valid data must still appear first.

Force show also protects the content path and language-stack containers. Data interfaces are verified using symbols matching the installed Windows version; first connection may require a symbol download. If returning from RDP still leaves the control waiting to appear, expand **Connection diagnostics** in its details and copy the `Language structural transitions` section. The last 8 structural changes and retention counters exclude input text and window titles. Incompatible symbols or interfaces produce a connection error rather than claiming force show succeeded.

The page prefers images rendered from Explorer's internal controls, preserving their current appearance and colors without screen capture. Hidden or absent controls and image retrieval failures automatically fall back to the existing font rendering or built-in icons. Successful retries restore the Explorer image.

Keep **Updated compatibility method (recommended)** selected for normal use. The first connection, or a connection after a Windows update, may need internet access and a short wait. If it fails, try again later or try the other detection method.

System-icon choices are separate from application hide rules. The main window's **Restore all** restores only application tray icons. **Restore all** in the **System icons** window restores only system controls and clears their saved hiding choices, without affecting application icons or rules. **Show all icons** still clears saved system-icon hiding choices; set them again if you want them hidden later. Both operations retain force-show preferences. **Undo last change** in the system page restores both hide and force-show selections from the previous operation.

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
