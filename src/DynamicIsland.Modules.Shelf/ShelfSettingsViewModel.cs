using System.ComponentModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DynamicIsland.Modules.Shelf.Services;

namespace DynamicIsland.Modules.Shelf;

/// <summary>Shelf section of the settings window; every change is saved at once.</summary>
public sealed partial class ShelfSettingsViewModel : ObservableObject
{
    private readonly ShelfStore _store;
    private bool _loading;

    [ObservableProperty]
    private bool _copyFiles;

    [ObservableProperty]
    private int _autoCleanupDays;

    [ObservableProperty]
    private int _maxSizeMb;

    public ShelfSettingsViewModel(ShelfStore store)
    {
        _store = store;
        Reload();
    }

    /// <summary>Shelf folder (shown and opened in Explorer).</summary>
    public string Folder => _store.Root;

    /// <summary>Re-reads the stored settings.</summary>
    public void Reload()
    {
        _loading = true;
        CopyFiles = _store.Index.Mode == ShelfMode.Copy;
        AutoCleanupDays = _store.Index.AutoCleanupDays;
        MaxSizeMb = _store.Index.MaxSizeMb;
        _loading = false;
    }

    partial void OnCopyFilesChanged(bool value) => Apply();

    partial void OnAutoCleanupDaysChanged(int value) => Apply();

    partial void OnMaxSizeMbChanged(int value) => Apply();

    [RelayCommand]
    private void OpenFolder()
    {
        try
        {
            Directory.CreateDirectory(_store.Root);
            var explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
            using var _ = Process.Start(new ProcessStartInfo(explorer) { UseShellExecute = false, ArgumentList = { _store.Root } });
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException)
        {
            // Nothing useful to show beyond the folder path already on screen.
        }
    }

    private void Apply()
    {
        if (_loading)
        {
            return;
        }

        _store.Index.Mode = CopyFiles ? ShelfMode.Copy : ShelfMode.Link;
        _store.Index.AutoCleanupDays = AutoCleanupDays;
        _store.Index.MaxSizeMb = MaxSizeMb;
        _store.Index.Normalize();
        _ = _store.SaveAsync();
    }
}
