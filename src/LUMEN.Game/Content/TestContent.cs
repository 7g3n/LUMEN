using Lumen.Core.Charts;
using Lumen.Core.Diagnostics;
using Lumen.Data;

namespace Lumen.Game.Content;

/// <summary>
/// LUMEN's own bundled practice track. The audio is synthesised (an original drum +
/// bass pattern), so nothing copyrighted ships with the game. Generated once into the
/// data folder on first run; the editor and Song Select pick it up like any other song.
/// </summary>
public static class TestContent
{
    public const double Bpm = 128.0;
    private const int Bars = 20;
    private const int SampleRate = 44100;

    public static string AudioFileName => "lumen-practice.wav";
    public static string ChartFileName => "lumen-practice.lumenchart";

    public sealed record Installed(string AudioPath, string ChartPath, Chart Chart);

    public static Installed EnsureInstalled(LumenPaths paths)
    {
        Directory.CreateDirectory(paths.Songs);
        Directory.CreateDirectory(paths.ChartsLocal);

        string audioPath = Path.Combine(paths.Songs, AudioFileName);
        string chartPath = Path.Combine(paths.ChartsLocal, ChartFileName);

        if (!File.Exists(audioPath))
        {
            Log.Info("synthesising practice track");
            AtomicFile.WriteAllBytes(audioPath, Synthesize());
        }

        Chart chart = BuildChart(audioPath);
        if (!File.Exists(chartPath))
        {
            AtomicFile.WriteAllText(chartPath, ChartJson.Serialize(chart));
        }

        return new Installed(audioPath, chartPath, chart);
    }

    // --- audio ---

    private static byte[] Synthesize()
    {
        double msPerBeat = 60_000.0 / Bpm;
        int totalBeats = Bars * 4;
        int totalSamples = (int)(totalBeats * msPerBeat / 1000.0 * SampleRate) + SampleRate; // + 1s tail
        var left = new float[totalSamples];

        var rng = new Random(20260909);

        // Bass note per beat (simple i-VI-VII-i-ish in A minor), sawtooth.
        double[] roots = { 55.00, 55.00, 82.41, 65.41 }; // A1 A1 E2 C2 per bar
        for (int beat = 0; beat < totalBeats; beat++)
        {
            double t0 = beat * msPerBeat / 1000.0;
            double freq = roots[(beat / 4) % roots.Length];
            AddTone(left, t0, 0.34, freq, 0.16, Wave.Saw, decay: 6);

            // Kick on every beat.
            AddKick(left, t0, 0.55);

            // Snare on beats 2 and 4.
            if (beat % 4 is 1 or 3)
            {
                AddNoise(left, t0, 0.14, 0.28, rng);
            }

            // Hats on the offbeats.
            AddNoise(left, t0 + msPerBeat / 2000.0, 0.05, 0.10, rng);
        }

        return EncodeWavStereo(left);
    }

    private enum Wave { Sine, Saw }

    private static void AddTone(float[] buf, double startSec, double durSec, double freq,
                                double amp, Wave shape, double decay)
    {
        int start = (int)(startSec * SampleRate);
        int len = (int)(durSec * SampleRate);
        for (int i = 0; i < len && start + i < buf.Length; i++)
        {
            double t = (double)i / SampleRate;
            double env = Math.Exp(-decay * t);
            double phase = freq * t;
            double s = shape == Wave.Sine
                ? Math.Sin(2 * Math.PI * phase)
                : 2.0 * (phase - Math.Floor(phase + 0.5));
            buf[start + i] += (float)(s * env * amp);
        }
    }

    private static void AddKick(float[] buf, double startSec, double amp)
    {
        int start = (int)(startSec * SampleRate);
        int len = (int)(0.16 * SampleRate);
        for (int i = 0; i < len && start + i < buf.Length; i++)
        {
            double t = (double)i / SampleRate;
            double env = Math.Exp(-24 * t);
            double freq = 120 * Math.Exp(-30 * t) + 45;
            buf[start + i] += (float)(Math.Sin(2 * Math.PI * freq * t) * env * amp);
        }
    }

    private static void AddNoise(float[] buf, double startSec, double durSec, double amp, Random rng)
    {
        int start = (int)(startSec * SampleRate);
        int len = (int)(durSec * SampleRate);
        for (int i = 0; i < len && start + i < buf.Length; i++)
        {
            double env = Math.Exp(-30 * (double)i / SampleRate);
            buf[start + i] += (float)((rng.NextDouble() * 2 - 1) * env * amp);
        }
    }

    private static byte[] EncodeWavStereo(float[] mono)
    {
        int frames = mono.Length;
        int dataBytes = frames * 2 * 2; // stereo, 16-bit
        using var ms = new MemoryStream(44 + dataBytes);
        using var w = new BinaryWriter(ms);

        void Str(string s) => w.Write(System.Text.Encoding.ASCII.GetBytes(s));

        Str("RIFF");
        w.Write(36 + dataBytes);
        Str("WAVE");
        Str("fmt ");
        w.Write(16);
        w.Write((short)1);            // PCM
        w.Write((short)2);            // channels
        w.Write(SampleRate);
        w.Write(SampleRate * 2 * 2);  // byte rate
        w.Write((short)(2 * 2));      // block align
        w.Write((short)16);           // bits
        Str("data");
        w.Write(dataBytes);

        foreach (float f in mono)
        {
            short v = (short)(Math.Clamp(f, -1f, 1f) * short.MaxValue);
            w.Write(v);
            w.Write(v);
        }

        w.Flush();
        return ms.ToArray();
    }

    // --- chart ---

    private static Chart BuildChart(string audioPath)
    {
        double msPerBeat = 60_000.0 / Bpm;
        int totalBeats = Bars * 4;
        var notes = new List<Note>();

        // Lane pattern: a walking figure with the occasional hold and offbeat.
        int[] walk = { 0, 1, 2, 3, 2, 1 };
        for (int beat = 4; beat < totalBeats - 2; beat++) // 1-bar lead-in
        {
            double t = beat * msPerBeat;
            int lane = walk[beat % walk.Length];

            bool holdBar = (beat / 4) % 4 == 3;
            if (holdBar && beat % 4 == 0)
            {
                notes.Add(Note.Hold(t, lane, t + msPerBeat * 3));
            }
            else
            {
                notes.Add(Note.Tap(t, lane));
                if (beat % 4 == 2 && beat > 8)
                {
                    notes.Add(Note.Tap(t + msPerBeat / 2, (lane + 2) % 4)); // offbeat
                }
            }
        }

        return new Chart
        {
            LaneCount = 4,
            BpmPoints = new[] { new BpmPoint(0, Bpm) },
            Notes = notes.OrderBy(n => n.TimeMs).ThenBy(n => n.Lane).ToArray(),
            Meta = new ChartMeta
            {
                Title = "First Light",
                Artist = "LUMEN",
                Creator = "LUMEN",
                DifficultyName = "NORMAL",
                DifficultyLevel = 3.5,
                AudioFile = Path.GetFileName(audioPath),
                PreviewMs = 8 * msPerBeat,
            },
        }.Normalized();
    }
}
