namespace Shamus.Core.Mazes;

/// <summary>
/// Footprint of the isometric plateau: the area the hero can reach, plus one ring of wall cells so the energy
/// beams and generator posts stand on the slab instead of hanging over the drop. Kept in Core (no Raylib) so the
/// contour can be tested; the renderer only turns the mask and its edges into cubes.
/// </summary>
public static class RoomFloor
{
    /// <summary>An axis-aligned boundary edge on the cell grid; endpoints are grid-line coordinates.</summary>
    public readonly record struct Edge(int X0, int Y0, int X1, int Y1);

    private static readonly int[] Nx = { 1, -1, 0, 0 };
    private static readonly int[] Ny = { 0, 0, 1, -1 };

    /// <summary>
    /// Reachable floor: a 4-neighbour flood fill of <see cref="RoomGrid.Empty"/> cells from the seed (nearest open
    /// cell if the seed itself is solid), grown once into every adjacent non-empty cell (the wall footprint).
    /// Returns an all-false mask when the room has no open cells at all.
    /// </summary>
    public static bool[,] Mask(RoomGrid grid, int seedX, int seedY)
    {
        var mask = new bool[RoomGrid.W, RoomGrid.H];
        var (sx, sy) = NearestOpen(grid, seedX, seedY);
        if (sx < 0) return mask;

        var queue = new Queue<(int X, int Y)>();
        queue.Enqueue((sx, sy));
        mask[sx, sy] = true;
        while (queue.Count > 0)
        {
            var (x, y) = queue.Dequeue();
            for (int i = 0; i < 4; i++)
            {
                int nx = x + Nx[i], ny = y + Ny[i];
                if (nx < 0 || ny < 0 || nx >= RoomGrid.W || ny >= RoomGrid.H) continue;
                if (mask[nx, ny] || grid.KindAt(nx, ny) != RoomGrid.Empty) continue;
                mask[nx, ny] = true;
                queue.Enqueue((nx, ny));
            }
        }

        // One extra ring of solid cells: the beams run through the centres of boundary cells, so without the ring
        // they would stand half a cell outside the floor.
        var grown = (bool[,])mask.Clone();
        for (int y = 0; y < RoomGrid.H; y++)
            for (int x = 0; x < RoomGrid.W; x++)
            {
                if (!mask[x, y]) continue;
                for (int i = 0; i < 4; i++)
                {
                    int nx = x + Nx[i], ny = y + Ny[i];
                    if (nx < 0 || ny < 0 || nx >= RoomGrid.W || ny >= RoomGrid.H) continue;
                    if (grid.KindAt(nx, ny) != RoomGrid.Empty) grown[nx, ny] = true;
                }
            }

        // Generator posts stand on the plateau: make sure their footprint cells belong to the slab even when the
        // post straddles a cell corner that the one-ring growth did not reach.
        const float postHalf = 0.45f;
        foreach (var (px, py) in grid.Walls.Posts)
        {
            int x0 = Math.Max(0, (int)MathF.Floor(px - postHalf));
            int x1 = Math.Min(RoomGrid.W - 1, (int)MathF.Floor(px + postHalf));
            int y0 = Math.Max(0, (int)MathF.Floor(py - postHalf));
            int y1 = Math.Min(RoomGrid.H - 1, (int)MathF.Floor(py + postHalf));
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                    grown[x, y] = true;
        }
        return grown;
    }

    /// <summary>Boundary edges of the mask, in cell coordinates (x to x+1 / y to y+1).</summary>
    public static List<Edge> Boundary(bool[,] mask)
    {
        var edges = new List<Edge>();
        for (int y = 0; y < RoomGrid.H; y++)
            for (int x = 0; x < RoomGrid.W; x++)
            {
                if (!mask[x, y]) continue;
                if (!At(mask, x, y - 1)) edges.Add(new Edge(x, y, x + 1, y));
                if (!At(mask, x, y + 1)) edges.Add(new Edge(x, y + 1, x + 1, y + 1));
                if (!At(mask, x - 1, y)) edges.Add(new Edge(x, y, x, y + 1));
                if (!At(mask, x + 1, y)) edges.Add(new Edge(x + 1, y, x + 1, y + 1));
            }
        return edges;
    }

    /// <summary>
    /// Same boundary, but consecutive collinear cell edges are merged into one long segment. The renderer uses this
    /// so a straight wall is one cube instead of one cube per cell (the per-cell joints show as vertical seams).
    /// </summary>
    public static List<Edge> MergedBoundary(bool[,] mask)
    {
        var all = Boundary(mask);
        var merged = new List<Edge>();

        foreach (var group in all.Where(e => e.Y0 == e.Y1).GroupBy(e => e.Y0))
        {
            var run = group.OrderBy(e => e.X0).ToList();
            int x0 = run[0].X0, x1 = run[0].X1;
            for (int i = 1; i < run.Count; i++)
            {
                if (run[i].X0 <= x1) x1 = Math.Max(x1, run[i].X1);
                else { merged.Add(new Edge(x0, group.Key, x1, group.Key)); x0 = run[i].X0; x1 = run[i].X1; }
            }
            merged.Add(new Edge(x0, group.Key, x1, group.Key));
        }

        foreach (var group in all.Where(e => e.X0 == e.X1).GroupBy(e => e.X0))
        {
            var run = group.OrderBy(e => e.Y0).ToList();
            int y0 = run[0].Y0, y1 = run[0].Y1;
            for (int i = 1; i < run.Count; i++)
            {
                if (run[i].Y0 <= y1) y1 = Math.Max(y1, run[i].Y1);
                else { merged.Add(new Edge(group.Key, y0, group.Key, y1)); y0 = run[i].Y0; y1 = run[i].Y1; }
            }
            merged.Add(new Edge(group.Key, y0, group.Key, y1));
        }

        return merged;
    }

    private static bool At(bool[,] mask, int x, int y) =>
        x >= 0 && y >= 0 && x < RoomGrid.W && y < RoomGrid.H && mask[x, y];

    /// <summary>The seed itself if open, otherwise the closest open cell (Manhattan-ish squared distance); (-1,-1) if none.</summary>
    private static (int X, int Y) NearestOpen(RoomGrid grid, int seedX, int seedY)
    {
        int sx = Math.Clamp(seedX, 0, RoomGrid.W - 1), sy = Math.Clamp(seedY, 0, RoomGrid.H - 1);
        if (grid.KindAt(sx, sy) == RoomGrid.Empty) return (sx, sy);

        int best = int.MaxValue, bx = -1, by = -1;
        for (int y = 0; y < RoomGrid.H; y++)
            for (int x = 0; x < RoomGrid.W; x++)
            {
                if (grid.KindAt(x, y) != RoomGrid.Empty) continue;
                int d = (x - sx) * (x - sx) + (y - sy) * (y - sy);
                if (d < best) { best = d; bx = x; by = y; }
            }
        return (bx, by);
    }
}
