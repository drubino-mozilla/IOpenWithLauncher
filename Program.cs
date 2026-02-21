using System.Drawing;
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
    private readonly TextBox _input;
    private readonly Button _launchButton;
    private readonly Label _statusLabel;

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

        var inputLabel = new Label
        {
            Text = "File extension:",
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 4),
        };
        outer.Controls.Add(inputLabel);

        var inputRow = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Margin = new Padding(0, 0, 0, 12),
        };

        _input = new TextBox
        {
            Width = 260,
            Text = ".pdf",
            Margin = new Padding(0, 0, 8, 0),
        };

        _launchButton = new Button
        {
            Text = "Launch",
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(12, 2, 12, 2),
        };

        inputRow.Controls.AddRange([_input, _launchButton]);
        outer.Controls.Add(inputRow);

        _statusLabel = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(400, 0),
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(0, 0, 0, 0),
        };
        outer.Controls.Add(_statusLabel);

        Controls.Add(outer);

        _launchButton.Click += OnLaunchClick;
        _input.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                OnLaunchClick(this, EventArgs.Empty);
                e.SuppressKeyPress = true;
            }
        };
        AcceptButton = _launchButton;
    }

    private async void OnLaunchClick(object? sender, EventArgs e)
    {
        var value = _input.Text.Trim();
        if (string.IsNullOrEmpty(value))
        {
            _statusLabel.ForeColor = Color.OrangeRed;
            _statusLabel.Text = "Please enter a file extension.";
            return;
        }

        string ext = value.StartsWith('.') ? value : "." + value;

        _statusLabel.ForeColor = SystemColors.GrayText;
        _statusLabel.Text = $"Opening default-app dialog for \"{ext}\"...\n(waiting for you to close it)";
        _launchButton.Enabled = false;

        var hwnd = Handle;
        var result = await Task.Run(() =>
            OpenWithLauncherInterop.LaunchForFileExtension(ext, hwnd));

        _launchButton.Enabled = true;
        _statusLabel.ForeColor = result.Succeeded ? Color.Green : Color.OrangeRed;
        _statusLabel.Text = result.Message;
    }
}
