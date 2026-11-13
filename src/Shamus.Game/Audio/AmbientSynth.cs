namespace Shamus.Game.Audio;

/// <summary>
/// Real-time dark ambient generator (never repeats): sub-bass drone, slowly changing minor pad,
/// wind, rare FM bells, a heartbeat that fades in with tension, and a stereo cross-feedback delay.
/// Pure DSP (no Raylib). Tension and Muffle may be set from the game thread at any time.
/// </summary>
public sealed class AmbientSynth
{
    public const int Rate = 44100;
    private const double TwoPi = Math.PI * 2;

    /// <summary>0..1: danger in the current room (enemies, Shadow).</summary>
    public volatile float TensionTarget;
    /// <summary>0..1: 1 = heavily low-passed (menus, pause).</summary>
    public volatile float MuffleTarget;

    private readonly Random _rng;
    private long _n;
    private float _tension, _muffle;

    // drone
    private double _d1, _d2;
    // pad
    private static readonly double[][] Chords =
    {
        new[] { 110.0, 130.81, 164.81, 196.0 },   // Am(add b7)
        new[] { 87.31, 130.81, 174.61, 220.0 },   // Fmaj
        new[] { 98.0, 116.54, 146.83, 196.0 },    // Gm
        new[] { 110.0, 123.47, 164.81, 220.0 },   // A sus
        new[] { 82.41, 98.0, 123.47, 164.81 },    // Em
    };
    private readonly double[] _padFreq = new double[4], _padTarget = new double[4], _padPhase = new double[4];
    private long _nextChord;
    private float _padLp;
    // wind
    private float _bp1, _bp2;
    // bells
    private long _nextBell;
    private readonly List<Bell> _bells = new();
    private sealed class Bell { public double F, Cp, Mp, Age, Pan; }
    // heartbeat
    private long _nextBeat; private double _beatAge = 9, _beatPhase;
    // delay
    private readonly float[] _dl, _dr;
    private int _dpos;
    private float _dlpL, _dlpR;
    // master low-pass
    private float _mL, _mR;

    public AmbientSynth(int seed = 0)
    {
        _rng = seed == 0 ? new Random() : new Random(seed);
        var c = Chords[0];
        for (int i = 0; i < 4; i++) { _padFreq[i] = _padTarget[i] = c[i]; }
        _nextChord = (long)(Rate * (18 + _rng.NextDouble() * 12));
        _nextBell = (long)(Rate * (4 + _rng.NextDouble() * 6));
        _dl = new float[(int)(Rate * 0.43)]; _dr = new float[(int)(Rate * 0.57)];
        _muffle = MuffleTarget = 0.5f;
    }

    private static float Layer(double seconds, double period, double phase) => (float)(0.6 + 0.4 * Math.Sin(TwoPi * (seconds / period) + phase));

