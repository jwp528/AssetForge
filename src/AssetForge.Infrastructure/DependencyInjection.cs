using AssetForge.Core.Interfaces;
using AssetForge.Core.Models;
using AssetForge.Infrastructure.Audio;
using AssetForge.Infrastructure.FileSystem;
using AssetForge.Infrastructure.LocalAI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AssetForge.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddAssetForgeInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<LocalAISettings>(configuration.GetSection(LocalAISettings.SectionName));
        services.AddSingleton<IProjectFileService, ProjectFileService>();
        services.AddSingleton<IAudioPreviewService, NAudioPreviewService>();
        services.AddHttpClient<ILocalAIClient, LocalAIClient>((provider, client) =>
        {
            var settings = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<LocalAISettings>>().Value;
            client.BaseAddress = new Uri(settings.BaseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromMinutes(10);
        });
        services.AddTransient<ISoundGenerator, LocalAISoundGenerator>();
        return services;
    }
}
