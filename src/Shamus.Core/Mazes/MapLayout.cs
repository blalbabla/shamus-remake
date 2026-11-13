namespace Shamus.Core.Mazes;

/// <summary>One level's panel of the maze map: a grid of text tokens (room number, "---", "|", "..", "toN").</summary>
public sealed class LevelMap
{
    public int Index { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public string[,] Cells { get; init; } = new string[0, 0];
    public Dictionary<int, (int X, int Y)> Rooms { get; } = new();

    public static bool IsRoomToken(string t) => t.Length == 3 && char.IsDigit(t[0]);

    /// <summary>Nearest room token left/right (dx) or up/down (dy) from a cell, or -1.</summary>
    public int Neighbour(int x, int y, int dx, int dy)
    {
        for (int cx = x + dx, cy = y + dy; cx >= 0 && cy >= 0 && cx < Width && cy < Height; cx += dx, cy += dy)
        {
            var t = Cells[cx, cy];
            if (IsRoomToken(t)) return int.Parse(t);
            if (t == "..") return -1;
            if (dx != 0 && t != "---" && !t.StartsWith("to")) return -1;
            if (dy != 0 && t != "|") return -1;
        }
        return -1;
    }
}

/// <summary>Per-level schematic of the maze, as in shamus_map_simplified.gif. Loaded from assets/maps/NAME.map or generated.</summary>
public sealed class MapLayout
{
    public IReadOnlyList<LevelMap> Levels { get; }
    private readonly Dictionary<int, int> _levelOfRoom = new();

    private MapLayout(List<LevelMap> levels)
    {
        Levels = levels;
        foreach (var l in levels) foreach (var r in l.Rooms.Keys) _levelOfRoom[r] = l.Index;
    }

    public int LevelOf(int room) => _levelOfRoom.TryGetValue(room, out var l) ? l : -1;

    public static MapLayout Parse(string text)
    {
        var blocks = new List<List<string[]>>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            if (line.StartsWith("level", StringComparison.OrdinalIgnoreCase)) { blocks.Add(new()); continue; }
            blocks[^1].Add(line.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }
        return FromBlocks(blocks);
    }

    private static MapLayout FromBlocks(List<List<string[]>> blocks)
    {
        var levels = new List<LevelMap>();
        for (int i = 0; i < blocks.Count; i++)
        {
            int w = blocks[i].Max(r => r.Length), h = blocks[i].Count;
            var cells = new string[w, h];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) cells[x, y] = x < blocks[i][y].Length ? blocks[i][y][x] : "..";
            var lm = new LevelMap { Index = i, Width = w, Height = h, Cells = cells };
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
                if (LevelMap.IsRoomToken(cells[x, y])) lm.Rooms[int.Parse(cells[x, y])] = (x, y);
            levels.Add(lm);
        }
        return new MapLayout(levels);
    }

    /// <summary>Fallback for mazes without a hand-made map: every run of consecutive connected rooms becomes one row.</summary>
    public static MapLayout Auto(MazeData m)
    {
        var blocks = new List<List<string[]>>();
        for (int level = 0; level < 4; level++)
        {
            var rows = new List<string[]>();
            var run = new List<string>();
            for (int r = 0; r < MazeData.RoomCount; r++)
            {
                if (m.Level(r) != level) continue;
                bool continues = run.Count > 0 && r > 0 && m.Level(r - 1) == level && m.OpenRight(r - 1) && m.OpenLeft(r);
                if (!continues && run.Count > 0) { rows.Add(run.ToArray()); run.Clear(); }
                run.Add($"{r:D3}");
            }
            if (run.Count > 0) rows.Add(run.ToArray());
            blocks.Add(rows);
        }
        return FromBlocks(blocks);
    }

    /// <summary>Loads assets/maps/NAME.map from the given directory, falling back to <see cref="Auto"/>.</summary>
    public static MapLayout Load(string mapsDir, MazeData maze)
    {
        var path = Path.Combine(mapsDir, maze.Name + ".map");
        return File.Exists(path) ? Parse(File.ReadAllText(path)) : Auto(maze);
    }
}
