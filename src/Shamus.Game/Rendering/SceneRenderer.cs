using System.Numerics;
using Raylib_cs;
using Shamus.Core;
using Shamus.Core.Mazes;
using Shamus.Core.Simulation;

namespace Shamus.Game.Rendering;

/// <summary>Draws the current room in 3D (top-down orthographic camera). Reads world state only.</summary>
public sealed class SceneRenderer : IDisposable
{
    public static readonly Color[] LevelColors = { new(220, 90, 80, 255), new(80, 120, 240, 255), new(80, 200, 100, 255), new(230, 70, 70, 255) };
    public static readonly string[] LevelNames = { "BLACK", "BLUE", "GREEN", "RED" };

    /// <summary>Design-space size of one frame; the window letterboxes it at native resolution.</summary>
    public const int VirtualWidth = 1280, VirtualHeight = 720;

    public CameraRig Rig { get; }
    private readonly RoomView _room = new();
    private readonly LightingShader _light = new();
    private readonly ShadowMap _shadow = new();
    private readonly GlowMap _glow = new();
    private readonly ParticleFx _fx = new();
    private float _trailTimer;

    public SceneRenderer(ViewMode mode = ViewMode.Top)
    {
        Rig = new CameraRig(mode);
        _light.UseShadows(_shadow);
    }

    public void Dispose()
    {
        _light.Dispose();
        _shadow.Dispose();
        _glow.Dispose();
        _room.Dispose();
    }

    private const float Ox = -RoomGrid.W / 2f, Oz = -RoomGrid.H / 2f;
    private static Vector3 P(float x, float y, float h) => new(Ox + x, h, Oz + y);

    /// <summary>
    /// Interpolates the hull heading across the ±180 wrap, the same way positions are interpolated: the heading is
    /// simulated per tick and the tank turns in place before it drives (see <see cref="GameWorld.Turning"/>).
    /// </summary>
    private static float LerpHeading(float from, float to, float t) => from + Wrap180(to - from) * t;

    /// <summary>Wraps an angle in degrees to (-180, 180], so a turn always takes the short way round.</summary>
    private static float Wrap180(float deg)
    {
        deg %= 360f;
        return deg > 180f ? deg - 360f : deg < -180f ? deg + 360f : deg;
    }

    /// <summary>
    /// Turns the frame's world events into effects: a muzzle flash where a shot leaves the barrel, an impact burst
    /// where a shell dies. Called right after every <c>GameWorld.Tick</c>, while the events are still fresh.
    /// </summary>
    public void OnEvents(GameWorld world)
    {
        foreach (var e in world.Events)
        {
            switch (e.Kind)
            {
                case WorldEventKind.Shot:
                    var (fx, fy) = Facing(world.FaceX, world.FaceY);
                    _fx.MuzzleFlash(P(e.X, e.Y, RoomMetrics.BarrelY), new Vector3(fx, 0f, fy));
                    break;
                case WorldEventKind.EnemyShot:
                    // one flash per fresh bolt: a drone's fan lights its three slits, a droid lights its turret band
                    foreach (var b in world.EnemyBullets)
                        if (b.Age == 1 && b.Owner is { } shooter)
                            _fx.EnemyMuzzle(P(shooter.X, shooter.Y, EnemyShellY(b)) + Dir3(b) * MuzzleOffset(b));
                    break;
                case WorldEventKind.BulletHitWall:
                case WorldEventKind.BulletHitBarrier:
                case WorldEventKind.BulletsCancelled:
                case WorldEventKind.BulletHitShadow:
                case WorldEventKind.EnemyKilled:
                case WorldEventKind.PodOpened:
                    _fx.Impact(P(e.X, e.Y, RoomMetrics.BarrelY));
                    break;
            }
        }
    }

    /// <summary>Advances the effects and leaves a smoke/fire trail behind every shell; dt = 0 freezes them (pause).</summary>
    public void UpdateEffects(GameWorld world, float dt)
    {
        _fx.Update(dt);
        if (dt <= 0f) return;
        _trailTimer += dt;
        const float TrailStep = 1f / 60f;   // emission rate is per tick, not per frame, so the trail density is stable
        while (_trailTimer >= TrailStep)
        {
            _trailTimer -= TrailStep;
            foreach (var b in world.Bullets)
            {
                var dir = Dir3(b);
                _fx.Trail(P(b.X, b.Y, RoomMetrics.BarrelY) - dir * 0.12f, dir);
            }
            foreach (var b in world.EnemyBullets)
            {
                var dir = Dir3(b);
                _fx.EnemyTrail(P(b.X, b.Y, EnemyShellY(b)) - dir * 0.1f, dir);
            }
        }
    }

    /// <summary>Drops every live effect, e.g. when a new game starts.</summary>
    public void ResetEffects()
    {
        _fx.Clear();
        _trailTimer = 0f;
    }

