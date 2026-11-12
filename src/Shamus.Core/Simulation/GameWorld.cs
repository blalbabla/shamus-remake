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
                Bullets.Clear();
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
        float nx = PlayerX + mx * Config.PlayerSpeed * len, ny = PlayerY + my * Config.PlayerSpeed * len;

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
