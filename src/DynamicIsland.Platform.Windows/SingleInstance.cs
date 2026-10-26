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
/// <remarks>
/// A mutex can only be released by the thread that acquired it, so create and dispose this object
/// on the same thread (the <c>Main</c> thread).
/// </remarks>
public sealed class SingleInstance : IDisposable
{
    private const int MaxMessageBytes = 8 * 1024;
    private static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(2);
    private static readonly string Suffix = WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
    private static readonly string MutexName = $"Local\\DynamicIsland-7C1E0A52-{Suffix}";
    private static readonly string PipeName = $"DynamicIsland-{Suffix}";

    private readonly Mutex _mutex;
    private readonly CancellationTokenSource _cts = new();
    private readonly Lock _gate = new();
    private readonly List<string[]> _pending = [];
    private Action<string[]>? _handler;
    private Task? _server;
    private ILogger? _logger;
    private bool _ownsMutex;

    private SingleInstance(Mutex mutex, bool ownsMutex)
    {
        _mutex = mutex;
        _ownsMutex = ownsMutex;
    }

    /// <summary>Whether this process is the primary instance.</summary>
    public bool IsPrimary => _ownsMutex;

    /// <summary>Tries to become the primary instance, waiting up to <paramref name="wait"/> for the mutex.</summary>
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

    /// <summary>
    /// Starts listening for forwarded arguments right away. Messages that arrive before
    /// <see cref="SetHandler"/> is called are queued, so nothing is lost during start-up.
    /// </summary>
    public void Listen(ILogger logger)
    {
        if (!_ownsMutex)
        {
            throw new InvalidOperationException("Only the primary instance can listen.");
        }

        _logger = logger;
        _server = Task.Run(() => ServeAsync(_cts.Token));
    }

    /// <summary>Sets the handler (invoked on a thread-pool thread) and flushes queued messages to it.</summary>
    public void SetHandler(Action<string[]> handler)
    {
        string[][] queued;
        lock (_gate)
        {
            _handler = handler;
            queued = [.. _pending];
            _pending.Clear();
        }

        foreach (var args in queued)
        {
            handler(args);
        }
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

    /// <inheritdoc />
    public void Dispose()
    {
        _cts.Cancel();
        try
        {
            _server?.Wait(TimeSpan.FromSeconds(1));
        }
        catch (AggregateException)
        {
            // The server loop logs its own failures.
        }

        if (_ownsMutex)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException ex)
            {
                _logger?.LogWarning(ex, "Mutex released on a non-owner thread; the OS frees it at process exit");
            }

            _ownsMutex = false;
        }

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

                // A client that connects and stays silent must not block later launches.
                using var perConnection = CancellationTokenSource.CreateLinkedTokenSource(ct);
                perConnection.CancelAfter(ConnectionTimeout);
                var args = await ReadMessageAsync(server, perConnection.Token).ConfigureAwait(false);
                Dispatch(args);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException or OperationCanceledException)
            {
                _logger?.LogWarning(ex, "Ignored a malformed or stalled single-instance message");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Single-instance pipe server failed; retrying");
                await Task.Delay(TimeSpan.FromSeconds(1), ct).ConfigureAwait(false);
            }
        }
    }

    private static async Task<string[]> ReadMessageAsync(Stream stream, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[1024];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct).ConfigureAwait(false)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > MaxMessageBytes)
            {
                throw new InvalidDataException("Forwarded arguments are too large.");
            }
        }

        return JsonSerializer.Deserialize<string[]>(buffer.ToArray()) ?? [];
    }

    private void Dispatch(string[] args)
    {
        Action<string[]>? handler;
        lock (_gate)
        {
            handler = _handler;
            if (handler is null)
            {
                _pending.Add(args);
                return;
            }
        }

        handler(args);
    }
}
