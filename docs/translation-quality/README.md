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
  /path/to/models docs/translation-quality/corpus.json results.jsonl
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
