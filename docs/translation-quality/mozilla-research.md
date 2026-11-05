# Mozilla Firefox Translations independent model evaluation

Retrieved and executed on 2026-11-05. These are different trained weights from the current OPUS-MT ONNX models, although both systems use the Marian machine translation family. No production engine, shipping dependency, or model selection was changed by this research.

## Exact artifacts and reproduction

`mozilla-artifact-lock.json` contains official Remote Settings record IDs, URLs, architecture/version filters, compressed/decompressed sizes and SHA-256 hashes. The EN→RU desktop model is **base / 3.0** (42,992,955-byte weights). RU→EN uses the newer **base-memory / 3.1** (31,561,787-byte weights). The different EN→RU **Android** base-memory model was deliberately excluded; it must not be described as the current desktop model.

The official runtime record is **v0.6.0**, collection version **4.0**, WASM SHA-256 `f38ef807636a7c994afedaab7ff8ffe0d590d21897f05139f747b80fd7bbe926`. Its Remote Settings revision is `1de4a085d3a7afb625c51a60aabb5ad298e4059f`. Firefox's separately pinned glue identifies itself as `v0.6.0+eea6e5a8` and has its own SHA-256; do not conflate those revision fields. The exact combination was executed successfully against all 66 inputs.

| Direction | Model+vocab+lex download, bytes | Uncompressed, bytes |
|---|---:|---:|
| English → Russian | 31,080,750 | 46,665,878 |
| Russian → English | 24,142,431 | 36,912,066 |
| Runtime WASM | 1,211,955 | 4,960,506 |

Combined model download is 55.22 decimal MB, plus runtime and approximately 100 KB of glue. Full-vocabulary inference omits the lex file when loading; the lock retains it for the default-shortlist comparison.

Research requirements: Node.js, Python with `zstandard` for downloads only. None is a dependency of the Windows application.

```sh
python docs/translation-quality/mozilla-download.py /tmp/mozilla-cache
node docs/translation-quality/mozilla-benchmark.cjs /tmp/mozilla-cache docs/translation-quality/corpus.json docs/translation-quality/mozilla-shortlist-results.json shortlist
node docs/translation-quality/mozilla-benchmark.cjs /tmp/mozilla-cache docs/translation-quality/corpus.json docs/translation-quality/mozilla-full-results.json full
```

The two Node invocations are sequential. The inference harness reads only local files, supplies `wasmBinary` directly, validates hashes before execution, and contains no network client. Actual translations and per-input wall/CPU/memory observations are committed in the two results files. `mozilla-qualitative-review.json` contains every source, both outputs, 0/1/2 scores and reasons. These scores are **AI bilingual qualitative review**, not a blinded human evaluation or a standardized benchmark.

## Results on the shared 66-case corpus

33 phrases per direction; 11 categories. Scores: 2 = meaning preserved and acceptable fluency; 1 = partly correct, recoverable meaning with grammar/nuance/terminology defects; 0 = major semantic error, omission, or unintelligibility. Acceptable paraphrases are allowed. One illustrative reference per phrase does not cover all valid translations.

| Configuration | Meaning preserved (2) | Partial (1) | Major problem (0) | Score / 132 |
|---|---:|---:|---:|---:|
| Official beam-1 configuration, shortlist | 44 | 10 | 12 | 98 |
| Same weights, no shortlist | 49 | 7 | 10 | 105 |

Full vocabulary helps several cases but is not universally better. It repairs the glasses/points ambiguity and the walk/bus question; it changes financial “bank” to “shore” in the paired river-bank example. Both modes fail all three English idioms and two Russian idioms outright, retain an unnatural double-edged-stick calque, omit “timed out”, mistranslate repository sharing, and fail the letter/“in it” pronoun. Therefore these weights are not a blanket quality upgrade based on this sample. They are a small, legally usable independent candidate with specific strengths and failure cases.

