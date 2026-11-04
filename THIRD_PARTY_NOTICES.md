# Third-party notices

Dynamic Island for Windows is MIT-licensed. It includes or depends on the following components.

## Bundled assets

### Lucide icons — ISC License
Icons in `src/DynamicIsland.UI/Icons/Lucide.xaml` are converted from
[lucide-static](https://www.npmjs.com/package/lucide-static) 1.61.0 by `tools/import_lucide.py`.

```
ISC License

Copyright (c) 2026 Lucide Icons and Contributors

Permission to use, copy, modify, and/or distribute this software for any
purpose with or without fee is hereby granted, provided that the above
copyright notice and this permission notice appear in all copies.

THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHOR DISCLAIMS ALL WARRANTIES
WITH REGARD TO THIS SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF
MERCHANTABILITY AND FITNESS. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR
ANY SPECIAL, DIRECT, INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES
WHATSOEVER RESULTING FROM LOSS OF USE, DATA OR PROFITS, WHETHER IN AN
ACTION OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION, ARISING OUT OF
OR IN CONNECTION WITH THE USE OR PERFORMANCE OF THIS SOFTWARE.
```

Some Lucide icons are derived from [Feather](https://github.com/feathericons/feather)
(MIT License, Copyright (c) 2013-2023 Cole Bemis).

## NuGet packages

| Package | License |
|---|---|
| CommunityToolkit.Mvvm | MIT |
| Microsoft.Extensions.Hosting / Logging / DependencyInjection | MIT |
| Serilog, Serilog.Extensions.Hosting, Serilog.Sinks.File | Apache-2.0 |
| H.NotifyIcon.Wpf | MIT |
| NAudio.Wasapi | MIT |
| Velopack | MIT |
| Microsoft.ML.OnnxRuntime 1.31.1 | MIT; bundled native dependency notices in `licenses/ONNXRuntime-ThirdPartyNotices.txt` |
| Microsoft.ML.Tokenizers 2.0.1 | MIT; dependency notices in `licenses/Tokenizers-ThirdPartyNotices.txt` |
| xunit.v3, Microsoft.Extensions.TimeProvider.Testing (tests only) | Apache-2.0 / MIT |

## Not included

- **SF Pro** fonts are not distributed. If you have them installed, the app uses them; otherwise it
  falls back to Segoe UI Variable. Apple's font licence forbids redistribution.
- **SF Symbols** are not used: Apple licenses them only for apps on Apple platforms.

## Telegram support icon — Simple Icons 16.0.0

The Telegram path in `src/DynamicIsland.UI/Icons/Support.xaml` comes from
https://github.com/simple-icons/simple-icons/blob/16.0.0/icons/telegram.svg.
Simple Icons dedicates its icons to the public domain under
[CC0 1.0](https://github.com/simple-icons/simple-icons/blob/16.0.0/LICENSE.md).
CC0 does not grant trademark rights. This unmodified mark labels a Telegram
support link only; it does not imply endorsement by Telegram.

The Mail path in the same support dictionary comes from lucide-static 1.61.0
(`icons/mail.svg`), under the Lucide ISC license reproduced above.

## Offline translation models — downloaded separately

The language package manager downloads unmodified, quantized ONNX conversions
of OPUS-MT Marian models by the Helsinki-NLP / University of Helsinki Language
Technology Research Group. Conversion is provided by onnx-community. Model
data is not bundled in the installer. Each direction is approximately 243 MB
(232 MiB); a separate explicit download is required for each direction.

| Direction | Original model | ONNX conversion and immutable revision |
|---|---|---|
| English → Russian | https://huggingface.co/Helsinki-NLP/opus-mt-en-ru | https://huggingface.co/onnx-community/opus-mt-en-ru/tree/c6967b328d292f9f3d14bab6e9de40f9bd367114 |
| Russian → English | https://huggingface.co/Helsinki-NLP/opus-mt-ru-en | https://huggingface.co/onnx-community/opus-mt-ru-en/tree/92ef0d550ca96ebd9cd5d13aab6ad41854d99a3d |

The model cards declare **CC BY 4.0**: https://creativecommons.org/licenses/by/4.0/.
The original Helsinki-NLP model cards currently identify **Apache-2.0**;
retain that upstream licence reference as well: https://www.apache.org/licenses/LICENSE-2.0.
Credit belongs to the original model authors and conversion contributors, not
Dynamic Island. The application downloads the pinned conversion bytes unchanged
and checks every SHA-256 before installation and before native inference. No
custom model code is downloaded or executed. Model quality is not guaranteed.
See the original model cards for training data, limitations and author citations.

## Owner-provided alert sounds

`Assets/Sounds/illuminate.mp3` (Pomodoro) and `stargaze.mp3` (Water) were supplied
by the project owner for this update. The task's preceding conversation records
the owner's confirmation of public distribution rights. This is owner-provided
authorization, not an independently identified third-party license or a grant
that these audio files inherit the application's MIT license. No third-party
hosting of the audio was used during development.
