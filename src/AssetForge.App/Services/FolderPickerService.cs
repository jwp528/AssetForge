using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace AssetForge.App.Services;

public interface IFolderPickerService { Task<string?> PickFolderAsync(); }

public sealed class FolderPickerService : IFolderPickerService
{
    public async Task<string?> PickFolderAsync()
    {
        var window = (Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (window is null || !window.StorageProvider.CanPickFolder) return null;
        var folders = await window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Open project folder",
            AllowMultiple = false
        });
        return folders.Count == 1 ? folders[0].TryGetLocalPath() : null;
    }
}
