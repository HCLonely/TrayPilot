# Development and technical notes

[Back to the user guide](../README.md)

## Build and diagnostics

### Local build

On Windows, install Git, Visual Studio 2022 C++ x64 build tools and the Windows SDK, together with the .NET 10 SDK or a newer SDK supporting `net10.0-windows`. Double-click `build.bat` in the `scripts` directory to build; the window keeps the result visible until you press a key. The first build requires internet access to restore NuGet packages.

Alternatively, run in PowerShell:

```powershell
.\scripts\build.ps1

# Lite package (requires .NET 10 Desktop Runtime x64)
.\scripts\build.ps1 -Package lite -OutputDirectory output\lite

# Custom configuration and output directory
.\scripts\build.ps1 -Configuration Debug -OutputDirectory output\debug
```

The default builds the Windows x64 full package in Release mode at `app/TrayPilot.exe`, automatically building and embedding the C++ native helper. Relative output paths are resolved against the repository root. Failures return a nonzero exit code. If PowerShell execution policy blocks the script, use `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build.ps1`.

The publish output is a single `app/TrayPilot.exe`, including built-in languages and the native helper. The self-contained build includes .NET (native runtime components extract at launch); the lite build requires .NET 10 Desktop Runtime x64. Sources are under `source/`, using C#, WebView2 and Win32 APIs; Windows Forms hosts the window and tray icon. WebView2 Evergreen Runtime is required.

### Detection diagnostics

```powershell
.\app\TrayPilot.exe --system-icons-probe .\probe.txt
.\app\TrayPilot.exe --system-icons-probe-legacy .\probe-legacy.txt
.\app\TrayPilot.exe --symbol-cache-test .\symbol-cache-test.txt
.\app\TrayPilot.exe --explorer-images-test .\image-test.txt
.\app\TrayPilot.exe --explorer-images-probe .\image-probe.json
.\app\TrayPilot.exe --application-images-test .\application-image-test.txt
.\app\TrayPilot.exe --application-images-probe .\application-image-probe.json
.\app\TrayPilot.exe --application-model-test .\application-model-test.txt
.\app\TrayPilot.exe --application-model-probe .\application-model-probe.json
.\app\TrayPilot.exe --application-model-live-test .\application-model-live.json
```

The first two commands test the updated and original methods respectively, reporting the backend and detected controls. Failures are written to the corresponding `.error.txt` file. The third checks the current symbol cache and rejection of corrupt or mismatched PDBs.

`--application-images-test` uses synthetic notifications to check ordinary-software HICON copying, pixel decoding, UID/GUID separation, process identity, updates, invalidation and cleanup. `--application-images-probe` tests the ordinary-software `ITrayNotify` callback and an independent native C++ control. It creates, updates, hides and deletes only its own test icon; user icons remain read-only, with no screen capture or preference writes. Its JSON separates `connected` (registration succeeded) from `available` (matching images actually received). No matching image produces exit code 1.

### Ordinary application image resources

`ScanSession` owns `ApplicationIconSource`. It prepares exact-version taskbar.dll and SystemTray.dll symbols and attaches a read-only image session in the background, preserving the initial fallback render. Failures back off for 60 seconds; Explorer identity changes cause reconnection. Disposal cleans up current and pending sessions. Taskbar.pdb body downloads time out after 45 seconds; larger SystemTray.pdb downloads after 120 seconds. Valid caches are reused. All DbgHelp operations share one native mutex.

`native/ApplicationModelImages.h` traverses primary-taskbar `NotifyIconView` objects on their XAML thread. PDB-matched interface vtables and getters provide `IconViewModel.ContentVM`. Verified constructor/accessor instruction patterns identify its retained notification data model; `UDKObject` then provides `WindowsUdk.UI.Shell.NotificationAreaIcon`. Identity field layouts are also instruction-validated. Unknown layouts fall back without speculative field scanning or unverified interface-slot calls.

The image resource is a `SoftwareBitmap`. A short read-only lock copies BGRA pixels after plane/bounds/alpha validation, immediately closing the reference and lock. Explorer's original bitmap is never modified or closed; format conversion creates a separate copy. This path does not capture the screen or render controls. Bounded image-stream reading is retained for compatible resource types.

Companion IPC protocol 3 uses a 200-byte header and 128 records of 66088 bytes (8459464 total). Each image is limited to 65536 bytes and 128×128 raw pixels. Records carry HWND, UID, GUID, dimensions and diagnostic types. Sequence counters guard coherent reads; failed, invalid or older-than-three-second images select fallback. Matching prefers GUID, otherwise actual HWND/UID, and checks PID, start time and executable. Conflicting identities never choose an arbitrary image.

`TrayEntry.LiveIconSnapshot` is excluded from saved settings/recovery records. `TrayImages` loads live pixels → original IconSnapshot → executable → generic icon. Web cache signatures include the source and bytes, updating both the main list and quick panel on changes, failures and recovery. Read-only native sessions neither change system-control visibility nor consume preserved system-control original values.

