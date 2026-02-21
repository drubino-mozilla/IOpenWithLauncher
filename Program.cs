using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace DIExplorer;

static class Program
{
    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.Run(new MainForm());
    }
}

sealed class MainForm : Form
{
    private readonly Label _statusLabel;
    private CancellationTokenSource? _highlightCts;
    private HighlightOverlayForm? _highlightOverlay;

    public MainForm()
    {
        Text = "DIExplorer";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Font = new Font("Segoe UI", 10f);
        Padding = new Padding(16);

        var outer = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Padding = Padding.Empty,
        };
        outer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var pdfButtonRow = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Margin = new Padding(0, 0, 0, 12),
        };
        var setPdfButton = new Button
        {
            Text = "Set PDF default",
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(12, 2, 12, 2),
            Margin = new Padding(0, 0, 8, 0),
        };
        setPdfButton.Click += OnSetPdfDefaultClick;
        pdfButtonRow.Controls.Add(setPdfButton);
        var testPdfButton = new Button
        {
            Text = "Test a PDF",
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(12, 2, 12, 2),
            Margin = new Padding(0, 0, 0, 0),
        };
        testPdfButton.Click += OnTestPdfClick;
        pdfButtonRow.Controls.Add(testPdfButton);
        outer.Controls.Add(pdfButtonRow);

        _statusLabel = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(600, 0),
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(0, 0, 0, 0),
        };
        // _statusLabel added at end so it exists when wiring browser buttons

        // --- Browser defaults (ms-settings) ---
        var browserLabel = new Label
        {
            Text = "Browser default (opens Windows Settings):",
            AutoSize = true,
            Margin = new Padding(0, 4, 0, 4),
        };
        outer.Controls.Add(browserLabel);

        bool isWin11 = WindowsSettingsLauncher.IsWindows11();
        if (isWin11)
        {
            var browserFlow = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = true,
                Margin = new Padding(0, 0, 0, 12),
            };
            foreach (var entry in WindowsSettingsLauncher.GetWindows11BrowserEntries())
                AddBrowserButton(browserFlow, _statusLabel, entry);
            outer.Controls.Add(browserFlow);
        }
        else
        {
            var setBrowserButton = new Button
            {
                Text = "Set browser default",
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(12, 2, 12, 2),
                Margin = new Padding(0, 0, 0, 12),
            };
            setBrowserButton.Click += (_, _) =>
            {
                bool ok = WindowsSettingsLauncher.OpenDefaultAppsWithBrowserFocused();
                _statusLabel.ForeColor = SystemColors.GrayText;
                _statusLabel.Text = ok ? "Opened Default apps with Web browser focused (Windows 10)." : "Opened Default apps (Windows 10).";
            };
            outer.Controls.Add(setBrowserButton);
        }

        var testLabel = new Label
        {
            Text = "Test default browser:",
            AutoSize = true,
            Margin = new Padding(0, 8, 0, 4),
        };
        outer.Controls.Add(testLabel);

        var testLinkButton = new Button
        {
            Text = "Open https://www.firefox.com/?",
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(12, 2, 12, 2),
            Margin = new Padding(0, 0, 0, 12),
        };
        testLinkButton.Click += (_, _) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo("https://www.firefox.com/?") { UseShellExecute = true });
                _statusLabel.ForeColor = SystemColors.GrayText;
                _statusLabel.Text = "Opened link in default browser.";
            }
            catch (Exception ex)
            {
                _statusLabel.ForeColor = Color.OrangeRed;
                _statusLabel.Text = $"Failed to open link: {ex.Message}";
            }
        };
        outer.Controls.Add(testLinkButton);

        outer.Controls.Add(_statusLabel);
        Controls.Add(outer);
    }

    private void AddBrowserButton(FlowLayoutPanel parent, Label statusLabel, WindowsSettingsLauncher.BrowserEntry entry)
    {
        var btn = new Button
        {
            Text = entry.DisplayLabel,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(12, 2, 12, 2),
            Margin = new Padding(0, 0, 8, 4),
        };
        btn.Click += (_, _) =>
        {
            bool ok = entry.Aumid != null
                ? WindowsSettingsLauncher.OpenDefaultAppsForAumid(entry.Aumid)
                : WindowsSettingsLauncher.OpenDefaultAppsForApp(entry.RegisteredName, entry.UseMachine);
            statusLabel.ForeColor = SystemColors.GrayText;
            statusLabel.Text = ok ? $"Opened Settings for {entry.DisplayLabel}." : "Could not open Settings.";

            if (ok)
            {
                bool isEdge = entry.DisplayLabel.Contains("Edge", StringComparison.OrdinalIgnoreCase);
                var character = isEdge ? OverlayCharacter.Clippy : OverlayCharacter.Kit;
                _ = StartSetDefaultHighlightAsync(character);
            }
        };
        parent.Controls.Add(btn);
    }

    private async Task StartSetDefaultHighlightAsync(OverlayCharacter character)
    {
        DismissHighlight();

        var cts = new CancellationTokenSource();
        _highlightCts = cts;

        void AppendStatus(string msg)
        {
            void Apply()
            {
                _statusLabel.ForeColor = SystemColors.GrayText;
                _statusLabel.Text = string.IsNullOrEmpty(_statusLabel.Text)
                    ? msg
                    : _statusLabel.Text + "\r\n" + msg;
            }

            if (InvokeRequired)
                BeginInvoke(Apply);
            else
                Apply();
        }

        try
        {
            AppendStatus("[Highlight] Searching for \"Set default\" button...");

            var rect = await Task.Run(
                () => SettingsButtonFinder.FindSetDefaultButtonAsync(cts.Token,
                    msg => AppendStatus(msg)),
                cts.Token);

            if (rect == null || cts.IsCancellationRequested)
            {
                AppendStatus($"[Highlight] Button not found. {SettingsButtonFinder.LastDiagnostic}");
                return;
            }

            AppendStatus($"[Highlight] Showing overlay at {rect.Value}");

            var overlay = new HighlightOverlayForm(character);
            overlay.SetTargetBounds(rect.Value);
            overlay.Show();
            _highlightOverlay = overlay;

            const int trackIntervalMs = 500;
            const int maxTrackMs = 30_000;
            var sw = System.Diagnostics.Stopwatch.StartNew();

            while (sw.ElapsedMilliseconds < maxTrackMs && !cts.IsCancellationRequested)
            {
                await Task.Delay(trackIntervalMs, cts.Token);

                if (!SettingsButtonFinder.IsSettingsWindowOpen())
                {
                    AppendStatus("[Highlight] Settings window closed — dismissing overlay.");
                    break;
                }

                var updated = await Task.Run(() => SettingsButtonFinder.FindSetDefaultButton(), cts.Token);
                if (updated == null)
                {
                    AppendStatus("[Highlight] Button disappeared — dismissing overlay.");
                    break;
                }

                if (overlay.IsDisposed)
                    break;

                overlay.SetTargetBounds(updated.Value);
                overlay.Invalidate();
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            DismissHighlight();
        }
    }

    private void DismissHighlight()
    {
        _highlightCts?.Cancel();
        _highlightCts?.Dispose();
        _highlightCts = null;

        if (_highlightOverlay is { IsDisposed: false })
        {
            _highlightOverlay.Close();
            _highlightOverlay.Dispose();
        }
        _highlightOverlay = null;
    }

    private async void OnSetPdfDefaultClick(object? sender, EventArgs e)
    {
        _statusLabel.ForeColor = SystemColors.GrayText;
        _statusLabel.Text = "Opening default-app dialog for \".pdf\"...\n(waiting for you to close it)";

        var hwnd = Handle;
        var result = await Task.Run(() =>
            OpenWithLauncherInterop.LaunchForFileExtension(".pdf", hwnd));

        _statusLabel.ForeColor = result.Succeeded ? Color.Green : Color.OrangeRed;
        _statusLabel.Text = result.Message;
    }

    private void OnTestPdfClick(object? sender, EventArgs e)
    {
        const string resourceName = "Test.PDF";
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            _statusLabel.ForeColor = Color.OrangeRed;
            _statusLabel.Text = $"Embedded resource '{resourceName}' not found.";
            return;
        }

        string tempPath = Path.Combine(Path.GetTempPath(), "DIExplorer_Test.PDF");
        try
        {
            using (var file = File.Create(tempPath))
                stream.CopyTo(file);
            Process.Start(new ProcessStartInfo(tempPath) { UseShellExecute = true });
            _statusLabel.ForeColor = SystemColors.GrayText;
            _statusLabel.Text = $"Opened PDF in default app: {tempPath}";
        }
        catch (Exception ex)
        {
            _statusLabel.ForeColor = Color.OrangeRed;
            _statusLabel.Text = $"Failed to extract or launch PDF: {ex.Message}";
        }
    }
}
