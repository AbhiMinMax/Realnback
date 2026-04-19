using NAudio.Wave;

namespace MemoryTrainer.Services;

public class AudioPlayerService : IDisposable
{
    private WaveOutEvent? _output;
    private AudioFileReader? _reader;

    public bool IsPlaying => _output?.PlaybackState == PlaybackState.Playing;
    public bool IsPaused => _output?.PlaybackState == PlaybackState.Paused;

    public event Action? PlaybackStopped;

    public void Play(string filePath)
    {
        Stop();
        try
        {
            _reader = new AudioFileReader(filePath);
            _output = new WaveOutEvent();
            _output.Init(_reader);
            _output.PlaybackStopped += (_, _) => PlaybackStopped?.Invoke();
            _output.Play();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AudioPlayerService] Play failed: {ex.Message}");
        }
    }

    public void Pause()
    {
        if (_output?.PlaybackState == PlaybackState.Playing)
            _output.Pause();
    }

    public void Resume()
    {
        if (_output?.PlaybackState == PlaybackState.Paused)
            _output.Play();
    }

    public void Stop()
    {
        _output?.Stop();
        _output?.Dispose();
        _reader?.Dispose();
        _output = null;
        _reader = null;
    }

    public void Dispose() => Stop();
}
