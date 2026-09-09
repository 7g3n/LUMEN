using FluentAssertions;
using Lumen.Core.Profiles;
using Xunit;

namespace Lumen.Tests.Core;

public class PlayerNameTests
{
    [Theory]
    [InlineData("7g3")]
    [InlineData("Nagisa")]
    [InlineData("\u306A\u304E\u3055")]   // nagisa (hiragana)
    [InlineData("LUMEN_Player")]
    [InlineData("a")]
    [InlineData("Mix \u7A7A 123")]
    [InlineData("Renee")]
    [InlineData("16 chars exactly")]
    public void Accepts_valid_names(string name)
    {
        PlayerName.Validate(name).IsValid.Should().BeTrue($"'{name}' should be valid");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void Rejects_empty_or_whitespace(string name)
    {
        PlayerNameResult result = PlayerName.Validate(name);
        result.IsValid.Should().BeFalse();
        result.Error.Should().Be(PlayerNameError.Empty);
    }

    [Theory]
    [InlineData("this name is way too long")]
    [InlineData("aaaaaaaaaaaaaaaaa")] // 17 chars
    public void Rejects_names_longer_than_sixteen(string name)
    {
        PlayerName.Validate(name).Error.Should().Be(PlayerNameError.TooLong);
    }

    [Theory]
    [InlineData('\u0000')] // NUL (Control)
    [InlineData('\u0007')] // BEL (Control)
    [InlineData('\u001B')] // ESC (Control)
    [InlineData('\u200B')] // zero-width space (Format)
    [InlineData('\u00AD')] // soft hyphen (Format)
    [InlineData('\uE000')] // private use
    [InlineData('\uD800')] // unpaired high surrogate
    public void Rejects_names_containing_a_disallowed_character(char bad)
    {
        string name = "ok" + bad + "name";

        PlayerName.Validate(name).Error.Should().Be(PlayerNameError.InvalidCharacters);
    }

    [Fact]
    public void Tab_is_whitespace_not_an_illegal_character()
    {
        PlayerNameResult result = PlayerName.Validate("a\tb");
        result.IsValid.Should().BeTrue();
        result.Normalized.Should().Be("a b");
    }

    [Fact]
    public void Line_and_paragraph_separators_normalize_away_like_other_whitespace()
    {
        PlayerName.Normalize("line\u2028sep").Should().Be("line sep");
        PlayerName.Validate("para\u2029graph").IsValid.Should().BeTrue();
    }

    [Fact]
    public void Trims_surrounding_whitespace()
    {
        PlayerName.Validate("  Nagisa  ").Normalized.Should().Be("Nagisa");
    }

    [Fact]
    public void Collapses_internal_whitespace_runs_to_one_space()
    {
        PlayerName.Normalize("a\t \tb").Should().Be("a b");
    }

    [Fact]
    public void Length_is_counted_in_grapheme_clusters()
    {
        // Family emoji: one grapheme cluster, many runes.
        const string emoji = "\U0001F468\u200D\U0001F469\u200D\U0001F467";
        PlayerName.CountGraphemes(emoji).Should().Be(1);
        PlayerName.Validate(emoji).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Sixteen_graphemes_ok_seventeen_not()
    {
        PlayerName.Validate(new string('x', 16)).IsValid.Should().BeTrue();
        PlayerName.Validate(new string('x', 17)).Error.Should().Be(PlayerNameError.TooLong);
    }

    [Fact]
    public void IsValid_matches_Validate()
    {
        PlayerName.IsValid("\u306A\u304E\u3055").Should().BeTrue();
        PlayerName.IsValid("").Should().BeFalse();
    }
}
