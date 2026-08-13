using AssetForge.Core.Models;
using AssetForge.Infrastructure.FileSystem;

namespace AssetForge.Tests;

public sealed class AssetWorkspaceServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "AssetForgeWorkspaceTests", Guid.NewGuid().ToString("N"));
    private readonly AssetWorkspaceService _service = new();
    private ProjectModel Project => new("Test", _root, []);

    public AssetWorkspaceServiceTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task RevisionsArePrivateUniqueAndPersisted()
    {
        var workspace = await _service.CreateNewAsync(Project, "shuffle", AssetType.SoundEffect);
        var first = await AddRevisionAsync(workspace, 100, false);
        var second = await AddRevisionAsync(workspace, 200, true);

        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal("v1", first.Label);
        Assert.Equal("v2", second.Label);
        Assert.Contains(Path.Combine(".assetforge", "revisions", workspace.Id), first.FilePath);
        Assert.False(Directory.Exists(Path.Combine(_root, "sounds")));

        var metadata = Directory.GetFiles(Path.Combine(_root, ".assetforge", "workspaces"), "*.json").Single();
        var persisted = System.Text.Json.JsonSerializer.Deserialize<AssetWorkspace>(await File.ReadAllTextAsync(metadata), new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        Assert.Equal(2, persisted!.Revisions.Count);
        Assert.Equal(4, persisted.Conversation.Count);
        Assert.Equal(200, persisted.Revisions[1].Seed);
    }

    [Fact]
    public async Task ApplyNewAssetAndUndoSurviveServiceRestart()
    {
        var workspace = await _service.CreateNewAsync(Project, "shuffle", AssetType.SoundEffect);
        var revision = await AddRevisionAsync(workspace, 123, false);
        await _service.ApplyAsync(Project, workspace, revision);
        var published = Path.Combine(_root, "sounds", "shuffle.wav");
        Assert.True(File.Exists(published));

        var restarted = new AssetWorkspaceService();
        var operation = await restarted.UndoAsync(Project);
        Assert.Equal(ProjectOperationType.Create, operation!.Type);
        Assert.False(File.Exists(published));
        Assert.True((await restarted.GetHistoryAsync(Project)).Operations.Single().IsUndone);
    }

    [Fact]
    public async Task ApplyReplacementAndUndoRestoresOriginal()
    {
        var path = Path.Combine(_root, "sounds", "click.wav");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "original");
        var asset = new AssetFile("click.wav", path, Path.Combine("sounds", "click.wav"), AssetType.SoundEffect, ".wav");
        var workspace = (await _service.OpenAsync(Project, asset)).Workspace;
        var revision = await AddRevisionAsync(workspace, 321, false, "replacement");
        await _service.ApplyAsync(Project, workspace, revision);
        Assert.Equal("replacement", await File.ReadAllTextAsync(path));

        await new AssetWorkspaceService().UndoAsync(Project);
        Assert.Equal("original", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task PublishedRenameAndUndoRestorePath()
    {
        var workspace = await _service.CreateNewAsync(Project, "old", AssetType.SoundEffect);
        var revision = await AddRevisionAsync(workspace, 1, false);
        await _service.ApplyAsync(Project, workspace, revision);
        await _service.RenameAsync(Project, workspace, "new");
        Assert.True(File.Exists(Path.Combine(_root, "sounds", "new.wav")));

        await new AssetWorkspaceService().UndoAsync(Project);
        Assert.True(File.Exists(Path.Combine(_root, "sounds", "old.wav")));
        Assert.False(File.Exists(Path.Combine(_root, "sounds", "new.wav")));
    }

    [Fact]
    public async Task DraftRenameAndUndoRestoreMetadata()
    {
        var workspace = await _service.CreateNewAsync(Project, "old", AssetType.SoundEffect);
        await _service.RenameAsync(Project, workspace, "new");
        await new AssetWorkspaceService().UndoAsync(Project);
        var metadata = Directory.GetFiles(Path.Combine(_root, ".assetforge", "workspaces"), "*.json").Single();
        var persisted = System.Text.Json.JsonSerializer.Deserialize<AssetWorkspace>(await File.ReadAllTextAsync(metadata), new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        Assert.Equal("old", persisted!.AssetName);
    }

    [Fact]
    public async Task DeletePublishedMovesToTrashAndUndoRestores()
    {
        var workspace = await _service.CreateNewAsync(Project, "trash-me", AssetType.SoundEffect);
        var revision = await AddRevisionAsync(workspace, 1, false);
        await _service.ApplyAsync(Project, workspace, revision);
        var published = Path.Combine(_root, "sounds", "trash-me.wav");
        await _service.DeletePublishedAsync(Project, workspace);
        Assert.False(File.Exists(published));
        Assert.Single(Directory.GetFiles(Path.Combine(_root, ".assetforge", "trash"), "*", SearchOption.AllDirectories));

        await new AssetWorkspaceService().UndoAsync(Project);
        Assert.True(File.Exists(published));
    }

    [Fact]
    public async Task DeleteDraftMovesToTrashAndUndoRestores()
    {
        var workspace = await _service.CreateNewAsync(Project, "draft", AssetType.SoundEffect);
        var revision = await AddRevisionAsync(workspace, 1, false);
        await _service.DeleteRevisionAsync(Project, workspace, revision);
        Assert.Equal(RevisionState.Deleted, revision.State);
        Assert.False(File.Exists(revision.FilePath));

        await new AssetWorkspaceService().UndoAsync(Project);
        Assert.True(File.Exists(revision.FilePath));
        var metadata = Directory.GetFiles(Path.Combine(_root, ".assetforge", "workspaces"), "*.json").Single();
        var persisted = System.Text.Json.JsonSerializer.Deserialize<AssetWorkspace>(await File.ReadAllTextAsync(metadata), new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        Assert.Equal(RevisionState.Draft, persisted!.Revisions.Single().State);
    }

    [Fact]
    public async Task ApplyCollisionPreservesExistingAsset()
    {
        var existing = Path.Combine(_root, "sounds", "same.wav"); Directory.CreateDirectory(Path.GetDirectoryName(existing)!); await File.WriteAllTextAsync(existing, "existing");
        var workspace = await _service.CreateNewAsync(Project, "same", AssetType.SoundEffect);
        var revision = await AddRevisionAsync(workspace, 1, false);
        var error = await Assert.ThrowsAsync<IOException>(() => _service.ApplyAsync(Project, workspace, revision));
        Assert.Contains("open the existing asset timeline", error.Message);
        Assert.Equal("existing", await File.ReadAllTextAsync(existing));
    }

    [Fact]
    public async Task LockedReplacementIsPreservedWhenBackupFails()
    {
        var path = Path.Combine(_root, "sounds", "locked.wav"); Directory.CreateDirectory(Path.GetDirectoryName(path)!); await File.WriteAllTextAsync(path, "original");
        var asset = new AssetFile("locked.wav", path, Path.Combine("sounds", "locked.wav"), AssetType.SoundEffect, ".wav");
        var workspace = (await _service.OpenAsync(Project, asset)).Workspace;
        var revision = await AddRevisionAsync(workspace, 1, false, "replacement");
        await using var fileLock = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        await Assert.ThrowsAsync<IOException>(() => _service.ApplyAsync(Project, workspace, revision));
        fileLock.Position = 0; using var reader = new StreamReader(fileLock, leaveOpen: true);
        Assert.Equal("original", await reader.ReadToEndAsync());
    }

    [Theory]
    [InlineData("")]
    [InlineData("../escape")]
    [InlineData("nested/name")]
    [InlineData("wrong.wav")]
    public async Task NewWorkspaceRejectsInvalidNames(string name)
    {
        await Assert.ThrowsAnyAsync<ArgumentException>(() => _service.CreateNewAsync(Project, name, AssetType.SoundEffect));
    }

    [Fact]
    public async Task ImageApplyPublishesToImgFolder()
    {
        var workspace = await _service.CreateNewAsync(Project, "logo", AssetType.Image);
        var temp = Path.Combine(_root, "temp.png"); await File.WriteAllTextAsync(temp, "png");
        var revision = await _service.AddRevisionAsync(Project, workspace, new GeneratedAsset(temp, AssetType.Image, "image-model", "logo"), new SoundGenerationRequest { ModelId = "image-model", Prompt = "logo", OutputType = AssetType.Image }, false);
        await _service.ApplyAsync(Project, workspace, revision);
        Assert.True(File.Exists(Path.Combine(_root, "img", "logo.png")));
    }

    [Fact]
    public async Task CorruptWorkspaceReturnsWarningWithoutChangingAsset()
    {
        var path = Path.Combine(_root, "sounds", "safe.wav"); Directory.CreateDirectory(Path.GetDirectoryName(path)!); await File.WriteAllTextAsync(path, "safe");
        var asset = new AssetFile("safe.wav", path, Path.Combine("sounds", "safe.wav"), AssetType.SoundEffect, ".wav");
        var first = await _service.OpenAsync(Project, asset);
        var metadata = Directory.GetFiles(Path.Combine(_root, ".assetforge", "workspaces"), "*.json").Single();
        await File.WriteAllTextAsync(metadata, "not json");
        var loaded = await new AssetWorkspaceService().OpenAsync(Project, asset);
        Assert.NotNull(loaded.Warning);
        Assert.Equal("safe", await File.ReadAllTextAsync(path));
        Assert.Equal(first.Workspace.Id, loaded.Workspace.Id);
    }

    private async Task<AssetRevision> AddRevisionAsync(AssetWorkspace workspace, int seed, bool retry, string content = "draft")
    {
        var temp = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".wav");
        await File.WriteAllTextAsync(temp, content);
        return await _service.AddRevisionAsync(Project, workspace, new GeneratedAsset(temp, AssetType.SoundEffect, "model", "prompt"), new SoundGenerationRequest { ModelId = "model", Prompt = "prompt", DurationSeconds = 2, Seed = seed }, retry);
    }

    public void Dispose()
    {
        if (!Directory.Exists(_root)) return;
        foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(_root, true);
    }
}