    /// <summary>Unit direction of the hull facing (falls back to +x before the player has moved).</summary>
    private static (float X, float Y) Facing(int faceX, int faceY)
    {
        float fx = faceX, fy = faceY;
        float len = MathF.Sqrt(fx * fx + fy * fy);
        return len < 0.5f ? (1f, 0f) : (fx / len, fy / len);
    }

    /// <summary>Unit flight direction of a shell (always horizontal).</summary>
    private static Vector3 Dir3(Bullet b) => Vector3.Normalize(new Vector3(b.DX, 0f, b.DY));

    /// <summary>Renders the light's depth map; call once per frame before <see cref="Draw"/>.</summary>
    public void RenderShadow(GameWorld world, float alpha)
    {
        if (!_shadow.IsValid) return;
        _shadow.Begin(_light.LightDirection);
        DrawWorld(world, alpha, shadowPass: true);
        _shadow.End();
    }

    /// <summary>
    /// Renders only the emitters into the glow target and blurs them; call once per frame before <see cref="Draw"/>,
    /// because it switches framebuffers and resets matrices just like the shadow pass.
    /// </summary>
    public void RenderGlow(GameWorld world, GameConfig config)
    {
        if (!_glow.IsValid) return;
        _glow.BeginEmitters();
        Rig.BeginView();
        Rlgl.DisableDepthTest();   // additive light only: there is nothing in this target to occlude against
        Raylib.BeginBlendMode(BlendMode.Additive);   // emitters accumulate, so their alpha reads as intensity
        DrawEmissive(world, config);
        Raylib.EndBlendMode();
        Rig.EndView();
        _glow.EndEmitters();
    }

    /// <summary>Renders the lit world, then unlit translucent overlays and the bloom pass.</summary>
    public void Draw(GameWorld world, GameConfig config, float alpha)
    {
        var maze = world.Maze;
        Color? doorColor = world.DoorClosed && world.Obj != null ? AtariPalette.ToColor(world.Obj.Hue, world.Obj.Luma) : null;
        int doorSide = world.DoorClosed ? world.DoorSide : 0;

        Rig.BeginView();
        _light.Begin();
        DrawWorld(world, alpha, shadowPass: false);
        // The jumpers stay inside the lit pass so their facets get real shading, but they never write depth:
        // a translucent gem that did would punch a hole into the floor behind it.
        DrawJumpers(world, (float)Raylib.GetTime());
        _light.End();

        // Plateau sides are unlit: a flat colour leaves no shaded return face in the convex corners.
        if (Rig.IsometricBlend > 0.5f)
            _room.DrawPlateauSides(world.Grid, (int)MathF.Floor(world.PlayerX), (int)MathF.Floor(world.PlayerY));

        // Unlit: shells, effects, floor title, pod barriers and the translucent force-field walls.
        DrawShells(world);
        DrawEnemyShells(world);
        _fx.Draw();
        _room.DrawTitle(world.Room);
        if (world.IsPodRoom && !world.PodOpen) DrawPodBarriers(world, config);
        _room.DrawWalls(world.Grid, doorColor, doorSide);
        Rig.EndView();

        // Bloom is a 2D additive pass, so it needs the 1280x720 UI projection back.
        _glow.Composite();
    }

    /// <summary>Opaque, shadow-casting world contents. The shadow pass skips the floor so only casters write depth.</summary>
    private void DrawWorld(GameWorld world, float alpha, bool shadowPass)
    {
        var maze = world.Maze;
        int level = maze.Level(world.Room);
        double time = Raylib.GetTime();

        if (shadowPass) _room.DrawPosts(world.Grid, level, time);
        else _room.DrawSolid(world.Grid, level, time, cliff: Rig.IsometricBlend > 0.5f,
            (int)MathF.Floor(world.PlayerX), (int)MathF.Floor(world.PlayerY));

        if (world.Obj != null) DrawItem(world.Obj, time);

        float px = world.PrevPlayerX + (world.PlayerX - world.PrevPlayerX) * alpha;
        float py = world.PrevPlayerY + (world.PlayerY - world.PrevPlayerY) * alpha;
        if (world.Phase != GamePhase.GameOver)
        {
            bool flash = world.Phase == GamePhase.Dying && (world.TickCount / 4) % 2 == 0;
            DrawTank(px, py, LerpHeading(world.PrevHeading, world.Heading, alpha), flash);
        }

        double now = Raylib.GetTime();
        foreach (var e in world.Enemies)
        {
            switch (e.Kind)
            {
                case EnemyKind.RoboDroid: DrawRoboDroid(e, alpha, (float)now); break;
                case EnemyKind.SpiralDrone: DrawSpiralDrone(e, world, (float)now); break;
                default:
                    // snap jumper: a ghostly gem, drawn in the unlit pass; here it only casts a shadow
                    if (shadowPass) JumperShadow(e);
                    break;
            }
        }
        if (world.ShadowActive) DrawShadow(P(world.ShadowX, world.ShadowY, 0f));
    }

