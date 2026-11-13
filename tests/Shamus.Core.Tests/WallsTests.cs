using Shamus.Core.Mazes;
using Xunit;

public class WallsTests
{
    static string Assets
    {
        get
        {
            var d = new DirectoryInfo(AppContext.BaseDirectory);
            while (!Directory.Exists(Path.Combine(d.FullName, "assets", "mazes"))) d = d.Parent!;
            return Path.Combine(d.FullName, "assets", "mazes");
        }
    }

    static IEnumerable<MazeData> AllMazes()
    {
        foreach (var n in new[] { "atari", "c64", "holmes", "cluseau", "marlowe", "bond" }) yield return MazeLoader.Load(Path.Combine(Assets, n + ".maze"));
        for (int seed = 1; seed <= 20; seed++) yield return ProceduralMaze.Generate(seed);
    }

    // true if some segment or post lies within <tol> of the point
    static bool Covered(WallSet w, float px, float py, float tol)
    {
        foreach (var (x, y) in w.Posts) if (MathF.Abs(x - px) <= tol && MathF.Abs(y - py) <= tol) return true;
        foreach (var s in w.Segments)
        {
            float minX = MathF.Min(s.X0, s.X1) - tol, maxX = MathF.Max(s.X0, s.X1) + tol, minY = MathF.Min(s.Y0, s.Y1) - tol, maxY = MathF.Max(s.Y0, s.Y1) + tol;
            if (px >= minX && px <= maxX && py >= minY && py <= maxY) return true;
        }
        return false;
    }

    [Fact]
    public void SegmentsAreAxisAlignedAndInsideTheRoom()
    {
        foreach (var m in AllMazes())
            for (int r = 0; r < 128; r++)
            {
                var w = RoomGrid.Build(m, r).Walls;
                foreach (var s in w.Segments)
                {
                    Assert.True(s.X0 == s.X1 || s.Y0 == s.Y1, $"room {r}: diagonal segment");
                    Assert.InRange(s.X0, 0f, RoomGrid.W); Assert.InRange(s.X1, 0f, RoomGrid.W);
                    Assert.InRange(s.Y0, 0f, RoomGrid.H); Assert.InRange(s.Y1, 0f, RoomGrid.H);
                }
            }
    }

    [Fact]
    public void EveryLethalBoundaryHasABeamNearby()
    {
        foreach (var m in AllMazes())
            for (int r = 0; r < 128; r++)
            {
                var g = RoomGrid.Build(m, r);
                var w = g.Walls;
                for (int x = 0; x < RoomGrid.W; x++)
                    for (int y = 0; y < RoomGrid.H; y++)
                    {
                        if (!g.IsSolid(x, y)) continue;
                        foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                        {
                            int nx = x + dx, ny = y + dy;
                            if (nx < 0 || ny < 0 || nx >= RoomGrid.W || ny >= RoomGrid.H || g.IsSolid(nx, ny)) continue;
                            float mx = x + 0.5f + dx * 0.5f, my = y + 0.5f + dy * 0.5f;     // middle of the shared edge
                            Assert.True(Covered(w, mx, my, 0.75f), $"room {r}: no beam near edge ({mx},{my})");
                        }
                    }
            }
    }

    [Fact]
    public void Room0HasSealedLeftDoorGeneratorsAndFrame()
    {
        var m = MazeLoader.Load(Path.Combine(Assets, "c64.maze"));
        var g = RoomGrid.Build(m, 0);
        g.SetSolid(0, RoomGrid.DoorY0, 1, RoomGrid.DoorY1, true);   // what GameWorld does for room 0
        var w = g.Walls;
        Assert.Contains(w.Segments, s => s.X0 == 0.5f && s.X1 == 0.5f && s.Y0 <= 8.1f && s.Y1 >= 13.4f);   // the sealed door beam
        Assert.Contains(w.Posts, p => MathF.Abs(p.X - 8.5f) < 0.01f && MathF.Abs(p.Y - 0.5f) < 0.01f);       // corner of the frame
        Assert.True(w.Posts.Count >= 10);
    }

    [Fact]
    public void PodRoomWithoutInteriorWallsHasFrameAndFourBlockOutlines()
    {
        var m = MazeLoader.Load(Path.Combine(Assets, "c64.maze"));
        var w = RoomGrid.Build(m, 18).Walls;   // pod room: h = v = 0
        // top and bottom wall beams between x = 9 and x = 32
        Assert.Contains(w.Segments, s => s.IsHorizontal && s.Y0 == 0.5f && s.X0 == 8.5f && s.X1 == 32.5f);
        Assert.Contains(w.Segments, s => s.IsHorizontal && s.Y0 == 21.5f && s.X0 == 8.5f && s.X1 == 32.5f);
        // inner edges of the four corner blocks
        Assert.Equal(4, w.Segments.Count(s => !s.IsHorizontal && (s.X0 == 8.5f || s.X0 == 32.5f)));
    }

    [Fact]
    public void EachCornerHasExactlyOneGenerator()
    {
        foreach (var m in AllMazes())
            for (int r = 0; r < 128; r++)
            {
                var posts = RoomGrid.Build(m, r).Walls.Posts;
                for (int i = 0; i < posts.Count; i++)
                    for (int j = i + 1; j < posts.Count; j++)
                    {
                        float dx = posts[i].X - posts[j].X, dy = posts[i].Y - posts[j].Y;
                        Assert.True(dx * dx + dy * dy >= 0.99f, $"room {r}: generators too close at ({posts[i].X},{posts[i].Y}) and ({posts[j].X},{posts[j].Y})");
                    }
            }
    }

    [Fact]
    public void OpeningADoorRemovesItsBeam()
    {
        var m = MazeLoader.Load(Path.Combine(Assets, "c64.maze"));
        var g = RoomGrid.Build(m, 18);
        g.SetSolid(39, RoomGrid.DoorY0, 40, RoomGrid.DoorY1, true);
        Assert.Contains(g.Walls.Segments, s => s.X0 == 39.5f && s.X1 == 39.5f);
        g.SetSolid(39, RoomGrid.DoorY0, 40, RoomGrid.DoorY1, false);
        Assert.DoesNotContain(g.Walls.Segments, s => s.X0 == 39.5f && s.X1 == 39.5f);
    }
}
