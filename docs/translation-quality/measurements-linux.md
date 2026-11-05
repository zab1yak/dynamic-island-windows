# Reproduced offline measurements — 2026-11-05

All four ONNX configurations completed the same 66 synthetic inputs (33 English
and 33 Russian), in the committed corpus order. No HTTP handler was available to
the runner. Synthetic texts and outputs are recorded in the adjacent JSONL files.
The corpus now supplies two illustrative references per input; the raw runs retain
the first reference available when they were run. Input texts have not changed.
References are guidance for semantic review, not an exact-match acceptance test.

| Configuration | Median wall/request | P95 wall/request | Total CPU | Process peak working set | Working set after last request |
|---|---:|---:|---:|---:|---:|
| v0.3.1 production greedy, whole input | 1212 ms | 1923 ms | 110.2 s | 1107.6 MiB | 635.6 MiB |
| Same model, greedy, sentence segments | 1207 ms | 2580 ms | 118.1 s | 961.5 MiB | 491.4 MiB |
| Same model, beam 4, sentence segments | 1634 ms | 2501 ms | 169.5 s | 1153.6 MiB | 680.9 MiB |
| TC-big int8, beam 4, sentence segments | 1780 ms | 2864 ms | 199.2 s | 1363.1 MiB | 939.9 MiB |

Environment: Ubuntu 24.04.3 LTS, Linux x64, .NET 10.0.14,
Microsoft.ML.OnnxRuntime 1.31.1, Microsoft.ML.Tokenizers 2.0.1. Native CPU
inference has two intra-operation threads and one inter-operation thread. Model
files are SHA-256 verified and native sessions are created/disposed per request,
so these numbers include verification/tokenizer/session setup as in the app.
No runtime memory trim or forced garbage collection was requested. Peak working
set is a lifetime high-water mark, and the last column is an immediate sample,
**not settled idle memory**. Windows x64/ARM64 acceptance remains separate.

Each configuration used a fresh process; the greedy segmented run was interrupted
after case 13 and resumed in a new process. Its per-request times are retained, and
its peak figure is the maximum reported by either process. These measurements are
observations from this machine, not throughput guarantees or a strict controlled
microbenchmark; short-lived download/build activity also occurred during this
work. CPU time is process time, while wall time can reflect that contention.

The model comparison is paired at beam 4 and identical segmentation. Beam 4 is
an experimental implementation with length penalty 1.0; exact Transformers beam
search parity is not claimed. All outputs must be scored for meaning before a
model/default change is selected. In particular, bigger weights still mistranslate
most idioms in this corpus. Segmentation fixes omitted second questions but can
remove context from short commands. New ONNX weights correct several technical
terms and number formatting, while introducing bank/glasses ambiguity errors.

See the Mozilla research report for its separately measured official-WASM
candidate. Its persistent native/WASM model lifetime differs from this ONNX
per-request lifetime; timings should not be read as a deployment-equivalent
comparison. No Windows idle RAM/CPU or end-user visual acceptance is claimed.

## Selected production behavior

A separate masked qualitative review selected TC-big int8 with beam 4 and
sentence segmentation: 108/132 points, seven major meaning errors, versus
87/132 and 18 major errors for the v0.3.1 baseline. This is a small subjective
AI review, not a certified human evaluation or a universal quality guarantee.
The full masked review belongs alongside the raw outputs; all six idioms and
ambiguity failures remain visible, including regressions for financial bank and
glasses/points. Better average results do not remove those limitations.

Only new explicit installations/updates choose TC-big. An existing package is
resolved by its recorded revision, preserving the original model and greedy
whole-input decoding until the user chooses Update. Revisions are immutable;
an atomic selection file stores the active and previous revision together.
Rollback checks hashes before switching and retains both versions. Reactivating
a retained latest model needs no download. Delete explicitly removes known
models for that direction; history/settings files are never read or modified.

Updated production smoke also passed locally with native Linux x64 ONNX inference
and a HTTP handler that always rejects network access. English input
`Good morning. The number is 42.` produced `Доброе утро. Номер 42.`; Russian
input `Доброе утро. Число — 42.` produced `Good morning. The number is 42.`.
The smoke checks greeting sense, both sentence segments and numeric retention,
not exact reference equality. The legacy production configuration was separately
resolved from existing v0.3.1 markers: `Hello` → `Привет.` and `Привет` → `Hey.`.
This verifies old packages still load without automatic update. An earlier
ambiguous single-word `Hello` check rejected the new model's legitimate phone
greeting `Алло.`; the test input was made contextually unambiguous.
