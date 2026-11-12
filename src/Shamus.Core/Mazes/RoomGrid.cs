namespace Shamus.Core.Mazes;

/// <summary>
/// Solid/empty cell grid of one room (40 x 22 cells; ~8 original pixels per cell).
/// Chambers: outer frame with left/right doors plus up to 6 interior wall pieces on a '#' grid.
/// Corridors: solid fill with a carved passage (shape from corridor type).
/// </summary>
public sealed class RoomGrid
{
    public const int W = 40, H = 22;
    // '#' grid geometry (cells)
    public static readonly int[] VLineX = { 16, 23 };
    public static readonly int[] HLineY = { 7, 14 };
    public const int DoorY0 = 8, DoorY1 = 14; // door rows [8,14)

    private readonly bool[,] _solid = new bool[W, H];
    public bool IsSolid(int x, int y) => x < 0 || y < 0 || x >= W || y >= H || _solid[x, y];

    public static RoomGrid Build(MazeData m, int room)
    {
        var g = new RoomGrid();
        if (m.IsCorridor(room)) g.BuildCorridor(m.CorridorType(room));
        else g.BuildChamber(m, room);
        return g;
    }

    private void Fill(int x0, int y0, int x1, int y1, bool v = true) // [x0,x1) x [y0,y1)
    {
        for (int x = Math.Max(0, x0); x < Math.Min(W, x1); x++)
            for (int y = Math.Max(0, y0); y < Math.Min(H, y1); y++) _solid[x, y] = v;
    }

    private void BuildChamber(MazeData m, int room)
    {
        // Four solid "wallpaper" corner blocks; the middle band (rows [8,14)) is open to both screen edges.
        const int L = 9, R = 32;
        Fill(0, 0, L, DoorY0); Fill(R, 0, W, DoorY0);
        Fill(0, DoorY1, L, H); Fill(R, DoorY1, W, H);
        Fill(L, 0, R, 1); Fill(L, H - 1, R, H); // top / bottom walls of the central column
        int[][] hx = { new[] { L, 16 }, new[] { 16, 24 }, new[] { 24, R } };
        int[][] vy = { new[] { 1, 8 }, new[] { 8, 15 }, new[] { 15, H - 1 } };
        for (int i = 0; i < 6; i++)
        {
            int row = i / 3, col = i % 3;
            if (m.HWall(room, i)) Fill(hx[col][0], HLineY[row], hx[col][1], HLineY[row] + 1);
            // vertical: bits 0-2 left line top->bottom, bits 3-5 right line
            if (m.VWall(room, i)) Fill(VLineX[i / 3], vy[i % 3][0], VLineX[i / 3] + 1, vy[i % 3][1]);
        }
    }

    private void BuildCorridor(int type)
    {
        Fill(0, 0, W, H);
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
        Fill(17, DoorY0, 24, DoorY1, false);        // centre is always present
        if (l) Fill(0, DoorY0, 17, DoorY1, false);
        if (r) Fill(24, DoorY0, W, DoorY1, false);
        if (d) Fill(17, DoorY1, 24, H, false);
        if (u) Fill(17, 0, 24, DoorY0, false);
    }

    public string Dump()
    {
        var sb = new System.Text.StringBuilder();
        for (int y = 0; y < H; y++) { for (int x = 0; x < W; x++) sb.Append(_solid[x, y] ? '#' : '.'); sb.AppendLine(); }
        return sb.ToString();
    }
}
