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

    [Fact]
    public void ShotLeavesTheMuzzle()
    {
        var w = new GameWorld(Maze(), new GameConfig(), 3);   // chamber, facing +x
        w.Enemies.Clear();
        float x0 = w.PlayerX;

        w.Tick(new InputState(0, 0, true));
        var b = Assert.Single(w.Bullets);
        Assert.True(b.X > x0 + 0.7f, $"the shell must start at the barrel tip, got x={b.X} for player x={x0}");
        Assert.Equal(w.PlayerY, b.Y, 3);
    }

    [Fact]
    public void FirstPressTurnsInPlaceThenDrives()
    {
        var w = new GameWorld(Maze(), new GameConfig(), 3);   // a chamber: the middle band is free to drive along
        w.Enemies.Clear();
        float x0 = w.PlayerX, y0 = w.PlayerY;
        Assert.Equal(90f, w.Heading, 2);                      // starts facing +x

        w.Tick(new InputState(0, 1, false));                  // new direction: a turn, not a step
        Assert.True(w.Turning);
        Assert.Equal(x0, w.PlayerX);
        Assert.Equal(y0, w.PlayerY);

        for (int i = 0; i < 30 && w.Turning; i++) w.Tick(new InputState(0, 1, false));
        Assert.False(w.Turning);
        Assert.Equal(0f, w.Heading, 2);                       // now aimed at +y (yaw 0) instead of +x (yaw 90)

        float yAimed = w.PlayerY;
        w.Tick(new InputState(0, 1, false));                  // held: it drives
        Assert.True(w.PlayerY > yAimed);
    }

    [Fact]
    public void HoldingTheSameDirectionDrivesAtOnce()
    {
        var w = new GameWorld(Maze(), new GameConfig(), 3);
        w.Enemies.Clear();
        float x0 = w.PlayerX;
        w.Tick(new InputState(1, 0, false));                  // already facing +x
        Assert.False(w.Turning);
        Assert.True(w.PlayerX > x0);
    }

    [Fact]
    public void TappingANewDirectionOnlyTurns()
    {
        var w = new GameWorld(Maze(), new GameConfig(), 3);
        w.Enemies.Clear();
        float x0 = w.PlayerX, y0 = w.PlayerY;

        w.Tick(new InputState(-1, 0, false));                 // one tap to the left
        Assert.True(w.Turning);
        for (int i = 0; i < 15; i++) w.Tick(new InputState(0, 0, false));   // key released
        Assert.False(w.Turning);
        Assert.Equal(-90f, w.Heading, 2);
        Assert.Equal(x0, w.PlayerX);                          // it turned, it did not move
        Assert.Equal(y0, w.PlayerY);
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
        Assert.All(w.Enemies, e => Assert.True(Math.Abs(e.X - w.PlayerX) + Math.Abs(e.Y - w.PlayerY) >= 6));
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

    [Fact]
    public void DroidTurnsSmoothlyInsteadOfSnapping()
    {
        var w = new GameWorld(Maze(), new GameConfig(), 3);
        var droid = w.Enemies.FirstOrDefault(e => e.Kind == EnemyKind.RoboDroid);
        Assert.NotNull(droid);

        droid!.Heading = droid.PrevHeading = 90f;   // facing +x
        for (int i = 0; i < 14; i++)
        {
            droid.DirX = 0; droid.DirY = -1;        // wants -y, i.e. heading 180
            droid.ModeLeft = 1000;                  // and must not re-decide under us
            w.Tick(new InputState(0, 0, false));
            if (i == 0)
                Assert.True(droid.Heading > 90f && droid.Heading < 180f,
                    $"the hull should ease into the turn, got {droid.Heading}");
        }
        Assert.Equal(180f, droid.Heading, 1);        // and it does get there
    }

    [Fact]
    public void EnemyColoursAreRolledAndDeterministic()
    {
        var a = new GameWorld(Maze(), new GameConfig { Seed = 5 }, 3);
        var b = new GameWorld(Maze(), new GameConfig { Seed = 5 }, 3);
        Assert.Equal(a.Enemies.Select(e => e.ColorIndex), b.Enemies.Select(e => e.ColorIndex));

        var seen = new HashSet<int>();
        for (int seed = 1; seed <= 8; seed++)
        {
            var w = new GameWorld(Maze(), new GameConfig { Seed = seed }, 50);   // level 2: up to 7 enemies
            Assert.NotEmpty(w.Enemies);
            foreach (var e in w.Enemies)
            {
                Assert.InRange(e.ColorIndex, 0, w.Config.EnemyColorCount - 1);
                seen.Add(e.ColorIndex);
            }
        }
        Assert.True(seen.Count > 1, "a run of rooms should not paint every enemy the same colour");
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

public class ShadowTests
{
    static MazeData Maze()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (!Directory.Exists(Path.Combine(d.FullName, "assets", "mazes"))) d = d.Parent!;
        return MazeLoader.Load(Path.Combine(d.FullName, "assets", "mazes", "c64.maze"));
    }

    [Fact]
    public void ShadowAppearsAndCatchesLingeringPlayer()
    {
        var w = new GameWorld(Maze(), new GameConfig { ShadowWarningTicks = 5, ShadowDelayTicks = 10, StartLives = 3 }, 0);
        w.Enemies.Clear();
        bool warned = false, appeared = false;
        for (int i = 0; i < 900 && w.Lives == 3; i++)
        {
            w.Enemies.Clear();
            w.Tick(new InputState(0, 0, false));
            warned |= w.Events.Any(e => e.Kind == WorldEventKind.ShadowWarning);
            appeared |= w.Events.Any(e => e.Kind == WorldEventKind.ShadowAppeared);
        }
        Assert.True(warned); Assert.True(appeared);
        Assert.Equal(2, w.Lives);
    }

    [Fact]
    public void KillingShadowInLastRoomStartsNewCycle()
    {
        var w = new GameWorld(Maze(), new GameConfig(), 127);
        Assert.True(w.IsPodRoom);
        for (int i = 0; i < 8000 && w.Phase != GamePhase.Won; i++) w.Tick(new InputState(0, 0, true));
        Assert.Equal(GamePhase.Won, w.Phase);
        for (int i = 0; i < 400 && w.Phase == GamePhase.Won; i++) w.Tick(new InputState(0, 0, false));
        Assert.Equal(GamePhase.Playing, w.Phase);
        Assert.Equal(1, w.Cycle);
        Assert.Equal(1, w.Room);
    }
}

public class DifficultyTests
{
    [Fact]
    public void DifficultyScalesEnemiesAndLives()
    {
        var b = new GameConfig();
        var n = b.WithDifficulty(Difficulty.Novice);
        var e = b.WithDifficulty(Difficulty.Expert);
        Assert.True(n.EnemySpeedScale < b.EnemySpeedScale && e.EnemySpeedScale > b.EnemySpeedScale);
        Assert.True(n.StartLives > b.StartLives && e.StartLives < b.StartLives);
        Assert.Equal(b.PodBarrierX0, n.PodBarrierX0); // untouched fields survive the clone
        Assert.Equal(b.EnemySpeedScale, b.WithDifficulty(Difficulty.Advanced).EnemySpeedScale);
    }
}

public class BoundaryAndGodTests
{
    static MazeData Maze()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (!Directory.Exists(Path.Combine(d.FullName, "assets", "mazes"))) d = d.Parent!;
        return MazeLoader.Load(Path.Combine(d.FullName, "assets", "mazes", "c64.maze"));
    }

    [Fact]
    public void Room0HasNoLeftExit()
    {
        var w = new GameWorld(Maze(), new GameConfig { GodMode = true }, 0);
        w.Enemies.Clear();
        for (int i = 0; i < 200; i++) w.Tick(new InputState(-1, 0, false));
        Assert.Equal(0, w.Room);
        Assert.True(w.PlayerX >= 1.0f);
    }

    [Fact]
    public void Room0LeftWallKillsWithoutGodMode()
    {
        var w = new GameWorld(Maze(), new GameConfig { StartLives = 3 }, 0);
        w.Enemies.Clear();
        for (int i = 0; i < 200 && w.Lives == 3; i++) w.Tick(new InputState(-1, 0, false));
        Assert.Equal(2, w.Lives);
    }

    [Fact]
    public void GodModeWallsAndEnemiesDoNotKill()
    {
        var w = new GameWorld(Maze(), new GameConfig { GodMode = true, StartLives = 3 }, 0);
        for (int i = 0; i < 600; i++) w.Tick(new InputState(i % 100 < 50 ? 1 : 0, i % 100 < 50 ? 0 : 1, false));
        Assert.Equal(3, w.Lives);
        Assert.Equal(GamePhase.Playing, w.Phase);
    }

    [Fact]
    public void VisitedRoomsAreTracked()
    {
        var w = new GameWorld(Maze(), new GameConfig(), 3);
        Assert.Contains(3, w.Visited);
        Assert.DoesNotContain(4, w.Visited);
    }
}

