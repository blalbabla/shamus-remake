namespace Shamus.Game.Audio;

/// <summary>
/// Real-time electronic ambient generator (never repeats): a pulsing sub-bass sequencer, detuned saw pad,
/// machine-room noise and hum, digital blips and chirps, a soft kick and a stereo cross-feedback delay.
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
    // machine-room noise
    private float _bp1, _bp2;
    // bass sequencer: one semitone offset per 16th note
    private static readonly int[] BassPattern = { 0, 0, 12, 0, 0, 7, 0, 10, 0, 0, 12, 0, 3, 0, 10, 7 };
    private long _stepIndex = long.MinValue, _kickIndex = long.MinValue;
    private double _bassPhase, _arpPhase, _kickPhase, _kickAge;
    private float _bassLp;
    // digital blips
    private long _nextBlip;
    private readonly List<Blip> _blips = new();
    private sealed class Blip { public int Type; public double F0, F1, Age, Len, Phase, Pan; }
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
        _nextBlip = (long)(Rate * (1 + _rng.NextDouble() * 2));
        _dl = new float[(int)(Rate * 0.37)]; _dr = new float[(int)(Rate * 0.51)];
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
            _tension += (TensionTarget - _tension) * 0.00008f;
            _muffle += (MuffleTarget - _muffle) * 0.0004f;

            // electronic tempo: a little faster and busier as danger rises
            double bpm = 96 + 28 * _tension;
            double stepPos = t * bpm / 60.0 * 4.0;          // 16th notes
            long stepIndex = (long)stepPos;
            if (stepIndex != _stepIndex) _stepIndex = stepIndex;
            int step = (int)(stepIndex & 15);
            double stepPhase = stepPos - stepIndex;

            // pad: slow chord progression; its root also drives the bass
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
                _padPhase[v] += TwoPi * _padFreq[v] * (1 + 0.0018 * Math.Sin(t * 0.27 + v)) / Rate;
                double ph = _padPhase[v] % TwoPi;
                double saw = ph / Math.PI - 1;                       // detuned saw pair, not a sine bell
                double square = Math.Tanh(2.2 * Math.Sin(ph));
                pad += (float)(saw * 0.35 + square * 0.22 + Math.Sin(ph * 2) * 0.06);
            }
            float breath = 0.5f + 0.5f * (float)Math.Sin(TwoPi * t / 17.0);
            float padCut = 300f + 600f * breath + 1600f * _tension * _tension;
            float a = 1f - MathF.Exp(-6.2832f * padCut / Rate);
            _padLp += a * (pad - _padLp);
            float padOut = _padLp * 0.040f * (0.4f + 0.6f * Layer(t, 73, 1.7));

            // drone: two detuned subs -> slow beating, pulsing faster with tension
            _d1 += TwoPi * 55.0 / Rate; _d2 += TwoPi * (55.35 + _tension * 0.8) / Rate;
            float drone = (float)(Math.Sin(_d1) + Math.Sin(_d2) + 0.3 * Math.Sin(_d1 * 2)) * 0.022f
                          * (0.55f + 0.45f * Layer(t, 55, 0)) * (1f + 0.3f * _tension) * 0.55f;

            // bass sequencer: saw bass on every 16th, root follows the pad
            double root = _padFreq[0];
            double bassF = root * Math.Pow(2, BassPattern[step] / 12.0) * 0.5;
            float bassEnv = (float)((1 - Math.Exp(-stepPhase * 200)) * Math.Exp(-stepPhase * 5.5));
            _bassPhase += TwoPi * bassF / Rate;
            double bp = _bassPhase % TwoPi;
            double bassWave = bp / Math.PI - 1;
            float bass = (float)(bassWave * 0.75 + Math.Sin(bp) * 0.25) * bassEnv * 0.032f
                         * (0.8f + 0.2f * Layer(t, 43, 0.7));

            // arpeggio: chord tones an octave up, on every 16th, panned by a slow LFO
            double arpF = _padFreq[(step * 3) % 4] * 2;
            _arpPhase += TwoPi * arpF / Rate;
            double ap = _arpPhase % TwoPi;
            float arp = (float)(Math.Tanh(2.5 * Math.Sin(ap)) * 0.6 + (ap / Math.PI - 1) * 0.4)
                        * (float)((1 - Math.Exp(-stepPhase * 300)) * Math.Exp(-stepPhase * 8)) * 0.026f;
            float arpPan = (float)Math.Sin(TwoPi * t / 11.0);

            // machine room: band-passed noise, electrical hum, slow servo wander
            float noise = (float)(_rng.NextDouble() * 2 - 1);
            float fc = 240f + 260f * (0.5f + 0.5f * (float)Math.Sin(TwoPi * t / 19.0)) + 220f * _tension;
            float g = 2f * MathF.Sin(MathF.PI * fc / Rate);
            _bp1 += g * (noise - _bp2 - 0.22f * _bp1);   // state-variable filter, low damping
            _bp2 += g * _bp1;
            float hum = (float)(Math.Sin(TwoPi * 100 * t + 0.3) * 0.010 + Math.Sin(TwoPi * 151 * t + 1.1) * 0.005)
                        * (0.6f + 0.4f * Layer(t, 89, 3.1));
            float machine = _bp1 * 0.055f * (0.3f + 0.7f * Layer(t, 89, 3.1)) + hum;

            // kick / machine pulse on every quarter note
            double beatPos = t * bpm / 60.0;
            long kickIndex = (long)beatPos;
            if (kickIndex != _kickIndex) { _kickIndex = kickIndex; _kickAge = 0; _kickPhase = 0; }
            _kickAge += 1.0 / Rate;
            double kickF = 48 + 75 * Math.Exp(-_kickAge * 24);
            _kickPhase += TwoPi * kickF / Rate;
            float kickEnv = (float)Math.Exp(-_kickAge * 9);
            float pulse = (float)Math.Sin(_kickPhase) * kickEnv * 0.050f * (0.55f + 0.45f * _tension);

            // digital blips: two-tone beeps, chirps, data bursts and rare sonar pings
            if (_n >= _nextBlip)
            {
                int type = _rng.Next(4);
                double[] scale = { 523.25, 587.33, 659.25, 783.99, 880, 1046.5, 1174.66, 1396.91 };
                double f = scale[_rng.Next(scale.Length)];
                var bl = new Blip
                {
                    Type = type,
                    F0 = f,
                    F1 = type switch { 0 => f * 1.5, 1 => f * 2.5, 2 => f * 0.55, _ => f },
                    Len = type switch { 0 => 0.16, 1 => 0.14, 2 => 0.09, _ => 1.7 },
                    Pan = _rng.NextDouble() * 2 - 1,
                };
                _blips.Add(bl);
                double gap = 0.35 + _rng.NextDouble() * 2.2 - 0.8 * _tension;
                _nextBlip = _n + (long)(Rate * Math.Max(0.18, gap));
            }
            float blipL = 0, blipR = 0;
            for (int b = _blips.Count - 1; b >= 0; b--)
            {
                var bl = _blips[b];
                bl.Age += 1.0 / Rate;
                if (bl.Age >= bl.Len) { _blips.RemoveAt(b); continue; }
                double u = bl.Age / bl.Len;
                double f = bl.Type == 0 ? (u < 0.5 ? bl.F0 : bl.F1) : bl.F0 + (bl.F1 - bl.F0) * u;
                bl.Phase += TwoPi * f / Rate;
                double ph = bl.Phase % TwoPi;
                float env, s;
                switch (bl.Type)
                {
                    case 0:  // square beep, two tones
                        s = (float)Math.Tanh(3.5 * Math.Sin(ph));
                        env = (float)Math.Exp(-bl.Age * 16);
                        break;
                    case 1:  // rising chirp
                        s = (float)Math.Sin(ph);
                        env = (float)Math.Exp(-bl.Age * 10);
                        break;
                    case 2:  // data/ring-mod burst
                        s = (float)(Math.Tanh(5 * Math.Sin(ph)) * Math.Sin(ph * 3.3));
                        env = (float)Math.Exp(-bl.Age * 45);
                        break;
                    default:  // sonar ping
                        s = (float)Math.Sin(ph);
                        env = (float)Math.Exp(-bl.Age * 2.4);
                        break;
                }
                float amp = bl.Type == 3 ? 0.09f : 0.07f;
                float smp = s * env * amp * (0.5f + 0.5f * Layer(t, 41, 4.4));
                smp *= (float)(1 - Math.Exp(-bl.Age * 400));   // a couple of ms of attack, no click\n                blipL += smp * (float)(0.5 - 0.5 * bl.Pan);
                blipR += smp * (float)(0.5 + 0.5 * bl.Pan);
            }

            // mix, sidechain the bass under the kick, then delay
            float bassOut = _bassLp + 0.08f * (bass - _bassLp);
            _bassLp = bassOut;
            bassOut *= 1f - 0.30f * kickEnv;
            float dry = drone + bassOut + padOut + machine + pulse;
            float inL = dry + blipL + arp * (0.5f - 0.5f * arpPan), inR = dry + blipR + arp * (0.5f + 0.5f * arpPan);
            int rl = _dpos % _dl.Length, rr = _dpos % _dr.Length;
            float dL = _dl[rl], dR = _dr[rr];
            _dlpL += 0.25f * (dL - _dlpL); _dlpR += 0.25f * (dR - _dlpR);
            _dl[rl] = inL * 0.5f + _dlpR * 0.45f;   // cross feedback
            _dr[rr] = inR * 0.5f + _dlpL * 0.45f;
            _dpos++;
            float outL = inL + _dlpL * 0.5f, outR = inR + _dlpR * 0.5f;

            // master low-pass (menus, pause) and soft clip
            float mc = 12000f * MathF.Pow(0.05f, _muffle);
            float ma = 1f - MathF.Exp(-6.2832f * mc / Rate);
            _mL += ma * (outL - _mL); _mR += ma * (outR - _mR);
            buf[2 * i] = ToShort(MathF.Tanh(_mL * 2.8f) * 0.95f);
            buf[2 * i + 1] = ToShort(MathF.Tanh(_mR * 2.8f) * 0.95f);
        }
    }

    private static short ToShort(float v) => (short)(Math.Clamp(v, -1f, 1f) * 32767);
}
