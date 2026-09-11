namespace Lumen.Audio;

/// <summary>
/// A min/max reduction of a decoded track, for drawing the editor's waveform (spec §48).
///
/// A four-minute song is ten million stereo frames; a waveform strip is a couple of
/// thousand pixels. Reducing once to a min/max pair per bucket and drawing from the
/// reduction is what keeps scrolling and zooming free, and min/max (rather than an
/// average) is what keeps a transient visible instead of averaging it away - the peaks
/// are the part an author is trying to line notes up with.
/// </summary>
public sealed class WaveformPeaks
{
    private readonly float[] _minMax;

    private WaveformPeaks(float[] minMax, int bucketCount, double durationMs)
    {
        _minMax = minMax;
        BucketCount = bucketCount;
        DurationMs = durationMs;
    }

    /// <summary>Buckets per second. Fine enough for a full-width strip at any zoom the editor allows.</summary>
    public const int BucketsPerSecond = 400;

    public int BucketCount { get; }

    public double DurationMs { get; }

    public static WaveformPeaks Empty { get; } = new(Array.Empty<float>(), 0, 0);

    public static WaveformPeaks From(AudioClip clip) =>
        From(clip.Samples, AudioClip.Channels, AudioClip.SampleRate);

    public static WaveformPeaks From(IReadOnlyList<float> interleaved, int channels, int sampleRate)
    {
        if (channels <= 0 || sampleRate <= 0 || interleaved.Count == 0)
        {
            return Empty;
        }

        int frames = interleaved.Count / channels;
        double durationMs = frames * 1000.0 / sampleRate;
        int buckets = Math.Max(1, (int)Math.Ceiling(frames / (double)sampleRate * BucketsPerSecond));
        int framesPerBucket = Math.Max(1, frames / buckets);

        var data = new float[buckets * 2];

        for (int b = 0; b < buckets; b++)
        {
            int start = b * framesPerBucket;
            int end = Math.Min(frames, start + framesPerBucket);

            float min = 0;
            float max = 0;
            bool any = false;

            for (int f = start; f < end; f++)
            {
                // Channels are mixed as they are read; a separate summed copy of a
                // ten-million-sample track would cost more than the reduction itself.
                float sum = 0;
                for (int c = 0; c < channels; c++)
                {
                    sum += interleaved[f * channels + c];
                }

                float value = sum / channels;
                if (!any)
                {
                    min = max = value;
                    any = true;
                    continue;
                }

                if (value < min) min = value;
                if (value > max) max = value;
            }

            data[b * 2] = min;
            data[b * 2 + 1] = max;
        }

        return new WaveformPeaks(data, buckets, durationMs);
    }

    /// <summary>
    /// The loudest excursion between two times, as (min, max) in -1..1. Used per drawn
    /// column, so a column always shows the peak inside it rather than one sample of it.
    /// </summary>
    public (float Min, float Max) Range(double fromMs, double toMs)
    {
        if (BucketCount == 0 || DurationMs <= 0 || toMs <= fromMs)
        {
            return (0, 0);
        }

        int first = BucketAt(fromMs);
        int last = Math.Max(first + 1, BucketAt(toMs));

        float min = 0;
        float max = 0;
        for (int b = first; b < last && b < BucketCount; b++)
        {
            if (_minMax[b * 2] < min) min = _minMax[b * 2];
            if (_minMax[b * 2 + 1] > max) max = _minMax[b * 2 + 1];
        }

        return (min, max);
    }

    private int BucketAt(double timeMs)
    {
        int bucket = (int)(timeMs / DurationMs * BucketCount);
        return Math.Clamp(bucket, 0, Math.Max(0, BucketCount - 1));
    }
}
