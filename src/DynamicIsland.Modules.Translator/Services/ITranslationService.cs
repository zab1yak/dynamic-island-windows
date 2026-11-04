namespace DynamicIsland.Modules.Translator.Services;

/// <summary>Credentials for the user's own Yandex Cloud service account. Never log this object.</summary>
public sealed class TranslationCredentials(string apiKey, bool consent)
{
    public string ApiKey { get; } = apiKey;

    public bool Consent { get; } = consent;

    public override string ToString() => "TranslationCredentials [redacted]";
}

/// <summary>Stores the optional secret in the current Windows user's credential vault.</summary>
public interface ITranslationCredentialStore
{
    TranslationCredentials? Read();

    void Write(TranslationCredentials credentials);

    void Delete();
}

public sealed record TranslationResult(string Text, string? DetectedLanguage);

public enum TranslationFailure
{
    NotConfigured,
    InvalidInput,
    Authentication,
    Quota,
    Unavailable,
    InvalidResponse,
}

/// <summary>Sanitized failure: contains neither the API key nor submitted text nor server payload.</summary>
public sealed class TranslationException(TranslationFailure failure) : Exception("Translation request failed.")
{
    public TranslationFailure Failure { get; } = failure;
}

public interface ITranslationService
{
    Task<TranslationResult> TranslateAsync(string text, string? sourceLanguage, string targetLanguage, CancellationToken cancellationToken);
}
