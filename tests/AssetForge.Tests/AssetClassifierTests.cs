using AssetForge.Core.Models;
using AssetForge.Core.Services;

namespace AssetForge.Tests;

public sealed class AssetClassifierTests
{
    [Theory]
    [InlineData("art/card.webp", AssetType.Image)]
    [InlineData("audio/music/theme.ogg", AssetType.Music)]
    [InlineData("voice/narrator.wav", AssetType.Speech)]
    [InlineData("sounds/click.mp3", AssetType.SoundEffect)]
    [InlineData("models/mesh.obj", AssetType.Unknown)]
    public void ClassifiesByExtensionAndFolderContext(string path, AssetType expected) => Assert.Equal(expected, AssetClassifier.Classify(path));

    [Fact]
    public void AudioCategoriesAreGeneralTypeCompatible() =>
        Assert.True(AssetClassifier.IsSameGeneralType(AssetType.Music, AssetType.SoundEffect));

    [Fact]
    public void ImageAndAudioAreNotGeneralTypeCompatible() =>
        Assert.False(AssetClassifier.IsSameGeneralType(AssetType.Image, AssetType.SoundEffect));
}
