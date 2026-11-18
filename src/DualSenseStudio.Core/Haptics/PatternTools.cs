namespace DualSenseStudio.Core.Haptics;

/// <summary>Non-destructive edits. Every method returns a new pattern.</summary>
public static class PatternTools
{
    private static readonly Channel[] Channels = [Channel.Left, Channel.Right, Channel.LeftTrigger, Channel.RightTrigger];

    private static Pattern Map(Pattern src, Func<byte[], byte[]> f)
    {
        var p = src.Clone();
        foreach (var c in Channels) p.SetChannel(c, f(src[c]));
        p.Modified = DateTime.Now;
        return p;
    }

    public static Pattern Trim(Pattern src, double startSec, double endSec)
    {
        int a = Math.Clamp((int)Math.Round(startSec * src.SampleRate), 0, src.Length);
        int b = Math.Clamp((int)Math.Round(endSec * src.SampleRate), a, src.Length);
        if (b - a < 2) return src.Clone();
        return Map(src, d => d[a..b]);
    }

    /// <summary>Time-stretch to a new loop length (seconds) by resampling.</summary>
    public static Pattern Stretch(Pattern src, double newSeconds)
    {
        int n = Math.Max(2, (int)Math.Round(newSeconds * src.SampleRate));
        if (src.Length < 2) return src.Clone();
        return Map(src, d =>
        {
            var r = new byte[n];
            for (int i = 0; i < n; i++)
            {
                double pos = (double)i * (d.Length - 1) / (n - 1);
                int i0 = (int)pos;
                int i1 = Math.Min(i0 + 1, d.Length - 1);
                double t = pos - i0;
                r[i] = (byte)Math.Round(d[i0] + (d[i1] - d[i0]) * t);
            }
            return r;
        });
    }

    /// <summary>Moving-average smoothing. Wraps around so the loop stays seamless.</summary>
    public static Pattern Smooth(Pattern src, double windowMs)
    {
        int w = Math.Max(1, (int)Math.Round(windowMs / 1000.0 * src.SampleRate / 2));
        return Map(src, d =>
        {
            int n = d.Length;
            var r = new byte[n];
            if (n == 0) return r;
            for (int i = 0; i < n; i++)
            {
                int sum = 0;
                for (int k = -w; k <= w; k++) sum += d[((i + k) % n + n) % n];
                r[i] = (byte)(sum / (2 * w + 1));
            }
            return r;
        });
    }

    public static Pattern Gain(Pattern src, double factor) =>
        Map(src, d => d.Select(v => (byte)Math.Clamp((int)Math.Round(v * factor), 0, 255)).ToArray());

    /// <summary>Scale so the loudest motor sample hits full strength.</summary>
    public static Pattern Normalize(Pattern src)
    {
        int max = Math.Max(src.Left.DefaultIfEmpty().Max(), src.Right.DefaultIfEmpty().Max());
        if (max == 0) return src.Clone();
        double f = 255.0 / max;
        var p = src.Clone();
        p.Left = src.Left.Select(v => (byte)Math.Clamp((int)Math.Round(v * f), 0, 255)).ToArray();
        p.Right = src.Right.Select(v => (byte)Math.Clamp((int)Math.Round(v * f), 0, 255)).ToArray();
        return p;
    }

    /// <summary>
    /// Snap to a rhythmic grid: each step holds the peak of what happened inside it,
    /// turning a loose performance into tight on-beat blocks.
    /// </summary>
    public static Pattern Quantize(Pattern src, double bpm, int stepsPerBeat)
    {
        double stepSec = 60.0 / Math.Max(1, bpm) / Math.Max(1, stepsPerBeat);
        int step = Math.Max(1, (int)Math.Round(stepSec * src.SampleRate));
        return Map(src, d =>
        {
            var r = new byte[d.Length];
            for (int s = 0; s < d.Length; s += step)
            {
                int end = Math.Min(d.Length, s + step);
                byte peak = 0;
                for (int i = s; i < end; i++) peak = Math.Max(peak, d[i]);
                // Leave a short gap at the end of each step so beats stay distinct.
                int gap = step >= 4 ? Math.Max(1, step / 8) : 0;
                for (int i = s; i < end; i++) r[i] = i >= end - gap && peak > 0 ? (byte)(peak / 4) : peak;
            }
            return r;
        });
    }

    /// <summary>Blend the end of the loop into its start so the repeat point doesn't jump.</summary>
    public static Pattern CrossfadeSeam(Pattern src, double ms)
    {
        int n = src.Length;
        int f = Math.Clamp((int)Math.Round(ms / 1000.0 * src.SampleRate), 1, n / 2);
        if (n < 4) return src.Clone();
        return Map(src, d =>
        {
            var r = (byte[])d.Clone();
            for (int i = 0; i < f; i++)
            {
                double t = (double)(i + 1) / (f + 1);
                int idx = n - f + i;
                r[idx] = (byte)Math.Round(d[idx] * (1 - t) + d[i] * t);
            }
            return r;
        });
    }

    public static Pattern Reverse(Pattern src) => Map(src, d => d.Reverse().ToArray());

    public static Pattern SwapSides(Pattern src)
    {
        var p = src.Clone();
        (p.Left, p.Right) = (p.Right, p.Left);
        (p.LeftTrigger, p.RightTrigger) = (p.RightTrigger, p.LeftTrigger);
        return p;
    }

    /// <summary>Layer <paramref name="top"/> onto <paramref name="bottom"/> (louder sample wins). Keeps bottom's length.</summary>
    public static Pattern Overdub(Pattern bottom, Pattern top)
    {
        var p = bottom.Clone();
        foreach (var c in Channels)
        {
            var b = p[c];
            var t = top[c];
            for (int i = 0; i < b.Length && i < t.Length; i++) b[i] = Math.Max(b[i], t[i]);
        }
        p.Modified = DateTime.Now;
        return p;
    }
}
