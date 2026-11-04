# Dynamic Island 0.2.1

- Fixed drop-target page clipping and improved recorder layout for long filenames.
- Adjusted hover motion, page reveal and settled shadow appearance.
- Added left/center/right monitor placement and a separate configurable hide/show
  shortcut (default Ctrl+Alt+H), with recovery from the tray.
- Added optional Yandex Cloud translation: manual requests, language selection,
  swap/copy/cancel, explicit consent, and a user-owned API key in Windows Credential
  Manager. API usage may incur charges; no shared key is bundled.
- Added media Repeat/Shuffle when supported by the actual Windows media session.
- Added in-page Pomodoro duration editing for the next session.
- Water portions now use fixed 50 ml increments from 50 to 700 ml, preserving
  recorded drinks/history and undo.
- Enlarged the existing Tools settings shortcut; File Explorer opens normally.

The original font families, Lucide icons, colors and island shape are retained.
Existing user settings/data are preserved with additive migration.

Validation: full Release cross-build and 140 portable regression tests passed.
The GitHub release workflow additionally runs the Windows test suite before
packaging x64 and ARM64 installers and Velopack update files.
Interactive Windows scale/DPI, animation, native shortcut/credential and real
player/API checks remain unverified. See `IMPLEMENTATION_STATUS_2026-11-03.md`.
