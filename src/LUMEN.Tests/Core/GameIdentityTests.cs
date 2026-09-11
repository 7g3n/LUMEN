using System.Reflection;
using FluentAssertions;
using Lumen.Core;
using Xunit;

namespace Lumen.Tests.Core;

public class GameIdentityTests
{
    [Fact]
    public void DatabaseFileName_uses_the_slug()
    {
        GameIdentity.DatabaseFileName.Should().Be("lumen.db");
    }

    [Fact]
    public void WindowTitle_combines_name_and_version()
    {
        GameIdentity.WindowTitle.Should().Be($"{GameIdentity.Name} {GameIdentity.Version}");
    }

    [Fact]
    public void Identity_strings_are_non_empty()
    {
        GameIdentity.Name.Should().NotBeNullOrWhiteSpace();
        GameIdentity.Tagline.Should().NotBeNullOrWhiteSpace();
        GameIdentity.DataFolderName.Should().NotBeNullOrWhiteSpace();
        GameIdentity.Slug.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Data_folder_name_is_filesystem_safe()
    {
        GameIdentity.DataFolderName.IndexOfAny(System.IO.Path.GetInvalidFileNameChars())
            .Should().Be(-1);
        GameIdentity.DataFolderName.Should().NotContain(" ");
    }

    [Fact]
    public void Format_versions_are_positive()
    {
        GameIdentity.ChartFormatVersion.Should().BePositive();
        GameIdentity.BackupFormatVersion.Should().BePositive();
    }

    /// <summary>
    /// The version the game reports has to be the version the build was stamped with.
    /// It used to be a constant "kept in sync" with the build file by hand, which is the
    /// kind of promise that holds until the first release and then quietly stops.
    /// </summary>
    [Fact]
    public void The_version_comes_from_the_build_rather_than_from_a_second_copy()
    {
        var informational = typeof(GameIdentity).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>();

        informational.Should().NotBeNull("the build must stamp a version into the assembly");
        informational!.InformationalVersion.Should().StartWith(GameIdentity.Version);
    }

    [Fact]
    public void The_version_looks_like_a_version()
    {
        GameIdentity.Version.Should().MatchRegex(@"^\d+\.\d+\.\d+");
        GameIdentity.Version.Should().NotContain("+", "a build stamp is not part of the version");
    }

    /// <summary>
    /// A local build has no build stamp and must not invent one; a stamped build shows it
    /// after the version, which is what makes "which build was this?" answerable from a
    /// crash report.
    /// </summary>
    [Fact]
    public void The_full_version_carries_the_build_stamp_only_when_there_is_one()
    {
        if (GameIdentity.BuildId.Length == 0)
        {
            GameIdentity.FullVersion.Should().Be(GameIdentity.Version);
        }
        else
        {
            GameIdentity.FullVersion.Should().Be($"{GameIdentity.Version}+{GameIdentity.BuildId}");
        }
    }

    [Fact]
    public void The_version_is_read_once_rather_than_on_every_ask()
    {
        // Reflection on every call would put an attribute lookup in the window title and
        // the log header; reading it once is why these are properties with initialisers.
        ReferenceEquals(GameIdentity.Version, GameIdentity.Version).Should().BeTrue();
        ReferenceEquals(GameIdentity.BuildId, GameIdentity.BuildId).Should().BeTrue();
    }
}
