namespace Shamus.Core.Mazes;

/// <summary>Structural and solvability checks for a maze (used by the procedural generator and the tests).</summary>
public static class MazeValidator
{
    private static readonly int[] ObjCellX = { 12, 20, 28 }, ObjCellY = { 4, 11, 18 };

    /// <summary>Rooms reachable from room 0; <paramref name="opened"/> are keyhole rooms whose door has been unlocked.</summary>
    public static HashSet<int> Reachable(MazeData m, ISet<int> opened)
    {
        var seen = new HashSet<int> { 0 };
        var q = new Queue<int>();
        q.Enqueue(0);
        bool Locked(int r, bool left) => m.Object(r) == ObjectKind.Keyhole && !m.IsPod(r) && !opened.Contains(r) && m.DoorOnLeft(r) == left;
        void Visit(int r) { if (seen.Add(r)) q.Enqueue(r); }
        while (q.Count > 0)
        {
            int r = q.Dequeue();
            if (r < MazeData.RoomCount - 1 && m.OpenRight(r) && m.OpenLeft(r + 1) && !Locked(r, false) && !Locked(r + 1, true)) Visit(r + 1);
            if (r > 0 && m.OpenLeft(r) && m.OpenRight(r - 1) && !Locked(r, true) && !Locked(r - 1, false)) Visit(r - 1);
            if (m.IsCorridor(r) && !m.IsPod(r)) Visit(m.VerticalLink(r));
        }
        return seen;
    }

    /// <summary>Can the player reach the last room, collecting keys and opening locks along the way?</summary>
    public static bool IsSolvable(MazeData m)
    {
        var opened = new HashSet<int>();
        var held = new List<int>();
        var taken = new HashSet<int>();
        while (true)
        {
            var reach = Reachable(m, opened);
            bool changed = false;
            foreach (int r in reach.OrderBy(x => x))
            {
                if (m.Object(r) == ObjectKind.Key && taken.Add(r)) { held.Add(m.KeyId(r)); changed = true; }
                if (m.Object(r) == ObjectKind.Keyhole && !m.IsPod(r) && !opened.Contains(r) && held.Remove(m.KeyId(r))) { opened.Add(r); changed = true; }
            }
            if (!changed) return reach.Contains(MazeData.RoomCount - 1);
        }
    }

    /// <summary>The two doors of a room are connected and enough object positions are reachable.</summary>
    public static bool RoomPlayable(MazeData m, int room)
    {
        if (m.IsCorridor(room) && !m.IsPod(room)) return true; // corridor shapes are fixed
        var g = RoomGrid.Build(m, room);
        var seen = new bool[RoomGrid.W, RoomGrid.H];
        var q = new Queue<(int, int)>();
        (int, int) start = m.OpenLeft(room) ? (0, 11) : (RoomGrid.W - 1, 11);
        if (g.IsSolid(start.Item1, start.Item2)) return false;
        seen[start.Item1, start.Item2] = true; q.Enqueue(start);
        int[] dx = { 1, -1, 0, 0 }, dy = { 0, 0, 1, -1 };
        while (q.Count > 0)
        {
            var (x, y) = q.Dequeue();
            for (int k = 0; k < 4; k++)
            {
                int nx = x + dx[k], ny = y + dy[k];
                if (nx < 0 || ny < 0 || nx >= RoomGrid.W || ny >= RoomGrid.H || seen[nx, ny] || g.IsSolid(nx, ny)) continue;
                seen[nx, ny] = true; q.Enqueue((nx, ny));
            }
        }
        if (!m.IsCorridor(room))
        {
            if (!seen[RoomGrid.W - 1, 11] || !seen[0, 11]) return false;
            int free = 0;
            foreach (int cx in ObjCellX) foreach (int cy in ObjCellY)
                if (seen[cx, cy] && !g.IsSolid(cx - 1, cy) && !g.IsSolid(cx + 1, cy) && !g.IsSolid(cx, cy - 1) && !g.IsSolid(cx, cy + 1)) free++;
            if (free < 2) return false;
        }
        return true;
    }

    public static List<string> Validate(MazeData m)
    {
        var e = new List<string>();
        for (int r = 0; r < MazeData.RoomCount; r++)
        {
            if (r < MazeData.RoomCount - 1 && m.OpenRight(r) != m.OpenLeft(r + 1)) e.Add($"exit mismatch between {r} and {r + 1}");
            if (m.IsCorridor(r) && !m.IsPod(r))
            {
                int l = m.VerticalLink(r);
                if (l == r || l >= MazeData.RoomCount || !m.IsCorridor(l) || m.IsPod(l)) e.Add($"room {r}: bad vertical link {l}");
                else
                {
                    if (m.VerticalLink(l) != r) e.Add($"vertical link {r}->{l} not mutual");
                    if (m.VerticalDown(r) == m.VerticalDown(l)) e.Add($"vertical link {r}<->{l}: same direction");
                }
            }
            if (m.IsPod(r) && m.Object(r) == ObjectKind.None) e.Add($"pod room {r} has no target");
            if (m.Object(r) == ObjectKind.Keyhole && (m.IsCorridor(r) || m.IsPod(r))) e.Add($"keyhole {r} outside a chamber");
            if ((m.Object(r) is ObjectKind.Key or ObjectKind.Potion or ObjectKind.Mystery) && m.IsCorridor(r)) e.Add($"object in corridor {r}");
            if (!RoomPlayable(m, r)) e.Add($"room {r} is not playable");
        }
        if (m.Object(MazeData.RoomCount - 1) != ObjectKind.Shadow || !m.IsPod(MazeData.RoomCount - 1)) e.Add("last room must be the Shadow pod");
        var ends = m.LevelEnds;
        if (!(0 < ends[0] && ends[0] < ends[1] && ends[1] < ends[2] && ends[2] < 127)) e.Add("bad level boundaries");
        if (e.Count == 0 && !IsSolvable(m)) e.Add("maze is not solvable");
        return e;
    }
}
