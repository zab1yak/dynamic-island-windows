using System.Runtime.InteropServices;
using DynamicIsland.Modules.Recorder.Services;
using NAudio.MediaFoundation;
using NAudio.Wave;

namespace DynamicIsland.Modules.Recorder.Interop;

/// <summary>
/// Records the default microphone to a temporary WAV (44.1 kHz, mono, 16-bit) with NAudio and, on
/// stop, keeps it as WAV or converts it to MP3 with Windows Media Foundation. Pausing keeps the
/// device open and simply stops writing, so resume is instant.
/// </summary>
internal sealed class MicrophoneRecorder : IDisposable
{
    private WaveIn? _input;
    private WaveFileWriter? _writer;
    private string? _tempPath;
    private volatile bool _paused;

    /// <summary>Peak level (0–1) of each ~50 ms buffer, raised on the audio thread.</summary>
    public event EventHandler<float>? Level;

    /// <summary>Raised on the audio thread when the device stopped with an error (unplugged microphone).</summary>
    public event EventHandler<Exception>? Failed;

    /// <summary>Whether a recording is open.</summary>
    public bool IsRecording => _input is not null;

    /// <summary>Starts recording into a temporary file in <paramref name="folder"/>.</summary>
    public void Start(string folder)
    {
        if (_input is not null)
        {
            return;
        }

        Directory.CreateDirectory(folder);
        _tempPath = Path.Combine(folder, $".recording-{Guid.NewGuid():N}.wav");
        _input = new WaveIn { WaveFormat = new WaveFormat(44100, 16, 1), BufferMilliseconds = 50 };
        _writer = new WaveFileWriter(_tempPath, _input.WaveFormat);
        _input.DataAvailable += OnData;
        _input.RecordingStopped += (_, e) =>
        {
            if (e.Exception is not null)
            {
                Failed?.Invoke(this, e.Exception);
            }
        };
        _paused = false;
        _input.StartRecording();
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
            if (format == RecordingFormat.Mp3 && Mp3Available())
            {
                try
                {
                    using (var reader = new WaveFileReader(temp))
                    {
                        MediaFoundationEncoder.EncodeToMp3(reader, targetPath, 128_000);
                    }

                    File.Delete(temp);
                    return targetPath;
                }
                catch (Exception ex) when (ex is COMException or InvalidOperationException or ArgumentException)
                {
                    // Fall back to WAV below.
                }
            }

            var wavPath = Path.ChangeExtension(targetPath, ".wav");
            File.Move(temp, wavPath, overwrite: false);
            return wavPath;
        });
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

    /// <summary>Raised when playback ends (on a worker thread).</summary>
    public event EventHandler? Stopped;

    /// <summary>Path being played, or <c>null</c>.</summary>
    public string? Current { get; private set; }

    public void Play(string path)
    {
        Stop();
        _reader = new AudioFileReader(path);
        _output = new WaveOut();
        _output.Init(_reader);
        _output.PlaybackStopped += (_, _) => Stopped?.Invoke(this, EventArgs.Empty);
        Current = path;
        _output.Play();
    }

    public void Stop()
    {
        _output?.Stop();
        _output?.Dispose();
        _reader?.Dispose();
        _output = null;
        _reader = null;
        Current = null;
    }

    public void Dispose() => Stop();
}
