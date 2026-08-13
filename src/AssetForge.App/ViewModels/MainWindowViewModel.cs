using System.Collections.ObjectModel;
using AssetForge.App.Services;
using AssetForge.Core.Interfaces;
using AssetForge.Core.Models;
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
    private readonly IAssetWorkspaceService _workspaces;
    private readonly IAudioPreviewService _audio;
    private readonly ILocalAIClient _localAI;
    private readonly ISoundGenerator _soundGenerator;
    private readonly IFolderPickerService _folderPicker;
    private readonly ILogger<MainWindowViewModel> _logger;
    private readonly PeriodicTimer _statusTimer;
    private readonly CancellationTokenSource _lifetime = new();
    private ProjectModel? _project;
    private AssetWorkspace? _workspace;

    [ObservableProperty] private string _projectName = "No project open";
    [ObservableProperty] private ObservableCollection<ProjectTreeNode> _projectTree = [];
    [ObservableProperty] private ProjectTreeNode? _selectedNode;
    [ObservableProperty] private string _assetFilter = "All";
    [ObservableProperty] private string _assetSearch = string.Empty;
    [ObservableProperty] private string _localAIStatus = "Checking…";
    [ObservableProperty] private bool _isLocalAIConnected;
    [ObservableProperty] private string _activityStatus = "Ready";
    [ObservableProperty] private string? _inlineError;
    [ObservableProperty] private Bitmap? _previewImage;
    [ObservableProperty] private string _canvasTitle = "Create or select an asset";
    [ObservableProperty] private string _canvasDetails = "Your selected draft or project asset appears here.";
    [ObservableProperty] private string _prompt = string.Empty;
    [ObservableProperty] private string _assetName = string.Empty;
    [ObservableProperty] private double _durationSeconds = 2;
    [ObservableProperty] private int? _seed;
    [ObservableProperty] private AssetType _generationType = AssetType.SoundEffect;
    [ObservableProperty] private LocalAIModel? _selectedModel;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private AssetRevision? _selectedRevision;
    [ObservableProperty] private AssetConversationEntry? _selectedConversationEntry;

    public ObservableCollection<LocalAIModel> SoundModels { get; } = [];
    public ObservableCollection<AssetRevision> Revisions { get; } = [];
    public ObservableCollection<AssetConversationEntry> Conversation { get; } = [];
    public IReadOnlyList<string> AssetFilters { get; } = ["All", "Images", "Audio", "Sound effects", "Music", "Speech"];
    public IReadOnlyList<AssetType> GenerationTypes { get; } = [AssetType.SoundEffect, AssetType.Music];
    public bool HasImagePreview => PreviewImage is not null;
    public bool HasWorkspace => _workspace is not null;
    public bool HasPublishedAsset => _workspace?.PublishedRelativePath is not null;
    public bool CanGenerate => _project is not null && GenerationType is AssetType.SoundEffect or AssetType.Music && IsLocalAIConnected && SelectedModel is not null && !string.IsNullOrWhiteSpace(Prompt) && !string.IsNullOrWhiteSpace(AssetName) && !IsBusy;
    public bool CanRetry => CanGenerate && SelectedRevision is not null;
    public bool CanApply => _project is not null && _workspace is not null && SelectedRevision is { State: not RevisionState.Deleted } && !IsBusy;
    public bool CanRename => _project is not null && _workspace is not null && !string.IsNullOrWhiteSpace(AssetName) && !IsBusy;
    public bool CanDeleteRevision => _project is not null && _workspace is not null && SelectedRevision is { State: not RevisionState.Deleted } && !IsBusy;
    public bool CanDeleteAsset => _project is not null && HasPublishedAsset && !IsBusy;
    public bool CanUndo { get; private set; }
    public string ConnectionGlyph => IsLocalAIConnected ? "●" : "○";
    public string PublishLabel => HasPublishedAsset ? "Apply revision" : "Publish asset";
    public string DestinationHint => GenerationType == AssetType.Image ? "Draft first · publishes to img" : "Draft first · publishes to sounds";

    public MainWindowViewModel(IProjectFileService projects, IAssetWorkspaceService workspaces, IAudioPreviewService audio,
        ILocalAIClient localAI, ISoundGenerator soundGenerator, IFolderPickerService folderPicker,
        IOptions<LocalAISettings> settings, ILogger<MainWindowViewModel> logger)
    {
        _projects = projects; _workspaces = workspaces; _audio = audio; _localAI = localAI;
        _soundGenerator = soundGenerator; _folderPicker = folderPicker; _logger = logger;
        _projects.ProjectChanged += OnProjectChanged;
        _statusTimer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(10, settings.Value.StatusIntervalSeconds)));
        _ = MonitorLocalAIAsync();
    }

    [RelayCommand]
    private async Task OpenProjectAsync()
    {
        var path = await _folderPicker.PickFolderAsync(); if (path is null) return;
        await RunAsync(async () =>
        {
            _project = await _projects.OpenProjectAsync(path, _lifetime.Token); ProjectName = _project.Name;
            ClearWorkspace(); RefreshTree(); await RefreshUndoAsync(); ActivityStatus = $"Loaded {_project.Assets.Count} assets";
        });
    }

    [RelayCommand(CanExecute = nameof(CanGenerate))]
    private Task GenerateAsync() => GenerateRevisionAsync(false);

    [RelayCommand(CanExecute = nameof(CanRetry))]
    private async Task RetryAsync()
    {
        if (SelectedRevision is { } revision)
        {
            Prompt = revision.Prompt; DurationSeconds = revision.DurationSeconds;
            SelectedModel = SoundModels.FirstOrDefault(m => m.Id == revision.ModelId) ?? SelectedModel;
            Seed = Random.Shared.Next(1, int.MaxValue);
        }
        await GenerateRevisionAsync(true);
    }

    private async Task GenerateRevisionAsync(bool isRetry)
    {
        await RunAsync(async () =>
        {
            if (_project is null) return;
            _workspace ??= await _workspaces.CreateNewAsync(_project, AssetName, GenerationType, _lifetime.Token);
            if (_workspace.PublishedRelativePath is null && _workspace.Revisions.Count == 0) { _workspace.AssetName = AssetName.Trim(); _workspace.AssetType = GenerationType; }
            var request = new SoundGenerationRequest { ModelId = SelectedModel!.Id, Prompt = Prompt.Trim(), DurationSeconds = DurationSeconds, Seed = Seed, OutputType = GenerationType };
            var temporary = await _soundGenerator.GenerateAsync(request, _lifetime.Token);
            try { var revision = await _workspaces.AddRevisionAsync(_project, _workspace, temporary, request, isRetry, _lifetime.Token); LoadWorkspace(_workspace, revision.Id); ActivityStatus = $"Created {revision.Label}"; }
            finally { try { File.Delete(temporary.FilePath); } catch (IOException ex) { _logger.LogDebug(ex, "Could not remove temporary generation"); } }
        });
    }

    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ApplyAsync()
    {
        if (_project is null || _workspace is null || SelectedRevision is null) return;
        await RunAsync(async () => { _workspace.AssetName = AssetName.Trim(); await _workspaces.ApplyAsync(_project, _workspace, SelectedRevision, _lifetime.Token); LoadWorkspace(_workspace, SelectedRevision.Id); RefreshTree(); await RefreshUndoAsync(); ActivityStatus = $"Applied {SelectedRevision.Label}"; });
    }

    [RelayCommand(CanExecute = nameof(CanRename))]
    private async Task RenameAsync()
    {
        if (_project is null || _workspace is null) return;
        await RunAsync(async () => { await _workspaces.RenameAsync(_project, _workspace, AssetName, _lifetime.Token); LoadWorkspace(_workspace, SelectedRevision?.Id); RefreshTree(); await RefreshUndoAsync(); ActivityStatus = "Renamed asset"; });
    }

    [RelayCommand(CanExecute = nameof(CanDeleteRevision))]
    private async Task DeleteRevisionAsync()
    {
        if (_project is null || _workspace is null || SelectedRevision is null) return;
        await RunAsync(async () => { var label = SelectedRevision.Label; await _workspaces.DeleteRevisionAsync(_project, _workspace, SelectedRevision, _lifetime.Token); LoadWorkspace(_workspace); await RefreshUndoAsync(); ActivityStatus = $"Deleted {label}"; });
    }

    [RelayCommand(CanExecute = nameof(CanDeleteAsset))]
    private async Task DeleteAssetAsync()
    {
        if (_project is null || _workspace is null) return;
        await RunAsync(async () => { await _workspaces.DeletePublishedAsync(_project, _workspace, _lifetime.Token); LoadWorkspace(_workspace); RefreshTree(); await RefreshUndoAsync(); ActivityStatus = "Moved asset to trash"; });
    }

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private async Task UndoAsync()
    {
        if (_project is null) return;
        await RunAsync(async () => { var undone = await _workspaces.UndoAsync(_project, _lifetime.Token); if (undone is null) return; RefreshTree(); await RefreshUndoAsync(); if (_workspace?.Id == undone.WorkspaceId) await ReloadCurrentWorkspaceAsync(); ActivityStatus = $"Undid {undone.Type}"; });
    }

    [RelayCommand] private async Task PlayAsync() { var path = SelectedRevision?.FilePath ?? SelectedNode?.Asset?.FullPath; if (path is not null) await RunAsync(() => _audio.PlayAsync(path, _lifetime.Token)); }
    [RelayCommand] private void Pause() => _audio.Pause();
    [RelayCommand] private void Stop() => _audio.Stop();

    partial void OnSelectedNodeChanged(ProjectTreeNode? value) { if (value?.Asset is { } asset) _ = OpenAssetWorkspaceAsync(asset); }
    partial void OnSelectedRevisionChanged(AssetRevision? value) { if (value is not null) { _workspace!.SelectedRevisionId = value.Id; ShowRevision(value); Prompt = value.Prompt; DurationSeconds = value.DurationSeconds; Seed = value.Seed; SelectedModel = SoundModels.FirstOrDefault(m => m.Id == value.ModelId) ?? SelectedModel; } NotifyCommands(); }
    partial void OnAssetFilterChanged(string value) => RefreshTree();
    partial void OnAssetSearchChanged(string value) => RefreshTree();
    partial void OnPromptChanged(string value) => NotifyCommands();
    partial void OnAssetNameChanged(string value) => NotifyCommands();
    partial void OnSelectedModelChanged(LocalAIModel? value) => NotifyCommands();
    partial void OnGenerationTypeChanged(AssetType value) { OnPropertyChanged(nameof(DestinationHint)); NotifyCommands(); }
    partial void OnIsBusyChanged(bool value) => NotifyCommands();
    partial void OnIsLocalAIConnectedChanged(bool value) { OnPropertyChanged(nameof(ConnectionGlyph)); NotifyCommands(); }

    private async Task OpenAssetWorkspaceAsync(AssetFile asset)
    {
        if (_project is null) return;
        await RunAsync(async () => { var loaded = await _workspaces.OpenAsync(_project, asset, _lifetime.Token); _workspace = loaded.Workspace; LoadWorkspace(_workspace); InlineError = loaded.Warning; ActivityStatus = $"Opened {asset.RelativePath}"; });
    }

    private async Task ReloadCurrentWorkspaceAsync()
    {
        if (_project is null || _workspace is null) return;
        var result = await _workspaces.OpenWorkspaceAsync(_project, _workspace.Id, _lifetime.Token);
        _workspace = result.Workspace; LoadWorkspace(_workspace); if (result.Warning is not null) InlineError = result.Warning;
    }

    private void LoadWorkspace(AssetWorkspace workspace, string? selectId = null)
    {
        AssetName = workspace.AssetName; GenerationType = workspace.AssetType;
        Revisions.Clear(); foreach (var revision in workspace.Revisions.Where(r => r.State != RevisionState.Deleted).OrderByDescending(r => r.Number)) Revisions.Add(revision);
        Conversation.Clear(); foreach (var entry in workspace.Conversation.OrderBy(e => e.CreatedAt)) Conversation.Add(entry);
        SelectedRevision = Revisions.FirstOrDefault(r => r.Id == (selectId ?? workspace.SelectedRevisionId)) ?? Revisions.FirstOrDefault();
        if (SelectedRevision is null) ShowPublished(workspace); OnPropertyChanged(nameof(HasWorkspace)); OnPropertyChanged(nameof(HasPublishedAsset)); OnPropertyChanged(nameof(PublishLabel)); NotifyCommands();
    }

    private void ClearWorkspace() { _workspace = null; Revisions.Clear(); Conversation.Clear(); SelectedRevision = null; AssetName = string.Empty; Prompt = string.Empty; CanvasTitle = "Create or select an asset"; CanvasDetails = "Describe a sound below or choose an existing project asset."; DisposePreview(); NotifyCommands(); }
    private void ShowRevision(AssetRevision revision) { DisposePreview(); CanvasTitle = $"{_workspace?.AssetName} · {revision.Label}"; CanvasDetails = $"Draft · {revision.ModelId} · seed {revision.Seed?.ToString() ?? "automatic"}"; }
    private void ShowPublished(AssetWorkspace workspace) { DisposePreview(); CanvasTitle = workspace.AssetName; CanvasDetails = workspace.PublishedRelativePath is null ? "No revisions yet" : $"Published · {workspace.PublishedRelativePath}"; if (workspace.AssetType == AssetType.Image && workspace.PublishedRelativePath is not null) TryLoadImage(Path.Combine(_project!.RootPath, workspace.PublishedRelativePath)); }
    private void TryLoadImage(string path) { try { PreviewImage = new Bitmap(path); } catch (Exception ex) { InlineError = $"Could not preview image: {ex.Message}"; } OnPropertyChanged(nameof(HasImagePreview)); }
    private void DisposePreview() { PreviewImage?.Dispose(); PreviewImage = null; OnPropertyChanged(nameof(HasImagePreview)); }

    private async Task RefreshUndoAsync() { if (_project is null) CanUndo = false; else { try { CanUndo = (await _workspaces.GetHistoryAsync(_project, _lifetime.Token)).Operations.Any(o => !o.IsUndone); } catch (InvalidDataException ex) { CanUndo = false; InlineError = ex.Message; } } OnPropertyChanged(nameof(CanUndo)); UndoCommand.NotifyCanExecuteChanged(); }
    private void RefreshTree() { if (_project is null) { ProjectTree = []; return; } IEnumerable<AssetFile> assets = _projects.GetAssets(_project); assets = AssetFilter switch { "Images" => assets.Where(a => a.Type == AssetType.Image), "Audio" => assets.Where(a => a.IsAudio), "Sound effects" => assets.Where(a => a.Type == AssetType.SoundEffect), "Music" => assets.Where(a => a.Type == AssetType.Music), "Speech" => assets.Where(a => a.Type == AssetType.Speech), _ => assets }; if (!string.IsNullOrWhiteSpace(AssetSearch)) assets = assets.Where(a => a.RelativePath.Contains(AssetSearch, StringComparison.OrdinalIgnoreCase)); ProjectTree = ProjectTreeNode.Build(assets); }
    private void OnProjectChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(() => { RefreshTree(); });

    private async Task MonitorLocalAIAsync() { do { await RefreshLocalAIAsync(); } while (await _statusTimer.WaitForNextTickAsync(_lifetime.Token).ConfigureAwait(false)); }
    private async Task RefreshLocalAIAsync() { try { var connected = await _localAI.IsAvailableAsync(_lifetime.Token); var models = connected ? await _localAI.GetModelsAsync(_lifetime.Token) : []; await Dispatcher.UIThread.InvokeAsync(() => { IsLocalAIConnected = connected; LocalAIStatus = connected ? "Connected" : "Offline"; SoundModels.Clear(); foreach (var model in models.Where(m => m.HasCapability("sound_generation"))) SoundModels.Add(model); SelectedModel ??= SoundModels.FirstOrDefault(); }); } catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { } catch (Exception ex) { _logger.LogWarning(ex, "LocalAI status failed"); await Dispatcher.UIThread.InvokeAsync(() => { IsLocalAIConnected = false; LocalAIStatus = "Offline"; }); } }
    private async Task RunAsync(Func<Task> action) { InlineError = null; IsBusy = true; ActivityStatus = "Working…"; try { await action(); } catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { } catch (Exception ex) { _logger.LogWarning(ex, "Workspace operation failed"); InlineError = ex.Message; ActivityStatus = "Action failed"; } finally { IsBusy = false; } }
    private void NotifyCommands() { OnPropertyChanged(nameof(CanGenerate)); OnPropertyChanged(nameof(CanRetry)); OnPropertyChanged(nameof(CanApply)); OnPropertyChanged(nameof(CanRename)); OnPropertyChanged(nameof(CanDeleteRevision)); OnPropertyChanged(nameof(CanDeleteAsset)); GenerateCommand.NotifyCanExecuteChanged(); RetryCommand.NotifyCanExecuteChanged(); ApplyCommand.NotifyCanExecuteChanged(); RenameCommand.NotifyCanExecuteChanged(); DeleteRevisionCommand.NotifyCanExecuteChanged(); DeleteAssetCommand.NotifyCanExecuteChanged(); }
    public void Dispose() { _lifetime.Cancel(); _statusTimer.Dispose(); _projects.ProjectChanged -= OnProjectChanged; _audio.Dispose(); DisposePreview(); _lifetime.Dispose(); }
}
