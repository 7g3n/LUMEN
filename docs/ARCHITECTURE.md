# LUMEN — Architecture

## Project dependency graph

```
                 LUMEN.Core          (net8.0 — pure: no engine, no I/O, no platform)
                 ▲    ▲    ▲
       ┌─────────┘    │    └──────────┐
   LUMEN.Data     LUMEN.Audio     LUMEN.Game ──► LUMEN.exe
   (net8.0)       (net8.0-windows)  (net8.0-windows)
       ▲              ▲                 │
       └──────────────┴─────── LUMEN.Game references Data + Audio
   LUMEN.Tests ──► Core + Data + Audio
```

`LUMEN.Core` deliberately targets plain `net8.0`, which makes a Windows-only or
engine dependency a compile error rather than a code-review catch. This is how
"logic and rendering are separated" (spec §68) is enforced structurally.

## Namespaces

- `Lumen.Core` — game rules. Sub-areas (added per phase): `Notes`, `Judgement`,
  `Scoring`, `Combo`, `Rating`, `Pp`, `Skill`, `Charts`, `Replays`, `Balance`.
  Repository *interfaces* live here (`IProfileRepository`, `IScoreRepository`, …);
  implementations live in `Lumen.Data`.
- `Lumen.Data` — `LumenPaths`, `Database`, `Migrations`, `Repositories`, `Backup`,
  `Export`, atomic-file writes.
- `Lumen.Audio` — `AudioEngine`, decoders, `AudioClock`, offset handling.
- `Lumen.Game` — `Engine` (loop, clock, frame limiter), `Input` (providers),
  `Ui` (toolkit), `Screens`, `Editor`, `Rendering`, `Animation`.

## Timing model (the important part)

A rhythm game is a clock with graphics attached.

1. **Master song time** = `audioSamplesPlayed / sampleRate + audioOffset`,
   interpolated with a `Stopwatch` between audio callbacks so rendering is smooth
   and never runs ahead of sound.
2. **`Conductor`** owns song time + the BPM map; converts time ↔ beat ↔ measure,
   including mid-song BPM changes (§46).
3. **Input events** are timestamped at the OS event against the same `Stopwatch`
   base, then shifted by `inputOffset` before judging (§66).
4. Update runs on the render thread reading immutable note snapshots; audio has
   its own thread.

## Note pipeline

- Immutable `Note` data (`TapNote`, `HoldNote`) → runtime `NoteEntity` (hit/held/judged).
- `INoteJudge` / `INoteRenderer` per type. Phase 1 = Tap + Hold; Slide / Burst /
  Flick / Chain / Special slot in without touching the engine (§18).
- Hold = head judgement + tail judgement + optional mid-hold break (§17).

## Three separate number systems (§25, §32)

| System   | Scope      | Engine (Core)   | Storage             |
|----------|------------|-----------------|---------------------|
| Score    | per play   | `ScoringEngine` | `scores`            |
| PP       | per play   | `PpAlgorithm`   | `performances`      |
| Rating   | player-wide| `RatingEngine`  | derived + `rating_snapshots` |

- **Score** = f(judgement counts, max combo, accuracy weighting, difficulty).
- **PP** pipeline (§31): `calculateBasePP` → `accuracyMultiplier` → `comboMultiplier`
  → `missPenalty` → `technicalMultiplier` → `speedMultiplier` → `readingMultiplier`
  → `calculateFinalPP`. Each pure, each independently tested.
- **Rating** (§29): best-N performance ratings, sorted desc, decay curve
  (`w₁=1.00, w₂≈0.97, …`, coefficients in `BalanceConfig`), combined. Pure function.
- **Skill profile** (§40): per-chart skill-attribute vector from difficulty analysis;
  player axes (Speed / Technical / Reading / Stamina / Accuracy) are weighted
  aggregates over performances. Rule-based first.

## Persistence

Repository pattern (§70) — game logic never sees SQL. WAL mode. Every score save is
one transaction (§11):

```
BEGIN TRANSACTION
  INSERT score
  compute performance → INSERT performance
  recompute Rating    → UPDATE snapshot
  recompute Total/Best PP
  update statistics
  evaluate achievements → INSERT unlocked
COMMIT              -- any failure → ROLLBACK
```

Schema migrations are ordered SQL steps keyed by `PRAGMA user_version`; the runner
is unit-tested against an in-memory database.

### Tables (built out per phase)

`profiles`, `settings`, `songs`, `charts`, `chart_versions`, `scores`, `performances`,
`replays`, `favorites`, `achievements`, `rating_snapshots`, `app_meta`.
`play_history` and `best_performances` are views.

## Filesystem (§9)

```
%LOCALAPPDATA%\LUMEN\        (or ./data/ next to the exe if portable.txt present)
  database/  lumen.db
  charts/    local/  imported/
  songs/
  replays/
  backups/   lumen-backup-YYYY-MM-DD-HHMMSS.lumenbackup
  cache/
  exports/
  settings/
  logs/
```

**Atomic writes (§74):** write `*.tmp` → flush → validate (parse/checksum) →
atomic rename over target. A kill at any point leaves the previous good file intact.

## Editor

- Command pattern for *every* mutation (§87); one undo/redo stack, itself unit-tested.
- Selection model with drag-rectangle multi-select (§89); clipboard keeps note timing
  relative to the playhead (§88).
- Waveform: decode to a mip-mapped min/max peak cache per zoom level.
- Grid: 1/1…1/32 + triplets 1/3, 1/6, 1/12.
- Test Play hands the in-memory chart to the Phase-3 gameplay screen; `Esc` restores
  the exact editor state (§50).
- `ValidateChart()` runs before every export (§52).
- Difficulty analysis → estimated level *and* the PP skill-attribute vector (§53).

## Distribution (§72)

- `dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true`
  → `LUMEN.exe` carrying the application and the whole .NET runtime, so no runtime install
  is needed. Release builds are ReadyToRun; without it the first frame of a play pays to
  JIT the gameplay draw path (§68).
- **Not** `-p:IncludeNativeLibrariesForSelfExtract=true`, which this originally specified.
  MonoGame resolves `SDL2.dll` by looking next to its own assembly, and inside a
  single-file bundle there is no such place, so bundling the natives produces an
  executable that dies on startup with *Failed to load library: SDL2.dll* — and it
  swallows `assets/fonts` into the bundle too, so even a fixed loader would render every
  screen without text. `SDL2.dll`, `soft_oal.dll` and `e_sqlite3.dll` ship beside the game.
- `NAudio` is a meta-package that pulls in `NAudio.WinForms`, which drags the entire
  Microsoft.WindowsDesktop runtime — WPF and WinForms — into a self-contained publish.
  `LUMEN.Audio.csproj` excludes it: 183 MB becomes 86 MB, and the release verification
  fails if it ever comes back.
- Installer: Inno Setup script in `tools/installer/` → `LUMEN-Setup.exe`. Installs to
  `%ProgramFiles%\LUMEN`; data goes to `%LOCALAPPDATA%\LUMEN` at runtime (§73).
- Portable: zip the publish folder → `LUMEN-Portable.zip` (+ `portable.txt`).
- One command builds and then verifies all three: `tools/release.ps1`. See
  [`RELEASE.md`](RELEASE.md) for the §101 checklist it runs.
