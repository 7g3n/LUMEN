namespace Lumen.Data.Migrations;

/// <summary>
/// The ordered list of schema migrations. Append only — never edit or reorder an
/// existing entry once it has shipped. Tables are introduced by the phase that needs
/// them (profiles in Phase 2, scores/performances in Phase 3-4, and so on).
/// </summary>
public static class SchemaMigrations
{
    public static readonly IReadOnlyList<Migration> All = new List<Migration>
    {
        Migration.Sql(1, "app_meta",
            """
            CREATE TABLE app_meta (
                key   TEXT PRIMARY KEY,
                value TEXT NOT NULL
            ) WITHOUT ROWID;
            """),

        Migration.Sql(2, "profiles_and_settings",
            """
            CREATE TABLE profiles (
                player_id           TEXT PRIMARY KEY,
                display_name        TEXT NOT NULL,
                avatar_path         TEXT,
                created_utc         TEXT NOT NULL,
                last_played_utc     TEXT NOT NULL,
                total_play_time_ms  INTEGER NOT NULL DEFAULT 0
            ) WITHOUT ROWID;

            CREATE INDEX ix_profiles_last_played ON profiles (last_played_utc DESC);

            CREATE TABLE settings (
                player_id  TEXT NOT NULL REFERENCES profiles (player_id) ON DELETE CASCADE,
                key        TEXT NOT NULL,
                value      TEXT NOT NULL,
                PRIMARY KEY (player_id, key)
            ) WITHOUT ROWID;
            """),

        Migration.Sql(3, "scores_performances_snapshots",
            """
            CREATE TABLE scores (
                score_id          TEXT PRIMARY KEY,
                player_id         TEXT NOT NULL REFERENCES profiles (player_id) ON DELETE CASCADE,
                chart_key         TEXT NOT NULL,
                chart_title       TEXT NOT NULL,
                chart_artist      TEXT NOT NULL,
                chart_creator     TEXT NOT NULL DEFAULT '',
                difficulty_name   TEXT NOT NULL,
                difficulty_level  REAL NOT NULL,
                score             INTEGER NOT NULL,
                accuracy          REAL NOT NULL,
                max_combo         INTEGER NOT NULL,
                perfect           INTEGER NOT NULL,
                great             INTEGER NOT NULL,
                good              INTEGER NOT NULL,
                bad               INTEGER NOT NULL,
                miss              INTEGER NOT NULL,
                full_combo        INTEGER NOT NULL,
                all_perfect       INTEGER NOT NULL,
                grade             TEXT NOT NULL,
                played_utc        TEXT NOT NULL
            ) WITHOUT ROWID;

            CREATE INDEX ix_scores_player_chart ON scores (player_id, chart_key);
            CREATE INDEX ix_scores_player_played ON scores (player_id, played_utc DESC);

            CREATE TABLE performances (
                performance_id      TEXT PRIMARY KEY,
                score_id            TEXT NOT NULL REFERENCES scores (score_id) ON DELETE CASCADE,
                player_id           TEXT NOT NULL REFERENCES profiles (player_id) ON DELETE CASCADE,
                chart_key           TEXT NOT NULL,
                pp                  REAL NOT NULL,
                performance_rating  REAL NOT NULL,
                base_pp             REAL NOT NULL,
                acc_mul             REAL NOT NULL,
                combo_mul           REAL NOT NULL,
                miss_mul            REAL NOT NULL,
                tech_mul            REAL NOT NULL,
                speed_mul           REAL NOT NULL,
                read_mul            REAL NOT NULL,
                accuracy            REAL NOT NULL,
                skill_attributes_json TEXT NOT NULL,
                computed_utc        TEXT NOT NULL
            ) WITHOUT ROWID;

            CREATE INDEX ix_perf_player_pp ON performances (player_id, pp DESC);
            CREATE INDEX ix_perf_player_chart ON performances (player_id, chart_key);

            CREATE TABLE rating_snapshots (
                snapshot_id  TEXT PRIMARY KEY,
                player_id    TEXT NOT NULL REFERENCES profiles (player_id) ON DELETE CASCADE,
                rating       REAL NOT NULL,
                total_pp     REAL NOT NULL,
                best_pp      REAL NOT NULL,
                computed_utc TEXT NOT NULL
            ) WITHOUT ROWID;

            CREATE INDEX ix_snapshots_player ON rating_snapshots (player_id, computed_utc DESC);
            """),

        Migration.Sql(4, "library_and_favorites",
            """
            CREATE TABLE charts (
                chart_key        TEXT PRIMARY KEY,
                title            TEXT NOT NULL,
                artist           TEXT NOT NULL,
                creator          TEXT NOT NULL DEFAULT '',
                difficulty_name  TEXT NOT NULL,
                difficulty_level REAL NOT NULL,
                note_count       INTEGER NOT NULL DEFAULT 0,
                hold_count       INTEGER NOT NULL DEFAULT 0,
                lane_count       INTEGER NOT NULL DEFAULT 4,
                duration_ms      REAL NOT NULL DEFAULT 0,
                preview_ms       REAL NOT NULL DEFAULT 0,
                audio_file       TEXT NOT NULL DEFAULT '',
                chart_path       TEXT NOT NULL,
                audio_path       TEXT NOT NULL DEFAULT '',
                source           TEXT NOT NULL DEFAULT 'Local',
                attributes_json  TEXT NOT NULL DEFAULT '{}',
                added_utc        TEXT NOT NULL,
                updated_utc      TEXT NOT NULL
            ) WITHOUT ROWID;

            CREATE INDEX ix_charts_song  ON charts (title, artist);
            CREATE INDEX ix_charts_level ON charts (difficulty_level);

            CREATE TABLE favorites (
                player_id   TEXT NOT NULL REFERENCES profiles (player_id) ON DELETE CASCADE,
                chart_key   TEXT NOT NULL,
                created_utc TEXT NOT NULL,
                PRIMARY KEY (player_id, chart_key)
            ) WITHOUT ROWID;

            CREATE VIEW play_history AS
                SELECT s.score_id, s.player_id, s.chart_key, s.chart_title, s.chart_artist,
                       s.chart_creator, s.difficulty_name, s.difficulty_level, s.score,
                       s.accuracy, s.max_combo, s.grade, s.full_combo, s.all_perfect,
                       COALESCE(p.pp, 0) AS pp, s.played_utc
                FROM scores s
                LEFT JOIN performances p ON p.score_id = s.score_id;
            """),

        Migration.Sql(5, "chart_versions",
            """
            CREATE TABLE chart_versions (
                chart_id         TEXT    NOT NULL,
                version          INTEGER NOT NULL,
                saved_utc        TEXT    NOT NULL,
                note_count       INTEGER NOT NULL DEFAULT 0,
                difficulty_level REAL    NOT NULL DEFAULT 0,
                title            TEXT    NOT NULL DEFAULT '',
                difficulty_name  TEXT    NOT NULL DEFAULT '',
                document         TEXT    NOT NULL,
                PRIMARY KEY (chart_id, version)
            ) WITHOUT ROWID;

            CREATE INDEX ix_chart_versions_saved ON chart_versions (chart_id, saved_utc DESC);
            """),
    };
}
