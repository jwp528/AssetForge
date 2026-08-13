using AssetForge.App.Services;
using AssetForge.App.ViewModels;
using AssetForge.App.Views;
using AssetForge.Infrastructure;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AssetForge.App;

public partial class App : Application
{
    private IHost? _host;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _host = Host.CreateDefaultBuilder()
                .ConfigureAppConfiguration((_, config) => config
                    .SetBasePath(AppContext.BaseDirectory)
                    .AddJsonFile("appsettings.json", optional: false)
                    .AddEnvironmentVariables("ASSETFORGE_"))
                .ConfigureServices((context, services) =>
                {
                    services.AddAssetForgeInfrastructure(context.Configuration);
                    services.AddSingleton<IFolderPickerService, FolderPickerService>();
                    services.AddSingleton<MainWindowViewModel>();
                }).Build();
            _host.Start();
            var viewModel = _host.Services.GetRequiredService<MainWindowViewModel>();
            desktop.MainWindow = new MainWindow { DataContext = viewModel };
            desktop.Exit += (_, _) => { _host.Dispose(); };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
