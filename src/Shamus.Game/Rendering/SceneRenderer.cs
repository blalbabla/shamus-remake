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

    public SceneRenderer(ViewMode mode = ViewMode.Top)
    {
        Rig = new CameraRig(mode);
        _light.UseShadows(_shadow);
    }

    public void Dispose()
    {
        _light.Dispose();
        _shadow.Dispose();
        _room.Dispose();
    }

    private const float Ox = -RoomGrid.W / 2f, Oz = -RoomGrid.H / 2f;
    private static Vector3 P(float x, float y, float h) => new(Ox + x, h, Oz + y);

    /// <summary>Renders the light's depth map; call once per frame before <see cref="Draw"/>.</summary>
    public void RenderShadow(GameWorld world, float alpha)
    {
        if (!_shadow.IsValid) return;
        _shadow.Begin(_light.LightDirection);
        DrawWorld(world, alpha, shadowPass: true);
        _shadow.End();
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

        // Unlit: floor title, pod barriers and the translucent force-field walls.
        _room.DrawTitle(world.Room);
        if (world.IsPodRoom && !world.PodOpen) DrawPodBarriers(world, config);
        _room.DrawWalls(world.Grid, doorColor, doorSide);
        Rig.EndView();

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
            DrawTank(px, py, world.FaceX, world.FaceY, flash);
        }

        double now = Raylib.GetTime();
        foreach (var e in world.Enemies)
        {
            var c = P(e.X, e.Y, 0.8f);
            switch (e.Kind)
            {
                case EnemyKind.RoboDroid:
                    Raylib.DrawCube(c, 0.9f, 1.2f, 0.9f, Color.Gray);
                    Raylib.DrawCube(c + new Vector3(0, 0.8f, 0), 0.6f, 0.4f, 0.6f, Color.DarkBlue);
                    Raylib.DrawCube(c + new Vector3(0, 0.85f, 0.31f), 0.4f, 0.12f, 0.05f, Color.Red);
                    break;
                case EnemyKind.SpiralDrone:
                    Raylib.DrawSphere(c, 0.5f, Color.Magenta);
                    Raylib.DrawCube(c, 1.3f, 0.1f, 0.1f, Color.Purple);
                    break;
                default:
                    float hop = MathF.Abs(MathF.Sin((float)now * 6f + e.Phase)) * 0.4f;
                    Raylib.DrawCube(c + new Vector3(0, hop - 0.2f, 0), 0.8f, 0.7f, 0.8f, Color.Green);
                    break;
            }
        }
        if (world.ShadowActive)
        {
            var sp = P(world.ShadowX, world.ShadowY, 0.9f);
            Raylib.DrawCylinder(sp + new Vector3(0, -0.9f, 0), 0.45f, 0.65f, 1.8f, 10, new Color(30, 15, 50, 255));
            Raylib.DrawSphere(sp + new Vector3(0, 1.0f, 0), 0.45f, new Color(20, 8, 35, 255));
            Raylib.DrawCube(sp + new Vector3(-0.16f, 1.05f, 0.38f), 0.14f, 0.08f, 0.05f, Color.Red);
            Raylib.DrawCube(sp + new Vector3(0.16f, 1.05f, 0.38f), 0.14f, 0.08f, 0.05f, Color.Red);
        }
        foreach (var b in world.EnemyBullets) Raylib.DrawSphere(P(b.X, b.Y, 0.9f), 0.2f, Color.Orange);
        foreach (var b in world.Bullets) Raylib.DrawSphere(P(b.X, b.Y, 0.9f), 0.22f, Color.SkyBlue);

        return mysteryPos;
    }

    /// <summary>Pod room energy barriers: translucent amber slabs with bright edges (additive, unlit).</summary>
    private static void DrawPodBarriers(GameWorld world, GameConfig config)
    {
        Rlgl.DrawRenderBatchActive();   // flush opaque geometry before depth writes are disabled
        Rlgl.DisableDepthMask();
        Raylib.BeginBlendMode(BlendMode.Additive);
        for (int i = 0; i < 2; i++)
        {
            float bx = config.PodBarrierX0[i] + config.PodBarrierWidth / 2f;
            float s0 = world.SlitY[i], sh = config.PodSlitHeight;
            float topH = s0 - 1f, botY0 = s0 + sh, botH = RoomGrid.H - 1f - botY0;
            var fill = new Color((byte)255, (byte)190, (byte)30, (byte)70);
            var edge = new Color((byte)255, (byte)230, (byte)120, (byte)200);
            foreach (var (cy, hh) in new[] { (1f + topH / 2f, topH), (botY0 + botH / 2f, botH) })
            {
                Raylib.DrawCube(P(bx, cy, 0.8f), config.PodBarrierWidth, 1.6f, hh, fill);
                Raylib.DrawCubeWires(P(bx, cy, 0.8f), config.PodBarrierWidth, 1.6f, hh, edge);
            }
        }
        Raylib.EndBlendMode();
        Rlgl.EnableDepthMask();
    }

    /// <summary>Hero: a small tank with a boxy hull, two tracks, a dome turret and a gun barrel.</summary>
    private static void DrawTank(float px, float py, int faceX, int faceY, bool flash)
    {
        float fx = faceX, fy = faceY;
        float len = MathF.Sqrt(fx * fx + fy * fy);
        if (len < 0.5f) { fx = 1; fy = 0; len = 1; }
        fx /= len; fy /= len;
        float yaw = MathF.Atan2(fx, fy) * (180f / MathF.PI);

        var hull = flash ? Color.White : new Color((byte)86, (byte)150, (byte)86, (byte)255);
        var track = flash ? Color.White : new Color((byte)80, (byte)82, (byte)95, (byte)255);
        var dome = flash ? Color.White : new Color((byte)170, (byte)225, (byte)150, (byte)255);
        var gun = flash ? Color.White : new Color((byte)120, (byte)122, (byte)135, (byte)255);

        var b = P(px, py, 0);
        Rlgl.PushMatrix();
        Rlgl.Translatef(b.X, 0, b.Z);
        Rlgl.Rotatef(yaw, 0, 1, 0);   // local +Z is forward
        Raylib.DrawCube(new Vector3(-0.46f, 0.17f, 0), 0.20f, 0.34f, 1.10f, track);
        Raylib.DrawCube(new Vector3(0.46f, 0.17f, 0), 0.20f, 0.34f, 1.10f, track);
        Raylib.DrawCube(new Vector3(0, 0.38f, 0), 0.72f, 0.32f, 0.96f, hull);
        Raylib.DrawCylinder(new Vector3(0, 0.56f, -0.02f), 0.24f, 0.26f, 0.08f, 12, new Color((byte)150, (byte)205, (byte)130, (byte)255));
        Raylib.DrawSphere(new Vector3(0, 0.62f, -0.02f), 0.30f, dome);
        Raylib.DrawCylinderEx(new Vector3(0, 0.64f, 0.10f), new Vector3(0, 0.64f, 0.80f), 0.065f, 0.055f, 10, gun);
        Rlgl.PopMatrix();
    }

    private static Vector3? DrawObject(RoomObject o)
    {
        Vector3? mystery = null;
        var oc = AtariPalette.ToColor(o.Hue, o.Luma);
        var op = P(o.X, o.Y, 0.8f);
        float bob = MathF.Sin((float)Raylib.GetTime() * 3f) * 0.12f;
        switch (o.Kind)
        {
            case ObjectKind.Key:
                Raylib.DrawSphere(op + new Vector3(-0.5f, bob, 0), 0.35f, oc);
                Raylib.DrawCube(op + new Vector3(0.1f, bob, 0), 1.0f, 0.18f, 0.18f, oc);
                Raylib.DrawCube(op + new Vector3(0.45f, bob, 0.2f), 0.15f, 0.18f, 0.4f, oc);
                break;
            case ObjectKind.Keyhole:
                Raylib.DrawCube(op, 1.0f, 1.2f, 1.0f, oc);
                Raylib.DrawCube(op + new Vector3(0, 0.62f, 0), 0.25f, 0.05f, 0.6f, Color.Black);
                break;
            case ObjectKind.Potion:
                Raylib.DrawSphere(op + new Vector3(0, bob, 0), 0.45f, new Color(120, 230, 150, 255));
                Raylib.DrawCylinder(op + new Vector3(0, 0.3f + bob, 0), 0.12f, 0.12f, 0.6f, 8, Color.LightGray);
                break;
            case ObjectKind.Shadow:
                Raylib.DrawCylinder(op + new Vector3(0, -0.3f, 0), 0.5f, 0.7f, 1.8f, 10, new Color(40, 20, 60, 255));
                Raylib.DrawSphere(op + new Vector3(0, 1.0f, 0), 0.5f, new Color(25, 10, 40, 255));
                Raylib.DrawCube(op + new Vector3(-0.18f, 1.05f, 0.4f), 0.15f, 0.08f, 0.05f, Color.Red);
                Raylib.DrawCube(op + new Vector3(0.18f, 1.05f, 0.4f), 0.15f, 0.08f, 0.05f, Color.Red);
                break;
            case ObjectKind.Mystery:
                Raylib.DrawCube(op + new Vector3(0, bob, 0), 0.9f, 0.9f, 0.9f, new Color(150, 80, 220, 255));
                mystery = op + new Vector3(0, 1.0f, 0);
                break;
        }
        return mystery;
    }
}
