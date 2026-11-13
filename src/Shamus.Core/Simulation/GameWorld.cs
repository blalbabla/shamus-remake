using Shamus.Core.Mazes;

namespace Shamus.Core.Simulation;

public enum GamePhase { Playing, Dying, GameOver, Won }

public sealed class Bullet
{
    public float X, Y, DX, DY;
}

/// <summary>Deterministic fixed-step simulation of one maze. Renderers read state; the only outbound channel is <see cref="Events"/>.</summary>
public sealed class GameWorld
{
    public MazeData Maze { get; }
    public GameConfig Config { get; }
    public int Room { get; private set; }
    public RoomGrid Grid { get; private set; } = null!;
    public float PlayerX { get; private set; }
    public float PlayerY { get; private set; }
    public float PrevPlayerX { get; private set; }
    public float PrevPlayerY { get; private set; }
    public int FaceX { get; private set; } = 1;
    public int FaceY { get; private set; }
    public int Lives { get; private set; }
    public GamePhase Phase { get; private set; } = GamePhase.Playing;
    public List<Bullet> Bullets { get; } = new();
    public List<WorldEvent> Events { get; } = new();
    public long TickCount { get; private set; }
    public List<Enemy> Enemies { get; } = new();
    public List<Bullet> EnemyBullets { get; } = new();
    public int Score { get; private set; }
    public bool RoomCleared { get; private set; }
    private readonly Random _rng;
    private readonly HashSet<int> _visited = new();
    private readonly HashSet<int> _podsOpened = new();   // pod rooms whose barriers were shot away
    public IReadOnlySet<int> Visited => _visited;
    /// <summary>True if the room's object was collected or its door unlocked.</summary>
    public bool IsTaken(int room) => _taken.Contains(room) || _unlocked.Contains(room);

    // Shadow: relentless, invulnerable chaser that appears when you linger in a room.
    public bool ShadowActive { get; private set; }
    public float ShadowX { get; private set; }
    public float ShadowY { get; private set; }
    public bool ShadowWarned { get; private set; }
    public int Cycle { get; private set; }               // completed loops of the maze
    private int _roomTicks, _winLeft;

    // objects, keys and doors
    public RoomObject? Obj { get; private set; }
    public List<int> Keys { get; } = new();              // key ids carried
    public int DoorSide { get; private set; }            // -1 left, +1 right, 0 none
    public bool DoorClosed { get; private set; }
    private readonly HashSet<int> _taken = new();        // rooms whose object was collected
    private readonly HashSet<int> _unlocked = new();     // keyhole rooms already opened
    private static readonly float[] ObjX = { 12.5f, 20f, 28f }, ObjY = { 4f, 11f, 18f };

    // pod state
    public bool IsPodRoom { get; private set; }
    public bool PodOpen { get; private set; }
    public readonly float[] SlitY = new float[2];
    private readonly float[] _slitDir = new float[2];

    private float _entryX, _entryY;
    private int _cooldown, _dyingLeft;

    public GameWorld(MazeData maze, GameConfig config, int startRoom = 0)
    {
        Maze = maze; Config = config; Lives = config.StartLives;
        _rng = new Random(config.Seed);
        EnterRoom(startRoom, 1.5f, 11f);
    }

    public bool Corridor => Maze.IsCorridor(Room);

    private void EnterRoom(int room, float x, float y)
    {
        Room = Math.Clamp(room, 0, MazeData.RoomCount - 1);
        Grid = RoomGrid.Build(Maze, Room);
        _visited.Add(Room);
        // the maze boundary: electrified wall instead of an exit
        if (Room == 0) Grid.SetSolid(0, RoomGrid.DoorY0, 1, RoomGrid.DoorY1, true);
        if (Room == MazeData.RoomCount - 1) Grid.SetSolid(RoomGrid.W - 1, RoomGrid.DoorY0, RoomGrid.W, RoomGrid.DoorY1, true);
        PlayerX = PrevPlayerX = _entryX = x;
        PlayerY = PrevPlayerY = _entryY = y;
        Bullets.Clear();
        IsPodRoom = Maze.IsPod(Room);
        PodOpen = IsPodRoom && Room != MazeData.RoomCount - 1 && _podsOpened.Contains(Room);
        SlitY[0] = 4f; _slitDir[0] = 1;
        SlitY[1] = 14f; _slitDir[1] = -1;
        Events.Add(new WorldEvent(WorldEventKind.RoomEntered, x, y, Room));
        SpawnObject();
        SpawnEnemies();
        ResetShadow();
    }

