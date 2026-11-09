# Music and updater hotfix — v0.3.3

## Scope and baseline

The owner explicitly authorized implementing, testing, committing and publishing
this two-part patch without another approval. At audit, remote main was
`3aa9c0870ac3bf6c84b755544e81ff1f36d45e78`, latest stable release was v0.3.2,
and v0.3.3 was free. Existing tags/releases are preserved.

Production changes are restricted to the music template and application-update
service/About presentation, plus the shutdown hook required for installation.
`scripts/verify_hotfix_scope.py` compares against v0.3.2 and rejects changes to
unrelated production paths, existing translations, other settings pages, the
music live/compact template, fonts, palette and geometry. No runtime dependency
version changes are needed.

## Music

The owner reference is the supplied 860×196 screenshot of Life (Zivert): large
square cover on the left; title, artist, progress and elapsed/total time in the
middle; previous/play-pause/next on the right. The existing 484×104-DIP page area
uses an 84-DIP cover, a flexible central column and a 108-DIP transport column.
Shuffle/repeat and the existing multi-player action remain as smaller secondary
actions. Unsupported commands remain disabled with existing localized hints.
The production media source, session handling and play/pause icon animations
remain unchanged; the live/compact template is preserved exactly.

`MediaLayoutTests` uses real MediaViewModel and the production template with
isolated session fakes. Its 112 captures cover RU/EN, seven user scales,
Playing/Paused/No session, long titles, no artwork, unknown duration and
unsupported controls, plus the live pill. Bound commands and seek capability
are exercised without claiming a real Windows player session.

## Application updates

The same Velopack/GithubSource integration is shared by background and manual
requests. The coordinator admits one operation at a time. Manual checking only
finds an update; download is explicit. Background checks keep their existing
first-minute/daily policy and automatic download behavior. States are delivered
through immutable snapshots and marshaled to WPF's dispatcher; every new status
and action is localized in English and Russian. An unavailable or invalid feed
is an error, not “no updates”. Development/portable copies get an explicit
explanation and a project release link.

Versions and the installed channel come from Velopack 1.2.161. Tests with the
actual pinned manager select 0.3.10 above 0.3.9 on win-arm64; comparisons are not
lexicographic. Release links derive from the checked GitHub release asset URL
and are constrained to this repository. Download percentages come from the
actual Velopack Action<int> callback; an unknown value is indeterminate. Cached
packages are rechecked using Velopack's own checksum verifier before Ready.
Rejected cache data is removed for retry; installed working files are untouched.

The pinned Velopack XML/source establishes two important lifecycle details:
CheckForUpdatesAsync has no CancellationToken parameter, and the exit helper
waits only 60 seconds. Cancelling a check therefore retains the single-flight
slot until its underlying request finishes, then discards its result. Downloads
use the native cancellation API. Settings hide cancels only a manual operation;
automatic checks/downloads are not cancelled merely by hiding the window.

A downloaded package remains pending until actual application shutdown. After
normal saves/drain, PrepareForExit launches the helper with silent=true and
restart=false immediately before Shutdown. This avoids starting its 60-second
wait while the app stays open. Stop cancels outstanding work and suppresses late
tray notifications during exit. The UI accurately offers Exit and install,
with an explanation that the application does not restart automatically.

Pinned upstream provenance: NuGet Velopack 1.2.161, repository commit
`92d6a1c91716729d449034df5c50307dcce39493`. API/source inspection is distinct
from execution; behavior and package-cache verification are also tested with
real pinned Velopack types and isolated locators/feed sources.

## Validation status

- Full Windows-target cross-build and the visual QA executable: passed with zero
  compiler warnings/errors. Cross-compilation is not Windows UI execution.
- Portable suite: 190 passed, including 20 update cases after the atomic progress/completion review fix.
- First Windows x64/ARM64 checkpoint: 496 tests and 588 island-window captures
  passed on each architecture. The separate settings QA host initially resolved
  its root icon URI against the test executable; its resource assembly is now set
  to the real app assembly. This was a QA-host error, not a product icon change.
- The final code adds the concurrent-progress regression; final Windows execution
  and image inspection are recorded below before publication.

Windows tests include the existing regression suite plus new update coordinator,
actual Velopack manager/cache, About command/dispatcher and music layout checks.
The standalone QA executable shows the actual IslandWindow and SettingsWindow
with synthetic data. Its About captures cover 12 states in both languages,
640/780-DIP widths and bitmap DPI 96/144. Fake version 9.9.9 in these images is
intentional; no test release is published and no installer is run by the UI fixture.

## Manual acceptance boundaries

The execution host is Linux; there is no interactive native Windows control
surface available to this session. Hosted Windows WPF renders and simulated
states do not establish physical monitor behavior, live GSMTC/player support,
or a full installed-copy upgrade on the owner's machine. Manual acceptance:

1. Compare the expanded music layout to the owner reference; play/pause, previous,
   next and supported seek must operate on the selected player. Inspect missing
   art, a long title and no session; verify live pill and other pages remain stable.
2. Inspect seven user scales on available physical monitor DPIs and confirm
   artwork corners, transport, progress and labels stay within the island.
3. In an installed copy, check for updates: latest and available versions must be
   explicit. Download must show real progress or an indeterminate indicator.
4. Hide settings during manual downloading, retry after a network failure, and
   verify there is no second operation. An automatic download remains independent.
5. After Ready, leave the app open for more than one minute, then fully exit;
   the verified pending version must apply on exit without an automatic restart.
6. In a portable/dev copy, confirm the explanatory state and release-page link.

The first upgrade from old v0.3.2 still uses its old updater until the new version
is installed. The direct Setup.exe is a fallback; the exit-helper fix applies
inside v0.3.3, not retroactively to the running old binary.
