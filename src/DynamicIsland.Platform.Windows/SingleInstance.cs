using System.IO.Pipes;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace DynamicIsland.Platform.Windows;

/// <summary>
/// Guarantees one running instance per user session. The first instance owns a named mutex and
/// listens on a named pipe; later instances forward their command-line arguments and exit.
/// The pipe is created with <see cref="PipeOptions.CurrentUserOnly"/>, so other users cannot connect.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private const int MaxMessageBytes = 8 * 1024;
    private static readonly string Suffix = WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
    private static readonly string MutexName = $"Local\\DynamicIsland-7C1E0A52-{Suffix}";
    private static readonly string PipeName = $"DynamicIsland-{Suffix}";

    private readonly Mutex _mutex;
    private readonly CancellationTokenSource _cts = new();
    private bool _ownsMutex;
    private ILogger? _logger;

    private SingleInstance(Mutex mutex, bool ownsMutex)
    {
        _mutex = mutex;
        _ownsMutex = ownsMutex;
    }

    /// <summary>Whether this process is the primary instance.</summary>
    public bool IsPrimary => _ownsMutex;

    /// <summary>Raised on a thread-pool thread with the arguments a secondary instance forwarded.</summary>
    public event EventHandler<string[]>? ArgumentsReceived;

    /// <summary>
    /// Tries to become the primary instance. With a positive <paramref name="wait"/> (used on restart)
    /// it waits for the previous process to release the mutex.
    /// </summary>
    public static SingleInstance Acquire(TimeSpan wait)
    {
        var mutex = new Mutex(initiallyOwned: false, MutexName);
        bool owns;
        try
        {
            owns = mutex.WaitOne(wait);
        }
        catch (AbandonedMutexException)
        {
            // The previous owner crashed; the mutex is now ours.
            owns = true;
        }

        return new SingleInstance(mutex, owns);
    }

    /// <summary>Starts listening for forwarded arguments. Only valid on the primary instance.</summary>
    public void StartServer(ILogger logger)
    {
        if (!_ownsMutex)
        {
            throw new InvalidOperationException("Only the primary instance can listen.");
        }

        _logger = logger;
        _ = Task.Run(() => ServeAsync(_cts.Token));
    }

    /// <summary>Sends <paramref name="args"/> to the primary instance. Returns <c>false</c> on failure.</summary>
    public static bool TrySendToPrimary(string[] args, TimeSpan timeout)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            client.Connect(timeout);
            var payload = JsonSerializer.SerializeToUtf8Bytes(args);
            if (payload.Length > MaxMessageBytes)
            {
                return false;
            }

            client.Write(payload);
            client.Flush();
            return true;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Releases the mutex early (before restarting) so the new process can take it.</summary>
    public void Release()
    {
        _cts.Cancel();
        if (_ownsMutex)
        {
            _mutex.ReleaseMutex();
            _ownsMutex = false;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Release();
        _cts.Dispose();
        _mutex.Dispose();
    }

    private async Task ServeAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(ct).ConfigureAwait(false);

                using var buffer = new MemoryStream();
                var chunk = new byte[1024];
                int read;
                while ((read = await server.ReadAsync(chunk, ct).ConfigureAwait(false)) > 0)
                {
                    buffer.Write(chunk, 0, read);
                    if (buffer.Length > MaxMessageBytes)
                    {
                        throw new InvalidDataException("Forwarded arguments are too large.");
                    }
                }

                var args = JsonSerializer.Deserialize<string[]>(buffer.ToArray()) ?? [];
                ArgumentsReceived?.Invoke(this, args);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException)
            {
                _logger?.LogWarning(ex, "Ignored a malformed single-instance message");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Single-instance pipe server failed; retrying");
                await Task.Delay(TimeSpan.FromSeconds(1), ct).ConfigureAwait(false);
            }
        }
    }
}