    /// <summary>
    /// Robo-droid, kept deliberately simple: two tracks with a lighter top plate, a hull between them and a wide drum
    /// turret that overhangs the tracks. The drum carries a dark groove around its whole diameter; the red beacon runs
    /// along that groove (which is also the gun band — bolts leave the drum at that height).
    /// </summary>
    private static void DrawRoboDroid(Enemy e, float alpha, float time)
    {
        var p = EnemyPalette.Of(e.ColorIndex);
        var track = new Color((byte)96, (byte)98, (byte)104, (byte)255);
        var trackTop = new Color((byte)142, (byte)144, (byte)150, (byte)255);
        var groove = new Color((byte)26, (byte)26, (byte)30, (byte)255);
        const float r = RoomMetrics.RobotTurretRadius;

        // the hull swings toward its driving axis with the same easing as the player's tank: the sim owns the angle,
        // the renderer only interpolates between ticks
        float yaw = LerpHeading(e.PrevHeading, e.Heading, alpha);

        var c = P(e.X, e.Y, 0f);
        Rlgl.PushMatrix();
        Rlgl.Translatef(c.X, 0f, c.Z);
        Rlgl.Rotatef(yaw, 0, 1, 0);

        foreach (float sx in new[] { -0.40f, 0.40f })
        {
            Raylib.DrawCube(new Vector3(sx, 0.11f, 0f), 0.18f, 0.22f, 1.16f, track);
            Raylib.DrawCube(new Vector3(sx, 0.235f, 0f), 0.15f, 0.03f, 1.08f, trackTop);
        }

        Raylib.DrawCube(new Vector3(0f, 0.33f, 0f), 0.74f, 0.18f, 0.88f, p.Body);

        // the turret drum overhangs the tracks; a lighter disc caps it
        Raylib.DrawCylinder(new Vector3(0f, 0.40f, 0f), r, r, 0.38f, 16, p.Body);
        Raylib.DrawCylinder(new Vector3(0f, 0.775f, 0f), r, r * 0.96f, 0.03f, 16, p.Light);

        // dark groove all the way round, at the height the bolts leave from
        Raylib.DrawCylinder(new Vector3(0f, RoomMetrics.RobotMuzzleY - 0.045f, 0f), r + 0.006f, r + 0.006f, 0.09f, 16, groove);

        Rlgl.PopMatrix();
        DrawRobotBeacon(e, time);
    }

    /// <summary>Red light running along the turret groove, like a lighthouse. Also fed to the glow pass.</summary>
    private static void DrawRobotBeacon(Enemy e, float time)
    {
        var beacon = new Color((byte)255, (byte)55, (byte)40, (byte)255);
        var c = P(e.X, e.Y, 0f);
        Rlgl.PushMatrix();
        Rlgl.Translatef(c.X, 0f, c.Z);
        Rlgl.Rotatef(time * 2.2f * (180f / MathF.PI) % 360f, 0, 1, 0);
        Raylib.DrawCube(new Vector3(0f, RoomMetrics.RobotMuzzleY, RoomMetrics.RobotTurretRadius + 0.002f),
            0.17f, 0.085f, 0.055f, beacon);
        Rlgl.PopMatrix();
    }

