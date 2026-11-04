using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using DynamicIsland.Core.Notifications;

namespace DynamicIsland.App.Services;

/// <summary>Short independent WPF audio playback, serialized and closed after every play.</summary>
internal sealed class AlertAudioService : IAlertAudio, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();

    public async Task<bool> PlayAsync(AlertSound sound, CancellationToken cancellationToken = default)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted) return false;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdown.Token);
        var ct = cancellation.Token;
        var entered = false;
        MediaPlayer? player = null;
        try
        {
            await _gate.WaitAsync(ct);
            entered = true;
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Sounds", sound == AlertSound.Water ? "stargaze.mp3" : "illuminate.mp3");
            if (!File.Exists(path)) return false;
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            await dispatcher.InvokeAsync(() =>
            {
                player = new MediaPlayer { Volume = 1 };
                player.MediaEnded += (_, _) => completion.TrySetResult(true);
                player.MediaFailed += (_, _) => completion.TrySetResult(false);
                player.MediaOpened += (_, _) => player.Play();
                player.Open(new Uri(path, UriKind.Absolute));
            });
            return await completion.Task.WaitAsync(TimeSpan.FromSeconds(15), ct);
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex) when (ex is TimeoutException or IOException or InvalidOperationException or NotSupportedException or COMException)
        {
            return false;
        }
        finally
        {
            try
            {
                if (player is not null && !dispatcher.HasShutdownStarted)
                    await dispatcher.InvokeAsync(player.Close);
            }
            finally { if (entered) _gate.Release(); }
        }
    }

    public void Dispose() => _shutdown.Cancel();
}