    private void ResetShadow() { ShadowActive = false; ShadowWarned = false; _roomTicks = 0; }

    private void SpawnObject()
    {
        Obj = null; DoorSide = 0; DoorClosed = false;
        var kind = Maze.Object(Room);
        if (kind == ObjectKind.None || (kind == ObjectKind.Shadow && !IsPodRoom)) return;
        if (IsPodRoom && _taken.Contains(Room) && kind != ObjectKind.Shadow) return; // already collected
        if (IsPodRoom)
        {   // the pod target is the room's object; position fixed in the centre
            Obj = new RoomObject { Kind = kind, X = Config.PodTargetX, Y = Config.PodTargetY, KeyId = Maze.KeyId(Room), Hue = Maze.ObjectHue(Room), Luma = Maze.ObjectLuma(Room) };
            return;
        }
        if (kind == ObjectKind.Keyhole)
        {
            DoorSide = Maze.DoorOnLeft(Room) ? -1 : 1;
            DoorClosed = !_unlocked.Contains(Room);
            if (DoorClosed) SetDoor(true);
        }
        else if (_taken.Contains(Room)) return;
        // random one of the 9 positions of the '#' grid, in a random order until a free one is found
        var order = Enumerable.Range(0, 9).OrderBy(_ => _rng.Next()).ToList();
        foreach (int i in order)
        {
            float x = ObjX[i % 3], y = ObjY[i / 3];
            if (!OpenArea(x, y, 0.7f)) continue;
            if (MathF.Abs(x - PlayerX) + MathF.Abs(y - PlayerY) < 4f) continue;
            Obj = new RoomObject { Kind = kind, X = x, Y = y, KeyId = Maze.KeyId(Room), Hue = Maze.ObjectHue(Room), Luma = Maze.ObjectLuma(Room) };
            return;
        }
    }

    /// <summary>Debug / test helper.</summary>
    public void DebugTeleport(float x, float y) { PlayerX = PrevPlayerX = x; PlayerY = PrevPlayerY = y; }

    private void SetDoor(bool closed)
    {
        int x0 = DoorSide < 0 ? 0 : RoomGrid.W - 1;
        Grid.SetSolid(x0, RoomGrid.DoorY0, x0 + 1, RoomGrid.DoorY1, closed);
    }

    private void UpdatePickup()
    {
        if (Obj == null || (IsPodRoom && !PodOpen)) return;
        float dx = Obj.X - PlayerX, dy = Obj.Y - PlayerY;
        if (dx * dx + dy * dy > Config.PickupRadius * Config.PickupRadius) return;
        switch (Obj.Kind)
        {
            case ObjectKind.Key:
                Keys.Add(Obj.KeyId); _taken.Add(Room);
                Events.Add(new WorldEvent(WorldEventKind.KeyPicked, Obj.X, Obj.Y, Obj.KeyId));
                Obj = null; break;
            case ObjectKind.Potion:
                Lives++; Score += Config.PotionPoints; _taken.Add(Room);
                Events.Add(new WorldEvent(WorldEventKind.PotionPicked, Obj.X, Obj.Y));
                Obj = null; break;
            case ObjectKind.Mystery:
                int pts = Config.BonusMin + _rng.Next((Config.BonusMax - Config.BonusMin) / 50 + 1) * 50;
                Score += pts; _taken.Add(Room);
                Events.Add(new WorldEvent(WorldEventKind.BonusPicked, Obj.X, Obj.Y, pts));
                Obj = null; break;
            case ObjectKind.Keyhole:
                if (DoorClosed && Keys.Remove(Obj.KeyId))
                {
                    DoorClosed = false; _unlocked.Add(Room); SetDoor(false);
                    Events.Add(new WorldEvent(WorldEventKind.DoorOpened, Obj.X, Obj.Y, Obj.KeyId));
                }
                break;
        }
    }

