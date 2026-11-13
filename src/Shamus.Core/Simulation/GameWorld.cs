using Shamus.Core.Mazes;

namespace Shamus.Core.Simulation;

public enum GamePhase { Playing, Dying, GameOver }

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
        PlayerX = PrevPlayerX = _entryX = x;
        PlayerY = PrevPlayerY = _entryY = y;
        Bullets.Clear();
        IsPodRoom = Maze.IsPod(Room);
        PodOpen = false;
        SlitY[0] = 4f; _slitDir[0] = 1;
        SlitY[1] = 14f; _slitDir[1] = -1;
        Events.Add(new WorldEvent(WorldEventKind.RoomEntered, x, y, Room));
        SpawnEnemies();
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
        if (IsPodRoom || Room == 127) { RoomCleared = true; return; }
        int level = Maze.Level(Room);
        int count = Math.Min(2 + level + _rng.Next(0, 3), 7);
        int guard = 0;
        while (Enemies.Count < count && guard++ < 500)
        {
            float x = 1 + (float)_rng.NextDouble() * (RoomGrid.W - 2), y = 1 + (float)_rng.NextDouble() * (RoomGrid.H - 2);
            if (!OpenArea(x, y, 0.7f)) continue;
            if (MathF.Abs(x - PlayerX) + MathF.Abs(y - PlayerY) < Config.MinSpawnDistance) continue;
            int kinds = level == 0 ? 2 : 3;
            Enemies.Add(new Enemy { Kind = (EnemyKind)_rng.Next(kinds), X = x, Y = y, Phase = (float)_rng.NextDouble() * 6.28f,
                                    ShotCooldown = 30 + _rng.Next(Config.EnemyShotCooldown) });
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
        if (Phase == GamePhase.Dying)
        {
            if (--_dyingLeft <= 0)
            {
                Phase = GamePhase.Playing;
                PlayerX = PrevPlayerX = _entryX;
                PlayerY = PrevPlayerY = _entryY;
                Bullets.Clear(); EnemyBullets.Clear();
            }
            return;
        }

        PrevPlayerX = PlayerX; PrevPlayerY = PlayerY;
        if (IsPodRoom && !PodOpen) UpdateSlits();
        MovePlayer(input);
        if (Phase != GamePhase.Playing) return;
        if (_cooldown > 0) _cooldown--;
        if (input.Fire && _cooldown == 0 && Bullets.Count < Config.MaxBullets) Fire();
        UpdateBullets();
        UpdateEnemies();
        if (Phase == GamePhase.Playing) UpdateEnemyBullets();
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

    private void UpdateEnemies()
    {
        int level = Maze.Level(Room);
        float spd = 1f + 0.25f * level;
        foreach (var e in Enemies)
        {
            if (!e.Alive) continue;
            e.Timer++;
            float dx = PlayerX - e.X, dy = PlayerY - e.Y;
            float dist = MathF.Sqrt(dx * dx + dy * dy) + 0.0001f;
            switch (e.Kind)
            {
                case EnemyKind.RoboDroid: // plods along one axis toward the player
                    {
                        float v = 0.045f * spd;
                        bool horiz = MathF.Abs(dx) > MathF.Abs(dy);
                        float sx = MathF.Sign(dx) * v, sy = MathF.Sign(dy) * v;
                        if (!(horiz ? Move(e, sx, 0) : Move(e, 0, sy)))      // blocked: try the other axis
                            _ = horiz ? Move(e, 0, sy) : Move(e, sx, 0);
                        break;
                    }
                case EnemyKind.SpiralDrone: // approaches along a swaying path
                    {
                        float v = 0.06f * spd;
                        float ang = MathF.Atan2(dy, dx) + 1.1f * MathF.Sin(e.Timer * 0.08f + e.Phase);
                        Move(e, MathF.Cos(ang) * v, 0); Move(e, 0, MathF.Sin(ang) * v);
                        break;
                    }
                default: // snap jumper: pause, then dash toward the player
                    {
                        if (e.DashLeft > 0)
                        {
                            if (!Move(e, e.DashX, e.DashY)) e.DashLeft = 0; else e.DashLeft--;
                        }
                        else if (e.Timer % 50 == 0)
                        {
                            float v = 0.32f * spd;
                            if (MathF.Abs(dx) > MathF.Abs(dy)) { e.DashX = MathF.Sign(dx) * v; e.DashY = 0; }
                            else { e.DashX = 0; e.DashY = MathF.Sign(dy) * v; }
                            e.DashLeft = 7;
                        }
                        break;
                    }
            }

            // shooting when roughly lined up with the player
            if (e.ShotCooldown > 0) e.ShotCooldown--;
            else if ((MathF.Abs(dx) < 1f || MathF.Abs(dy) < 1f) && EnemyBullets.Count < 3 + level && dist < 24f)
            {
                float v = Config.EnemyBulletSpeed;
                EnemyBullets.Add(new Bullet { X = e.X, Y = e.Y, DX = MathF.Abs(dx) < 1f ? 0 : MathF.Sign(dx) * v, DY = MathF.Abs(dx) < 1f ? MathF.Sign(dy) * v : 0 });
                e.ShotCooldown = Config.EnemyShotCooldown;
                Events.Add(new WorldEvent(WorldEventKind.EnemyShot, e.X, e.Y));
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
        float spd = Config.PlayerSpeed * (RoomCleared && !IsPodRoom ? Config.ClearedSpeedBonus : 1f);
        float nx = PlayerX + mx * spd * len, ny = PlayerY + my * spd * len;

        // pod barriers are solid for the player
        if (InBarrier(nx + mx * Config.PlayerRadius, PlayerY)) nx = PlayerX;
        if (InBarrier(PlayerX, ny + my * Config.PlayerRadius)) ny = PlayerY;
        PlayerX = nx; PlayerY = ny;

        if (Config.WallsKill && PlayerHitsWall(PlayerX, PlayerY)) { Die(); return; }

        if (PlayerX >= RoomGrid.W) EnterRoom(Room + 1, 1.5f, PlayerY);
        else if (PlayerX < 0) EnterRoom(Room - 1, RoomGrid.W - 1.5f, PlayerY);
        else if (Corridor && PlayerY < 0) EnterRoom(Maze.VerticalLink(Room), PlayerX, RoomGrid.H - 1.5f);
        else if (Corridor && PlayerY >= RoomGrid.H) EnterRoom(Maze.VerticalLink(Room), PlayerX, 1.5f);
        else if (!Corridor && (PlayerY < 0 || PlayerY >= RoomGrid.H)) Die();
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
                        break;
                    }
                    if (InBarrier(b.X, b.Y)) { dead = true; Events.Add(new WorldEvent(WorldEventKind.BulletHitBarrier, b.X, b.Y)); break; }
                }
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

    private void Die()
    {
        Lives--;
        Events.Add(new WorldEvent(WorldEventKind.PlayerDied, PlayerX, PlayerY));
        if (Lives <= 0) { Phase = GamePhase.GameOver; Events.Add(new WorldEvent(WorldEventKind.GameOver)); }
        else { Phase = GamePhase.Dying; _dyingLeft = Config.DeathTicks; }
    }
}
