# OpenWithLauncher

A small .NET 9 WinForms app that programmatically opens the Windows **"Select a default app for .xxx files"** system dialog -- the same popup used by Adobe Acrobat, Chromium, and others to prompt users to change their default file association.

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

A pre-built binary is available on the [Releases](https://github.com/drubino-mozilla/IOpenWithLauncher/releases) page. Download `OpenWithLauncher-win-x64.zip`, extract, and run `OpenWithLauncher.exe`.

## Requirements

- Windows 10 or later
- [.NET 9.0 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/9.0) (win-x64)

## Usage

Run the app, type a file extension (e.g. `.pdf`, `.html`, `.txt`), and click **Launch** (or press Enter). The system "Select a default app" popup appears and blocks until closed.

## Building from Source

```bash
dotnet build
```

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
