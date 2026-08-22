using System.Threading;

namespace RoImportBridge;

internal static class Program
{
    private const string MutexName = "RoImportBridge.SingleInstance";

    [STAThread]
    private static void Main(string[] args)
    {
        using var mutex = new Mutex(true, MutexName, out var ownsMutex);

        if (!ownsMutex)
        {
            if (!args.Any(argument => argument.Equals("--background", StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show("RoImport Bridge is already running.", "RoImport Bridge", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }

            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new BridgeApplicationContext(args));
    }
}
