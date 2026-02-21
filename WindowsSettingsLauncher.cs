using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace DIExplorer;

/// <summary>
/// Launches Windows Settings using the ms-settings: URI protocol or (on Windows 10)
/// IApplicationActivationManager to focus the Web browser section.
/// See: https://learn.microsoft.com/en-us/windows/apps/develop/launch/launch-default-apps-settings
/// Firefox uses IApplicationActivationManager + target=SystemSettings_DefaultApps_Browser on Windows 10.
/// </summary>
internal static class WindowsSettingsLauncher
{
    /// <summary>
    /// Windows 11 build number (21H2). App-specific deep links (registeredAppMachine) require Win11 21H2 + 2023-04 update.
    /// </summary>
    private const int Windows11FirstBuild = 22000;

    /// <summary>
    /// Default apps settings page. On Windows 10 this opens the general default apps UI.
    /// On Windows 11 (with 2023-04+ update) you can append ?registeredAppMachine=AppName to open a specific app's defaults.
    /// </summary>
    private const string DefaultAppsUri = "ms-settings:defaultapps";

    /// <summary>
    /// Windows 10: AUMID for the Settings app. Used with IApplicationActivationManager to open
    /// Default apps and focus the Web browser row (target=SystemSettings_DefaultApps_Browser).
    /// </summary>
    private const string SettingsAppAumid = "windows.immersivecontrolpanel_cw5n1h2txyewy!microsoft.windows.immersivecontrolpanel";

