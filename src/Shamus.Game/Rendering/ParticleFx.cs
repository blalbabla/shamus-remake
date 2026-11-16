using System.Numerics;
using Raylib_cs;

namespace Shamus.Game.Rendering;

/// <summary>
/// Cosmetic effects only: muzzle flashes, shell trails and impact bursts. The simulation knows nothing about them —
/// they are spawned from world events and from the live shell list, then advance on their own clock. Everything is
/// a sphere, like the rest of the game's primitives: sparks, fire and flashes are drawn additively (and feed the glow
/// pass), smoke is drawn with plain alpha so it darkens instead of brightening.
/// </summary>
public sealed class ParticleFx
{
    private enum Kind : byte { Spark, Fire, Flash, Smoke }

    private struct Particle
    {
        public Vector3 Pos;
        public Vector3 Vel;
        public float Life;
        public float MaxLife;
        public float Radius;
        public float Growth;
        public float Drag;
        public Color Color;
        public Kind Kind;
    }

    /// <summary>Hard cap so a long fight cannot grow the list without bound; the oldest puff is dropped.</summary>
    private const int MaxParticles = 700;

    private static readonly Color FlashColor = new(255, 250, 205, 255);
    private static readonly Color SparkColor = new(255, 210, 120, 255);
    private static readonly Color FireColor = new(255, 150, 40, 255);
    private static readonly Color SmokeColor = new(186, 186, 194, 100);
    private static readonly Color SmokeDark = new(124, 124, 134, 95);

    private readonly List<Particle> _items = new();
    private readonly Random _rng = new(20261116);

    public int Count => _items.Count;

    /// <summary>Barrel flash and a spray of sparks when the gun goes off.</summary>
    public void MuzzleFlash(Vector3 muzzle, Vector3 dir)
    {
        Add(muzzle + dir * 0.04f, Vector3.Zero, 0.09f, 0.11f, 0.26f, 0f, FlashColor, Kind.Flash);
        for (int i = 0; i < 5; i++)
            Add(muzzle, dir * R(5f, 10f) + Rand3(2.2f), R(0.08f, 0.18f), R(0.025f, 0.045f), 0.015f, 3.5f, SparkColor, Kind.Spark);
    }

    /// <summary>One puff of smoke and one lick of engine fire behind a flying shell.</summary>
    public void Trail(Vector3 tail, Vector3 dir)
    {
        // Two puffs per tick, spread back along the shell's last third of a cell: at 0.6 cells per tick a single
        // puff would leave a visible bead chain instead of a trail.
        for (int i = 0; i < 2; i++)
            Add(tail - dir * R(0f, 0.3f), -dir * R(0.3f, 0.9f) + Rand3(0.35f) + new Vector3(0f, R(0.1f, 0.4f), 0f),
                R(0.24f, 0.38f), R(0.05f, 0.075f), R(0.2f, 0.34f), 2.2f, SmokeColor, Kind.Smoke);
        Add(tail, -dir * R(1f, 2f) + Rand3(0.2f), R(0.04f, 0.07f), R(0.05f, 0.08f), 0.02f, 3f, FireColor, Kind.Fire);
    }

    /// <summary>Impact burst: a flash, sparks outward and a smoke cloud that rises and swells.</summary>
    public void Impact(Vector3 at)
    {
        Add(at, Vector3.Zero, 0.08f, 0.10f, 0.22f, 0f, FlashColor, Kind.Flash);   // the hit itself lights up
        for (int i = 0; i < 12; i++)
            Add(at, RandDir() * R(3f, 9f), R(0.12f, 0.3f), R(0.035f, 0.07f), 0.01f, 3f,
                i % 3 == 0 ? FlashColor : SparkColor, Kind.Spark);
        for (int i = 0; i < 6; i++)
            Add(at + Rand3(0.1f), new Vector3(R(-0.5f, 0.5f), R(0.2f, 0.9f), R(-0.5f, 0.5f)),
                R(0.4f, 0.7f), R(0.08f, 0.13f), R(0.3f, 0.5f), 1.5f, i == 0 ? SmokeDark : SmokeColor, Kind.Smoke);
    }

    public void Update(float dt)
    {
        if (dt <= 0f) return;
        for (int i = _items.Count - 1; i >= 0; i--)
        {
            var p = _items[i];
            p.Life -= dt;
            if (p.Life <= 0f) { _items.RemoveAt(i); continue; }
            p.Pos += p.Vel * dt;
            p.Vel *= MathF.Max(0f, 1f - p.Drag * dt);
            _items[i] = p;
        }
    }

    public void Clear() => _items.Clear();

    /// <summary>Draws every particle; call with the 3D view active, before the translucent walls.</summary>
    public void Draw()
    {
        if (_items.Count == 0) return;
        // Flush the queued opaque geometry while depth writes are still on, then add the effects on top.
        Rlgl.DrawRenderBatchActive();
        Rlgl.DisableDepthMask();

        Raylib.BeginBlendMode(BlendMode.Alpha);
        for (int i = 0; i < _items.Count; i++)
            if (_items[i].Kind == Kind.Smoke) Sphere(_items[i]);
        Raylib.EndBlendMode();

        Raylib.BeginBlendMode(BlendMode.Additive);
        for (int i = 0; i < _items.Count; i++)
            if (_items[i].Kind != Kind.Smoke) Sphere(_items[i]);
        Raylib.EndBlendMode();

        Rlgl.EnableDepthMask();
    }

    /// <summary>Emitters only, for the glow pass (the caller is already in additive mode).</summary>
    public void DrawEmissive()
    {
        for (int i = 0; i < _items.Count; i++)
            if (_items[i].Kind != Kind.Smoke) Sphere(_items[i]);
    }

    private static void Sphere(in Particle p)
    {
        float t = p.Life / p.MaxLife;                      // 1 -> 0 over the lifetime
        var c = new Color(p.Color.R, p.Color.G, p.Color.B, (byte)(p.Color.A * t));
        Raylib.DrawSphereEx(p.Pos, p.Radius + p.Growth * (1f - t), 5, 8, c);
    }

    private void Add(Vector3 pos, Vector3 vel, float life, float radius, float growth, float drag, Color color, Kind kind)
    {
        if (_items.Count >= MaxParticles) _items.RemoveAt(0);   // drop the oldest puff rather than skip the new one
        _items.Add(new Particle
        {
            Pos = pos, Vel = vel, Life = life, MaxLife = life,
            Radius = radius, Growth = growth, Drag = drag, Color = color, Kind = kind,
        });
    }

    private float R(float a, float b) => a + (float)_rng.NextDouble() * (b - a);

    private Vector3 Rand3(float s) => new(R(-s, s), R(-s, s), R(-s, s));

    private Vector3 RandDir()
    {
        var v = Rand3(1f);
        float len = v.Length();
        return len < 0.001f ? Vector3.UnitY : v / len;
    }
}
