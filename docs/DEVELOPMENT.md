# Development and technical notes

[Back to the user guide](../README.md)

## Build and diagnostics

### Local build

On Windows, install Visual Studio 2022 C++ x64 build tools and the Windows SDK, together with the .NET 10 SDK or a newer SDK supporting `net10.0-windows`, then run from the repository root:

```powershell
dotnet publish .\source\TrayPilot.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o .\app
```

The publish output is a single `app/TrayPilot.exe`, including built-in languages and the native helper. The self-contained build includes .NET (native runtime components extract at launch); the lite build requires .NET 10 Desktop Runtime x64. Sources are under `source/`, using C#, Windows Forms and Win32 APIs.

### Detection diagnostics

```powershell
.\app\TrayPilot.exe --system-icons-probe .\probe.txt
.\app\TrayPilot.exe --system-icons-probe-legacy .\probe-legacy.txt
.\app\TrayPilot.exe --symbol-cache-test .\symbol-cache-test.txt
```

The first two commands test the updated and original methods respectively, reporting the backend and detected controls. Failures are written to the corresponding `.error.txt` file. The third checks the current symbol cache and rejection of corrupt or mismatched PDBs.

### Refresh and resource usage

- Visible automatic refresh performs a full scan every 2.5 seconds. Background rule/recovery maintenance checks known icons every 2.5 seconds and performs full discovery approximately every 10 seconds. Idle background automatic refresh uses 15 seconds. Manual refresh and opening the main window request full discovery; open menus defer scanning.
- Disabling automatic list refresh does not disable active rules or recovery maintenance. Hidden and minimized windows skip rendering.
- Rendering compares fields and snapshot bytes directly. State, rule and image changes update existing rows; structural, filter and language changes rebuild the list. Each form owns a bounded 256-entry image cache with deterministic eviction and disposal.
- Scan-local process metadata and fallback process snapshots are reused. Process path buffers start at 1,024 characters and grow to 32,768 when needed. Icon modifications still validate the live owner.
- Batch hiding persists recovery records before modifications. Batch restoration saves successful removals together. UI operations, refresh and exit restoration run serially with asynchronous state polling; startup recovery and synchronous diagnostics retain synchronous entry points.
- Background system-icon sessions skip appearance capture. Closing the dialog with no hidden selections releases the session after restoration acknowledgement, with a three-second wait limit. The native stop path also attempts restoration. Active system-icon management retains 250 ms discovery polling for dynamic controls.

Run these diagnostics using a single-file published executable. Integration diagnostics temporarily create test icons or change system icons and restore them afterwards.

```powershell
.\app\TrayPilot.exe --self-test .\self-test.txt
.\app\TrayPilot.exe --refresh-regression-test .\refresh-regression.txt
.\app\TrayPilot.exe --resilience-test .\resilience.txt
.\app\TrayPilot.exe --refresh-performance-test .\refresh-performance.txt
.\app\TrayPilot.exe --menu-performance-test .\menu-performance.txt
.\app\TrayPilot.exe --system-icons-ui-test .\system-icons-ui.txt
```

The refresh benchmark uses 80 seeded 32×32 snapshots, five warmups per workload, 100 unchanged renders, 30 single-row state changes and five full scans. Allocation counts measure cumulative managed allocations on the calling thread, not resident memory or total process CPU. Compare repeated runs of the same harness on the same machine using medians. Regression checks cover cache invalidation and limits, GDI resources, incremental row updates and background scheduling.

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
- Explorer's built-in controls have a separate **System icons** control dialog; see its compatibility and activation limitations above.
- Missing tray records, unmatched paths, protected processes and special implementations may prevent discovery or control.

## System icon implementation

The system-icons dialog offers two **Detection method** choices. Selection reconnects immediately and is saved across launches:

- **Updated compatibility method (recommended)** is the default. It reads the installed `taskbar.dll` symbol identity, downloads the exact Microsoft PDB over HTTPS, validates its GUID and DBI age, and caches it before using the existing control discovery. It requires no additional `symsrv.dll`, addressing the original method's “Element not found” (`0x80070490`) when symbol downloading is unavailable. Ordinary application tray scanning is unchanged.
- **Original method** preserves the existing DbgHelp symbol-server path. It remains selectable and depends on the local symbol-download components and network configuration.

The updated method may need a download on first connection or after a Windows update (45-second download timeout). Valid caches work offline; damaged or mismatched caches are downloaded again. Unpublished symbols, network failures, or incompatible internal layouts produce a connection error without guessing offsets. Switching methods restores the old session's controls and reapplies saved hide choices after a successful connection.

The native helper is embedded in the EXE and extracted on demand into `%LOCALAPPDATA%/TrayPilot/native/<SHA256>/`; no companion DLL is needed. This is single-file distribution, not a pure managed implementation: the XAML diagnostics API loads an in-process COM DLL into Explorer. Building from source still requires the C++ toolchain. This implementation targets the primary taskbar on Windows 11 x64; secondary-taskbar clocks and ARM64 are unverified. The updated method passed individual, combined and crash-restoration checks for volume, network, battery, clock, supplementary language icons and Show desktop on **Windows 11 25H2, build 26200.9457**. Other categories were absent and still need testing on a desktop displaying them.
