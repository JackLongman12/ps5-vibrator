namespace DualSenseStudio.Core.Haptics;

public sealed record PresetInfo(string Name, string Description, Pattern Pattern);

/// <summary>Built-in looping patterns, generated procedurally (deterministic).</summary>
public static class PresetLibrary
{
    private const int Rate = Pattern.DefaultSampleRate;

    private static readonly Lazy<IReadOnlyList<PresetInfo>> _all = new(Build);
    public static IReadOnlyList<PresetInfo> All => _all.Value;

    private static IReadOnlyList<PresetInfo> Build()
    {
        var list = new List<PresetInfo>
        {
            Make("Heartbeat", "Lub-dub double beat, once a second.", 1.0, t =>
            {
                double v = Bump(t, 0.00, 0.12) + 0.65 * Bump(t, 0.24, 0.12);
                return (v, v * 0.55);
            }),
            Make("Pulse", "Steady on/off pulse with soft edges.", 1.0, t =>
            {
                double v = Trap(t, 0, 0.05, 0.35, 0.06);
                return (v, v);
            }),
            Make("Wave", "Smooth sine swell, rising and falling.", 2.0, t =>
            {
                double v = 0.5 - 0.5 * Math.Cos(2 * Math.PI * t / 2.0);
                return (v, v * 0.8);
            }),
            Make("Ping-Pong", "Bounces left to right.", 1.2, t =>
                (Trap(t, 0.0, 0.04, 0.42, 0.08), Trap(t, 0.6, 0.04, 0.42, 0.08))),
            Make("Ramp Up", "Builds from nothing to full, then drops.", 3.0, t =>
            {
                double v = t > 2.92 ? 0 : t / 2.92;
                return (v, v * v);
            }),
            Make("Escalator", "Five rising steps.", 4.0, t =>
            {
                int step = (int)(t / 0.8);
                double local = t - step * 0.8;
                double v = local > 0.74 ? 0 : (step + 1) / 5.0;
                return (v, v * 0.7);
            }),
            Make("Staccato", "Fast taps with an accent on every fourth.", 2.0, t =>
            {
                int i = (int)(t / 0.125);
                double local = t - i * 0.125;
                double amp = i % 4 == 0 ? 1.0 : 0.55;
                double v = local < 0.045 ? amp : 0;
                return (v * 0.5, v);
            }),
            MakeNoise("Earthquake", "Deep, uneven rumble with random jolts.", 4.0, 11, (t, n) =>
            {
                double l = 0.55 + 0.35 * n.Smooth(t * 6);
                double r = n.Smooth(t * 15 + 40) > 0.55 ? 0.9 : 0.15;
                return (l, r);
            }),
            MakeStateful("Engine Rev", "Idle putter that revs up and settles back.", 4.0, () =>
            {
                double phase = 0;
                return t =>
                {
                    double rev = t < 1 ? 0 : t < 2.5 ? (t - 1) / 1.5 : t < 3.0 ? 1 : 1 - (t - 3.0);
                    rev = Math.Clamp(rev, 0, 1);
                    double freq = 8 + 12 * rev;
                    phase += freq / Rate;
                    double mod = 0.5 + 0.5 * Math.Sin(2 * Math.PI * phase);
                    double baseAmp = 0.3 + 0.7 * rev;
                    return (baseAmp * (0.6 + 0.4 * mod), baseAmp * mod * 0.6);
                };
            }),
            MakeNoise("Rain", "Scattered light droplets.", 4.0, 23, (t, n) =>
            {
                double drop = n.Hash((int)(t / 0.03)) > 0.82 ? 0.3 + 0.5 * n.Hash((int)(t / 0.03) + 999) : 0;
                return (0.08, drop);
            }),
            Make("Purr", "Soft fast flutter, like a cat purring.", 2.0, t =>
            {
                double breath = 0.75 + 0.25 * Math.Sin(2 * Math.PI * t / 2.0);
                double flutter = 0.5 + 0.5 * Math.Sin(2 * Math.PI * 22 * t);
                return (breath * (0.2 + 0.25 * flutter), 0.1);
            }),
            MakeNoise("Ocean Swell", "Slow waves with a little surface texture.", 6.0, 5, (t, n) =>
            {
                double v = 0.5 - 0.5 * Math.Cos(2 * Math.PI * t / 6.0);
                return (v * 0.85 + 0.1 * n.Smooth(t * 8), v * v * 0.5);
            }),
            Make("SOS", "Morse code S-O-S.", 5.2, SosAt),
            Make("Tick-Tock", "Clock tick on the left, tock on the right.", 1.0, t =>
                (Trap(t, 0.0, 0.0, 0.04, 0.01) * 0.7, Trap(t, 0.5, 0.0, 0.06, 0.01) * 0.9)),
            MakeNoise("Thunder", "Distant rumble, a sharp crack, then a long roll.", 6.0, 77, (t, n) =>
            {
                if (t < 3.0)
                {
                    double build = t / 3.0;
                    return (0.15 + 0.45 * build * (0.6 + 0.4 * n.Smooth(t * 5)), 0.05);
                }
                if (t < 3.2) return (1.0, 1.0);
                double decay = Math.Exp(-(t - 3.2) * 1.1);
                return (decay * (0.5 + 0.5 * n.Smooth(t * 7)), decay * 0.3);
            }),
            MakeNoise("Chaos", "Random levels jumping ten times a second.", 3.0, 101, (t, n) =>
            {
                int i = (int)(t / 0.1);
                return (n.Hash(i), n.Hash(i + 5000));
            }),
            Make("Trigger Pulse", "The adaptive triggers buzz in time with a soft motor pulse.", 1.0,
                t => (Trap(t, 0, 0.03, 0.2, 0.05) * 0.4, Trap(t, 0, 0.03, 0.2, 0.05) * 0.3),
                t => (Trap(t, 0, 0.02, 0.25, 0.05), Trap(t, 0, 0.02, 0.25, 0.05)),
                triggerFrequency: 40),
            Make("Trigger Seesaw", "Left and right triggers take turns vibrating.", 2.0,
                _ => (0, 0),
                t => (0.5 + 0.5 * Math.Sin(2 * Math.PI * t / 2.0), 0.5 - 0.5 * Math.Sin(2 * Math.PI * t / 2.0)),
                triggerFrequency: 28),
        };
        return list;
    }

