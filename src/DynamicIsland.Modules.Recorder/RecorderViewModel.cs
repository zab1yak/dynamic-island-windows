using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Resources;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DynamicIsland.Core.Island;
using DynamicIsland.Core.Modules;
using DynamicIsland.Core.Storage;
using DynamicIsland.Core.Threading;
using DynamicIsland.Modules.Recorder.Interop;
using DynamicIsland.Modules.Recorder.Services;
using DynamicIsland.UI.Localization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DynamicIsland.Modules.Recorder;

/// <summary>Recorder state.</summary>
public enum RecorderState
{
    Idle,
    Recording,
    Paused,
}

/// <summary>A row in the recordings list.</summary>
public sealed partial class RecordingItem(RecordingFile file) : ObservableObject
{
    [ObservableProperty]
    private bool _isPlaying;

    public RecordingFile File { get; } = file;

    public string Name => File.Name;

    public string Details => File.Path.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) ? "MP3" : "WAV";
}

/// <summary>
/// Voice recorder page. While recording, the live pill shows a red dot and a timer; the expanded page
/// shows waveform bars driven by the real microphone level. The microphone indicator lights up because
/// the island itself opens a capture stream (Core Audio), exactly as for any other app.
/// </summary>
public sealed partial class RecorderViewModel : ObservableObject, ILiveActivitySource, IDisposable
{
    /// <summary>File name of the settings.</summary>
    public const string SettingsFile = "recorder.json";

    private readonly MicrophoneRecorder _recorder = new();
    private readonly RecordingPlayer _player = new();
    private readonly WaveformHistory _waveform = new();
    private readonly IStorageService _storage;
    private readonly TimeProvider _time;
    private readonly IUiDispatcher _dispatcher;
    private readonly IslandStateMachine _island;
    private readonly ILogger<RecorderViewModel> _logger;
    private ITimer? _clock;
    private DateTimeOffset _segmentStart;
    private TimeSpan _accumulated;
    private DateTimeOffset _startedAt;

    [ObservableProperty]
    private RecorderState _state;

    [ObservableProperty]
    private string _elapsedText = "0:00";

    [ObservableProperty]
    private IReadOnlyList<double> _levels = [];

    [ObservableProperty]
    private string? _notice;

    public RecorderViewModel(IStorageService storage, TimeProvider time, IUiDispatcher dispatcher, IslandStateMachine island, ILogger<RecorderViewModel> logger)
    {
        _storage = storage;
        _time = time;
        _dispatcher = dispatcher;
        _island = island;
        _logger = logger;
        Levels = [.. _waveform.Levels];
        Live = new RecorderLiveViewModel(this);
        _recorder.Level += (_, peak) => _dispatcher.Post(() => OnLevel(peak));
        _recorder.Failed += (_, ex) => _dispatcher.Post(() => OnFailed(ex));
        _player.Stopped += (_, path) => _dispatcher.Post(() =>
        {
            // Release the file so it can be deleted, but only if nothing else started meanwhile.
            if (_player.Current == path)
            {
                _player.Stop();
            }

            foreach (var item in Recordings)
            {
                item.IsPlaying = item.File.Path == _player.Current;
            }
        });
    }

    /// <inheritdoc />
    public event EventHandler? Changed;

    public RecorderLiveViewModel Live { get; }

    public RecorderSettings Settings { get; private set; } = new();

    public ObservableCollection<RecordingItem> Recordings { get; } = [];

    public bool IsBusy => State != RecorderState.Idle;

    /// <summary>Whether the list has anything to show.</summary>
    public bool HasRecordings => Recordings.Count > 0;

    /// <inheritdoc />
    public LiveActivity? Current => State == RecorderState.Idle
        ? null
        : new LiveActivity("recorder", DynamicIsland.Core.Modules.ActivityKind.Transfer, 95, Live, _startedAt);

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        Settings = await _storage.LoadAsync<RecorderSettings>(SettingsFile, cancellationToken) ?? new RecorderSettings();
        if (_recorder.RecoverStale(Settings.EffectiveFolder) is > 0 and var recovered)
        {
            _logger.LogInformation("Recovered {Count} unfinished recording(s)", recovered);
        }

