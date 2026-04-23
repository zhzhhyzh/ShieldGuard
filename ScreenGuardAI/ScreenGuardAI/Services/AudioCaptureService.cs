using System.IO;
using NAudio.Wave;

namespace ScreenGuardAI.Services;

/// <summary>
/// Captures system audio via WASAPI Loopback and implements Voice Activity Detection (VAD).
/// When silence is detected for a configurable duration, fires ParagraphCompleted with
/// the accumulated audio resampled to 16kHz mono float32 (Whisper's expected format).
/// </summary>
public class AudioCaptureService : IDisposable
{
    private WasapiLoopbackCapture? _capture;
    private readonly List<byte> _audioBuffer = new();
    private readonly object _bufferLock = new();

    private DateTime _lastSoundTime = DateTime.UtcNow;
    private bool _isCapturing;
    private bool _hasSpeechStarted;

    // Configuration
    public float SilenceThreshold { get; set; } = 0.01f; // RMS threshold for silence
    public int SilenceTimeoutMs { get; set; } = 1500;     // ms of silence before paragraph end
    public int MinAudioLengthMs { get; set; } = 500;      // minimum audio length to emit

    // Events
    public event Action<float[]>? ParagraphCompleted;
    public event Action<float>? VolumeChanged;
    public event Action<string>? StatusChanged;

    public bool IsCapturing => _isCapturing;

    public void Start()
    {
        if (_isCapturing) return;

        try
        {
            _capture = new WasapiLoopbackCapture();
            _capture.DataAvailable += OnDataAvailable;
            _capture.RecordingStopped += OnRecordingStopped;

            _audioBuffer.Clear();
            _hasSpeechStarted = false;
            _lastSoundTime = DateTime.UtcNow;
            _isCapturing = true;

            _capture.StartRecording();
            StatusChanged?.Invoke("LISTENING :: capturing system audio...");
        }
        catch (Exception ex)
        {
            _isCapturing = false;
            StatusChanged?.Invoke($"AUDIO ERROR: {ex.Message}");
        }
    }

    public void Stop()
    {
        if (!_isCapturing) return;
        _isCapturing = false;

        try
        {
            _capture?.StopRecording();
        }
        catch { }

        // Flush any remaining buffered audio
        FlushBuffer();

        StatusChanged?.Invoke("AUDIO OFF");
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (!_isCapturing || e.BytesRecorded == 0) return;

        var waveFormat = _capture!.WaveFormat;

        // Calculate RMS volume from the captured audio
        float rms = CalculateRms(e.Buffer, e.BytesRecorded, waveFormat);
        VolumeChanged?.Invoke(rms);

        bool isSpeech = rms > SilenceThreshold;

        if (isSpeech)
        {
            _lastSoundTime = DateTime.UtcNow;

            if (!_hasSpeechStarted)
            {
                _hasSpeechStarted = true;
                StatusChanged?.Invoke("LISTENING :: speech detected...");
            }

            // Accumulate audio bytes
            lock (_bufferLock)
            {
                _audioBuffer.AddRange(new ReadOnlySpan<byte>(e.Buffer, 0, e.BytesRecorded));
            }
        }
        else if (_hasSpeechStarted)
        {
            // Still accumulate a bit of trailing silence for natural endings
            lock (_bufferLock)
            {
                _audioBuffer.AddRange(new ReadOnlySpan<byte>(e.Buffer, 0, e.BytesRecorded));
            }

            // Check if silence timeout has been reached
            var silenceDuration = (DateTime.UtcNow - _lastSoundTime).TotalMilliseconds;
            if (silenceDuration >= SilenceTimeoutMs)
            {
                FlushBuffer();
            }
        }
    }

    /// <summary>
    /// Flush the accumulated audio buffer, resample to 16kHz mono float32, and fire ParagraphCompleted.
    /// </summary>
    private void FlushBuffer()
    {
        byte[] rawBytes;
        lock (_bufferLock)
        {
            if (_audioBuffer.Count == 0)
            {
                _hasSpeechStarted = false;
                return;
            }
            rawBytes = _audioBuffer.ToArray();
            _audioBuffer.Clear();
        }
        _hasSpeechStarted = false;

        if (_capture == null) return;

        var waveFormat = _capture.WaveFormat;

        // Check minimum audio length
        double durationMs = (double)rawBytes.Length / waveFormat.AverageBytesPerSecond * 1000;
        if (durationMs < MinAudioLengthMs) return;

        // Resample to 16kHz mono float32 on a background thread
        Task.Run(() =>
        {
            try
            {
                var samples = ResampleTo16kMono(rawBytes, waveFormat);
                if (samples.Length > 0)
                {
                    StatusChanged?.Invoke($"TRANSCRIBING :: {samples.Length / 16000.0:F1}s of audio...");
                    ParagraphCompleted?.Invoke(samples);
                }
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"RESAMPLE ERROR: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Resamples raw PCM bytes from the capture format to 16kHz mono float32.
    /// </summary>
    private static float[] ResampleTo16kMono(byte[] rawBytes, WaveFormat sourceFormat)
    {
        using var inputStream = new RawSourceWaveStream(
            new MemoryStream(rawBytes), sourceFormat);

        // Convert to IEEE float first
        var sampleProvider = inputStream.ToSampleProvider();

        // Convert to mono if stereo
        ISampleProvider monoProvider = sourceFormat.Channels > 1
            ? sampleProvider.ToMono()
            : sampleProvider;

        // Resample to 16kHz
        var resampled = new NAudio.Wave.SampleProviders.WdlResamplingSampleProvider(monoProvider, 16000);

        // Read all resampled samples
        var outputSamples = new List<float>();
        var buffer = new float[4096];
        int samplesRead;
        while ((samplesRead = resampled.Read(buffer, 0, buffer.Length)) > 0)
        {
            for (int i = 0; i < samplesRead; i++)
                outputSamples.Add(buffer[i]);
        }

        return outputSamples.ToArray();
    }

    /// <summary>
    /// Calculates RMS volume from raw PCM bytes, handling IEEE float and PCM16 formats.
    /// </summary>
    private static float CalculateRms(byte[] buffer, int bytesRecorded, WaveFormat format)
    {
        float sum = 0;
        int sampleCount = 0;

        if (format.Encoding == WaveFormatEncoding.IeeeFloat)
        {
            // 32-bit float samples
            for (int i = 0; i < bytesRecorded - 3; i += 4)
            {
                float sample = BitConverter.ToSingle(buffer, i);
                sum += sample * sample;
                sampleCount++;
            }
        }
        else if (format.BitsPerSample == 16)
        {
            // 16-bit PCM
            for (int i = 0; i < bytesRecorded - 1; i += 2)
            {
                short sample = BitConverter.ToInt16(buffer, i);
                float normalized = sample / 32768f;
                sum += normalized * normalized;
                sampleCount++;
            }
        }

        if (sampleCount == 0) return 0;
        return (float)Math.Sqrt(sum / sampleCount);
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception != null)
        {
            StatusChanged?.Invoke($"AUDIO ERROR: {e.Exception.Message}");
        }
    }

    public void Dispose()
    {
        Stop();
        _capture?.Dispose();
        _capture = null;
    }
}
