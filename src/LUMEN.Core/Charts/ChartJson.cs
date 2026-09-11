using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Lumen.Core.Charts;

/// <summary>
/// Reads and writes the <c>.lumenchart</c> JSON described in docs/CHART_FORMAT.md.
///
/// Reading runs the document through <see cref="ChartMigrator"/> first, so a file written
/// by any past version opens without the rest of the game knowing that old versions exist.
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
        JsonNode? node = JsonNode.Parse(json)
                         ?? throw new FormatException("Empty chart JSON.");

        if (node is not JsonObject document)
        {
            throw new FormatException("A chart file must be a JSON object.");
        }

        ChartMigrator.Migrate(document);

        ChartDto? dto = document.Deserialize<ChartDto>(Options)
                        ?? throw new FormatException("Empty chart JSON.");
        return FromDto(dto);
    }

    private static ChartDto ToDto(Chart c) => new()
    {
        FormatVersion = c.FormatVersion,
        Id = c.Id?.ToString("D"),
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
            Description = c.Meta.Description,
            Tags = c.Meta.Tags.ToList(),
            CoverFile = c.Meta.CoverFile,
            DurationMs = c.Meta.DurationMs,
        },
        Analysis = AnalysisOf(c),
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
            Id = Guid.TryParse(d.Id, out Guid id) ? id : null,
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
                Description = d.Meta?.Description ?? "",
                Tags = ChartTags.Normalize(d.Meta?.Tags ?? new List<string>()),
                CoverFile = string.IsNullOrWhiteSpace(d.Meta?.CoverFile) ? null : d.Meta.CoverFile,
                DurationMs = d.Meta?.DurationMs ?? 0,
            },
        };
    }

    // --- wire DTOs ---

    /// <summary>
    /// The advisory analysis block (§53). It is written for anyone reading the file - the
    /// author, another tool - and deliberately ignored on read: a chart received from
    /// someone else carries *their* build's numbers, and the levels our pp is computed
    /// from have to be ours. Recomputing costs a pass over the notes and removes the
    /// question entirely.
    /// </summary>
    private static AnalysisDto AnalysisOf(Chart chart)
    {
        DifficultyAnalyzer.Result result = DifficultyAnalyzer.Analyze(chart);
        ChartAttributes a = result.Attributes;

        return new AnalysisDto
        {
            EstimatedLevel = Math.Round(result.EstimatedLevel, 2),
            NoteCount = chart.Notes.Count,
            Attributes = new Dictionary<string, double>
            {
                ["speed"] = Math.Round(a.Speed, 3),
                ["technical"] = Math.Round(a.Technical, 3),
                ["reading"] = Math.Round(a.Reading, 3),
                ["stamina"] = Math.Round(a.Stamina, 3),
                ["reaction"] = Math.Round(a.Reaction, 3),
                ["patternComplexity"] = Math.Round(a.PatternComplexity, 3),
            },
        };
    }

    private sealed class ChartDto
    {
        public int FormatVersion { get; set; }
        public string? Id { get; set; }
        public int LaneCount { get; set; }
        public MetaDto? Meta { get; set; }
        public TimingDto? Timing { get; set; }
        public List<NoteDto>? Notes { get; set; }
        public AnalysisDto? Analysis { get; set; }
    }

    private sealed class AnalysisDto
    {
        public double EstimatedLevel { get; set; }
        public int NoteCount { get; set; }
        public Dictionary<string, double> Attributes { get; set; } = new();
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
        public string Description { get; set; } = "";
        public List<string> Tags { get; set; } = new();
        public string? CoverFile { get; set; }
        public double DurationMs { get; set; }
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