    private bool OpenArea(float x, float y, float r)
    {
        for (int sx = -1; sx <= 1; sx += 2)
            for (int sy = -1; sy <= 1; sy += 2)
                if (Grid.IsSolid((int)MathF.Floor(x + sx * r), (int)MathF.Floor(y + sy * r))) return false;
        return true;
    }

    private void SpawnEnemies()
    {
        Enemies.Clear(); EnemyBullets.Clear();
        RoomCleared = false;
        if ((IsPodRoom && !PodOpen) || Room == MazeData.RoomCount - 1) { RoomCleared = true; return; }
        int level = Maze.Level(Room);
        int count = Math.Min(2 + level + _rng.Next(0, 3), 7);
        int guard = 0;
        while (Enemies.Count < count && guard++ < 500)
        {
            float x = 1 + (float)_rng.NextDouble() * (RoomGrid.W - 2), y = 1 + (float)_rng.NextDouble() * (RoomGrid.H - 2);
            if (!OpenArea(x, y, 0.7f)) continue;
            if (MathF.Abs(x - PlayerX) + MathF.Abs(y - PlayerY) < Config.MinSpawnDistance) continue;
            // all three kinds from the start; jumpers are rarer on the first level
            int roll = _rng.Next(level == 0 ? 11 : 12);
            var kind = level == 0 ? (roll < 5 ? EnemyKind.RoboDroid : roll < 9 ? EnemyKind.SpiralDrone : EnemyKind.SnapJumper)
                                  : (roll < 4 ? EnemyKind.RoboDroid : roll < 8 ? EnemyKind.SpiralDrone : EnemyKind.SnapJumper);
            Enemies.Add(new Enemy { Kind = kind, X = x, Y = y, Phase = (float)_rng.NextDouble() * 6.28f,
                                    ShotCooldown = 30 + _rng.Next(Config.EnemyShotCooldown) });
            var ne = Enemies[^1];
            if (ne.Kind == EnemyKind.SpiralDrone) ne.CanShoot = _rng.NextDouble() < Config.DroneShooterChance;
        }
        if (Enemies.Count == 0) RoomCleared = true;
    }

    /// <summary>Is point inside a closed pod barrier (not in its slit)?</summary>
    public bool InBarrier(float x, float y)
    {
        if (!IsPodRoom || PodOpen) return false;
        if (y < 1f || y >= RoomGrid.H - 1f) return false;
        for (int i = 0; i < 2; i++)
        {
            float x0 = Config.PodBarrierX0[i];
            if (x >= x0 && x < x0 + Config.PodBarrierWidth && !(y >= SlitY[i] && y < SlitY[i] + Config.PodSlitHeight)) return true;
        }
        return false;
    }

    private bool PlayerHitsWall(float px, float py)
    {
        float r = Config.PlayerRadius;
        for (int sx = -1; sx <= 1; sx += 2)
            for (int sy = -1; sy <= 1; sy += 2)
            {
                float x = px + sx * r, y = py + sy * r;
                int cx = (int)MathF.Floor(x), cy = (int)MathF.Floor(y);
                if (cx < 0 || cx >= RoomGrid.W) continue;                    // leaving through a door
                if ((cy < 0 || cy >= RoomGrid.H) && Corridor) continue;     // leaving through a corridor exit
                if (Grid.IsSolid(cx, cy)) return true;
            }
        return false;
    }

