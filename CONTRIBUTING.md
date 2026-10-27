# Contributing

Thanks for helping! Bug reports, ideas and pull requests are welcome.

## Before you start

- Read [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) — layers, the module contract and how the island renders.
- Check [docs/DECISIONS.md](docs/DECISIONS.md) — why things are the way they are.
- For bigger changes, open an issue first so we can agree on the approach.

## Development

```powershell
dotnet build
dotnet test
dotnet run --project src/DynamicIsland.App -- --showcase   # Debug: component gallery
```

- .NET 10 SDK, Windows 10 2004+ (WinRT APIs are used).
- Warnings are errors; keep the analyzers happy instead of suppressing them.
- `DynamicIsland.Core` targets plain `net10.0`: no WPF or WinRT types there.
- Logic goes into small classes with unit tests (xUnit v3, `FakeTimeProvider` for anything time-based).
- UI text is localized: add keys to the module's `Resources/strings.json` (English **and** Russian), then run
  `python -I tools/gen_resx.py <strings.json> <folder>`. A test checks that both languages have the same keys.
- Colours, fonts, radii come from `src/DynamicIsland.UI/Themes`; icons from `IconProvider` (Lucide).
  New icons: add them to `tools/import_lucide.py` and regenerate.
- No timers that tick while the island is idle. Animations run on `AnimationClock`, which only renders while
  something moves.

## Adding an island page (module)

1. New project `src/DynamicIsland.Modules.<Name>` referencing `Core` and `UI`.
2. Implement `IIslandModule`; ship a `ResourceDictionary` with `DataTemplate`s for your view models.
3. Register it with one line in `AppHostBuilder` (`services.Add<Name>Module()`). The window does not change.

## Commits and pull requests

- [Conventional Commits](https://www.conventionalcommits.org/): `feat:`, `fix:`, `docs:`, `refactor:`, `test:`, `chore:`, `ci:`.
- One logical change per pull request; `dotnet build` and `dotnet test` must pass (CI runs them).
- Describe what you verified by hand (see [docs/QA_CHECKLIST.md](docs/QA_CHECKLIST.md)).

## Releases

Maintainers tag `vX.Y.Z`; GitHub Actions builds the x64 and ARM64 installers with Velopack and publishes
the release.
