using System.Linq;
using FluentAssertions;
using Lumen.Core.Tournaments;
using Xunit;

namespace Lumen.Tests.Core.Tournaments;

public class TournamentRulesTests
{
    /// <summary>
    /// The default that matters most. A tournament is a separate competition, and a player
    /// who enters one should not find their everyday rating moved by a chart an organiser
    /// chose under rules they did not pick.
    /// </summary>
    [Fact]
    public void A_tournament_does_not_touch_the_players_normal_rating_by_default()
    {
        TournamentRules.Official.AffectsNormalRating.Should().BeFalse();
        new TournamentRules().AffectsNormalRating.Should().BeFalse();
    }

    [Fact]
    public void An_official_rule_set_allows_nothing_to_lean_on()
    {
        TournamentRules rules = TournamentRules.Official;

        rules.MaxAttempts.Should().Be(1);
        rules.RetryAllowed.Should().BeFalse();
        rules.PauseAllowed.Should().BeFalse();
        rules.ModifiersAllowed.Should().BeFalse();
        rules.MirrorAllowed.Should().BeFalse();
        rules.RandomAllowed.Should().BeFalse();
        rules.AutoplayAllowed.Should().BeFalse();
        rules.IsUsable.Should().BeTrue();
    }

    [Fact]
    public void A_casual_rule_set_is_forgiving_and_still_usable()
    {
        TournamentRules casual = TournamentRules.Casual;

        casual.RetryAllowed.Should().BeTrue();
        casual.MaxAttempts.Should().BeGreaterThan(1);
        casual.IsUsable.Should().BeTrue();
        casual.AffectsNormalRating.Should().BeFalse("even a casual event stays separate");
    }

    /// <summary>
    /// A player's input offset corrects for their hardware, not their skill. Forcing it to
    /// zero does not level the field, it tilts it towards whoever owns the fastest monitor.
    /// </summary>
    [Fact]
    public void Calibration_is_left_to_the_player_unless_an_organiser_says_otherwise()
    {
        TournamentRules.Official.FixedInputOffsetMs.Should().BeNull();
        TournamentRules.Official.FixedAudioOffsetMs.Should().BeNull();
        TournamentRules.Official.FixedNoteSpeed.Should().BeNull();
    }

    // --- validation ---

    [Fact]
    public void A_usable_rule_set_has_nothing_to_report()
    {
        TournamentRules.Official.Problems().Should().BeEmpty();
    }

    [Fact]
    public void A_match_that_could_end_level_is_refused()
    {
        TournamentRules even = TournamentRules.Official with { BestOf = 2 };

        even.IsUsable.Should().BeFalse();
        even.Problems().Should().ContainSingle(p => p.Contains("odd"));
    }

    [Fact]
    public void A_player_has_to_get_at_least_one_attempt()
    {
        (TournamentRules.Official with { MaxAttempts = 0 }).Problems()
            .Should().Contain(p => p.Contains("at least one attempt"));
    }

    [Fact]
    public void A_tie_break_that_cannot_resolve_anything_is_refused()
    {
        TournamentRules none = TournamentRules.Official with
        {
            TieBreak = System.Array.Empty<TieBreakCriterion>(),
        };

        none.IsUsable.Should().BeFalse();
        none.Problems().Should().Contain(p => p.Contains("at least one tie-break"));
    }

    [Fact]
    public void The_same_tie_break_listed_twice_is_refused()
    {
        TournamentRules duplicated = TournamentRules.Official with
        {
            TieBreak = new[] { TieBreakCriterion.Score, TieBreakCriterion.Score },
        };

        duplicated.Problems().Should().Contain(p => p.Contains("twice"));
    }

    /// <summary>A contradiction worth catching before a tie happens rather than during one.</summary>
    [Fact]
    public void Score_cannot_break_ties_in_a_tournament_that_does_not_record_it()
    {
        TournamentRules contradiction = TournamentRules.Official with { ScoreEnabled = false };

        contradiction.IsUsable.Should().BeFalse();
        contradiction.Problems().Should().Contain(p => p.Contains("does not record"));
    }