    /// <summary>
    /// Spiral drone, built after the reference: a low wide saucer with conical spikes around the rim and a tapering
    /// tower on top. The tower carries three vertical slits and the whole drone turns to face the player, so a shot
    /// leaves the slits both visually and in the simulation (bolts fly from the same angles as the fan).
    /// </summary>
    private static void DrawSpiralDrone(Enemy e, GameWorld world, float time)
    {
        var p = EnemyPalette.Of(e.ColorIndex);
        var slit = new Color((byte)24, (byte)8, (byte)28, (byte)255);

        float hover = MathF.Sin(time * 2.2f + e.Phase) * 0.06f;
        float dx = world.PlayerX - e.X, dy = world.PlayerY - e.Y;
        float yaw = MathF.Atan2(dx, dy) * (180f / MathF.PI);   // local +Z points at the player

        var c = P(e.X, e.Y, 0f);
        Rlgl.PushMatrix();
        Rlgl.Translatef(c.X, hover, c.Z);
        Rlgl.Rotatef(yaw, 0, 1, 0);

        // saucer body, with a slightly wider lighter disc capping it
        Raylib.DrawCylinder(new Vector3(0, 0.30f, 0), 0.58f, 0.58f, 0.20f, 18, p.Body);
        Raylib.DrawCylinder(new Vector3(0, 0.50f, 0), 0.50f, 0.58f, 0.04f, 18, p.Light);

        // six spikes around the rim, angled slightly down as in the reference
        for (int i = 0; i < 6; i++)
        {
            float a = i * MathF.PI / 3f + 0.26f;
            var dir = new Vector3(MathF.Sin(a), 0f, MathF.Cos(a));
            Raylib.DrawCylinderEx(dir * 0.54f + new Vector3(0f, 0.41f, 0f), dir * 0.88f + new Vector3(0f, 0.36f, 0f),
                0.055f, 0.004f, 5, p.Light);
        }

        // tapering tower (truncated cone)
        Raylib.DrawCylinderEx(new Vector3(0, 0.53f, 0), new Vector3(0, 0.86f, 0), 0.34f, 0.235f, 14, p.Dark);

        // three vertical slits on the face that looks at the player: the gun ports. Their angles match the +-0.35 rad
        // spread of a drone's fan, so the bolts really do leave the slits.
        foreach (float a in new[] { -0.35f, 0f, 0.35f })
        {
            Rlgl.PushMatrix();
            Rlgl.Rotatef(a * 180f / MathF.PI, 0, 1, 0);
            Raylib.DrawCube(new Vector3(0f, RoomMetrics.DroneSlitY, 0.288f), 0.055f, 0.20f, 0.05f, slit);
            Rlgl.PopMatrix();
        }

        Rlgl.PopMatrix();
    }

    /// <summary>
    /// Player shells: a slim yellow cylinder of the barrel's diameter with a bright cone on the nose, drawn along the
    /// flight direction and at the barrel height, so a shot visibly leaves the muzzle and travels where it collides.
    /// <paramref name="intensity"/> scales the colour for the glow pass, where a full-brightness shell would bloom
    /// into a ball larger than the tank.
    /// </summary>
    private static void DrawShells(GameWorld world, float intensity = 1f)
    {
        var body = Dim(new Color((byte)255, (byte)205, (byte)60, (byte)255), intensity);
        var tip = Dim(new Color((byte)255, (byte)245, (byte)170, (byte)255), intensity);
        foreach (var b in world.Bullets)
        {
            var dir = Dir3(b);
            var p = P(b.X, b.Y, RoomMetrics.BarrelY);
            var tail = p - dir * 0.12f;
            var nose = p + dir * 0.10f;
            Raylib.DrawCylinderEx(tail, nose, 0.055f, 0.055f, 8, body);          // barrel diameter
            Raylib.DrawCylinderEx(nose, nose + dir * 0.11f, 0.055f, 0.004f, 8, tip);   // cone nose
        }
    }

    private static Color Dim(Color c, float f) =>
        new((byte)(c.R * f), (byte)(c.G * f), (byte)(c.B * f), c.A);

    /// <summary>
    /// Enemy shells: a small plasma orb, the same size as the player's shell but round and orange-hot with a pale core,
    /// so the two are never confused. Drawn unlit at the height its shooter fires from.
    /// </summary>
    private static void DrawEnemyShells(GameWorld world, float intensity = 1f)
    {
        var shell = Dim(new Color((byte)255, (byte)120, (byte)40, (byte)255), intensity);
        var core = Dim(new Color((byte)255, (byte)226, (byte)150, (byte)255), intensity);
        foreach (var b in world.EnemyBullets)
        {
            var p = P(b.X, b.Y, EnemyShellY(b));
            Raylib.DrawSphereEx(p, 0.085f, 6, 10, shell);
            Raylib.DrawSphereEx(p, 0.045f, 6, 8, core);
        }
    }

    /// <summary>Height an enemy bolt flies at: the owner's gun port (drone slits, droid turret band), not a fixed floor offset.</summary>
    private static float EnemyShellY(Bullet b) => b.Owner?.Kind switch
    {
        EnemyKind.RoboDroid => RoomMetrics.RobotMuzzleY,
        _ => RoomMetrics.DroneSlitY,
    };

    /// <summary>How far the muzzle flash sits from the shooter's centre: at the droid's drum wall or the drone's slits.</summary>
    private static float MuzzleOffset(Bullet b) => b.Owner?.Kind switch
    {
        EnemyKind.RoboDroid => RoomMetrics.RobotTurretRadius + 0.02f,
        _ => 0.30f,
    };

    /// <summary>Centre height of a snap jumper's gem; the top of the upper cone reaches the enemy height contract.</summary>
    private const float JumperY = 0.48f;

