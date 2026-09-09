# LUMEN — Chart & Package Formats

All formats are original to LUMEN. Format version is independent of the app version
(spec §97) and is stamped in every file for the migration layer (§98).

Current: **chart format v1**, **backup format v1**.

## `.lumenchart` — a single chart

UTF-8 JSON. One difficulty on one song. Does **not** embed audio.

```jsonc
{
  "formatVersion": 1,
  "id": "b1b6...uuid",
  "meta": {
    "title": "…",
    "artist": "…",
    "creator": "7g3",              // auto-set from active profile, editable (§78)
    "difficultyName": "MASTER",    // author-chosen label (§91)
    "difficultyLevel": 14.7,       // float (§26)
    "description": "…",
    "tags": ["stream", "technical"],
    "coverFile": "cover.png",      // resolved inside a .lumen package; may be null
    "audioFile": "song.ogg",       // resolved inside a .lumen package
    "durationMs": 128000,
    "previewMs": 42000
  },
  "timing": {
    "chartOffsetMs": 0,            // shifts the whole note grid vs. audio (§47)
    "bpm": [                       // ordered; first entry at 0 ms (§46)
      { "atMs": 0,     "bpm": 180.0 },
      { "atMs": 32000, "bpm": 200.0 }
    ]
  },
  "notes": [
    { "type": "tap",  "ms": 1000,  "lane": 0 },
    { "type": "hold", "ms": 2000,  "lane": 2, "endMs": 2750 }
  ],
  "analysis": {                    // written by the editor's difficulty analysis (§53); advisory
    "estimatedLevel": 14.72,
    "attributes": { "speed": 13.8, "technical": 14.2, "reading": 13.4,
                    "stamina": 12.9, "reaction": 13.1, "patternComplexity": 14.0 },
    "noteCount": 1268
  }
}
```

Note types reserved for later phases: `slide`, `burst`, `flick`, `chain`, `special` (§18).

## `.lumen` — a song + chart(s) package (§55)

A ZIP archive. Hand one file to a friend; they import and play immediately (§56).

```
MySong.lumen  (zip)
  manifest.json          // package metadata + content index + integrity hashes
  song.ogg               // or .wav / .mp3
  cover.png              // optional
  charts/
    master.lumenchart
    expert.lumenchart
```

`manifest.json`:

```jsonc
{
  "formatVersion": 1,
  "package": { "title": "…", "artist": "…", "creator": "7g3",
               "createdUtc": "2026-09-09T12:00:00Z", "lumenVersion": "0.1.0" },
  "audio": { "file": "song.ogg", "sha256": "…", "durationMs": 128000 },
  "cover": { "file": "cover.png", "sha256": "…" },
  "charts": [
    { "file": "charts/master.lumenchart", "sha256": "…",
      "difficultyName": "MASTER", "difficultyLevel": 14.7 }
  ]
}
```

Import copies audio into `songs/`, charts into `charts/imported/`, and registers rows
in `songs` / `charts` / `chart_versions`.

## `.lumenbackup` — full data backup / machine migration (§12, §13)

A ZIP archive containing everything needed to reconstruct a player's data on another
machine (§13). Also the auto-backup format, named `lumen-backup-YYYY-MM-DD-HHMMSS.lumenbackup`.

```
lumen-backup-2026-09-09-143005.lumenbackup  (zip)
  manifest.json          // formatVersion, lumenVersion, createdUtc, profile summary, counts
  database.sql           // full logical dump (portable across SQLite versions)
  charts/                // every local + imported .lumenchart
  songs/                 // referenced audio
  replays/               // replay blobs
  settings/              // settings snapshots, key bindings, offsets
```

Restore validates `manifest.json`, then rebuilds the database from `database.sql`
inside a transaction and copies files into place with atomic writes.

## Migration layer (§98)

`ChartMigrator` upgrades any `formatVersion < GameIdentity.ChartFormatVersion` to
current on load, step by step (`v1→v2→v3`). Old files always remain readable.
Each migration step has a round-trip unit test.
