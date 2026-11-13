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
/// Turns the cell grid into vector walls: filled (thick) regions get an outline along their boundary with empty space,
/// one-cell (thin) walls get a single centre line; generators stand at every end point.
/// </summary>
public static class RoomWalls
{
    public static WallSet Build(RoomGrid g)
    {
        var set = new WallSet();
        var posts = new HashSet<(int, int)>();
        void Add(float x0, float y0, float x1, float y1)
        {
            x0 = Clamp(x0, RoomGrid.W); x1 = Clamp(x1, RoomGrid.W); y0 = Clamp(y0, RoomGrid.H); y1 = Clamp(y1, RoomGrid.H);
            set.Segments.Add(new WallSegment(x0, y0, x1, y1));
            AddPost(x0, y0); AddPost(x1, y1);
        }
        void AddPost(float x, float y)
        {
            if (posts.Add(((int)MathF.Round(x * 100), (int)MathF.Round(y * 100)))) set.Posts.Add((x, y));
        }

        // 1. outlines of thick regions: boundary between a thick cell and an empty cell inside the room
        var vEdges = new SortedSet<(int x, int y)>();   // vertical unit edge at x, from y to y+1
        var hEdges = new SortedSet<(int y, int x)>();   // horizontal unit edge at y, from x to x+1
        for (int x = 0; x < RoomGrid.W; x++)
            for (int y = 0; y < RoomGrid.H; y++)
            {
                if (g.KindAt(x, y) != RoomGrid.Thick) continue;
                if (Inside(x - 1, y) && g.KindAt(x - 1, y) == RoomGrid.Empty) vEdges.Add((x, y));
                if (Inside(x + 1, y) && g.KindAt(x + 1, y) == RoomGrid.Empty) vEdges.Add((x + 1, y));
                if (Inside(x, y - 1) && g.KindAt(x, y - 1) == RoomGrid.Empty) hEdges.Add((y, x));
                if (Inside(x, y + 1) && g.KindAt(x, y + 1) == RoomGrid.Empty) hEdges.Add((y + 1, x));
            }
        foreach (var run in Runs(vEdges.Select(e => (e.x, e.y)))) Add(run.line, run.start, run.line, run.end);
        foreach (var run in Runs(hEdges.Select(e => (e.y, e.x)))) Add(run.start, run.line, run.end, run.line);

        // 2. centre lines of thin walls (runs of two or more cells); lone cells become a generator
        var inRun = new bool[RoomGrid.W, RoomGrid.H];
        for (int y = 0; y < RoomGrid.H; y++)
            for (int x = 0; x < RoomGrid.W;)
            {
                if (g.KindAt(x, y) != RoomGrid.Thin) { x++; continue; }
                int x0 = x; while (x < RoomGrid.W && g.KindAt(x, y) == RoomGrid.Thin) x++;
                int x1 = x - 1;
                if (x1 > x0)
                {
                    float a = x0 + 0.5f, b = x1 + 0.5f;
                    if (g.KindAt(x0 - 1, y) == RoomGrid.Thick && x0 > 0) a = x0;       // reach the outline of a neighbouring block
                    if (g.KindAt(x1 + 1, y) == RoomGrid.Thick && x1 + 1 < RoomGrid.W) b = x1 + 1;
                    Add(a, y + 0.5f, b, y + 0.5f);
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
                    float a = y0 + 0.5f, b = y1 + 0.5f;
                    if (g.KindAt(x, y0 - 1) == RoomGrid.Thick && y0 > 0) a = y0;
                    if (g.KindAt(x, y1 + 1) == RoomGrid.Thick && y1 + 1 < RoomGrid.H) b = y1 + 1;
                    Add(x + 0.5f, a, x + 0.5f, b);
                    for (int i = y0; i <= y1; i++) inRun[x, i] = true;
                }
            }
        for (int x = 0; x < RoomGrid.W; x++)
            for (int y = 0; y < RoomGrid.H; y++)
                if (g.KindAt(x, y) == RoomGrid.Thin && !inRun[x, y]) AddPost(x + 0.5f, y + 0.5f);
        return set;
    }

    private static bool Inside(int x, int y) => x >= 0 && y >= 0 && x < RoomGrid.W && y < RoomGrid.H;

    /// <summary>Merges unit edges (line, pos) into maximal runs along each line.</summary>
    private static IEnumerable<(int line, int start, int end)> Runs(IEnumerable<(int line, int pos)> edges)
    {
        int line = -1, start = 0, prev = 0;
        bool open = false;
        foreach (var (l, p) in edges)
        {
            if (open && l == line && p == prev + 1) { prev = p; continue; }
            if (open) yield return (line, start, prev + 1);
            line = l; start = p; prev = p; open = true;
        }
        if (open) yield return (line, start, prev + 1);
    }

    /// <summary>Pulls coordinates lying on the room border half a cell inwards, onto the centre line of the border cells.</summary>
    private static float Clamp(float v, int size) => v <= 0f ? 0.5f : v >= size ? size - 0.5f : v;
}
