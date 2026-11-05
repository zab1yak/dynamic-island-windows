# WPF rendering evidence

These files are actual Windows x64 WPF renders from quality run
[42086403663](https://github.com/zab1yak/dynamic-island-windows/actions/runs/42086403663),
using the production IslandWindow and module templates with synthetic data.
They are bitmap-DPI renders, not physical-monitor DPI captures. The two font
boards compare the previous machine fallback, embedded Inter and static
Source Sans 3 fixtures; tests verify actual file URI, weight and no simulation.

- `tasks-translucent-light.png`: production tasks template over fixed light backdrop.
- `translator-translucent-dark.png`: production translator template over fixed dark backdrop.
- `timed-alpha-x64.json`: alpha stays 192 across compact, expansion, settled, +1s/+3s.
- `controlled-shadow-comparison.png`: current body versus exact old shadow parameters
  attached to the current body; **not an old installed-binary screenshot**.
- `controlled-effect-x64.json`: effective alpha 192 versus 213 in that causal control.

The x64 run passed 588 covering window renders, all 468 Windows tests and native
translation with downloads forbidden. ARM64 passed tests, current-alpha timeline
and native translation, but its hosted renderer did not materialize a delta for
the old-effect causal control. That control is inconclusive on this ARM64 runner;
it is not a failure of current alpha. The full 2,352-image matrix per architecture is covered by final acceptance run
[42089082130](https://github.com/zab1yak/dynamic-island-windows/actions/runs/42089082130),
including the localization concurrency fix; see the QA checklist for the final gates.