public class PodRegressionTests
{
    static MazeData Maze()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (!Directory.Exists(Path.Combine(d.FullName, "assets", "mazes"))) d = d.Parent!;
        return MazeLoader.Load(Path.Combine(d.FullName, "assets", "mazes", "c64.maze"));
    }

    [Fact]
    public void OpeningPodSpawnsEnemiesAndKeyIsCollectable()
    {
        var m = Maze();
        var w = new GameWorld(m, new GameConfig(), 18);
        Assert.NotNull(w.Obj);
        for (int i = 0; i < 6000 && !w.PodOpen; i++) { Assert.Empty(w.Enemies); w.Tick(new InputState(0, 0, true)); } // none while closed
        Assert.True(w.PodOpen);
        Assert.NotEmpty(w.Enemies);                  // opening the pod fills the room with enemies
        Assert.NotNull(w.Obj);                       // the key is still there after the barriers vanish
        w.Enemies.Clear();
        w.DebugTeleport(w.Obj!.X, w.Obj.Y);
        w.Tick(new InputState(0, 0, false));
        Assert.Single(w.Keys);
        Assert.Null(w.Obj);
    }
}

public class SpawnMixTests
{
    [Fact]
    public void AllEnemyKindsAppearFromTheFirstLevel()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (!Directory.Exists(Path.Combine(d.FullName, "assets", "mazes"))) d = d.Parent!;
        var m = MazeLoader.Load(Path.Combine(d.FullName, "assets", "mazes", "c64.maze"));
        var seen = new HashSet<EnemyKind>();
        for (int seed = 1; seed <= 40; seed++)
        {
            var w = new GameWorld(m, new GameConfig { Seed = seed }, 3); // level 1 chamber
            foreach (var e in w.Enemies) seen.Add(e.Kind);
        }
        Assert.Contains(EnemyKind.SnapJumper, seen);
        Assert.Contains(EnemyKind.RoboDroid, seen);
        Assert.Contains(EnemyKind.SpiralDrone, seen);
    }
}

