using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;

namespace DynamicIsland.Modules.Translator.Services;

/// <summary>Local Marian/OPUS inference. No network operation or process launch occurs here.</summary>
public sealed class OfflineTranslationService : ITranslationService, IDisposable
{
    private readonly ITranslationPackageManager packages;
    private readonly OfflineDecodingOptions? _decoding;

    public OfflineTranslationService(ITranslationPackageManager packages) { this.packages = packages; }

    internal OfflineTranslationService(ITranslationPackageManager packages, OfflineDecodingOptions decoding)
    {
        this.packages = packages;
        _decoding = decoding;
        if (decoding.BeamWidth is < 1 or > 4) throw new ArgumentOutOfRangeException(nameof(decoding));
    }

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
        _ = TranslationModelCatalog.Find(sourceLanguage, targetLanguage)
            ?? throw new TranslationException(TranslationFailure.InvalidInput);
        var path = packages.GetInstalledPath(sourceLanguage, targetLanguage);
        TranslationModel model;
        try
        {
            model = TranslationModelCatalog.FindInstalled(sourceLanguage, targetLanguage, path)
                ?? throw new TranslationException(TranslationFailure.NotConfigured);
        }
        catch (IOException) { throw new TranslationException(TranslationFailure.NotConfigured); }
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            return await Task.Run(() => Translate(text, model, path, _decoding ?? new(model.BeamWidth, model.SplitSentences), cancellationToken), cancellationToken).ConfigureAwait(false);
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

    private static TranslationResult Translate(string text, TranslationModel model, string path, OfflineDecodingOptions decoding, CancellationToken cancellationToken)
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
        var segments = decoding.SplitSentences ? TranslationSentenceSplitter.Split(text) : [text];
        var translations = new List<string>();
        foreach (var segment in segments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var tokens = sourceTokenizer.EncodeToTokens(segment, out _);
            if (tokens.Count > 255) throw new TranslationException(TranslationFailure.InvalidInput);
            IEnumerable<long> input = tokens.Select(t => (long)vocabulary.GetValueOrDefault(t.Value, vocabulary["<unk>"]));
            // Marian multilingual labels are vocabulary control tokens, never SentencePiece text.
            if (model.LanguagePrefix is not null) input = input.Prepend(vocabulary[model.LanguagePrefix]);
            var inputIds = input.Append(vocabulary["</s>"]).ToArray();
            var attention = new DenseTensor<long>(Enumerable.Repeat(1L, inputIds.Length).ToArray(), [1, inputIds.Length]);
            var encoderInputs = new[]
            {
                NamedOnnxValue.CreateFromTensor("input_ids", new DenseTensor<long>(inputIds, [1, inputIds.Length])),
                NamedOnnxValue.CreateFromTensor("attention_mask", attention),
            };
            using var encoded = encoder.Run(encoderInputs, ["last_hidden_state"], run);
            var hiddenState = encoded.First().AsTensor<float>();
            var ids = Generate(decoder, hiddenState, attention, start, end, decoding.BeamWidth, run, cancellationToken);
            var byId = vocabulary.ToDictionary(p => p.Value, p => p.Key);
            var decoded = ids.Select(id => byId.TryGetValue((int)id, out var piece) && targetTokenizer.Vocabulary.TryGetValue(piece, out var targetId)
                ? targetId : targetTokenizer.UnknownId);
            var output = targetTokenizer.Decode(decoded);
            if (string.IsNullOrWhiteSpace(output)) throw new TranslationException(TranslationFailure.InvalidResponse);
            translations.Add(output);
        }
        return new(string.Join(" ", translations), model.Source);
    }

    private sealed record Beam(long[] Tokens, double Score);

    private static long[] Generate(InferenceSession decoder, Tensor<float> hiddenState, Tensor<long> attention,
        int start, int end, int width, RunOptions run, CancellationToken cancellationToken)
    {
        var active = new List<Beam> { new([start], 0) };
        var completed = new List<Beam>();
        for (var step = 0; step < 256; step++)
        {
            var candidates = new List<Beam>();
            foreach (var beam in active)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var inputs = new[]
                {
                    NamedOnnxValue.CreateFromTensor("input_ids", new DenseTensor<long>(beam.Tokens, [1, beam.Tokens.Length])),
                    NamedOnnxValue.CreateFromTensor("encoder_hidden_states", hiddenState),
                    NamedOnnxValue.CreateFromTensor("encoder_attention_mask", attention),
                };
                using var result = decoder.Run(inputs, ["logits"], run);
                var logits = result.First().AsTensor<float>();
                var size = logits.Dimensions[2];
                var top = new List<(int Id, float Score)>();
                double sum = 0;
                var max = float.NegativeInfinity;
                for (var id = 0; id < size; id++)
                    if (id != start) max = Math.Max(max, logits[0, beam.Tokens.Length - 1, id]);
                for (var id = 0; id < size; id++)
                {
                    if (id == start) continue;
                    var score = logits[0, beam.Tokens.Length - 1, id];
                    sum += Math.Exp(score - max);
                    if (top.Count == width && score <= top[^1].Score) continue;
                    top.Add((id, score));
                    top.Sort((a, b) => b.Score.CompareTo(a.Score));
                    if (top.Count > width) top.RemoveAt(width);
                }
                var normalizer = max + Math.Log(sum);
                foreach (var next in top)
                {
                    var candidate = new Beam([.. beam.Tokens, next.Id], beam.Score + next.Score - normalizer);
                    candidates.Add(candidate);
                }
            }
            // Preserve greedy behavior exactly at width one. For beam search EOS competes
            // with live hypotheses; length penalty is 1.0, as Marian generation defaults.
            var ranked = candidates.OrderByDescending(b => b.Score).Take(width).ToArray();
            foreach (var beam in ranked.Where(b => b.Tokens[^1] == end)) completed.Add(beam);
            active = ranked.Where(b => b.Tokens[^1] != end).ToList();
            if (active.Count == 0 || completed.Count >= width)
                return completed.OrderByDescending(b => b.Score / (b.Tokens.Length - 1)).First().Tokens[1..^1];
        }
        // Never silently return a partial result when the generation budget is exceeded.
        throw new TranslationException(TranslationFailure.InvalidResponse);
    }


    // No wait handle is created for this managed async gate. Keeping it valid lets an already
    // cancelled/draining operation release it safely during emergency process shutdown.
    public void Dispose() => Interlocked.Exchange(ref _disposed, 1);
}

internal sealed record OfflineDecodingOptions(int BeamWidth = 1, bool SplitSentences = false);
