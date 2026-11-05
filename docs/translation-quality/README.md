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
redistribution review and resource measurements. The selected production candidate is TC-big
with beam 4 and generic sentence segmentation; Windows acceptance remains a release gate.

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


## Blinded semantic review and selection

`masked-independent-review.json` records an independent **AI qualitative review**,
not a human evaluation. The reviewer received randomized labels A–E, all 330
outputs, source texts and illustrative references, without model provenance.
Every output was graded 2 (acceptable), 1 (partial) or 0 (major error). References
were guidance, not an exact-string requirement. Mapping was disclosed only after
all grades were committed.

| Label | Configuration | Score /132 | Acceptable | Partial | Major |
|---|---|---:|---:|---:|---:|
| D | v0.3.1 greedy, whole input | 87 | 39 | 9 | 18 |
| A | Same weights, greedy, segmented | 98 | 43 | 12 | 11 |
| E | Same weights, beam 4, segmented | 94 | 41 | 12 | 13 |
| C | TC-big int8, beam 4, segmented | 108 | 49 | 10 | 7 |
| B | Mozilla official full-vocabulary WASM | 102 | 46 | 10 | 10 |

TC-big is selected for the next package revision: it improves the paired semantic
result and uses the existing native ONNX deployment path on both Windows
architectures. This does not establish universal superiority: against Mozilla it
wins 10 cases, loses 6 and ties 50. Ambiguous bank/glasses senses regress and idioms
remain unreliable. Its measured Linux peak memory and latency are higher; see
[measurement limitations](measurements-linux.md). Mozilla is a promising smaller
alternative, but a Windows .NET/WASM bridge has not been implemented or validated.

Existing installed packages retain their original decoder and remain usable.
The new weights require an explicit package update. Revisioned storage preserves
the old package for rollback and never removes translation history.

This corpus does not cover empty/invalid input, code or JSON; validation is tested
separately. Standalone “What's up?” is not a scored case, and its greeting versus
literal-event interpretation must not be forced by a phrase replacement.


### Screenshot greeting diagnostic (outside the scored corpus)

`greeting-diagnostic.json` and the two `greeting-*-linux-x64.jsonl` files record a
separate real no-network check after selection. Both baseline and TC-big produce
«Что случилось?» for standalone “What's up?”, and both keep that interpretation
in “Hi Alex, what's up?”. It is a possible event/concern reading, but the friendly
“How are things?” sense is not reliably selected. **This example is not fixed by
the new weights**, and no replacement rule was added. In the server-context case,
TC-big preserves the second sentence (“It stopped responding.”) that baseline
omits. These three diagnostic cases do not alter the blinded 66-case scores.
