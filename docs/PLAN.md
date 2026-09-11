# LUMEN — Implementation Plan

Working title. In-game name set in `GameIdentity`, not hardcoded.
Published plan artifact: <https://claude.ai/code/artifact/cfdcfca4-563e-40c5-b9f9-356cca5a0d31>

## Stack

| Concern            | Choice | Phase |
|--------------------|--------|-------|
| Runtime            | .NET 8 (LTS), C# 12. `net8.0-windows` for app projects, `net8.0` for Core/Data. | 0 |
| Game loop / render | MonoGame 3.8 DesktopGL. Fixed timestep off, vsync off, custom refresh-rate limiter (Phase 1). | 1 |
| Audio              | NAudio + WASAPI (MIT). Sample-accurate position drives the conductor. Fallbacks: ManagedBass, FMOD. | 3 |
| Database           | Microsoft.Data.Sqlite + `bundle_e_sqlite3`. Hand-written SQL, `PRAGMA user_version` migrations, WAL. | 1 |
| Game UI            | Custom lightweight toolkit (layout + theming + tween). | 2 |
| Editor UI          | Dear ImGui via ImGui.NET for docked panels; viewports rendered with MonoGame. | 6 |
| Serialization      | System.Text.Json. | 3 |
| Testing            | xUnit + FluentAssertions. | every phase |
| Distribution       | `dotnet publish` single-file → `LUMEN.exe`; Inno Setup → `LUMEN-Setup.exe`; zip → `LUMEN-Portable.zip`. | 11 |

## Guiding constraints (checked every phase)

