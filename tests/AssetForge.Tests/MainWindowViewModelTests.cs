using AssetForge.App.Services;
using AssetForge.App.ViewModels;
using AssetForge.Core.Interfaces;
using AssetForge.Core.Models;
using AssetForge.Infrastructure.FileSystem;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AssetForge.Tests;

public sealed class MainWindowViewModelTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "AssetForgeViewModelTests", Guid.NewGuid().ToString("N"));

    public MainWindowViewModelTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task GenerateCreatesDraftThenApplyPublishesIt()
    {
        using var files = new ProjectFileService();
        using var viewModel = Create(files);
        await viewModel.OpenProjectCommand.ExecuteAsync(null);
        PrepareGeneration(viewModel);

        await viewModel.GenerateCommand.ExecuteAsync(null);
        Assert.Single(viewModel.Revisions);
        Assert.False(Directory.Exists(Path.Combine(_root, "sounds")));
        Assert.True(viewModel.CanApply);

        await viewModel.ApplyCommand.ExecuteAsync(null);
        Assert.True(File.Exists(Path.Combine(_root, "sounds", "shuffle.wav")));
        Assert.True(viewModel.HasPublishedAsset);
        Assert.True(viewModel.CanUndo);
    }

    [Fact]
    public async Task RetryCreatesAnotherRevisionWithFreshSeed()
    {
        using var files = new ProjectFileService();
        using var viewModel = Create(files);
        await viewModel.OpenProjectCommand.ExecuteAsync(null);
        PrepareGeneration(viewModel);
        viewModel.Seed = 42;
        await viewModel.GenerateCommand.ExecuteAsync(null);
        var first = viewModel.SelectedRevision!;

        await viewModel.RetryCommand.ExecuteAsync(null);
        Assert.Equal(2, viewModel.Revisions.Count);
        Assert.NotEqual(first.Id, viewModel.SelectedRevision!.Id);
        Assert.NotEqual(42, viewModel.SelectedRevision.Seed);
        Assert.Contains(viewModel.Conversation, entry => entry.Message.StartsWith("Retried", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DeleteDraftAndUndoRestoreRevision()
    {
        using var files = new ProjectFileService();
        using var viewModel = Create(files);
        await viewModel.OpenProjectCommand.ExecuteAsync(null);
        PrepareGeneration(viewModel);
        await viewModel.GenerateCommand.ExecuteAsync(null);
        await viewModel.DeleteRevisionCommand.ExecuteAsync(null);
        Assert.Empty(viewModel.Revisions);
        Assert.True(viewModel.CanUndo);

        await viewModel.UndoCommand.ExecuteAsync(null);
        Assert.Single(viewModel.Revisions);
    }

    private MainWindowViewModel Create(ProjectFileService files) => new(
        files, new AssetWorkspaceService(), new FakeAudio(), new FakeLocalAI(), new FakeSoundGenerator(_root),
        new FakeFolderPicker(_root), Options.Create(new LocalAISettings { StatusIntervalSeconds = 600 }),
        NullLogger<MainWindowViewModel>.Instance);

    private static void PrepareGeneration(MainWindowViewModel viewModel)
    {
        var model = new LocalAIModel { Id = "sound-model", Capabilities = ["sound_generation"] };
        viewModel.SoundModels.Add(model); viewModel.SelectedModel = model; viewModel.IsLocalAIConnected = true;
        viewModel.AssetName = "shuffle"; viewModel.Prompt = "cards shuffling"; viewModel.DurationSeconds = 2;
    }

    private sealed class FakeFolderPicker(string path) : IFolderPickerService { public Task<string?> PickFolderAsync() => Task.FromResult<string?>(path); }
    private sealed class FakeAudio : IAudioPreviewService { public Task PlayAsync(string path, CancellationToken cancellationToken = default) => Task.CompletedTask; public void Pause() { } public void Stop() { } public void Dispose() { } }
    private sealed class FakeLocalAI : ILocalAIClient
    {
        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<IReadOnlyList<LocalAIModel>> GetModelsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<LocalAIModel>>([]);
        public Task<LocalAIBinaryResponse> GenerateSoundAsync(SoundGenerationRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    private sealed class FakeSoundGenerator(string root) : ISoundGenerator
    {
        public async Task<GeneratedAsset> GenerateAsync(SoundGenerationRequest request, CancellationToken cancellationToken = default)
        {
            var path = Path.Combine(root, Guid.NewGuid().ToString("N") + ".wav"); await File.WriteAllTextAsync(path, "audio", cancellationToken);
            return new GeneratedAsset(path, request.OutputType, request.ModelId, request.Prompt);
        }
    }

    public void Dispose()
    {
        if (!Directory.Exists(_root)) return;
        foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(_root, true);
    }
}
