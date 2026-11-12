using Shamus.Core;
using Shamus.Core.Mazes;
using Shamus.Core.Simulation;
using Xunit;

public class WorldTests
{
    static MazeData Maze()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (!Directory.Exists(Path.Combine(d.FullName, "assets", "mazes"))) d = d.Parent!;
        return MazeLoader.Load(Path.Combine(d.FullName, "assets", "mazes", "c64.maze"));
    }

    [Fact]
    public void BulletStopsAtWall()
    {
        var w = new GameWorld(Maze(), new GameConfig(), 0);
        for (int i = 0; i < 200; i++) w.Tick(new InputState(0, 0, i == 0));
        Assert.Empty(w.Bullets);
    }

    [Fact]
    public void WalkingIntoWallKills()
    {
        var w = new GameWorld(Maze(), new GameConfig { StartLives = 3 }, 0);
        for (int i = 0; i < 300 && w.Lives == 3; i++) w.Tick(new InputState(0, 1, false));
        Assert.Equal(2, w.Lives);
    }

    [Fact]
    public void PodOpensWhenTargetIsHit()
    {
        // Player stands at the entrance and holds fire until a bullet passes both slits and hits the target.
        var w = new GameWorld(Maze(), new GameConfig(), 18);
        Assert.True(w.IsPodRoom);
        bool opened = false;
        for (int i = 0; i < 6000 && !opened; i++)
        {
            w.Tick(new InputState(0, 0, true));
            opened = w.PodOpen;
        }
        Assert.True(opened);
    }

    [Fact]
    public void Deterministic()
    {
        var a = new GameWorld(Maze(), new GameConfig(), 18);
        var b = new GameWorld(Maze(), new GameConfig(), 18);
        for (int i = 0; i < 1000; i++)
        {
            var inp = new InputState(i % 7 == 0 ? 1 : 0, 0, i % 5 == 0);
            a.Tick(inp); b.Tick(inp);
        }
        Assert.Equal(a.PlayerX, b.PlayerX);
        Assert.Equal(a.Lives, b.Lives);
        Assert.Equal(a.PodOpen, b.PodOpen);
    }
}
