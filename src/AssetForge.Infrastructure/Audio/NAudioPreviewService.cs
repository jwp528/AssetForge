using AssetForge.Core.Interfaces;
using NAudio.Wave;

namespace AssetForge.Infrastructure.Audio;

public sealed class NAudioPreviewService : IAudioPreviewService
{
    private WaveOutEvent? _output;
    private AudioFileReader? _reader;

    public Task PlayAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(path)) throw new FileNotFoundException("The audio file no longer exists.", path);
        Stop();
        _reader = new AudioFileReader(path);
        _output = new WaveOutEvent();
        _output.Init(_reader);
        _output.Play();
        return Task.CompletedTask;
    }

    public void Pause() => _output?.Pause();
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
