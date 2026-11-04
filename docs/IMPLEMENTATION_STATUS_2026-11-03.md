# Implementation status — 2026-11-03

## Update 2026-11-04 — version 0.3.0 published

GitHub access was restored after the owner installed the Codex connector. The
implementation is now in remote `main`, and stable release **v0.3.0** is published:
https://github.com/zab1yak/dynamic-island-windows/releases/tag/v0.3.0

- Application tag: `ac44353c12cec4d7ba28958593e4754b1d926b43` (same source trees
  as the local implementation, with commits transferred through the GitHub API).
- Full Windows suite: **419 passed, 0 failed, 0 skipped**; CI succeeded.
- Portable suite: **140 passed**.
- Both x64 and ARM64 installers, portable ZIPs, full/delta packages and update
  manifests are uploaded. Both manifests advertise version 0.3.0.
- Release workflow: https://github.com/zab1yak/dynamic-island-windows/actions/runs/41809464300
- Packaging repair `81d6606` explicitly supplies each architecture to Velopack,
  resumes an unpublished existing tag, and retries transient upload failures.
  Packaging checks out the immutable application tag; the tag was not moved.
- Both packaging/upload steps succeeded. The ARM64 job's redundant final release
  notes edit returned HTTP 502 after all files were published; x64 had already
  written the correct notes. The workflow now writes notes once, with retries.
  The historical run is red for that final metadata request, not a test or asset
  failure. Public assets and both update manifests were verified independently.
- Interactive Windows visual/DPI/monitor acceptance, actual native credentials
  and live player/Yandex API behavior remain unverified. Passing hosted Windows
  tests is not a substitute for those checks.

The remaining text is the original audit/history. Its earlier publication and
connection blockers have been resolved by the update above.

Baseline: clean `main`, `1c56f4175618336c159659650d3eb084a0ec90a8`.
GitHub's latest published release was checked separately: `v0.2.0`, published
2026-10-27. No release or tag was created. The installed Windows binary is unknown.
The uploaded source ZIP was unavailable; work used the actual GitHub source.
No Claude JSONL histories were attached to this workspace or claimed as read.

## Requirements and evidence

“Implemented” below means code is present and cross-compiles, not visual acceptance.

| Task | Status | Change / outstanding verification |
|---|---|---|
| T-00 approved design/data | Preserved in code; visual acceptance pending | Original font families, Lucide 1.61.0, palette and shape math retained. No user data was opened, reset or removed. |
| T-01 settings shortcut | Implemented | Existing Tools settings pill 30→36 DIPs, glyph 14→16, tooltip; existing host command retained. Verify click, keyboard and small scale. |
| T-02 Translator | Implemented; native/live checks pending | New registered module; official API, six target languages and source detection, swap/copy/cancel, bounded scrolling, manual sends, consent and Credential Manager. Provider tests pass; no live API request made. See module README. |
| T-03 placement | Implemented; monitor checks pending | Left/center/right picker, additive schema 4, physical monitor-relative placement and relocated drag detector. Placement math tests pass. |
| T-04 text/icon quality | Partial | Existing vector fonts/glyphs retained. Fixed Lucide importer relative subpath origin for added icons. No Windows before/after render comparison, so no claim that every blurry text case is fixed. |
| T-05 manual hide | Implemented; native checks pending | Independent Ctrl+Alt+H default, configurable shortcut, duplicate/conflict handling, full visual/input/drop suppression, tray recovery, separate fullscreen and lock semantics. Core tests pass. |
| T-06 hover motion | Partial; visual checks pending | Critically damped shape springs and relative settle tolerances; page reveal follows geometry; settled shadow fades in. Shadow still detaches at movement start, so a visible collapse shadow fade-out is not claimed. No new FPS/CPU measurements. |
| T-07 scale/clipping | Partial; Windows reproduction pending | Fixed confirmed DropTarget 140-body/160-page mismatch, now same expanded height; Recorder flexible split and long-name ellipsis. Water settings action retains position/glyph with an icon-only label to fit beside the ring. Geometry bounds cover nine scales and four DPI factors. Current HEAD already had root LayoutTransform and recorder row fixes before this work. |
| T-08 Pomodoro duration | Implemented | In-page minute stepper, existing per-phase limits, next-session edit rule, reset applies duration now; persisted active duration preserves progress/deadline on edits/restart. Service tests pass. |
| T-09 Water serving | Implemented | 50–700 ml in fixed 50 ml steps, default 250; legacy portion normalization, persisted selection, original drink/history values and undo retained. Tests pass. |
| T-10 File Explorer | Already implemented in baseline | Confirmed Tools launches Windows explorer.exe without path arguments. Recorder's separate recordings-folder action retained. Not launched on Linux. |
| T-11 Repeat/Shuffle | Implemented; live player checks pending | Real GSMTC capability/state and commands; unknown/unsupported disabled, no optimistic success; refresh and session-switch guards, RU/EN and Lucide glyphs. Windows behavioral tests compile but were not executed here. |

