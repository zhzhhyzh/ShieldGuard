using System.IO;
using Whisper.net;
using Whisper.net.Ggml;

namespace ScreenGuardAI.Services;

/// <summary>
/// Local speech-to-text service using Whisper.net (whisper.cpp).
/// Downloads the model on first use, then transcribes float32 PCM audio samples.
/// </summary>
public class TranscriptionService : IDisposable
{
    private WhisperProcessor? _processor;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private readonly SemaphoreSlim _transcribeLock = new(1, 1);
    private bool _isInitialized;
    private string _modelPath = string.Empty;

    /// <summary>
    /// Which Whisper model to use. "base" is recommended for real-time performance.
    /// </summary>
    public string ModelSize { get; set; } = "base";

    /// <summary>
    /// Language hint for Whisper. "en" for English, "auto" for auto-detect.
    /// </summary>
    public string Language { get; set; } = "en";

    public bool IsInitialized => _isInitialized;

    // Events
    public event Action<string>? StatusChanged;
    public event Action<int>? DownloadProgressChanged;

    /// <summary>
    /// Gets the models directory path under %APPDATA%/ScreenGuardAI/models/.
    /// </summary>
    private static string GetModelsDirectory()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ScreenGuardAI", "models");
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// Maps string model size to GgmlType enum.
    /// </summary>
    private GgmlType GetGgmlType() => ModelSize.ToLowerInvariant() switch
    {
        "tiny" => GgmlType.Tiny,
        "base" => GgmlType.Base,
        "small" => GgmlType.Small,
        "medium" => GgmlType.Medium,
        "large" => GgmlType.LargeV3,
        _ => GgmlType.Base
    };

    /// <summary>
    /// Initialize the Whisper processor. Downloads the model if not present.
    /// Call this before transcribing.
    /// </summary>
    public async Task InitializeAsync()
    {
        if (_isInitialized) return;

        await _initLock.WaitAsync();
        try
        {
            if (_isInitialized) return;

            var ggmlType = GetGgmlType();
            var modelFileName = $"ggml-{ModelSize.ToLowerInvariant()}.bin";
            _modelPath = Path.Combine(GetModelsDirectory(), modelFileName);

            // Download model if not present
            if (!File.Exists(_modelPath))
            {
                StatusChanged?.Invoke($"Downloading Whisper {ModelSize} model...");
                await DownloadModelAsync(ggmlType, _modelPath);
                StatusChanged?.Invoke($"Model downloaded: {modelFileName}");
            }

            // Create the Whisper processor
            StatusChanged?.Invoke("Loading Whisper model...");
            var factory = WhisperFactory.FromPath(_modelPath);
            _processor = factory.CreateBuilder()
                .WithLanguage(Language)
                .WithThreads(Math.Max(1, Environment.ProcessorCount / 2))
                .Build();

            _isInitialized = true;
            StatusChanged?.Invoke("Whisper model ready");
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke($"Whisper init failed: {ex.Message}");
            throw;
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <summary>
    /// Transcribes 16kHz mono float32 PCM samples to text.
    /// </summary>
    public async Task<string> TranscribeAsync(float[] samples)
    {
        if (!_isInitialized || _processor == null)
            throw new InvalidOperationException("TranscriptionService not initialized. Call InitializeAsync first.");

        await _transcribeLock.WaitAsync();
        try
        {
            // Whisper.net expects a WAV-like stream. We'll write a minimal WAV header + data.
            using var ms = new MemoryStream();
            WriteWavHeader(ms, samples.Length, 16000, 1);
            // Write float samples as 16-bit PCM
            foreach (var sample in samples)
            {
                var clamped = Math.Clamp(sample, -1f, 1f);
                short pcm16 = (short)(clamped * 32767);
                ms.Write(BitConverter.GetBytes(pcm16));
            }
            // Update WAV header sizes
            UpdateWavHeader(ms);
            ms.Position = 0;

            var segments = new List<string>();
            await foreach (var result in _processor.ProcessAsync(ms))
            {
                var text = result.Text?.Trim();
                if (!string.IsNullOrWhiteSpace(text))
                    segments.Add(text);
            }

            return string.Join(" ", segments).Trim();
        }
        finally
        {
            _transcribeLock.Release();
        }
    }

    /// <summary>
    /// Downloads the Whisper GGML model from Hugging Face.
    /// </summary>
    private async Task DownloadModelAsync(GgmlType ggmlType, string targetPath)
    {
        var tempPath = targetPath + ".downloading";
        try
        {
            using var modelStream = await WhisperGgmlDownloader.Default.GetGgmlModelAsync(ggmlType);
            using var fileStream = File.Create(tempPath);

            var buffer = new byte[81920];
            long totalRead = 0;
            int bytesRead;
            while ((bytesRead = await modelStream.ReadAsync(buffer)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                totalRead += bytesRead;
                // Report progress in MB
                DownloadProgressChanged?.Invoke((int)(totalRead / (1024 * 1024)));
            }

            fileStream.Close();
            File.Move(tempPath, targetPath, overwrite: true);
        }
        catch
        {
            // Clean up partial download
            try { File.Delete(tempPath); } catch { }
            throw;
        }
    }

    /// <summary>
    /// Writes a minimal WAV file header for 16-bit PCM mono 16kHz.
    /// </summary>
    private static void WriteWavHeader(Stream stream, int sampleCount, int sampleRate, int channels)
    {
        var writer = new BinaryWriter(stream);
        int bitsPerSample = 16;
        int byteRate = sampleRate * channels * bitsPerSample / 8;
        int blockAlign = channels * bitsPerSample / 8;
        int dataSize = sampleCount * blockAlign;

        writer.Write("RIFF"u8);
        writer.Write(36 + dataSize);    // ChunkSize
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);               // Subchunk1Size (PCM)
        writer.Write((short)1);         // AudioFormat (PCM)
        writer.Write((short)channels);
        writer.Write(sampleRate);
        writer.Write(byteRate);
        writer.Write((short)blockAlign);
        writer.Write((short)bitsPerSample);
        writer.Write("data"u8);
        writer.Write(dataSize);
    }

    /// <summary>
    /// Updates the RIFF and data chunk sizes in the WAV header after writing all data.
    /// </summary>
    private static void UpdateWavHeader(MemoryStream ms)
    {
        long totalLength = ms.Length;
        ms.Position = 4;
        ms.Write(BitConverter.GetBytes((int)(totalLength - 8)));  // RIFF chunk size
        ms.Position = 40;
        ms.Write(BitConverter.GetBytes((int)(totalLength - 44))); // data chunk size
    }

    public void Dispose()
    {
        _processor?.Dispose();
        _processor = null;
        _isInitialized = false;
    }
}
