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

public class EnemyTests
{
    static MazeData Maze()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (!Directory.Exists(Path.Combine(d.FullName, "assets", "mazes"))) d = d.Parent!;
        return MazeLoader.Load(Path.Combine(d.FullName, "assets", "mazes", "c64.maze"));
    }

    [Fact]
    public void ChamberSpawnsEnemiesAwayFromPlayer()
    {
        var w = new GameWorld(Maze(), new GameConfig(), 0);
        Assert.NotEmpty(w.Enemies);
        Assert.All(w.Enemies, e => Assert.True(Math.Abs(e.X - w.PlayerX) + Math.Abs(e.Y - w.PlayerY) >= 10));
    }

    [Fact]
    public void PodRoomHasNoEnemies()
    {
        var w = new GameWorld(Maze(), new GameConfig(), 18);
        Assert.Empty(w.Enemies);
    }

    [Fact]
    public void SimulationWithEnemiesIsDeterministic()
    {
        var a = new GameWorld(Maze(), new GameConfig { Seed = 5 }, 3);
        var b = new GameWorld(Maze(), new GameConfig { Seed = 5 }, 3);
        for (int i = 0; i < 2000; i++)
        {
            var inp = new InputState(i % 90 < 45 ? 1 : 0, 0, i % 5 == 0);
            a.Tick(inp); b.Tick(inp);
        }
        Assert.Equal(a.Score, b.Score);
        Assert.Equal(a.Lives, b.Lives);
        Assert.Equal(a.Enemies.Count, b.Enemies.Count);
    }
}

public class BulletClashTests
{
    [Fact]
    public void PlayerBulletCancelsEnemyBullet()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (!Directory.Exists(Path.Combine(d.FullName, "assets", "mazes"))) d = d.Parent!;
        var m = MazeLoader.Load(Path.Combine(d.FullName, "assets", "mazes", "c64.maze"));
        var w = new GameWorld(m, new GameConfig(), 3);
        w.Enemies.Clear();
        // enemy bullet flying toward the player along the door band
        w.EnemyBullets.Add(new Bullet { X = 12f, Y = w.PlayerY, DX = -0.28f, DY = 0 });
        bool cancelled = false;
        for (int i = 0; i < 60 && !cancelled; i++)
        {
            w.Tick(new InputState(0, 0, i == 0));
            cancelled = w.Events.Any(e => e.Kind == WorldEventKind.BulletsCancelled);
        }
        Assert.True(cancelled);
        Assert.Empty(w.EnemyBullets);
    }
}

public class ObjectTests
{
    static MazeData Maze(string n = "c64")
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (!Directory.Exists(Path.Combine(d.FullName, "assets", "mazes"))) d = d.Parent!;
        return MazeLoader.Load(Path.Combine(d.FullName, "assets", "mazes", n + ".maze"));
    }

    [Theory]
    [InlineData("c64")] [InlineData("holmes")] [InlineData("cluseau")] [InlineData("marlowe")] [InlineData("bond")]
    public void EveryKeyholeHasAMatchingKey(string name)
    {
        var m = Maze(name);
        var keys = Enumerable.Range(0, 128).Where(r => m.Object(r) == ObjectKind.Key).Select(m.KeyId).ToList();
        // Source data quirk: Cluseau room 117 has a light-red keyhole with no matching key anywhere.
        var orphans = Enumerable.Range(0, 128).Where(r => m.Object(r) == ObjectKind.Keyhole && !keys.Contains(m.KeyId(r))).ToList();
        Assert.True(orphans.Count == 0 || (name == "cluseau" && orphans.SequenceEqual(new[] { 117 })), string.Join(",", orphans));
    }

    [Fact]
    public void KeyOpensDoor()
    {
        var m = Maze();
        int room = Enumerable.Range(0, 128).First(r => m.Object(r) == ObjectKind.Keyhole && !m.IsCorridor(r));
        var w = new GameWorld(m, new GameConfig(), room);
        Assert.True(w.DoorClosed);
        w.Keys.Add(m.KeyId(room));
        Assert.NotNull(w.Obj);
        w.DebugTeleport(w.Obj!.X, w.Obj.Y);
        w.Tick(new InputState(0, 0, false));
        Assert.False(w.DoorClosed);
        Assert.Empty(w.Keys);
    }

    [Fact]
    public void WithoutKeyDoorStaysClosed()
    {
        var m = Maze();
        int room = Enumerable.Range(0, 128).First(r => m.Object(r) == ObjectKind.Keyhole && !m.IsCorridor(r));
        var w = new GameWorld(m, new GameConfig(), room);
        w.DebugTeleport(w.Obj!.X, w.Obj.Y);
        w.Tick(new InputState(0, 0, false));
        Assert.True(w.DoorClosed);
    }

    [Fact]
    public void PotionGivesLife()
    {
        var m = Maze();
        int room = Enumerable.Range(0, 128).First(r => m.Object(r) == ObjectKind.Potion && !m.IsCorridor(r));
        var w = new GameWorld(m, new GameConfig(), room);
        int lives = w.Lives;
        w.DebugTeleport(w.Obj!.X, w.Obj.Y);
        w.Tick(new InputState(0, 0, false));
        Assert.Equal(lives + 1, w.Lives);
        Assert.Null(w.Obj);
    }
}

public class RespawnTests
{
    [Fact]
    public void DeathRespawnsFreshEnemies()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (!Directory.Exists(Path.Combine(d.FullName, "assets", "mazes"))) d = d.Parent!;
        var m = MazeLoader.Load(Path.Combine(d.FullName, "assets", "mazes", "c64.maze"));
        var w = new GameWorld(m, new GameConfig { StartLives = 3 }, 0);
        var before = w.Enemies.ToList();
        for (int i = 0; i < 300 && w.Phase != GamePhase.Dying; i++) w.Tick(new InputState(0, 1, false)); // walk into a wall
        Assert.Equal(GamePhase.Dying, w.Phase);
        for (int i = 0; i < 100 && w.Phase == GamePhase.Dying; i++) w.Tick(new InputState(0, 0, false));
        Assert.Equal(GamePhase.Playing, w.Phase);
        Assert.NotEmpty(w.Enemies);
        Assert.All(w.Enemies, e => Assert.DoesNotContain(e, before));
    }
}
