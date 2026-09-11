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

        Migration.Sql(6, "replays_and_achievements",
            """
            CREATE TABLE replays (
                replay_id        TEXT PRIMARY KEY,
                player_id        TEXT NOT NULL REFERENCES profiles (player_id) ON DELETE CASCADE,
                score_id         TEXT,
                chart_key        TEXT NOT NULL,
                chart_id         TEXT,
                title            TEXT NOT NULL DEFAULT '',
                artist           TEXT NOT NULL DEFAULT '',
                difficulty_name  TEXT NOT NULL DEFAULT '',
                difficulty_level REAL NOT NULL DEFAULT 0,
                score            INTEGER NOT NULL DEFAULT 0,
                accuracy         REAL NOT NULL DEFAULT 0,
                max_combo        INTEGER NOT NULL DEFAULT 0,
                grade            TEXT NOT NULL DEFAULT '',
                full_combo       INTEGER NOT NULL DEFAULT 0,
                event_count      INTEGER NOT NULL DEFAULT 0,
                recorded_utc     TEXT NOT NULL,
                file_name        TEXT NOT NULL
            ) WITHOUT ROWID;

            CREATE INDEX ix_replays_player ON replays (player_id, recorded_utc DESC);
            CREATE INDEX ix_replays_chart  ON replays (chart_key, score DESC);

            CREATE TABLE achievements (
                player_id      TEXT NOT NULL REFERENCES profiles (player_id) ON DELETE CASCADE,
                achievement_id TEXT NOT NULL,
                unlocked_utc   TEXT NOT NULL,
                PRIMARY KEY (player_id, achievement_id)
            ) WITHOUT ROWID;
            """),
        // Tournaments live entirely alongside normal play rather than inside it. Nothing
        // here references `scores`, and nothing in `scores` references any of this: a
        // tournament result is a separate fact about a separate competition, and the
        // moment the two share a table somebody's everyday rating starts moving because
        // an organiser picked a hard chart.
        Migration.Sql(7, "tournaments",
            """
            CREATE TABLE tournaments (
                tournament_id  TEXT PRIMARY KEY,
                name           TEXT NOT NULL,
                description    TEXT NOT NULL DEFAULT '',
                organizer      TEXT NOT NULL DEFAULT '',
                format         INTEGER NOT NULL,
                status         INTEGER NOT NULL DEFAULT 0,
                rules_json     TEXT NOT NULL,
                rule_hash      TEXT NOT NULL DEFAULT '',
                game_version   TEXT NOT NULL DEFAULT '',
                random_seed    INTEGER NOT NULL DEFAULT 0,
                created_utc    TEXT NOT NULL,
                started_utc    TEXT,
                finished_utc   TEXT,
                winner_player_id TEXT
            ) WITHOUT ROWID;

            CREATE INDEX ix_tournaments_created ON tournaments (created_utc DESC);

            -- display_name is a snapshot, not a join: players rename themselves, and a
            -- bracket that silently relabels a finished match no longer shows what happened.
            CREATE TABLE tournament_participants (
                tournament_id TEXT NOT NULL REFERENCES tournaments (tournament_id) ON DELETE CASCADE,
                player_id     TEXT NOT NULL,
                display_name  TEXT NOT NULL,
                seed          INTEGER NOT NULL DEFAULT 0,
                status        INTEGER NOT NULL DEFAULT 0,
                joined_utc    TEXT NOT NULL,
                PRIMARY KEY (tournament_id, player_id)
            ) WITHOUT ROWID;

            -- The hashes are what make "the chart that was played" a checkable claim.
            CREATE TABLE tournament_songs (
                tournament_id   TEXT NOT NULL REFERENCES tournaments (tournament_id) ON DELETE CASCADE,
                chart_key       TEXT NOT NULL,
                title           TEXT NOT NULL DEFAULT '',
                difficulty_name TEXT NOT NULL DEFAULT '',
                level           REAL NOT NULL DEFAULT 0,
                chart_hash      TEXT NOT NULL DEFAULT '',
                audio_hash      TEXT NOT NULL DEFAULT '',
                category        TEXT NOT NULL DEFAULT '',
                PRIMARY KEY (tournament_id, chart_key)
            ) WITHOUT ROWID;

            CREATE TABLE tournament_rounds (
                round_id      TEXT PRIMARY KEY,
                tournament_id TEXT NOT NULL REFERENCES tournaments (tournament_id) ON DELETE CASCADE,
                round_index   INTEGER NOT NULL,
                name          TEXT NOT NULL,
                side          INTEGER NOT NULL DEFAULT 0,
                is_complete   INTEGER NOT NULL DEFAULT 0
            ) WITHOUT ROWID;

            CREATE INDEX ix_rounds_tournament ON tournament_rounds (tournament_id, round_index);

            CREATE TABLE tournament_matches (
                match_id        TEXT PRIMARY KEY,
                tournament_id   TEXT NOT NULL REFERENCES tournaments (tournament_id) ON DELETE CASCADE,
                round_id        TEXT NOT NULL REFERENCES tournament_rounds (round_id) ON DELETE CASCADE,
                slot            INTEGER NOT NULL,
                player1_id      TEXT,
                player2_id      TEXT,
                status          INTEGER NOT NULL DEFAULT 0,
                winner_player_id TEXT,
                chart_keys      TEXT NOT NULL DEFAULT '',
                best_of         INTEGER NOT NULL DEFAULT 1,
                started_utc     TEXT,
                completed_utc   TEXT
            ) WITHOUT ROWID;

            CREATE INDEX ix_matches_tournament ON tournament_matches (tournament_id, round_id, slot);

            -- Everything needed to check a result later travels with it: which chart, which
            -- build, which rules, and the replay. A number in a table proves nothing alone.
            CREATE TABLE tournament_match_results (
                result_id     TEXT PRIMARY KEY,
                match_id      TEXT NOT NULL REFERENCES tournament_matches (match_id) ON DELETE CASCADE,
                tournament_id TEXT NOT NULL REFERENCES tournaments (tournament_id) ON DELETE CASCADE,
                player_id     TEXT NOT NULL,
                chart_key     TEXT NOT NULL,
                game_index    INTEGER NOT NULL DEFAULT 0,
                score         INTEGER NOT NULL DEFAULT 0,
                accuracy      REAL NOT NULL DEFAULT 0,
                max_combo     INTEGER NOT NULL DEFAULT 0,
                perfect       INTEGER NOT NULL DEFAULT 0,
                great         INTEGER NOT NULL DEFAULT 0,
                good          INTEGER NOT NULL DEFAULT 0,
                bad           INTEGER NOT NULL DEFAULT 0,
                miss          INTEGER NOT NULL DEFAULT 0,
                pp            REAL NOT NULL DEFAULT 0,
                full_combo    INTEGER NOT NULL DEFAULT 0,
                all_perfect   INTEGER NOT NULL DEFAULT 0,
                replay_id     TEXT,
                chart_hash    TEXT NOT NULL DEFAULT '',
                game_version  TEXT NOT NULL DEFAULT '',
                rule_hash     TEXT NOT NULL DEFAULT '',
                submitted_utc TEXT NOT NULL,
                confirmed_utc TEXT
            ) WITHOUT ROWID;

            CREATE INDEX ix_results_match ON tournament_match_results (match_id, game_index);
            CREATE INDEX ix_results_tournament ON tournament_match_results (tournament_id, player_id);

            -- Append-only. The difference between a result and a result somebody can check.
            CREATE TABLE tournament_events (
                event_id      TEXT PRIMARY KEY,
                tournament_id TEXT NOT NULL REFERENCES tournaments (tournament_id) ON DELETE CASCADE,
                type          TEXT NOT NULL,
                actor_player_id TEXT,
                payload       TEXT NOT NULL DEFAULT '{}',
                timestamp_utc TEXT NOT NULL
            ) WITHOUT ROWID;

            CREATE INDEX ix_events_tournament ON tournament_events (tournament_id, timestamp_utc);
            """),
        // v7 first shipped in development with the chart columns keyed by a Guid that no
        // other table in the game uses. Charts are identified everywhere else by their
        // chart key — a hash of the chart's own contents — which is both the identifier
        // that exists and, because it changes when a chart is edited, exactly the "was this
        // the chart we agreed on" check a tournament needs.
        //
        // v7 above now creates the corrected tables, so a fresh database is right in one
        // step. This repairs the ones that already took the earlier shape. Dropping rather
        // than migrating the rows is safe and deliberate: tournaments have never been in a
        // release, so no database anywhere holds a tournament worth keeping, and inventing
        // a chart key for rows that never had one would be fabricating data.
        Migration.Sql(8, "tournaments_chart_key",
            """
            DROP TABLE IF EXISTS tournament_match_results;
            DROP TABLE IF EXISTS tournament_matches;
            DROP TABLE IF EXISTS tournament_songs;

            CREATE TABLE tournament_songs (
                tournament_id   TEXT NOT NULL REFERENCES tournaments (tournament_id) ON DELETE CASCADE,
                chart_key       TEXT NOT NULL,
                title           TEXT NOT NULL DEFAULT '',
                difficulty_name TEXT NOT NULL DEFAULT '',
                level           REAL NOT NULL DEFAULT 0,
                chart_hash      TEXT NOT NULL DEFAULT '',
                audio_hash      TEXT NOT NULL DEFAULT '',
                category        TEXT NOT NULL DEFAULT '',
                PRIMARY KEY (tournament_id, chart_key)
            ) WITHOUT ROWID;

            CREATE TABLE tournament_matches (
                match_id        TEXT PRIMARY KEY,
                tournament_id   TEXT NOT NULL REFERENCES tournaments (tournament_id) ON DELETE CASCADE,
                round_id        TEXT NOT NULL REFERENCES tournament_rounds (round_id) ON DELETE CASCADE,
                slot            INTEGER NOT NULL,
                player1_id      TEXT,
                player2_id      TEXT,
                status          INTEGER NOT NULL DEFAULT 0,
                winner_player_id TEXT,
                chart_keys      TEXT NOT NULL DEFAULT '',
                best_of         INTEGER NOT NULL DEFAULT 1,
                started_utc     TEXT,
                completed_utc   TEXT
            ) WITHOUT ROWID;

            CREATE INDEX ix_matches_tournament ON tournament_matches (tournament_id, round_id, slot);

            CREATE TABLE tournament_match_results (
                result_id     TEXT PRIMARY KEY,
                match_id      TEXT NOT NULL REFERENCES tournament_matches (match_id) ON DELETE CASCADE,
                tournament_id TEXT NOT NULL REFERENCES tournaments (tournament_id) ON DELETE CASCADE,
                player_id     TEXT NOT NULL,
                chart_key     TEXT NOT NULL,
                game_index    INTEGER NOT NULL DEFAULT 0,
                score         INTEGER NOT NULL DEFAULT 0,
                accuracy      REAL NOT NULL DEFAULT 0,
                max_combo     INTEGER NOT NULL DEFAULT 0,
                perfect       INTEGER NOT NULL DEFAULT 0,
                great         INTEGER NOT NULL DEFAULT 0,
                good          INTEGER NOT NULL DEFAULT 0,
                bad           INTEGER NOT NULL DEFAULT 0,
                miss          INTEGER NOT NULL DEFAULT 0,
                pp            REAL NOT NULL DEFAULT 0,
                full_combo    INTEGER NOT NULL DEFAULT 0,
                all_perfect   INTEGER NOT NULL DEFAULT 0,
                replay_id     TEXT,
                chart_hash    TEXT NOT NULL DEFAULT '',
                game_version  TEXT NOT NULL DEFAULT '',
                rule_hash     TEXT NOT NULL DEFAULT '',
                submitted_utc TEXT NOT NULL,
                confirmed_utc TEXT
            ) WITHOUT ROWID;

            CREATE INDEX ix_results_match ON tournament_match_results (match_id, game_index);
            CREATE INDEX ix_results_tournament ON tournament_match_results (tournament_id, player_id);
            """),
    };
}