    public static bool IsWindows11()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            if (key?.GetValue("CurrentBuild") is string buildStr && int.TryParse(buildStr, out int build))
                return build >= Windows11FirstBuild;
        }
        catch
        {
            // ignore
        }

        return Environment.OSVersion.Version.Build >= Windows11FirstBuild;
    }

    /// <summary>
    /// Opens the Default Apps settings page (simple URI; no scroll/focus on Windows 10).
    /// </summary>
    public static bool OpenDefaultApps()
    {
        return OpenUri(DefaultAppsUri);
    }

    /// <summary>
    /// Opens the Default Apps settings page with the Web browser row scrolled into view and focused (Windows 10 only).
    /// Uses IApplicationActivationManager with target=SystemSettings_DefaultApps_Browser, same as Firefox.
    /// On failure (e.g. older build), falls back to <see cref="OpenDefaultApps"/>.
    /// </summary>
    public static bool OpenDefaultAppsWithBrowserFocused()
    {
        try
        {
            var activator = (IApplicationActivationManager)new ApplicationActivationManager();
            const int aoNone = 0;

            // First open the Default apps page.
            int hr = activator.ActivateApplication(SettingsAppAumid, "page=SettingsPageAppsDefaults", aoNone, out _);
            if (hr < 0)
                return OpenDefaultApps();

            // Then activate with target so the Web browser section is scrolled into view and focused.
            activator.ActivateApplication(SettingsAppAumid,
                "page=SettingsPageAppsDefaults&target=SystemSettings_DefaultApps_Browser", aoNone, out _);
            return true;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"IApplicationActivationManager failed: {ex.Message}. Falling back to URI.");
            return OpenDefaultApps();
        }
    }

    /// <summary>
    /// Opens Windows Settings to the given app's default-apps page (Windows 11 only).
    /// <paramref name="registeredAppName"/> must match the value name under
    /// HKLM\SOFTWARE\RegisteredApplications or HKCU\Software\RegisteredApplications.
    /// </summary>
    /// <param name="useMachine">If true, use registeredAppMachine (HKLM); otherwise registeredAppUser (HKCU).</param>
    public static bool OpenDefaultAppsForApp(string registeredAppName, bool useMachine = true)
    {
        if (string.IsNullOrWhiteSpace(registeredAppName))
            return false;

        string encoded = Uri.EscapeDataString(registeredAppName.Trim());
        string param = useMachine ? "registeredAppMachine" : "registeredAppUser";
        string uri = $"{DefaultAppsUri}?{param}={encoded}";
        return OpenUri(uri);
    }

    /// <summary>
    /// Opens Windows Settings to the given packaged (Store) app's default-apps page (Windows 11 only).
    /// Uses registeredAUMID (e.g. "Mozilla.Firefox.12345_0zbj777q4mb0p!Firefox").
    /// </summary>
    public static bool OpenDefaultAppsForAumid(string aumid)
    {
        if (string.IsNullOrWhiteSpace(aumid))
            return false;

        string encoded = Uri.EscapeDataString(aumid.Trim());
        string uri = $"{DefaultAppsUri}?registeredAUMID={encoded}";
        return OpenUri(uri);
    }

    /// <summary>
    /// Launches a ms-settings: or other URI via the shell (default handler).
    /// </summary>
    public static bool OpenUri(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
            return false;

        try
        {
            var si = new ProcessStartInfo
            {
                FileName = uri,
                UseShellExecute = true,
            };
            Process.Start(si);
            return true;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Failed to open URI: {uri}. {ex.Message}");
            return false;
        }
    }

    private const string RegPathRegisteredApplications = @"SOFTWARE\RegisteredApplications";

    /// <summary>
    /// One browser entry for the Windows 11 "Set default" buttons. Uses the actual
    /// registered app name (or AUMID for Store apps) so the deep link navigates to that app's page.
    /// </summary>
    /// <param name="Aumid">When non-null, this is a packaged (Store) app; use registeredAUMID URI. Otherwise use RegisteredName with registeredAppMachine/User.</param>
    public sealed record BrowserEntry(string DisplayLabel, string RegisteredName, bool UseMachine, string? Aumid = null);

    /// <summary>
    /// Enumerates RegisteredApplications (HKLM and HKCU) and returns browser entries with
    /// the exact registered names so ms-settings:defaultapps?registeredApp*=... navigates correctly.
    /// Both release Firefox and Firefox Nightly register as "Firefox-&lt;hash&gt;" (different hashes);
    /// we resolve the display name from the StartMenuInternet key they point to.
    /// </summary>
    public static List<BrowserEntry> GetWindows11BrowserEntries()
    {
        var machineNames = GetRegisteredAppValueNames(Registry.LocalMachine);
        var userNames = GetRegisteredAppValueNames(Registry.CurrentUser);

        var result = new List<BrowserEntry>();

        // All "Firefox-<hash>" entries (release and Nightly both use this pattern). Resolve display name from registry.
        foreach (var name in machineNames.Where(n => n.StartsWith("Firefox-", StringComparison.Ordinal)).OrderBy(n => n))
            AddFirefoxEntry(result, name, true, Registry.LocalMachine);
        foreach (var name in userNames.Where(n => n.StartsWith("Firefox-", StringComparison.Ordinal)).OrderBy(n => n))
        {
            if (machineNames.Contains(name)) continue; // already added from HKLM
            AddFirefoxEntry(result, name, false, Registry.CurrentUser);
        }

        // Firefox (Microsoft Store): packaged app, use AUMID (PackageFamilyName!ApplicationId). Desktop Firefox uses Firefox-<hash>, not Store.
        if (TryGetStoreFirefoxAumid(out string? storeFirefoxAumid) && storeFirefoxAumid != null)
            result.Add(new BrowserEntry("Firefox (Microsoft Store)", storeFirefoxAumid, false, storeFirefoxAumid));

        // Microsoft Edge: fixed name, usually in HKLM
        const string edgeName = "Microsoft Edge";
        if (machineNames.Contains(edgeName))
            result.Add(new BrowserEntry("Microsoft Edge", edgeName, true));
        else if (userNames.Contains(edgeName))
            result.Add(new BrowserEntry("Microsoft Edge", edgeName, false));

        return result;
    }

    /// <summary>
    /// Resolves the display name for a "Firefox-<hash>" entry by reading the path it points to
    /// (e.g. Software\Clients\StartMenuInternet\Firefox-XXX\Capabilities) and the (Default) of the parent key.
    /// </summary>
    private static void AddFirefoxEntry(List<BrowserEntry> result, string registeredName, bool useMachine, RegistryKey root)
    {
        string displayLabel = ResolveFirefoxDisplayName(root, registeredName);
        result.Add(new BrowserEntry(displayLabel, registeredName, useMachine));
    }

    private static string ResolveFirefoxDisplayName(RegistryKey root, string registeredName)
    {
        try
        {
            using var regApps = root.OpenSubKey(RegPathRegisteredApplications);
            object? pathObj = regApps?.GetValue(registeredName);
            if (pathObj is not string path || string.IsNullOrWhiteSpace(path))
                return "Firefox (firefox.com)";

            // Path is typically "Software\Clients\StartMenuInternet\Firefox-XXX\Capabilities". Parent key has (Default) = "Firefox" or "Firefox Nightly".
            var pathTrimmed = path.Trim().Replace('/', '\\').TrimStart('\\');
            if (pathTrimmed.EndsWith("\\Capabilities", StringComparison.OrdinalIgnoreCase))
                pathTrimmed = pathTrimmed[..^"\\Capabilities".Length];
            if (!pathTrimmed.Contains("StartMenuInternet", StringComparison.Ordinal))
                return "Firefox (firefox.com)";

            // Path may be "Software\Clients\StartMenuInternet\Firefox-XXX" - open under root (HKLM/HKCU)
            using var parentKey = root.OpenSubKey(pathTrimmed);
            if (parentKey?.GetValue("") is string displayName && !string.IsNullOrWhiteSpace(displayName))
                return displayName.Trim();
        }
        catch
        {
            // ignore
        }
        return "Firefox (firefox.com)";
    }

    private static HashSet<string> GetRegisteredAppValueNames(RegistryKey root)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            using var key = root.OpenSubKey(RegPathRegisteredApplications);
            if (key == null) return set;
            foreach (var name in key.GetValueNames())
            {
                if (!string.IsNullOrEmpty(name))
                    set.Add(name);
            }
        }
        catch
        {
            // ignore
        }
        return set;
    }

    private static void AddFirstMatch(List<BrowserEntry> result, string displayLabel,
        HashSet<string> machineNames, HashSet<string> userNames, Predicate<string> match)
    {
        string? name = machineNames.FirstOrDefault(n => match(n)) ?? userNames.FirstOrDefault(n => match(n));
        if (string.IsNullOrEmpty(name)) return;
        bool useMachine = machineNames.Contains(name);
        result.Add(new BrowserEntry(displayLabel, name, useMachine, null));
    }

    /// <summary>
    /// Detects Firefox installed from the Microsoft Store via Start menu apps (AUMID).
    /// Store Firefox has AUMID like "Mozilla.Firefox.12345_0zbj777q4mb0p!Firefox"; desktop uses Firefox-&lt;hash&gt; in RegisteredApplications.
    /// </summary>
    private static bool TryGetStoreFirefoxAumid(out string? aumid)
    {
        aumid = null;
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -NonInteractive -Command \"Get-StartApps | Where-Object { $_.AppId -match '^Mozilla\\.Firefox' } | Select-Object -First 1 -ExpandProperty AppId\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var proc = Process.Start(psi);
            if (proc == null) return false;
            string output = proc.StandardOutput.ReadToEnd().Trim();
            proc.WaitForExit(5000);
            if (proc.ExitCode == 0 && !string.IsNullOrWhiteSpace(output) && output.Contains('!'))
                aumid = output;
        }
        catch
        {
            // ignore
        }
        return !string.IsNullOrEmpty(aumid);
    }

    #region IApplicationActivationManager (Windows 10 – focus Web browser in Default apps)

    [ComImport]
    [Guid("2E941141-7F97-4756-BA1D-9DECDE894A3D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IApplicationActivationManager
    {
        [PreserveSig]
        int ActivateApplication([MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
            [MarshalAs(UnmanagedType.LPWStr)] string arguments,
            int options,
            out uint processId);

        int ActivateForFile([MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
            IntPtr itemArray, [MarshalAs(UnmanagedType.LPWStr)] string verb, out uint processId);

        int ActivateForProtocol([MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
            IntPtr itemArray, out uint processId);
    }

    [ComImport]
    [Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")]
    private class ApplicationActivationManager
    {
    }

    #endregion
}
