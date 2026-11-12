using System.Numerics;
using Raylib_cs;
using Shamus.Core.Mazes;

// M0-M2 prototype: top-down 3D room viewer with a movable player. Controls: WASD/arrows move, [ ] switch room.
var mazeName = args.Length > 0 ? args[0] : "atari";
int room = args.Length > 1 ? int.Parse(args[1]) : 0;
var maze = MazeLoader.Load(Path.Combine(AppContext.BaseDirectory, "assets", "mazes", mazeName + ".maze"));

Raylib.InitWindow(1280, 720, "Shamus 3D");
Raylib.SetTargetFPS(60);

var cam = new Camera3D
{
    Position = new Vector3(0, 40, 0.01f),
    Target = Vector3.Zero,
    Up = new Vector3(0, 0, -1),
    FovY = RoomGrid.W * 0.62f, // ortho width-ish
    Projection = CameraProjection.Orthographic,
};

RoomGrid grid = RoomGrid.Build(maze, room);
Vector2 pos = new(3f, 11f); // cell units
const float Radius = 0.45f;

Color[] levelColors = { new(220, 90, 80, 255), new(80, 120, 240, 255), new(80, 200, 100, 255), new(230, 70, 70, 255) };

void SetRoom(int r, Vector2 p) { room = Math.Clamp(r, 0, 127); grid = RoomGrid.Build(maze, room); pos = p; }

bool Blocked(Vector2 p)
{
    for (int dx = -1; dx <= 1; dx += 2)
        for (int dy = -1; dy <= 1; dy += 2)
        {
            int cx = (int)MathF.Floor(p.X + dx * Radius), cy = (int)MathF.Floor(p.Y + dy * Radius);
            if (cx < 0 || cx >= RoomGrid.W) continue; // leaving through a door
            if (grid.IsSolid(cx, cy)) return true;
        }
    return false;
}

while (!Raylib.WindowShouldClose())
{
    float dt = Raylib.GetFrameTime();
    var dir = Vector2.Zero;
    if (Raylib.IsKeyDown(KeyboardKey.W) || Raylib.IsKeyDown(KeyboardKey.Up)) dir.Y -= 1;
    if (Raylib.IsKeyDown(KeyboardKey.S) || Raylib.IsKeyDown(KeyboardKey.Down)) dir.Y += 1;
    if (Raylib.IsKeyDown(KeyboardKey.A) || Raylib.IsKeyDown(KeyboardKey.Left)) dir.X -= 1;
    if (Raylib.IsKeyDown(KeyboardKey.D) || Raylib.IsKeyDown(KeyboardKey.Right)) dir.X += 1;
    if (dir != Vector2.Zero)
    {
        dir = Vector2.Normalize(dir) * 12f * dt;
        var nx = new Vector2(pos.X + dir.X, pos.Y); if (!Blocked(nx)) pos = nx;
        var ny = new Vector2(pos.X, pos.Y + dir.Y); if (!Blocked(ny)) pos = ny;
    }
    if (Raylib.IsKeyPressed(KeyboardKey.RightBracket)) SetRoom(room + 1, new(3f, 11f));
    if (Raylib.IsKeyPressed(KeyboardKey.LeftBracket)) SetRoom(room - 1, new(RoomGrid.W - 3f, 11f));

    // room transitions
    if (pos.X >= RoomGrid.W) SetRoom(room + 1, new(1.5f, pos.Y));
    else if (pos.X < 0) SetRoom(room - 1, new(RoomGrid.W - 1.5f, pos.Y));
    else if (maze.IsCorridor(room) && (pos.Y < 0 || pos.Y >= RoomGrid.H))
    {
        bool fromTop = pos.Y < 0;
        int target = maze.VerticalLink(room);
        SetRoom(target, new(pos.X, fromTop ? RoomGrid.H - 1.5f : 1.5f));
    }

    Raylib.BeginDrawing();
    Raylib.ClearBackground(new Color(8, 8, 14, 255));
    Raylib.BeginMode3D(cam);
    var wall = levelColors[maze.Level(room)];
    float ox = -RoomGrid.W / 2f, oz = -RoomGrid.H / 2f;
    Raylib.DrawCube(new Vector3(0, -0.5f, 0), RoomGrid.W, 1, RoomGrid.H, new Color(18, 18, 28, 255));
    for (int y = 0; y < RoomGrid.H; y++)
    {
        for (int x = 0; x < RoomGrid.W;)
        {
            if (!grid.IsSolid(x, y)) { x++; continue; }
            int x0 = x; while (x < RoomGrid.W && grid.IsSolid(x, y)) x++;
            float w = x - x0;
            var c = new Vector3(ox + x0 + w / 2f, 0.75f, oz + y + 0.5f);
            Raylib.DrawCube(c, w, 1.5f, 1f, wall);
            Raylib.DrawCubeWires(c, w, 1.5f, 1f, Color.Black);
        }
    }
    var pc = new Vector3(ox + pos.X, 0.9f, oz + pos.Y);
    Raylib.DrawCube(pc, 0.9f, 1.8f, 0.9f, Color.Gold);
    Raylib.DrawSphere(pc + new Vector3(0, 1.2f, 0), 0.45f, Color.Orange);
    Raylib.EndMode3D();
    Raylib.DrawText($"{maze.Name}  room {room}  level {maze.Level(room) + 1}  {maze.Kind(room)}", 12, 10, 24, Color.White);
    Raylib.DrawText("WASD move   [ ] prev/next room", 12, 40, 18, Color.Gray);
    Raylib.EndDrawing();
}
Raylib.CloseWindow();
