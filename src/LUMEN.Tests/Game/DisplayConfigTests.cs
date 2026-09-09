using System;
using System.IO;
using FluentAssertions;
using Lumen.Game.Config;
using Xunit;

namespace Lumen.Tests.Game;

public class DisplayConfigTests : IDisposable
{
    private readonly string _dir;

    public DisplayConfigTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "lumen-cfg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public void Load_writes_defaults_when_the_file_is_absent()
    {
        DisplayConfig config = DisplayConfig.Load(_dir);

        config.Width.Should().Be(1280);
        config.FollowRefreshRate.Should().BeTrue();
        File.Exists(Path.Combine(_dir, "display.json")).Should().BeTrue();
    }

    [Fact]
    public void Save_then_Load_round_trips()
    {
        var original = new DisplayConfig { Width = 2560, Height = 1440, Fullscreen = true, Vsync = true, FpsCap = 240 };
        original.Save(_dir);

        DisplayConfig loaded = DisplayConfig.Load(_dir);

        loaded.Should().BeEquivalentTo(original);
    }

    [Fact]
    public void Load_clamps_absurd_values()
    {
        File.WriteAllText(Path.Combine(_dir, "display.json"),
            "{\"width\":10,\"height\":10,\"fpsCap\":99999}");

        DisplayConfig loaded = DisplayConfig.Load(_dir);

        loaded.Width.Should().BeGreaterThanOrEqualTo(960);
        loaded.Height.Should().BeGreaterThanOrEqualTo(540);
        loaded.FpsCap.Should().BeLessThanOrEqualTo(1000);
    }

    [Fact]
    public void Load_falls_back_to_defaults_on_corrupt_json()
    {
        File.WriteAllText(Path.Combine(_dir, "display.json"), "{ not json");

        DisplayConfig loaded = DisplayConfig.Load(_dir);

        loaded.Width.Should().Be(1280);
    }
}
