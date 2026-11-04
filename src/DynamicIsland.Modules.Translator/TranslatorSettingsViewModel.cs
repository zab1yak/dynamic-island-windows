using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DynamicIsland.Modules.Translator.Services;
using DynamicIsland.UI.Localization;

namespace DynamicIsland.Modules.Translator;

/// <summary>API-key input is supplied by a PasswordBox and never bound to a string property.</summary>
public sealed partial class TranslatorSettingsViewModel : ObservableObject, IDisposable
{
    private readonly ITranslationCredentialStore _store;
    private readonly TranslatorViewModel _translator;
    private string _statusKey = "Translator.Configure";

    [ObservableProperty]
    private bool _consent;

    public TranslatorSettingsViewModel(ITranslationCredentialStore store, TranslatorViewModel translator)
    {
        _store = store;
        _translator = translator;
        Loc.Instance.CultureChanged += OnCultureChanged;
    }

    public string Status => Loc.Instance[_statusKey];

    public void Load()
    {
        try
        {
            var access = _store.Read();
            Consent = access?.Consent == true;
            _translator.SetConfigured(access is not null && Consent);
            SetStatus(access is not null && Consent ? "Translator.Connected" : "Translator.Configure");
        }
        catch (Win32Exception)
        {
            SetStatus("Translator.CredentialError");
        }
    }

    public bool SaveApiKey(string apiKey)
    {
        try
        {
            var key = apiKey.Trim();
            // A blank PasswordBox retains the existing key; no secret is ever loaded into the UI.
            if (key.Length == 0)
            {
                key = _store.Read()?.ApiKey ?? string.Empty;
            }

            if (key.Length == 0)
            {
                SetStatus("Translator.Configure");
                return false;
            }

            _translator.CancelCommand.Execute(null);
            _store.Write(new TranslationCredentials(key, Consent));
            _translator.SetConfigured(Consent);
            SetStatus(Consent ? "Translator.Connected" : "Translator.ConsentRequired");
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or ArgumentException)
        {
            SetStatus("Translator.CredentialError");
            return false;
        }
    }

    [RelayCommand]
    private void Disconnect()
    {
        _translator.CancelCommand.Execute(null);
        try
        {
            _store.Delete();
            Consent = false;
            _translator.SetConfigured(false);
            SetStatus("Translator.Configure");
        }
        catch (Win32Exception)
        {
            SetStatus("Translator.CredentialError");
        }
    }

    public void Dispose() => Loc.Instance.CultureChanged -= OnCultureChanged;

    private void OnCultureChanged(object? sender, EventArgs e) => OnPropertyChanged(nameof(Status));

    private void SetStatus(string key)
    {
        _statusKey = key;
        OnPropertyChanged(nameof(Status));
    }
}
