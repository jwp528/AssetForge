using System.Collections.ObjectModel;
using AssetForge.App.Services;
using AssetForge.Core.Interfaces;
using AssetForge.Core.Models;
using AssetForge.Core.Services;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AssetForge.App.ViewModels;

public partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly IProjectFileService _projects;
    private readonly IAudioPreviewService _audio;
    private readonly ILocalAIClient _localAI;
    private readonly ISoundGenerator _soundGenerator;
    private readonly IFolderPickerService _folderPicker;
    private readonly ILogger<MainWindowViewModel> _logger;
    private readonly PeriodicTimer _statusTimer;
    private readonly CancellationTokenSource _lifetime = new();
    private ProjectModel? _project;
    private IReadOnlyList<LocalAIModel> _models = [];

    [ObservableProperty] private string _projectName = "No project open";
    [ObservableProperty] private ObservableCollection<ProjectTreeNode> _projectTree = [];
    [ObservableProperty] private ProjectTreeNode? _selectedNode;
    [ObservableProperty] private string _assetFilter = "All";
    [ObservableProperty] private string _localAIStatus = "Checking…";
    [ObservableProperty] private bool _isLocalAIConnected;
    [ObservableProperty] private string _statusMessage = "Open a project folder to begin.";
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private Bitmap? _previewImage;
    [ObservableProperty] private string _previewTitle = "Select an asset";
    [ObservableProperty] private string _previewDetails = "Images and audio appear here.";
    [ObservableProperty] private string _prompt = string.Empty;
    [ObservableProperty] private string _assetName = string.Empty;
    [ObservableProperty] private double _durationSeconds = 2;
    [ObservableProperty] private int? _seed;
    [ObservableProperty] private AssetType _generationType = AssetType.SoundEffect;
    [ObservableProperty] private LocalAIModel? _selectedModel;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private GeneratedAsset? _selectedGeneratedAsset;

    public ObservableCollection<LocalAIModel> SoundModels { get; } = [];
    public ObservableCollection<GeneratedAsset> GeneratedAssets { get; } = [];
    public IReadOnlyList<string> AssetFilters { get; } = ["All", "Images", "Audio", "Sound effects", "Music", "Speech"];
    public IReadOnlyList<AssetType> GenerationTypes { get; } = [AssetType.SoundEffect, AssetType.Music];
    public bool HasImagePreview => PreviewImage is not null;
    public bool HasSelectedAudio => SelectedNode?.Asset?.IsAudio == true;
    public bool CanReplace => HasSelectedAudio && SelectedGeneratedAsset is not null && !IsBusy;
    public bool CanGenerate => _project is not null && IsLocalAIConnected && SelectedModel is not null && !string.IsNullOrWhiteSpace(Prompt) && !string.IsNullOrWhiteSpace(AssetName) && !IsBusy;
    public bool CanRename => _project is not null && SelectedGeneratedAsset is not null && !string.IsNullOrWhiteSpace(AssetName) && !IsBusy;
    public string ConnectionGlyph => IsLocalAIConnected ? "●" : "○";

    public MainWindowViewModel(IProjectFileService projects, IAudioPreviewService audio, ILocalAIClient localAI,
        ISoundGenerator soundGenerator, IFolderPickerService folderPicker, IOptions<LocalAISettings> settings,
        ILogger<MainWindowViewModel> logger)
    {
        _projects = projects; _audio = audio; _localAI = localAI; _soundGenerator = soundGenerator; _folderPicker = folderPicker; _logger = logger;
        _projects.ProjectChanged += OnProjectChanged;
        _statusTimer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(10, settings.Value.StatusIntervalSeconds)));
        _ = MonitorLocalAIAsync();
    }

    [RelayCommand]
    private async Task OpenProjectAsync()
    {
        var path = await _folderPicker.PickFolderAsync();
        if (path is null) return;
        await RunAsync(async () => { _project = await _projects.OpenProjectAsync(path); ProjectName = _project.Name; RefreshTree(); OnPropertyChanged(nameof(CanGenerate)); GenerateCommand.NotifyCanExecuteChanged(); StatusMessage = $"Loaded {_project.Assets.Count} supported assets."; });
    }

    [RelayCommand(CanExecute = nameof(CanGenerate))]
    private async Task GenerateAsync()
    {
        await RunAsync(async () =>
        {
            var temporary = await _soundGenerator.GenerateAsync(new SoundGenerationRequest
            {
                ModelId = SelectedModel!.Id, Prompt = Prompt.Trim(), DurationSeconds = DurationSeconds, Seed = Seed, OutputType = GenerationType
            }, _lifetime.Token);
            var generated = await _projects.SaveGeneratedAssetAsync(_project!, temporary, AssetName, _lifetime.Token);
            try { File.Delete(temporary.FilePath); } catch (IOException ex) { _logger.LogDebug(ex, "Could not remove temporary generated asset {Path}", temporary.FilePath); }
            GeneratedAssets.Insert(0, generated); SelectedGeneratedAsset = generated;
            PreviewGenerated(generated); AssetName = Path.GetFileNameWithoutExtension(generated.FilePath); RefreshTree(); StatusMessage = $"Saved {Path.GetRelativePath(_project!.RootPath, generated.FilePath)}.";
        });
    }

    [RelayCommand(CanExecute = nameof(CanRename))]
    private async Task RenameGeneratedAsync()
    {
        if (_project is null || SelectedGeneratedAsset is not { } selected) return;
        await RunAsync(async () =>
        {
            var renamed = await _projects.RenameGeneratedAssetAsync(_project, selected, AssetName, _lifetime.Token);
            var index = GeneratedAssets.IndexOf(selected);
            if (index >= 0) GeneratedAssets[index] = renamed;
            SelectedGeneratedAsset = renamed;
            AssetName = Path.GetFileNameWithoutExtension(renamed.FilePath);
            RefreshTree(); StatusMessage = $"Renamed asset to {renamed.Name}.";
        });
    }

    [RelayCommand]
    private async Task PlayAsync()
    {
        var path = SelectedGeneratedAsset?.FilePath ?? SelectedNode?.Asset?.FullPath;
        if (path is null) return;
        await RunAsync(() => _audio.PlayAsync(path, _lifetime.Token));
    }
    [RelayCommand] private void Pause() => _audio.Pause();
    [RelayCommand] private void Stop() => _audio.Stop();

    [RelayCommand(CanExecute = nameof(CanReplace))]
    private async Task ReplaceOriginalAsync()
    {
        if (_project is null || SelectedNode?.Asset is not { } target || SelectedGeneratedAsset is not { } generated) return;
        await RunAsync(async () =>
        {
            var relative = target.RelativePath;
            var backup = await _projects.ReplaceFileAsync(_project, target, generated, _lifetime.Token);
            await ReloadProjectAsync(relative);
            StatusMessage = $"Replaced {relative}. Backup: {Path.GetRelativePath(_project.RootPath, backup)}";
        });
    }

    partial void OnSelectedNodeChanged(ProjectTreeNode? value)
    {
        _audio.Stop(); PreviewImage?.Dispose(); PreviewImage = null;
        if (value?.Asset is not { } asset) { PreviewTitle = value?.Name ?? "Select an asset"; PreviewDetails = value is null ? "Images and audio appear here." : "Folder"; }
        else
        {
            PreviewTitle = asset.Name; PreviewDetails = $"{asset.Type} · {asset.RelativePath}";
            if (asset.Type == AssetType.Image)
            {
                try { PreviewImage = new Bitmap(asset.FullPath); PreviewDetails += $" · {PreviewImage.PixelSize.Width} × {PreviewImage.PixelSize.Height}"; }
                catch (Exception ex) { ErrorMessage = $"Could not preview image: {ex.Message}"; }
            }
            GenerationType = asset.Type is AssetType.Music ? AssetType.Music : AssetType.SoundEffect;
        }
        OnPropertyChanged(nameof(HasImagePreview)); OnPropertyChanged(nameof(HasSelectedAudio)); OnPropertyChanged(nameof(CanReplace));
        ReplaceOriginalCommand.NotifyCanExecuteChanged();
    }

    partial void OnAssetFilterChanged(string value) => RefreshTree();
    partial void OnPromptChanged(string value) { OnPropertyChanged(nameof(CanGenerate)); GenerateCommand.NotifyCanExecuteChanged(); }
    partial void OnAssetNameChanged(string value) { OnPropertyChanged(nameof(CanGenerate)); OnPropertyChanged(nameof(CanRename)); GenerateCommand.NotifyCanExecuteChanged(); RenameGeneratedCommand.NotifyCanExecuteChanged(); }
    partial void OnSelectedModelChanged(LocalAIModel? value) { OnPropertyChanged(nameof(CanGenerate)); GenerateCommand.NotifyCanExecuteChanged(); }
    partial void OnIsBusyChanged(bool value) { OnPropertyChanged(nameof(CanGenerate)); OnPropertyChanged(nameof(CanReplace)); OnPropertyChanged(nameof(CanRename)); GenerateCommand.NotifyCanExecuteChanged(); ReplaceOriginalCommand.NotifyCanExecuteChanged(); RenameGeneratedCommand.NotifyCanExecuteChanged(); }
    partial void OnIsLocalAIConnectedChanged(bool value) { OnPropertyChanged(nameof(ConnectionGlyph)); OnPropertyChanged(nameof(CanGenerate)); GenerateCommand.NotifyCanExecuteChanged(); }
    partial void OnSelectedGeneratedAssetChanged(GeneratedAsset? value) { if (value is not null) { PreviewGenerated(value); AssetName = Path.GetFileNameWithoutExtension(value.FilePath); } OnPropertyChanged(nameof(CanReplace)); OnPropertyChanged(nameof(CanRename)); ReplaceOriginalCommand.NotifyCanExecuteChanged(); RenameGeneratedCommand.NotifyCanExecuteChanged(); }

    private void PreviewGenerated(GeneratedAsset generated) { PreviewImage?.Dispose(); PreviewImage = null; PreviewTitle = generated.Name; PreviewDetails = $"Generated {generated.Type} · {generated.ModelId}"; OnPropertyChanged(nameof(HasImagePreview)); }

    private async Task MonitorLocalAIAsync()
    {
        do { await RefreshLocalAIAsync(); }
        while (await _statusTimer.WaitForNextTickAsync(_lifetime.Token).ConfigureAwait(false));
    }

    private async Task RefreshLocalAIAsync()
    {
        try
        {
            var connected = await _localAI.IsAvailableAsync(_lifetime.Token);
            IReadOnlyList<LocalAIModel> models = connected ? await _localAI.GetModelsAsync(_lifetime.Token) : [];
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                IsLocalAIConnected = connected; LocalAIStatus = connected ? "Connected" : "Offline"; _models = models;
                SoundModels.Clear(); foreach (var model in models.Where(m => m.HasCapability("sound_generation"))) SoundModels.Add(model);
                SelectedModel = SoundModels.FirstOrDefault();
                if (connected && SoundModels.Count == 0) ErrorMessage = "No compatible LocalAI sound-generation model found.";
            });
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex) { _logger.LogWarning(ex, "Could not refresh LocalAI status"); await Dispatcher.UIThread.InvokeAsync(() => { IsLocalAIConnected = false; LocalAIStatus = "Offline"; }); }
    }

    private void OnProjectChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(async () => { if (_project is not null) await ReloadProjectAsync(SelectedNode?.Asset?.RelativePath); });
    private async Task ReloadProjectAsync(string? selectRelative)
    {
        if (_project is null) return;
        _project = await _projects.OpenProjectAsync(_project.RootPath, _lifetime.Token); RefreshTree();
        if (selectRelative is not null) SelectedNode = Flatten(ProjectTree).FirstOrDefault(n => n.Asset?.RelativePath == selectRelative);
    }
    private void RefreshTree()
    {
        if (_project is null) { ProjectTree = []; return; }
        IEnumerable<AssetFile> assets = _projects.GetAssets(_project);
        assets = AssetFilter switch { "Images" => assets.Where(a => a.Type == AssetType.Image), "Audio" => assets.Where(a => a.IsAudio), "Sound effects" => assets.Where(a => a.Type == AssetType.SoundEffect), "Music" => assets.Where(a => a.Type == AssetType.Music), "Speech" => assets.Where(a => a.Type == AssetType.Speech), _ => assets };
        ProjectTree = ProjectTreeNode.Build(assets);
    }
    private static IEnumerable<ProjectTreeNode> Flatten(IEnumerable<ProjectTreeNode> nodes) { foreach (var node in nodes) { yield return node; foreach (var child in Flatten(node.Children)) yield return child; } }
    private async Task RunAsync(Func<Task> action)
    {
        ErrorMessage = null; IsBusy = true;
        try { await action(); }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex) { _logger.LogWarning(ex, "AssetForge operation failed"); ErrorMessage = ex.Message; }
        finally { IsBusy = false; }
    }
    public void Dispose() { _lifetime.Cancel(); _statusTimer.Dispose(); _projects.ProjectChanged -= OnProjectChanged; _audio.Dispose(); PreviewImage?.Dispose(); _lifetime.Dispose(); }
}
