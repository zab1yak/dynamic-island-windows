# v0.3.3 visual evidence

- `music-reference-comparison.png`: owner reference beside a real Windows WPF
  IslandWindow render. Synthetic after-image track/artwork; scale 100%, bitmap DPI144.
- `updates-ru-downloading-37-780wide-96bitmapdpi.png`: actual About template with
  simulated native progress callback state (37%). No invented byte counts.
- `updates-en-ready-780wide-96bitmapdpi.png`: actual About template explaining
  full exit and no automatic restart.
- `about-update-qa.json`: all96 About state captures, RU/EN ×12 states ×2 widths
  ×2 bitmap DPIs. Installed version0.3.3 is real; candidate9.9.9 is a local fake.

Source: Windows x64 run42692845287, production tree unchanged in the later
final-check run. Both architectures passed684 window renders in that run;
its ARM64 unit-test host ordering issue was corrected separately before release.
Full artifacts are in GitHub Actions, named Music-and-update-UI-X64/Arm64.
These are real WPF windows rendered by RenderTargetBitmap, not screenshots of a
physical monitor or proof of live media/installed upgrade acceptance.
