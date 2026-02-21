using System.Diagnostics;
using System.Drawing;
using System.Windows.Automation;

namespace DIExplorer;

/// <summary>
/// Uses UI Automation to locate the "Set default" button inside the
/// Windows 11 Settings default-apps page. The Settings app loads
/// asynchronously after the ms-settings: URI is launched, so this
/// class polls the UI tree until the button appears or a timeout expires.
/// </summary>
internal static class SettingsButtonFinder
{
    private const int PollIntervalMs = 500;
    private const int MaxPollDurationMs = 10_000;

    /// <summary>
    /// Diagnostic info collected during the most recent search attempt.
    /// Read from the UI thread after awaiting a search to show progress.
    /// </summary>
    public static string LastDiagnostic { get; private set; } = "";

    /// <summary>
    /// Polls the UI Automation tree for the "Set default" button inside the
    /// Windows Settings window. Returns the button's screen-coordinate
    /// bounding rectangle, or null if the button was not found within the
    /// timeout period. <paramref name="progress"/> is invoked on each poll
    /// iteration with a human-readable status string.
    /// </summary>
    public static async Task<Rectangle?> FindSetDefaultButtonAsync(
        CancellationToken ct = default, Action<string>? progress = null)
    {
        var sw = Stopwatch.StartNew();
        int attempt = 0;

        while (sw.ElapsedMilliseconds < MaxPollDurationMs)
        {
            ct.ThrowIfCancellationRequested();
            attempt++;

            var rect = FindSetDefaultButton();
            if (rect.HasValue)
            {
                string msg = $"[Highlight] Found button at {rect.Value} after {attempt} poll(s).";
                LastDiagnostic = msg;
                progress?.Invoke(msg);
                return rect;
            }

            progress?.Invoke($"[Highlight] Poll {attempt}: {LastDiagnostic}");

            try
            {
                await Task.Delay(PollIntervalMs, ct);
            }
            catch (TaskCanceledException)
            {
                return null;
            }
        }

        string timeout = $"[Highlight] Timed out after {attempt} polls ({sw.ElapsedMilliseconds}ms). Last: {LastDiagnostic}";
        LastDiagnostic = timeout;
        progress?.Invoke(timeout);
        return null;
    }

    /// <summary>
    /// Returns the current screen rectangle of the "Set default" button,
    /// or null if the Settings window or button is not found.
    /// Safe to call from any thread.
    /// </summary>
    public static Rectangle? FindSetDefaultButton()
    {
        try
        {
            var settingsWindow = FindSettingsWindow();
            if (settingsWindow == null)
            {
                LastDiagnostic = "Settings window not found.";
                return null;
            }

            var allButtons = settingsWindow.FindAll(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));

            AutomationElement? button = null;
            var names = new List<string>();
            foreach (AutomationElement b in allButtons)
            {
                try
                {
                    string bName = b.Current.Name ?? "";
                    names.Add($"\"{bName}\"");
                    if (button == null &&
                        bName.IndexOf("Set default", StringComparison.OrdinalIgnoreCase) >= 0)
                        button = b;
                }
                catch { }
            }

            if (button == null)
            {
                LastDiagnostic = $"Settings window found, but no button containing \"Set default\". "
                    + $"Buttons ({names.Count}): {string.Join(", ", names.Take(15))}"
                    + (names.Count > 15 ? $" ... +{names.Count - 15} more" : "");
                return null;
            }

            var r = button.Current.BoundingRectangle;
            if (r.IsEmpty || double.IsInfinity(r.Width) || double.IsInfinity(r.Height))
            {
                LastDiagnostic = $"Button found but BoundingRectangle is invalid: {r}";
                return null;
            }

            var result = new Rectangle((int)r.X, (int)r.Y, (int)r.Width, (int)r.Height);
            LastDiagnostic = $"Button at {result}. Name: \"{button.Current.Name}\".";
            return result;
        }
        catch (ElementNotAvailableException)
        {
            LastDiagnostic = "ElementNotAvailableException (window closed mid-search).";
            return null;
        }
        catch (Exception ex)
        {
            LastDiagnostic = $"Exception: {ex.GetType().Name}: {ex.Message}";
            Trace.WriteLine($"SettingsButtonFinder error: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Returns true if any top-level Settings window is still open.
    /// </summary>
    public static bool IsSettingsWindowOpen()
    {
        try
        {
            return FindSettingsWindow() != null;
        }
        catch
        {
            return false;
        }
    }

    private static AutomationElement? FindSettingsWindow()
    {
        var children = AutomationElement.RootElement.FindAll(
            TreeScope.Children,
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window));

        foreach (AutomationElement window in children)
        {
            try
            {
                string cls = window.Current.ClassName ?? "";
                string name = window.Current.Name ?? "";

                if (cls == "ApplicationFrameWindow" &&
                    name.IndexOf("Settings", StringComparison.OrdinalIgnoreCase) >= 0)
                    return window;

                int pid = window.Current.ProcessId;
                try
                {
                    var proc = Process.GetProcessById(pid);
                    if (proc.ProcessName.Equals("SystemSettings", StringComparison.OrdinalIgnoreCase))
                        return window;
                }
                catch { }
            }
            catch (ElementNotAvailableException)
            {
                continue;
            }
        }

        return null;
    }
}
