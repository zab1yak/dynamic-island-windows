using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using DynamicIsland.Modules.Translator.Services;

namespace DynamicIsland.Tests.Translator;

internal sealed class MemoryCredentials : ITranslationCredentialStore
{
    public TranslationCredentials? Value { get; set; } = new("test-key", true);

    public TranslationCredentials? Read() => Value;

    public void Write(TranslationCredentials credentials) => Value = credentials;

    public void Delete() => Value = null;
}

public sealed class TranslationServiceTests
{
    [Fact]
    public async Task Translate_UsesOfficialEndpointApiKeyAndPlainText_NoFolderForServiceAccount()
    {
        using var handler = new Handler(async (request, token) =>
        {
            Assert.Equal("https://translate.api.cloud.yandex.net/translate/v2/translate", request.RequestUri!.AbsoluteUri);
            Assert.Equal("Api-Key", request.Headers.Authorization!.Scheme);
            Assert.Equal("test-key", request.Headers.Authorization.Parameter);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            Assert.Equal("PLAIN_TEXT", body.RootElement.GetProperty("format").GetString());
            Assert.Equal("Hello\n<world>", body.RootElement.GetProperty("texts")[0].GetString());
            Assert.Equal("ru", body.RootElement.GetProperty("targetLanguageCode").GetString());
            Assert.False(body.RootElement.TryGetProperty("folderId", out _));
            Assert.False(body.RootElement.TryGetProperty("sourceLanguageCode", out _));
            return Json("{\"translations\":[{\"text\":\"Привет\\n<мир>\",\"detectedLanguageCode\":\"en\"}]}");
        });
        using var client = new HttpClient(handler);
        var service = new YandexTranslationService(client, new MemoryCredentials());
        var translated = await service.TranslateAsync("Hello\n<world>", null, "ru", TestContext.Current.CancellationToken);
        Assert.Equal("Привет\n<мир>", translated.Text);
        Assert.Equal("en", translated.DetectedLanguage);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    public async Task MissingCredentialsOrConsent_NeverSendsRequest(bool hasKey, bool consent)
    {
        using var handler = new Handler((_, _) => throw new InvalidOperationException("Must not send."));
        using var client = new HttpClient(handler);
        var store = new MemoryCredentials { Value = hasKey ? new TranslationCredentials("key", consent) : null };
        var service = new YandexTranslationService(client, store);
        var exception = await Assert.ThrowsAsync<TranslationException>(() => service.TranslateAsync("Hello", "en", "ru", TestContext.Current.CancellationToken));
        Assert.Equal(TranslationFailure.NotConfigured, exception.Failure);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, TranslationFailure.Authentication)]
    [InlineData(HttpStatusCode.Forbidden, TranslationFailure.Authentication)]
    [InlineData(HttpStatusCode.TooManyRequests, TranslationFailure.Quota)]
    [InlineData(HttpStatusCode.PaymentRequired, TranslationFailure.Quota)]
    [InlineData(HttpStatusCode.ServiceUnavailable, TranslationFailure.Unavailable)]
    public async Task ApiErrors_AreSanitized(HttpStatusCode status, TranslationFailure expected)
    {
        using var handler = new Handler((_, _) => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent("secret key and personal input") }));
        using var client = new HttpClient(handler);
        var service = new YandexTranslationService(client, new MemoryCredentials());
        var exception = await Assert.ThrowsAsync<TranslationException>(() => service.TranslateAsync("private input", "en", "ru", TestContext.Current.CancellationToken));
        Assert.Equal(expected, exception.Failure);
        Assert.DoesNotContain("private input", exception.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("secret key", exception.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"translations\":[]}")]
    [InlineData("{\"translations\":[{}]}")]
    public async Task InvalidResponses_AreRejected(string body)
    {
        using var handler = new Handler((_, _) => Task.FromResult(Json(body)));
        using var client = new HttpClient(handler);
        var service = new YandexTranslationService(client, new MemoryCredentials());
        var exception = await Assert.ThrowsAsync<TranslationException>(() => service.TranslateAsync("hello", "en", "ru", TestContext.Current.CancellationToken));
        Assert.Equal(TranslationFailure.InvalidResponse, exception.Failure);
    }

    [Fact]
    public async Task LongInput_IsRejectedBeforeNetwork()
    {
        using var handler = new Handler((_, _) => throw new InvalidOperationException("Must not send."));
        using var client = new HttpClient(handler);
        var service = new YandexTranslationService(client, new MemoryCredentials());
        var exception = await Assert.ThrowsAsync<TranslationException>(() => service.TranslateAsync(new string('a', 10001), "en", "ru", TestContext.Current.CancellationToken));
        Assert.Equal(TranslationFailure.InvalidInput, exception.Failure);
    }

    [Fact]
    public async Task CallerCancellation_IsNotReportedAsNetworkFailure()
    {
        using var handler = new Handler(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Json("{}");
        });
        using var client = new HttpClient(handler);
        var service = new YandexTranslationService(client, new MemoryCredentials());
        using var cancellation = new CancellationTokenSource();
        var pending = service.TranslateAsync("hello", "en", "ru", cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    [Fact]
    public async Task NetworkFailure_DoesNotExposeTransportDetails()
    {
        using var handler = new Handler((_, _) => throw new HttpRequestException("key or private input in transport details"));
        using var client = new HttpClient(handler);
        var service = new YandexTranslationService(client, new MemoryCredentials());
        var exception = await Assert.ThrowsAsync<TranslationException>(() => service.TranslateAsync("hello", "en", "ru", TestContext.Current.CancellationToken));
        Assert.Equal(TranslationFailure.Unavailable, exception.Failure);
        Assert.Null(exception.InnerException);
        Assert.DoesNotContain("transport details", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void CredentialDisplay_RedactsSecret() => Assert.DoesNotContain("key-secret", new TranslationCredentials("key-secret", true).ToString(), StringComparison.Ordinal);

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }
}
