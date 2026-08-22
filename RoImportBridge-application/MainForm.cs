namespace RoImportBridge;

internal sealed class MainForm : Form
{
    private readonly BridgeServer bridgeServer;
    private readonly Label statusLabel;
    private readonly CheckBox startupCheckBox;

    public MainForm(BridgeServer bridgeServer)
    {
        this.bridgeServer = bridgeServer;
        Text = "RoImport Bridge";
        Width = 430;
        Height = 250;
        MinimumSize = new Size(430, 250);
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        FormBorderStyle = FormBorderStyle.FixedDialog;

        statusLabel = CreateStatusLabel();
        startupCheckBox = CreateStartupCheckBox();
        Controls.Add(CreateTitleLabel());
        Controls.Add(statusLabel);
        Controls.Add(startupCheckBox);
        Controls.Add(CreateHideButton());

        bridgeServer.StatusChanged += UpdateStatus;
        FormClosing += HandleFormClosing;
    }

    private Label CreateTitleLabel()
    {
        return new Label
        {
            Text = "RoImport Local Bridge",
            AutoSize = true,
            Font = new Font(Font.FontFamily, 14, FontStyle.Bold),
            Location = new Point(24, 24)
        };
    }

    private Label CreateStatusLabel()
    {
        return new Label
        {
            Text = "Starting bridge...",
            AutoSize = true,
            Location = new Point(26, 70)
        };
    }

    private CheckBox CreateStartupCheckBox()
    {
        var checkBox = new CheckBox
        {
            Text = "Run RoImport Bridge when Windows starts",
            AutoSize = true,
            Checked = StartupManager.IsEnabled(),
            Location = new Point(26, 108)
        };
        checkBox.CheckedChanged += HandleStartupChanged;
        return checkBox;
    }

    private Button CreateHideButton()
    {
        var button = new Button
        {
            Text = "Run in background",
            Width = 160,
            Height = 34,
            Location = new Point(26, 150)
        };
        button.Click += (_, _) => Hide();
        return button;
    }

    private void HandleStartupChanged(object? sender, EventArgs e)
    {
        try
        {
            StartupManager.SetEnabled(startupCheckBox.Checked);
        }
        catch (Exception error)
        {
            startupCheckBox.CheckedChanged -= HandleStartupChanged;
            startupCheckBox.Checked = !startupCheckBox.Checked;
            startupCheckBox.CheckedChanged += HandleStartupChanged;
            MessageBox.Show(error.Message, "RoImport Bridge", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void UpdateStatus(string status)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => UpdateStatus(status)));
            return;
        }

        statusLabel.Text = status;
    }

    private void HandleFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (e.CloseReason != CloseReason.UserClosing)
        {
            return;
        }

        e.Cancel = true;
        Hide();
    }
}