`--application-model-test` covers pixels, transparency, identity, IPC and actual Web image fallback/recovery. `--application-model-probe` exports matched images and record diagnostics. `--application-model-live-test` exercises real scan integration. Live probes never alter icon visibility or preferences. Windows 11 25H2 testing obtained Weixin, QQ, PowerToys PowerDisplay and EarTrumpet images; entries without an instantiated model used fallback. The backend does not open overflow views or temporarily show hidden icons; unavailable overflow/hidden models may always fall back. Other Windows layouts remain unverified.

The early ITrayNotify route remains diagnostic-only: managed and independent native callbacks both returned zero records on the tested installation. Production image reading does not use it. Unregistration uses a pointer to a 32-bit cookie.

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

Per-program bindings use `ProgramHotkeys`, a normalized executable-path to Windows Forms Keys integer map; missing fields in older settings default to an empty map. `ProgramHotkeys.cs` owns Win32 registration, save rollback and serialized visibility toggles. Global settings also reject conflicting program bindings. Handle destruction and disposal release registrations. `programhotkeys.js` reuses the shortcut editor and provides rule context menus. `--web-rules-live-test` covers persistence, occupied-key rollback, native program toggles, rule exceptions, menu positioning, keyboard access and clearing.

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

- Application icons prefer Explorer's current image, then the Windows cache, executable and generic fallback. Cached icons and tooltips may be stale. Names mainly come from executable file descriptions, so scripts may display their host application's name.
- Explorer's built-in controls have the **System icons** page; see its compatibility and activation limitations above.
- Missing tray records, unmatched paths, protected processes and special implementations may prevent discovery or control.

## System icon implementation

Images are independent of visibility control. `native/IconImages.h` renders identified controls inside Explorer on its XAML thread using `RenderTargetBitmap.RenderAsync` / `GetPixelsAsync`, with no screen capture or temporary unhiding. Each control has at most one pending operation and starts at most once per second while appearance reading is enabled. Transparent layout padding is removed; glyph layers and colors are preserved.

Shared-memory protocol version 2 places 12 image slots at offset 2024. Each 49176-byte slot holds a 24-byte header and up to 192×64×4 premultiplied BGRA bytes; mapping size is 592136 bytes. Independent sequence counters guard coherent reads. Managed code rejects failed HRESULTs, empty/transparent images, invalid dimensions, future timestamps and images older than three seconds. Coroutines retain their original session and do not publish after stop; image failures never set the visibility session's error field.

`ExplorerIconImage.cs` validates and converts pixels. `WebSystemIcons.cs` caches PNGs by pixel hash, automatically uses `SystemIconImages.Create` on failure or expiry and resumes internal images after a successful retry. Native images retain their colors and transparent backgrounds, without a gray backing or border in cards or details; fallback masks retain page-theme tinting. Collapsed or absent controls and controls without visible graphics, such as Show desktop, may always use fallback. Ordinary application icons use the separate resource-reading and fallback path described above.

`--explorer-images-test` checks IPC validation and premultiplied colors. The read-only `--explorer-images-probe` writes per-control availability, dimensions, HRESULTs and extracted PNGs beside its JSON report; it returns nonzero if no valid image is obtained. Neither command captures the screen. `--web-system-bridge-test` also covers image priority, fallback and retry. A live Windows 11 25H2 probe obtained network, clock and supplementary language images. Volume and language controls were already hidden; absent categories remain unverified.

The system-icons dialog offers two **Detection method** choices. Selection reconnects immediately and is saved across launches:

- **Updated compatibility method (recommended)** is the default. It reads the installed `taskbar.dll` symbol identity, downloads the exact Microsoft PDB over HTTPS, validates its GUID and DBI age, and caches it before using the existing control discovery. It requires no additional `symsrv.dll`, addressing the original method's “Element not found” (`0x80070490`) when symbol downloading is unavailable. Ordinary application tray scanning is unchanged.
- **Original method** preserves the existing DbgHelp symbol-server path. It remains selectable and depends on the local symbol-download components and network configuration.

The updated method may need a download on first connection or after a Windows update (45-second download timeout). Valid caches work offline; damaged or mismatched caches are downloaded again. Unpublished symbols, network failures, or incompatible internal layouts produce a connection error without guessing offsets. Switching methods restores the old session's controls and reapplies saved hide choices after a successful connection.

The native helper is embedded in the EXE and extracted on demand into `%LOCALAPPDATA%/TrayPilot/native/<SHA256>/`; no companion DLL is needed. This is single-file distribution, not a pure managed implementation: the XAML diagnostics API loads an in-process COM DLL into Explorer. Building from source still requires the C++ toolchain. This implementation targets the primary taskbar on Windows 11 x64; secondary-taskbar clocks and ARM64 are unverified. The updated method passed individual, combined and crash-restoration checks for volume, network, battery, clock, supplementary language icons and Show desktop on **Windows 11 25H2, build 26200.9457**. Other categories were absent and still need testing on a desktop displaying them.
