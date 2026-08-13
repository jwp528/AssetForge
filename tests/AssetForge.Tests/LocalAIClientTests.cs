using System.Net;
using System.Text;
using System.Text.Json;
using AssetForge.Core.Models;
using AssetForge.Infrastructure.LocalAI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AssetForge.Tests;

public sealed class LocalAIClientTests
{
    [Fact]
    public async Task DiscoversCapabilities()
    {
        var handler = new StubHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/v1/models/capabilities" => Json(HttpStatusCode.OK, """{"object":"list","data":[{"id":"sfx","capabilities":["sound_generation"],"input_modalities":["text"],"output_modalities":["audio"]}]}"""),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });
        var models = await CreateClient(handler).GetModelsAsync();
        Assert.Single(models);
        Assert.True(models[0].HasCapability("SOUND_GENERATION"));
    }

    [Fact]
    public async Task OfflineStatusReturnsFalse()
    {
        var client = CreateClient(new StubHandler((Func<HttpRequestMessage, HttpResponseMessage>)(_ => throw new HttpRequestException("offline"))));
        Assert.False(await client.IsAvailableAsync());
    }

    [Fact]
    public async Task SoundRequestUsesVerifiedWireNamesAndOmitsNulls()
    {
        string? body = null;
        var handler = new StubHandler(async request =>
        {
            body = await request.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) { Headers = { ContentType = new("audio/wav") } } };
        });
        await CreateClient(handler).GenerateSoundAsync(new SoundGenerationRequest { ModelId = "sfx", Prompt = "click", DurationSeconds = 2 });
        using var json = JsonDocument.Parse(body!);
        Assert.Equal("sfx", json.RootElement.GetProperty("model_id").GetString());
        Assert.Equal("click", json.RootElement.GetProperty("text").GetString());
        Assert.False(json.RootElement.TryGetProperty("prompt_influence", out _));
        Assert.False(json.RootElement.TryGetProperty("seed", out _));
    }

    [Fact]
    public async Task EmptyModelListIsAccepted()
    {
        var models = await CreateClient(new StubHandler(_ => Json(HttpStatusCode.OK, "{\"object\":\"list\",\"data\":[]}"))).GetModelsAsync();
        Assert.Empty(models);
    }

    [Fact]
    public async Task MalformedModelPayloadThrows()
    {
        await Assert.ThrowsAsync<JsonException>(() => CreateClient(new StubHandler(_ => Json(HttpStatusCode.OK, "not-json"))).GetModelsAsync());
    }

    [Fact]
    public async Task GenerationErrorIncludesServerDetail()
    {
        var client = CreateClient(new StubHandler(_ => Json(HttpStatusCode.BadRequest, "bad model")));
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => client.GenerateSoundAsync(new SoundGenerationRequest { ModelId = "missing", Prompt = "x" }));
        Assert.Contains("bad model", error.Message);
    }

    private static LocalAIClient CreateClient(HttpMessageHandler handler) => new(
        new HttpClient(handler) { BaseAddress = new Uri("http://localhost:8080/") },
        Options.Create(new LocalAISettings()), NullLogger<LocalAIClient>.Instance);
    private static HttpResponseMessage Json(HttpStatusCode code, string body) => new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : this(request => Task.FromResult(handler(request))) { }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => handler(request);
    }
}