    /// <summary>
    /// Snap jumpers are never solid geometry: they are drawn unlit and translucent. While waiting they shimmer between
    /// SnapJumperAlphaMin and Max; a jump cross-fades, so the ghost at the old spot fades out while the body fades in
    /// at the new one. The ghost is drawn first, dimmer, so the body always reads as the real one.
    /// </summary>
    private static void DrawJumpers(GameWorld world, float time)
    {
        Rlgl.DrawRenderBatchActive();
        Rlgl.DisableDepthMask();   // a fading body must not punch a hole in the depth buffer
        Raylib.BeginBlendMode(BlendMode.Alpha);
        foreach (var e in world.Enemies)
        {
            if (e.Kind != EnemyKind.SnapJumper) continue;
            var (body, ghost, hop) = JumperFade(e, world.Config, time);
            var paint = EnemyPalette.Of(e.ColorIndex);
            JumperModel(P(e.BlinkFromX, e.BlinkFromY, JumperY + hop), ghost * 0.85f, paint, time);
            JumperModel(P(e.X, e.Y, JumperY + hop), body, paint, time);
        }
        Raylib.EndBlendMode();
        Rlgl.EnableDepthMask();
    }

    /// <summary>
    /// The jumper: a floating gem — hexagonal bipyramid — around a bright turning core, with two shards orbiting it.
    /// Sharp, faceted and clearly not boxy, so it never reads as one of the other two enemies.
    /// </summary>
    private static void JumperModel(Vector3 center, float alpha, EnemyPalette.Scheme paint, float time)
    {
        if (alpha <= 0.01f) return;
        byte A(float f) => (byte)(Math.Clamp(alpha * f, 0f, 1f) * 255);
        var shell = new Color(paint.Body.R, paint.Body.G, paint.Body.B, A(0.8f));
        var core = new Color(paint.Light.R, paint.Light.G, paint.Light.B, A(0.95f));

        // two cones meeting at the equator = a faceted crystal
        Raylib.DrawCylinderEx(center + new Vector3(0f, -0.46f, 0f), center, 0.004f, 0.36f, 6, shell);
        Raylib.DrawCylinderEx(center, center + new Vector3(0f, 0.46f, 0f), 0.36f, 0.004f, 6, shell);

        // the core turns inside the gem
        Rlgl.PushMatrix();
        Rlgl.Translatef(center.X, center.Y, center.Z);
        Rlgl.Rotatef(time * 55f % 360f, 0f, 1f, 0f);
        Raylib.DrawCube(Vector3.Zero, 0.22f, 0.22f, 0.22f, core);
        Rlgl.PopMatrix();

        // two shards orbiting the gem, so it is alive even while standing still
        for (int i = 0; i < 2; i++)
        {
            float a = time * 2.6f + i * MathF.PI;
            var off = new Vector3(MathF.Sin(a) * 0.52f, 0.06f - i * 0.14f, MathF.Cos(a) * 0.52f);
            Raylib.DrawCube(center + off, 0.12f, 0.12f, 0.12f, core);
        }
    }

    /// <summary>Depth-only silhouette of the gem for the shadow pass.</summary>
    private static void JumperShadow(Enemy e)
    {
        var c = P(e.X, e.Y, JumperY);
        Raylib.DrawCylinderEx(c + new Vector3(0f, -0.46f, 0f), c, 0.004f, 0.36f, 6, Color.White);
        Raylib.DrawCylinderEx(c, c + new Vector3(0f, 0.46f, 0f), 0.36f, 0.004f, 6, Color.White);
    }

    /// <summary>Opacity of a jumper's body and of the ghost left behind, plus its idle bob.</summary>
    private static (float Body, float Ghost, float Hop) JumperFade(Enemy e, GameConfig cfg, float time)
    {
        float min = Math.Clamp(cfg.SnapJumperAlphaMin, 0f, 1f);
        float max = Math.Clamp(cfg.SnapJumperAlphaMax, min, 1f);
        float shimmer = min + (max - min) * (0.5f + 0.5f * MathF.Sin(time * 5f + e.Phase));
        float hop = MathF.Abs(MathF.Sin(time * 6f + e.Phase)) * 0.08f;
        if (e.BlinkTicks <= 0) return (shimmer, 0f, hop);
        float t = 1f - e.BlinkTicks / (float)Math.Max(1, cfg.SnapBlinkTicks);   // 0 at the jump, 1 when it has landed
        return (t, 1f - t, hop);
    }

    /// <summary>Pod room energy barriers: translucent amber slabs with bright edges (additive, unlit).</summary>
    private static void DrawPodBarriers(GameWorld world, GameConfig config)
    {
        Rlgl.DrawRenderBatchActive();   // flush opaque geometry before depth writes are disabled
        Rlgl.DisableDepthMask();
        Raylib.BeginBlendMode(BlendMode.Additive);
        BarrierBoxes(world, config, new Color((byte)255, (byte)190, (byte)30, (byte)70),
            new Color((byte)255, (byte)230, (byte)120, (byte)200), wires: true);
        Raylib.EndBlendMode();
        Rlgl.EnableDepthMask();
    }

