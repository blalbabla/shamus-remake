namespace Shamus.Core.Mazes;

/// <summary>An axis-aligned energy wall between two generators, in room cell coordinates.</summary>
public readonly record struct WallSegment(float X0, float Y0, float X1, float Y1)
{
    public bool IsHorizontal => Math.Abs(Y0 - Y1) < 1e-4f;
    public float Length => Math.Abs(X1 - X0) + Math.Abs(Y1 - Y0);
}

/// <summary>Wall beams and generator posts of one room.</summary>
public sealed class WallSet
{
    public List<WallSegment> Segments { get; } = new();
    public List<(float X, float Y)> Posts { get; } = new();
}

/// <summary>
/// Turns the cell grid into vector walls. Every beam runs through the centres of solid cells, so all end points lie on one
/// half-cell grid and each corner gets exactly one generator:
/// thick regions get a line along their boundary cells (convex corners are closed through the corner cell),
/// thin walls get a line along their cells; lines that end next to a thick cell reach that cell's centre.
/// </summary>
public static class RoomWalls
{
    public static WallSet Build(RoomGrid g)
    {
        var set = new WallSet();
        var posts = new HashSet<(int, int)>();
        void AddPost(float x, float y)
        {
            if (posts.Add(((int)MathF.Round(x * 100), (int)MathF.Round(y * 100)))) set.Posts.Add((x, y));
        }
        void Add(float x0, float y0, float x1, float y1)
        {
            set.Segments.Add(new WallSegment(x0, y0, x1, y1));
            AddPost(x0, y0); AddPost(x1, y1);
        }
        bool Thick(int x, int y) => Inside(x, y) && g.KindAt(x, y) == RoomGrid.Thick;
        bool Open(int x, int y) => Inside(x, y) && g.KindAt(x, y) == RoomGrid.Empty;

        // 1. outlines of thick regions: thick cells that touch an open cell form lines through their centres
        for (int x = 0; x < RoomGrid.W; x++)
            for (int y = 0; y < RoomGrid.H;)
            {
                if (!(Thick(x, y) && (Open(x - 1, y) || Open(x + 1, y)))) { y++; continue; }
                int y0 = y; while (y < RoomGrid.H && Thick(x, y) && (Open(x - 1, y) || Open(x + 1, y))) y++;
                int y1 = y - 1;
                if (Thick(x, y0 - 1)) y0--;      // close the corner / reach the neighbouring wall
                if (Thick(x, y1 + 1)) y1++;
                Add(x + 0.5f, y0 + 0.5f, x + 0.5f, y1 + 0.5f);
            }
        for (int y = 0; y < RoomGrid.H; y++)
            for (int x = 0; x < RoomGrid.W;)
            {
                if (!(Thick(x, y) && (Open(x, y - 1) || Open(x, y + 1)))) { x++; continue; }
                int x0 = x; while (x < RoomGrid.W && Thick(x, y) && (Open(x, y - 1) || Open(x, y + 1))) x++;
                int x1 = x - 1;
                if (Thick(x0 - 1, y)) x0--;
                if (Thick(x1 + 1, y)) x1++;
                Add(x0 + 0.5f, y + 0.5f, x1 + 0.5f, y + 0.5f);
            }

        // 2. thin walls: a line through every run of two or more thin cells; lone cells become a generator
        var inRun = new bool[RoomGrid.W, RoomGrid.H];
        for (int y = 0; y < RoomGrid.H; y++)
            for (int x = 0; x < RoomGrid.W;)
            {
                if (g.KindAt(x, y) != RoomGrid.Thin) { x++; continue; }
                int x0 = x; while (x < RoomGrid.W && g.KindAt(x, y) == RoomGrid.Thin) x++;
                int x1 = x - 1;
                if (x1 > x0)
                {
                    int a = x0, b = x1;
                    if (Thick(a - 1, y)) a--;
                    if (Thick(b + 1, y)) b++;
                    Add(a + 0.5f, y + 0.5f, b + 0.5f, y + 0.5f);
                    for (int i = x0; i <= x1; i++) inRun[i, y] = true;
                }
            }
        for (int x = 0; x < RoomGrid.W; x++)
            for (int y = 0; y < RoomGrid.H;)
            {
                if (g.KindAt(x, y) != RoomGrid.Thin) { y++; continue; }
                int y0 = y; while (y < RoomGrid.H && g.KindAt(x, y) == RoomGrid.Thin) y++;
                int y1 = y - 1;
                if (y1 > y0)
                {
                    int a = y0, b = y1;
                    if (Thick(x, a - 1)) a--;
                    if (Thick(x, b + 1)) b++;
                    Add(x + 0.5f, a + 0.5f, x + 0.5f, b + 0.5f);
                    for (int i = y0; i <= y1; i++) inRun[x, i] = true;
                }
            }
        for (int x = 0; x < RoomGrid.W; x++)
            for (int y = 0; y < RoomGrid.H; y++)
                if (g.KindAt(x, y) == RoomGrid.Thin && !inRun[x, y]) AddPost(x + 0.5f, y + 0.5f);
        return set;
    }

    private static bool Inside(int x, int y) => x >= 0 && y >= 0 && x < RoomGrid.W && y < RoomGrid.H;
}
