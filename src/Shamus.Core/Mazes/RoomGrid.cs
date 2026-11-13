namespace Shamus.Core.Mazes;

/// <summary>
/// Cell grid of one room (40 x 22 cells; ~8 original pixels per cell).
/// Chambers: four solid corner blocks, a frame in the central column and up to 6 interior wall pieces on a '#' grid.
/// Corridors: solid fill with a carved passage (shape from corridor type).
/// Every solid cell is either <see cref="Thick"/> (a filled region, drawn as an outline) or <see cref="Thin"/>
/// (a one-cell wall, drawn as a single centre line); see <see cref="Walls"/>.
/// </summary>
public sealed class RoomGrid
{
    public const int W = 40, H = 22;
    // '#' grid geometry (cells)
    public static readonly int[] VLineX = { 16, 23 };
    public static readonly int[] HLineY = { 7, 14 };
    public const int DoorY0 = 8, DoorY1 = 14; // door rows [8,14)

    public const byte Empty = 0, Thick = 1, Thin = 2;

    private readonly byte[,] _kind = new byte[W, H];
    private WallSet? _walls;

    public bool IsSolid(int x, int y) => x < 0 || y < 0 || x >= W || y >= H || _kind[x, y] != Empty;

    /// <summary>Cell kind (Empty, Thick, Thin); cells outside the room count as Thick.</summary>
    public byte KindAt(int x, int y) => x < 0 || y < 0 || x >= W || y >= H ? Thick : _kind[x, y];

    /// <summary>Vector description of the visible walls (cached; recomputed after any change to the grid).</summary>
    public WallSet Walls => _walls ??= RoomWalls.Build(this);

    /// <summary>Marks cells [x0,x1) x [y0,y1) as thin solid / empty (used for locked doors and sealed exits).</summary>
    public void SetSolid(int x0, int y0, int x1, int y1, bool solid) => Fill(x0, y0, x1, y1, solid ? Thin : Empty);

    public static RoomGrid Build(MazeData m, int room)
    {
        var g = new RoomGrid();
        if (m.IsCorridor(room)) g.BuildCorridor(m.CorridorType(room));
        else g.BuildChamber(m, room);
        return g;
    }

    private void Fill(int x0, int y0, int x1, int y1, byte kind) // [x0,x1) x [y0,y1)
    {
        _walls = null;
        for (int x = Math.Max(0, x0); x < Math.Min(W, x1); x++)
            for (int y = Math.Max(0, y0); y < Math.Min(H, y1); y++) _kind[x, y] = kind;
    }

    private void BuildChamber(MazeData m, int room)
    {
        // Four solid corner blocks; the middle band (rows [8,14)) is open to both screen edges.
        const int L = 9, R = 32;
        Fill(0, 0, L, DoorY0, Thick); Fill(R, 0, W, DoorY0, Thick);
        Fill(0, DoorY1, L, H, Thick); Fill(R, DoorY1, W, H, Thick);
        Fill(L, 0, R, 1, Thin); Fill(L, H - 1, R, H, Thin); // top / bottom walls of the central column
        int[][] hx = { new[] { L, 16 }, new[] { 16, 24 }, new[] { 24, R } };
        int[][] vy = { new[] { 1, 8 }, new[] { 8, 15 }, new[] { 15, H - 1 } };
        for (int i = 0; i < 6; i++)
        {
            int row = i / 3, col = i % 3;
            if (m.HWall(room, i)) Fill(hx[col][0], HLineY[row], hx[col][1], HLineY[row] + 1, Thin);
            // vertical: bits 0-2 left line top->bottom, bits 3-5 right line
            if (m.VWall(room, i)) Fill(VLineX[i / 3], vy[i % 3][0], VLineX[i / 3] + 1, vy[i % 3][1], Thin);
        }
    }

    private void BuildCorridor(int type)
    {
        Fill(0, 0, W, H, Thick);
        bool d = false, u = false, l = false, r = false;
        switch (type)
        {
            case 0: d = true; break;               // $80 dead end open at bottom
            case 1: d = r = true; break;           // $90 bottom to right
            case 2: d = l = true; break;           // $A0 bottom to left
            case 3: d = l = r = true; break;       // $B0 T with bottom exit
            case 4: u = true; break;               // $C0 dead end open at top
            case 5: u = r = true; break;           // $D0 top to right
            case 6: u = l = true; break;           // $E0 top to left
            default: u = l = r = true; break;      // $F0 T with top exit
        }
        Fill(17, DoorY0, 24, DoorY1, Empty);        // centre is always present
        if (l) Fill(0, DoorY0, 17, DoorY1, Empty);
        if (r) Fill(24, DoorY0, W, DoorY1, Empty);
        if (d) Fill(17, DoorY1, 24, H, Empty);
        if (u) Fill(17, 0, 24, DoorY0, Empty);
    }

    public string Dump()
    {
        var sb = new System.Text.StringBuilder();
        for (int y = 0; y < H; y++) { for (int x = 0; x < W; x++) sb.Append(_kind[x, y] == Empty ? '.' : _kind[x, y] == Thick ? '#' : '+'); sb.AppendLine(); }
        return sb.ToString();
    }
}
