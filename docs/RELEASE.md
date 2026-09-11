# Releasing LUMEN

One command builds every artifact and then checks the artifacts it just built:

```powershell
.\tools\release.ps1
```

It refuses to build from a red test suite, publishes, packages, and runs the verification
below against the published executable — not the one in `bin\`. Anything that fails makes
the script exit non-zero.

| Task | What it does |
|------|--------------|
| `.\tools\release.ps1` | tests → publish → portable zip → installer → verify |
| `.\tools\release.ps1 publish` | just the published folder |
| `.\tools\release.ps1 portable` | just the zip |
| `.\tools\release.ps1 installer` | just the installer (needs Inno Setup) |
| `.\tools\release.ps1 verify` | re-run the checks against what is already in `dist\` |
| `.\tools\release.ps1 clean` | delete `dist\` |

## What ships

```
dist\LUMEN-<version>-win-x64\    the game
dist\LUMEN-Portable-<version>.zip
dist\LUMEN-Setup-<version>.exe
```

The published folder is:

```
LUMEN.exe          the game and the .NET runtime it needs, in one file
SDL2.dll           MonoGame's window, input and GL context
soft_oal.dll       MonoGame's OpenAL fallback
e_sqlite3.dll      SQLite
assets\fonts\      three bundled OFL fonts
*.pdb              symbols
```

**On "one `LUMEN.exe`".** The managed application and the entire .NET runtime are inside
`LUMEN.exe`; nobody needs to install .NET, which is the promise that matters. The three
native libraries sit beside it rather than inside it, and that is deliberate rather than
an oversight: MonoGame resolves `SDL2.dll` by looking next to its own assembly, and inside
a single-file bundle there is no such place. Bundling them with
`IncludeNativeLibrariesForSelfExtract` produces an executable that starts and immediately
dies with *Failed to load library: SDL2.dll* — and it also swallows `assets\fonts` into the
bundle, so even a patched loader would render every screen without text. Three DLLs beside
the game is the honest shape of this application, and both the installer and the zip carry
them.

**The symbols ship on purpose.** LUMEN has no telemetry and no crash uploader, so a crash
report is something a player sends by hand. With the `.pdb` files present those reports
carry line numbers; without them they carry addresses. 170 KB is a cheap price for a
readable bug report.

**No Windows Desktop runtime.** `NAudio` is a meta-package, and one of its pieces is
`NAudio.WinForms` — a few UI controls this game has no use for, which drag all of WPF and
WinForms into a self-contained publish. `LUMEN.Audio.csproj` excludes it explicitly. That
one line is the difference between a 183 MB download and an 86 MB one, and the verification
fails if the desktop runtime ever comes back.

## Version stamping

The version lives in exactly one place — `<Version>` in `Directory.Build.props`. Everything
else asks: the assemblies are stamped from it, `GameIdentity.Version` reads it back off the
assembly, and `tools\release.ps1` reads it to name the artifacts and to pass
`/DAppVersion` to Inno Setup.

The SDK also appends the commit hash, so a build reports itself as `0.1.0+b00fb85` in the
title bar, the log header and every crash report. That suffix is diagnostic only:
`GameIdentity.Version` is `0.1.0`, which is what chart manifests, backups and the
data-version check compare against, and it is shortened to seven characters for display
because nobody reads forty off a title bar.

To cut a release, change `<Version>` and run the script. There is nothing else to update.

## Verification (§101)

Run by `.\tools\release.ps1 verify`. Every line below is a check the script performs and
fails on; none of them are asserted in prose.

*The spec's §101 list is not reproduced verbatim here — it was given in the project brief
rather than committed to the repo. What follows is the checklist derived from the
constraints the repo does record: fully offline, data under `%LOCALAPPDATA%`, portable mode,
no runtime install, crash-resilient, and the frame budget from §68.*

**What ships**
- [x] `LUMEN.exe` is present
- [x] `SDL2.dll`, `soft_oal.dll` and `e_sqlite3.dll` ship beside it
- [x] the three bundled fonts ship (without them every screen renders blank)
- [x] no WPF/WinForms runtime is shipped
- [x] self-contained: no `runtimeconfig.json` beside the exe, so no .NET install is needed

**That it works**
- [x] starts and renders (`--smoke`)
- [x] reports the version it was built as
- [x] plays the practice chart end to end and scores it (`--autoplay`)
- [x] no frame over budget from the game's own work (§68)
- [x] the practice track is generated on a machine that has never run LUMEN (§81)
- [x] a deliberate crash is caught and a report written (`--crashtest`, §96)
- [x] the crash report names the build it came from

**Where it puts things (§73)**
- [x] user data lands under `%LOCALAPPDATA%\LUMEN`
- [x] nothing is written into the install folder
- [x] with `portable.txt` beside it, data lands next to the executable instead

**The artifacts**
- [x] the portable zip carries `LUMEN.exe` and its `portable.txt`
- [x] the installer builds

Two notes on how the checks are run, because both were mistakes before they were rules:

- **Only `--smoke` runs against the real `%LOCALAPPDATA%`.** It reaches the setup screen
  and exits, creating the folder tree and an empty database. Everything that would write a
  profile, a score or a replay runs in a throwaway portable copy instead. A release check
  has no business leaving a play in somebody's own game data — the first version of this
  script did exactly that.
- **`Start-Process -PassThru` needs its handle touched** before `ExitCode` can be read, or
  every check reads a blank exit code and fails a run that in fact succeeded. A
  verification that cries wolf is worse than none.

## Offline

Nothing in the game or the release path reaches the network: no updater, no telemetry, no
runtime download, no CDN for fonts or audio. The practice song is synthesised by the game
itself and the fonts are bundled, so a machine that has never been online can install,
launch and play. The verification above runs with no connection of any kind, which is how
it is normally run.

## Prerequisites

- .NET 8 SDK. On this machine it is a user-local install at `%USERPROFILE%\.dotnet`; both
  `build.ps1` and `tools\release.ps1` put it on `PATH` themselves.
- [Inno Setup 6](https://jrsoftware.org/isinfo.php) for the installer:
  `winget install --id JRSoftware.InnoSetup`. Without it the script builds the other two
  artifacts, says clearly that the installer was skipped, and does not pretend otherwise.

## The installer

`tools\installer\LUMEN.iss`. Installs to `%ProgramFiles%\LUMEN`, needs elevation for that
and never again — the game itself runs unelevated and writes only to `%LOCALAPPDATA%`.

`AppId` is a fixed GUID and must never change: it is what makes the next release upgrade
this install rather than sit beside it. A test in `InstallerScriptTests` pins it.

Uninstalling leaves your data alone unless you say otherwise. It asks once, defaulting to
**No**, because losing a rating and a chart library to an uninstall you meant as an upgrade
is not a recoverable mistake.
