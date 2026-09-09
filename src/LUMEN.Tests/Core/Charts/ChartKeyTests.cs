using FluentAssertions;
using Lumen.Core.Charts;
using Xunit;

namespace Lumen.Tests.Core.Charts;

public class ChartKeyTests
{
    private static Chart Make(string title = "First Light", string diff = "NORMAL", int extraNote = 0)
    {
        var notes = new System.Collections.Generic.List<Note> { Note.Tap(0, 0), Note.Tap(500, 1) };
        for (int i = 0; i < extraNote; i++)
        {
            notes.Add(Note.Tap(1000 + i * 100, 2));
        }

        return new Chart
        {
            LaneCount = 4,
            Notes = notes.ToArray(),
            Meta = new ChartMeta { Title = title, Artist = "LUMEN", Creator = "7g3", DifficultyName = diff },
        };
    }

    [Fact]
    public void Same_identity_produces_the_same_key()
    {
        ChartKey.For(Make()).Should().Be(ChartKey.For(Make()));
    }

    [Fact]
    public void Key_is_case_and_whitespace_insensitive_on_identity_fields()
    {
        var a = Make(title: "First Light");
        var b = Make(title: "  first light ");
        ChartKey.For(a).Should().Be(ChartKey.For(b));
    }

    [Fact]
    public void Different_difficulty_gives_a_different_key()
    {
        ChartKey.For(Make(diff: "NORMAL")).Should().NotBe(ChartKey.For(Make(diff: "MASTER")));
    }

    [Fact]
    public void Different_note_count_gives_a_different_key()
    {
        ChartKey.For(Make()).Should().NotBe(ChartKey.For(Make(extraNote: 3)));
    }

    [Fact]
    public void Key_is_16_hex_chars()
    {
        ChartKey.For(Make()).Should().MatchRegex("^[0-9a-f]{16}$");
    }
}