public class SnapJumperTests
{
    [Fact]
    public void BlinksWithoutSliding()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (!Directory.Exists(Path.Combine(d.FullName, "assets", "mazes"))) d = d.Parent!;
        var m = MazeLoader.Load(Path.Combine(d.FullName, "assets", "mazes", "c64.maze"));
        var w = new GameWorld(m, new GameConfig { GodMode = true }, 3);
        w.Enemies.Clear();
        var jumper = new Enemy { Kind = EnemyKind.SnapJumper, X = 20f, Y = 11f, CanShoot = false };
        w.Enemies.Add(jumper);

        bool blinked = false;
        for (int i = 0; i < 200 && !blinked; i++)
        {
            w.Tick(new InputState(0, 0, false));
            blinked = jumper.BlinkTicks > 0;
        }
        Assert.True(blinked, "the jumper should blink within a couple of intervals");

        float dx = jumper.X - jumper.BlinkFromX, dy = jumper.Y - jumper.BlinkFromY;
        Assert.InRange(MathF.Sqrt(dx * dx + dy * dy), 1.5f, 2.1f);   // a blink of about two cells, not a slide
        Assert.False(w.Grid.IsSolid((int)MathF.Floor(jumper.X), (int)MathF.Floor(jumper.Y)));   // lands in the open
    }
    [Fact]
    public void JumpersNeverShoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (!Directory.Exists(Path.Combine(d.FullName, "assets", "mazes"))) d = d.Parent!;
        var m = MazeLoader.Load(Path.Combine(d.FullName, "assets", "mazes", "c64.maze"));
        for (int seed = 1; seed <= 30; seed++)
        {
            var w = new GameWorld(m, new GameConfig { Seed = seed, GodMode = true }, 3);
            foreach (var e in w.Enemies.Where(e => e.Kind != EnemyKind.SnapJumper).ToList()) w.Enemies.Remove(e);
            for (int i = 0; i < 600; i++)
            {
                w.Tick(new InputState(0, 0, false));
                Assert.DoesNotContain(w.Events, ev => ev.Kind == WorldEventKind.EnemyShot);
            }
        }
    }
}