    /// <summary>Fills <paramref name="buf"/> with interleaved stereo 16-bit samples.</summary>
    public void Render(short[] buf, int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            double t = _n / (double)Rate;
            _n++;
            // smooth the control values
            _tension += (TensionTarget - _tension) * 0.00008f;
            _muffle += (MuffleTarget - _muffle) * 0.0004f;

            // slow sequencer: layers sweep between 20% and 100%
            float lDrone = Layer(t, 55, 0), lPad = Layer(t, 73, 1.7), lWind = Layer(t, 89, 3.1), lBell = Layer(t, 107, 4.4);

            // drone: two detuned subs -> slow beating, pulsing faster with tension
            _d1 += TwoPi * 55.0 / Rate; _d2 += TwoPi * (55.35 + _tension * 0.8) / Rate;
            float drone = (float)(Math.Sin(_d1) + Math.Sin(_d2) + 0.25 * Math.Sin(_d1 * 2)) * 0.04f * (0.55f + 0.45f * lDrone) * (1f + 0.4f * _tension);

            // pad: four voices gliding towards the current chord
            if (_n >= _nextChord)
            {
                var c = Chords[_rng.Next(Chords.Length)];
                for (int v = 0; v < 4; v++) _padTarget[v] = c[v] * (_rng.Next(6) == 0 ? 2 : 1);
                _nextChord = _n + (long)(Rate * (18 + _rng.NextDouble() * 14));
            }
            float pad = 0;
            for (int v = 0; v < 4; v++)
            {
                _padFreq[v] += (_padTarget[v] - _padFreq[v]) * 0.00004;
                _padPhase[v] += TwoPi * _padFreq[v] * (1 + 0.0015 * Math.Sin(t * 0.3 + v)) / Rate;
                double p = _padPhase[v];
                pad += (float)(Math.Sin(p) + 0.5 * Math.Sin(2 * p) + 0.3 * Math.Sin(3 * p) + 0.15 * Math.Sin(5 * p));
            }
            float breath = 0.5f + 0.5f * (float)Math.Sin(TwoPi * t / 17.0);
            float padCut = 250f + 450f * breath + 1400f * _tension * _tension;
            float a = 1f - MathF.Exp(-6.2832f * padCut / Rate);
            _padLp += a * (pad - _padLp);
            float padOut = _padLp * 0.028f * (0.4f + 0.6f * lPad) * 0.5f;

            // wind: noise through a resonant band-pass that wanders
            float noise = (float)(_rng.NextDouble() * 2 - 1);
            float fc = 350f + 450f * (0.5f + 0.5f * (float)Math.Sin(TwoPi * t / 23.0)) + 250f * _tension;
            float g = 2f * MathF.Sin(MathF.PI * fc / Rate);
            _bp1 += g * (noise - _bp2 - 0.18f * _bp1);   // state-variable filter, low damping
            _bp2 += g * _bp1;
            float windAmp = 0.35f + 0.5f * (0.5f + 0.5f * (float)Math.Sin(TwoPi * t / 31.0 + 1));
            float wind = _bp1 * 0.09f * (0.2f + 0.8f * lWind) * windAmp;

            // bells: rare, quiet FM strikes
            if (_n >= _nextBell)
            {
                double[] scale = { 220, 261.63, 293.66, 329.63, 392, 440, 523.25, 587.33, 659.25 };
                _bells.Add(new Bell { F = scale[_rng.Next(scale.Length)] * (_rng.Next(3) == 0 ? 2 : 1), Pan = _rng.NextDouble() * 2 - 1 });
                _nextBell = _n + (long)(Rate * (7 + _rng.NextDouble() * 16 - 4 * _tension));
            }
            float bellL = 0, bellR = 0;
            for (int b = _bells.Count - 1; b >= 0; b--)
            {
                var bl = _bells[b];
                bl.Age += 1.0 / Rate;
                if (bl.Age > 6) { _bells.RemoveAt(b); continue; }
                bl.Mp += TwoPi * bl.F * 1.4 / Rate; bl.Cp += TwoPi * bl.F / Rate;
                double idx = 2.5 * Math.Exp(-bl.Age * 2.2);
                float s = (float)(Math.Sin(bl.Cp + idx * Math.Sin(bl.Mp)) * Math.Exp(-bl.Age * 1.1)) * 0.08f * (0.3f + 0.7f * lBell);
                bellL += s * (float)(0.5 - 0.5 * bl.Pan); bellR += s * (float)(0.5 + 0.5 * bl.Pan);
            }

            // heartbeat when tense
            float beat = 0;
            if (_tension > 0.35f)
            {
                if (_n >= _nextBeat) { _beatAge = 0; _nextBeat = _n + (long)(Rate * (1.25 - 0.55 * _tension)); }
                _beatAge += 1.0 / Rate; _beatPhase += TwoPi * 48.0 / Rate;
                beat = (float)(Math.Sin(_beatPhase) * Math.Exp(-_beatAge * 14)) * 0.10f * (_tension - 0.3f);
            }

            // mix, delay
            float dry = drone + padOut + wind + beat;
            float inL = dry + bellL, inR = dry + bellR;
            int rl = (_dpos) % _dl.Length, rr = (_dpos) % _dr.Length;
            float dL = _dl[rl], dR = _dr[rr];
            _dlpL += 0.25f * (dL - _dlpL); _dlpR += 0.25f * (dR - _dlpR);
            _dl[rl] = inL * 0.5f + _dlpR * 0.45f;   // cross feedback
            _dr[rr] = inR * 0.5f + _dlpL * 0.45f;
            _dpos++;
            float outL = inL + _dlpL * 0.5f, outR = inR + _dlpR * 0.5f;

            // master low-pass (menus, pause) and soft clip
            float mc = 12000f * MathF.Pow(0.05f, _muffle);   // 12 kHz .. ~600 Hz
            float ma = 1f - MathF.Exp(-6.2832f * mc / Rate);
            _mL += ma * (outL - _mL); _mR += ma * (outR - _mR);
            buf[2 * i] = ToShort(MathF.Tanh(_mL * 2.2f) * 0.9f);
            buf[2 * i + 1] = ToShort(MathF.Tanh(_mR * 2.2f) * 0.9f);
        }
    }

    private static short ToShort(float v) => (short)(Math.Clamp(v, -1f, 1f) * 32767);
}
