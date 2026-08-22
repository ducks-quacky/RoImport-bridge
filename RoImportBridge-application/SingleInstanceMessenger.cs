using System.IO.Pipes;
using System.Text;

namespace RoImportBridge;

internal sealed class SingleInstanceMessenger : IDisposable
{
    private const string PipeName = "RoImportBridge.OpenWindow";
    private readonly CancellationTokenSource cancellationTokenSource = new();

    public event Action? OpenRequested;

    public void Start()
    {
        _ = ListenAsync(cancellationTokenSource.Token);
    }

    public static async Task RequestOpenAsync()
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            await client.ConnectAsync(1500);
            var bytes = Encoding.UTF8.GetBytes("open");
            await client.WriteAsync(bytes);
            await client.FlushAsync();
        }
        catch
        {
        }
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(cancellationToken);
                var buffer = new byte[32];
                var count = await server.ReadAsync(buffer, cancellationToken);
                var message = Encoding.UTF8.GetString(buffer, 0, count);

                if (message.Equals("open", StringComparison.OrdinalIgnoreCase))
                {
                    OpenRequested?.Invoke();
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
            }
        }
    }

    public void Dispose()
    {
        cancellationTokenSource.Cancel();
        cancellationTokenSource.Dispose();
    }
}
