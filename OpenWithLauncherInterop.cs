using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace OpenWithLauncher;

/// <summary>
/// Undocumented COM interface reverse-engineered by Chromium
/// (chromium/src base/win/default_apps_util.cc).
/// Opens the Windows "Select a default app for .xxx files" dialog.
/// </summary>
[ComImport]
[Guid("6A283FE2-ECFA-4599-91C4-E80957137B26")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IOpenWithLauncher
{
    [PreserveSig]
    int Launch(
        IntPtr hWndParent,
        [MarshalAs(UnmanagedType.LPWStr)] string path,
        int flags);
}

internal static class OpenWithLauncherInterop
{
    private const int OpenWithFlags = 0x2004;

    private const string OpenWithRegistryPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\OpenWith";
    private const string OpenWithRegistryValue = "OpenWithLauncher";

    private const string UserChoiceKeyTemplate =
        @"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\{0}\UserChoice";

    /// <summary>
    /// Reads the CLSID for the "Execute Unknown" COM class from the registry.
    /// </summary>
    public static Guid? GetOpenWithLauncherClsid()
    {
        using var key = Registry.LocalMachine.OpenSubKey(OpenWithRegistryPath);
        if (key?.GetValue(OpenWithRegistryValue) is not string value || string.IsNullOrEmpty(value))
            return null;

        return Guid.TryParse(value, out var clsid) ? clsid : null;
    }

    /// <summary>
    /// Reads the current default ProgId for a file extension directly from
    /// HKCU\...\FileExts\.ext\UserChoice\ProgId. This bypasses any COM
    /// caching and always returns the live registry value.
    /// </summary>
    public static string? ReadUserChoiceProgId(string extension)
    {
        try
        {
            string keyPath = string.Format(UserChoiceKeyTemplate, extension);
            using var key = Registry.CurrentUser.OpenSubKey(keyPath);
            return key?.GetValue("ProgId") as string;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Opens the "Select a default app for [extension] files" dialog.
    /// Blocks until the user closes it.
    /// </summary>
    public static LaunchResult LaunchForFileExtension(string extension, IntPtr parentHwnd = default)
    {
        var clsid = GetOpenWithLauncherClsid();
        if (clsid is null)
            return LaunchResult.ClsidNotFound;

        var clsidGuid = clsid.Value;
        var iid = typeof(IOpenWithLauncher).GUID;
        int hr = CoCreateInstance(ref clsidGuid, IntPtr.Zero, CLSCTX_LOCAL_SERVER, ref iid, out var obj);
        if (hr < 0)
            return LaunchResult.ComError(hr);

        var launcher = (IOpenWithLauncher)obj;
        try
        {
            CoAllowSetForegroundWindow(obj, IntPtr.Zero);

            string? before = ReadUserChoiceProgId(extension);

            hr = launcher.Launch(parentHwnd, extension, OpenWithFlags);

            if (hr == 0) // S_OK
            {
                string? after = WaitForDefaultToChange(extension, before);
                bool actuallyChanged = !string.Equals(before, after, StringComparison.OrdinalIgnoreCase);
                return LaunchResult.UserAccepted(before, after, actuallyChanged);
            }

            if (hr == HresultFromWin32(ERROR_CANCELLED))
                return LaunchResult.Cancelled;

            return LaunchResult.LaunchError(hr);
        }
        finally
        {
            Marshal.ReleaseComObject(launcher);
        }
    }

    /// <summary>
    /// Polls the UserChoice registry value for up to ~2 seconds, returning
    /// as soon as it differs from <paramref name="before"/> or time runs out.
    /// </summary>
    private static string? WaitForDefaultToChange(string extension, string? before)
    {
        const int maxAttempts = 20;
        const int delayMs = 100;

        for (int i = 0; i < maxAttempts; i++)
        {
            Thread.Sleep(delayMs);
            string? current = ReadUserChoiceProgId(extension);
            if (!string.Equals(before, current, StringComparison.OrdinalIgnoreCase))
                return current;
        }

        return ReadUserChoiceProgId(extension);
    }

    private const int CLSCTX_LOCAL_SERVER = 4;
    private const int ERROR_CANCELLED = 1223;

    private static int HresultFromWin32(int win32Error) =>
        win32Error <= 0 ? win32Error : (win32Error & 0x0000FFFF) | unchecked((int)0x80070000);

    [DllImport("ole32.dll")]
    private static extern int CoAllowSetForegroundWindow(
        [MarshalAs(UnmanagedType.IUnknown)] object pUnk,
        IntPtr lpvReserved);

    [DllImport("ole32.dll")]
    private static extern int CoCreateInstance(
        ref Guid rclsid,
        IntPtr pUnkOuter,
        int dwClsContext,
        ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out object ppv);
}

internal record LaunchResult(bool Succeeded, string Message, int? HResult = null)
{
    public static LaunchResult UserAccepted(string? before, string? after, bool changed) =>
        changed
            ? new(true, $"Default changed: {before ?? "(none)"} \u2192 {after ?? "(none)"}")
            : new(true, $"User confirmed the existing default ({after ?? "unknown"}).");

    public static readonly LaunchResult Cancelled =
        new(true, "User closed the dialog without making a change.");

    public static readonly LaunchResult ClsidNotFound =
        new(false, "Could not find OpenWithLauncher CLSID in registry. Is this Windows 10+?");

    public static LaunchResult ComError(int hr) =>
        new(false, $"Failed to create IOpenWithLauncher COM object. HRESULT: 0x{hr:X8}.", hr);

    public static LaunchResult LaunchError(int hr) =>
        new(false, $"IOpenWithLauncher::Launch failed. HRESULT: 0x{hr:X8}.", hr);
}