| Source | Shortlist | Full vocabulary | Finding |
|---|---|---|---|
| Break a leg! | Сломайте ногу! | Сломать ногу! | Both lose idiomatic good-luck meaning. |
| You look upset. What's up? | Ты выглядишь расстроена. - Что за сух. | Ты выглядишь расстроенным. - Что случилось? | Full repairs corrupted question. |
| The bank is closed, so we sat on the river bank. | Банк закрыт, поэтому мы сидели на берегу реки. | Берег закрыт, поэтому мы сидели на берегу реки. | Full loses financial-bank sense. |
| Он снял очки, чтобы набрать очки в игре. | He took off the points to score points in the game. | He took off his glasses to score points in the game. | Full resolves both senses. |
| The request timed out while connecting to the server. | Спрос при подключению к серверу. | Запрос при подключении к серверу. | Both omit timeout condition. |

## Measured cost and limits

Linux x64, Node v24.22.1, AMD EPYC 9V74 virtual host with 9 CPUs exposed. The official Firefox glue uses fallback WASM GEMM in Node because Firefox's private `WebAssembly.mozIntGemm` hook is absent. These observations are **not** Windows/.NET, ARM64, WebView2, or Firefox performance measurements. Other work may run on this shared host; no dedicated-machine statistical claim is made. Only one complete sequential run per final configuration is recorded. The runtime uses the Firefox beam-1 config, workspace 128 MB and no translation cache; configuration is embedded in each result.

| Configuration | Median EN→RU | Median RU→EN | Total process wall time, all 66 | Process peak RSS |
|---|---:|---:|---:|---:|
| Shortlist | 33.9 ms | 26.9 ms | 3.30 s | 359.1 MiB |
| Full vocabulary | 54.4 ms | 46.3 ms | 4.08 s | 348.6 MiB |

Total process CPU (user+system) was 4.12 s / 4.95 s respectively; CPU time may exceed wall time due to runtime worker/JIT activity. WASM heap peaked at 253.0 / 249.75 MiB; RSS includes Node, JIT, raw file buffers and WASM, not just model weights. Both directions run sequentially in one process, deleting model objects between directions; the WASM linear heap does not shrink. Product worker teardown would be needed to return its reserved heap reliably. Exact per-input CPU and memory, load latency, and p95 derivation inputs remain in the result JSON.

## Licensing and Windows integration feasibility

Mozilla's current [README](https://github.com/mozilla/translations/blob/main/README.md) explicitly states that model files are distributed under MPL 2.0. The June 17, 2026 collaborator reply in [issue #1434](https://github.com/mozilla/translations/issues/1434) independently confirms the weights' license; the issue was closed through the documentation clarification in August. The official runtime record also explicitly says MPL-2.0. This is not a noncommercial model family. Shipping requires retaining appropriate MPL notices and making the covered source/modifications available under the license; examine bundled third-party notices when packaging a runtime.

Firefox's [Bergamot documentation](https://firefox-source-docs.mozilla.org/toolkit/components/translations/resources/03_bergamot.html) describes a Marian fork with a WASM API. Current [Firefox glue and worker source](https://github.com/mozilla-firefox/firefox/tree/main/toolkit/components/translations) are the primary reference for allocation/alignment, sentence iteration, options, and lifecycle. Model presence in Firefox does **not** provide a ready-made Windows .NET package or a signed x64/ARM64 native bridge.

Practical integration options, not implemented or validated here:

1. Host the pinned WASM/glue locally in a WebView2 worker, use the supported JavaScript/.NET message bridge, bundle/hash-check offline assets, and explicitly manage worker teardown, timeout/cancellation, maximum input length, error handling and memory. Node is not required. WebView2 runtime availability/deployment and its WASM behavior must be tested on real Windows x64 and ARM64. Chromium does not supply Firefox's private `mozIntGemm` optimization; this test demonstrates fallback functionality on Linux Node only.
2. Build the upstream Marian/Bergamot native engine and a narrow C ABI wrapper for .NET P/Invoke. This adds native source/build, third-party license and CPU-dispatch maintenance. Windows x64 and native ARM64 builds must each be produced and tested; this research does not claim upstream provides shipping DLLs or that ARM64 is already solved.

No model files, third-party runtime binaries, Node/Python runtime, or production bridge were added to the repository. Only reproducible research tools, locks, outputs and review notes are committed.

Primary machine-readable sources: [model Remote Settings](https://firefox.settings.services.mozilla.com/v1/buckets/main/collections/translations-models-v2/records), [WASM Remote Settings](https://firefox.settings.services.mozilla.com/v1/buckets/main/collections/translations-wasm-v2/records). Retrieval date and every selected record are in the artifact lock.
