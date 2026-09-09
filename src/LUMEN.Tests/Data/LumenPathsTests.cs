using System;
using System.IO;
using FluentAssertions;
using Lumen.Core;
using Lumen.Data;
using Xunit;

namespace Lumen.Tests.Data;

public class LumenPathsTests : IDisposable
{
    private readonly string _tempDir;

    public LumenPathsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "lumen-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
            // best effort
        }
    }

    [Fact]
    public void Installed_mode_roots_under_local_app_data()
    {
        LumenPaths paths = LumenPaths.Resolve(_tempDir); // no sentinel file present

        string expectedRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            GameIdentity.DataFolderName);

        paths.Root.Should().Be(expectedRoot);
    }

    [Fact]
    public void Portable_sentinel_switches_root_next_to_the_executable()
    {
        File.WriteAllText(Path.Combine(_tempDir, LumenPaths.PortableSentinelFileName), "");

        LumenPaths paths = LumenPaths.Resolve(_tempDir);

        paths.Root.Should().Be(Path.Combine(_tempDir, "data"));
    }

    [Fact]
    public void EnsureCreated_builds_the_full_tree()
    {
        File.WriteAllText(Path.Combine(_tempDir, LumenPaths.PortableSentinelFileName), "");
        LumenPaths paths = LumenPaths.Resolve(_tempDir);

        paths.EnsureCreated();

        Directory.Exists(paths.Database).Should().BeTrue();
        Directory.Exists(paths.ChartsLocal).Should().BeTrue();
        Directory.Exists(paths.ChartsImported).Should().BeTrue();
        Directory.Exists(paths.Songs).Should().BeTrue();
        Directory.Exists(paths.Replays).Should().BeTrue();
        Directory.Exists(paths.Backups).Should().BeTrue();
        Directory.Exists(paths.Cache).Should().BeTrue();
        Directory.Exists(paths.Exports).Should().BeTrue();
        Directory.Exists(paths.Settings).Should().BeTrue();
        Directory.Exists(paths.Logs).Should().BeTrue();
    }

    [Fact]
    public void DatabaseFile_sits_inside_the_database_folder()
    {
        File.WriteAllText(Path.Combine(_tempDir, LumenPaths.PortableSentinelFileName), "");
        LumenPaths paths = LumenPaths.Resolve(_tempDir);

        paths.DatabaseFile.Should().Be(Path.Combine(paths.Database, "lumen.db"));
    }
}
