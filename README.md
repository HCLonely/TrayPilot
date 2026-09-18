# TrayPilot

A Windows 11 tray icon manager that hides unwanted icons while keeping their applications running.

[简体中文](README_CN.md) | English

## Quick start

1. Use a published application package, or build from source as described below.
2. Run `TrayPilot.exe` to open the main window.
3. Double-click an icon or row to toggle only that icon. For persistent hiding, right-click and choose **Auto-hide only this icon**. **All icons for this application** provides application-wide controls.
4. Closing the window keeps TrayPilot running in the tray by default. Choose **Exit** to restore icons hidden by TrayPilot and quit.

Hidden icons disappear from both the taskbar and its overflow menu; the application keeps running. Restored icons return to the taskbar or overflow area according to Windows settings.

![Example](./Example.gif?raw=true)

## Main window controls

| Action | Result |
| --- | --- |
| Single-click | Select an item without changing visibility |
| Left double-click | Toggle only the clicked icon |
| Ctrl / Shift-click | Select multiple items, then use **Hide selected** / **Restore selected** |
| Right-click | Open properties, visibility, rule and end-task commands |
| Hover | Inspect state, rules, cached tooltip, process, PID, full path and icon identifier |
| Search | Filter by application name, process or path |
| List / Grid | Switch layouts while preserving the search and selection |

Hidden items have faded icons and text. Rule members display **✓ Matched** in the list and a corner badge in the grid, independently of their current visibility. Both layouts support multiple selection, tooltips and double-click actions. Only the hovered item receives a bright blue highlight.

**Properties** presents process, icon and file-version information in a read-only table. Select rows and choose **Copy information**, or copy all information when no rows are selected.

**End task** forcefully terminates the process owning the clicked icon; unsaved work is not saved automatically. TrayPilot checks its path and start time first, and does not terminate other processes with the same name or child processes. Hide rules are retained. Use **Exit** for TrayPilot itself. Explorer-hosted icons such as Safely Remove Hardware and Eject Media cannot be ended as tasks.

## Hide rules and restoration

Application rules match full executable paths, ignoring case and path-separator differences. Existing rules retain this scope. Individual icon rules match the path and GUID, or UID plus window class, so they survive process restarts. Instances sharing the same icon identifier also match; changing identifiers requires a new rule.

Multiple icons for one application display their identifiers and PIDs. Double-click, the individual context-menu toggle, and Hide/Restore selected affect only the chosen icons. To replace an existing application rule with an individual rule, remove the application rule from **Hide rules**, then choose **Auto-hide only this icon** on the desired icon. Multi-icon applications also expose individual controls in the tray submenu.

| Command | Effect on icons and rules |
| --- | --- |
| Add to matching rules | Save the path and apply hiding immediately unless automatic hiding is paused |
| Manually hide / restore | Keep rules unchanged; restoring one matched icon temporarily excludes only that icon from automatic hiding for this session |
| Hide matching icons | Hide matching icons, clear temporary exceptions and resume automatic hiding |
| Show all icons (hotkey) | Show detected hidden icons and TrayPilot's own icon, retain rules and pause automatic hiding |
| Restore all (main window button) | Restore icons hidden by TrayPilot, retaining all rules and temporarily skipping automatic hiding for restored paths during this session |
| Exit | Restore icons hidden by TrayPilot and quit, retaining rules for the next launch |

**Restore all, individual restoration and manual hiding never add or remove matching rules.** The automatic-hiding pause state survives restarts. Temporary exceptions from manual restoration (including Restore all) do not survive a restart and are also cleared by **Hide matching icons**.

Open **Hide rules** from the top menu to view saved rules, including applications that are not running. Entries show an icon, name and full path, and support multiple deletion. Removing a rule restores its icons first; failed restoration retains the rule for retry. Missing executables use a fallback icon.

Automatic refresh runs every **2.5 seconds** by default. Turning it off stops periodic checks without restoring icons or changing rules; a check already in progress finishes. You can still use **Refresh**, and visibility actions also update the list. Manual refresh still applies rules when automatic hiding is active, so disabling auto-refresh does not pause automatic hiding. Auto-refresh starts enabled on every launch.

Refresh preserves search text, focus, caret / text selection and existing item selection. Search edits made during a refresh apply to the updated results. Unchanged content does not rebuild the list.

## System tray and startup

Left-click or double-click TrayPilot's tray icon to open the main window; right-click for quick controls.

- Quick controls list detected applications independently of the main search filter, with up to 10 applications per page.
- A check mark means all tray icons for that application are shown. Applications with multiple icons open a submenu with individual toggles and an **All icons for this application** toggle.
- Use **Previous page / Next page** or the mouse wheel to navigate. Scrolling stops at either end.
- Every page retains the **TrayPilot (this application)** visibility toggle. Its icon can also be toggled in Settings. Run the executable again to reopen the existing window when its icon is hidden.
- The menu also provides **Refresh, Start with Windows, Settings, About and Exit**. About displays the version, operating system, executable path, settings folder and language folder.

