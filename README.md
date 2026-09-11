# LUMEN

> Working title — the in-game name is set in `src/LUMEN.Core/GameIdentity.cs`, never hardcoded.

A fully-offline Windows rhythm game with a total Rating, per-play Performance Points (PP),
and a built-in chart editor. Play · compete · create — in one `LUMEN.exe`.

## Status

**Phase 5 — Song select: complete.** The chart folders are scanned into a library index,
and Song Select lists songs with their difficulty range, per-chart best score, accuracy
and PP, and the chart's local ranking with your own row marked — all on one screen.
Live search over title, artist and charter; six sort keys; favourites that persist per
profile. Picking a chart starts the game. 261 unit tests green.

Prior phases: Rating, PP, per-play performances and statistics, with score saving as one
all-or-nothing SQLite transaction (Phase 4); Tap + Hold gameplay with a deterministic
engine and a NAudio/WASAPI audio clock (Phase 3); player setup, profiles and the
screen/UI toolkit (Phase 2); window, clock, logging, crash guard, SQLite migrations
(Phase 1).

`LUMEN.exe --autoplay` plays the practice chart with perfect input and prints the result.
`LUMEN.exe --capture <dir>` renders each screen to a PNG.

See [`docs/PLAN.md`](docs/PLAN.md) for the full 11-phase plan and
[`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) for the design.

## Requirements

- Windows 10 / 11 (x64)
- .NET 8 SDK — this machine uses a user-local install at `%USERPROFILE%\.dotnet`
  (the machine-wide runtime has no SDK). `build.ps1` handles the PATH.

## Build & run

```powershell
.\build.ps1 build     # compile everything
.\build.ps1 test      # run the unit tests
.\build.ps1 run       # launch the game
.\build.ps1 smoke     # launch, verify the window comes up, exit
.\build.ps1 crashtest # launch, throw in the loop, verify the error screen catches it
.\build.ps1 publish   # self-contained single-file LUMEN.exe -> .\publish\
```

Or use the SDK directly:

```powershell
$env:PATH = "$env:USERPROFILE\.dotnet;$env:PATH"
dotnet build LUMEN.sln
```

## Layout

| Project        | Framework        | Role |
|----------------|------------------|------|
| `LUMEN.Core`   | `net8.0`         | Pure game logic — notes, judgement, scoring, Rating, PP, chart model. No engine, no I/O. |
| `LUMEN.Data`   | `net8.0`         | SQLite, repositories, migrations, backup, path resolution. |
| `LUMEN.Audio`  | `net8.0-windows` | Low-latency audio playback and the audio clock (Phase 3). |
| `LUMEN.Game`   | `net8.0-windows` | MonoGame application → `LUMEN.exe`. |
| `LUMEN.Tests`  | `net8.0-windows` | xUnit. |

## Data location

User data lives under `%LOCALAPPDATA%\LUMEN\` (installed) or `./data/` next to the
executable when a `portable.txt` sentinel is present. Game binaries never write elsewhere.
