using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using Lumen.Core.Editing;
using Xunit;

namespace Lumen.Tests.Core.Editing;

public class AudioDropTests
{
    /// <summary>A stand-in for the filesystem, so these stay about the rules.</summary>
    private static Func<string, long> Sizes(params (string Path, long Size)[] files) =>
        path => files.FirstOrDefault(f => f.Path == path).Path is null ? -1
            : files.First(f => f.Path == path).Size;

    private const long Megabyte = 1024 * 1024;

    [Fact]
    public void A_wav_is_accepted()
    {
        var result = AudioDrop.Classify(new[] { @"C:\songs\track.wav" },
            Sizes((@"C:\songs\track.wav", 4 * Megabyte)));

        result.IsAccepted.Should().BeTrue();
        result.Path.Should().Be(@"C:\songs\track.wav");
        result.Message.Should().Contain("track.wav");
    }

    [Theory]
    [InlineData(".wav")]
    [InlineData(".mp3")]
    [InlineData(".ogg")]
    [InlineData(".aiff")]
    [InlineData(".aif")]
    public void Every_advertised_format_is_actually_accepted(string extension)
    {
        string path = @"C:\drop\song" + extension;

        AudioDrop.Classify(new[] { path }, Sizes((path, Megabyte)))
            .IsAccepted.Should().BeTrue();
    }

    /// <summary>
    /// The list the error message offers has to be the list the code honours, or the
    /// message sends people to try something that will be refused again.
    /// </summary>
    [Fact]
    public void The_advertised_list_matches_what_is_accepted()
    {
        foreach (string extension in AudioDrop.SupportedExtensions)
        {
            AudioDrop.SupportedList.Should()
                .Contain(extension.TrimStart('.').ToUpperInvariant());
        }
    }

    [Fact]
    public void Extension_matching_ignores_case()
    {
        AudioDrop.IsSupported(@"C:\drop\SONG.WAV").Should().BeTrue();
        AudioDrop.IsSupported(@"C:\drop\Song.Mp3").Should().BeTrue();
    }

    [Fact]
    public void Something_that_is_not_audio_is_refused_with_the_formats_that_are()
    {
        var result = AudioDrop.Classify(new[] { @"C:\pictures\cover.png" });

        result.IsAccepted.Should().BeFalse();
        result.Outcome.Should().Be(AudioDropOutcome.UnsupportedFormat);
        result.Message.Should().Contain("WAV");
        result.Message.Should().Contain(".png");
    }

    [Fact]
    public void Dropping_nothing_is_not_an_error()
    {
        AudioDrop.Classify(Array.Empty<string>()).Outcome.Should().Be(AudioDropOutcome.Empty);
        AudioDrop.Classify(null!).Outcome.Should().Be(AudioDropOutcome.Empty);

        AudioDrop.Classify(Array.Empty<string>()).Message.Should().BeEmpty(
            "there is nothing to tell somebody who dropped nothing");
    }

    /// <summary>
    /// Dragging a folder's worth of files onto the editor is a reasonable thing to do, and
    /// "use this song" is not ambiguous just because a cover image came along.
    /// </summary>
    [Fact]
    public void A_mixed_drop_takes_the_audio_and_says_what_it_passed_over()
    {
        string[] paths =
        {
            @"C:\album\cover.png",
            @"C:\album\track.mp3",
            @"C:\album\notes.txt",
        };

        var result = AudioDrop.Classify(paths, Sizes((@"C:\album\track.mp3", 6 * Megabyte)));

        result.IsAccepted.Should().BeTrue();
        result.Path.Should().Be(@"C:\album\track.mp3");
        result.IgnoredCount.Should().Be(2);
        result.Message.Should().Contain("ignored 2");
    }

    [Fact]
    public void Two_audio_files_take_the_first_and_say_so()
    {
        string[] paths = { @"C:\a.wav", @"C:\b.wav" };

        var result = AudioDrop.Classify(paths, Sizes((@"C:\a.wav", Megabyte), (@"C:\b.wav", Megabyte)));

        result.Path.Should().Be(@"C:\a.wav");
        result.IgnoredCount.Should().Be(1);
        result.Message.Should().Contain("ignored 1 other file");
    }

    /// <summary>
    /// Decoding happens into memory as 32-bit floats, so an enormous file is a frozen
    /// editor. Refusing it with a sentence is kinder than appearing to hang.
    /// </summary>
    [Fact]
    public void A_file_too_large_to_be_a_song_is_refused_with_its_size()
    {
        string path = @"C:\huge.wav";

        var result = AudioDrop.Classify(new[] { path },
            Sizes((path, AudioDrop.MaxBytes + Megabyte)));

        result.IsAccepted.Should().BeFalse();
        result.Outcome.Should().Be(AudioDropOutcome.TooLarge);
        result.Message.Should().Contain("MB");
    }

    [Fact]
    public void A_file_at_exactly_the_limit_is_still_allowed()
    {
        string path = @"C:\borderline.wav";

        AudioDrop.Classify(new[] { path }, Sizes((path, AudioDrop.MaxBytes)))
            .IsAccepted.Should().BeTrue();
    }

    [Fact]
    public void A_file_that_is_not_there_is_reported_rather_than_loaded()
    {
        var result = AudioDrop.Classify(new[] { @"C:\gone.wav" }, Sizes());

        result.IsAccepted.Should().BeFalse();
        result.Outcome.Should().Be(AudioDropOutcome.Unreadable);
        result.Message.Should().Contain("gone.wav");
    }

    [Fact]
    public void An_empty_file_is_refused_before_the_decoder_sees_it()
    {
        string path = @"C:\empty.wav";

        var result = AudioDrop.Classify(new[] { path }, Sizes((path, 0)));

        result.Outcome.Should().Be(AudioDropOutcome.Unreadable);
        result.Message.Should().Contain("empty");
    }

    /// <summary>The real filesystem path, rather than the stand-in, for one case.</summary>
    [Fact]
    public void A_real_file_on_disk_is_measured_and_accepted()
    {
        string dir = Path.Combine(Path.GetTempPath(), "lumen-drop-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "real.wav");
            File.WriteAllBytes(path, new byte[1024]);

            AudioDrop.Classify(new[] { path }).IsAccepted.Should().BeTrue();
            AudioDrop.Classify(new[] { Path.Combine(dir, "missing.wav") })
                .Outcome.Should().Be(AudioDropOutcome.Unreadable);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }
}