**Keep running in the tray when the window is closed** is enabled by default; active rules continue to run in the background. Disable this setting to restore icons and exit when closing the window.

**Start with Windows** is off by default. Enable it by saving Settings or toggle it immediately in the tray menu. It starts TrayPilot when the current user signs in, without administrator permissions. Startup launches stay in the tray unless TrayPilot's own icon is disabled, in which case the main window opens. Manual launches open the main window. Disabling removes the startup entry; re-enable it from the new location after moving the executable.

## Hotkeys, appearance and language

Enable each hotkey separately in **Settings**, focus its input, press Ctrl or Alt with another key, and save. All three start disabled. Duplicate or occupied combinations are rejected while retaining the previous settings.

| Action | Preset combination |
| --- | --- |
| Open main window | Ctrl+Alt+M |
| Show all icons | Ctrl+Alt+S |
| Hide matching icons | Ctrl+Alt+H |

Hotkeys remain available while the main window is hidden. The former quick-controls hotkey was removed; its binding is not reused for a new action.

**Appearance** offers Follow system (default), Light and Dark across the main window, Settings, Properties, About, rules and menus. Follow system responds to system theme changes.

**Language** offers Simplified Chinese and English. On first launch or when no language is configured, the system UI language is matched to an available language pack; unsupported languages fall back to English. Manual selections take effect after saving and are retained on subsequent launches. Language, theme, close-to-tray behavior, own-icon visibility, automatic-hiding pause state and hotkeys are persisted. Application names, paths and third-party tooltip text retain their original language.

### Add a language pack

Runtime language packs are in `languages/`; source packs are in `source/languages/`. They use UTF-8 JSON:

```json
{
  "Name": "English",
  "Strings": {
    "mainWindowTitle": "TrayPilot · Tray Icon Manager",
    "settings": "Settings"
  }
}
```

Keys are short, stable semantic English identifiers in `camelCase` (for example, `mainWindowTitle` and `trayIconDetails`), without display punctuation, line breaks or format placeholders. Copy an existing complete pack and translate only the values in `Strings`, retaining keys and placeholders such as `{0}` and `{1}`. The filename without its extension identifies the language; `Name` is its label in Settings. Reopen Settings to discover added packs. Missing translations fall back to built-in English. Missing or malformed packs do not prevent normal icon management.

## Configuration and recovery

Settings, hide rules and recovery records are stored in:

```text
%LOCALAPPDATA%\TrayPilot\settings.json
```

Recovery records are saved before hiding icons. On startup, TrayPilot attempts to restore recorded icons before applying the current rules. It also restores icons before a normal exit; if restoration fails, it retains the records and cancels exit so you can retry.

After a forced termination, reopen TrayPilot and choose **Restore all**, or restart the affected application so it recreates its icons. Do not delete recovery records while icons remain hidden.

TrayPilot does not modify other applications' settings or write to Windows tray `NotifyIconSettings`. Start with Windows separately uses the current user's `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` entry and clears TrayPilot's corresponding `StartupApproved\Run` state when toggled.

## Compatibility and implementation

This project is a prototype targeting **Windows 11 25H2**. Other Windows versions, future patches and unusual applications require validation on the actual system.

- Icons come from the Windows cache, then the executable, then a generic fallback. Cached icons and tooltips may be stale. Names mainly come from executable file descriptions, so scripts may display their host application's name.
- Explorer's built-in volume, network, battery and clock controls are outside the managed scope.
- Missing tray records, unmatched paths, protected processes and special implementations may prevent discovery or control.

## Build and diagnostics

### Local build

On Windows, install the .NET 8 SDK or a newer SDK supporting `net8.0-windows`, then run from the repository root:

```powershell
dotnet publish .\source\TrayPilot.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o .\app
```

The output includes `app/TrayPilot.exe` and the separate `app/languages/` folder. Source code is under `source/`, using C#, Windows Forms and Win32 APIs.

Run diagnostics in a signed-in Windows desktop session and wait for each process to finish:

```powershell
Start-Process .\app\TrayPilot.exe -ArgumentList '--scan', '.\scan.json' -Wait
Start-Process .\app\TrayPilot.exe -ArgumentList '--self-test', '.\test-report.txt' -Wait
Start-Process .\app\TrayPilot.exe -ArgumentList '--startup-test', '.\test-report-startup.txt' -Wait
```

- `--scan` writes discovered tray records to JSON, including process paths and tooltips.
- `--self-test` uses a separate test process, isolated configuration and test registry paths to check UID/GUID discovery, hiding and restoration, rule persistence, process identity, UI interaction, languages, hotkeys and startup behavior.
- `--startup-test` checks startup registration and launch behavior separately.
- `--icon-selection-test` checks individual control, persistent UID/GUID rules, temporary restoration and matching after process restarts using a separate two-icon test application.
- `--verify-task-manager` requires Task Manager to be running. It uses isolated settings to verify discovery of both the cached and live CPU icons, rule-based hiding and rediscovery, then restores their original visibility.

Reports are generated by these commands and are not committed with the source. Use results generated on your current system when assessing compatibility.
