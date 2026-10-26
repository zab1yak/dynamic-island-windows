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
| xunit.v3, Microsoft.Extensions.TimeProvider.Testing (tests only) | Apache-2.0 / MIT |

## Not included

- **SF Pro** fonts are not distributed. If you have them installed, the app uses them; otherwise it
  falls back to Segoe UI Variable. Apple's font licence forbids redistribution.
- **SF Symbols** are not used: Apple licenses them only for apps on Apple platforms.
