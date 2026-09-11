# LUMEN

> Working title — the in-game name is set in `src/LUMEN.Core/GameIdentity.cs`, never hardcoded.

A fully-offline Windows rhythm game with a total Rating, per-play Performance Points (PP),
and a built-in chart editor. Play · compete · create — in one `LUMEN.exe`.

## Status

**Phase 8 — Replays and achievements: complete.** Every play you finish is recorded, with
no switch to remember to flip — a replay you had to ask for in advance is never there for
the run that turned out to matter. Watching one feeds your own inputs back through the
real game, so it reproduces the play exactly rather than approximately. Achievements
unlock from your statistics rather than from the moment they happened, which means one
added later still unlocks for what you have already done. 448 unit tests green.

Prior phases: the chart and package formats with validation, revision history and format
migration (Phase 7); the chart editor with total undo, a beat grid that follows tempo
changes and Test Play (Phase 6); the song library, Song Select with per-chart bests and
local rankings, and favourites (Phase 5); Rating, PP, per-play performances and statistics, with score
saving as one all-or-nothing SQLite transaction (Phase 4); Tap + Hold gameplay with a
deterministic engine and a NAudio/WASAPI audio clock (Phase 3); player setup, profiles and
the screen/UI toolkit (Phase 2); window, clock, logging, crash guard, SQLite migrations
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
