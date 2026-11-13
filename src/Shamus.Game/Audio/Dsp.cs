namespace Shamus.Game.Audio;

/// <summary>Tiny mono DSP toolbox. Pure functions (no Raylib), so rendering can run in parallel.</summary>
public static class Dsp
{
    public const int Rate = 44100;

    public static float[] Buf(float seconds) => new float[(int)(seconds * Rate)];

    /// <summary>Adds a frequency sweep (exponential f0 to f1) with exponential decay. shape 0 = sine, 1 = square-ish, 2 = saw.</summary>
    public static void AddSweep(float[] b, float t0, float dur, float f0, float f1, float amp, float decay, int shape = 0, float vibrato = 0)
    {
        int s = (int)(t0 * Rate), n = Math.Min((int)(dur * Rate), b.Length - s);
        double phase = 0;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)n;
            double f = f0 * Math.Pow(f1 / f0, t);
            if (vibrato > 0) f *= 1 + 0.03 * Math.Sin(i * vibrato / Rate * 6.2832);
            phase += f / Rate;
            double p = phase - Math.Floor(phase);
            double w = shape switch { 1 => p < 0.5 ? 0.6 : -0.6, 2 => 2 * p - 1, _ => Math.Sin(p * 6.2832) };
            b[s + i] += (float)(w * amp * Math.Exp(-decay * i / Rate));
        }
    }

    /// <summary>Low-passed noise burst with a cutoff sweep and exponential decay.</summary>
    public static void AddNoise(float[] b, Random rng, float t0, float dur, float cutStart, float cutEnd, float amp, float decay)
    {
        int s = (int)(t0 * Rate), n = Math.Min((int)(dur * Rate), b.Length - s);
        float y = 0;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)n;
            float cut = cutStart + (cutEnd - cutStart) * t;
            float a = 1 - MathF.Exp(-6.2832f * cut / Rate);
            y += a * ((float)(rng.NextDouble() * 2 - 1) - y);
            b[s + i] += y * amp * MathF.Exp(-decay * i / Rate);
        }
    }

    /// <summary>Inharmonic bell: a few partials with decay.</summary>
    public static void AddBell(float[] b, float t0, float f, float amp, float decay)
    {
        float[] ratios = { 1f, 2.76f, 5.4f, 8.9f }, gains = { 1f, 0.5f, 0.25f, 0.12f };
        for (int k = 0; k < ratios.Length; k++) AddSweep(b, t0, 1.2f, f * ratios[k], f * ratios[k], amp * gains[k], decay * (1 + k * 0.6f));
    }

    public static void FadeEdges(float[] b, float ms = 3)
    {
        int n = Math.Min((int)(ms / 1000f * Rate), b.Length / 2);
        for (int i = 0; i < n; i++) { float g = i / (float)n; b[i] *= g; b[^(i + 1)] *= g; }
    }

    public static void Normalize(float[] b, float peak)
    {
        float m = 0;
        foreach (var v in b) m = MathF.Max(m, MathF.Abs(v));
        if (m < 1e-6f) return;
        float g = peak / m;
        for (int i = 0; i < b.Length; i++) b[i] *= g;
    }

    /// <summary>Mono 16-bit 44.1 kHz WAV file bytes.</summary>
    public static byte[] ToWav(float[] s)
    {
        var ms = new MemoryStream(44 + s.Length * 2);
        var w = new BinaryWriter(ms);
        w.Write("RIFF"u8); w.Write(36 + s.Length * 2); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(Rate); w.Write(Rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8); w.Write(s.Length * 2);
        foreach (var v in s) w.Write((short)(Math.Clamp(v, -1f, 1f) * 32767));
        return ms.ToArray();
    }
}
