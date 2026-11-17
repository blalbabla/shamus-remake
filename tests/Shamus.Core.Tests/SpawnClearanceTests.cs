using Shamus.Core;
using Shamus.Core.Mazes;
using Shamus.Core.Simulation;
using Xunit;

public class SpawnClearanceTests
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
    public void WallTouchingTheSquareEdgeIsStillRejected()
    {
        var grid = RoomGrid.Build(Maze("c64"), 0);
        grid.SetSolid(20, 11, 21, 12, true);   // one wall cell on the middle of the square's top edge

        // the four corner samples of the old check land outside this cell
        Assert.False(grid.IsSolid(19, 9));
        Assert.False(grid.IsSolid(21, 9));
        Assert.False(grid.IsSolid(19, 11));
        Assert.False(grid.IsSolid(21, 11));

        Assert.False(grid.IsAreaFree(20.5f, 10.5f, 0.7f));
        Assert.True(grid.IsAreaFree(18.5f, 10.5f, 0.7f));
    }

    [Fact]
    public void SpawnedEnemiesNeverOverlapWalls()
    {
        var m = Maze("c64");
        for (int seed = 1; seed <= 20; seed++)
        {
            var world = new GameWorld(m, new GameConfig { Seed = seed }, 0);
            foreach (var e in world.Enemies)
                Assert.True(world.Grid.IsAreaFree(e.X, e.Y, world.Config.EnemyRadius),
                    $"seed {seed}: enemy at {e.X:F2},{e.Y:F2} overlaps a wall");
        }
    }
}
