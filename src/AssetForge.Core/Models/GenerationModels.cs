using System.Text.Json.Serialization;

namespace AssetForge.Core.Models;

public sealed class SoundGenerationRequest
{
    public string ModelId { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
    public double DurationSeconds { get; set; } = 2;
    public double? PromptInfluence { get; set; }
    public int? Seed { get; set; }
    public AssetType OutputType { get; set; } = AssetType.SoundEffect;
}

public sealed class ImageGenerationRequest
{
    public string ModelId { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
    public int Width { get; set; } = 512;
    public int Height { get; set; } = 512;
}

public sealed class TextToSpeechRequest
{
    public string ModelId { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string? ReferenceAudioPath { get; set; }
}

public sealed class LocalAISettings
{
    public const string SectionName = "LocalAI";
    public string BaseUrl { get; set; } = "http://localhost:8080";
    public int StatusIntervalSeconds { get; set; } = 30;
}

public sealed class LocalAIModelsResponse
{
    [JsonPropertyName("object")] public string Object { get; set; } = string.Empty;
    [JsonPropertyName("data")] public List<LocalAIModel> Data { get; set; } = [];
}

public sealed class LocalAIModel
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("capabilities")] public List<string> Capabilities { get; set; } = [];
    [JsonPropertyName("input_modalities")] public List<string> InputModalities { get; set; } = [];
    [JsonPropertyName("output_modalities")] public List<string> OutputModalities { get; set; } = [];

    public bool HasCapability(string capability) =>
        Capabilities.Contains(capability, StringComparer.OrdinalIgnoreCase);
}
