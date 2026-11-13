using System.Numerics;
using Raylib_cs;
using Shamus.Core;
using Shamus.Core.Mazes;
using Shamus.Core.Simulation;
using Shamus.Game.Rendering;

// Controls: WASD/arrows move (8 directions), Space/Ctrl fire, [ ] debug room jump, R restart.
var mazeName = args.Length > 0 ? args[0] : "c64";
int startRoom = args.Length > 1 ? int.Parse(args[1]) : 0;
var maze = MazeLoader.Load(Path.Combine(AppContext.BaseDirectory, "assets", "mazes", mazeName + ".maze"));
// Tunable constants live in assets/config.json (source copy preferred, so F5 picks up edits without rebuild).
string ConfigPath()
{
    for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
    {
        var p = Path.Combine(d.FullName, "assets", "config.json");
        if (File.Exists(p) && Directory.Exists(Path.Combine(d.FullName, "src"))) return p;
    }
    return Path.Combine(AppContext.BaseDirectory, "assets", "config.json");
}
var config = GameConfig.Load(ConfigPath());
var world = new GameWorld(maze, config, startRoom);

Raylib.InitWindow(1280, 720, "Shamus 3D");
Raylib.SetTargetFPS(60);

var cam = new Camera3D
{
    Position = new Vector3(0, 40, 0.01f),
    Target = Vector3.Zero,
    Up = new Vector3(0, 0, -1),
    FovY = RoomGrid.W * 0.62f,
    Projection = CameraProjection.Orthographic,
};

Color[] levelColors = { new(220, 90, 80, 255), new(80, 120, 240, 255), new(80, 200, 100, 255), new(230, 70, 70, 255) };
const float Step = 1f / 60f;
float acc = 0;
float ox = -RoomGrid.W / 2f, oz = -RoomGrid.H / 2f;
Vector3 P(float x, float y, float h) => new(ox + x, h, oz + y);

while (!Raylib.WindowShouldClose())
{
    if (Raylib.IsKeyPressed(KeyboardKey.RightBracket)) world = new GameWorld(maze, config, world.Room + 1);
    if (Raylib.IsKeyPressed(KeyboardKey.LeftBracket)) world = new GameWorld(maze, config, world.Room - 1);
    if (Raylib.IsKeyPressed(KeyboardKey.R)) world = new GameWorld(maze, config, startRoom);
    if (Raylib.IsKeyPressed(KeyboardKey.F5)) { config = GameConfig.Load(ConfigPath()); world = new GameWorld(maze, config, world.Room); }

    int mx = 0, my = 0;
    if (Raylib.IsKeyDown(KeyboardKey.A) || Raylib.IsKeyDown(KeyboardKey.Left)) mx--;
    if (Raylib.IsKeyDown(KeyboardKey.D) || Raylib.IsKeyDown(KeyboardKey.Right)) mx++;
    if (Raylib.IsKeyDown(KeyboardKey.W) || Raylib.IsKeyDown(KeyboardKey.Up)) my--;
    if (Raylib.IsKeyDown(KeyboardKey.S) || Raylib.IsKeyDown(KeyboardKey.Down)) my++;
    bool fire = Raylib.IsKeyDown(KeyboardKey.Space) || Raylib.IsKeyDown(KeyboardKey.LeftControl);

    acc = MathF.Min(acc + Raylib.GetFrameTime(), 0.1f);
    while (acc >= Step) { world.Tick(new InputState(mx, my, fire)); acc -= Step; }
    float alpha = acc / Step;

    Raylib.BeginDrawing();
    Raylib.ClearBackground(new Color(8, 8, 14, 255));
    Raylib.BeginMode3D(cam);

    var wall = levelColors[maze.Level(world.Room)];
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

    // locked door overlay in the key colour
    if (world.DoorClosed && world.Obj != null)
    {
        var dc = AtariPalette.ToColor(world.Obj.Hue, world.Obj.Luma);
        float dxc = world.DoorSide < 0 ? 0.5f : RoomGrid.W - 0.5f;
        Raylib.DrawCube(P(dxc, (RoomGrid.DoorY0 + RoomGrid.DoorY1) / 2f, 0.9f), 1f, 1.8f, RoomGrid.DoorY1 - RoomGrid.DoorY0, dc);
    }
    Vector3? mysteryPos = null;
    if (world.Obj != null)
    {
        var o = world.Obj;
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
                Raylib.DrawSphere(op + new Vector3(0, bob, 0), 0.45f, oc.A == 0 ? Color.Lime : new Color(120, 230, 150, 255));
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
                mysteryPos = op + new Vector3(0, 1.0f, 0);
                break;
        }
    }

    if (world.IsPodRoom)
    {
        if (!world.PodOpen)
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
    }

    // player (interpolated)
    float px = world.PrevPlayerX + (world.PlayerX - world.PrevPlayerX) * alpha;
    float py = world.PrevPlayerY + (world.PlayerY - world.PrevPlayerY) * alpha;
    if (world.Phase != GamePhase.GameOver)
    {
        bool flash = world.Phase == GamePhase.Dying && (world.TickCount / 4) % 2 == 0;
        var pc = P(px, py, 0.9f);
        Raylib.DrawCube(pc, 0.8f, 1.6f, 0.8f, flash ? Color.White : Color.Gold);
        Raylib.DrawSphere(pc + new Vector3(0, 1.1f, 0), 0.42f, flash ? Color.White : Color.Orange);
        // facing marker
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
    foreach (var b in world.EnemyBullets)
        Raylib.DrawSphere(P(b.X, b.Y, 0.9f), 0.2f, Color.Orange);
    foreach (var b in world.Bullets)
        Raylib.DrawSphere(P(b.X, b.Y, 0.9f), 0.22f, Color.SkyBlue);

    Raylib.EndMode3D();
    if (mysteryPos is Vector3 mp)
    {
        var sp = Raylib.GetWorldToScreen(mp, cam);
        Raylib.DrawText("?", (int)sp.X - 8, (int)sp.Y - 14, 32, Color.White);
    }
    for (int i = 0; i < world.Keys.Count; i++)
        Raylib.DrawRectangle(12 + i * 26, 104, 22, 22, AtariPalette.ToColor(world.Keys[i] & 0x0F, (world.Keys[i] >> 4) & 0x0E));
    Raylib.DrawText($"{maze.Name}  room {world.Room}  level {maze.Level(world.Room) + 1}  {maze.Kind(world.Room)}", 12, 10, 24, Color.White);
    Raylib.DrawText($"lives {world.Lives}   score {world.Score}   enemies {world.Enemies.Count}" + (world.Phase == GamePhase.GameOver ? "   GAME OVER (R)" : ""), 12, 40, 24, Color.Yellow);
    if (world.ShadowWarned && !world.ShadowActive && !world.IsPodRoom && (world.TickCount / 20) % 2 == 0)
        Raylib.DrawText("!! SHADOW APPROACHES !!", 470, 40, 28, Color.Red);
    if (world.Phase == GamePhase.Won) Raylib.DrawText("SHADOW DEFEATED", 500, 330, 40, Color.Gold);
    if (world.Cycle > 0) Raylib.DrawText($"loop {world.Cycle + 1}", 12, 130, 18, Color.Gray);
    Raylib.DrawText("WASD move   Space fire   [ ] room   R restart   F5 reload assets/config.json", 12, 72, 18, Color.Gray);
    Raylib.EndDrawing();
}
Raylib.CloseWindow();
