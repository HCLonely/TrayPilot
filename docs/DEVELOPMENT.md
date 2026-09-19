# Development and technical notes

[Back to the user guide](../README.md)

## Build and diagnostics

### Local build

On Windows, install Visual Studio 2022 C++ x64 build tools and the Windows SDK, together with the .NET 8 SDK or a newer SDK supporting `net8.0-windows`, then run from the repository root:

```powershell
dotnet publish .\source\TrayPilot.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o .\app
```

The publish output is a single `app/TrayPilot.exe`, including built-in languages and the native helper. The self-contained build includes .NET (native runtime components extract at launch); the lite build requires .NET 8 Desktop Runtime x64. Sources are under `source/`, using C#, Windows Forms and Win32 APIs.

### Detection diagnostics

```powershell
.\app\TrayPilot.exe --system-icons-probe .\probe.txt
.\app\TrayPilot.exe --system-icons-probe-legacy .\probe-legacy.txt
.\app\TrayPilot.exe --symbol-cache-test .\symbol-cache-test.txt
```

The first two commands test the updated and original methods respectively, reporting the backend and detected controls. Failures are written to the corresponding `.error.txt` file. The third checks the current symbol cache and rejection of corrupt or mismatched PDBs.

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

Recovery records are saved before hiding icons. On startup, TrayPilot attempts to restore recorded icons before applying the current rules. It also restores icons before a normal exit; if restoration fails, it retains the records and cancels exit so you can retry.

After a forced termination, reopen TrayPilot and choose **Restore all**, or restart the affected application so it recreates its icons. Do not delete recovery records while icons remain hidden.

TrayPilot does not modify other applications' settings or write to Windows tray `NotifyIconSettings`. Startup uses a per-user scheduled task. Enabling/disabling removes only TrayPilot's legacy Run and StartupApproved entries.

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