1. Fully offline — every core loop works with no network (§3, §75).
2. Everything local — SQLite + files under `%LOCALAPPDATA%\LUMEN\` (§9, §10).
3. Game name is data, not code — `GameIdentity` only (spec preamble).
4. Logic / rendering separated — `LUMEN.Core` has zero engine/DB refs (§68).
5. Score, PP, Rating are three independent systems (§25, §32).
6. `playerId` (UUID) ≠ `displayName`; renames never orphan scores (§6, §80).
7. Nothing copied — original system, UI, formats, assets (preamble).
8. Crash-resilient — WAL + transactions + atomic temp→validate→rename (§11, §74).
9. No phase ships with a TODO. Inspect → Plan → Implement → Typecheck → Test → Build → Run → Fix (§102).

## Phases

Each phase also passes the universal gate: typecheck clean · tests green ·
`dotnet build` clean · app runs · zero TODO carried forward.

### Phase 0 — Toolchain & solution bootstrap ✅
.NET 8 SDK (user-local), `git init`, `.editorconfig`, `.gitignore`, `Directory.Build.props`,
`LUMEN.sln` + 5 projects wired, `GameIdentity`, `LumenPaths`, titled window, `build.ps1`, docs.
**Exit:** `build.ps1 build|test|smoke` all succeed; window opens titled from `GameIdentity`;
data tree created; 9 unit tests green. *(met)*

### Phase 1 — Windows EXE foundation (§1) ✅
Window/display config (`settings/display.json`), `GameClock`, refresh-rate `FrameLimiter`
(SDL2 refresh query + 1 ms timer), perf overlay.
`FileLog` day-rolling logs in `logs/`; `CrashGuard` (AppDomain hook + in-loop try/catch
→ error screen + `crash-*.txt` + Win32 dialog, §96).
`AtomicFile` temp→flush→rename (§74). `Database` (WAL, FK, busy_timeout) +
`MigrationRunner` (`PRAGMA user_version`, per-step transaction) → schema v1 `app_meta`;
`AppMetaStore` (install id, launch tracking).
**Exit met:** offline launch; migrated `lumen.db` v1; `--crashtest` → error screen, no
hard crash, logged + report written; frame limiter holds monitor refresh (240 Hz → 239 fps);
29 unit tests green (identity, paths, atomic file, migrations, database, display config, clock).

### Phase 2 — Player setup & profile (§4–8, §80–83) ✅
`PlayerName` validation (grapheme-counted 1–16, Unicode/emoji, trim + whitespace collapse,
rejects control/format/private-use/lone-surrogate). `Profile` + `ProfileSummary`;
`IProfileRepository` / `ISettingsRepository` in Core, implemented in Data over schema v2
(`profiles`, `settings` with FK cascade). `Session` (active profile via `app_meta`,
restored on launch).
UI toolkit: `Theme`, `InputRouter`/`InputFrame` (window text-input, IME-aware),
`UiRenderer`, `Screen`/`ScreenManager` (stack + fade), `TextField`, `MenuList`, `ScreenChrome`.
Screens: Setup, Welcome, MainMenu, Profile (+ inline rename), Settings (audio/gameplay/
timing/display rows persisted live), Placeholder. `--capture` renders each screen to PNG.
**Exit met:** setup creates a UUID profile; rename keeps `player_id` (test); 20-case name
validation matrix; settings survive a DB reopen (test); 75 unit tests green; all 4 screens
render cleanly (visually verified).

### Phase 3 — Game engine (§15–25) ✅
Core (pure): `BalanceConfig` (+ `balance.json`), `Judgement`, `Chart`/`Note`/`BpmPoint`,
`TempoMap` (time<->beat across tempo changes), `ChartJson` (v1 `.lumenchart`),
`JudgementRule`, `ScoreState` (all-perfect FC = exactly MaxScore), `GameplaySession`
(deterministic: chart + ordered `LaneEvent`s -> judgements), `PlayResult` + grades.
Audio: NAudio/WASAPI `AudioEngine` -> in-memory `AudioClip` (WAV/MP3/OGG decode +
resample), `WasapiAudioTrack` (sample-accurate position - output latency),
`VirtualAudioTrack` (silent fallback, no crash).
Game: `Conductor` (audio position + stopwatch extrapolation, monotonic), `KeyBindings`
(rebindable A/S/D/F), `LaneInputSource` / `IGameplayInput`, `GameplayScreen` (playfield,
falling notes, judgement popups, combo/score HUD, pause, countdown), `ResultScreen` (§36),
`ErrorNoticeScreen` (§96), `TestContent` (synthesised original practice track + chart).
`--autoplay` runs the whole song with perfect input.
**Exit met:** `--autoplay` -> 100.00% / 1,000,000 / x96 / FC / AP end-to-end offline with
audio; determinism test passes across 60/240/143 fps slicing; judgement/accuracy/combo/
score fully `BalanceConfig`-driven; 116 unit tests green; gameplay renders (screenshot).

### Phase 4 — Rating / PP / performance / statistics (§28–34, §37–40) ✅
Core: `PpConfig` / `RatingConfig` (in `balance.json`). `PpAlgorithm` — the 8 methods
(§31) each pure and independently tested. `PerformanceRating` (per-play, feeds the pool).
`RatingEngine` (decay-weighted top-N mean; `ComputeTotalPp` decayed sum). `SkillProfile`
(5 axes from best performances, §40). `DifficultyAnalyzer` (light rule-based attributes
+ estimated level, §53 preview). `ChartKey` (stable per-chart id until Phase 5/7).
Data: schema v3 (`scores`, `performances`, `rating_snapshots`). `ScoreRepository.Save`
= one transaction (§11): insert score → insert performance → recompute snapshot;
rollback leaves nothing. Best Performances, statistics, skill profile, recent plays.
Game: `GameContext.Scores`; `GameplayScreen` saves on finish + accrues play time;
`ResultScreen` shows +PP and the Rating change with a staged count-up (§38) and
NEW PERSONAL BEST / PP RECORD / RATING RECORD badges (§37); Profile + Main Menu show
real numbers.
**Exit met:** PP/Rating suites with boundary + monotonicity + regression-locked values;
`A_failed_save_rolls_back_completely` proves atomic rollback; `--autoplay` twice shows
`rating 0.00->4.95 PB PPREC` then `4.95->4.95` (no double-count); profile screen shows
all headline numbers + skill axes (screenshot); 174 unit tests green.

### Phase 5 — Song select (§26–27, §35, §39, §62–63) ✅
Core: `LibraryChart` / `SongGroup` / `ChartStats`; `DifficultyBands` (§26–27, bands are
display only — every calculation still uses the float level); `LibraryQuery`, a pure
group/filter/sort used by the screen and the tests alike. `ILibraryRepository`.
Data: schema v4 (`charts`, `favorites`, `play_history` view). `LibraryScanner` reconciles
the index with the chart folders — the files stay the source of truth, a missing file
drops its row, and one unreadable chart never fails the scan. `ScoreRepository` gains
`GetChartStats` (best score / accuracy / PP per chart in one query), `GetChartRanking`
(one row per profile at its best score) and `GetChartRank`.
Game: `SongSelectScreen` — song list with level ranges, difficulty chips, YOUR BEST, and
the local ranking with the YOU row on the same screen; live search over title/artist/
charter, six sort keys, favourites, and selection that survives a play. PLAY and
SONG SELECT both open it.
**Exit met:** 87 new tests (261 total, green) covering grouping, search folding,
level-range filtering, every sort key, favourites-per-profile, scanner add/update/prune/
skip-broken, ranking order + YOU marking + rank lookup; `--capture` shows the library
with three songs, five charts, a best-score panel and a `YOU #1` ranking row; selecting a
chart pushes the Phase-3 gameplay screen.

