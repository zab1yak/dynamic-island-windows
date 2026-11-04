# Offline translation — v0.3.1 implementation and evidence

## Engine choice

The translator uses **Microsoft.ML.OnnxRuntime 1.31.1** (CPU, MIT) and
**Microsoft.ML.Tokenizers 2.0.1** (managed SentencePiece, MIT), with the
quantized Marian OPUS-MT models below. Google.Protobuf, used by the managed
SentencePiece reader, is BSD-3-Clause. No Python installation, interpreter,
external server, shell, `pip`, browser automation or cloud API key is used.

Argos Translate was investigated first. Its normal integration is a Python
library using CTranslate2. CTranslate2's official prebuilt installation matrix
lists Windows x86-64, while its ARM64 builds are listed for Linux/macOS. Shipping
and validating a new Windows ARM64 Python/native sidecar would introduce an
additional unsupported distribution. ONNX Runtime provides official C# packages
containing both Windows x64 and ARM64 native libraries. This is a packaging
reason for the selected implementation; it is not evidence of native execution
on a Windows ARM64 device.

Primary references (checked 2026-11-04):

- https://github.com/argosopentech/argos-translate
- https://opennmt.net/CTranslate2/installation.html
- https://onnxruntime.ai/docs/get-started/with-csharp.html
- https://learn.microsoft.com/dotnet/api/microsoft.ml.tokenizers.sentencepiecetokenizer
- https://api.nuget.org/v3-flatcontainer/microsoft.ml.onnxruntime/index.json
- https://api.nuget.org/v3-flatcontainer/microsoft.ml.tokenizers/index.json

## Model provenance and licensing

| Direction | Source repository | Immutable revision | Download bytes |
|---|---|---|---:|
| English → Russian | `onnx-community/opus-mt-en-ru` | `c6967b328d292f9f3d14bab6e9de40f9bd367114` | 242731178 |
| Russian → English | `onnx-community/opus-mt-ru-en` | `92ef0d550ca96ebd9cd5d13aab6ad41854d99a3d` | 242731178 |

Each direction downloads six data files: quantized encoder and decoder ONNX,
source and target SentencePiece models, vocabulary JSON and configuration JSON.
`Services/TranslationModelCatalog.cs` records exact byte lengths and SHA-256
for every file. The hashes were compared with locally downloaded bytes; large
file hashes also match Hugging Face LFS metadata. Binary weights are not in Git
or the main installer. These are fixed known files, not executable scripts or
arbitrary model code. There is no ZIP extraction or remote filename handling.

The pinned ONNX model cards declare **CC BY 4.0**. Attribution: Helsinki-NLP /
OPUS-MT, with ONNX conversion and quantization by ONNX Community (Xenova).
The current upstream `Helsinki-NLP/opus-mt-en-ru` and `opus-mt-ru-en` model cards
identify Apache-2.0; retain the upstream attribution and licence reference too.
An `ATTRIBUTION.txt` accompanies installed data and the application notices
identify these dependencies. Conversion and quantization are format changes;
this project does not train or modify the model weights.

- https://huggingface.co/onnx-community/opus-mt-en-ru/blob/c6967b328d292f9f3d14bab6e9de40f9bd367114/README.md
- https://huggingface.co/onnx-community/opus-mt-ru-en/blob/92ef0d550ca96ebd9cd5d13aab6ad41854d99a3d/README.md
- https://huggingface.co/Helsinki-NLP/opus-mt-en-ru
- https://huggingface.co/Helsinki-NLP/opus-mt-ru-en
- https://creativecommons.org/licenses/by/4.0/
- https://www.apache.org/licenses/LICENSE-2.0

## Download and local processing safeguards

Downloads require a user action. A dedicated HTTP client disables automatic
redirects; the manager explicitly follows at most eight redirects, validating
HTTPS and Hugging Face origins on every hop. Each streamed file must exactly
match its pinned length and SHA-256. Free space is checked before downloading.
Cancellation removes staging; no partial model is reported as ready.
Verified files are published by a directory move and final installed marker.
Restart cleanup handles only this model's strict GUID staging directories,
rejecting symlinks and foreign nested trees. A cross-process file lock protects
installation/removal and stale cleanup. The package manager owns its dedicated
HTTP client; application shutdown must cancel and drain UI operations first.

The inference service has no HTTP client and never sends submitted text anywhere.
It validates every model file again before native parsing. Native sessions are
created on a worker thread only after Translate and disposed after that operation;
no model session or worker process is retained while idle. Cancellation terminates
the current ONNX run. Native session construction itself cannot be interrupted
mid-constructor; cancellation is checked around it. The async gate remains valid
for a draining operation during emergency disposal.

The current models cover explicit English/Russian pairs. Optional detection is a
simple Cyrillic heuristic, not a general language detector. Input is limited to
2,000 characters **and** 255 model tokens. Longer inputs are rejected rather than
silently truncated. Greedy decoding has a 256-token budget; exceeding it produces
an error rather than a partial result. Quality is that of these small fixed models,
not a promise of cloud-service parity.

## Reproducible native smoke check

This CLI links the actual production service sources and uses real native ONNX
inference, with isolated model data. It prints only the fixed synthetic greetings;
the application never logs arbitrary user translations.

```sh
dotnet build tests/DynamicIsland.Offline.Smoke -c Release
# Explicit network permission: install both pinned directions into an isolated folder.
dotnet run --project tests/DynamicIsland.Offline.Smoke -c Release -- /tmp/island-smoke-models --install
# No download code is invoked; this mode installs an HTTP handler that rejects all requests.
dotnet run --project tests/DynamicIsland.Offline.Smoke -c Release -- /tmp/island-smoke-models
```

On Linux x64, .NET SDK 10.0.403 / runtime 10.0.14, native ONNX Runtime 1.31.1:

| Synthetic input | Actual output | Wall time | Process CPU time | Process peak working set |
|---|---|---:|---:|---:|
| Hello | Привет. | 3.52 s | 3.71 s | 622 MiB |
| Привет | Hey. | 2.69 s | 3.40 s | 702 MiB (cumulative peak) |

A separate clean run through the production C# package downloader installed both
directions successfully, then produced the same outputs (2.69 s / 3.04 s;
cumulative peak 778 MiB). The CLI performs no downloads without `--install`;
in that mode its HTTP handler rejects any attempted network request.

A first earlier invocation took 6.35 s and 3.53 s respectively. These are single
short-input smoke observations on this container, not Windows benchmarks or idle
RAM measurements. Model loading is deliberately repeated to release native session
memory between user requests. Peak memory during translation exceeds the app's
normal idle-memory target; native Windows memory/startup/idle CPU measurements
remain required. No interactive WPF, Windows x64 native inference, or Windows ARM64
native inference was executed in this environment. Presence of matching native DLLs
and cross-compilation does not establish those execution checks.

The Windows x64 ONNX Runtime DLL import table was inspected: it imports
`MSVCP140.dll`, `MSVCP140_1.dll`, `VCRUNTIME140.dll` and `VCRUNTIME140_1.dll`.
The ONNX NuGet package does not bundle those VC++ runtime DLLs. A self-contained
.NET publish alone is not proof that the required C++ runtime is present on a clean
user machine. The release must bundle the correct officially redistributable
runtime components for each architecture or prove the prerequisite packaging;
this check remains a clean-machine release gate until verified.
