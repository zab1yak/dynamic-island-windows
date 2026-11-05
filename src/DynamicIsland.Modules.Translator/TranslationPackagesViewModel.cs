using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DynamicIsland.Modules.Translator.Services;
using DynamicIsland.UI.Localization;

namespace DynamicIsland.Modules.Translator;

/// <summary>One cancellable package operation shared by the page and settings.</summary>
public sealed partial class TranslationPackagesViewModel : ObservableObject, IDisposable
{
    private readonly ITranslationPackageManager _manager;
    private CancellationTokenSource? _operation;
    private bool _shuttingDown;
    private string _statusKey = "Translator.PackageHelp";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DownloadCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveCommand))]
    [NotifyCanExecuteChangedFor(nameof(RollbackCommand))]
    private bool _isBusy;
    [ObservableProperty] private double _progress;
    public ObservableCollection<TranslationPackageItem> Packages { get; } = [];
    public string Status => Loc.Instance[_statusKey];
    public string StatusKey => _statusKey;
    public event EventHandler? PackagesChanged;

    public TranslationPackagesViewModel(ITranslationPackageManager manager)
    {
        _manager = manager;
        Refresh();
        Loc.Instance.CultureChanged += OnCultureChanged;
    }

    public bool IsInstalled(string source, string target) => _manager.IsInstalled(source, target);

    public void Refresh()
    {
        Packages.Clear();
        foreach (var package in _manager.AvailablePackages)
        {
            Packages.Add(new TranslationPackageItem(package, _manager.IsInstalled(package.SourceLanguage, package.TargetLanguage),
                _manager.GetInstalledRevision(package.SourceLanguage, package.TargetLanguage),
                _manager.IsUpdateAvailable(package.SourceLanguage, package.TargetLanguage),
                _manager.CanRollback(package.SourceLanguage, package.TargetLanguage)));
        }
        PackagesChanged?.Invoke(this, EventArgs.Empty);
    }

    private bool CanOperate(TranslationPackageItem? package) => !_shuttingDown && !IsBusy && package is not null;

    [RelayCommand(CanExecute = nameof(CanOperate))]
    private Task DownloadAsync(TranslationPackageItem? package) => package is null ? Task.CompletedTask : InstallAsync(package.SourceLanguage, package.TargetLanguage);

    public async Task InstallAsync(string source, string target)
    {
        if (_shuttingDown || IsBusy) { return; }
        using var cancellation = new CancellationTokenSource();
        _operation = cancellation;
        IsBusy = true;
        Progress = 0;
        SetStatus("Translator.Downloading");
        try
        {
            var progress = new Progress<TranslationPackageProgress>(value => Progress = value.TotalBytes > 0 ? 100.0 * value.DownloadedBytes / value.TotalBytes : 0);
            await _manager.InstallAsync(source, target, progress, cancellation.Token);
            SetStatus("Translator.PackageReady");
        }
        catch (OperationCanceledException) { SetStatus("Translator.Cancelled"); }
        catch (Exception ex) when (ex is IOException or System.Net.Http.HttpRequestException or UnauthorizedAccessException or InvalidDataException or TranslationException)
        {
            SetStatus("Translator.DownloadError");
        }
        finally
        {
            _operation = null;
            IsBusy = false;
            Refresh();
        }
    }

    [RelayCommand(CanExecute = nameof(CanOperate))]
    private async Task RemoveAsync(TranslationPackageItem? package)
    {
        if (package is null) { return; }
        IsBusy = true;
        try
        {
            await _manager.RemoveAsync(package.SourceLanguage, package.TargetLanguage, CancellationToken.None);
            SetStatus("Translator.PackageRemoved");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TranslationException)
        {
            SetStatus("Translator.PackageError");
        }
        finally { IsBusy = false; Refresh(); }
    }

    private bool CanRollback(TranslationPackageItem? package) => CanOperate(package) && package!.RollbackAvailable;

    [RelayCommand(CanExecute = nameof(CanRollback))]
    private async Task RollbackAsync(TranslationPackageItem? package)
    {
        if (package is null) { return; }
        using var cancellation = new CancellationTokenSource();
        _operation = cancellation;
        IsBusy = true;
        try
        {
            await _manager.RollbackAsync(package.SourceLanguage, package.TargetLanguage, cancellation.Token);
            SetStatus("Translator.RolledBack");
        }
        catch (OperationCanceledException) { SetStatus("Translator.Cancelled"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or TranslationException)
        {
            SetStatus("Translator.RollbackError");
        }
        finally { _operation = null; IsBusy = false; Refresh(); }
    }

    [RelayCommand] private void Cancel() => _operation?.Cancel();
    private void SetStatus(string key) { _statusKey = key; OnPropertyChanged(nameof(Status)); }
    private void OnCultureChanged(object? sender, EventArgs e) { Refresh(); OnPropertyChanged(nameof(Status)); }
    public async Task ShutdownAsync()
    {
        _shuttingDown = true;
        var pending = new[] { DownloadCommand.ExecutionTask, RemoveCommand.ExecutionTask, RollbackCommand.ExecutionTask }.OfType<Task>().ToArray();
        _operation?.Cancel();
        try { await Task.WhenAll(pending); }
        catch (OperationCanceledException) { /* Expected when cancelling a download. */ }
    }

    public void Dispose() { _operation?.Cancel(); Loc.Instance.CultureChanged -= OnCultureChanged; }
}

public sealed record TranslationPackageItem(TranslationPackage Package, bool Installed, string? InstalledRevision = null, bool UpdateAvailable = false, bool RollbackAvailable = false)
{
    public string SourceLanguage => Package.SourceLanguage;
    public string TargetLanguage => Package.TargetLanguage;
    public string Name => Loc.Instance["Translator." + (SourceLanguage == "ru" ? "Russian" : "English")] + " → " + Loc.Instance["Translator." + (TargetLanguage == "ru" ? "Russian" : "English")];
    public string DownloadLabel => Loc.Instance[UpdateAvailable ? "Translator.Update" : "Translator.Download"];
    public string Details => $"{Package.ModelName} · {Package.Revision} · {Package.DownloadBytes / 1000000.0:F1} MB · {new Uri(Package.SourceUrl).Host} · {Package.License}";
    public string State => Loc.Instance[Installed ? "Translator.Installed" : "Translator.NotInstalled"]
        + (Installed ? $" · {InstalledRevision ?? Loc.Instance["Translator.LegacyPackage"]}" : "")
        + (UpdateAvailable ? " · " + Loc.Instance["Translator.UpdateAvailable"] : "");
}
