# LUMEN

> Working title — the in-game name is set in `src/LUMEN.Core/GameIdentity.cs`, never hardcoded.

A fully-offline Windows rhythm game with a total Rating, per-play Performance Points (PP),
and a built-in chart editor. Play · compete · create — in one `LUMEN.exe`.

## Status

**Phase 10 — Polish, optimization and accessibility: complete.** A new player is taken
from naming themselves, through a tutorial that asks them to press each lane key rather
than telling them which is which, straight into their first song — no menu to navigate and
nothing to read. Timing calibration measures your taps against a generated metronome and
takes the median, so one fumbled tap cannot move your offset. Accessibility (§67) covers
reduced motion, high contrast, shape cues beside every judgement, effect intensity, and
switches for the judgement and combo displays.

Frame pacing was measured rather than assumed. Over a three-minute song at 240 fps —
44,927 frames — the game's own work never exceeded its budget: 2.80 ms at worst against
5.63 ms, and not one garbage collection during the song. Getting there meant removing a
database query from the draw loop, a per-frame rescan of every note, and the per-frame
string formatting behind them: 2,791 bytes allocated per frame became 234. The profiler
splits each frame into the game's own work, the driver's present call and the frame
limiter's wait, which is how a reproducible 60 ms stutter was identified as 58 ms inside
the graphics driver with the game's work for that frame at a tenth of a millisecond.
528 unit tests green.

Prior phases: one-file backup, restore and machine migration (Phase 9); replays that
reproduce a play exactly and achievements that unlock from your statistics (Phase 8); the
chart and package formats with validation, revision history and format migration (Phase 7);
the chart editor with total undo, a beat grid that follows tempo changes and Test Play
(Phase 6); the song library, Song Select with per-chart bests and local rankings, and
favourites (Phase 5); Rating, PP, per-play performances and statistics, with score saving
as one all-or-nothing SQLite transaction (Phase 4); Tap + Hold gameplay with a
deterministic engine and a NAudio/WASAPI audio clock (Phase 3); player setup, profiles and
the screen/UI toolkit (Phase 2); window, clock, logging, crash guard, SQLite migrations
(Phase 1).

`LUMEN.exe --autoplay` plays the practice chart with perfect input and prints the result
and the frame-time figures; `--autoplay <chart>` does the same for any chart, which is how
a chart can be verified end to end without a person at the keyboard.
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
