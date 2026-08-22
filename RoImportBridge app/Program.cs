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
                SingleInstanceMessenger.RequestOpenAsync().GetAwaiter().GetResult();
            }

            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new BridgeApplicationContext(args));
    }
}
