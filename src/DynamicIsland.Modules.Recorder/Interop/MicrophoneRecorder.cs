using System.Runtime.InteropServices;
using DynamicIsland.Core;
using DynamicIsland.Modules.Recorder.Services;
using NAudio.MediaFoundation;
using NAudio.Wave;

namespace DynamicIsland.Modules.Recorder.Interop;

/// <summary>
/// Records the default microphone to a temporary WAV (44.1 kHz, mono, 16-bit) with NAudio and, on
/// stop, keeps it as WAV or converts it to MP3 with Windows Media Foundation. Pausing keeps the
/// device open and simply stops writing, so resume is instant.
/// <para>
/// The raw audio is written to a private folder (<c>%LOCALAPPDATA%\DynamicIsland\recorder-temp</c>),
/// not to the recordings folder, which may sync to the cloud. A temp file left by a crash is
/// recovered into the recordings folder as a WAV on the next start.
/// </para>
/// </summary>
internal sealed class MicrophoneRecorder(string? tempFolder = null) : IDisposable
{
    private const string TempPrefix = ".recording-";
    private readonly string _tempFolder = tempFolder ?? Path.Combine(AppPaths.DataRoot, "recorder-temp");

    private WaveIn? _input;
    private WaveFileWriter? _writer;
    private string? _tempPath;
    private volatile bool _paused;

    /// <summary>Peak level (0–1) of each ~50 ms buffer (WaveIn uses window callbacks: the UI thread).</summary>
    public event EventHandler<float>? Level;

    /// <summary>Raised when the device stopped with an error (unplugged microphone).</summary>
    public event EventHandler<Exception>? Failed;

    /// <summary>Whether a recording is open.</summary>
    public bool IsRecording => _input is not null;

    /// <summary>
    /// Starts recording into a private temporary file. On failure nothing stays open, so the next
    /// attempt (for example after plugging a microphone in) starts cleanly.
    /// </summary>
    public void Start()
    {
        if (_input is not null)
        {
            return;
        }

        Directory.CreateDirectory(_tempFolder);
        var temp = Path.Combine(_tempFolder, $"{TempPrefix}{Guid.NewGuid():N}.wav");
        WaveIn? input = null;
        WaveFileWriter? writer = null;
        try
        {
            input = new WaveIn { WaveFormat = new WaveFormat(44100, 16, 1), BufferMilliseconds = 50 };
            writer = new WaveFileWriter(temp, input.WaveFormat);
            input.DataAvailable += OnData;
            input.RecordingStopped += (_, e) =>
            {
                if (e.Exception is not null)
                {
                    Failed?.Invoke(this, e.Exception);
                }
            };
            _paused = false;
            (_input, _writer, _tempPath) = (input, writer, temp);
            input.StartRecording();
        }
        catch
        {
            if (input is not null)
            {
                input.DataAvailable -= OnData;
                input.Dispose();
            }

            writer?.Dispose();
            (_input, _writer, _tempPath) = (null, null, null);
            TryDelete(temp);
            throw;
        }
    }

    /// <summary>Moves temp files left by a crash into <paramref name="folder"/> as WAV recordings.</summary>
    /// <returns>How many recordings were recovered.</returns>
    public int RecoverStale(string folder)
    {
        if (_input is not null || !Directory.Exists(_tempFolder))
        {
            return 0;
        }

        var recovered = 0;
        foreach (var file in new DirectoryInfo(_tempFolder).GetFiles(TempPrefix + "*.wav"))
        {
            try
            {
                if (file.Length <= 44)
                {
                    file.Delete();
                    continue;
                }

                Directory.CreateDirectory(folder);
                File.Move(file.FullName, RecordingLibrary.UniquePath(folder, RecordingLibrary.FileName(file.CreationTime, RecordingFormat.Wav)));
                recovered++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Still locked or the folder is unavailable: try again next start.
            }
        }

        return recovered;
    }

    /// <summary>Pauses or resumes writing.</summary>
    public void SetPaused(bool paused) => _paused = paused;

