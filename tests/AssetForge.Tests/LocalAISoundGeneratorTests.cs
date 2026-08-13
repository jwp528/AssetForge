using AssetForge.Core.Interfaces;
using AssetForge.Core.Models;
using AssetForge.Infrastructure.LocalAI;

namespace AssetForge.Tests;

public sealed class LocalAISoundGeneratorTests
{
    [Theory]
    [InlineData("audio/mpeg", ".mp3")]
    [InlineData("audio/flac", ".flac")]
    [InlineData("audio/ogg", ".ogg")]
    [InlineData(null, ".wav")]
    public void MapsContentType(string? contentType, string expected) => Assert.Equal(expected, LocalAISoundGenerator.GetExtension(contentType));

    [Fact]
    public async Task RejectsEmptyPromptBeforeCallingServer()
    {
        var client = new NeverCalledClient();
        await Assert.ThrowsAsync<ArgumentException>(() => new LocalAISoundGenerator(client).GenerateAsync(new SoundGenerationRequest { ModelId = "model" }));
        Assert.False(client.Called);
    }

    private sealed class NeverCalledClient : ILocalAIClient
    {
        public bool Called { get; private set; }
        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<IReadOnlyList<LocalAIModel>> GetModelsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<LocalAIModel>>([]);
        public Task<LocalAIBinaryResponse> GenerateSoundAsync(SoundGenerationRequest request, CancellationToken cancellationToken = default) { Called = true; throw new NotSupportedException(); }
    }
}
