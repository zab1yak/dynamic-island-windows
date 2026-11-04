using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DynamicIsland.Modules.Translator.Services;

/// <summary>Official Translate v2 adapter, using the user's service-account API key.</summary>
public sealed class YandexTranslationService(HttpClient client, ITranslationCredentialStore credentials) : ITranslationService
{
    public const int MaximumCharacters = 10000;

    private static readonly Uri Endpoint = new("https://translate.api.cloud.yandex.net/translate/v2/translate");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task<TranslationResult> TranslateAsync(string text, string? sourceLanguage, string targetLanguage, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > MaximumCharacters || !ValidLanguage(targetLanguage) || (sourceLanguage is not null && !ValidLanguage(sourceLanguage)))
        {
            throw new TranslationException(TranslationFailure.InvalidInput);
        }

        var access = credentials.Read();
        if (access is null || !access.Consent || string.IsNullOrWhiteSpace(access.ApiKey) || access.ApiKey.Any(char.IsControl))
        {
            throw new TranslationException(TranslationFailure.NotConfigured);
        }

        // Do not add a folderId: service-account requests use the account's own folder.
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Api-Key", access.ApiKey);
        request.Content = JsonContent.Create(new Request(sourceLanguage, targetLanguage, "PLAIN_TEXT", [text]), options: JsonOptions);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new TranslationException(response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => TranslationFailure.Authentication,
                    HttpStatusCode.TooManyRequests or HttpStatusCode.PaymentRequired => TranslationFailure.Quota,
                    _ => TranslationFailure.Unavailable,
                });
            }

            var result = await response.Content.ReadFromJsonAsync<Response>(JsonOptions, timeout.Token).ConfigureAwait(false);
            if (result?.Translations is not { Length: 1 } rows || rows[0]?.Text is not { } translated)
            {
                throw new TranslationException(TranslationFailure.InvalidResponse);
            }

            return new TranslationResult(translated, rows[0]!.DetectedLanguageCode);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TranslationException(TranslationFailure.Unavailable);
        }
        catch (HttpRequestException)
        {
            throw new TranslationException(TranslationFailure.Unavailable);
        }
        catch (JsonException)
        {
            throw new TranslationException(TranslationFailure.InvalidResponse);
        }
    }

    private static bool ValidLanguage(string code) => code.Length is 2 or 3 && code.All(c => c is >= 'a' and <= 'z');

    private sealed record Request(string? SourceLanguageCode, string TargetLanguageCode, string Format, string[] Texts);

    private sealed record Response(Row?[]? Translations);

    private sealed record Row(string? Text, string? DetectedLanguageCode);
}