    public void Tick(InputState input)
    {
        Events.Clear();
        TickCount++;
        if (Phase == GamePhase.GameOver) return;
        if (Phase == GamePhase.Won) { if (--_winLeft <= 0) NextCycle(); return; }
        if (Phase == GamePhase.Dying)
        {
            if (--_dyingLeft <= 0)
            {
                Phase = GamePhase.Playing;
                PlayerX = PrevPlayerX = _entryX;
                PlayerY = PrevPlayerY = _entryY;
                Bullets.Clear();
                ResetShadow();
                SpawnEnemies(); // a fresh random set of enemies, as on entering the room
            }
            return;
        }

        PrevPlayerX = PlayerX; PrevPlayerY = PlayerY;
        if (IsPodRoom && !PodOpen) UpdateSlits();
        MovePlayer(input);
        if (Phase != GamePhase.Playing) return;
        if (_cooldown > 0) _cooldown--;
        if (input.Fire && _cooldown == 0 && Bullets.Count < Config.MaxBullets) Fire();
        UpdatePickup();
        UpdateBullets();
        UpdateEnemies();
        if (Phase == GamePhase.Playing) UpdateEnemyBullets();
        if (Phase == GamePhase.Playing) UpdateShadow();
    }

    private void KillEnemy(Enemy e)
    {
        e.Alive = false;
        Score += e.Kind switch { EnemyKind.RoboDroid => 50, EnemyKind.SpiralDrone => 100, _ => 150 };
        Events.Add(new WorldEvent(WorldEventKind.EnemyKilled, e.X, e.Y, (int)e.Kind));
    }

    /// <summary>Moves an enemy unless a wall is in the way (walls do not harm enemies).</summary>
    private bool Move(Enemy e, float dx, float dy)
    {
        float nx = e.X + dx, ny = e.Y + dy;
        if (nx < 1f || nx >= RoomGrid.W - 1f || ny < 1f || ny >= RoomGrid.H - 1f || !OpenArea(nx, ny, 0.45f)) return false;
        e.X = nx; e.Y = ny;
        return true;
    }

    /// <summary>Picks a new, semi-random intent: usually toward the player, sometimes sideways, back or random.</summary>
    private void Decide(Enemy e, float dx, float dy)
    {
        double r = _rng.NextDouble();
        bool horiz = MathF.Abs(dx) > MathF.Abs(dy);
        int towardX = MathF.Sign(dx) == 0 ? 1 : (int)MathF.Sign(dx), towardY = MathF.Sign(dy) == 0 ? 1 : (int)MathF.Sign(dy);
        // Robo-droid: methodical, mostly purposeful pursuit with the occasional detour.
        if (e.Kind == EnemyKind.RoboDroid)
        {
            if (r < 0.75) { e.DirX = horiz ? towardX : 0; e.DirY = horiz ? 0 : towardY; }
            else if (r < 0.90) { e.DirX = horiz ? 0 : towardX; e.DirY = horiz ? towardY : 0; }
            else { int d = _rng.Next(4); e.DirX = d == 0 ? 1 : d == 1 ? -1 : 0; e.DirY = d == 2 ? 1 : d == 3 ? -1 : 0; }
            e.ModeLeft = 25 + _rng.Next(40);
            return;
        }
        // Snap jumper: aggressive, jumps almost always straight at the player (direction stored as a unit vector).
        if (e.Kind == EnemyKind.SnapJumper)
        {
            float len = MathF.Sqrt(dx * dx + dy * dy) + 0.0001f;
            float ux = dx / len, uy = dy / len;
            if (r < 0.15f) { float sx = -uy, sy = ux; if (_rng.Next(2) == 0) { sx = -sx; sy = -sy; } ux = sx; uy = sy; } // sidestep jump
            e.JumpX = ux; e.JumpY = uy;
            e.ModeLeft = 10;
            return;
        }
        // Spiral drone: chaotic, changing trajectories; angular offset from the straight line to the player.
        e.Offset = r < 0.35 ? 0f : r < 0.65 ? (_rng.Next(2) == 0 ? 1.57f : -1.57f)  // circle around
                 : r < 0.80 ? 3.14f + (float)(_rng.NextDouble() - 0.5)              // retreat
                 : (float)(_rng.NextDouble() * 6.28);                               // drift
        e.ModeLeft = 15 + _rng.Next(45);
    }

