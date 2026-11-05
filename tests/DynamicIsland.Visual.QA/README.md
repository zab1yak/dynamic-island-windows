# Windows visual QA executable

Runs a standalone WPF Application and the actual production IslandWindow constructor,
production templates, controls, animation clock and theme. All module bindings use
synthetic fixtures; module services are never constructed. Settings stay in memory.
The browser watcher is constructed but never started. Window native initialization
(styles, temporary hotkeys and fullscreen watcher) runs on an isolated CI desktop.
No identity package, credentials, certificates, device capture, user files or network
translation are used.

Build production first, then:

```powershell
dotnet run --project tests/DynamicIsland.Visual.QA -c Release -- --quick --output artifacts/visual-qa
```

`--quick` exports a covering selection of the matrix (all seven user scales, four
bitmap DPI values, three themes, seven production pages, RU/EN and both backdrops).
Omit it for the complete 2,352-image matrix. These are WPF RenderTargetBitmap images
at declared bitmap DPIs, not physical-monitor DPI screenshots. Translucent source
renders are alpha-composited over fixed light/dark backdrops for inspection.

`--output PATH` overrides `DI_VISUAL_QA_ARTIFACTS`; the default is
`artifacts/visual-qa`. JSON records the actual glyph-run font URI/weight/simulation,
card brush alpha, body alpha, and a hit test outside the body geometry. Timed captures
exercise compact → expansion → settled → +1s/+3s. The controlled v0.3.1-effect image
reattaches the exact prior shadow parameters to the current production Body; it is
a causal reproduction, **not a screenshot from the old installed binary**.

A nonzero exit code indicates failed assertions. `qa-failure.txt` preserves errors.

The controlled old-effect diagnostic records `Reproduced` and render tier. Some
hosted ARM64 renderers do not materialize bitmap effects in RenderTargetBitmap;
a missing control delta is reported as inconclusive, not as a product failure.
Stable current alpha, current absence of shadow, surface scope, actual fonts and
hit testing remain strict assertions on both architectures.
