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
}
