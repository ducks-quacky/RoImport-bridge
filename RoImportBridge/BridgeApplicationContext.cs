namespace RoImportBridge;

internal sealed class BridgeApplicationContext : ApplicationContext
{
    private readonly NotifyIcon trayIcon;
    private readonly MainForm mainForm;
    private readonly BridgeServer bridgeServer;

    public BridgeApplicationContext(string[] args)
    {
        bridgeServer = new BridgeServer();
        mainForm = new MainForm(bridgeServer);
        trayIcon = CreateTrayIcon();
        mainForm.FormClosed += HandleFormClosed;
        StartBridge();
        ShowInitialWindow(args);
    }

    private NotifyIcon CreateTrayIcon()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => ShowWindow());
        menu.Items.Add("Exit", null, (_, _) => ExitApplication());

        return new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "RoImport Bridge",
            Visible = true,
            ContextMenuStrip = menu
        };
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
        if (mainForm.Visible)
        {
            mainForm.Activate();
            return;
        }

        mainForm.Show();
        mainForm.WindowState = FormWindowState.Normal;
        mainForm.Activate();
    }

    private void HandleFormClosed(object? sender, FormClosedEventArgs e)
    {
        ExitApplication();
    }

    private void ExitApplication()
    {
        trayIcon.Visible = false;
        trayIcon.Dispose();
        bridgeServer.Dispose();
        ExitThread();
    }
}
