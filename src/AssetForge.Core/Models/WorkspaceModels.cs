namespace AssetForge.Core.Models;

public enum RevisionState { Draft, Applied, Deleted }
public enum ConversationRole { User, AssetForge, Tool }
public enum ProjectOperationType { Create, Replace, Rename, DeleteAsset, DeleteRevision }

public sealed class AssetRevision
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public int Number { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
    public double DurationSeconds { get; set; }
    public int? Seed { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public RevisionState State { get; set; } = RevisionState.Draft;
    public string? PublishedRelativePath { get; set; }
    public string Label => $"v{Number}";
    public string Name => Path.GetFileName(FilePath);
}

public sealed class AssetConversationEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public ConversationRole Role { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? RevisionId { get; set; }
}

public sealed class AssetWorkspace
{
    public const int CurrentSchemaVersion = 1;
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string AssetName { get; set; } = string.Empty;
    public AssetType AssetType { get; set; } = AssetType.SoundEffect;
    public string? PublishedRelativePath { get; set; }
    public string? SelectedRevisionId { get; set; }
    public List<AssetRevision> Revisions { get; set; } = [];
    public List<AssetConversationEntry> Conversation { get; set; } = [];
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ProjectOperation
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public ProjectOperationType Type { get; set; }
    public string WorkspaceId { get; set; } = string.Empty;
    public string? SourceRelativePath { get; set; }
    public string? TargetRelativePath { get; set; }
    public string? StoredRelativePath { get; set; }
    public string? RevisionId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public bool IsUndone { get; set; }
}

public sealed class ProjectHistory
{
    public const int CurrentSchemaVersion = 1;
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public List<ProjectOperation> Operations { get; set; } = [];
}

public sealed record WorkspaceLoadResult(AssetWorkspace Workspace, string? Warning = null);