public class FriendlyFireTests
{
    [Fact]
    public void EnemyBulletKillsAnotherEnemyWithoutScore()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (!Directory.Exists(Path.Combine(d.FullName, "assets", "mazes"))) d = d.Parent!;
        var m = MazeLoader.Load(Path.Combine(d.FullName, "assets", "mazes", "c64.maze"));
        var w = new GameWorld(m, new GameConfig { GodMode = true }, 3);
        w.Enemies.Clear();
        var shooter = new Enemy { Kind = EnemyKind.RoboDroid, X = 12, Y = 11, CanShoot = false };
        var victim = new Enemy { Kind = EnemyKind.RoboDroid, X = 20, Y = 11, CanShoot = false };
        w.Enemies.Add(shooter); w.Enemies.Add(victim);
        w.EnemyBullets.Add(new Bullet { Owner = shooter, X = 12.5f, Y = 11, DX = 0.28f, DY = 0 });
        int score = w.Score;
        for (int i = 0; i < 60 && victim.Alive; i++) w.Tick(new InputState(0, 0, false));
        Assert.False(victim.Alive);
        Assert.True(shooter.Alive || w.Enemies.Contains(shooter) == false); // shooter is not hit by its own bullet
        Assert.Equal(score, w.Score);
    }
}

public class SpawnCountTests
{
    static MazeData Maze()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (!Directory.Exists(Path.Combine(d.FullName, "assets", "mazes"))) d = d.Parent!;
        return MazeLoader.Load(Path.Combine(d.FullName, "assets", "mazes", "c64.maze"));
    }

    [Theory]
    [InlineData(3, 1, 5)]      // level 1 chamber
    [InlineData(40, 2, 7)]     // level 2 chamber (room 40 is in 38..66)
    [InlineData(75, 3, 9)]     // level 3
    [InlineData(100, 4, 11)]   // level 4
    public void EnemyCountsFollowLevelRanges(int room, int min, int max)
    {
        var m = Maze();
        if (m.IsCorridor(room) || m.IsPod(room)) room = Enumerable.Range(room, 20).First(r => !m.IsCorridor(r) && !m.IsPod(r) && m.Level(r) == m.Level(room));
        int lo = int.MaxValue, hi = 0;
        for (int seed = 1; seed <= 150; seed++)
        {
            var w = new GameWorld(m, new GameConfig { Seed = seed }, room);
            lo = Math.Min(lo, w.Enemies.Count); hi = Math.Max(hi, w.Enemies.Count);
        }
        Assert.Equal(min, lo);
        Assert.Equal(max, hi);
    }

    [Fact]
    public void EnemiesSpawnFarFromTheHero()
    {
        var m = Maze();
        int near = 0, total = 0;
        for (int seed = 1; seed <= 100; seed++)
        {
            var w = new GameWorld(m, new GameConfig { Seed = seed }, 100);
            foreach (var e in w.Enemies)
            {
                total++;
                float dist = MathF.Sqrt((e.X - w.PlayerX) * (e.X - w.PlayerX) + (e.Y - w.PlayerY) * (e.Y - w.PlayerY));
                Assert.True(dist >= 6f, $"enemy spawned {dist:F1} cells from the hero");
                if (dist < 12f) near++;
            }
        }
        Assert.True(near < total * 0.15, $"{near}/{total} enemies spawned within 12 cells");
    }
}
