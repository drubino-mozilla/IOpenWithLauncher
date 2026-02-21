# DIExplorer

**Desktop Integrations Explorer** — a .NET 9 WinForms app for exploring and working with Windows desktop integrations. The first feature: programmatically open the Windows **"Select a default app for .xxx files"** system dialog (the same popup used by Adobe Acrobat, Chromium, and others to prompt users to change their default file association).

![Select a default app for .pdf files](https://i.sstatic.net/9p6Oq.png)

## Background

Starting with Windows 10, Microsoft locked down programmatic control of file associations. The old `SHOpenWithDialog` API no longer works for *setting* defaults, and there is no documented replacement.

The **Chromium** project reverse-engineered an undocumented COM interface called `IOpenWithLauncher` that Windows itself uses internally. This tool wraps that interface in a simple GUI utility.

### The Undocumented Interface

```c++
// From chromium/src  base/win/default_apps_util.cc
class __declspec(uuid("6A283FE2-ECFA-4599-91C4-E80957137B26")) IOpenWithLauncher
    : public IUnknown {
 public:
  virtual HRESULT STDMETHODCALLTYPE Launch(HWND hWndParent,
                                           const wchar_t* lpszPath,
                                           int flags) = 0;
};
```

| Detail | Value |
|---|---|
| **Interface IID** | `{6A283FE2-ECFA-4599-91C4-E80957137B26}` |
| **CLSID** | Read from registry: `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\OpenWith` → `OpenWithLauncher` value |
| **COM Class Name** | "Execute Unknown" (per Chromium comments) |
| **Activation** | `CLSCTX_LOCAL_SERVER` (out-of-process) |
| **Flags** | `0x2004` (discovered empirically by Chromium) |
| **Behavior** | Blocking -- `Launch()` does not return until the dialog is closed |

### Return Values

- `S_OK` (0): The user clicked OK / Set default.
- `HRESULT_FROM_WIN32(ERROR_CANCELLED)` (0x800704C7): The user closed the dialog without clicking OK.

Note that `S_OK` is returned even if the user re-selected the already-current default. This tool detects that case by reading the `ProgId` from the `UserChoice` registry key before and after the dialog (see below).

## Download

A pre-built binary is available on the [Releases](https://github.com/drubino-mozilla/DIExplorer/releases) page. Download `DIExplorer-win-x64.zip`, extract, and run `DIExplorer.exe`. The release is a **single self-contained EXE** (~160 MB); no .NET runtime or other dependencies are required.

## Requirements

- **Pre-built release**: Windows 10 or later (x64). No .NET installation needed.
- **Building from source**: [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) and the Desktop workload (WinForms/WPF).

## Usage

- **Set PDF default**: Opens the same IOpenWithLauncher dialog for `.pdf` only (convenience for testing PDF defaults).
- **Browser default**: Opens Windows Settings so you can change the default browser:
  - **Windows 10**: One button opens **Settings → Default apps** with the **Web browser** row scrolled into view and focused (same as Firefox), using `IApplicationActivationManager` with `target=SystemSettings_DefaultApps_Browser`. If that fails, it falls back to `ms-settings:defaultapps`.
  - **Windows 11**: Buttons are built from the registry: the app enumerates `HKLM\SOFTWARE\RegisteredApplications` and `HKCU\Software\RegisteredApplications` and shows one button per detected browser, using each app’s **exact** registered name so the deep link navigates to that app’s default-apps page. Only browsers that are actually registered appear. Firefox desktop uses names like `Firefox-<hash>` (not `Firefox`), so the app resolves these at runtime. Buttons (when present) include:
    - **Firefox (firefox.com)** — release Firefox from mozilla.org
    - **Firefox (Microsoft Store)** — Firefox from the Microsoft Store (if registered)
    - **Firefox Nightly** — Nightly from firefox.com
    - **Microsoft Edge** — switch back to Edge

The app detects Windows 11 by build number (≥ 22000). On Windows 11, the deep link is `ms-settings:defaultapps?registeredAppMachine=<Name>` or `?registeredAppUser=<Name>`, where `<Name>` is the value name under the corresponding RegisteredApplications key. The app uses the correct parameter (machine vs user) depending on where each browser is registered.

## Building from Source

```bash
dotnet build
```

To produce the same single-file EXE as the official release (self-contained, no dependencies):

```bash
dotnet publish -c Release
```

The output is in `bin\Release\net9.0-windows\win-x64\publish\DIExplorer.exe`.

## How It Works

1. Reads the CLSID from `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\OpenWith` → `OpenWithLauncher`.
2. Calls `CoCreateInstance` with that CLSID and `CLSCTX_LOCAL_SERVER` to create the out-of-process COM object.
3. Calls `CoAllowSetForegroundWindow` on the COM object so Windows allows it to bring the dialog to the foreground.
4. Snapshots the current default by reading `ProgId` from `HKCU\...\Explorer\FileExts\.ext\UserChoice`.
5. Calls `IOpenWithLauncher::Launch(hWndParent, ".pdf", 0x2004)` which opens the system dialog and blocks.
6. Re-reads `UserChoice\ProgId` (polling briefly to account for the asynchronous registry write) and compares to the snapshot to determine if the default actually changed, stayed the same, or the user cancelled.

## Credits

- The `IOpenWithLauncher` COM interface was reverse-engineered by the [Chromium](https://chromium.googlesource.com/chromium/src/+/refs/heads/main/base/win/default_apps_util.cc) project.
- The interface is also used by Adobe Acrobat to prompt users to set it as the default PDF handler.

## Disclaimer

`IOpenWithLauncher` is an **undocumented Windows interface**. It is not part of the public Windows SDK and could change or be removed in future Windows updates. Use at your own risk.
