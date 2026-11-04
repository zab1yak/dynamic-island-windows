using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;

namespace DynamicIsland.Modules.Translator.Services;

/// <summary>Local Marian/OPUS inference. No network operation or process launch occurs here.</summary>
public sealed class OfflineTranslationService(ITranslationPackageManager packages) : ITranslationService, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _disposed;

    public async Task<TranslationResult> TranslateAsync(string text, string? sourceLanguage, string targetLanguage, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(text) || text.Length > 2000)
            throw new TranslationException(TranslationFailure.InvalidInput);
        sourceLanguage ??= text.Any(c => c is >= '\u0400' and <= '\u052f') ? "ru" : "en";
        if (sourceLanguage == targetLanguage && sourceLanguage is "en" or "ru") return new(text, sourceLanguage);
        var model = TranslationModelCatalog.Find(sourceLanguage, targetLanguage)
            ?? throw new TranslationException(TranslationFailure.InvalidInput);
        var path = packages.GetInstalledPath(sourceLanguage, targetLanguage);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            return await Task.Run(() => Translate(text, model, path, cancellationToken), cancellationToken).ConfigureAwait(false);
        }
        catch (OnnxRuntimeException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new TranslationException(TranslationFailure.Unavailable);
        }
        catch (IOException) { throw new TranslationException(TranslationFailure.NotConfigured); }
        catch (JsonException) { throw new TranslationException(TranslationFailure.InvalidResponse); }
        catch (DllNotFoundException) { throw new TranslationException(TranslationFailure.Unavailable); }
        catch (BadImageFormatException) { throw new TranslationException(TranslationFailure.Unavailable); }
        finally { _gate.Release(); }
    }

    private static TranslationResult Translate(string text, TranslationModel model, string path, CancellationToken cancellationToken)
    {
        // Verify every installed data file before handing it to a native parser. Models contain no custom operators.
        TranslationPackageManager.RejectReparsePoint(path);
        foreach (var file in model.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var filePath = Path.Combine(path, file.LocalName);
            TranslationPackageManager.RejectReparsePoint(filePath);
            using var input = File.OpenRead(filePath);
            if (input.Length != file.Size || !Convert.ToHexString(SHA256.HashData(input)).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new TranslationException(TranslationFailure.NotConfigured);
        }
        using var sourceStream = File.OpenRead(Path.Combine(path, "source.spm"));
        using var targetStream = File.OpenRead(Path.Combine(path, "target.spm"));
        var sourceTokenizer = SentencePieceTokenizer.Create(sourceStream, false, false);
        var targetTokenizer = SentencePieceTokenizer.Create(targetStream, false, false);
        var vocabulary = JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(Path.Combine(path, "vocab.json")))
            ?? throw new TranslationException(TranslationFailure.InvalidResponse);
        var tokens = sourceTokenizer.EncodeToTokens(text, out _);
        if (tokens.Count > 255) throw new TranslationException(TranslationFailure.InvalidInput);
        var inputIds = tokens.Select(t => (long)vocabulary.GetValueOrDefault(t.Value, vocabulary["<unk>"])).Append(vocabulary["</s>"]).ToArray();
        using var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(path, "config.json")));
        var start = config.RootElement.GetProperty("decoder_start_token_id").GetInt32();
        var end = config.RootElement.GetProperty("eos_token_id").GetInt32();
        using var options = new SessionOptions
        {
            IntraOpNumThreads = 2,
            InterOpNumThreads = 1,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
        };
        // Sessions are lazy and scoped to this operation: no model memory or inference threads in idle state.
        using var encoder = new InferenceSession(Path.Combine(path, "encoder_model_quantized.onnx"), options);
        using var decoder = new InferenceSession(Path.Combine(path, "decoder_model_quantized.onnx"), options);
        using var run = new RunOptions();
        using var registration = cancellationToken.Register(() => run.Terminate = true);
        cancellationToken.ThrowIfCancellationRequested();
        var attention = new DenseTensor<long>(Enumerable.Repeat(1L, inputIds.Length).ToArray(), [1, inputIds.Length]);
        var encoderInputs = new[]
        {
            NamedOnnxValue.CreateFromTensor("input_ids", new DenseTensor<long>(inputIds, [1, inputIds.Length])),
            NamedOnnxValue.CreateFromTensor("attention_mask", attention),
        };
        using var encoded = encoder.Run(encoderInputs, ["last_hidden_state"], run);
        var hiddenState = encoded.First().AsTensor<float>();
        var generated = new List<long> { start };
        var decoded = new List<int>();
        var byId = vocabulary.ToDictionary(p => p.Value, p => p.Key);
        for (var step = 0; step < 256; step++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var decoderInputs = new[]
            {
                NamedOnnxValue.CreateFromTensor("input_ids", new DenseTensor<long>(generated.ToArray(), [1, generated.Count])),
                NamedOnnxValue.CreateFromTensor("encoder_hidden_states", hiddenState),
                NamedOnnxValue.CreateFromTensor("encoder_attention_mask", attention),
            };
            using var result = decoder.Run(decoderInputs, ["logits"], run);
            var logits = result.First().AsTensor<float>();
            var vocabularySize = logits.Dimensions[2];
            var bestId = 0;
            var bestScore = float.NegativeInfinity;
            for (var id = 0; id < vocabularySize; id++)
            {
                if (id == start) continue;
                var score = logits[0, generated.Count - 1, id];
                if (score > bestScore) { bestScore = score; bestId = id; }
            }
            if (bestId == end)
            {
                var output = targetTokenizer.Decode(decoded);
                if (string.IsNullOrWhiteSpace(output)) throw new TranslationException(TranslationFailure.InvalidResponse);
                return new(output, model.Source);
            }
            generated.Add(bestId);
            if (byId.TryGetValue(bestId, out var piece) && targetTokenizer.Vocabulary.TryGetValue(piece, out var targetId))
                decoded.Add(targetId);
            else decoded.Add(targetTokenizer.UnknownId);
        }
        // Never silently return a partial result when the generation budget is exceeded.
        throw new TranslationException(TranslationFailure.InvalidResponse);
    }

    // No wait handle is created for this managed async gate. Keeping it valid lets an already
    // cancelled/draining operation release it safely during emergency process shutdown.
    public void Dispose() => Interlocked.Exchange(ref _disposed, 1);
}
