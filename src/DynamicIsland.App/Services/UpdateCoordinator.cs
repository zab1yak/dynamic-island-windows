using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace DynamicIsland.App.Services;

internal enum UpdateState { Idle, Checking, Latest, Available, Downloading, Ready, Error, Unsupported, Cancelled }
internal enum UpdateStage { Checking, Downloading, Preparing }
internal enum UpdateFailureReason { Network, Timeout, InvalidFeed, Integrity, Unavailable }
internal sealed record UpdateFailure(UpdateStage Stage, UpdateFailureReason Reason);
internal sealed record UpdateSnapshot(UpdateState State, string InstalledVersion, string? AvailableVersion,
    string ReleaseUrl, int? ProgressPercent, DateTimeOffset? CheckedAt, UpdateFailure? Failure);

internal interface IApplicationUpdateController
{
    UpdateSnapshot Current { get; }
    event EventHandler? Changed;
    bool IsInstalled { get; }
    bool IsBusy { get; }
    Task CheckAsync(bool notify = false, CancellationToken cancellationToken = default);
    Task DownloadAsync(CancellationToken cancellationToken = default);
    void Cancel();
}

internal sealed record UpdateCandidate(string Version, string ReleaseUrl, object? Handle = null);
internal interface IUpdateBackend
{
    bool IsInstalled { get; }
    string InstalledVersion { get; }
    // Velopack's check has no cancellation API. Retain single-flight ownership until it completes.
    Task<UpdateCandidate?> CheckAsync();
    Task DownloadAsync(UpdateCandidate candidate, Action<int> progress, CancellationToken cancellationToken);
    void PrepareForExit(UpdateCandidate candidate);
}

internal sealed class UpdateBackendException(UpdateFailureReason reason, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public UpdateFailureReason Reason { get; } = reason;
}

/// <summary>One state machine for manual checks, background checks and verified pending updates.</summary>
internal sealed class UpdateCoordinator : IApplicationUpdateController, IDisposable
{
    internal const string ReleasesUrl = "https://github.com/zab1yak/dynamic-island-windows/releases/latest";
    private readonly IUpdateBackend _backend;
    private readonly TimeProvider _time;
    private readonly Action<UpdateSnapshot>? _notifyReady;
    private readonly Action<Exception, UpdateStage>? _logFailure;
    private readonly object _sync = new();
    private UpdateSnapshot _current;
    private UpdateCandidate? _candidate;
    private UpdateCandidate? _pending;
    private CancellationTokenSource? _operation;
    private int _active;
    private bool _prepared;
    private bool _disposed;

    public UpdateCoordinator(IUpdateBackend backend, TimeProvider time, Action<UpdateSnapshot>? notifyReady = null,
        Action<Exception, UpdateStage>? logFailure = null)
    {
        _backend = backend;
        _time = time;
        _notifyReady = notifyReady;
        _logFailure = logFailure;
        _current = new(backend.IsInstalled ? UpdateState.Idle : UpdateState.Unsupported, backend.InstalledVersion,
            null, ReleasesUrl, null, null, null);
    }

    public UpdateSnapshot Current => Volatile.Read(ref _current);
    public bool IsInstalled => _backend.IsInstalled;
    public bool IsBusy => Volatile.Read(ref _active) != 0;
    public event EventHandler? Changed;

    public Task CheckAsync(bool notify = false, CancellationToken cancellationToken = default) =>
        RunAsync(check: true, notify, cancellationToken);
    public Task DownloadAsync(CancellationToken cancellationToken = default) =>
        RunAsync(check: false, notify: false, cancellationToken);

