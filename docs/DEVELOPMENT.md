# Development and technical notes

[Back to the user guide](../README.md)

## Build and diagnostics

### Local build

On Windows, install Visual Studio 2022 C++ x64 build tools and the Windows SDK, together with the .NET 10 SDK or a newer SDK supporting `net10.0-windows`, then run from the repository root:

```powershell
dotnet publish .\source\TrayPilot.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o .\app
```

The publish output is a single `app/TrayPilot.exe`, including built-in languages and the native helper. The self-contained build includes .NET (native runtime components extract at launch); the lite build requires .NET 10 Desktop Runtime x64. Sources are under `source/`, using C#, WebView2 and Win32 APIs; Windows Forms hosts the window and tray icon. WebView2 Evergreen Runtime is required.

### Detection diagnostics

```powershell
.\app\TrayPilot.exe --system-icons-probe .\probe.txt
.\app\TrayPilot.exe --system-icons-probe-legacy .\probe-legacy.txt
.\app\TrayPilot.exe --symbol-cache-test .\symbol-cache-test.txt
```

The first two commands test the updated and original methods respectively, reporting the backend and detected controls. Failures are written to the corresponding `.error.txt` file. The third checks the current symbol cache and rejection of corrupt or mismatched PDBs.

### Refresh and startup

Web pages update existing rows and preserve images, selection, focus and scroll. Identical state causes no DOM mutations. Foreground automatic discovery runs every 2.5 seconds; background rule/recovery checks reuse the scan session. Startup bundles embedded HTML, CSS and scripts in memory, and preloads the reusable quick-control view. Loading failures provide retry.

```powershell
.\app\TrayPilot.exe --web-startup-test .\startup.json
.\app\TrayPilot.exe --web-startup-live-test .\startup-live.json
.\app\TrayPilot.exe --web-feedback-preview .\interface.png
.\app\TrayPilot.exe --web-feedback-bridge-test .\feedback.json
.\app\TrayPilot.exe --web-preferences-bridge-test .\preferences.json
.\app\TrayPilot.exe --web-system-bridge-test .\system.json
.\app\TrayPilot.exe --web-rules-live-test .\rules.json
.\app\TrayPilot.exe --self-test .\self-test.txt
.\app\TrayPilot.exe --resilience-test .\resilience.txt
```

Startup timings measure window construction through first presentation in the current diagnostic session. Compare repeated runs on the same machine and environment. The live startup diagnostic uses isolated settings and normal read-only discovery. Visibility and termination bridge tests operate only on test-owned processes; startup registration uses isolated entries. System bridge tests use simulated controls.

## Add a language pack

Built-in language packs are embedded in the EXE; source packs are in `source/languages/`. Optional JSON files in a `languages/` folder beside the EXE add or override languages. They use UTF-8 JSON:

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

Recovery records are saved before hiding icons. On startup, TrayPilot attempts to restore recorded icons before applying the current rules. `RestoreIconsOnExit` defaults to `false`, including settings files that lack the field: quitting keeps icon states and recovery records. Enable **Restore icons on exit** to restore before a normal exit; restoration failures retain the records and cancel that exit for retry. Shutdown/sign-out queries do not restore or close anything. Confirmed session termination follows the same preference without displaying a blocking dialog.

Normal system-icon session cleanup still restores original values. An explicit no-restore exit uses native `stop=2`, stops polling and retains the current visibility. Weak element references and original property values remain in Explorer for a later session to restore, without an active timer or old session handles. Unexpected crashes retain the existing recovery behavior. Windows rebuilding the taskbar or an owning application updating its icons may change visibility after exit.

After a forced termination, reopen TrayPilot and choose **Restore all**, or restart the affected application so it recreates its icons. Do not delete recovery records while icons remain hidden.

TrayPilot does not modify other applications' settings or write to Windows tray `NotifyIconSettings`. Startup uses a per-user scheduled task. Enabling/disabling removes only TrayPilot's legacy Run and StartupApproved entries.

Settings include a format version; older files without it remain supported, while unsupported future versions are never downgraded. Saving flushes a temporary file to disk before replacement and retains the previous file as `settings.json.bak`. Invalid JSON or malformed rules/recovery records trigger backup loading; the original is preserved as `settings.json.corrupt-<unique ID>`. A warning explains that the previous backup can omit recent changes. If neither file is valid, startup stops without replacing recovery records with empty defaults.

Single-instance activation precedes settings loading. Startup recovery runs serially and asynchronously after the window is shown; failures retain records and allow retry from the window. Refresh reuses scanned states and rechecks icons targeted by automatic hiding, while mutations still verify process identity. The executable-name cache holds at most 256 entries and expires metadata after five minutes, including updates at the same path.

## Compatibility and implementation

This project is a prototype targeting **Windows 11 25H2**. Other Windows versions, future patches and unusual applications require validation on the actual system.

- Icons come from the Windows cache, then the executable, then a generic fallback. Cached icons and tooltips may be stale. Names mainly come from executable file descriptions, so scripts may display their host application's name.
- Explorer's built-in controls have the **System icons** page; see its compatibility and activation limitations above.
- Missing tray records, unmatched paths, protected processes and special implementations may prevent discovery or control.

## System icon implementation

The system-icons dialog offers two **Detection method** choices. Selection reconnects immediately and is saved across launches:

- **Updated compatibility method (recommended)** is the default. It reads the installed `taskbar.dll` symbol identity, downloads the exact Microsoft PDB over HTTPS, validates its GUID and DBI age, and caches it before using the existing control discovery. It requires no additional `symsrv.dll`, addressing the original method's “Element not found” (`0x80070490`) when symbol downloading is unavailable. Ordinary application tray scanning is unchanged.
- **Original method** preserves the existing DbgHelp symbol-server path. It remains selectable and depends on the local symbol-download components and network configuration.

The updated method may need a download on first connection or after a Windows update (45-second download timeout). Valid caches work offline; damaged or mismatched caches are downloaded again. Unpublished symbols, network failures, or incompatible internal layouts produce a connection error without guessing offsets. Switching methods restores the old session's controls and reapplies saved hide choices after a successful connection.

The native helper is embedded in the EXE and extracted on demand into `%LOCALAPPDATA%/TrayPilot/native/<SHA256>/`; no companion DLL is needed. This is single-file distribution, not a pure managed implementation: the XAML diagnostics API loads an in-process COM DLL into Explorer. Building from source still requires the C++ toolchain. This implementation targets the primary taskbar on Windows 11 x64; secondary-taskbar clocks and ARM64 are unverified. The updated method passed individual, combined and crash-restoration checks for volume, network, battery, clock, supplementary language icons and Show desktop on **Windows 11 25H2, build 26200.9457**. Other categories were absent and still need testing on a desktop displaying them.
