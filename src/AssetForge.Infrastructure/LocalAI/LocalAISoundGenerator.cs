using AssetForge.Core.Interfaces;
using AssetForge.Core.Models;

namespace AssetForge.Infrastructure.LocalAI;

public sealed class LocalAISoundGenerator(ILocalAIClient client) : ISoundGenerator
{
    public async Task<GeneratedAsset> GenerateAsync(SoundGenerationRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.ModelId)) throw new ArgumentException("Select a sound-generation model.");
        if (string.IsNullOrWhiteSpace(request.Prompt)) throw new ArgumentException("Enter a sound description.");
        if (request.DurationSeconds is < 0.1 or > 300) throw new ArgumentOutOfRangeException(nameof(request), "Duration must be between 0.1 and 300 seconds.");

        var response = await client.GenerateSoundAsync(request, cancellationToken);
        if (response.Content.Length == 0) throw new InvalidDataException("LocalAI returned an empty audio file.");
        var extension = GetExtension(response.ContentType);
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AssetForge", "generated");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{DateTimeOffset.UtcNow:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}{extension}");
        await File.WriteAllBytesAsync(path, response.Content, cancellationToken);
        return new GeneratedAsset(path, request.OutputType, request.ModelId, request.Prompt);
    }

    public static string GetExtension(string? contentType) => contentType?.ToLowerInvariant() switch
    {
        "audio/mpeg" or "audio/mp3" => ".mp3",
        "audio/flac" => ".flac",
        "audio/ogg" or "application/ogg" => ".ogg",
        "audio/opus" => ".opus",
        _ => ".wav"
    };
}