    /// <summary>Stops and saves to <paramref name="targetPath"/>. Returns the saved path (WAV if MP3 is unavailable).</summary>
    public async Task<string?> StopAsync(string targetPath, RecordingFormat format)
    {
        var input = _input;
        var writer = _writer;
        var temp = _tempPath;
        _input = null;
        _writer = null;
        _tempPath = null;
        if (input is null || writer is null || temp is null)
        {
            return null;
        }

        input.DataAvailable -= OnData;
        input.StopRecording();
        input.Dispose();
        await writer.DisposeAsync();

        return await Task.Run(() =>
        {
            var folder = Path.GetDirectoryName(targetPath)!;
            Directory.CreateDirectory(folder);
            if (format == RecordingFormat.Mp3 && Mp3Available())
            {
                try
                {
                    using (var reader = new WaveFileReader(temp))
                    {
                        MediaFoundationEncoder.EncodeToMp3(reader, targetPath, 128_000);
                    }

                    TryDelete(temp);
                    return targetPath;
                }
                catch (Exception ex) when (ex is COMException or InvalidOperationException or ArgumentException
                    or IOException or UnauthorizedAccessException or NAudio.MmException)
                {
                    // A half-written MP3 must not show up in the list; keep the WAV instead.
                    TryDelete(targetPath);
                }
            }

            // If this move fails, the temp file stays in the private folder and is recovered next start.
            var wavPath = RecordingLibrary.UniquePath(folder, Path.ChangeExtension(Path.GetFileName(targetPath), ".wav"));
            File.Move(temp, wavPath, overwrite: false);
            return wavPath;
        });
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left for RecoverStale.
        }
    }

    /// <summary>Whether Windows has an MP3 encoder (Media Foundation).</summary>
    public static bool Mp3Available()
    {
        try
        {
            MediaFoundationApi.Startup();
            return MediaFoundationEncoder.GetOutputMediaTypes(AudioSubtypes.MFAudioFormat_MP3).Length > 0;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or DllNotFoundException)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_input is not null)
        {
            _input.DataAvailable -= OnData;
            _input.StopRecording();
            _input.Dispose();
            _input = null;
        }

        _writer?.Dispose();
        _writer = null;
    }

    private void OnData(object? sender, WaveInEventArgs e)
    {
        float peak = 0;
        for (var i = 0; i + 1 < e.BytesRecorded; i += 2)
        {
            var sample = Math.Abs(BitConverter.ToInt16(e.Buffer, i) / 32768f);
            if (sample > peak)
            {
                peak = sample;
            }
        }

        if (!_paused)
        {
            _writer?.Write(e.Buffer, 0, e.BytesRecorded);
        }

        Level?.Invoke(this, _paused ? 0 : peak);
    }
}

/// <summary>Plays one recording at a time.</summary>
internal sealed class RecordingPlayer : IDisposable
{
    private WaveOut? _output;
    private AudioFileReader? _reader;

    /// <summary>Raised when the current recording ends by itself; the argument is its path.</summary>
    public event EventHandler<string>? Stopped;

    /// <summary>Path being played, or <c>null</c>.</summary>
    public string? Current { get; private set; }

    public void Play(string path)
    {
        Stop();
        _reader = new AudioFileReader(path);
        _output = new WaveOut();
        _output.Init(_reader);
        _output.PlaybackStopped += OnPlaybackStopped;
        Current = path;
        _output.Play();
    }

    public void Stop()
    {
        if (_output is not null)
        {
            // Detach first: a stopped event of the old output must not reset the next one.
            _output.PlaybackStopped -= OnPlaybackStopped;
        }

        _output?.Stop();
        _output?.Dispose();
        _reader?.Dispose();
        _output = null;
        _reader = null;
        Current = null;
    }

    public void Dispose() => Stop();

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        if (ReferenceEquals(sender, _output) && Current is { } path)
        {
            Stopped?.Invoke(this, path);
        }
    }
}
