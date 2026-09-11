using System.Text.Json;
using System.Text.Json.Serialization;
using Lumen.Core.Charts;
using Lumen.Core.Gameplay;

namespace Lumen.Core.Replays;

/// <summary>
/// Reads and writes a replay's event stream.
///
/// Events are stored as three parallel arrays rather than an array of objects: a long
/// play is tens of thousands of them, and `{"lane":0,"isDown":true,"timeMs":1234.5}`
/// repeated that many times is mostly punctuation. The columnar form is roughly a third
/// of the size and still plain JSON anyone can read.
/// </summary>
public static class ReplayJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Serialize(Replay replay)
    {
        var lanes = new int[replay.Events.Count];
        var downs = new bool[replay.Events.Count];
        var times = new double[replay.Events.Count];

        for (int i = 0; i < replay.Events.Count; i++)
        {
            LaneEvent e = replay.Events[i];
            lanes[i] = e.Lane;
            downs[i] = e.IsDown;
            times[i] = Math.Round(e.TimeMs, 3);
        }

        var dto = new ReplayDto
        {
            FormatVersion = Replay.FormatVersion,
            ReplayId = replay.ReplayId.ToString("D"),
            PlayerId = replay.PlayerId.ToString("D"),
            PlayerName = replay.PlayerName,
            ChartKey = replay.ChartKey,
            ChartId = replay.ChartId?.ToString("D"),
            RecordedUtc = replay.RecordedUtc.ToString("O"),
            InputOffsetMs = replay.InputOffsetMs,
            AudioOffsetMs = replay.AudioOffsetMs,
            Chart = new ChartDto
            {
                Title = replay.Chart.Title,
                Artist = replay.Chart.Artist,
                Creator = replay.Chart.Creator,
                DifficultyName = replay.Chart.DifficultyName,
                DifficultyLevel = replay.Chart.DifficultyLevel,
                AudioFile = replay.Chart.AudioFile,
            },
            Result = new ResultDto
            {
                Score = replay.Result.Score,
                Accuracy = replay.Result.Accuracy,
                MaxCombo = replay.Result.MaxCombo,
                Perfect = replay.Result.Perfect,
                Great = replay.Result.Great,
                Good = replay.Result.Good,
                Bad = replay.Result.Bad,
                Miss = replay.Result.Miss,
                FullCombo = replay.Result.FullCombo,
                AllPerfect = replay.Result.AllPerfect,
                Grade = replay.Result.Grade,
            },
            Events = new EventsDto { Lane = lanes, Down = downs, Ms = times },
        };

        return JsonSerializer.Serialize(dto, Options);
    }

    public static Replay Deserialize(string json)
    {
        ReplayDto dto = JsonSerializer.Deserialize<ReplayDto>(json, Options)
                        ?? throw new FormatException("Empty replay.");

        if (dto.FormatVersion > Replay.FormatVersion)
        {
            throw new FormatException(
                $"This replay was recorded by a newer version of {GameIdentity.Name} " +
                $"(replay format v{dto.FormatVersion}).");
        }

        int[] lanes = dto.Events?.Lane ?? Array.Empty<int>();
        bool[] downs = dto.Events?.Down ?? Array.Empty<bool>();
        double[] times = dto.Events?.Ms ?? Array.Empty<double>();

        int count = Math.Min(lanes.Length, Math.Min(downs.Length, times.Length));
        if (count != lanes.Length || count != downs.Length || count != times.Length)
        {
            throw new FormatException("This replay is damaged — its event columns disagree.");
        }

        var events = new LaneEvent[count];
        for (int i = 0; i < count; i++)
        {
            events[i] = new LaneEvent(lanes[i], downs[i], times[i]);
        }

        return new Replay
        {
            ReplayId = Guid.TryParse(dto.ReplayId, out Guid id) ? id : Guid.NewGuid(),
            PlayerId = Guid.TryParse(dto.PlayerId, out Guid player) ? player : Guid.Empty,
            PlayerName = dto.PlayerName ?? "",
            ChartKey = dto.ChartKey ?? "",
            ChartId = Guid.TryParse(dto.ChartId, out Guid chartId) ? chartId : null,
            Chart = new ChartMeta
            {
                Title = dto.Chart?.Title ?? "Untitled",
                Artist = dto.Chart?.Artist ?? "",
                Creator = dto.Chart?.Creator ?? "",
                DifficultyName = dto.Chart?.DifficultyName ?? "NORMAL",
                DifficultyLevel = dto.Chart?.DifficultyLevel ?? 1,
                AudioFile = dto.Chart?.AudioFile ?? "",
            },
            RecordedUtc = DateTime.TryParse(dto.RecordedUtc, null,
                System.Globalization.DateTimeStyles.RoundtripKind, out DateTime when)
                ? when.ToUniversalTime()
                : DateTime.UtcNow,
            InputOffsetMs = dto.InputOffsetMs,
            AudioOffsetMs = dto.AudioOffsetMs,
            Events = events,
            Result = new ReplayResult(
                dto.Result?.Score ?? 0, dto.Result?.Accuracy ?? 0, dto.Result?.MaxCombo ?? 0,
                dto.Result?.Perfect ?? 0, dto.Result?.Great ?? 0, dto.Result?.Good ?? 0,
                dto.Result?.Bad ?? 0, dto.Result?.Miss ?? 0,
                dto.Result?.FullCombo ?? false, dto.Result?.AllPerfect ?? false,
                dto.Result?.Grade ?? ""),
        };
    }

    // --- wire DTOs ---

    private sealed class ReplayDto
    {
        public int FormatVersion { get; set; }
        public string? ReplayId { get; set; }
        public string? PlayerId { get; set; }
        public string? PlayerName { get; set; }
        public string? ChartKey { get; set; }
        public string? ChartId { get; set; }
        public string? RecordedUtc { get; set; }
        public double InputOffsetMs { get; set; }
        public double AudioOffsetMs { get; set; }
        public ChartDto? Chart { get; set; }
        public ResultDto? Result { get; set; }
        public EventsDto? Events { get; set; }
    }

    private sealed class ChartDto
    {
        public string Title { get; set; } = "";
        public string Artist { get; set; } = "";
        public string Creator { get; set; } = "";
        public string DifficultyName { get; set; } = "";
        public double DifficultyLevel { get; set; }
        public string AudioFile { get; set; } = "";
    }

    private sealed class ResultDto
    {
        public long Score { get; set; }
        public double Accuracy { get; set; }
        public int MaxCombo { get; set; }
        public int Perfect { get; set; }
        public int Great { get; set; }
        public int Good { get; set; }
        public int Bad { get; set; }
        public int Miss { get; set; }
        public bool FullCombo { get; set; }
        public bool AllPerfect { get; set; }
        public string Grade { get; set; } = "";
    }

    private sealed class EventsDto
    {
        public int[] Lane { get; set; } = Array.Empty<int>();
        public bool[] Down { get; set; } = Array.Empty<bool>();
        public double[] Ms { get; set; } = Array.Empty<double>();
    }
}
