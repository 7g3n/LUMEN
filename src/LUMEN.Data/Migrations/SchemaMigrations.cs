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
    };
}