    private void UpdateEnemies()
    {
        int level = Maze.Level(Room);
        float spd = (1f + 0.25f * level) * (1f + Config.CycleEnemySpeedBonus * Cycle) * Config.EnemySpeedScale;
        foreach (var e in Enemies)
        {
            if (!e.Alive) continue;
            e.Timer++;
            float dx = PlayerX - e.X, dy = PlayerY - e.Y;
            float dist = MathF.Sqrt(dx * dx + dy * dy) + 0.0001f;
            if (e.ModeLeft > 0) e.ModeLeft--; else Decide(e, dx, dy);
            switch (e.Kind)
            {
                case EnemyKind.RoboDroid: // plods along axes; direction re-chosen every so often
                    {
                        float v = 0.036f * spd;
                        if (!Move(e, e.DirX * v, e.DirY * v)) e.ModeLeft = 0; // blocked: decide again next tick
                        break;
                    }
                case EnemyKind.SpiralDrone: // sways around a heading that may circle or back off
                    {
                        float v = 0.06f * spd;
                        float ang = MathF.Atan2(dy, dx) + e.Offset + 0.5f * MathF.Sin(e.Timer * 0.12f + e.Phase);
                        bool movedX = Move(e, MathF.Cos(ang) * v, 0), movedY = Move(e, 0, MathF.Sin(ang) * v);
                        if (!movedX && !movedY) e.ModeLeft = 0;
                        break;
                    }
                default: // snap jumper: pause, then dash in a (mostly player-ward) direction
                    {
                        if (e.DashLeft > 0)
                        {
                            if (!Move(e, e.DashX, e.DashY)) e.DashLeft = 0; else e.DashLeft--;
                        }
                        else if (e.Timer % 32 == 0)
                        {
                            Decide(e, dx, dy);
                            float v = 0.5f * spd;                // fast, large jump (~4 cells)
                            e.DashX = e.JumpX * v; e.DashY = e.JumpY * v;
                            e.DashLeft = 8;
                        }
                        break;
                    }
            }

            // shooting when roughly lined up with the player
            if (e.ShotCooldown > 0) e.ShotCooldown--;
            else if (e.CanShoot && EnemyBullets.Count < 3 + level && dist < 26f)
            {
                float v = Config.EnemyBulletSpeed;
                bool aligned = MathF.Abs(dx) < 1f || MathF.Abs(dy) < 1f;
                if (e.Kind == EnemyKind.SpiralDrone)
                {
                    // suppressive fire: a fan of three shots toward the player's side of the room
                    float ang = MathF.Atan2(dy, dx);
                    for (int k = -1; k <= 1; k++)
                        EnemyBullets.Add(new Bullet { X = e.X, Y = e.Y, DX = MathF.Cos(ang + k * 0.35f) * v, DY = MathF.Sin(ang + k * 0.35f) * v });
                    e.ShotCooldown = Config.EnemyShotCooldown + 30;
                    Events.Add(new WorldEvent(WorldEventKind.EnemyShot, e.X, e.Y, (int)e.Kind));
                }
                else if (aligned)
                {
                    EnemyBullets.Add(new Bullet { X = e.X, Y = e.Y, DX = MathF.Abs(dx) < 1f ? 0 : MathF.Sign(dx) * v, DY = MathF.Abs(dx) < 1f ? MathF.Sign(dy) * v : 0 });
                    e.ShotCooldown = e.Kind == EnemyKind.SnapJumper ? Config.EnemyShotCooldown * 2 : Config.EnemyShotCooldown;
                    Events.Add(new WorldEvent(WorldEventKind.EnemyShot, e.X, e.Y, (int)e.Kind));
                }
            }

            // contact kills the player
            float rr = Config.EnemyRadius + Config.PlayerRadius;
            if (dist < rr) { KillEnemy(e); Die(); break; }
        }
        Enemies.RemoveAll(e => !e.Alive);
        if (!RoomCleared && Enemies.Count == 0) { RoomCleared = true; Events.Add(new WorldEvent(WorldEventKind.RoomCleared)); }
    }

