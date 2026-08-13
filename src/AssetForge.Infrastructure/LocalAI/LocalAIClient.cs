using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AssetForge.Core.Interfaces;
using AssetForge.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AssetForge.Infrastructure.LocalAI;

public sealed class LocalAIClient(HttpClient httpClient, IOptions<LocalAISettings> settings, ILogger<LocalAIClient> logger) : ILocalAIClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await httpClient.GetAsync("readyz", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException ex) { logger.LogDebug(ex, "LocalAI is unavailable at {BaseUrl}", settings.Value.BaseUrl); return false; }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested) { return false; }
    }

    public async Task<IReadOnlyList<LocalAIModel>> GetModelsAsync(CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync("v1/models/capabilities", cancellationToken);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<LocalAIModelsResponse>(JsonOptions, cancellationToken);
        return payload?.Data ?? [];
    }

    public async Task<LocalAIBinaryResponse> GenerateSoundAsync(SoundGenerationRequest request, CancellationToken cancellationToken = default)
    {
        var payload = new SoundPayload
        {
            ModelId = request.ModelId,
            Text = request.Prompt,
            DurationSeconds = request.DurationSeconds,
            PromptInfluence = request.PromptInfluence,
            Seed = request.Seed
        };
        using var response = await httpClient.PostAsJsonAsync("v1/sound-generation", payload, JsonOptions, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"LocalAI sound generation failed ({(int)response.StatusCode}): {detail}");
        }
        return new LocalAIBinaryResponse(await response.Content.ReadAsByteArrayAsync(cancellationToken), response.Content.Headers.ContentType?.MediaType);
    }

    internal sealed class SoundPayload
    {
        [JsonPropertyName("model_id")] public string ModelId { get; init; } = string.Empty;
        [JsonPropertyName("text")] public string Text { get; init; } = string.Empty;
        [JsonPropertyName("duration_seconds")] public double DurationSeconds { get; init; }
        [JsonPropertyName("prompt_influence")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? PromptInfluence { get; init; }
        [JsonPropertyName("seed")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? Seed { get; init; }
    }
}
