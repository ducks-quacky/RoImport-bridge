namespace RoImportBridge;

internal sealed class LogsForm : Form
{
    private readonly UploadLogStore logStore;
    private readonly ListView logList;
    private readonly Button copyButton;

    public LogsForm(UploadLogStore logStore)
    {
        this.logStore = logStore;
        Text = "Upload Logs";
        Width = 900;
        Height = 520;
        MinimumSize = new Size(720, 420);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Color.FromArgb(14, 14, 16);
        ForeColor = Color.White;
        Icon = AppAssets.GetIcon();

        logList = CreateLogList();
        copyButton = CreateButton("Copy details", 128);
        var refreshButton = CreateButton("Refresh", 90);

        copyButton.Enabled = false;
        copyButton.Click += (_, _) => CopySelectedDetails();
        refreshButton.Click += (_, _) => RefreshLogs();
        logList.SelectedIndexChanged += (_, _) => copyButton.Enabled = logList.SelectedItems.Count > 0;

        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 58,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(16, 12, 16, 10),
            BackColor = BackColor
        };
        footer.Controls.Add(copyButton);
        footer.Controls.Add(refreshButton);

        Controls.Add(logList);
        Controls.Add(footer);
        RefreshLogs();
    }

    public void RefreshLogs()
    {
        logList.BeginUpdate();
        logList.Items.Clear();

        foreach (var entry in logStore.GetEntries())
        {
            var item = new ListViewItem(entry.UploadedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss"));
            item.SubItems.Add(entry.FileName);
            item.SubItems.Add(entry.AssetId);
            item.SubItems.Add($"{entry.CreatorType}:{entry.CreatorId}");
            item.SubItems.Add(FormatSize(entry.FileSizeBytes));
            item.Tag = entry;
            logList.Items.Add(item);
        }

        logList.EndUpdate();
    }

    private ListView CreateLogList()
    {
        var list = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            BorderStyle = BorderStyle.None,
            BackColor = Color.FromArgb(20, 20, 23),
            ForeColor = Color.FromArgb(235, 235, 238),
            HideSelection = false
        };
        list.Columns.Add("Uploaded", 150);
        list.Columns.Add("File", 300);
        list.Columns.Add("Asset ID", 130);
        list.Columns.Add("Creator", 150);
        list.Columns.Add("Size", 90);
        return list;
    }

    private Button CreateButton(string text, int width)
    {
        var button = new Button
        {
            Text = text,
            Width = width,
            Height = 32,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(36, 36, 41),
            ForeColor = Color.White,
            Cursor = Cursors.Hand
        };
        button.FlatAppearance.BorderColor = Color.FromArgb(58, 58, 64);
        return button;
    }

    private void CopySelectedDetails()
    {
        if (logList.SelectedItems.Count == 0 || logList.SelectedItems[0].Tag is not UploadLogEntry entry)
        {
            return;
        }

        var details = string.Join(Environment.NewLine,
            $"Uploaded: {entry.UploadedAt.LocalDateTime:yyyy-MM-dd HH:mm:ss}",
            $"File: {entry.FileName}",
            $"Asset ID: {entry.AssetId}",
            $"Creator: {entry.CreatorType}:{entry.CreatorId}",
            $"Content type: {entry.ContentType}",
            $"Size: {FormatSize(entry.FileSizeBytes)}");
        Clipboard.SetText(details);
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        if (bytes < 1024 * 1024)
        {
            return $"{bytes / 1024d:0.0} KB";
        }

        return $"{bytes / 1024d / 1024d:0.0} MB";
    }
}
