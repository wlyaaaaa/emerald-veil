using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;

namespace EmeraldVeil.App;

/// <summary>Bounded, current-user/current-session commands for the existing tray process.</summary>
internal sealed class SessionCommandChannel : IDisposable
{
    internal static string PipeName => $"EmeraldVeil.Control.{Process.GetCurrentProcess().SessionId}";
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _server;

    internal SessionCommandChannel(Func<string, Task<string>> dispatch)
    {
        _server = Task.Run(() => ServeAsync(dispatch));
    }

    private async Task ServeAsync(Func<string, Task<string>> dispatch)
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(_stop.Token).ConfigureAwait(false);
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                deadline.CancelAfter(TimeSpan.FromSeconds(3));
                byte[] input = new byte[32];
                int length = 0;
                while (length < input.Length)
                {
                    int count = await pipe.ReadAsync(input.AsMemory(length, 1), deadline.Token).ConfigureAwait(false);
                    if (count == 0) break;
                    if (input[length++] == (byte)'\n') break;
                }
                string command = Encoding.UTF8.GetString(input, 0, length).Trim();
                string response = await dispatch(command).WaitAsync(deadline.Token).ConfigureAwait(false);
                await pipe.WriteAsync(Encoding.UTF8.GetBytes(response), deadline.Token).ConfigureAwait(false);
                await pipe.FlushAsync(deadline.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (IOException) { }
            catch (Exception exception)
            {
                Debug.WriteLine($"Local command failed: {exception.Message}");
                try { await Task.Delay(250, _stop.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { }
            }
        }
    }

    internal static async Task<string> SendAsync(string command)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(deadline.Token).ConfigureAwait(false);
        await pipe.WriteAsync(Encoding.UTF8.GetBytes(command + "\n"), deadline.Token).ConfigureAwait(false);
        await pipe.FlushAsync(deadline.Token).ConfigureAwait(false);
        using var output = new MemoryStream();
        byte[] buffer = new byte[4096];
        while (true)
        {
            int count = await pipe.ReadAsync(buffer, deadline.Token).ConfigureAwait(false);
            if (count == 0) break;
            if (output.Length + count > 65536) throw new InvalidDataException("Control response exceeds its bound.");
            output.Write(buffer, 0, count);
        }
        return Encoding.UTF8.GetString(output.ToArray());
    }

    public void Dispose()
    {
        _stop.Cancel();
        _ = _server.ContinueWith(_ => _stop.Dispose(), TaskScheduler.Default);
    }
}
