using Lumen.Core.Achievements;
using Microsoft.Data.Sqlite;

namespace Lumen.Data.Repositories;

/// <summary>
/// Which achievements a profile has unlocked, and when (spec §64).
///
/// Only unlocks are stored. Progress towards a locked achievement is recomputed from the
/// player's statistics whenever the profile screen asks, so there is no second copy of a
/// number that could drift from the scores it came from.
/// </summary>
public sealed class AchievementRepository
{
    private readonly Database _db;

    public AchievementRepository(Database db) => _db = db;

    public IReadOnlyCollection<string> UnlockedIds(Guid playerId)
    {
        using SqliteCommand cmd = _db.CreateCommand();
        cmd.CommandText = "SELECT achievement_id FROM achievements WHERE player_id = $p;";
        cmd.Parameters.AddWithValue("$p", playerId.ToString("D"));

        var set = new HashSet<string>(StringComparer.Ordinal);
        using SqliteDataReader r = cmd.ExecuteReader();
        while (r.Read())
        {
            set.Add(r.GetString(0));
        }

        return set;
    }

    public int UnlockedCount(Guid playerId)
    {
        using SqliteCommand cmd = _db.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM achievements WHERE player_id = $p;";
        cmd.Parameters.AddWithValue("$p", playerId.ToString("D"));
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
    }

    /// <summary>
    /// Evaluates every achievement against the statistics and records anything newly
    /// earned. Returns what unlocked, so the result screen can announce it.
    /// </summary>
    public IReadOnlyList<AchievementDefinition> Evaluate(Guid playerId, AchievementStats stats)
    {
        IReadOnlyCollection<string> unlocked = UnlockedIds(playerId);
        IReadOnlyList<AchievementDefinition> earned =
            AchievementEngine.NewlyEarned(stats, unlocked);

        if (earned.Count == 0)
        {
            return earned;
        }

        string nowUtc = DateTime.UtcNow.ToString("O");
        foreach (AchievementDefinition definition in earned)
        {
            using SqliteCommand cmd = _db.CreateCommand();
            // "Do nothing" on conflict: an achievement keeps the date it was first earned,
            // even if the same evaluation runs twice.
            cmd.CommandText =
                """
                INSERT INTO achievements (player_id, achievement_id, unlocked_utc)
                VALUES ($p, $a, $utc)
                ON CONFLICT (player_id, achievement_id) DO NOTHING;
                """;
            cmd.Parameters.AddWithValue("$p", playerId.ToString("D"));
            cmd.Parameters.AddWithValue("$a", definition.Id);
            cmd.Parameters.AddWithValue("$utc", nowUtc);
            cmd.ExecuteNonQuery();
        }

        return earned;
    }

    /// <summary>Every achievement with its unlock date and current progress, for the profile.</summary>
    public IReadOnlyList<AchievementState> All(Guid playerId, AchievementStats stats)
    {
        var dates = new Dictionary<string, DateTime>(StringComparer.Ordinal);

        using (SqliteCommand cmd = _db.CreateCommand())
        {
            cmd.CommandText =
                "SELECT achievement_id, unlocked_utc FROM achievements WHERE player_id = $p;";
            cmd.Parameters.AddWithValue("$p", playerId.ToString("D"));

            using SqliteDataReader r = cmd.ExecuteReader();
            while (r.Read())
            {
                dates[r.GetString(0)] = DateTime.Parse(r.GetString(1), null,
                    System.Globalization.DateTimeStyles.RoundtripKind).ToUniversalTime();
            }
        }

        return AchievementEngine.All
            .Select(definition => new AchievementState(
                definition,
                dates.TryGetValue(definition.Id, out DateTime when) ? when : null,
                definition.Progress(stats)))
            // Unlocked first, then whatever the player is closest to earning: the list is
            // there to suggest what to do next, not to be an alphabet.
            .OrderByDescending(a => a.Unlocked)
            .ThenByDescending(a => a.Unlocked ? 0 : a.Definition.Fraction(stats))
            .ToArray();
    }
}
