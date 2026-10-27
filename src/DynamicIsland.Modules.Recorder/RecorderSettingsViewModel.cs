using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DynamicIsland.Modules.Recorder.Services;
using DynamicIsland.UI.Localization;

namespace DynamicIsland.Modules.Recorder;

/// <summary>Recorder settings: recordings folder (checked by <see cref="RecorderSettings.IsAcceptableFolder"/>) and format.</summary>
public sealed partial class RecorderSettingsViewModel : ObservableObject
{
    private readonly RecorderViewModel _recorder;
    private bool _loading;

    [ObservableProperty]
    private string _folder = string.Empty;

    [ObservableProperty]
    private bool _saveAsMp3;

    [ObservableProperty]
    private string? _folderError;

    public RecorderSettingsViewModel(RecorderViewModel recorder)
    {
        _recorder = recorder;
        Reload();
    }

    /// <summary>Re-reads the stored settings.</summary>
    public void Reload()
    {
        _loading = true;
        Folder = _recorder.Settings.EffectiveFolder;
        SaveAsMp3 = _recorder.Settings.Format == RecordingFormat.Mp3;
        FolderError = null;
        _loading = false;
    }

    /// <summary>Applies a folder picked by the user; refused folders keep the old one.</summary>
    public bool TrySetFolder(string folder)
    {
        if (!RecorderSettings.IsAcceptableFolder(folder))
        {
            FolderError = Loc.Instance["Recorder.FolderRefused"];
            return false;
        }

        FolderError = null;
        _recorder.Settings.Folder = folder;
        Folder = _recorder.Settings.EffectiveFolder;
        _ = _recorder.SaveSettingsAsync();
        return true;
    }

    [RelayCommand]
    private void ChooseFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { InitialDirectory = Folder, Title = Loc.Instance["Recorder.ChooseFolder"] };
        if (dialog.ShowDialog() == true)
        {
            TrySetFolder(dialog.FolderName);
        }
    }

    [RelayCommand]
    private void ResetFolder()
    {
        _recorder.Settings.Folder = null;
        Folder = _recorder.Settings.EffectiveFolder;
        FolderError = null;
        _ = _recorder.SaveSettingsAsync();
    }

    partial void OnSaveAsMp3Changed(bool value)
    {
        if (_loading)
        {
            return;
        }

        _recorder.Settings.Format = value ? RecordingFormat.Mp3 : RecordingFormat.Wav;
        _ = _recorder.SaveSettingsAsync();
    }
}