### Phase 6 — Chart editor (§42–51, §86–91)
ImGui shell; audio import + preview; waveform; timeline + zoom + playback speed.
BPM (single + changes); offsets; grid snap incl. triplets.
Note place/delete/move; drag multi-select; relative copy/paste; undo/redo (Command pattern).
Configurable shortcuts; Test Play ⇄ editor; save to local library; creator auto-set.
**Exit:** audio → notes → Test Play → save → Song Select → playable; every mutation
undo/redo-able (command-stack test); waveform aligns to audio position.

### Phase 7 — Chart system (§52–61, §98)
`.lumenchart` / `.lumen` / `.lumenbackup` formats; parser + validator.
Difficulty analysis feeding displayed level + PP skill attributes.
Chart versioning with history; drag-drop import; export; format migration layer v1→vN;
metadata + tags.
**Exit:** export `.lumen` → wipe → import → identical chart & difficulty; validator flags
overlaps/missing BPM/bad timing/missing metadata; difficulty estimate stable & tested;
migration test.

### Phase 8 — Replay & achievements (§41, §64)
Replay recording (input + judgement stream + seed); deterministic playback; list + viewer;
optional export. Achievement engine with the spec's local set; unlock UI; on profile.
**Exit:** replay reproduces identical score & judgements (determinism test);
achievements unlock at correct thresholds; replays survive restart.

### Phase 9 — Backup / restore / data migration (§12–13, §74)
Auto-backup on schedule + on version change; manual Create/Restore Backup.
`.lumenbackup` export/import full round-trip. Open-folder actions. Atomic-write audit.
**Exit:** export on A → import on clean B → identical everything; restore from auto-backup;
process killed mid-write → DB still opens.

### Phase 10 — Polish / optimization / accessibility (§66–68, §81–85, §95–96)
Frame-pacing & input-latency profiling; 144/165/240 Hz. Calibration screen.
Accessibility pass (§67). Result-screen animation polish. Tutorial + first-play flow.
Error-handling & logging passes.
**Exit:** no frame over budget during a 3-min song; reduced-motion honoured everywhere;
calibration offset applies; scripted new-user walkthrough reaches first score without docs.

### Phase 11 — Windows release (§72–73, §101)
Self-contained single-file `LUMEN.exe`; Inno Setup `LUMEN-Setup.exe`; `LUMEN-Portable.zip`.
Version stamping; §101 build-verification; offline smoke-test matrix.
**Exit:** all three artifacts from one script; clean VM: installer → full loop offline;
portable zip runs without install; §101 fully ticked.
