# Dynamic Island for Windows

A black, animated **island** at the top-center of your screen — inspired by the iPhone Dynamic Island
and the MacBook notch apps. It fuses with the screen edge, springs open on hover and keeps your music,
a Pomodoro timer, water reminders, a file shelf and a to-do list one glance away.

> 🇷🇺 Русское описание — ниже, в разделе [«На русском»](#на-русском).

<!-- Screenshots / GIF: put them in docs/media and link here. -->

## Features

- **The island** — pure black, fused with the top edge with concave "shoulders", continuous (squircle)
  corners, spring animations. Hover to open, click to pin, `Ctrl+Alt+Space` to toggle. Clicks pass
  through the transparent area around it. Hides itself over fullscreen games, videos and presentations.
- **Status** — battery (green bolt while charging, red below 20 %, yellow in energy saver), red dot while
  the camera is in use or the screen is being shared, orange dot for the microphone (with a readable app
  name). The title of the active browser tab (Chrome, Edge, Yandex, Firefox) scrolls in the header.
- **Music** — controls whatever plays in Windows (Spotify, Yandex Music, browsers, players): artwork,
  scrolling title, live position, drag to seek, previous / play-pause / next, switching between sources.
  A live pill with the artwork and **real-level** bars (the system's output peak meter) while music plays.
- **Remembers the last track** across reboots and shows it paused at the same position. Press Play and the
  app is started for you, then playback resumes at that position (see [what works where](#resuming-the-last-track)).
- **Pomodoro** — 25 / 5 / 15 minutes with a long break every 4 sessions (all adjustable), ring progress,
  rolling digits, sound + Windows notification + an alert in the island. Survives restarts.
- **Water** — daily goal, "Drink 250 ml", fine − / + buttons (50 ml), undo, reminders every N minutes within active hours, midnight
  reset with 90-day history.
- **Shelf** — a temporary basket: drop files, folders, browser images or text (or paste with Ctrl+V),
  drag them back out into any folder or messenger. Copy or link mode, shell thumbnails, auto-cleanup,
  size limit. The island turns into a drop zone when you drag a file to the top edge.
- **To-do** — add with Enter (Shift+Enter for a new line), rename, check off with a little animation,
  drag to reorder.
- **Voice recorder** — record, pause, stop; WAV or MP3 into `Music\DynamicIsland`; play and delete recent
  recordings. A red dot with a timer while recording, a live waveform when expanded.
- **Tools** — Settings and data-folder shortcuts, the island's own FPS, CPU load (green → yellow → red).
- **Settings** in the style of macOS System Settings: start with Windows, display, size, delays, hotkey,
  pages on/off and order, per-page options, Russian / English.
- Auto-updates from GitHub Releases.

## Install

1. Download **`DynamicIslandForWindows-win-Setup.exe`** from the latest
   [release](https://github.com/zab1yak/dynamic-island-windows/releases/latest)
   (ARM64 PCs: the `win-arm64` setup).
2. Run it. No administrator rights are needed; the app installs into your user profile and starts with
   Windows (you can turn that off in Settings).

### "Windows protected your PC" (SmartScreen)

The app is not code-signed yet, so Windows SmartScreen may warn about an unrecognised app. Click
**More info → Run anyway**. If you prefer, check the file first: the release is built by
[GitHub Actions](.github/workflows/release.yml) from the tagged source, and you can build it yourself
(below).

Updates are checked once a day (can be turned off) and installed when you exit the app.

## Hotkeys and gestures

| Action | How |
|---|---|
| Open / close the island | `Ctrl+Alt+Space` (change it in Settings → Island) |
| Open on hover / pin | Hover the island; click its background to pin, click again to unpin |
| Next / previous page | Arrows ‹ ›, horizontal touchpad swipe, Shift + mouse wheel, ← / → keys |
| Close | `Esc` |
| Shelf | Drag files to the top edge; Ctrl+V or the Paste button; drag items out |

## Requirements

Windows 10 version 2004 (build 19041) or newer, x64 or ARM64. Best on a laptop screen; any monitor works.

## Known limitations (honest list)

- **Screen-sharing indicator is a heuristic.** Windows has no public API for "the screen is being
  captured". The red dot follows the sharing bars of browsers (Meet and other sites), Zoom and Teams, and
  Game Bar recording. **A Discord stream is not detected** (it shows no separate window). The optional
  "known recorders" mode (OBS, ShareX, …) shows the dot while such a program merely runs.
- **Notification history** (the «Notifications» page) needs package identity, which Windows grants only to
  packaged apps. The island ships a tiny identity-only package for that; it is set up only when you press
  «Turn on» (see [Turning on notifications](#turning-on-notifications)).
- **Voice recorder MP3** uses the Windows Media Foundation encoder. Windows "N" editions without the Media
  Feature Pack have none — recordings are then saved as WAV.
- **Camera / microphone indicators** read the per-app usage records Windows keeps for its privacy settings
  (`CapabilityAccessManager\ConsentStore`). This format is not officially documented and may change in a
  future Windows update. Desktop apps are only counted while their process runs.
- **Music** depends on what each app publishes to Windows media controls: some apps (often browsers) don't
  report a timeline, so position and seeking are unavailable for them.
- **The visualizer** shows the real output level of the default playback device (one peak value split into
  bars), not a per-frequency spectrum.
- **Shelf:** from a browser or mail client that offers several "virtual" files in one drag, only the first is
  taken (a WPF limitation). Programs (`.exe`, `.lnk`, scripts…) are revealed in Explorer instead of being
  run on click; downloaded files keep their "from the internet" mark.
- **Hotkey:** if another app already uses `Ctrl+Alt+Space` (for example Claude Desktop's Quick Entry),
  the island shows a notice and keeps working on hover — pick another combination in Settings.
- **Fonts:** if you have Apple's SF Pro installed, it is used; otherwise Segoe UI Variable. SF Pro is not
  bundled (its licence forbids redistribution). SF Symbols are not used for the same reason.

### Turning on notifications

The «Notifications» page shows notifications from Telegram and other apps. Windows allows that only for
apps with *package identity*, so the island registers a small identity-only package (no program files, it
points at the installed app). Nothing happens until you press **Turn on**:

1. Open the island, go to the «Notifications» page and press **Turn on**.
2. Windows may ask for administrator rights once (User Account Control). That adds the island's public
   certificate `DynamicIslandIdentity.cer` to *Trusted People* on this computer. The certificate's private
   key was deleted right after signing, so nothing else can ever be signed with it.
3. Press **Restart** on the page.
4. Press **Allow access** and confirm the Windows prompt. The list appears; **Clear** hides what is shown now.

To undo: uninstalling the island removes the package. The machine certificate can be removed in
`certlm.msc` → *Trusted People* → «DynamicIsland Identity». Access can be turned off any time in
Windows Settings → Privacy & security → Notifications.

### Resuming the last track

After a reboot the island shows the last track paused. Nothing starts by itself. When you press Play:

| Source | What happens |
|---|---|
| **Yandex Music (desktop)** | The app is started through its Start-menu entry (`shell:AppsFolder\ru.yandex.desktop.music`). Verified on the developer machine: the app opens. Once it offers playback, the island presses Play and seeks to the saved position if the app reopened the same track. |
| **Spotify** (Store or desktop) | Started through its registered `spotify:` link. Resume and seek work if Spotify reopens the same track (it usually remembers it). |
| **Browser** (YouTube, Yandex Music on the web, …) | Browsers cannot reopen a tab from outside, so the island tells you to open the tab yourself. |
| Other players | Started through their Start-menu entry or `App Paths` registration if available; otherwise you are asked to open the app. |

If an app does not support seeking, or starts a different track, playback resumes without moving the position
and the island says so. End-to-end resume after a real reboot is part of the
[manual checklist](docs/QA_CHECKLIST.md) and still needs confirmation on more machines.

## Build from source

Requirements: Windows, [.NET 10 SDK](https://dotnet.microsoft.com/download).

```powershell
git clone https://github.com/zab1yak/dynamic-island-windows.git
cd dynamic-island-windows
dotnet build
dotnet test
dotnet run --project src/DynamicIsland.App
```

Debug builds have a component gallery: run with `--showcase` or use the tray menu.

Installer (same as CI):

```powershell
dotnet publish src/DynamicIsland.App -c Release -r win-x64 --self-contained -p:PublishReadyToRun=true -o artifacts/publish/win-x64
dotnet tool restore
dotnet vpk pack --packId DynamicIslandForWindows --packVersion 0.1.0 --packDir artifacts/publish/win-x64 --mainExe DynamicIsland.exe --outputDir artifacts/releases
```

Architecture, plan and design decisions: [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md),
[docs/PLAN.md](docs/PLAN.md), [docs/DECISIONS.md](docs/DECISIONS.md),
[docs/PERFORMANCE.md](docs/PERFORMANCE.md).

## Privacy

Everything stays on your computer in `%LOCALAPPDATA%\DynamicIsland` (settings, timers, tasks, the shelf,
logs). To show the last track after a reboot, its title, artist and artwork are kept in
`media-lasttrack.json` there. Logs never contain track titles, task texts, window titles, notification
texts or file names. Voice recordings are written to a private temp folder first and only the finished file
lands in your recordings folder. The only network access is the update
check against this repository's GitHub Releases (can be turned off).

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md). Bug reports and ideas are welcome in
[Issues](https://github.com/zab1yak/dynamic-island-windows/issues).

## License

[MIT](LICENSE). Third-party components and their licences: [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)
(icons: [Lucide](https://lucide.dev), ISC).

---

## На русском

**Dynamic Island for Windows** — чёрный анимированный «остров» по центру верхнего края экрана, как Dynamic
Island на iPhone. Внутри: музыка (любой плеер Windows, обложка, перемотка, визуализатор по реальному уровню
звука, запоминание последнего трека после перезагрузки), Pomodoro, напоминания пить воду, «Полка» для файлов,
список задач, диктофон и страница «Инструменты» (FPS, загрузка CPU). Индикаторы батареи, камеры, микрофона и
показа экрана; название активной вкладки браузера в шапке.

**Установка:** скачайте `DynamicIslandForWindows-win-Setup.exe` из
[релизов](https://github.com/zab1yak/dynamic-island-windows/releases/latest) и запустите. Права администратора
не нужны. Если Windows SmartScreen покажет предупреждение («Система Windows защитила ваш компьютер») —
нажмите **«Подробнее» → «Выполнить в любом случае»**: приложение пока не подписано сертификатом.

**Горячая клавиша:** `Ctrl+Alt+Space` (меняется в настройках). Наведение раскрывает остров, клик закрепляет.

**Ограничения:** индикатор показа экрана — эвристика (по окнам-плашкам браузеров, Zoom, Teams и записи
Game Bar; стрим Discord не определяется); история уведомлений доступна только MSIX-приложениям; индикаторы камеры и
микрофона читают недокументированные записи Windows о доступе к устройствам; браузеры не умеют сами открыть
вкладку, поэтому трек из браузера после перезагрузки нужно открыть вручную. Полный список — выше, в разделе
«Known limitations».