    // ---- shape helpers -------------------------------------------------

    /// <summary>Half-sine bump starting at <paramref name="start"/> lasting <paramref name="dur"/>.</summary>
    private static double Bump(double t, double start, double dur)
    {
        if (t < start || t > start + dur) return 0;
        return Math.Sin(Math.PI * (t - start) / dur);
    }

    /// <summary>Trapezoid: rise, hold, fall.</summary>
    private static double Trap(double t, double start, double rise, double hold, double fall)
    {
        double x = t - start;
        if (x < 0) return 0;
        if (x < rise) return rise <= 0 ? 1 : x / rise;
        x -= rise;
        if (x < hold) return 1;
        x -= hold;
        if (x < fall) return fall <= 0 ? 0 : 1 - x / fall;
        return 0;
    }

    private static (double, double) SosAt(double t)
    {
        const double dot = 0.15, dash = 0.45, gap = 0.15, letterGap = 0.45;
        double[] seq = [dot, dot, dot, -1, dash, dash, dash, -1, dot, dot, dot];
        double cursor = 0;
        foreach (var s in seq)
        {
            if (s < 0) { cursor += letterGap - gap; continue; }
            if (t >= cursor && t < cursor + s) return (0.9, 0.9);
            cursor += s + gap;
        }
        return (0, 0);
    }

    // ---- builders ------------------------------------------------------

    private static PresetInfo Make(string name, string desc, double seconds,
        Func<double, (double l, double r)> motors,
        Func<double, (double lt, double rt)>? triggers = null,
        int triggerFrequency = 35)
    {
        int n = (int)Math.Round(seconds * Rate);
        var p = Pattern.Create(n, name);
        p.Category = "Preset";
        p.Description = desc;
        p.TriggerFrequency = triggerFrequency;
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / Rate;
            var (l, r) = motors(t);
            p.Left[i] = ToByte(l);
            p.Right[i] = ToByte(r);
            if (triggers != null)
            {
                var (lt, rt) = triggers(t);
                p.LeftTrigger[i] = ToByte(lt);
                p.RightTrigger[i] = ToByte(rt);
            }
        }
        return new PresetInfo(name, desc, p);
    }

    private static PresetInfo MakeNoise(string name, string desc, double seconds, int seed,
        Func<double, Noise, (double l, double r)> f)
    {
        var noise = new Noise(seed);
        return Make(name, desc, seconds, t => f(t, noise));
    }

    private static PresetInfo MakeStateful(string name, string desc, double seconds,
        Func<Func<double, (double, double)>> factory) => Make(name, desc, seconds, factory());

    private static byte ToByte(double v) => (byte)Math.Clamp((int)Math.Round(v * 255), 0, 255);

    /// <summary>Tiny deterministic value-noise helper.</summary>
    private sealed class Noise(int seed)
    {
        public double Hash(int i)
        {
            unchecked
            {
                uint h = (uint)(i * 374761393 + seed * 668265263);
                h = (h ^ (h >> 13)) * 1274126177;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / (double)0xFFFFFF;
            }
        }

        public double Smooth(double x)
        {
            int i = (int)Math.Floor(x);
            double f = x - i;
            double u = f * f * (3 - 2 * f);
            return Hash(i) + (Hash(i + 1) - Hash(i)) * u;
        }
    }
}