    private async Task RunAsync(bool check, bool notify, CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref _active, 1, 0) != 0) return;
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var stage = check ? UpdateStage.Checking : UpdateStage.Downloading;
        try
        {
            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                _operation = operation;
            }
            operation.Token.ThrowIfCancellationRequested();
            if (!IsInstalled) { Set(Current with { State = UpdateState.Unsupported, ProgressPercent = null, Failure = null }); return; }
            if (_pending is not null) { Set(Current with { State = UpdateState.Ready, ProgressPercent = 100, Failure = null }); return; }
            if (check)
            {
                Set(Current with { State = UpdateState.Checking, ProgressPercent = null, Failure = null });
                var candidate = await _backend.CheckAsync().ConfigureAwait(false);
                operation.Token.ThrowIfCancellationRequested();
                _candidate = candidate;
                Set(Current with
                {
                    State = candidate is null ? UpdateState.Latest : UpdateState.Available,
                    AvailableVersion = candidate?.Version,
                    ReleaseUrl = candidate?.ReleaseUrl ?? ReleasesUrl,
                    CheckedAt = _time.GetUtcNow(),
                    ProgressPercent = null,
                    Failure = null,
                });
                if (!notify || candidate is null) return;
            }
            if (_candidate is null) return;
            stage = UpdateStage.Downloading;
            Set(Current with { State = UpdateState.Downloading, ProgressPercent = null, Failure = null });
            await _backend.DownloadAsync(_candidate, percent => ReportProgress(operation, percent), operation.Token).ConfigureAwait(false);
            operation.Token.ThrowIfCancellationRequested();
            _pending = _candidate;
            Set(Current with { State = UpdateState.Ready, ProgressPercent = 100, Failure = null });
            if (notify) _notifyReady?.Invoke(Current);
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested)
        {
            Set(Current with { State = UpdateState.Cancelled, ProgressPercent = null, Failure = null });
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _logFailure?.Invoke(exception, stage);
            Set(Current with { State = UpdateState.Error, ProgressPercent = null, Failure = new(stage, Classify(exception, stage)) });
        }
        finally
        {
            lock (_sync) { if (ReferenceEquals(_operation, operation)) _operation = null; }
            Interlocked.Exchange(ref _active, 0);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Cancel()
    {
        lock (_sync) { _operation?.Cancel(); }
    }

    public bool PrepareForExit()
    {
        Exception failure;
        lock (_sync)
        {
            if (_prepared || _pending is null) return true;
            try { _backend.PrepareForExit(_pending); _prepared = true; return true; }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                failure = exception;
                Volatile.Write(ref _current, _current with
                {
                    State = UpdateState.Error,
                    Failure = new(UpdateStage.Preparing, Classify(exception, UpdateStage.Preparing)),
                });
            }
        }
        _logFailure?.Invoke(failure, UpdateStage.Preparing);
        Changed?.Invoke(this, EventArgs.Empty);
        return false;
    }

    private void ReportProgress(CancellationTokenSource operation, int percent)
    {
        lock (_sync)
        {
            // The generation/state check and write share the same lock as terminal
            // transitions: a callback cannot resurrect Downloading after Ready/Error.
            if (!ReferenceEquals(_operation, operation) || operation.IsCancellationRequested || _current.State != UpdateState.Downloading) return;
            Volatile.Write(ref _current, _current with { ProgressPercent = percent is >= 0 and <= 100 ? percent : null });
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Set(UpdateSnapshot snapshot)
    {
        lock (_sync) { Volatile.Write(ref _current, snapshot); }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static UpdateFailureReason Classify(Exception exception, UpdateStage stage) => exception switch
    {
        UpdateBackendException known => known.Reason,
        HttpRequestException => UpdateFailureReason.Network,
        TimeoutException or OperationCanceledException => UpdateFailureReason.Timeout,
        CryptographicException => UpdateFailureReason.Integrity,
        JsonException or InvalidDataException => stage == UpdateStage.Downloading ? UpdateFailureReason.Integrity : UpdateFailureReason.InvalidFeed,
        _ => UpdateFailureReason.Unavailable,
    };

    public void Dispose()
    {
        lock (_sync) { _disposed = true; _operation?.Cancel(); }
        GC.SuppressFinalize(this);
    }
}
