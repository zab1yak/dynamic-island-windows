using System.Globalization;
using System.Resources;
using DynamicIsland.Core.Modules;
using DynamicIsland.Modules.Translator;
using DynamicIsland.Modules.Translator.Services;

namespace DynamicIsland.Tests.Translator;

public sealed class TranslatorViewModelTests
{
    [Fact]
    public void EditingTextAndLanguages_DoesNotCallProvider()
    {
        var source = new DeferredService();
        using var viewModel = new TranslatorViewModel(source, new Host());
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
        using var viewModel = new TranslatorViewModel(service, new Host()) { Input = "first" };
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
        using var viewModel = new TranslatorViewModel(service, new Host()) { Input = "hello", SourceLanguage = "en", TargetLanguage = "ru" };
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
    public void Settings_BlankFieldKeepsExistingKey_DisconnectDeletesIt()
    {
        var store = new MemoryCredentials();
        using var translator = new TranslatorViewModel(new DeferredService(), new Host());
        using var settings = new TranslatorSettingsViewModel(store, translator) { Consent = true };
        Assert.True(settings.SaveApiKey(string.Empty));
        Assert.Equal("test-key", store.Value!.ApiKey);
        settings.DisconnectCommand.Execute(null);
        Assert.Null(store.Value);
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

    private sealed class Host : IIslandHost
    {
        public void RequestKeyboard() { }

        public void ReleaseKeyboard() { }

        public void OpenSettings(string? moduleId = null) { }
    }
}
