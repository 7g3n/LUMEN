namespace Lumen.Core.Profiles;

/// <summary>
/// A local player identity. <see cref="PlayerId"/> is a generated UUID and the only
/// stable key; <see cref="DisplayName"/> can change at any time without touching
/// scores, replays or statistics (spec §6, §80).
/// </summary>
public sealed record Profile
{
    public required Guid PlayerId { get; init; }

    public required string DisplayName { get; init; }

    public required DateTime CreatedUtc { get; init; }

    public required DateTime LastPlayedUtc { get; init; }

    public long TotalPlayTimeMs { get; init; }

    public string? AvatarPath { get; init; }
}

/// <summary>
/// Headline numbers for the profile screen (spec §7). Everything is zero until scores
/// exist; the real aggregation lands in Phase 4.
/// </summary>
public sealed record ProfileSummary
{
    public required Profile Profile { get; init; }

    public double Rating { get; init; }

    public long TotalPp { get; init; }

    public double BestPp { get; init; }

    public double Accuracy { get; init; }

    public int PlayCount { get; init; }

    public int FullCombos { get; init; }

    public static ProfileSummary Empty(Profile profile) => new() { Profile = profile };
}
