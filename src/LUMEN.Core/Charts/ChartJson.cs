using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lumen.Core.Charts;

/// <summary>
/// Reads and writes the <c>.lumenchart</c> JSON described in docs/CHART_FORMAT.md. The
/// full package format and the migration layer arrive in Phase 7; this is the v1 core.
/// </summary>
public static class ChartJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static string Serialize(Chart chart) => JsonSerializer.Serialize(ToDto(chart), Options);

    public static Chart Deserialize(string json)
    {
        ChartDto? dto = JsonSerializer.Deserialize<ChartDto>(json, Options)
                        ?? throw new FormatException("Empty chart JSON.");
        return FromDto(dto);
    }

    private static ChartDto ToDto(Chart c) => new()
    {
        FormatVersion = c.FormatVersion,
        LaneCount = c.LaneCount,
        Meta = new MetaDto
        {
            Title = c.Meta.Title,
            Artist = c.Meta.Artist,
            Creator = c.Meta.Creator,
            DifficultyName = c.Meta.DifficultyName,
            DifficultyLevel = c.Meta.DifficultyLevel,
            AudioFile = c.Meta.AudioFile,
            PreviewMs = c.Meta.PreviewMs,
        },
        Timing = new TimingDto
        {
            ChartOffsetMs = c.ChartOffsetMs,
            Bpm = c.BpmPoints.Select(b => new BpmDto { AtMs = b.AtMs, Bpm = b.Bpm }).ToList(),
        },
        Notes = c.Notes.Select(n => new NoteDto
        {
            Type = n.Kind.ToString().ToLowerInvariant(),
            Ms = n.TimeMs,
            Lane = n.Lane,
            EndMs = n.IsHold ? n.EndTimeMs : null,
        }).ToList(),
    };

    private static Chart FromDto(ChartDto d)
    {
        var notes = (d.Notes ?? new()).Select(n => new Note
        {
            Kind = Enum.TryParse<NoteKind>(n.Type, ignoreCase: true, out NoteKind k) ? k : NoteKind.Tap,
            TimeMs = n.Ms,
            Lane = n.Lane,
            EndTimeMs = n.EndMs ?? 0,
        }).ToArray();

        var bpm = (d.Timing?.Bpm ?? new()).Select(b => new BpmPoint(b.AtMs, b.Bpm)).ToArray();

        return new Chart
        {
            FormatVersion = d.FormatVersion == 0 ? GameIdentity.ChartFormatVersion : d.FormatVersion,
            LaneCount = d.LaneCount <= 0 ? 4 : d.LaneCount,
            ChartOffsetMs = d.Timing?.ChartOffsetMs ?? 0,
            BpmPoints = bpm.Length > 0 ? bpm : new[] { new BpmPoint(0, 120) },
            Notes = notes,
            Meta = new ChartMeta
            {
                Title = d.Meta?.Title ?? "Untitled",
                Artist = d.Meta?.Artist ?? "Unknown",
                Creator = d.Meta?.Creator ?? "",
                DifficultyName = d.Meta?.DifficultyName ?? "NORMAL",
                DifficultyLevel = d.Meta?.DifficultyLevel ?? 1.0,
                AudioFile = d.Meta?.AudioFile ?? "",
                PreviewMs = d.Meta?.PreviewMs ?? 0,
            },
        };
    }

    // --- wire DTOs ---

    private sealed class ChartDto
    {
        public int FormatVersion { get; set; }
        public int LaneCount { get; set; }
        public MetaDto? Meta { get; set; }
        public TimingDto? Timing { get; set; }
        public List<NoteDto>? Notes { get; set; }
    }

    private sealed class MetaDto
    {
        public string Title { get; set; } = "";
        public string Artist { get; set; } = "";
        public string Creator { get; set; } = "";
        public string DifficultyName { get; set; } = "";
        public double DifficultyLevel { get; set; }
        public string AudioFile { get; set; } = "";
        public double PreviewMs { get; set; }
    }

    private sealed class TimingDto
    {
        public double ChartOffsetMs { get; set; }
        public List<BpmDto> Bpm { get; set; } = new();
    }

    private sealed class BpmDto
    {
        public double AtMs { get; set; }
        public double Bpm { get; set; }
    }

    private sealed class NoteDto
    {
        public string Type { get; set; } = "tap";
        public double Ms { get; set; }
        public int Lane { get; set; }
        public double? EndMs { get; set; }
    }
}
