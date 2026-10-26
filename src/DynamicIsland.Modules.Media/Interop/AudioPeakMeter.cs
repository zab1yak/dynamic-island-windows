using System.Runtime.InteropServices;
using DynamicIsland.Modules.Media.Services;
using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;

namespace DynamicIsland.Modules.Media.Interop;

/// <summary>
/// Reads the real output level of the default playback device (WASAPI <c>IAudioMeterInformation</c>).
/// No audio is captured or recorded; only the peak meter Windows already maintains is read.
/// </summary>
internal sealed class AudioPeakMeter(ILogger<AudioPeakMeter> logger) : IAudioLevelSource, IDisposable
{
    private MMDeviceEnumerator? _enumerator;
    private MMDevice? _device;
    private DateTime _nextRetry = DateTime.MinValue;

    /// <inheritdoc />
    public float ReadPeak()
    {
        try
        {
            if (_device is null)
            {
                if (DateTime.UtcNow < _nextRetry)
                {
                    return 0;
                }

                _enumerator ??= new MMDeviceEnumerator();
                _device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            }

            return Math.Clamp(_device.AudioMeterInformation.MasterPeakValue, 0f, 1f);
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            // The default device changed or disappeared: drop it and try again shortly.
            logger.LogDebug(ex, "Audio meter unavailable");
            Release();
            _nextRetry = DateTime.UtcNow.AddSeconds(2);
            return 0;
        }
    }

    /// <inheritdoc />
    public void Reset() => Release();

    /// <inheritdoc />
    public void Dispose()
    {
        Release();
        _enumerator?.Dispose();
        _enumerator = null;
    }

    private void Release()
    {
        _device?.Dispose();
        _device = null;
    }
}
