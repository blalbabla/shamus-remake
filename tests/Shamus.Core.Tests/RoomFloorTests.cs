using Shamus.Core.Mazes;
using Xunit;

public class RoomFloorTests
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

    static MazeData Maze(string name) => MazeLoader.Load(Path.Combine(Assets, name + ".maze"));

    [Fact]
    public void ChamberMaskIsAThickPlus()
    {
        var grid = RoomGrid.Build(Maze("c64"), 0);
        var mask = RoomFloor.Mask(grid, 20, 11);

        Assert.True(mask[20, 11], "room centre");
        Assert.True(mask[20, 18], "vertical arm");
        Assert.True(mask[35, 10], "horizontal arm");
        Assert.False(mask[35, 18], "bottom-right corner block is a drop, not floor");
        Assert.False(mask[3, 3], "top-left corner block is a drop");
    }

    [Fact]
    public void WallRingIsIncludedSoBeamsStandOnTheFloor()
    {
        var grid = RoomGrid.Build(Maze("c64"), 0);
        var mask = RoomFloor.Mask(grid, 20, 11);
        Assert.True(mask[32, 14], "boundary thick cell under the beams belongs to the slab");
    }

    [Fact]
    public void CorridorMaskFollowsThePassage()
    {
        var m = Maze("c64");
        Assert.True(m.IsCorridor(1));
        Assert.Equal(3, m.CorridorType(1));   // T with the bottom exit
        var grid = RoomGrid.Build(m, 1);
        var mask = RoomFloor.Mask(grid, 20, 11);

        Assert.True(mask[20, 11], "centre of the passage");
        Assert.True(mask[20, 20], "bottom branch of corridor type 3");
        Assert.False(mask[2, 2], "solid fill outside the passage");
    }

    [Fact]
    public void SolidSeedFallsBackToTheNearestOpenCell()
    {
        var grid = RoomGrid.Build(Maze("c64"), 0);
        var fromSolid = RoomFloor.Mask(grid, 35, 18);   // inside the bottom-right block
        Assert.True(fromSolid[20, 11]);
        Assert.Equal(RoomFloor.Mask(grid, 20, 11).Length, fromSolid.Length);
    }

    [Fact]
    public void FullySolidRoomHasNoPlateau()
    {
        var grid = RoomGrid.Build(Maze("c64"), 0);
        grid.SetSolid(0, 0, RoomGrid.W, RoomGrid.H, true);
        var mask = RoomFloor.Mask(grid, 20, 11);
        Assert.DoesNotContain(true, Flatten(mask));
        Assert.Empty(RoomFloor.Boundary(mask));
    }

    [Fact]
    public void BoundaryIsClosedAxisAlignedAndHasNoOpenEnds()
    {
        foreach (var name in new[] { "atari", "c64", "holmes", "cluseau", "marlowe", "bond" })
        {
            var m = Maze(name);
            for (int room = 0; room < MazeData.RoomCount; room++)
            {
                var mask = RoomFloor.Mask(RoomGrid.Build(m, room), 20, 11);
                var edges = RoomFloor.Boundary(mask);
                var degree = new Dictionary<(int X, int Y), int>();
                foreach (var e in edges)
                {
                    Assert.True(e.X0 == e.X1 || e.Y0 == e.Y1, $"{name} room {room}: diagonal edge");
                    Assert.NotEqual((e.X0, e.Y0), (e.X1, e.Y1));
                    foreach (var v in new[] { (e.X0, e.Y0), (e.X1, e.Y1) })
                        degree[v] = degree.GetValueOrDefault(v) + 1;
                }
                foreach (var (v, d) in degree)
                {
                    Assert.True(d % 2 == 0, $"{name} room {room}: open contour end at {v}");
                    Assert.True(d >= 2, $"{name} room {room}: dangling edge at {v}");
                }
            }
        }
    }

    [Fact]
    public void MergedBoundaryKeepsTheSameContourLength()
    {
        var m = Maze("c64");
        foreach (var room in new[] { 0, 1, 3 })
        {
            var mask = RoomFloor.Mask(RoomGrid.Build(m, room), 20, 11);
            var raw = RoomFloor.Boundary(mask);
            var merged = RoomFloor.MergedBoundary(mask);
            static int Len(RoomFloor.Edge e) => Math.Abs(e.X1 - e.X0) + Math.Abs(e.Y1 - e.Y0);
            Assert.Equal(raw.Sum(Len), merged.Sum(Len));
            foreach (var e in merged) Assert.True(e.X0 == e.X1 || e.Y0 == e.Y1);
        }
    }

    [Fact]
    public void EveryPostStandsOnThePlateau()
    {
        var m = Maze("c64");
        foreach (var room in new[] { 0, 1, 3 })
        {
            var grid = RoomGrid.Build(m, room);
            var mask = RoomFloor.Mask(grid, 20, 11);
            foreach (var (px, py) in grid.Walls.Posts)
            {
                int x = Math.Clamp((int)MathF.Floor(px), 0, RoomGrid.W - 1);
                int y = Math.Clamp((int)MathF.Floor(py), 0, RoomGrid.H - 1);
                Assert.True(mask[x, y], $"room {room}: post {px},{py} hangs over the drop");
            }
        }
    }

    static IEnumerable<bool> Flatten(bool[,] mask)
    {
        foreach (var b in mask) yield return b;
    }
}
