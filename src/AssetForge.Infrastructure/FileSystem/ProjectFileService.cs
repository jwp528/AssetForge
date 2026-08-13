using AssetForge.Core.Interfaces;
using AssetForge.Core.Models;
using AssetForge.Core.Services;

namespace AssetForge.Infrastructure.FileSystem;

public sealed class ProjectFileService : IProjectFileService
{
    private FileSystemWatcher? _watcher;
    private CancellationTokenSource? _debounce;
    public event EventHandler? ProjectChanged;

    public Task<ProjectModel> OpenProjectAsync(string path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            throw new DirectoryNotFoundException("Select an existing project folder.");

        var root = Path.GetFullPath(path);
        var project = new ProjectModel(new DirectoryInfo(root).Name, root, Scan(root));
        ConfigureWatcher(root);
        return Task.FromResult(project);
    }

    public IReadOnlyList<AssetFile> GetAssets(ProjectModel project) => Scan(project.RootPath);

    public async Task<GeneratedAsset> SaveGeneratedAssetAsync(ProjectModel project, GeneratedAsset generated, string assetName, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(generated.FilePath)) throw new FileNotFoundException("The generated asset no longer exists.", generated.FilePath);
        var targetPath = GetGeneratedAssetPath(project, generated, assetName);
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        await CopyAsync(generated.FilePath, targetPath, cancellationToken);
        return generated with { FilePath = targetPath };
    }

    public Task<GeneratedAsset> RenameGeneratedAssetAsync(ProjectModel project, GeneratedAsset generated, string assetName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(generated.FilePath)) throw new FileNotFoundException("The generated asset no longer exists.", generated.FilePath);
        var currentPath = Path.GetFullPath(generated.FilePath);
        var relative = Path.GetRelativePath(project.RootPath, currentPath);
        if (relative.StartsWith("..", StringComparison.Ordinal))
            throw new InvalidOperationException("Only generated assets saved in the selected project can be renamed.");
        var targetPath = GetGeneratedAssetPath(project, generated, assetName);
        if (string.Equals(currentPath, targetPath, StringComparison.OrdinalIgnoreCase)) return Task.FromResult(generated);
        File.Move(currentPath, targetPath, false);
        return Task.FromResult(generated with { FilePath = targetPath });
    }

    public async Task<string> ReplaceFileAsync(ProjectModel project, AssetFile target, GeneratedAsset generated, CancellationToken cancellationToken = default)
    {
        if (!AssetClassifier.IsSameGeneralType(target.Type, generated.Type))
            throw new InvalidOperationException("The generated asset type does not match the selected project asset.");
        if (!File.Exists(target.FullPath)) throw new FileNotFoundException("The selected project asset no longer exists.", target.FullPath);
        if (!File.Exists(generated.FilePath)) throw new FileNotFoundException("The generated asset no longer exists.", generated.FilePath);

        var targetPath = Path.GetFullPath(target.FullPath);
        var relative = Path.GetRelativePath(project.RootPath, targetPath);
        if (relative.StartsWith("..", StringComparison.Ordinal))
            throw new InvalidOperationException("The target must be inside the selected project.");

        var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmssfff");
        var backupPath = Path.Combine(project.RootPath, ".assetforge", "backups", stamp, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
        await CopyAsync(targetPath, backupPath, cancellationToken);

        var stagingPath = targetPath + ".assetforge-replacement";
        try
        {
            await CopyAsync(generated.FilePath, stagingPath, cancellationToken);
            File.Move(stagingPath, targetPath, true);
        }
        finally
        {
            if (File.Exists(stagingPath)) File.Delete(stagingPath);
        }

        return backupPath;
    }

    private static async Task CopyAsync(string source, string destination, CancellationToken cancellationToken)
    {
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
        await input.CopyToAsync(output, cancellationToken);
        await output.FlushAsync(cancellationToken);
    }

    private static string GetGeneratedAssetPath(ProjectModel project, GeneratedAsset generated, string assetName)
    {
        var name = assetName.Trim();
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Enter an asset name.", nameof(assetName));
        if (name is "." or ".." || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains(Path.DirectorySeparatorChar) || name.Contains(Path.AltDirectorySeparatorChar))
            throw new ArgumentException("Use a filename without folder separators or invalid characters.", nameof(assetName));

        var generatedExtension = Path.GetExtension(generated.FilePath);
        var suppliedExtension = Path.GetExtension(name);
        if (string.IsNullOrEmpty(suppliedExtension)) name += generatedExtension;
        else if (!string.Equals(suppliedExtension, generatedExtension, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"The generated asset must keep its {generatedExtension} extension.", nameof(assetName));

        var folder = generated.Type == AssetType.Image ? "img" : AssetClassifier.IsAudio(generated.Type) ? "sounds" : throw new InvalidOperationException("Unsupported generated asset type.");
        var path = Path.GetFullPath(Path.Combine(project.RootPath, folder, name));
        if (File.Exists(path)) throw new IOException($"An asset named '{name}' already exists in the {folder} folder.");
        return path;
    }

    private static IReadOnlyList<AssetFile> Scan(string root)
    {
        var assets = new List<AssetFile>();
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            IEnumerable<string> children;
            try { children = Directory.EnumerateDirectories(directory); }
            catch (UnauthorizedAccessException) { continue; }
            foreach (var child in children)
            {
                if (string.Equals(Path.GetFileName(child), ".assetforge", StringComparison.OrdinalIgnoreCase)) continue;
                pending.Push(child);
            }
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(directory); }
            catch (UnauthorizedAccessException) { continue; }
            foreach (var file in files)
            {
                var type = AssetClassifier.Classify(file);
                if (type == AssetType.Unknown) continue;
                assets.Add(new AssetFile(Path.GetFileName(file), file, Path.GetRelativePath(root, file), type, Path.GetExtension(file)));
            }
        }
        return assets.OrderBy(a => a.RelativePath, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private void ConfigureWatcher(string root)
    {
        _watcher?.Dispose();
        _watcher = new FileSystemWatcher(root)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true
        };
        _watcher.Changed += OnChanged;
        _watcher.Created += OnChanged;
        _watcher.Deleted += OnChanged;
        _watcher.Renamed += OnChanged;
    }

    private void OnChanged(object sender, FileSystemEventArgs e)
    {
        if (e.FullPath.Split(Path.DirectorySeparatorChar).Contains(".assetforge", StringComparer.OrdinalIgnoreCase)) return;
        _debounce?.Cancel();
        _debounce?.Dispose();
        _debounce = new CancellationTokenSource();
        var token = _debounce.Token;
        _ = Task.Run(async () =>
        {
            try { await Task.Delay(350, token); ProjectChanged?.Invoke(this, EventArgs.Empty); }
            catch (OperationCanceledException) { }
        }, token);
    }

    public void Dispose() { _debounce?.Cancel(); _debounce?.Dispose(); _watcher?.Dispose(); }
}
