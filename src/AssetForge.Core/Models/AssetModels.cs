namespace AssetForge.Core.Models;

public enum AssetType { Image, SoundEffect, Music, Speech, Unknown }

public sealed record AssetFile(
    string Name,
    string FullPath,
    string RelativePath,
    AssetType Type,
    string Extension)
{
    public bool IsAudio => Type is AssetType.SoundEffect or AssetType.Music or AssetType.Speech;
}

public sealed record ProjectModel(string Name, string RootPath, IReadOnlyList<AssetFile> Assets);

public sealed record GeneratedAsset(
    string FilePath,
    AssetType Type,
    string ModelId,
    string Prompt)
{
    public string Name => Path.GetFileName(FilePath);
}
