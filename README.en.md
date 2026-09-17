# TrayPilot 0.4.1

Windows 11 tray icon manager. Run `app/TrayPilot.exe`; no separate .NET installation is required. Keep the adjacent `app/languages` folder when moving or distributing the application.

## Controls

- Double-click an application to show or hide its tray icons. Single-click selects it. Ctrl/Shift selects multiple items.
- Switch between List and Grid. Hidden icons appear faded. Hover for details.
- Right-click an application for Properties, Show/Hide icons, or End task. Properties use a non-editable table with a Copy information button. End task forcefully terminates only the selected process, after verifying its identity.
- Hide rules display application icons, names and executable paths, including saved rules for applications that are not running. Missing executables use a fallback icon. Removing a rule restores its icons first.
- Auto-refresh is enabled by default and can be turned off. Refresh keeps search text, selection and focus intact.

## System tray and exit

By default, closing the window keeps TrayPilot running in the system tray and continues enforcing hide rules. Disable this behavior in Settings if closing should exit instead.

Left-click or double-click TrayPilot's tray icon to open the main window. Right-click for quick controls. The tray menu includes Open main window, About and Exit, plus all detected applications, independent of the main search filter. A check mark means all icons for that application's executable path are shown. Click once to toggle; if some icons are hidden, the command shows all of them. Windows may still place shown icons in its overflow area.

Choose Exit to restore managed icons and quit. Show or hide TrayPilot itself using the dedicated tray-menu entry or Settings. Run the executable again to reopen the existing window even when its icon is hidden. Quick controls paginate applications in groups of ten. Scroll down for the next page and up for the previous page; scrolling stops at the first and last pages. Open main window comes first, Settings and About are near the bottom, and Exit is last. Saved hide rules are applied again the next time TrayPilot runs.

About displays the application version, description, operating system, executable path, settings folder and language folder.

## Global hotkey

In Settings, enable the hotkey, focus its input field, press Ctrl or Alt plus another key, then Save. Two independent hotkeys open quick controls and the main window, including when the window and tray icon are hidden. No key is reserved by default; Ctrl+Alt+T (quick controls) and Ctrl+Alt+M (main window) are the initial suggested combinations. Conflicts are reported, and a failed change retains the existing registration. Disabling the hotkey or exiting releases it.

## Languages and settings

Settings offers Simplified Chinese and English, with immediate switching. Simplified Chinese is the default. Language, close-to-tray behavior, own-icon visibility and both hotkeys are saved in `%LOCALAPPDATA%\TrayPilot\settings.json`.

UTF-8 language packs live in `app/languages/zh-CN.json` and `app/languages/en-US.json`. To add a language, copy a complete pack, change its filename and `Name`, and translate the values in `Strings`. Keep the keys and format placeholders such as `{0}` unchanged. Reopen Settings to discover added packs. Missing translations fall back to Chinese; missing or malformed packs do not prevent normal operation.

Application names, paths, Windows-provided errors and third-party tooltip text retain their original language.

## Source and verification

Source and source language packs are under `source`. Build using .NET 8 or later:

```powershell
dotnet publish .\source\TrayPilot.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o .\app
```

`test-report.txt` contains integration results. Tests operate on a separate test process, including hide/restore, process termination, tray controls, language switching, global hotkeys and saved settings.

Tray discovery uses Windows notification records and has been verified on Windows 11 25H2. Special application implementations and future Windows changes may behave differently. Recovery records are retained if icon restoration fails; do not delete them while icons remain hidden.
