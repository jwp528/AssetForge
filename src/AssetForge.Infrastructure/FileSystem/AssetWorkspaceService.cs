using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AssetForge.Core.Interfaces;
using AssetForge.Core.Models;
using AssetForge.Core.Services;

namespace AssetForge.Infrastructure.FileSystem;

public sealed class AssetWorkspaceService : IAssetWorkspaceService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task<WorkspaceLoadResult> OpenAsync(ProjectModel project, AssetFile? asset = null, CancellationToken cancellationToken = default)
    {
        if (asset is null) return new WorkspaceLoadResult(new AssetWorkspace());
        var id = StableId(asset.RelativePath);
        var path = WorkspacePath(project, id);
        if (!File.Exists(path))
        {
            var created = new AssetWorkspace { Id = id, AssetName = Path.GetFileNameWithoutExtension(asset.Name), AssetType = asset.Type, PublishedRelativePath = asset.RelativePath };
            await SaveWorkspaceAsync(project, created, cancellationToken);
            return new WorkspaceLoadResult(created);
        }
        try
        {
            var workspace = await ReadAsync<AssetWorkspace>(path, cancellationToken) ?? throw new JsonException("Workspace metadata is empty.");
            if (workspace.SchemaVersion > AssetWorkspace.CurrentSchemaVersion) throw new JsonException("Workspace metadata was created by a newer AssetForge version.");
            return new WorkspaceLoadResult(workspace);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            var safe = new AssetWorkspace { Id = id, AssetName = Path.GetFileNameWithoutExtension(asset.Name), AssetType = asset.Type, PublishedRelativePath = asset.RelativePath };
            return new WorkspaceLoadResult(safe, $"History could not be loaded: {ex.Message}. The project asset was not changed.");
        }
    }

    public async Task<WorkspaceLoadResult> OpenWorkspaceAsync(ProjectModel project, string workspaceId, CancellationToken cancellationToken = default)
    {
        try
        {
            var workspace = await OpenByIdAsync(project, workspaceId, cancellationToken) ?? throw new FileNotFoundException("Workspace metadata no longer exists.");
            return new WorkspaceLoadResult(workspace);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return new WorkspaceLoadResult(new AssetWorkspace { Id = workspaceId }, $"History could not be loaded: {ex.Message}. Project assets were not changed.");
        }
    }

    public async Task<AssetWorkspace> CreateNewAsync(ProjectModel project, string assetName, AssetType assetType, CancellationToken cancellationToken = default)
    {
        ValidateName(assetName, null);
        var workspace = new AssetWorkspace { AssetName = assetName.Trim(), AssetType = assetType };
        await SaveWorkspaceAsync(project, workspace, cancellationToken);
        return workspace;
    }

    public async Task<AssetRevision> AddRevisionAsync(ProjectModel project, AssetWorkspace workspace, GeneratedAsset generated, SoundGenerationRequest request, bool isRetry, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(generated.FilePath)) throw new FileNotFoundException("The generated draft no longer exists.", generated.FilePath);
        var revision = new AssetRevision
        {
            Number = workspace.Revisions.Count == 0 ? 1 : workspace.Revisions.Max(r => r.Number) + 1,
            ModelId = request.ModelId, Prompt = request.Prompt, DurationSeconds = request.DurationSeconds, Seed = request.Seed
        };
        var extension = Path.GetExtension(generated.FilePath);
        var relative = Path.Combine(".assetforge", "revisions", workspace.Id, revision.Id + extension);
        revision.FilePath = Path.Combine(project.RootPath, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(revision.FilePath)!);
        await CopyNewAsync(generated.FilePath, revision.FilePath, cancellationToken);
        workspace.Revisions.Add(revision);
        workspace.SelectedRevisionId = revision.Id;
        workspace.Conversation.Add(new AssetConversationEntry { Role = ConversationRole.User, Message = request.Prompt, RevisionId = revision.Id });
        workspace.Conversation.Add(new AssetConversationEntry { Role = ConversationRole.AssetForge, Message = $"{(isRetry ? "Retried" : "Generated")} {revision.Label} with {request.ModelId}.", RevisionId = revision.Id });
        await SaveWorkspaceAsync(project, workspace, cancellationToken);
        return revision;
    }

    public async Task ApplyAsync(ProjectModel project, AssetWorkspace workspace, AssetRevision revision, CancellationToken cancellationToken = default)
    {
        EnsureDraft(workspace, revision);
        var extension = Path.GetExtension(revision.FilePath);
        ValidateName(workspace.AssetName, extension);
        var targetRelative = workspace.PublishedRelativePath ?? Path.Combine(FolderFor(workspace.AssetType), workspace.AssetName + extension);
        var target = SafeProjectPath(project, targetRelative);
        var operation = new ProjectOperation { WorkspaceId = workspace.Id, TargetRelativePath = targetRelative, RevisionId = revision.Id };
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        if (File.Exists(target))
        {
            if (workspace.PublishedRelativePath is null) throw new IOException($"An asset named '{Path.GetFileName(target)}' already exists. Rename this workspace or open the existing asset timeline.");
            operation.Type = ProjectOperationType.Replace;
            operation.StoredRelativePath = Path.Combine(".assetforge", "backups", operation.Id, targetRelative);
            var backup = SafeProjectPath(project, operation.StoredRelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
            await CopyNewAsync(target, backup, cancellationToken);
            await ReplaceFromAsync(revision.FilePath, target, cancellationToken);
        }
        else
        {
            operation.Type = ProjectOperationType.Create;
            await CopyNewAsync(revision.FilePath, target, cancellationToken);
        }
        revision.State = RevisionState.Applied;
        revision.PublishedRelativePath = targetRelative;
        workspace.PublishedRelativePath = targetRelative;
        workspace.Conversation.Add(new AssetConversationEntry { Role = ConversationRole.Tool, Message = $"Applied {revision.Label} to {targetRelative}.", RevisionId = revision.Id });
        await RecordAsync(project, workspace, operation, cancellationToken);
    }

    public async Task RenameAsync(ProjectModel project, AssetWorkspace workspace, string newName, CancellationToken cancellationToken = default)
    {
        var extension = workspace.PublishedRelativePath is null ? null : Path.GetExtension(workspace.PublishedRelativePath);
        ValidateName(newName, extension);
        var oldName = workspace.AssetName;
        if (workspace.PublishedRelativePath is null)
        {
            workspace.AssetName = newName.Trim();
            workspace.Conversation.Add(new AssetConversationEntry { Role = ConversationRole.Tool, Message = $"Renamed {oldName} to {workspace.AssetName}." });
            await RecordAsync(project, workspace, new ProjectOperation { Type = ProjectOperationType.Rename, WorkspaceId = workspace.Id, SourceRelativePath = oldName, TargetRelativePath = workspace.AssetName, StoredRelativePath = "workspace-name" }, cancellationToken);
            return;
        }
        var sourceRelative = workspace.PublishedRelativePath;
        var targetRelative = Path.Combine(Path.GetDirectoryName(sourceRelative) ?? FolderFor(workspace.AssetType), newName.Trim() + extension);
        var source = SafeProjectPath(project, sourceRelative);
        var target = SafeProjectPath(project, targetRelative);
        if (File.Exists(target)) throw new IOException($"An asset named '{Path.GetFileName(target)}' already exists.");
        File.Move(source, target);
        workspace.AssetName = newName.Trim(); workspace.PublishedRelativePath = targetRelative;
        workspace.Conversation.Add(new AssetConversationEntry { Role = ConversationRole.Tool, Message = $"Renamed {oldName} to {workspace.AssetName}." });
        await RecordAsync(project, workspace, new ProjectOperation { Type = ProjectOperationType.Rename, WorkspaceId = workspace.Id, SourceRelativePath = sourceRelative, TargetRelativePath = targetRelative }, cancellationToken);
    }

    public async Task DeleteRevisionAsync(ProjectModel project, AssetWorkspace workspace, AssetRevision revision, CancellationToken cancellationToken = default)
    {
        EnsureDraft(workspace, revision);
        if (revision.State == RevisionState.Deleted) return;
        var operation = new ProjectOperation { Type = ProjectOperationType.DeleteRevision, WorkspaceId = workspace.Id, RevisionId = revision.Id };
        operation.SourceRelativePath = Path.GetRelativePath(project.RootPath, revision.FilePath);
        operation.StoredRelativePath = Path.Combine(".assetforge", "trash", operation.Id, Path.GetFileName(revision.FilePath));
        MoveNew(revision.FilePath, SafeProjectPath(project, operation.StoredRelativePath));
        revision.State = RevisionState.Deleted;
        workspace.SelectedRevisionId = workspace.Revisions.LastOrDefault(r => r.State != RevisionState.Deleted)?.Id;
        workspace.Conversation.Add(new AssetConversationEntry { Role = ConversationRole.Tool, Message = $"Deleted draft {revision.Label}. Undo is available.", RevisionId = revision.Id });
        await RecordAsync(project, workspace, operation, cancellationToken);
    }

    public async Task DeletePublishedAsync(ProjectModel project, AssetWorkspace workspace, CancellationToken cancellationToken = default)
    {
        if (workspace.PublishedRelativePath is null) throw new InvalidOperationException("This workspace has no published asset.");
        var operation = new ProjectOperation { Type = ProjectOperationType.DeleteAsset, WorkspaceId = workspace.Id, SourceRelativePath = workspace.PublishedRelativePath };
        operation.StoredRelativePath = Path.Combine(".assetforge", "trash", operation.Id, workspace.PublishedRelativePath);
        MoveNew(SafeProjectPath(project, workspace.PublishedRelativePath), SafeProjectPath(project, operation.StoredRelativePath));
        workspace.PublishedRelativePath = null;
        workspace.Conversation.Add(new AssetConversationEntry { Role = ConversationRole.Tool, Message = "Moved the published asset to AssetForge trash. Undo is available." });
        await RecordAsync(project, workspace, operation, cancellationToken);
    }

    public async Task<ProjectOperation?> UndoAsync(ProjectModel project, CancellationToken cancellationToken = default)
    {
        var history = await GetHistoryAsync(project, cancellationToken);
        var operation = history.Operations.LastOrDefault(o => !o.IsUndone);
        if (operation is null) return null;
        var workspace = (await OpenByIdAsync(project, operation.WorkspaceId, cancellationToken)) ?? throw new InvalidDataException("The workspace for the last operation no longer exists.");
        switch (operation.Type)
        {
            case ProjectOperationType.Create:
                MoveNew(SafeProjectPath(project, operation.TargetRelativePath!), TrashUndoPath(project, operation)); workspace.PublishedRelativePath = null; SetRevisionDraft(workspace, operation.RevisionId); break;
            case ProjectOperationType.Replace:
                await ReplaceFromAsync(SafeProjectPath(project, operation.StoredRelativePath!), SafeProjectPath(project, operation.TargetRelativePath!), cancellationToken); SetRevisionDraft(workspace, operation.RevisionId); break;
            case ProjectOperationType.Rename:
                if (operation.StoredRelativePath == "workspace-name") workspace.AssetName = operation.SourceRelativePath!;
                else { File.Move(SafeProjectPath(project, operation.TargetRelativePath!), SafeProjectPath(project, operation.SourceRelativePath!)); workspace.PublishedRelativePath = operation.SourceRelativePath; workspace.AssetName = Path.GetFileNameWithoutExtension(operation.SourceRelativePath!); }
                break;
            case ProjectOperationType.DeleteAsset:
                MoveNew(SafeProjectPath(project, operation.StoredRelativePath!), SafeProjectPath(project, operation.SourceRelativePath!)); workspace.PublishedRelativePath = operation.SourceRelativePath; break;
            case ProjectOperationType.DeleteRevision:
                var revision = workspace.Revisions.Single(r => r.Id == operation.RevisionId); MoveNew(SafeProjectPath(project, operation.StoredRelativePath!), SafeProjectPath(project, operation.SourceRelativePath!)); revision.State = RevisionState.Draft; workspace.SelectedRevisionId = revision.Id; break;
        }
        operation.IsUndone = true;
        workspace.Conversation.Add(new AssetConversationEntry { Role = ConversationRole.Tool, Message = $"Undid {operation.Type}." });
        await SaveWorkspaceAsync(project, workspace, cancellationToken); await SaveHistoryAsync(project, history, cancellationToken);
        return operation;
    }

    public async Task<ProjectHistory> GetHistoryAsync(ProjectModel project, CancellationToken cancellationToken = default)
    {
        var path = HistoryPath(project);
        if (!File.Exists(path)) return new ProjectHistory();
        try { return await ReadAsync<ProjectHistory>(path, cancellationToken) ?? new ProjectHistory(); }
        catch (JsonException ex) { throw new InvalidDataException("Project history is corrupt. Project assets were not changed.", ex); }
    }

    private static async Task RecordAsync(ProjectModel project, AssetWorkspace workspace, ProjectOperation operation, CancellationToken cancellationToken)
    { var history = await ReadHistorySafeAsync(project, cancellationToken); history.Operations.Add(operation); await SaveWorkspaceAsync(project, workspace, cancellationToken); await SaveHistoryAsync(project, history, cancellationToken); }
    private static async Task<ProjectHistory> ReadHistorySafeAsync(ProjectModel project, CancellationToken token) { var path = HistoryPath(project); return File.Exists(path) ? await ReadAsync<ProjectHistory>(path, token) ?? new ProjectHistory() : new ProjectHistory(); }
    private static async Task<AssetWorkspace?> OpenByIdAsync(ProjectModel project, string id, CancellationToken token) => await ReadAsync<AssetWorkspace>(WorkspacePath(project, id), token);
    private static async Task SaveWorkspaceAsync(ProjectModel project, AssetWorkspace workspace, CancellationToken token) { workspace.UpdatedAt = DateTimeOffset.UtcNow; await WriteAtomicAsync(WorkspacePath(project, workspace.Id), workspace, token); }
    private static Task SaveHistoryAsync(ProjectModel project, ProjectHistory history, CancellationToken token) => WriteAtomicAsync(HistoryPath(project), history, token);
    private static async Task<T?> ReadAsync<T>(string path, CancellationToken token) { await using var stream = File.OpenRead(path); return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, token); }
    private static async Task WriteAtomicAsync<T>(string path, T value, CancellationToken token) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); var temp = path + ".tmp-" + Guid.NewGuid().ToString("N"); await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true)) { await JsonSerializer.SerializeAsync(stream, value, JsonOptions, token); await stream.FlushAsync(token); } File.Move(temp, path, true); }
    private static async Task CopyNewAsync(string source, string target, CancellationToken token) { Directory.CreateDirectory(Path.GetDirectoryName(target)!); await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true); await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true); await input.CopyToAsync(output, token); await output.FlushAsync(token); }
    private static async Task ReplaceFromAsync(string source, string target, CancellationToken token) { var stage = target + ".assetforge-stage-" + Guid.NewGuid().ToString("N"); try { await CopyNewAsync(source, stage, token); File.Move(stage, target, true); } finally { if (File.Exists(stage)) File.Delete(stage); } }
    private static void MoveNew(string source, string target) { if (!File.Exists(source)) throw new FileNotFoundException("The asset no longer exists.", source); if (File.Exists(target)) throw new IOException("The recovery destination already exists."); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Move(source, target); }
    private static void EnsureDraft(AssetWorkspace workspace, AssetRevision revision) { if (!workspace.Revisions.Contains(revision)) throw new InvalidOperationException("The revision does not belong to this workspace."); if (!File.Exists(revision.FilePath)) throw new FileNotFoundException("The draft file no longer exists.", revision.FilePath); }
    private static void SetRevisionDraft(AssetWorkspace workspace, string? revisionId) { var revision = workspace.Revisions.FirstOrDefault(r => r.Id == revisionId); if (revision is not null) { revision.State = RevisionState.Draft; revision.PublishedRelativePath = null; workspace.SelectedRevisionId = revision.Id; } }
    private static void ValidateName(string name, string? extension) { var value = name.Trim(); if (string.IsNullOrWhiteSpace(value) || value is "." or ".." || value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || value.Contains('/') || value.Contains('\\') || Path.HasExtension(value)) throw new ArgumentException("Enter a valid asset name without folders or a file extension.", nameof(name)); }
    private static string FolderFor(AssetType type) => type == AssetType.Image ? "img" : AssetClassifier.IsAudio(type) ? "sounds" : throw new InvalidOperationException("Unsupported asset type.");
    private static string SafeProjectPath(ProjectModel project, string relative) { var root = Path.GetFullPath(project.RootPath) + Path.DirectorySeparatorChar; var path = Path.GetFullPath(Path.Combine(project.RootPath, relative)); if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Path must stay inside the selected project."); return path; }
    private static string StableId(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.Replace('\\', '/').ToLowerInvariant())))[..24].ToLowerInvariant();
    private static string WorkspacePath(ProjectModel project, string id) => Path.Combine(project.RootPath, ".assetforge", "workspaces", id + ".json");
    private static string HistoryPath(ProjectModel project) => Path.Combine(project.RootPath, ".assetforge", "history.json");
    private static string TrashUndoPath(ProjectModel project, ProjectOperation operation) => SafeProjectPath(project, Path.Combine(".assetforge", "trash", operation.Id + "-undo", operation.TargetRelativePath!));
}
