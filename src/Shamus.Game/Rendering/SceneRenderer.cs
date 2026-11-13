using System.Numerics;
using Raylib_cs;
using Shamus.Core;
using Shamus.Core.Mazes;
using Shamus.Core.Simulation;

namespace Shamus.Game.Rendering;

/// <summary>Draws the current room in 3D (top-down orthographic camera). Reads world state only.</summary>
public sealed class SceneRenderer
{
    public static readonly Color[] LevelColors = { new(220, 90, 80, 255), new(80, 120, 240, 255), new(80, 200, 100, 255), new(230, 70, 70, 255) };
    public static readonly string[] LevelNames = { "BLACK", "BLUE", "GREEN", "RED" };

    private readonly Camera3D _cam = new()
    {
        Position = new Vector3(0, 40, 0.01f),
        Target = Vector3.Zero,
        Up = new Vector3(0, 0, -1),
        FovY = RoomGrid.W * 0.62f,
        Projection = CameraProjection.Orthographic,
    };
    private const float Ox = -RoomGrid.W / 2f, Oz = -RoomGrid.H / 2f;
    private static Vector3 P(float x, float y, float h) => new(Ox + x, h, Oz + y);

    /// <summary>Renders the world; returns the screen position for the "?" label of a mystery bonus, if any.</summary>
    public void Draw(GameWorld world, GameConfig config, float alpha)
    {
        var maze = world.Maze;
        Vector3? mysteryPos = null;
        Raylib.BeginMode3D(_cam);

        var wall = LevelColors[maze.Level(world.Room)];
        Raylib.DrawCube(new Vector3(0, -0.5f, 0), RoomGrid.W, 1, RoomGrid.H, new Color(18, 18, 28, 255));
        var g = world.Grid;
        for (int y = 0; y < RoomGrid.H; y++)
        {
            for (int x = 0; x < RoomGrid.W;)
            {
                if (!g.IsSolid(x, y)) { x++; continue; }
                int x0 = x; while (x < RoomGrid.W && g.IsSolid(x, y)) x++;
                float w = x - x0;
                var c = P(x0 + w / 2f, y + 0.5f, 0.75f);
                Raylib.DrawCube(c, w, 1.5f, 1f, wall);
                Raylib.DrawCubeWires(c, w, 1.5f, 1f, Color.Black);
            }
        }

        if (world.DoorClosed && world.Obj != null)
        {
            var dc = AtariPalette.ToColor(world.Obj.Hue, world.Obj.Luma);
            float dxc = world.DoorSide < 0 ? 0.5f : RoomGrid.W - 0.5f;
            Raylib.DrawCube(P(dxc, (RoomGrid.DoorY0 + RoomGrid.DoorY1) / 2f, 0.9f), 1f, 1.8f, RoomGrid.DoorY1 - RoomGrid.DoorY0, dc);
        }
        if (world.Obj != null) mysteryPos = DrawObject(world.Obj);

        if (world.IsPodRoom && !world.PodOpen)
        {
            var bar = new Color(240, 220, 60, 255);
            for (int i = 0; i < 2; i++)
            {
                float bx = config.PodBarrierX0[i] + config.PodBarrierWidth / 2f;
                float s = world.SlitY[i], sh = config.PodSlitHeight;
                float topH = s - 1f, botY0 = s + sh, botH = RoomGrid.H - 1f - botY0;
                Raylib.DrawCube(P(bx, 1f + topH / 2f, 0.75f), config.PodBarrierWidth, 1.5f, topH, bar);
                Raylib.DrawCube(P(bx, botY0 + botH / 2f, 0.75f), config.PodBarrierWidth, 1.5f, botH, bar);
            }
        }

        float px = world.PrevPlayerX + (world.PlayerX - world.PrevPlayerX) * alpha;
        float py = world.PrevPlayerY + (world.PlayerY - world.PrevPlayerY) * alpha;
        if (world.Phase != GamePhase.GameOver)
        {
            bool flash = world.Phase == GamePhase.Dying && (world.TickCount / 4) % 2 == 0;
            var pc = P(px, py, 0.9f);
            Raylib.DrawCube(pc, 0.8f, 1.6f, 0.8f, flash ? Color.White : Color.Gold);
            Raylib.DrawSphere(pc + new Vector3(0, 1.1f, 0), 0.42f, flash ? Color.White : Color.Orange);
            Raylib.DrawCube(pc + new Vector3(world.FaceX * 0.5f, 0.2f, world.FaceY * 0.5f), 0.25f, 0.25f, 0.25f, Color.Red);
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

        Raylib.EndMode3D();
        if (mysteryPos is Vector3 mp)
        {
            var sp = Raylib.GetWorldToScreen(mp, _cam);
            Raylib.DrawText("?", (int)sp.X - 8, (int)sp.Y - 14, 32, Color.White);
        }
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