    private void UpdateEnemyBullets()
    {
        for (int i = EnemyBullets.Count - 1; i >= 0; i--)
        {
            var b = EnemyBullets[i];
            b.X += b.DX; b.Y += b.DY;
            int cx = (int)MathF.Floor(b.X), cy = (int)MathF.Floor(b.Y);
            bool dead = b.X < 0 || b.X >= RoomGrid.W || b.Y < 0 || b.Y >= RoomGrid.H || Grid.IsSolid(cx, cy);
            float px = b.X - PlayerX, py = b.Y - PlayerY;
            if (!dead && px * px + py * py < 0.45f * 0.45f) { EnemyBullets.RemoveAt(i); Die(); return; }
            if (dead) EnemyBullets.RemoveAt(i);
        }
    }

    private void UpdateSlits()
    {
        for (int i = 0; i < 2; i++)
        {
            SlitY[i] += _slitDir[i] * Config.PodSlitSpeed;
            float max = RoomGrid.H - 1f - Config.PodSlitHeight;
            if (SlitY[i] <= 1f) { SlitY[i] = 1f; _slitDir[i] = 1; }
            else if (SlitY[i] >= max) { SlitY[i] = max; _slitDir[i] = -1; }
        }
    }

    private void MovePlayer(InputState input)
    {
        int mx = Math.Sign(input.MoveX), my = Math.Sign(input.MoveY);
        if (mx != 0 || my != 0) { FaceX = mx; FaceY = my; }
        float len = (mx != 0 && my != 0) ? 0.70710678f : 1f;
        float spd = Config.PlayerSpeed * (RoomCleared && !(IsPodRoom && !PodOpen) ? Config.ClearedSpeedBonus : 1f);
        float nx = PlayerX + mx * spd * len, ny = PlayerY + my * spd * len;

        // pod barriers are solid for the player
        if (InBarrier(nx + mx * Config.PlayerRadius, PlayerY)) nx = PlayerX;
        if (InBarrier(PlayerX, ny + my * Config.PlayerRadius)) ny = PlayerY;
        PlayerX = nx; PlayerY = ny;

        if (Config.WallsKill && PlayerHitsWall(PlayerX, PlayerY))
        {
            if (!Config.GodMode) { Die(); return; }
            // god mode: walls still block, but do not kill; slide along them
            if (!PlayerHitsWall(nx, PrevPlayerY)) { PlayerX = nx; PlayerY = PrevPlayerY; }
            else if (!PlayerHitsWall(PrevPlayerX, ny)) { PlayerX = PrevPlayerX; PlayerY = ny; }
            else { PlayerX = PrevPlayerX; PlayerY = PrevPlayerY; }
        }

        if (PlayerX >= RoomGrid.W) EnterRoom(Room + 1, 1.5f, PlayerY);
        else if (PlayerX < 0) EnterRoom(Room - 1, RoomGrid.W - 1.5f, PlayerY);
        else if (Corridor && PlayerY < 0) EnterRoom(Maze.VerticalLink(Room), PlayerX, RoomGrid.H - 1.5f);
        else if (Corridor && PlayerY >= RoomGrid.H) EnterRoom(Maze.VerticalLink(Room), PlayerX, 1.5f);
        else if (!Corridor && (PlayerY < 0 || PlayerY >= RoomGrid.H)) { if (Config.GodMode) PlayerY = Math.Clamp(PlayerY, 0.5f, RoomGrid.H - 0.5f); else Die(); }
    }

    private void Fire()
    {
        float len = (FaceX != 0 && FaceY != 0) ? 0.70710678f : 1f;
        Bullets.Add(new Bullet { X = PlayerX, Y = PlayerY, DX = FaceX * len * Config.BulletSpeed, DY = FaceY * len * Config.BulletSpeed });
        _cooldown = Config.FireCooldown;
        Events.Add(new WorldEvent(WorldEventKind.Shot, PlayerX, PlayerY));
    }

