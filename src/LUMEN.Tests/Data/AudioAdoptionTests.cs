using System;
using System.IO;
using System.Linq;
using System.Text;
using FluentAssertions;
using Lumen.Data.Library;
using Xunit;

namespace Lumen.Tests.Data;

public class AudioAdoptionTests : IDisposable
{
    private readonly string _root;
    private readonly string _songs;
    private readonly string _elsewhere;

    public AudioAdoptionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "lumen-adopt-" + Guid.NewGuid().ToString("N"));
        _songs = Path.Combine(_root, "songs");
        _elsewhere = Path.Combine(_root, "desktop");
        Directory.CreateDirectory(_songs);
        Directory.CreateDirectory(_elsewhere);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private string Write(string directory, string name, string contents)
    {
        string path = Path.Combine(directory, name);
        File.WriteAllText(path, contents, Encoding.UTF8);
        return path;
    }

    /// <summary>
    /// The reason this exists: a chart stores the name of its audio and resolves it against
    /// the songs folder, so a file left on a desktop is a chart that plays today and never
    /// again.
    /// </summary>
    [Fact]
    public void A_file_from_outside_is_copied_in_and_the_chart_gets_a_bare_name()
    {
        string source = Write(_elsewhere, "track.wav", "audio");

        var result = AudioAdoption.Adopt(source, _songs);

        result.FileName.Should().Be("track.wav");
        result.FileName.Should().NotContain(Path.DirectorySeparatorChar.ToString(),
            "a chart records a name, never a path");
        result.Copied.Should().BeTrue();
        File.Exists(Path.Combine(_songs, "track.wav")).Should().BeTrue();
        File.ReadAllText(Path.Combine(_songs, "track.wav")).Should().Be("audio");
    }

    [Fact]
    public void The_original_is_left_where_it_was()
    {
        string source = Write(_elsewhere, "track.wav", "audio");

        AudioAdoption.Adopt(source, _songs);

        File.Exists(source).Should().BeTrue("adopting is a copy, not a move");
    }

    [Fact]
    public void A_file_already_in_the_songs_folder_is_not_copied_again()
    {
        string source = Write(_songs, "already.wav", "audio");

        var result = AudioAdoption.Adopt(source, _songs);

        result.Copied.Should().BeFalse();
        result.FileName.Should().Be("already.wav");
        Directory.GetFiles(_songs).Should().HaveCount(1);
    }

    /// <summary>Dropping the same song twice should not leave two copies of it.</summary>
    [Fact]
    public void Dropping_the_same_file_twice_reuses_the_one_already_there()
    {
        string source = Write(_elsewhere, "track.wav", "identical bytes");

        AudioAdoption.Adopt(source, _songs);
        var second = AudioAdoption.Adopt(source, _songs);

        second.ReusedExisting.Should().BeTrue();
        second.FileName.Should().Be("track.wav");
        Directory.GetFiles(_songs).Should().HaveCount(1);
    }

    /// <summary>
    /// And the other half of that: two different songs that happen to share a name must not
    /// overwrite one another, or a chart quietly starts playing the wrong music.
    /// </summary>
    [Fact]
    public void A_different_file_with_the_same_name_gets_its_own_copy()
    {
        Write(_songs, "track.wav", "the first song");
        string source = Write(_elsewhere, "track.wav", "a completely different song");

        var result = AudioAdoption.Adopt(source, _songs);

        result.FileName.Should().Be("track (2).wav");
        result.ReusedExisting.Should().BeFalse();
        File.ReadAllText(Path.Combine(_songs, "track.wav")).Should().Be("the first song");
        File.ReadAllText(Path.Combine(_songs, "track (2).wav")).Should().Be("a completely different song");
    }

    [Fact]
    public void A_third_clashing_file_keeps_counting()
    {
        Write(_songs, "track.wav", "one");
        Write(_songs, "track (2).wav", "two");
        string source = Write(_elsewhere, "track.wav", "three");

        AudioAdoption.Adopt(source, _songs).FileName.Should().Be("track (3).wav");
    }

    [Fact]
    public void The_songs_folder_is_created_when_it_is_not_there_yet()
    {
        string fresh = Path.Combine(_root, "brand-new-songs");
        string source = Write(_elsewhere, "track.wav", "audio");

        AudioAdoption.Adopt(source, fresh);

        File.Exists(Path.Combine(fresh, "track.wav")).Should().BeTrue();
    }

    [Fact]
    public void A_file_that_is_not_there_says_so_rather_than_half_working()
    {
        Action adopt = () => AudioAdoption.Adopt(Path.Combine(_elsewhere, "gone.wav"), _songs);

        adopt.Should().Throw<FileNotFoundException>();
        Directory.GetFiles(_songs).Should().BeEmpty();
    }

    [Fact]
    public void No_path_at_all_is_refused()
    {
        ((Action)(() => AudioAdoption.Adopt("", _songs))).Should().Throw<ArgumentException>();
        ((Action)(() => AudioAdoption.Adopt("   ", _songs))).Should().Throw<ArgumentException>();
    }

    /// <summary>
    /// A copy interrupted by a crash must leave debris the startup sweep clears, never a
    /// half-written song the library would treat as real (§74).
    /// </summary>
    [Fact]
    public void A_finished_copy_leaves_no_temporary_behind()
    {
        string source = Write(_elsewhere, "track.wav", "audio");

        AudioAdoption.Adopt(source, _songs);

        Directory.GetFiles(_songs, "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public void Identical_content_hashes_the_same_and_different_content_does_not()
    {
        string a = Write(_elsewhere, "a.wav", "same");
        string b = Write(_elsewhere, "b.wav", "same");
        string c = Write(_elsewhere, "c.wav", "different");

        AudioAdoption.HashOf(a).Should().Be(AudioAdoption.HashOf(b));
        AudioAdoption.HashOf(a).Should().NotBe(AudioAdoption.HashOf(c));
    }

    [Fact]
    public void A_large_file_is_copied_whole()
    {
        // Streamed rather than read into memory, so this must survive being bigger than a
        // buffer.
        string source = Path.Combine(_elsewhere, "big.wav");
        var bytes = new byte[5 * 1024 * 1024];
        new Random(7).NextBytes(bytes);
        File.WriteAllBytes(source, bytes);

        var result = AudioAdoption.Adopt(source, _songs);

        File.ReadAllBytes(result.Path).Should().Equal(bytes);
    }
}
