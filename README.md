# LUMEN

### 遊ぶ。競う。作る。 — ぜんぶ、ひとつの `LUMEN.exe` に。<br>Play. Compete. Create. — all in one `LUMEN.exe`.

> **説明書はいらない。100時間後も、まだ伸びる。**<br>
> **No manual to read. Still getting better a hundred hours later.**

完全オフラインの Windows 用リズムゲーム。総合レーティングと1プレイごとの PP、そして譜面エディタを内蔵しています。ダウンロードした後は、ネットワーク接続を一切必要としません。

A fully-offline Windows rhythm game with a total Rating, per-play Performance Points (PP),
and a built-in chart editor. After the download it never needs a network connection again.

> Working title — the in-game name is set in `src/LUMEN.Core/GameIdentity.cs`, never hardcoded.

---

## ダウンロード / Download

**[→ 最新版をダウンロード / Get the latest release](https://github.com/7g3n/LUMEN/releases/latest)**

| | |
|---|---|
| **[LUMEN-Setup-0.1.0.exe](https://github.com/7g3n/LUMEN/releases/download/v0.1.0/LUMEN-Setup-0.1.0.exe)** (28 MB) | インストーラー。ふつうはこちら。<br>Installer. Start here. |
| **[LUMEN-Portable-0.1.0.zip](https://github.com/7g3n/LUMEN/releases/download/v0.1.0/LUMEN-Portable-0.1.0.zip)** (35 MB) | 展開して実行。USB メモリでも動き、PC に何も残しません。<br>Unzip and run. Works from a USB stick and leaves nothing behind. |

Windows 10 / 11 (x64)。**.NET のインストールは不要です** — ランタイムは `LUMEN.exe` に同梱されています。
Windows 10 / 11 (x64). **No .NET install required** — the runtime is inside `LUMEN.exe`.

<details>
<summary>SHA-256</summary>

```
26d3a9a2a790dae11248a24caffc4f7e1cdc724bbe320d164cdd101a3fa8f74e  LUMEN-Setup-0.1.0.exe
ecb69a4c5d7e89b3bf513243926e201cf9e9e7a1b7603dc9d2c51b2008ed3b88  LUMEN-Portable-0.1.0.zip
```
</details>

---

## 導入方法

### インストーラーで入れる

1. **[LUMEN-Setup-0.1.0.exe](https://github.com/7g3n/LUMEN/releases/download/v0.1.0/LUMEN-Setup-0.1.0.exe)** をダウンロードして実行します。
2. 「**WindowsによってPCが保護されました**」と出たら、「**詳細情報**」→「**実行**」を押してください。このインストーラーには**コード署名がありません**。個人開発のソフトに証明書が付いていないときの通常の警告です。気になる場合は上の SHA-256 でファイルを検証できます。
3. インストール先を選びます。**「このユーザーのみ」を選べば管理者権限は要りません。**
4. 起動して、名前を入れます。あとはチュートリアルからそのまま最初の曲が始まります。

**曲を用意する必要はありません。** 練習曲はゲーム自身が合成して作ります。

### 持ち運んで使う（インストールしない）

1. **[LUMEN-Portable-0.1.0.zip](https://github.com/7g3n/LUMEN/releases/download/v0.1.0/LUMEN-Portable-0.1.0.zip)** をダウンロードして、好きな場所（USB メモリでも可）に展開します。
2. `LUMEN.exe` をダブルクリックします。

同梱の `portable.txt` がある間、セーブデータは `LUMEN.exe` の隣の `data` フォルダに入ります。PC 側には何も残りません。

### 遊びかた

| | |
|---|---|
| `A` `S` `D` `F` | 4つのレーンを叩く（設定で変更できます） |
| `Esc` | 一時停止 / 戻る |
| `Enter` | 決定 |

ノーツが判定ラインに重なる瞬間にキーを押します。ラインに近いほど良い判定になります。

### セーブデータの場所

`%LOCALAPPDATA%\LUMEN\` にプロファイル、スコア、譜面、曲、リプレイが入ります。**アンインストールしても消えません**（削除するか訊かれ、既定は「消さない」です）。別の PC に移すときは、ゲーム内の 設定 → データ からバックアップを1ファイルに書き出せます。

### うまく動かないとき

- **起動しない / 画面が真っ黒**: `%LOCALAPPDATA%\LUMEN\logs\` のログを見てください。落ちた場合は同じ場所に `crash-*.txt` が残ります。
- **音が出ない**: ゲームは音声デバイスがなくても動きます（画面に `SILENT` と出ます）。Windows の音声出力先を確認してください。
- **タイミングが合わない**: 設定 → **Timing Calibration** で、鳴っているクリック音に合わせて叩くだけで自分に合った値が入ります。
- **動きが速すぎる / 見づらい**: 設定の Accessibility に、動きの抑制・ハイコントラスト・判定の形状表示などがあります。

---

## Getting started

### Install it

1. Download and run **[LUMEN-Setup-0.1.0.exe](https://github.com/7g3n/LUMEN/releases/download/v0.1.0/LUMEN-Setup-0.1.0.exe)**.
2. Windows will say **"Windows protected your PC"**. Choose **More info** → **Run anyway**. The installer is **not code-signed** — this is the normal warning for independent software without a certificate. You can verify the download against the SHA-256 above if you would rather check.
3. Choose where to install. **Picking "Just for me" needs no administrator rights.**
4. Launch it and type a name. A short tutorial follows, and then your first song starts.

**You do not need to supply any music.** The practice track is synthesised by the game itself.

### Or run it without installing

1. Download **[LUMEN-Portable-0.1.0.zip](https://github.com/7g3n/LUMEN/releases/download/v0.1.0/LUMEN-Portable-0.1.0.zip)** and unzip it anywhere — a USB stick is fine.
2. Double-click `LUMEN.exe`.

While the bundled `portable.txt` sits beside it, everything LUMEN saves goes into the `data`
folder next to the executable. Nothing is left on the machine you played on.

### How to play

| | |
|---|---|
| `A` `S` `D` `F` | the four lanes (rebindable in Settings) |
| `Esc` | pause / back |
| `Enter` | confirm |

Press a lane's key as its note reaches the hit line. The closer to the line, the better the judgement.

### Where your data lives

`%LOCALAPPDATA%\LUMEN\` holds your profiles, scores, charts, songs and replays.
**Uninstalling leaves them alone** — it asks, and the default is to keep them. To move to
another machine, Settings → Data writes the lot to a single backup file.

### If something goes wrong

- **It will not start, or the window is black**: look in `%LOCALAPPDATA%\LUMEN\logs\`. If it crashed, a `crash-*.txt` report is in the same folder.
- **No sound**: the game runs without an audio device (it shows `SILENT` on screen). Check which output Windows is using.
- **The timing feels off**: Settings → **Timing Calibration**. Tap along with the click and it works out your offset for you.
- **Too much motion, or hard to read**: Settings → Accessibility has reduced motion, high contrast and shape cues beside every judgement.

---

## What's in it / 中身

- **遊ぶ / Play** — Tap and Hold notes, five judgement tiers, frame pacing verified up to 240 Hz, timing calibration
- **競う / Compete** — a total Rating and per-play PP, personal bests, local rankings, achievements, and replays that reproduce a play exactly
- **作る / Create** — a chart editor with total undo, a beat grid that follows tempo changes, Test Play, and `.lumen` package export

---

## Status

**Phase 11 — Windows release: complete.** One command builds all three artifacts and then
runs the ones it built:

```powershell
.\tools\release.ps1
```

`LUMEN-0.1.0-win-x64\` (86 MB), `LUMEN-Portable-0.1.0.zip` (35 MB) and
`LUMEN-Setup-0.1.0.exe` (28 MB), followed by twenty checks the script exits on: that the
game starts, plays a chart end to end without a frame over budget, catches a deliberate
crash, keeps its data in `%LOCALAPPDATA%` and nothing in its install folder, moves that
data beside the executable when `portable.txt` is present, and needs no .NET installed.
Three things turned up that would otherwise have shipped: the single-file publish as
originally specified could not start at all (MonoGame cannot find `SDL2.dll` inside a
bundle), an unused NAudio dependency was dragging all of WPF and WinForms into the
download and doubling it, and a silent uninstall deleted the player's profiles, scores and
charts — a confirmation prompt is answered Yes when nobody is there to see it. 541 unit
tests green. See [`docs/RELEASE.md`](docs/RELEASE.md).

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

## Building from source

- Windows 10 / 11 (x64)
- .NET 8 SDK — this machine uses a user-local install at `%USERPROFILE%\.dotnet`
  (the machine-wide runtime has no SDK). `build.ps1` handles the PATH.

```powershell
.\build.ps1 build     # compile everything
.\build.ps1 test      # run the unit tests
.\build.ps1 run       # launch the game
.\build.ps1 smoke     # launch, verify the window comes up, exit
.\build.ps1 crashtest # launch, throw in the loop, verify the error screen catches it
.\build.ps1 publish   # self-contained LUMEN.exe -> .\publish\
.\build.ps1 release   # every release artifact, then verify them -> .\dist\
```

`release` is the one that matters before shipping: it refuses to build from a red test
suite, produces the installer, the portable zip and the published folder, and then runs
the game it just built through the checks in [`docs/RELEASE.md`](docs/RELEASE.md).

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
| `LUMEN.Audio`  | `net8.0-windows` | Low-latency audio playback and the audio clock. |
| `LUMEN.Game`   | `net8.0-windows` | MonoGame application → `LUMEN.exe`. |
| `LUMEN.Tests`  | `net8.0-windows` | xUnit. |

## Data location

User data lives under `%LOCALAPPDATA%\LUMEN\` (installed) or `./data/` next to the
executable when a `portable.txt` sentinel is present. Game binaries never write elsewhere.