        ReloadList();
    }

    public Task SaveSettingsAsync() => _storage.SaveAsync(SettingsFile, Settings);

    /// <summary>Stops a running recording and saves it (application exit).</summary>
    public async Task FinishAsync()
    {
        if (State != RecorderState.Idle)
        {
            await StopAsync();
        }
    }

    public void Dispose()
    {
        _clock?.Dispose();
        _recorder.Dispose();
        _player.Dispose();
    }

    [RelayCommand]
    private void Record()
    {
        if (State != RecorderState.Idle)
        {
            return;
        }

        try
        {
            _player.Stop();
            _recorder.Start();
        }
        catch (Exception ex) when (ex is NAudio.MmException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "Recording could not start");
            ShowNotice(Loc.Instance["Recorder.NoMicrophone"]);
            return;
        }

        _startedAt = _time.GetUtcNow();
        _segmentStart = _startedAt;
        _accumulated = TimeSpan.Zero;
        State = RecorderState.Recording;
        _clock = _time.CreateTimer(_ => _dispatcher.Post(UpdateElapsed), null, TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(250));
        UpdateElapsed();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void PauseResume()
    {
        if (State == RecorderState.Recording)
        {
            _accumulated += _time.GetUtcNow() - _segmentStart;
            _recorder.SetPaused(true);
            State = RecorderState.Paused;
        }
        else if (State == RecorderState.Paused)
        {
            _segmentStart = _time.GetUtcNow();
            _recorder.SetPaused(false);
            State = RecorderState.Recording;
        }

        UpdateElapsed();
    }

    [RelayCommand]
    private async Task StopAsync()
    {
        if (State == RecorderState.Idle)
        {
            return;
        }

        _clock?.Dispose();
        _clock = null;
        State = RecorderState.Idle;
        _waveform.Reset();
        Levels = [.. _waveform.Levels];
        Changed?.Invoke(this, EventArgs.Empty);

        var folder = Settings.EffectiveFolder;
        var target = RecordingLibrary.UniquePath(folder, RecordingLibrary.FileName(_time.GetLocalNow().DateTime, Settings.Format));
        try
        {
            var saved = await _recorder.StopAsync(target, Settings.Format);
            if (saved is not null && Settings.Format == RecordingFormat.Mp3 && saved.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
            {
                ShowNotice(Loc.Instance["Recorder.Mp3Unavailable"]);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Saving the recording failed");
            ShowNotice(Loc.Instance["Recorder.SaveFailed"]);
        }

        ReloadList();
    }

    [RelayCommand]
    private void Play(RecordingItem? item)
    {
        if (item is null || State != RecorderState.Idle)
        {
            return;
        }

        if (item.IsPlaying)
        {
            _player.Stop();
            item.IsPlaying = false;
            return;
        }

        try
        {
            _player.Play(item.File.Path);
            foreach (var other in Recordings)
            {
                other.IsPlaying = ReferenceEquals(other, item);
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or NAudio.MmException or System.Runtime.InteropServices.COMException)
        {
            _logger.LogWarning(ex, "Playback failed");
        }
    }

    [RelayCommand]
    private void Delete(RecordingItem? item)
    {
        if (item is null)
        {
            return;
        }

        if (item.IsPlaying || _player.Current == item.File.Path)
        {
            _player.Stop();
        }

        if (!RecordingLibrary.Delete(Settings.EffectiveFolder, item.File.Path))
        {
            ShowNotice(Loc.Instance["Recorder.DeleteFailed"]);
        }

        ReloadList();
    }

    [RelayCommand]
    private void OpenFolder()
    {
        try
        {
            Directory.CreateDirectory(Settings.EffectiveFolder);
            var explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
            using var _ = Process.Start(new ProcessStartInfo(explorer) { UseShellExecute = false, ArgumentList = { Settings.EffectiveFolder } });
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Opening the recordings folder failed");
        }
    }

    private void OnLevel(float peak)
    {
        if (State == RecorderState.Idle)
        {
            return;
        }

        _waveform.Push(peak);
        // Bars are drawn only while someone can see them.
        if (_island.Mode != IslandMode.Hidden)
        {
            Levels = [.. _waveform.Levels];
        }
    }

    private void OnFailed(Exception ex)
    {
        _logger.LogWarning(ex, "Microphone stopped during recording");
        ShowNotice(Loc.Instance["Recorder.DeviceLost"]);
        _ = StopAsync();
    }

    private void UpdateElapsed()
    {
        var elapsed = _accumulated + (State == RecorderState.Recording ? _time.GetUtcNow() - _segmentStart : TimeSpan.Zero);
        ElapsedText = RecordingLibrary.FormatElapsed(elapsed);
    }

    private void ReloadList()
    {
        Recordings.Clear();
        foreach (var file in RecordingLibrary.List(Settings.EffectiveFolder))
        {
            Recordings.Add(new RecordingItem(file));
        }

        OnPropertyChanged(nameof(HasRecordings));
    }

    private void ShowNotice(string message)
    {
        Notice = message;
        _time.CreateTimer(_ => _dispatcher.Post(() =>
        {
            if (Notice == message)
            {
                Notice = null;
            }
        }), null, TimeSpan.FromSeconds(5), Timeout.InfiniteTimeSpan);
    }

    partial void OnStateChanged(RecorderState value) => OnPropertyChanged(nameof(IsBusy));
}

/// <summary>Live pill while recording: red dot and timer.</summary>
public sealed class RecorderLiveViewModel(RecorderViewModel recorder)
{
    public RecorderViewModel Recorder { get; } = recorder;
}

/// <summary>Voice recorder module.</summary>
public sealed class RecorderModule(RecorderViewModel viewModel, RecorderSettingsViewModel settings) : IIslandModule
{
    private static readonly ResourceManager Strings = new("DynamicIsland.Modules.Recorder.Resources.Strings", typeof(RecorderModule).Assembly);

    public string Id => "recorder";

    public string TitleKey => "Recorder.Title";

    public string IconKey => "Mic";

    public int Order => 5;

    public object ExpandedViewModel => viewModel;

    public object? SettingsViewModel
    {
        get
        {
            settings.Reload();
            return settings;
        }
    }

    public ILiveActivitySource? LiveActivities => viewModel;

    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        Loc.Instance.Register(Strings);
        return viewModel.LoadAsync(cancellationToken);
    }

    public async Task ShutdownAsync()
    {
        await viewModel.FinishAsync();
        viewModel.Dispose();
    }
}

/// <summary>DI registration.</summary>
public static class RecorderRegistration
{
    public static IServiceCollection AddRecorderModule(this IServiceCollection services)
    {
        services.AddSingleton<RecorderViewModel>();
        services.AddSingleton<RecorderSettingsViewModel>();
        services.AddSingleton<RecorderModule>();
        services.AddSingleton<IIslandModule>(sp => sp.GetRequiredService<RecorderModule>());
        services.AddSingleton(new ModuleResources(new Uri("pack://application:,,,/DynamicIsland.Modules.Recorder;component/Views/RecorderTemplates.xaml")));
        return services;
    }
}
