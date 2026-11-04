using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DynamicIsland.Core.Modules;
using DynamicIsland.Modules.Translator.Services;
using DynamicIsland.UI.Localization;

namespace DynamicIsland.Modules.Translator;

public sealed record TranslationLanguage(string Code, string NameKey)
{
    public string Name => Loc.Instance[NameKey];
}

/// <summary>Manual translation only: editing input never sends text to a third party.</summary>
public sealed partial class TranslatorViewModel : ObservableObject, IDisposable
{
    private readonly ITranslationService _service;
    private readonly IIslandHost _host;
    private CancellationTokenSource? _request;
    private int _generation;
    private string _statusKey = "Translator.Configure";
    private IReadOnlyList<TranslationLanguage> _targets = CreateLanguages();
    private IReadOnlyList<TranslationLanguage> _sources = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TranslateCommand))]
    private string _input = string.Empty;

    [ObservableProperty]
    private string _output = string.Empty;

    [ObservableProperty]
    private string _sourceLanguage = string.Empty;

    [ObservableProperty]
    private string _targetLanguage = "en";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TranslateCommand))]
    private bool _isBusy;

    public TranslatorViewModel(ITranslationService service, IIslandHost host)
    {
        _service = service;
        _host = host;
        RefreshLanguages();
        Loc.Instance.CultureChanged += OnCultureChanged;
    }

    private static IReadOnlyList<TranslationLanguage> CreateLanguages() =>
    [
        new("ru", "Translator.Russian"), new("en", "Translator.English"),
        new("de", "Translator.German"), new("fr", "Translator.French"),
        new("es", "Translator.Spanish"), new("zh", "Translator.Chinese"),
    ];

    public IReadOnlyList<TranslationLanguage> TargetLanguages => _targets;

    public IReadOnlyList<TranslationLanguage> SourceLanguages => _sources;

    public string Status => Loc.Instance[_statusKey];

    public void SetConfigured(bool configured) => SetStatus(configured ? "Translator.Manual" : "Translator.Configure");

    public void SetTyping(bool typing)
    {
        if (typing)
        {
            _host.RequestKeyboard();
        }
        else
        {
            _host.ReleaseKeyboard();
        }
    }

    public void Dispose()
    {
        CancelPending();
        Loc.Instance.CultureChanged -= OnCultureChanged;
    }

    private bool CanTranslate() => !IsBusy && !string.IsNullOrWhiteSpace(Input) && Input.Length <= YandexTranslationService.MaximumCharacters;

    [RelayCommand(CanExecute = nameof(CanTranslate), AllowConcurrentExecutions = true)]
    private async Task TranslateAsync()
    {
        CancelPending();
        using var cancellation = new CancellationTokenSource();
        _request = cancellation;
        var generation = _generation;
        IsBusy = true;
        Output = string.Empty;
        SetStatus("Translator.Working");
        try
        {
            var translated = await _service.TranslateAsync(Input, string.IsNullOrEmpty(SourceLanguage) ? null : SourceLanguage, TargetLanguage, cancellation.Token);
            if (generation == _generation)
            {
                Output = translated.Text;
                SetStatus("Translator.Ready");
            }
        }
        catch (OperationCanceledException)
        {
            if (generation == _generation)
            {
                SetStatus("Translator.Cancelled");
            }
        }
        catch (TranslationException ex)
        {
            if (generation == _generation)
            {
                SetStatus("Translator." + ex.Failure);
            }
        }
        catch (Win32Exception)
        {
            if (generation == _generation)
            {
                SetStatus("Translator.CredentialError");
            }
        }
        finally
        {
            if (ReferenceEquals(_request, cancellation))
            {
                _request = null;
                IsBusy = false;
            }
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        CancelPending();
        SetStatus("Translator.Cancelled");
    }

    [RelayCommand]
    private void Swap()
    {
        var previous = SourceLanguage;
        var previousOutput = Output;
        SourceLanguage = TargetLanguage;
        TargetLanguage = string.IsNullOrEmpty(previous) ? (TargetLanguage == "en" ? "ru" : "en") : previous;
        if (!string.IsNullOrWhiteSpace(previousOutput))
        {
            Input = previousOutput;
        }

        Output = string.Empty;
    }

    [RelayCommand]
    private void OpenSettings() => _host.OpenSettings("translator");

    partial void OnInputChanged(string value) => Invalidate();

    partial void OnSourceLanguageChanged(string value) => Invalidate();

    partial void OnTargetLanguageChanged(string value) => Invalidate();

    private void Invalidate()
    {
        CancelPending();
        Output = string.Empty;
        SetStatus("Translator.Manual");
    }

    private void CancelPending()
    {
        _generation++;
        _request?.Cancel();
        _request = null;
        IsBusy = false;
    }

    private void SetStatus(string key)
    {
        _statusKey = key;
        OnPropertyChanged(nameof(Status));
    }

    private void OnCultureChanged(object? sender, EventArgs e)
    {
        RefreshLanguages();
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(SourceLanguages));
        OnPropertyChanged(nameof(TargetLanguages));
    }

    private void RefreshLanguages()
    {
        _targets = CreateLanguages();
        _sources = new[] { new TranslationLanguage(string.Empty, "Translator.Auto") }.Concat(_targets).ToArray();
    }
}
