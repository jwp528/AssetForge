using AssetForge.Core.Models;

namespace AssetForge.Core.Interfaces;

public interface IProjectFileService : IDisposable
{
    event EventHandler? ProjectChanged;
    Task<ProjectModel> OpenProjectAsync(string path, CancellationToken cancellationToken = default);
    IReadOnlyList<AssetFile> GetAssets(ProjectModel project);
    Task<string> ReplaceFileAsync(ProjectModel project, AssetFile target, GeneratedAsset generated, CancellationToken cancellationToken = default);
}

public interface IAudioPreviewService : IDisposable
{
    Task PlayAsync(string path, CancellationToken cancellationToken = default);
    void Pause();
    void Stop();
}

public interface ILocalAIClient
{
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LocalAIModel>> GetModelsAsync(CancellationToken cancellationToken = default);
    Task<LocalAIBinaryResponse> GenerateSoundAsync(SoundGenerationRequest request, CancellationToken cancellationToken = default);
}

public sealed record LocalAIBinaryResponse(byte[] Content, string? ContentType);

public interface ISoundGenerator
{
    Task<GeneratedAsset> GenerateAsync(SoundGenerationRequest request, CancellationToken cancellationToken = default);
}

public interface IImageGenerator
{
    Task<GeneratedAsset> GenerateAsync(ImageGenerationRequest request, CancellationToken cancellationToken = default);
}

public interface ITextToSpeechGenerator
{
    Task<GeneratedAsset> GenerateAsync(TextToSpeechRequest request, CancellationToken cancellationToken = default);
}