    private void UpdateBullets()
    {
        for (int i = Bullets.Count - 1; i >= 0; i--)
        {
            var b = Bullets[i];
            bool dead = false;
            const int Sub = 3; // sub-steps so fast bullets can't skip thin walls
            for (int s = 0; s < Sub && !dead; s++)
            {
                b.X += b.DX / Sub; b.Y += b.DY / Sub;
                if (b.X < -1 || b.X > RoomGrid.W + 1 || b.Y < -1 || b.Y > RoomGrid.H + 1) { dead = true; break; }
                if (IsPodRoom && !PodOpen)
                {
                    float dx = b.X - Config.PodTargetX, dy = b.Y - Config.PodTargetY;
                    if (dx * dx + dy * dy <= Config.PodTargetRadius * Config.PodTargetRadius)
                    {
                        PodOpen = true; dead = true;
                        Events.Add(new WorldEvent(WorldEventKind.PodOpened, b.X, b.Y));
                        if (Room == MazeData.RoomCount - 1) Win();
                        else { _podsOpened.Add(Room); SpawnEnemies(); } // barriers vanish, the room fills with enemies
                        break;
                    }
                    if (InBarrier(b.X, b.Y)) { dead = true; Events.Add(new WorldEvent(WorldEventKind.BulletHitBarrier, b.X, b.Y)); break; }
                }
                for (int k = EnemyBullets.Count - 1; k >= 0; k--)
                {
                    var eb = EnemyBullets[k];
                    if ((eb.X - b.X) * (eb.X - b.X) + (eb.Y - b.Y) * (eb.Y - b.Y) < 0.5f * 0.5f)
                    {
                        EnemyBullets.RemoveAt(k); dead = true;
                        Events.Add(new WorldEvent(WorldEventKind.BulletsCancelled, b.X, b.Y));
                        break;
                    }
                }
                if (dead) break;
                foreach (var e in Enemies)
                    if (e.Alive && (e.X - b.X) * (e.X - b.X) + (e.Y - b.Y) * (e.Y - b.Y) < 0.7f * 0.7f)
                    { KillEnemy(e); dead = true; break; }
                if (dead) break;
                int cx = (int)MathF.Floor(b.X), cy = (int)MathF.Floor(b.Y);
                if (cx >= 0 && cx < RoomGrid.W && cy >= 0 && cy < RoomGrid.H && Grid.IsSolid(cx, cy))
                { dead = true; Events.Add(new WorldEvent(WorldEventKind.BulletHitWall, b.X, b.Y)); }
            }
            if (dead) Bullets.RemoveAt(i);
        }
    }

    private bool CellFree(int x, int y) => x >= 0 && y >= 0 && x < RoomGrid.W && y < RoomGrid.H && OpenArea(x + 0.5f, y + 0.5f, 0.4f);

    /// <summary>Centre of the neighbouring cell that is one step closer (by BFS) to the player.</summary>
    private (float, float) NextWaypoint()
    {
        int px = Math.Clamp((int)PlayerX, 0, RoomGrid.W - 1), py = Math.Clamp((int)PlayerY, 0, RoomGrid.H - 1);
        int sx = Math.Clamp((int)ShadowX, 0, RoomGrid.W - 1), sy = Math.Clamp((int)ShadowY, 0, RoomGrid.H - 1);
        var dist = new int[RoomGrid.W, RoomGrid.H];
        for (int x = 0; x < RoomGrid.W; x++) for (int y = 0; y < RoomGrid.H; y++) dist[x, y] = -1;
        var q = new Queue<(int, int)>();
        dist[px, py] = 0; q.Enqueue((px, py));
        int[] ox = { 1, -1, 0, 0 }, oy = { 0, 0, 1, -1 };
        while (q.Count > 0)
        {
            var (cx, cy) = q.Dequeue();
            for (int k = 0; k < 4; k++)
            {
                int nx = cx + ox[k], ny = cy + oy[k];
                if (nx < 0 || ny < 0 || nx >= RoomGrid.W || ny >= RoomGrid.H || dist[nx, ny] >= 0 || !CellFree(nx, ny)) continue;
                dist[nx, ny] = dist[cx, cy] + 1; q.Enqueue((nx, ny));
            }
        }
        if (dist[sx, sy] < 0) return (PlayerX, PlayerY);
        int bx = sx, by = sy, bd = dist[sx, sy];
        for (int k = 0; k < 4; k++)
        {
            int nx = sx + ox[k], ny = sy + oy[k];
            if (nx < 0 || ny < 0 || nx >= RoomGrid.W || ny >= RoomGrid.H) continue;
            if (dist[nx, ny] >= 0 && dist[nx, ny] < bd) { bd = dist[nx, ny]; bx = nx; by = ny; }
        }
        return (bx + 0.5f, by + 0.5f);
    }

