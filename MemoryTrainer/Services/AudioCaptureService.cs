using NAudio.Wave;

namespace MemoryTrainer.Services;

public class AudioCaptureService : IDisposable
{
    private const int ClipBeforeSeconds = 15;
    private const int ClipAfterSeconds = 15;
    private const double SilenceThreshold = 0.001;

    private WasapiLoopbackCapture? _capture;
    private WaveFormat? _format;
    private readonly object _bufferLock = new();
    private readonly List<byte[]> _chunks = new();
    private long _totalBytes;
    private long _maxBytes;
    private bool _running;

    public void StartBuffer()
    {
        if (_running) return;
        try
        {
            _capture = new WasapiLoopbackCapture();
            _format = _capture.WaveFormat;
            _maxBytes = (long)(_format.AverageBytesPerSecond * (ClipBeforeSeconds + ClipAfterSeconds));
            _capture.DataAvailable += OnData;
            _capture.StartRecording();
            _running = true;
            System.Diagnostics.Debug.WriteLine("[AudioCaptureService] WASAPI loopback buffer started");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AudioCaptureService] Failed to start loopback: {ex.Message}");
            _capture?.Dispose();
            _capture = null;
            _running = false;
        }
    }

    public void StopBuffer()
    {
        if (!_running) return;
        try
        {
            _capture?.StopRecording();
            _capture?.Dispose();
            _capture = null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AudioCaptureService] Error stopping buffer: {ex.Message}");
        }
        _running = false;
        lock (_bufferLock) { _chunks.Clear(); _totalBytes = 0; }
    }

    private void OnData(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded == 0) return;
        var chunk = new byte[e.BytesRecorded];
        Buffer.BlockCopy(e.Buffer, 0, chunk, 0, e.BytesRecorded);
        lock (_bufferLock)
        {
            _chunks.Add(chunk);
            _totalBytes += chunk.Length;
            while (_totalBytes > _maxBytes && _chunks.Count > 0)
            {
                _totalBytes -= _chunks[0].Length;
                _chunks.RemoveAt(0);
            }
        }
    }

    public async Task<string?> CaptureAsync(string outputPath)
    {
        if (!_running || _format == null)
        {
            System.Diagnostics.Debug.WriteLine("[AudioCaptureService] Buffer not running, trying mic fallback");
            return await TryMicFallbackAsync(outputPath);
        }

        await Task.Delay(TimeSpan.FromSeconds(ClipAfterSeconds));

        int needed = (int)(_format.AverageBytesPerSecond * (ClipBeforeSeconds + ClipAfterSeconds));
        byte[] audio;
        lock (_bufferLock)
            audio = TakeLastBytes(needed);

        if (IsSilent(audio, _format))
        {
            System.Diagnostics.Debug.WriteLine("[AudioCaptureService] Loopback silence, trying mic fallback");
            return await TryMicFallbackAsync(outputPath);
        }

        WriteWav(audio, _format, outputPath);
        return outputPath;
    }

    private async Task<string?> TryMicFallbackAsync(string outputPath)
    {
        try
        {
            var fmt = new WaveFormat(44100, 16, 1);
            var chunks = new List<byte[]>();
            using var mic = new WaveInEvent { WaveFormat = fmt };
            mic.DataAvailable += (_, e) =>
            {
                var c = new byte[e.BytesRecorded];
                Buffer.BlockCopy(e.Buffer, 0, c, 0, e.BytesRecorded);
                chunks.Add(c);
            };
            mic.StartRecording();
            await Task.Delay(TimeSpan.FromSeconds(ClipBeforeSeconds + ClipAfterSeconds));
            mic.StopRecording();

            var all = Combine(chunks);
            if (IsSilent(all, fmt))
            {
                System.Diagnostics.Debug.WriteLine("[AudioCaptureService] Mic also silent, skipping audio capture");
                return null;
            }
            WriteWav(all, fmt, outputPath);
            return outputPath;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AudioCaptureService] Mic fallback failed: {ex.Message}");
            return null;
        }
    }

    private byte[] TakeLastBytes(int count)
    {
        var available = (int)Math.Min(_totalBytes, count);
        var result = new byte[available];
        int pos = available;
        for (int i = _chunks.Count - 1; i >= 0 && pos > 0; i--)
        {
            var chunk = _chunks[i];
            int take = Math.Min(chunk.Length, pos);
            Buffer.BlockCopy(chunk, chunk.Length - take, result, pos - take, take);
            pos -= take;
        }
        return result;
    }

    private static byte[] Combine(List<byte[]> chunks)
    {
        int total = chunks.Sum(c => c.Length);
        var result = new byte[total];
        int pos = 0;
        foreach (var c in chunks) { Buffer.BlockCopy(c, 0, result, pos, c.Length); pos += c.Length; }
        return result;
    }

    private static bool IsSilent(byte[] bytes, WaveFormat fmt)
    {
        if (bytes.Length < 4) return true;
        double sumSq = 0;
        int count = 0;
        if (fmt.Encoding == WaveFormatEncoding.IeeeFloat)
        {
            for (int i = 0; i <= bytes.Length - 4; i += 4)
            {
                float s = BitConverter.ToSingle(bytes, i);
                sumSq += s * s;
                count++;
            }
        }
        else if (fmt.BitsPerSample == 16)
        {
            for (int i = 0; i <= bytes.Length - 2; i += 2)
            {
                double s = BitConverter.ToInt16(bytes, i) / 32768.0;
                sumSq += s * s;
                count++;
            }
        }
        else
        {
            return false;
        }
        return count == 0 || Math.Sqrt(sumSq / count) < SilenceThreshold;
    }

    private static void WriteWav(byte[] data, WaveFormat fmt, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var writer = new WaveFileWriter(path, fmt);
        writer.Write(data, 0, data.Length);
    }

    public void Dispose() => StopBuffer();
}
