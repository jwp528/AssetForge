using AssetForge.Core.Models;

namespace AssetForge.Core.Services;

public static class AssetClassifier
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".png", ".jpg", ".jpeg", ".webp" };
    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".wav", ".mp3", ".ogg", ".flac", ".opus" };

    public static bool IsSupported(string path) => Classify(path) != AssetType.Unknown;

    public static AssetType Classify(string path)
    {
        var extension = Path.GetExtension(path);
        if (ImageExtensions.Contains(extension)) return AssetType.Image;
        if (!AudioExtensions.Contains(extension)) return AssetType.Unknown;

        var segments = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(s => s.Contains("speech", StringComparison.OrdinalIgnoreCase) ||
                              s.Contains("voice", StringComparison.OrdinalIgnoreCase) ||
                              s.Contains("tts", StringComparison.OrdinalIgnoreCase)))
            return AssetType.Speech;
        if (segments.Any(s => s.Contains("music", StringComparison.OrdinalIgnoreCase) ||
                              s.Contains("song", StringComparison.OrdinalIgnoreCase) ||
                              s.Contains("soundtrack", StringComparison.OrdinalIgnoreCase)))
            return AssetType.Music;
        return AssetType.SoundEffect;
    }

    public static bool IsSameGeneralType(AssetType left, AssetType right) =>
        left == AssetType.Image && right == AssetType.Image || IsAudio(left) && IsAudio(right);

    public static bool IsAudio(AssetType type) =>
        type is AssetType.SoundEffect or AssetType.Music or AssetType.Speech;
}
