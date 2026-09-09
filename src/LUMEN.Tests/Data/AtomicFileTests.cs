using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using Lumen.Data;
using Xunit;

namespace Lumen.Tests.Data;

public class AtomicFileTests : IDisposable
{
    private readonly string _dir;

    public AtomicFileTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "lumen-atomic-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public void WriteAllText_creates_the_file_and_no_tmp_remains()
    {
        string path = Path.Combine(_dir, "sub", "a.json");

        AtomicFile.WriteAllText(path, "{\"x\":1}");

        File.ReadAllText(path).Should().Be("{\"x\":1}");
        File.Exists(path + ".tmp").Should().BeFalse();
    }

    [Fact]
    public void WriteAllText_overwrites_atomically()
    {
        string path = Path.Combine(_dir, "b.txt");
        AtomicFile.WriteAllText(path, "first");

        AtomicFile.WriteAllText(path, "second");

        File.ReadAllText(path).Should().Be("second");
        File.Exists(path + ".tmp").Should().BeFalse();
    }

    [Fact]
    public void WriteAllText_writes_utf8_without_a_bom()
    {
        string path = Path.Combine(_dir, "c.txt");
        AtomicFile.WriteAllText(path, "なぎさ");

        byte[] bytes = File.ReadAllBytes(path);
        bytes.Take(3).Should().NotEqual(new byte[] { 0xEF, 0xBB, 0xBF });
        File.ReadAllText(path).Should().Be("なぎさ");
    }

    [Fact]
    public void ReadAllTextOrNull_ignores_a_leftover_tmp()
    {
        string path = Path.Combine(_dir, "d.txt");
        File.WriteAllText(path + ".tmp", "half-written");

        AtomicFile.ReadAllTextOrNull(path).Should().BeNull();
    }
}