    /// <summary>Emitters for the glow pass. Bright, unlit colours: the blur turns them into halos.</summary>
    private void DrawEmissive(GameWorld world, GameConfig config)
    {
        int level = world.Maze.Level(world.Room);
        double time = Raylib.GetTime();
        Color? doorColor = world.DoorClosed && world.Obj != null ? AtariPalette.ToColor(world.Obj.Hue, world.Obj.Luma) : null;

        _room.DrawEmissive(world.Grid, level, time, doorColor, world.DoorClosed ? world.DoorSide : 0);
        DrawShells(world, 0.7f);
        DrawEnemyShells(world, 0.85f);
        foreach (var e in world.Enemies)
            if (e.Kind == EnemyKind.RoboDroid) DrawRobotBeacon(e, (float)time);   // the lighthouse sweep blooms
        foreach (var e in world.Enemies)
        {
            if (e.Kind != EnemyKind.SnapJumper) continue;   // the shimmering jumper glows softly
            var (body, ghost, hop) = JumperFade(e, config, (float)time);
            var paint = EnemyPalette.Of(e.ColorIndex);
            JumperModel(P(e.BlinkFromX, e.BlinkFromY, JumperY + hop), ghost * 0.5f, paint, (float)time);
            JumperModel(P(e.X, e.Y, JumperY + hop), body * 0.5f, paint, (float)time);
        }
        _fx.DrawEmissive();
        if (world.IsPodRoom && !world.PodOpen)
            BarrierBoxes(world, config, new Color((byte)255, (byte)200, (byte)60, (byte)110),
                new Color((byte)255, (byte)240, (byte)160, (byte)255), wires: false);

        if (world.Obj is { Kind: ObjectKind.Mystery } o)
            DrawItem(o, time, emissive: true);
        if (world.ShadowActive)
        {
            float bodyH = RoomMetrics.ShadowHeight - 0.4f;
            var feet = P(world.ShadowX, world.ShadowY, 0f);
            Raylib.DrawCube(feet + new Vector3(-0.15f, bodyH + 0.28f, 0.40f), 0.2f, 0.12f, 0.1f, new Color((byte)255, (byte)40, (byte)40, (byte)255));
            Raylib.DrawCube(feet + new Vector3(0.15f, bodyH + 0.28f, 0.40f), 0.2f, 0.12f, 0.1f, new Color((byte)255, (byte)40, (byte)40, (byte)255));
        }
    }

    /// <summary>The two pod barriers of the room, each split into a slab above and below the moving slit.</summary>
    private static void BarrierBoxes(GameWorld world, GameConfig config, Color fill, Color edge, bool wires)
    {
        for (int i = 0; i < 2; i++)
        {
            float bx = config.PodBarrierX0[i] + config.PodBarrierWidth / 2f;
            float s0 = world.SlitY[i], sh = config.PodSlitHeight;
            float topH = s0 - 1f, botY0 = s0 + sh, botH = RoomGrid.H - 1f - botY0;
            float by = RoomMetrics.PodBarrierHeight / 2f;
            foreach (var (cy, hh) in new[] { (1f + topH / 2f, topH), (botY0 + botH / 2f, botH) })
            {
                Raylib.DrawCube(P(bx, cy, by), config.PodBarrierWidth, RoomMetrics.PodBarrierHeight, hh, fill);
                if (wires) Raylib.DrawCubeWires(P(bx, cy, by), config.PodBarrierWidth, RoomMetrics.PodBarrierHeight, hh, edge);
            }
        }
    }

