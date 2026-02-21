# DIExplorer

A small prototype app for **Firefox integrations with the Windows desktop**. It contains two experiments:

1. **Can we open the same “Set default” prompt for PDFs that Adobe Acrobat opens?**
2. **Can we use accessibility (UI Automation) to visually highlight the “Set default” button in Windows Settings?**

---

## Prototype 1: Opening the PDF “Set default” dialog

### Background

Starting with Windows 10, Microsoft locked down programmatic control of file associations. The old `SHOpenWithDialog` API no longer works for *setting* defaults, and there is no documented replacement. Adobe Acrobat and Chromium both show a system dialog that says “Select a default app for .pdf files” (or similar). That dialog comes from an **undocumented COM interface** that the Chromium project reverse-engineered: **`IOpenWithLauncher`**.

### Solution

We use the same approach as Chromium: read the COM class ID (CLSID) from the registry, create the out-of-process COM object, call `CoAllowSetForegroundWindow` so the dialog can come to the foreground, then call `IOpenWithLauncher::Launch()` with the file extension (e.g. `.pdf`) and the flags Chromium discovered. The call blocks until the user closes the dialog. To tell whether the user actually *changed* the default or just clicked OK with the same app selected, we read the `ProgId` from the `UserChoice` registry key before and after the dialog (and poll briefly after it closes, since Windows writes the new value asynchronously).

### Technical details

- **Interface IID**: `{6A283FE2-ECFA-4599-91C4-E80957137B26}`
- **CLSID**: Read at runtime from `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\OpenWith` → value `OpenWithLauncher`.
- **Activation**: `CLSCTX_LOCAL_SERVER` (out-of-process).
- **Method**: `Launch(HWND hWndParent, const wchar_t* lpszPath, int flags)` — we pass the extension (e.g. `".pdf"`) and flags `0x2004` (empirically determined by Chromium).
- **Return values**: `S_OK` when the user clicks OK/Set default; `HRESULT_FROM_WIN32(ERROR_CANCELLED)` when they close without confirming. Because `S_OK` is returned even when the user re-selects the current default, we compare `HKCU\...\Explorer\FileExts\.ext\UserChoice` → `ProgId` before and after (with a short poll after the dialog closes) to report “changed”, “confirmed existing”, or “cancelled”.

---

## Prototype 2: Highlighting the “Set default” button in Settings

### Solution

When the user opens Windows Settings to change the default browser (e.g. via our deep link to that app’s default-apps page), we use **UI Automation** to find the Settings window and the button whose name contains “Set default”. We get that button’s screen rectangle, then show a **borderless, topmost, transparent overlay** that draws a character (Kit the Firefox mascot, or Clippy for Edge) so that a transparent “gap” in the artwork lines up with the button—making the character appear to point at it. A purple outline is drawn around the button. The overlay is click-through and does not steal focus. We keep re-querying the button’s position (and whether the Settings window is still open) and update the overlay until the user closes Settings or the button disappears.

### Technical details

- **UI Automation**: We use the `System.Windows.Automation` APIs to enumerate top-level windows, find the Settings window (by class name `ApplicationFrameWindow` and name containing “Settings”, or by process name `SystemSettings`), then search descendant elements for a `ControlType.Button` whose `Name` contains “Set default”. The Settings UI loads asynchronously after the deep link is opened, so we poll until the button appears or a timeout expires.
- **Overlay**: A WinForms form with `FormBorderStyle.None`, `TopMost = true`, and layered window styles (`WS_EX_LAYERED`, `WS_EX_TRANSPARENT`, `WS_EX_TOOLWINDOW`, `WS_EX_NOACTIVATE`) so it’s non-interactive and doesn’t take focus. We use `UpdateLayeredWindow` with per-pixel alpha so the character’s anti-aliased edges blend correctly. The character art (Kit as SVG, Clippy as PNG) is embedded; we scale and position it so the button falls in the defined “gap” region of the artwork.
- **Tracking**: A background loop runs every 500 ms: it checks if the Settings window is still open and re-calls the UI Automation search to get the button’s current `BoundingRectangle`. If the window is closed or the button is gone, we dismiss the overlay. Otherwise we update the overlay’s position and redraw.

---

## Credits

The `IOpenWithLauncher` COM interface was reverse-engineered by the [Chromium](https://chromium.googlesource.com/chromium/src/+/refs/heads/main/base/win/default_apps_util.cc) project and is also used by Adobe Acrobat.

## Disclaimer

`IOpenWithLauncher` is an undocumented Windows interface, not part of the public SDK; it could change or be removed in future Windows updates. Use at your own risk.