    private void Win()
    {
        Phase = GamePhase.Won; _winLeft = Config.WinTicks;
        Score += Config.ShadowKillPoints; Obj = null;
        Events.Add(new WorldEvent(WorldEventKind.ShadowKilled, Config.PodTargetX, Config.PodTargetY));
    }

    private void NextCycle()
    {
        Cycle++;
        _taken.Clear(); _unlocked.Clear();
        Phase = GamePhase.Playing;
        EnterRoom(1, 1.5f, 11f);
        Events.Add(new WorldEvent(WorldEventKind.CycleStarted, 0, 0, Cycle));
    }

    private void UpdateShadow()
    {
        if (IsPodRoom) return;
        _roomTicks++;
        if (!ShadowWarned && _roomTicks >= Config.ShadowWarningTicks) { ShadowWarned = true; Events.Add(new WorldEvent(WorldEventKind.ShadowWarning)); }
        if (!ShadowActive && _roomTicks >= Config.ShadowDelayTicks)
        {
            ShadowActive = true;
            ShadowX = PlayerX < RoomGrid.W / 2f ? RoomGrid.W - 1.5f : 1.5f; ShadowY = 11f;
            Events.Add(new WorldEvent(WorldEventKind.ShadowAppeared, ShadowX, ShadowY));
        }
        if (!ShadowActive) return;
        float dx = PlayerX - ShadowX, dy = PlayerY - ShadowY;
        float v = Config.PlayerSpeed * Config.ShadowSpeedFactor * (1f + 0.1f * Math.Min(Cycle, 4));
        float len = MathF.Sqrt(dx * dx + dy * dy) + 0.0001f;
        // path around walls: follow a BFS distance field towards the player's cell
        var (tx, ty) = len < 2f ? (PlayerX, PlayerY) : NextWaypoint();
        float wx = tx - ShadowX, wy = ty - ShadowY, wl = MathF.Sqrt(wx * wx + wy * wy) + 0.0001f;
        float nx = ShadowX + wx / wl * v, ny = ShadowY + wy / wl * v;
        if (OpenArea(nx, ShadowY, 0.35f)) ShadowX = nx;
        if (OpenArea(ShadowX, ny, 0.35f)) ShadowY = ny;
        // invulnerable: player bullets are absorbed
        for (int i = Bullets.Count - 1; i >= 0; i--)
            if ((Bullets[i].X - ShadowX) * (Bullets[i].X - ShadowX) + (Bullets[i].Y - ShadowY) * (Bullets[i].Y - ShadowY) < 0.6f * 0.6f)
            { Events.Add(new WorldEvent(WorldEventKind.BulletHitShadow, Bullets[i].X, Bullets[i].Y)); Bullets.RemoveAt(i); }
        if (len < 0.7f) Die();
    }

    private void Die()
    {
        if (Config.GodMode) return;
        Lives--;
        Events.Add(new WorldEvent(WorldEventKind.PlayerDied, PlayerX, PlayerY));
        if (Lives <= 0) { Phase = GamePhase.GameOver; Events.Add(new WorldEvent(WorldEventKind.GameOver)); }
        else { Phase = GamePhase.Dying; _dyingLeft = Config.DeathTicks; }
    }
}