    /// <summary>
    /// Hero: a small tank with a boxy hull, two tracks, a dome turret and a gun barrel.
    /// Local Y runs from 0 (tracks on the floor) to <see cref="RoomMetrics.PlayerHeight"/> (top of the dome).
    /// <paramref name="yaw"/> is the simulated hull heading in degrees (local +Z is forward), already interpolated.
    /// </summary>
    private static void DrawTank(float px, float py, float yaw, bool flash)
    {
        const float trackH = RoomMetrics.TrackHeight;   // 0.34
        const float trackY = trackH / 2f;               // tracks stand on the floor
        // The hull, plate, turret and barrel keep their spacing but all drop by half a track height:
        // the tracks then flank the hull instead of carrying it like a box on skis.
        const float drop = trackH / 2f;
        const float hullY = 0.44f - drop;    // 0.27
        const float plateY = 0.60f - drop;   // 0.43
        const float ringY = 0.66f - drop;    // 0.49
        const float domeY = 0.70f - drop;    // 0.53

        var hull = flash ? Color.White : new Color((byte)86, (byte)150, (byte)86, (byte)255);
        var hullTop = flash ? Color.White : new Color((byte)108, (byte)176, (byte)104, (byte)255);
        var track = flash ? Color.White : new Color((byte)80, (byte)82, (byte)95, (byte)255);
        var tread = flash ? Color.White : new Color((byte)54, (byte)56, (byte)66, (byte)255);
        var turret = flash ? Color.White : new Color((byte)150, (byte)205, (byte)130, (byte)255);
        var dome = flash ? Color.White : new Color((byte)170, (byte)225, (byte)150, (byte)255);
        var gun = flash ? Color.White : new Color((byte)120, (byte)122, (byte)135, (byte)255);

        var b = P(px, py, 0);
        Rlgl.PushMatrix();
        Rlgl.Translatef(b.X, 0, b.Z);
        Rlgl.Rotatef(yaw, 0, 1, 0);   // local +Z is forward
        // tracks, with tread blocks so the silhouette reads as a tracked vehicle, not a box
        Raylib.DrawCube(new Vector3(-0.42f, trackY, 0), 0.20f, trackH, 1.06f, track);
        Raylib.DrawCube(new Vector3(0.42f, trackY, 0), 0.20f, trackH, 1.06f, track);
        for (int i = -1; i <= 1; i++)
        {
            Raylib.DrawCube(new Vector3(-0.42f, trackY, i * 0.34f), 0.22f, 0.10f, 0.12f, tread);
            Raylib.DrawCube(new Vector3(0.42f, trackY, i * 0.34f), 0.22f, 0.10f, 0.12f, tread);
        }
        // hull, then a slightly lighter top plate
        Raylib.DrawCube(new Vector3(0, hullY, 0), 0.68f, 0.30f, 0.94f, hull);
        Raylib.DrawCube(new Vector3(0, plateY, -0.02f), 0.56f, 0.06f, 0.80f, hullTop);
        // turret ring, small dome, barrel pointing forward (the muzzle sits at GameConfig.MuzzleLength)
        Raylib.DrawCylinder(new Vector3(0, ringY, 0), 0.23f, 0.24f, 0.08f, 14, turret);
        Raylib.DrawSphere(new Vector3(0, domeY, 0), 0.20f, dome);
        Raylib.DrawCylinderEx(new Vector3(0, RoomMetrics.BarrelY, 0.14f), new Vector3(0, RoomMetrics.BarrelY, 0.74f),
            0.055f, 0.045f, 10, gun);
        Rlgl.PopMatrix();
    }

    /// <summary>The Shadow: a dark column with a hooded head, deliberately taller than the walls (its feet are at y = 0).</summary>
    private static void DrawShadow(Vector3 feet)
    {
        var body = new Color(30, 15, 50, 255);
        var head = new Color(20, 8, 35, 255);
        float bodyH = RoomMetrics.ShadowHeight - 0.4f;
        Raylib.DrawCylinder(feet + new Vector3(0, bodyH / 2f, 0), 0.45f, 0.65f, bodyH, 10, body);
        Raylib.DrawSphere(feet + new Vector3(0, bodyH + 0.24f, 0), 0.4f, head);
        // eyes sit on the front of the head sphere (radius 0.4 at that height), so they are not buried inside it
        Raylib.DrawCube(feet + new Vector3(-0.15f, bodyH + 0.28f, 0.40f), 0.16f, 0.09f, 0.08f, Color.Red);
        Raylib.DrawCube(feet + new Vector3(0.15f, bodyH + 0.28f, 0.40f), 0.16f, 0.09f, 0.08f, Color.Red);
    }

    /// <summary>
    /// Room pickups. All of them float above the floor. The key lies flat and spins in both views; the keyhole
    /// lies flat and still; the potion plus and the mystery question mark stand upright and spin in the isometric
    /// view, but lie flat and still in the top view. The change follows the animated camera tilt.
    /// </summary>
    private void DrawItem(RoomObject o, double time, bool emissive = false)
    {
        float t = (float)time;
        float blend = Rig.IsometricBlend;
        float bob = MathF.Sin(t * 2.2f) * 0.035f;
        var oc = AtariPalette.ToColor(o.Hue, o.Luma);
        float upright = RoomMetrics.PropHover + (RoomMetrics.StandingPropHover - RoomMetrics.PropHover) * blend + bob;

        switch (o.Kind)
        {
            case ObjectKind.Key:
                ItemTransformFlat(o, RoomMetrics.PropHover + bob, t * 30f);
                KeyModel(oc);
                Rlgl.PopMatrix();
                break;
            case ObjectKind.Keyhole:
                ItemTransformFlat(o, RoomMetrics.KeyholeHover + bob * 0.3f, 0f);
                KeyholeModel(oc);
                Rlgl.PopMatrix();
                break;
            case ObjectKind.Potion:
                ItemTransformUpright(o, upright, t * 30f * blend, -90f * (1f - blend));
                PlusModel(new Color((byte)120, (byte)230, (byte)150, (byte)255));
                Rlgl.PopMatrix();
                break;
            case ObjectKind.Mystery:
                ItemTransformUpright(o, upright, t * 30f * blend, -90f * (1f - blend));
                QuestionModel(emissive ? new Color((byte)235, (byte)190, (byte)255, (byte)255)
                                       : new Color((byte)170, (byte)100, (byte)240, (byte)255));
                Rlgl.PopMatrix();
                break;
            case ObjectKind.Shadow:
                DrawShadow(P(o.X, o.Y, 0f));
                break;
        }
    }

