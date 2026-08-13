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
