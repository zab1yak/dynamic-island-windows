# Dynamic Island for Windows

Read `docs/ARCHITECTURE.md`, recent `docs/DECISIONS.md` entries and
`docs/IMPLEMENTATION_STATUS_2026-11-03.md` before continuing this work.

- Preserve the approved WPF design: SF Pro/system fallback, Lucide ISC glyphs,
  palette, shoulders, continuous corners and existing page composition.
- Core stays free of WPF/WinRT. Modules do not reference each other or App.
  Register modules in the composition root; do not hard-code them in IslandWindow.
- Preserve user files and settings. An assistant's historical “done” is not test evidence.
  Distinguish the installed binary, local HEAD, remote main and published release.
- Use existing MVVM, localization, storage, TimeProvider and test fakes. Never embed
  provider secrets or log translated text. Translation requires explicit saved consent
  and a user action; native credential storage is Windows Credential Manager.
- Use real native subagents only when available and useful. Assign disjoint files,
  keep shared settings/composition edits with one owner, integrate and review centrally.
  Architecture/development/UI/QA/security/performance are responsibilities, not ten
  permanently running agents. Do not install global agents or MCPs implicitly.
- Run `dotnet build -c Release` and `dotnet test -c Release --no-build` on Windows.
  The portable test project runs real platform-independent logic on Linux; it cannot
  validate WPF, native hotkeys, media sessions, native credentials or visual quality.
- UI acceptance requires interactive Windows screenshots across scale and DPI values.
  Record unperformed checks explicitly. Never claim new FPS/CPU measurements from a build.
- Keep commits focused. Do not publish releases/tags or bypass a rejected remote action.
