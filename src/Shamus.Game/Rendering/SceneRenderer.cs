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
                    // one flash per fresh bolt: a drone's fan lights its three slits, a droid lights its eye
                    foreach (var b in world.EnemyBullets)
                        if (b.Age == 1 && b.Owner is { } shooter)
                            _fx.EnemyMuzzle(P(shooter.X, shooter.Y, EnemyShellY(b)) + Dir3(b) * 0.34f);
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

    /// <summary>Renders the lit world, then unlit translucent overlays and the 2D "?" label.</summary>
    public void Draw(GameWorld world, GameConfig config, float alpha)
    {
        var maze = world.Maze;
        Color? doorColor = world.DoorClosed && world.Obj != null ? AtariPalette.ToColor(world.Obj.Hue, world.Obj.Luma) : null;
        int doorSide = world.DoorClosed ? world.DoorSide : 0;

        Rig.BeginView();
        _light.Begin();
        Vector3? mysteryPos = DrawWorld(world, alpha, shadowPass: false);
        _light.End();

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

        if (mysteryPos is Vector3 mp)
        {
            var sp = Raylib.GetWorldToScreenEx(mp, Rig.Camera, VirtualWidth, VirtualHeight);
            Raylib.DrawText("?", (int)sp.X - 8, (int)sp.Y - 14, 32, Color.White);
        }
    }

    /// <summary>Opaque, shadow-casting world contents. The shadow pass skips the floor so only casters write depth.</summary>
    private Vector3? DrawWorld(GameWorld world, float alpha, bool shadowPass)
    {
        var maze = world.Maze;
        int level = maze.Level(world.Room);
        double time = Raylib.GetTime();
        Vector3? mysteryPos = null;

        if (shadowPass) _room.DrawPosts(world.Grid, level, time);
        else _room.DrawSolid(world.Grid, level, time);

        if (world.Obj != null) mysteryPos = DrawObject(world.Obj);

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
                case EnemyKind.RoboDroid: DrawRoboDroid(e); break;
                case EnemyKind.SpiralDrone: DrawSpiralDrone(e, world, (float)now); break;
                default:
                    float hop = MathF.Abs(MathF.Sin((float)now * 6f + e.Phase)) * 0.25f;
                    Raylib.DrawCube(P(e.X, e.Y, 0.35f + hop), 0.8f, 0.7f, 0.8f, EnemyPalette.Of(e.ColorIndex).Body);
                    break;
            }
        }
        if (world.ShadowActive) DrawShadow(P(world.ShadowX, world.ShadowY, 0f));

        return mysteryPos;
    }

    /// <summary>Methodical walker: a boxy body with a dark head and a single red eye that fires straight ahead.</summary>
    private static void DrawRoboDroid(Enemy e)
    {
        var p = EnemyPalette.Of(e.ColorIndex);
        // body + head together reach RoomMetrics.EnemyHeight
        Raylib.DrawCube(P(e.X, e.Y, 0.39f), 0.9f, 0.78f, 0.9f, p.Body);
        Raylib.DrawCube(P(e.X, e.Y, RoomMetrics.RobotEyeY), 0.55f, 0.18f, 0.55f, p.Dark);
        Raylib.DrawCube(P(e.X, e.Y, RoomMetrics.RobotEyeY) + new Vector3(0, 0, 0.29f), 0.4f, 0.09f, 0.05f, Color.Red);
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

    /// <summary>Height an enemy bolt flies at: the owner's gun port (drone slits, droid eye), not a fixed floor offset.</summary>
    private static float EnemyShellY(Bullet b) => b.Owner?.Kind switch
    {
        EnemyKind.RoboDroid => RoomMetrics.RobotEyeY,
        _ => RoomMetrics.DroneSlitY,
    };

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
        _fx.DrawEmissive();
        if (world.IsPodRoom && !world.PodOpen)
            BarrierBoxes(world, config, new Color((byte)255, (byte)200, (byte)60, (byte)110),
                new Color((byte)255, (byte)240, (byte)160, (byte)255), wires: false);

        if (world.Obj is { Kind: ObjectKind.Mystery } o)
            Raylib.DrawCube(P(o.X, o.Y, 0.45f), 0.85f, 0.85f, 0.85f, new Color((byte)190, (byte)120, (byte)255, (byte)255));
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

    private static Vector3? DrawObject(RoomObject o)
    {
        Vector3? mystery = null;
        var oc = AtariPalette.ToColor(o.Hue, o.Luma);
        float bob = MathF.Sin((float)Raylib.GetTime() * 3f) * 0.1f;
        switch (o.Kind)
        {
            case ObjectKind.Key:
                var kp = P(o.X, o.Y, 0.5f + bob);
                Raylib.DrawSphere(kp + new Vector3(-0.45f, 0, 0), 0.32f, oc);
                Raylib.DrawCube(kp + new Vector3(0.1f, 0, 0), 1.0f, 0.16f, 0.16f, oc);
                Raylib.DrawCube(kp + new Vector3(0.45f, 0, 0.18f), 0.14f, 0.16f, 0.36f, oc);
                break;
            case ObjectKind.Keyhole:
                var hp = P(o.X, o.Y, 0f);
                Raylib.DrawCube(hp + new Vector3(0, 0.4f, 0), 1.0f, 0.8f, 1.0f, oc);
                Raylib.DrawCube(hp + new Vector3(0, 0.83f, 0), 0.25f, 0.05f, 0.6f, Color.Black);
                break;
            case ObjectKind.Potion:
                var pp = P(o.X, o.Y, 0.4f + bob);
                Raylib.DrawSphere(pp, 0.4f, new Color(120, 230, 150, 255));
                Raylib.DrawCylinder(pp + new Vector3(0, 0.3f, 0), 0.12f, 0.12f, 0.2f, 8, Color.LightGray);
                break;
            case ObjectKind.Shadow:
                DrawShadow(P(o.X, o.Y, 0f));
                break;
            case ObjectKind.Mystery:
                var mp = P(o.X, o.Y, 0.45f + bob);
                Raylib.DrawCube(mp, 0.8f, 0.8f, 0.8f, new Color(150, 80, 220, 255));
                mystery = mp + new Vector3(0, 0.5f, 0);
                break;
        }
        return mystery;
    }
}
