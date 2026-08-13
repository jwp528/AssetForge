using AssetForge.Core.Models;

namespace AssetForge.Core.Interfaces;

public interface IProjectFileService : IDisposable
{
    event EventHandler? ProjectChanged;
    Task<ProjectModel> OpenProjectAsync(string path, CancellationToken cancellationToken = default);
    IReadOnlyList<AssetFile> GetAssets(ProjectModel project);
}

public interface IAssetWorkspaceService
{
    Task<WorkspaceLoadResult> OpenAsync(ProjectModel project, AssetFile? asset = null, CancellationToken cancellationToken = default);
    Task<WorkspaceLoadResult> OpenWorkspaceAsync(ProjectModel project, string workspaceId, CancellationToken cancellationToken = default);
    Task<AssetWorkspace> CreateNewAsync(ProjectModel project, string assetName, AssetType assetType, CancellationToken cancellationToken = default);
    Task<AssetRevision> AddRevisionAsync(ProjectModel project, AssetWorkspace workspace, GeneratedAsset generated, SoundGenerationRequest request, bool isRetry, CancellationToken cancellationToken = default);
    Task ApplyAsync(ProjectModel project, AssetWorkspace workspace, AssetRevision revision, CancellationToken cancellationToken = default);
    Task RenameAsync(ProjectModel project, AssetWorkspace workspace, string newName, CancellationToken cancellationToken = default);
    Task DeleteRevisionAsync(ProjectModel project, AssetWorkspace workspace, AssetRevision revision, CancellationToken cancellationToken = default);
    Task DeletePublishedAsync(ProjectModel project, AssetWorkspace workspace, CancellationToken cancellationToken = default);
    Task<ProjectOperation?> UndoAsync(ProjectModel project, CancellationToken cancellationToken = default);
    Task<ProjectHistory> GetHistoryAsync(ProjectModel project, CancellationToken cancellationToken = default);
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
