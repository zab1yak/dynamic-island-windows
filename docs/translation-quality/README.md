# Offline translation quality benchmark

The committed synthetic corpus contains 66 cases in 11 categories, evenly split
between English → Russian and Russian → English. References are examples of
acceptable meaning, not exact-match tests. Review outputs for preservation of
meaning, omissions/additions, negation, idiom interpretation, fluency and names or
numbers. Multiple valid renderings are expected; never optimize the engine to
individual corpus strings. The corpus is diagnostic and small, not a general
translation-quality leaderboard.

The CLI links the actual production service sources. It makes no HTTP requests,
uses no API keys, launches no helper processes and requires no Python at runtime.
Supply an isolated directory containing previously downloaded pinned packages:

```sh
dotnet run --project tests/DynamicIsland.Translation.Benchmark -c Release -- \
  /path/to/models docs/translation-quality/corpus.json results.jsonl [beam-width:1..4] [--sentences] [candidate-manifest.json]
```

Each JSONL row is flushed immediately and includes actual output or error,
per-request wall/CPU time, process lifetime peak working set, post-request working
set, runtime, architecture and OS. Peak working set is a process high-water mark,
not an individual-request allocation. Every request has a two-minute cancellation
limit. Use a fresh process and a distinct output file for each configuration.
Compare on the same machine; Linux evidence does not establish native Windows
x64/ARM64 performance. These synthetic inputs are intentionally recorded; the
application must never log user translation text.

Baseline production decoding must be captured before changing defaults. Model
selection requires paired meaning/fluency review across the entire corpus, legal
redistribution review and resource measurements. No candidate is yet approved.

The checked larger candidate is **TigreGotico/opus-mt-tc-big-en-zle-onnx** and
**TigreGotico/opus-mt-tc-big-zle-en-onnx**, dynamic int8 export of Helsinki-NLP
TC-big models, both CC-BY-4.0. `tc-big-artifact-lock.json` pins repository revisions,
remote names, local names, byte sizes and SHA-256 for every consumed file.
The previously suggested `onnx-community/opus-mt-tc-big-en-ru` / `ru-en` IDs do
not exist publicly; they were never used for the fresh measurements here.

The official [English-to-East-Slavic model card](https://huggingface.co/Helsinki-NLP/opus-mt-tc-big-en-zle)
requires a sentence-initial language label. The Russian label `>>rus<<` is a direct
vocabulary control token (ID 25502 in this pinned export), prepended after
SentencePiece tokenization. Encoding the label as ordinary sentence text would be
incorrect. The reverse model targets only English and needs no target prefix.
The [export model card](https://huggingface.co/TigreGotico/opus-mt-tc-big-en-zle-onnx)
documents dynamic int8 conversion. Its merged decoder is not consumed.

The decoder comparison uses an experimental beam width of four, length penalty
1.0 and completion after four finished hypotheses; it is not a claim of exact
Hugging Face generation-algorithm parity. Sentence segmentation is generic and
preserves punctuation, decimal/version tokens, initials and common abbreviated
titles. No phrase-specific translation rules or substitution dictionary exists.
The benchmark can resume a partial JSONL by skipping IDs already present; if a run
was interrupted, disclose that fact when interpreting timing/high-water memory.
