using System.Globalization;
using System.Resources;
using DynamicIsland.Core.Modules;
using DynamicIsland.Core.Storage;
using DynamicIsland.UI.Localization;
using DynamicIsland.Modules.Translator;
using DynamicIsland.Modules.Translator.Services;

namespace DynamicIsland.Tests.Translator;

public sealed class TranslatorViewModelTests
{
    [Fact]
    public void EditingTextAndLanguages_DoesNotCallProvider()
    {
        var source = new DeferredService();
        using var environment = new TestEnvironment(source);
        var viewModel = environment.ViewModel;
        viewModel.Input = "hello";
        viewModel.SourceLanguage = "en";
        viewModel.TargetLanguage = "ru";
        Assert.Equal(0, source.Calls);
        Assert.True(viewModel.TranslateCommand.CanExecute(null));
    }

    [Fact]
    public async Task EditDuringRequest_CancelsAndRejectsLateResponse()
    {
        var service = new DeferredService();
        using var environment = new TestEnvironment(service);
        var viewModel = environment.ViewModel;
        viewModel.Input = "first";
        var pending = viewModel.TranslateCommand.ExecuteAsync(null);
        Assert.True(viewModel.IsBusy);
        viewModel.Input = "second";
        Assert.True(service.Token.IsCancellationRequested);
        service.Response.SetResult(new TranslationResult("stale result", "en"));
        await pending;
        Assert.Empty(viewModel.Output);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task SwapUsesResultAndReversesKnownLanguages()
    {
        var service = new DeferredService();
        using var environment = new TestEnvironment(service);
        var viewModel = environment.ViewModel;
        viewModel.Input = "hello";
        viewModel.SourceLanguage = "en";
        viewModel.TargetLanguage = "ru";
        var pending = viewModel.TranslateCommand.ExecuteAsync(null);
        service.Response.SetResult(new TranslationResult("привет", "en"));
        await pending;
        viewModel.SwapCommand.Execute(null);
        Assert.Equal("ru", viewModel.SourceLanguage);
        Assert.Equal("en", viewModel.TargetLanguage);
        Assert.Equal("привет", viewModel.Input);
        Assert.Empty(viewModel.Output);
    }

    [Fact]
    public void MissingPackage_DisablesTranslationAndOpensPackageManager()
    {
        using var environment = new TestEnvironment(new DeferredService(), installed: false);
        var viewModel = environment.ViewModel;
        viewModel.Input = "Hello";
        Assert.True(viewModel.NeedsPackage);
        Assert.False(viewModel.TranslateCommand.CanExecute(null));
        viewModel.DownloadCommand.Execute(null);
        Assert.Equal("translator", environment.Host.SettingsModule);
    }

    [Fact]
    public async Task ShutdownCancelsAndWaitsForAProviderIgnoringCancellation()
    {
        var service = new DeferredService();
        using var environment = new TestEnvironment(service);
        var viewModel = environment.ViewModel;
        viewModel.Input = "Hello";
        var translation = viewModel.TranslateCommand.ExecuteAsync(null);
        var shutdown = viewModel.ShutdownAsync();
        Assert.True(service.Token.IsCancellationRequested);
        Assert.False(shutdown.IsCompleted);
        service.Response.SetResult(new TranslationResult("Late", "en"));
        await shutdown;
        await translation;
        Assert.Empty(viewModel.Output);
        Assert.False(viewModel.TranslateCommand.CanExecute(null));
    }

    [Fact]
    public void LanguageChoiceNeverDisplaysInternalRecordProperties()
    {
        Loc.Instance.Register(new ResourceManager("DynamicIsland.Modules.Translator.Resources.Strings", typeof(TranslatorModule).Assembly));
        using var environment = new TestEnvironment(new DeferredService());
        foreach (var choice in environment.ViewModel.SourceLanguages)
        {
            Assert.DoesNotContain("Code", choice.ToString(), StringComparison.Ordinal);
            Assert.Equal(choice.Name, choice.ToString());
        }
    }

    [Fact]
    public void AllTranslationMessagesExistInEnglishAndRussian()
    {
        var resources = new ResourceManager("DynamicIsland.Modules.Translator.Resources.Strings", typeof(TranslatorModule).Assembly);
        var english = resources.GetResourceSet(CultureInfo.GetCultureInfo("en"), true, false)!;
        var russian = resources.GetResourceSet(CultureInfo.GetCultureInfo("ru"), true, false)!;
        foreach (System.Collections.DictionaryEntry entry in english)
        {
            Assert.False(string.IsNullOrWhiteSpace(russian.GetString((string)entry.Key)));
        }

        Assert.Equal("Translator", resources.GetString("Translator.Title", CultureInfo.GetCultureInfo("en")));
        Assert.Equal("Переводчик", resources.GetString("Translator.Title", CultureInfo.GetCultureInfo("ru")));
    }

    private sealed class DeferredService : ITranslationService
    {
        public int Calls { get; private set; }

        public CancellationToken Token { get; private set; }

        public TaskCompletionSource<TranslationResult> Response { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<TranslationResult> TranslateAsync(string text, string? sourceLanguage, string targetLanguage, CancellationToken cancellationToken)
        {
            Calls++;
            Token = cancellationToken;
            return Response.Task;
        }
    }

    private sealed class TestEnvironment : IDisposable
    {
        private readonly TranslationHistoryService _history;
        private readonly TranslationPackagesViewModel _packages;
        public TestEnvironment(ITranslationService service, bool installed = true)
        {
            Host = new Host();
            _history = new TranslationHistoryService(new MemoryStorage(), TimeProvider.System);
            _packages = new TranslationPackagesViewModel(new Packages(installed));
            ViewModel = new TranslatorViewModel(service, Host, _history, _packages, TimeProvider.System);
        }
        public Host Host { get; }
        public TranslatorViewModel ViewModel { get; }
        public void Dispose() { ViewModel.Dispose(); _history.Dispose(); _packages.Dispose(); }
    }

    private sealed class Packages(bool installed) : ITranslationPackageManager
    {
        public IReadOnlyList<TranslationPackage> AvailablePackages => [];
        public bool IsInstalled(string sourceLanguage, string targetLanguage) => installed;
        public string GetInstalledPath(string sourceLanguage, string targetLanguage) => string.Empty;
        public Task InstallAsync(string sourceLanguage, string targetLanguage, IProgress<TranslationPackageProgress>? progress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task RemoveAsync(string sourceLanguage, string targetLanguage, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class MemoryStorage : IStorageService
    {
        public string RootPath => string.Empty;
        public string ResolvePath(string relativePath) => relativePath;
        public Task<T?> LoadAsync<T>(string relativePath, CancellationToken cancellationToken = default) where T : class => Task.FromResult<T?>(null);
        public Task SaveAsync<T>(string relativePath, T value, CancellationToken cancellationToken = default) where T : class => Task.CompletedTask;
    }

    private sealed class Host : IIslandHost
    {
        public string? SettingsModule { get; private set; }
        public void RequestKeyboard() { }
        public void ReleaseKeyboard() { }
        public void OpenSettings(string? moduleId = null) => SettingsModule = moduleId;
    }
}