    [Fact]
    public void A_nonsense_note_speed_is_refused()
    {
        (TournamentRules.Official with { FixedNoteSpeed = 0 }).IsUsable.Should().BeFalse();
        (TournamentRules.Official with { FixedNoteSpeed = 99 }).IsUsable.Should().BeFalse();
        (TournamentRules.Official with { FixedNoteSpeed = 6 }).IsUsable.Should().BeTrue();
    }

    [Fact]
    public void Several_problems_are_all_reported_rather_than_the_first_one()
    {
        TournamentRules broken = TournamentRules.Official with
        {
            BestOf = 2,
            MaxAttempts = 0,
        };

        broken.Problems().Should().HaveCountGreaterThan(1);
    }

    // --- the hash ---

    /// <summary>
    /// A result recorded against a rule set is only meaningful if you can still tell,
    /// afterwards, what those rules were.
    /// </summary>
    [Fact]
    public void The_same_rules_hash_the_same_way_every_time()
    {
        TournamentRules.Official.Hash().Should().Be(TournamentRules.Official.Hash());
        new TournamentRules().Hash().Should().Be(TournamentRules.Official.Hash());
    }

    [Fact]
    public void Different_rules_hash_differently()
    {
        string official = TournamentRules.Official.Hash();

        (TournamentRules.Official with { RetryAllowed = true }).Hash().Should().NotBe(official);
        (TournamentRules.Official with { MaxAttempts = 2 }).Hash().Should().NotBe(official);
        (TournamentRules.Official with { BestOf = 3 }).Hash().Should().NotBe(official);
        (TournamentRules.Official with { AffectsNormalRating = true }).Hash().Should().NotBe(official);
        (TournamentRules.Official with { FixedNoteSpeed = 6 }).Hash().Should().NotBe(official);
        (TournamentRules.Official with { PickMode = SongPickMode.Fixed }).Hash().Should().NotBe(official);
    }

    /// <summary>
    /// Reordering the tie-break changes who wins, so it has to change the fingerprint too.
    /// </summary>
    [Fact]
    public void Reordering_the_tie_break_changes_the_hash()
    {
        TournamentRules reordered = TournamentRules.Official with
        {
            TieBreak = new[] { TieBreakCriterion.Accuracy, TieBreakCriterion.Score },
        };

        reordered.Hash().Should().NotBe(TournamentRules.Official.Hash());
    }

    [Fact]
    public void The_hash_is_short_enough_to_read_and_long_enough_to_trust()
    {
        string hash = TournamentRules.Official.Hash();

        hash.Should().HaveLength(16);
        hash.Should().MatchRegex("^[0-9a-f]+$");
    }

    [Fact]
    public void Every_field_of_the_rule_set_is_covered_by_the_hash()
    {
        // If a field is added and left out of Hash(), two different rule sets would claim
        // to be the same. This walks the public surface to make that failure loud.
        var varied = new[]
        {
            TournamentRules.Official with { ScoreEnabled = false, TieBreak = new[] { TieBreakCriterion.Accuracy } },
            TournamentRules.Official with { PpEnabled = false },
            TournamentRules.Official with { AffectsNormalRating = true },
            TournamentRules.Official with { MaxAttempts = 5 },
            TournamentRules.Official with { RetryAllowed = true },
            TournamentRules.Official with { PauseAllowed = true },
            TournamentRules.Official with { ModifiersAllowed = true },
            TournamentRules.Official with { MirrorAllowed = true },
            TournamentRules.Official with { RandomAllowed = true },
            TournamentRules.Official with { AutoplayAllowed = true },
            TournamentRules.Official with { FixedNoteSpeed = 6 },
            TournamentRules.Official with { FixedInputOffsetMs = 12 },
            TournamentRules.Official with { FixedAudioOffsetMs = -8 },
            TournamentRules.Official with { BestOf = 3 },
            TournamentRules.Official with { PickMode = SongPickMode.PlayerPick },
        };

        varied.Select(r => r.Hash()).Should().OnlyHaveUniqueItems();
    }
}
