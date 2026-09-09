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

### Phase 2 — Player setup & profile (§4–8, §80–83)
First-run setup + name validation (1–16, Unicode, trim, no empty/control chars).
Profile creation (`playerId` UUID + `displayName`). `ProfileRepository`, `SettingsRepository`.
Rename preserving score links. Multi-profile data model, single-profile UI. Settings shell.
**Exit:** setup → profile row; rename keeps `player_id`; validation test matrix; settings persist.

### Phase 3 — Game engine (§15–25)
NAudio engine; WAV/OGG/MP3 decode; sample-accurate position; `Conductor` + BPM map.
`InputProvider` + Keyboard + Mouse; rebindable lane keys. Tap + Hold notes.
Judgement windows / accuracy / combo / score — all `BalanceConfig`-driven.
Gameplay screen + playfield; pause/fail/complete; minimal result screen.
**Exit:** bundled test chart plays end-to-end offline; config-driven & tested; A/V sync;
deterministic judgement for fixed input timings.

### Phase 4 — Rating / PP / performance / statistics (§28–34, §37–40)
`RatingEngine` (weighted best-N, config decay, pure). `PPAlgorithm` (8 methods, pure, each tested).
Atomic score-save transaction (§11). Statistics aggregation. Best Performances.
PB / PP-record / Rating-record / FC detection. Result-screen count-up animation.
Rule-based skill profile.
**Exit:** large PP & Rating test suites (boundary + monotonicity + regression-locked);
crash mid-save → clean rollback; profile shows all headline numbers; skill axes computed.

### Phase 5 — Song select (§26–27, §35, §39, §62–63)
Library scan → DB; Song Select with per-chart best score/accuracy/PP; difficulty banding.
Sort/filter/search. Local Ranking from SQLite with a YOU row. Favourites. Play history.
**Exit:** charts listed with best stats; local ranking shows rank; favourites persist;
selecting a chart launches Phase-3 gameplay.

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
