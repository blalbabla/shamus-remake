// Compares RoomGrid (built from maze data) with the reference screenshots. Usage: RoomCompare <maze> <imagesDir> [room]
using Raylib_cs;
using Shamus.Core.Mazes;

var maze = MazeLoader.Load(args[0]);
string dir = args[1];
int only = args.Length > 2 ? int.Parse(args[2]) : -1;
var res = new List<(int room, double diff)>();
for (int r = 0; r < 128; r++)
{
    if (only >= 0 && r != only) continue;
    var path = Path.Combine(dir, $"room{r}.png");
    if (!File.Exists(path)) continue;
    var img = Raylib.LoadImage(path);
    // play area: first 176 px rows; cell = 8x8 px
    var mask = new bool[RoomGrid.W, RoomGrid.H];
    for (int cx = 0; cx < RoomGrid.W; cx++)
        for (int cy = 0; cy < RoomGrid.H; cy++)
        {
            int n = 0;
            for (int px = 0; px < 8; px++) for (int py = 0; py < 8; py++)
            {
                var c = Raylib.GetImageColor(img, cx * 8 + px, cy * 8 + py);
                if (c.R + c.G + c.B > 60) n++;
            }
            mask[cx, cy] = n >= 14;
        }
    Raylib.UnloadImage(img);
    var g = RoomGrid.Build(maze, r);
    int d = 0;
    for (int x = 0; x < RoomGrid.W; x++) for (int y = 0; y < RoomGrid.H; y++) if (mask[x, y] != g.IsSolid(x, y)) d++;
    res.Add((r, d / (double)(RoomGrid.W * RoomGrid.H)));
    if (only >= 0)
    {
        for (int y = 0; y < RoomGrid.H; y++)
        {
            for (int x = 0; x < RoomGrid.W; x++) Console.Write(mask[x, y] ? '#' : '.');
            Console.Write("   ");
            for (int x = 0; x < RoomGrid.W; x++) Console.Write(g.IsSolid(x, y) ? '#' : '.');
            Console.WriteLine();
        }
    }
}
Console.WriteLine($"rooms {res.Count}, mean diff {res.Average(t => t.diff):P1}");
foreach (var t in res.OrderByDescending(t => t.diff).Take(12)) Console.WriteLine($"room {t.room}: {t.diff:P1} kind={maze.Kind(t.room)} h={maze.Horizontal(t.room):X2} v={maze.Vertical(t.room):X2}");
