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
    };
}
