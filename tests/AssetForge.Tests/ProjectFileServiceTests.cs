using AssetForge.Core.Models;
using AssetForge.Infrastructure.FileSystem;

namespace AssetForge.Tests;

public sealed class ProjectFileServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "AssetForgeTests", Guid.NewGuid().ToString("N"));

    public ProjectFileServiceTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task ScanFindsSupportedFilesAndExcludesAssetForgeDirectory()
    {
        Directory.CreateDirectory(Path.Combine(_root, "images"));
        Directory.CreateDirectory(Path.Combine(_root, ".assetforge", "backups"));
        await File.WriteAllTextAsync(Path.Combine(_root, "images", "logo.png"), "image");
        await File.WriteAllTextAsync(Path.Combine(_root, "readme.txt"), "text");
        await File.WriteAllTextAsync(Path.Combine(_root, ".assetforge", "backups", "old.png"), "old");
        using var service = new ProjectFileService();
        var project = await service.OpenProjectAsync(_root);
        Assert.Single(project.Assets);
        Assert.Equal(Path.Combine("images", "logo.png"), project.Assets[0].RelativePath);
    }

    [Fact]
    public async Task ReplaceCreatesBackupAndReplacesTarget()
    {
        var targetPath = Path.Combine(_root, "sounds", "click.wav");
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        await File.WriteAllTextAsync(targetPath, "original");
        var generatedPath = Path.Combine(_root, "generated.wav");
        await File.WriteAllTextAsync(generatedPath, "replacement");
        using var service = new ProjectFileService();
        var project = await service.OpenProjectAsync(_root);
        var target = project.Assets.Single(a => a.Name == "click.wav");
        var backup = await service.ReplaceFileAsync(project, target, new GeneratedAsset(generatedPath, AssetType.SoundEffect, "test", "click"));
        Assert.Equal("original", await File.ReadAllTextAsync(backup));
        Assert.Equal("replacement", await File.ReadAllTextAsync(targetPath));
        Assert.Contains(Path.Combine(".assetforge", "backups"), backup);
    }

    [Fact]
    public async Task ReplaceRejectsMismatchedGeneralType()
    {
        var targetPath = Path.Combine(_root, "image.png");
        var generatedPath = Path.Combine(_root, "sound.wav");
        await File.WriteAllTextAsync(targetPath, "original");
        await File.WriteAllTextAsync(generatedPath, "sound");
        using var service = new ProjectFileService();
        var project = await service.OpenProjectAsync(_root);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ReplaceFileAsync(project, project.Assets.Single(), new GeneratedAsset(generatedPath, AssetType.SoundEffect, "test", "x")));
        Assert.Equal("original", await File.ReadAllTextAsync(targetPath));
    }

    [Fact]
    public async Task LockedTargetIsPreservedWhenBackupCannotBeCreated()
    {
        var targetPath = Path.Combine(_root, "locked.wav");
        var generatedPath = Path.Combine(_root, "generated.wav");
        await File.WriteAllTextAsync(targetPath, "original");
        await File.WriteAllTextAsync(generatedPath, "replacement");
        using var service = new ProjectFileService();
        var project = await service.OpenProjectAsync(_root);
        await using var lockStream = new FileStream(targetPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        await Assert.ThrowsAsync<IOException>(() => service.ReplaceFileAsync(project, project.Assets.Single(a => a.Name == "locked.wav"), new GeneratedAsset(generatedPath, AssetType.SoundEffect, "test", "x")));
        lockStream.Position = 0;
        using var reader = new StreamReader(lockStream, leaveOpen: true);
        Assert.Equal("original", await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task WatcherRaisesOneDebouncedChangeNotification()
    {
        using var service = new ProjectFileService();
        await service.OpenProjectAsync(_root);
        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;
        service.ProjectChanged += (_, _) => { Interlocked.Increment(ref count); changed.TrySetResult(); };
        var path = Path.Combine(_root, "new.wav");
        await File.WriteAllTextAsync(path, "one");
        await File.AppendAllTextAsync(path, "two");
        await changed.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await Task.Delay(500);
        Assert.Equal(1, Volatile.Read(ref count));
    }

    public void Dispose()
    {
        if (!Directory.Exists(_root)) return;
        foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(_root, true);
    }
}
