namespace RoImportBridge;

internal sealed class BridgeApplicationContext : ApplicationContext
{
    private readonly NotifyIcon trayIcon;
    private readonly MainForm mainForm;
    private readonly BridgeServer bridgeServer;
    private readonly SingleInstanceMessenger messenger;

    public BridgeApplicationContext(string[] args)
    {
        var logStore = new UploadLogStore();
        bridgeServer = new BridgeServer(logStore);
        mainForm = new MainForm(bridgeServer, logStore);
        trayIcon = CreateTrayIcon();
        messenger = new SingleInstanceMessenger();
        messenger.OpenRequested += ShowWindow;
        messenger.Start();
        StartBridge();
        ShowInitialWindow(args);
    }

    private NotifyIcon CreateTrayIcon()
    {
        var menu = new ContextMenuStrip
        {
            BackColor = Color.FromArgb(24, 24, 27),
            ForeColor = Color.White
        };
        menu.Items.Add("Open", null, (_, _) => ShowWindow());
        menu.Items.Add("Exit", null, (_, _) => ExitApplication());

        var icon = new NotifyIcon
        {
            Icon = AppAssets.GetIcon(),
            Text = "RoImport Bridge",
            Visible = true,
            ContextMenuStrip = menu
        };
        icon.DoubleClick += (_, _) => ShowWindow();
        return icon;
    }

    private void StartBridge()
    {
        _ = bridgeServer.StartAsync();
    }

    private void ShowInitialWindow(string[] args)
    {
        if (args.Any(argument => argument.Equals("--background", StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        ShowWindow();
    }

    private void ShowWindow()
    {
        if (mainForm.InvokeRequired)
        {
            mainForm.BeginInvoke(new Action(ShowWindow));
            return;
        }

        if (!mainForm.Visible)
        {
            mainForm.Show();
        }

        if (mainForm.WindowState == FormWindowState.Minimized)
        {
            mainForm.WindowState = FormWindowState.Normal;
        }

        mainForm.ShowInTaskbar = true;
        mainForm.BringToFront();
        mainForm.Activate();
    }

    private void ExitApplication()
    {
        trayIcon.Visible = false;
        trayIcon.Dispose();
        messenger.Dispose();
        bridgeServer.Dispose();
        mainForm.Dispose();
        ExitThread();
    }
}