The supplied recorder screenshots place the timer above controls. Baseline source
already places the timer beside controls (`cd408a1`) and scales Root with a
LayoutTransform (`abbd9bd`). This is evidence of a screenshot/source mismatch,
not proof that the installed binary is current or that the screenshot's P0 is resolved.

## Executed verification

Environment: Linux, local official .NET SDK 10.0.403, no interactive Windows desktop.

Baseline and changed full solution cross-builds both succeeded, zero warnings/errors:

```sh
dotnet build -c Release -p:EnableWindowsTargeting=true \
  -p:UseSharedCompilation=false -m:1 -nr:false
```

Serial MSBuild and direct compilation are needed because this environment blocks
the compiler server's local named pipe. These are invocation flags, not project changes.

Reproducible portable tests link the actual service sources and the original test
files; they contain no alternate implementation or simulated UI:

```sh
dotnet build tests/DynamicIsland.Portable.Tests/DynamicIsland.Portable.Tests.csproj \
  -c Release -p:UseSharedCompilation=false -m:1 -nr:false
dotnet tests/DynamicIsland.Portable.Tests/bin/Release/net10.0/DynamicIsland.Portable.Tests.dll
```

Result: **140 passed, 0 failed, 0 skipped**. Coverage includes Core island/shape/
hotkey/settings tests, PomodoroTimer, WaterTracker and fake-HTTP translation provider.
An additional temporary harness ran 42 service assertions successfully.

Full Windows tests were attempted: **zero tests ran**, missing
`Microsoft.WindowsDesktop.App` on Linux. They are not reported as passing.
Exploratory inclusion of the Windows storage path tests in a Linux-only runner
failed three Windows-path cases because Linux treats backslashes as filename characters;
those platform-specific tests are retained unchanged in the full Windows project.

Changed XAML/resx/JSON were parsed, localization parity checked, and git diff checked.
Review used real delegated agents with separate source ownership; no global tools,
credentials, certificates, identity package or security settings were installed/changed.

## Remaining Windows acceptance

Build and test on Windows before release:

```powershell
dotnet build -c Release
dotnet test -c Release --no-build
dotnet run --project src/DynamicIsland.App -c Release -- --show
```

Use a disposable Windows account/profile for test data; do not overwrite the real
user's settings. Manually configure and check this matrix with screenshots:

| Scope | Values / actions | Pass condition |
|---|---|---|
| Scale | 70, 75, 80, 83, 90, 95, 100, 110, 130% | Same shoulders/radii; no cropped controls, page dots or click regions. |
| Modes/pages | Compact, live, expanded, pinned, drop; every enabled page | Recorder idle/record/pause/error and long filenames; all important controls visible. |
| DPI/monitors | 100/125/150/200%; left/center/right; non-primary monitor; sleep/unplug | Correct monitor, no clipping/double scaling or click trap. |
| Motion | Enter/leave/re-enter, hotkey/scale/monitor change mid-motion | Smooth continuous reversal; measure frames and idle CPU separately. |
| Hide | Both shortcuts, lock/fullscreen/pin/drop, conflict, tray Show | Hidden overlay cannot catch input; restore always available. |
| Modules | Pomodoro start/pause/restart/reset; Water bounds/undo/midnight | Existing data and next-session rules preserved. |
| Media | Supporting/unsupported Spotify/browser sessions, session change | State follows actual player; failures never paint success. |
| Translation | Missing/revoked key, consent, native store, focus/scroll/copy, user-owned API | No startup/edit sends; no secrets/text logged; no real paid test without explicit permission. |

## Commits, publication and rollback

Local implementation commits: `7d9be0b`, `d0ec65f`, `8176a02`, `b009e28`,
`68e6475`, `581273d`. The portable tests/context are committed separately.
**Push did not happen.** Automatic approval review rejected publication to public
`main` because it did not accept authorization inside the attached Markdown as
trusted approval. Do not retry through a different transport or bypass that rejection.
The owner subsequently explicitly authorized both push and a new GitHub release
in chat. The owner requested version 0.3.0 (superseding the unpublished 0.2.1 draft); it and the release workflow are prepared locally. The new
attempt was blocked by credentials, not approval: terminal Git has no HTTPS login,
and the connected GitHub integration returned HTTP 403, `Resource not accessible
by integration`, when creating a Git tree. No remote branch, tag or release was
changed. Continue after the GitHub connection has write access to this repository;
do not ask for the already-granted push/release authorization again.

No release is ready for publication while Windows visual/native acceptance is pending.
For code rollback, inspect and revert the relevant commits on a clean branch in reverse
dependency order; do not reset history or delete `%LOCALAPPDATA%/DynamicIsland`.
Schema 4 adds fields, Water only changes the next serving, and Pomodoro adds the active
duration. User-file backups should be made locally before any actual installed-version
downgrade; none of those files were accessed in this session.
