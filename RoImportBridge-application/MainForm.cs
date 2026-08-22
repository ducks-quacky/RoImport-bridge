namespace RoImportBridge;

internal sealed class MainForm : Form
{
    private readonly BridgeServer bridgeServer;
    private readonly UploadLogStore logStore;
    private readonly Label statusLabel;
    private readonly CheckBox startupCheckBox;
    private readonly CheckBox startupBackgroundCheckBox;

    public MainForm(BridgeServer bridgeServer, UploadLogStore logStore)
    {
        this.bridgeServer = bridgeServer;
        this.logStore = logStore;
        Text = "RoImport Bridge";
        Width = 520;
        Height = 390;
        MinimumSize = new Size(520, 390);
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        BackColor = Color.FromArgb(14, 14, 16);
        ForeColor = Color.White;
        Icon = AppAssets.GetIcon();

        statusLabel = CreateStatusLabel();
        startupCheckBox = CreateToggle("Start when Windows starts", StartupManager.IsEnabled());
        startupBackgroundCheckBox = CreateToggle("Start in the background", StartupManager.RunsInBackground());
        startupBackgroundCheckBox.Enabled = startupCheckBox.Checked;

        Controls.Add(CreateHeader());
        Controls.Add(CreateStatusCard());
        Controls.Add(CreateSettingsPanel());
        Controls.Add(CreateActionPanel());

        startupCheckBox.CheckedChanged += HandleStartupChanged;
        startupBackgroundCheckBox.CheckedChanged += HandleStartupBackgroundChanged;
        bridgeServer.StatusChanged += UpdateStatus;
        FormClosing += HandleFormClosing;
    }

    private Control CreateHeader()
    {
        var panel = new Panel
        {
            Location = new Point(24, 18),
            Size = new Size(456, 58),
            BackColor = BackColor
        };
        var logo = new PictureBox
        {
            Location = new Point(0, 0),
            Size = new Size(52, 52),
            SizeMode = PictureBoxSizeMode.Zoom,
            Image = AppAssets.GetLogo()
        };
        var subtitle = new Label
        {
            Text = "Local image upload service",
            AutoSize = true,
            ForeColor = Color.FromArgb(155, 155, 164),
            Font = new Font("Segoe UI", 10),
            Location = new Point(68, 17)
        };
        panel.Controls.Add(logo);
        panel.Controls.Add(subtitle);
        return panel;
    }

    private Control CreateStatusCard()
    {
        var panel = CreateCard(new Point(24, 88), new Size(456, 66));
        var title = new Label
        {
            Text = "Status",
            AutoSize = true,
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            ForeColor = Color.FromArgb(170, 170, 178),
            Location = new Point(16, 11)
        };
        statusLabel.Location = new Point(16, 33);
        panel.Controls.Add(title);
        panel.Controls.Add(statusLabel);
        return panel;
    }

    private Control CreateSettingsPanel()
    {
        var panel = CreateCard(new Point(24, 168), new Size(456, 104));
        startupCheckBox.Location = new Point(16, 16);
        startupBackgroundCheckBox.Location = new Point(38, 58);
        panel.Controls.Add(startupCheckBox);
        panel.Controls.Add(startupBackgroundCheckBox);
        return panel;
    }

    private Control CreateActionPanel()
    {
        var panel = new FlowLayoutPanel
        {
            Location = new Point(24, 292),
            Size = new Size(456, 42),
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = BackColor
        };
        var backgroundButton = CreateButton("Run in background", 164);
        var logsButton = CreateButton("Logs", 100);
        var exitButton = CreateButton("Exit", 92);
        backgroundButton.Click += (_, _) => Hide();
        logsButton.Click += (_, _) => ShowLogs();
        exitButton.Click += (_, _) => Application.Exit();
        panel.Controls.Add(backgroundButton);
        panel.Controls.Add(logsButton);
        panel.Controls.Add(exitButton);
        return panel;
    }

    private Label CreateStatusLabel()
    {
        return new Label
        {
            Text = "Starting...",
            AutoSize = true,
            Font = new Font("Segoe UI", 10),
            ForeColor = Color.FromArgb(230, 230, 234)
        };
    }

    private CheckBox CreateToggle(string text, bool isChecked)
    {
        return new CheckBox
        {
            Text = text,
            AutoSize = true,
            Checked = isChecked,
            Font = new Font("Segoe UI", 10),
            ForeColor = Color.FromArgb(235, 235, 238),
            BackColor = Color.FromArgb(22, 22, 25),
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand
        };
    }

    private Panel CreateCard(Point location, Size size)
    {
        return new Panel
        {
            Location = location,
            Size = size,
            BackColor = Color.FromArgb(22, 22, 25)
        };
    }

    private Button CreateButton(string text, int width)
    {
        var button = new Button
        {
            Text = text,
            Width = width,
            Height = 36,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(34, 34, 39),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 9),
            Cursor = Cursors.Hand,
            Margin = new Padding(0, 0, 10, 0)
        };
        button.FlatAppearance.BorderColor = Color.FromArgb(55, 55, 62);
        return button;
    }

    private void HandleStartupChanged(object? sender, EventArgs e)
    {
        startupBackgroundCheckBox.Enabled = startupCheckBox.Checked;
        SaveStartupSettings();
    }

    private void HandleStartupBackgroundChanged(object? sender, EventArgs e)
    {
        SaveStartupSettings();
    }

    private void SaveStartupSettings()
    {
        try
        {
            StartupManager.SetEnabled(startupCheckBox.Checked, startupBackgroundCheckBox.Checked);
        }
        catch (Exception error)
        {
            MessageBox.Show(error.Message, "RoImport Bridge", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ShowLogs()
    {
        using var logsForm = new LogsForm(logStore);
        logsForm.ShowDialog(this);
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