    /// <summary>Flat pickup matrix: hover, then spin around the vertical (the key turns like it lies on a table).</summary>
    private void ItemTransformFlat(RoomObject o, float hover, float spinDeg)
    {
        var pos = P(o.X, o.Y, hover);
        Rlgl.PushMatrix();
        Rlgl.Translatef(pos.X, pos.Y, pos.Z);
        Rlgl.Rotatef(spinDeg, 0f, 1f, 0f);
    }

    /// <summary>
    /// Standing pickup matrix: hover, spin around the vertical, then lay it down with the camera tilt (top view).
    /// In the isometric view the plus and the question mark therefore turn like a sign on a pole.
    /// </summary>
    private void ItemTransformUpright(RoomObject o, float hover, float spinDeg, float tiltDeg)
    {
        var pos = P(o.X, o.Y, hover);
        Rlgl.PushMatrix();
        Rlgl.Translatef(pos.X, pos.Y, pos.Z);
        Rlgl.Rotatef(spinDeg, 0f, 1f, 0f);
        Rlgl.Rotatef(tiltDeg, 1f, 0f, 0f);
    }

    /// <summary>A key lying flat: ring bow, shaft and two teeth in the floor plane; +X is the tip.</summary>
    private static void KeyModel(Color c)
    {
        for (int i = 0; i < 12; i++)
        {
            float a = i * MathF.Tau / 12f;
            Raylib.DrawSphere(new Vector3(-0.62f + MathF.Cos(a) * 0.30f, 0f, MathF.Sin(a) * 0.30f), 0.095f, c);
        }
        Raylib.DrawCube(new Vector3(0.02f, 0f, 0f), 0.95f, 0.12f, 0.12f, c);
        Raylib.DrawCube(new Vector3(0.36f, 0f, 0.19f), 0.16f, 0.12f, 0.34f, c);
        Raylib.DrawCube(new Vector3(0.10f, 0f, 0.15f), 0.12f, 0.12f, 0.26f, c);
    }

    /// <summary>A keyhole plaque lying flat: a coloured disc with a dark hole and slot on top.</summary>
    private static void KeyholeModel(Color c)
    {
        var hole = new Color((byte)10, (byte)10, (byte)14, (byte)255);
        Raylib.DrawCylinder(new Vector3(0f, 0f, 0f), 0.60f, 0.60f, 0.10f, 20, c);
        Raylib.DrawCylinder(new Vector3(0f, 0.101f, 0.02f), 0.15f, 0.15f, 0.02f, 12, hole);
        Raylib.DrawCube(new Vector3(0f, 0.111f, 0.32f), 0.15f, 0.02f, 0.44f, hole);
    }

    /// <summary>A big plus in the local XY plane (stands upright before the camera-tilt lay-down).</summary>
    private static void PlusModel(Color c)
    {
        Raylib.DrawCube(new Vector3(0f, 0f, 0f), 0.48f, 1.50f, 0.20f, c);
        Raylib.DrawCube(new Vector3(0f, 0f, 0f), 1.50f, 0.48f, 0.20f, c);
    }

    /// <summary>A question mark in the local XY plane: hook, stem and dot, built from small spheres.</summary>
    private static void QuestionModel(Color c)
    {
        var center = new Vector3(0f, 0.23f, 0f);
        const float R = 0.27f;
        for (int i = 0; i <= 12; i++)
        {
            float a = MathF.PI / 180f * (200f - 220f * i / 12f);
            Raylib.DrawSphere(center + new Vector3(MathF.Cos(a) * R, MathF.Sin(a) * R, 0f), 0.085f, c);
        }
        Raylib.DrawSphere(new Vector3(0.19f, 0.0f, 0f), 0.082f, c);
        Raylib.DrawSphere(new Vector3(0.13f, -0.14f, 0f), 0.082f, c);
        Raylib.DrawSphere(new Vector3(0.07f, -0.27f, 0f), 0.082f, c);
        Raylib.DrawSphere(new Vector3(0f, -0.50f, 0f), 0.10f, c);
    }
}
