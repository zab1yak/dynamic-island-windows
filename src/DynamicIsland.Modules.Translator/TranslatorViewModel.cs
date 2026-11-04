using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DynamicIsland.Core.Modules;
using DynamicIsland.Modules.Translator.Services;
using DynamicIsland.UI.Localization;

namespace DynamicIsland.Modules.Translator;

public sealed class TranslationLanguage(string code, string nameKey) : ObservableObject
{
    public string Code { get; } = code;
    public string NameKey { get; } = nameKey;
    public string Name => Loc.Instance[NameKey];
    public void RefreshName() => OnPropertyChanged(nameof(Name));
    public override string ToString() => Name;
}

/// <summary>Explicit local translation; editing text never starts a translation or network request.</summary>
public sealed partial class TranslatorViewModel : ObservableObject, IDisposable
{
    private readonly ITranslationService _service;
    private readonly IIslandHost _host;
    private readonly TranslationHistoryService _history;
    private CancellationTokenSource? _request;
    private readonly object _translationGate = new();
    private int _activeTranslations;
    private TaskCompletionSource? _translationsDrained;
    private int _generation;
    private string _statusKey = "Translator.Manual";
    private bool _loadingHistory;
    private bool _shuttingDown;
    private readonly TimeProvider _time;
    private DateTimeOffset _confirmClearUntil;
    private readonly IReadOnlyList<TranslationLanguage> _languages = CreateLanguages();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TranslateCommand))]
    private string _input = string.Empty;
    [ObservableProperty] private string _output = string.Empty;
    [ObservableProperty] private string _sourceLanguage = "en";
    [ObservableProperty] private string _targetLanguage = "ru";
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TranslateCommand))]
    private bool _isBusy;
    [ObservableProperty] private bool _saveHistory;
    [ObservableProperty] private bool _confirmClearHistory;

    public TranslatorViewModel(ITranslationService service, IIslandHost host, TranslationHistoryService history, TranslationPackagesViewModel packages, TimeProvider timeProvider)
    {
        _time = timeProvider;
        _service = service;
        _host = host;
        _history = history;
        Packages = packages;
        Loc.Instance.CultureChanged += OnCultureChanged;
        Packages.PackagesChanged += OnPackagesChanged;
    }

    public TranslationPackagesViewModel Packages { get; }
    public ObservableCollection<TranslationHistoryEntry> History { get; } = [];
    private static IReadOnlyList<TranslationLanguage> CreateLanguages() => [new("en", "Translator.English"), new("ru", "Translator.Russian")];
    public IReadOnlyList<TranslationLanguage> SourceLanguages => _languages;
    public IReadOnlyList<TranslationLanguage> TargetLanguages => SourceLanguages;
    public bool NeedsPackage => SourceLanguage != TargetLanguage && !Packages.IsInstalled(SourceLanguage, TargetLanguage);
    public string Status => Loc.Instance[NeedsPackage && !IsBusy && _statusKey == "Translator.Manual" ? "Translator.PackageMissing" : _statusKey];
    public string SelectedPackageDetails => Packages.Packages.FirstOrDefault(package => package.SourceLanguage == SourceLanguage && package.TargetLanguage == TargetLanguage)?.Details ?? string.Empty;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _history.LoadAsync(cancellationToken);
            _loadingHistory = true;
            SaveHistory = _history.Enabled;
            _loadingHistory = false;
            RefreshHistory();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { SetStatus("Translator.HistoryError"); }
    }

    public void SetTyping(bool typing)
    {
        if (typing) { _host.RequestKeyboard(); } else { _host.ReleaseKeyboard(); }
    }

    private bool CanTranslate() => !_shuttingDown && !IsBusy && !Packages.IsBusy && !NeedsPackage && !string.IsNullOrWhiteSpace(Input) && Input.Length <= 2000;

    [RelayCommand(CanExecute = nameof(CanTranslate), AllowConcurrentExecutions = true)]
    private async Task TranslateAsync()
    {
        if (_shuttingDown) { return; }
        lock (_translationGate)
        {
            if (_activeTranslations++ == 0) { _translationsDrained = new(TaskCreationOptions.RunContinuationsAsynchronously); }
        }
        CancelPending();
        using var cancellation = new CancellationTokenSource();
        _request = cancellation;
        var generation = _generation;
        var input = Input;
        var source = SourceLanguage;
        var target = TargetLanguage;
        IsBusy = true;
        Output = string.Empty;
        SetStatus("Translator.Working");
        try
        {
            var translated = await _service.TranslateAsync(input, source, target, cancellation.Token);
            if (generation == _generation)
            {
                Output = translated.Text;
                SetStatus("Translator.Ready");
                try
                {
                    await _history.AddAsync(source, target, input, Output, cancellation.Token);
                    RefreshHistory();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { SetStatus("Translator.HistoryError"); }
            }
        }
        catch (OperationCanceledException) { if (generation == _generation) { SetStatus("Translator.Cancelled"); } }
        catch (TranslationException ex) { if (generation == _generation) { SetStatus("Translator." + ex.Failure); } }
        finally
        {
            if (ReferenceEquals(_request, cancellation)) { _request = null; IsBusy = false; }
            lock (_translationGate)
            {
                if (--_activeTranslations == 0) { _translationsDrained?.TrySetResult(); }
            }
        }
    }

    [RelayCommand] private void Download() => _host.OpenSettings("translator");
    [RelayCommand] private void Cancel() { CancelPending(); SetStatus("Translator.Cancelled"); }
    [RelayCommand] private void Clear() { Input = string.Empty; Output = string.Empty; }
    [RelayCommand] private void Swap()
    {
        var source = SourceLanguage;
        var output = Output;
        SourceLanguage = TargetLanguage;
        TargetLanguage = source;
        if (!string.IsNullOrWhiteSpace(output)) { Input = output; }
        Output = string.Empty;
    }
    [RelayCommand] private void OpenSettings() => _host.OpenSettings("translator");
    [RelayCommand] private void Restore(TranslationHistoryEntry? entry)
    {
        if (entry is null) { return; }
        SourceLanguage = entry.SourceLanguage;
        TargetLanguage = entry.TargetLanguage;
        Input = entry.Input;
        Output = entry.Output;
        SetStatus("Translator.Ready");
    }
    [RelayCommand] private async Task DeleteHistoryAsync(TranslationHistoryEntry? entry)
    {
        if (_shuttingDown || entry is null) { return; }
        try { await _history.DeleteAsync(entry.Id); RefreshHistory(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { SetStatus("Translator.HistoryError"); }
    }
    [RelayCommand] private async Task ClearHistoryAsync()
    {
        if (_shuttingDown) { return; }
        var now = _time.GetUtcNow();
        if (!ConfirmClearHistory || now > _confirmClearUntil)
        {
            _confirmClearUntil = now.AddSeconds(4);
            ConfirmClearHistory = true;
            return;
        }
        try { await _history.ClearAsync(); RefreshHistory(); ConfirmClearHistory = false; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { SetStatus("Translator.HistoryError"); }
    }
    [RelayCommand] private async Task SaveHistoryPreferenceAsync()
    {
        if (_shuttingDown) { return; }
        try
        {
            bool desired;
            do
            {
                desired = SaveHistory;
                await _history.SetEnabledAsync(desired);
            }
            while (desired != SaveHistory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _loadingHistory = true; SaveHistory = _history.Enabled; _loadingHistory = false;
            SetStatus("Translator.HistoryError");
        }
    }
    partial void OnSaveHistoryChanged(bool value)
    {
        if (!_loadingHistory && !_shuttingDown) { SaveHistoryPreferenceCommand.Execute(null); }
    }
    partial void OnInputChanged(string value) => Invalidate();
    partial void OnSourceLanguageChanged(string value) => Invalidate();
    partial void OnTargetLanguageChanged(string value) => Invalidate();
    private void Invalidate() { CancelPending(); Output = string.Empty; SetStatus("Translator.Manual"); OnPropertyChanged(nameof(NeedsPackage)); OnPropertyChanged(nameof(SelectedPackageDetails)); TranslateCommand.NotifyCanExecuteChanged(); }
    private void CancelPending() { _generation++; _request?.Cancel(); _request = null; IsBusy = false; }
    private void SetStatus(string key) { _statusKey = key; OnPropertyChanged(nameof(Status)); }
    private void RefreshHistory() { History.Clear(); foreach (var entry in _history.Entries) { History.Add(entry); } }
    private void OnPackagesChanged(object? sender, EventArgs e) { OnPropertyChanged(nameof(NeedsPackage)); SetStatus(Packages.StatusKey); TranslateCommand.NotifyCanExecuteChanged(); }
    private void OnCultureChanged(object? sender, EventArgs e) { foreach (var language in _languages) { language.RefreshName(); } OnPropertyChanged(nameof(Status)); }
    public async Task ShutdownAsync()
    {
        _shuttingDown = true;
        TranslateCommand.NotifyCanExecuteChanged();
        var pending = new[] { TranslateCommand.ExecutionTask, DeleteHistoryCommand.ExecutionTask, ClearHistoryCommand.ExecutionTask, SaveHistoryPreferenceCommand.ExecutionTask }.OfType<Task>().ToArray();
        CancelPending();
        Task translations;
        lock (_translationGate) { translations = _translationsDrained?.Task ?? Task.CompletedTask; }
        await Packages.ShutdownAsync();
        try { await Task.WhenAll(pending.Append(translations)); }
        catch (OperationCanceledException) { /* Shutdown cancellation is expected. */ }
        Dispose();
    }

    public void Dispose()
    {
        CancelPending();
        Packages.PackagesChanged -= OnPackagesChanged;
        Loc.Instance.CultureChanged -= OnCultureChanged;
    }
}
