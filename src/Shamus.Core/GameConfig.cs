namespace Shamus.Core;

/// <summary>Gameplay constants. Units: room cells (RoomGrid.W x RoomGrid.H), time: ticks at 60 Hz.</summary>
public sealed class GameConfig
{
    public int StartLives { get; init; } = 5;
    public float PlayerSpeed { get; init; } = 0.2f;
    public float PlayerRadius { get; init; } = 0.4f;
    public float BulletSpeed { get; init; } = 0.6f;
    public int MaxBullets { get; init; } = 3;
    public int FireCooldown { get; init; } = 8;
    public bool WallsKill { get; init; } = true;   // original: electrified walls
    public int DeathTicks { get; init; } = 60;

    public int Seed { get; init; } = 1;
    public float EnemyRadius { get; init; } = 0.5f;
    public float EnemyBulletSpeed { get; init; } = 0.28f;
    public int EnemyShotCooldown { get; init; } = 90;
    public float ClearedSpeedBonus { get; init; } = 1.25f;
    public int MinSpawnDistance { get; init; } = 10;

    // Pod room: two vertical barriers, each with a moving slit; the target object sits in the middle.
    public float[] PodBarrierX0 { get; init; } = { 15.5f, 23.5f };
    public float PodBarrierWidth { get; init; } = 2f;
    public float PodSlitHeight { get; init; } = 2.5f;
    public float PodSlitSpeed { get; init; } = 0.08f;
    public float PodTargetX { get; init; } = 20.5f;
    public float PodTargetY { get; init; } = 11f;
    public float PodTargetRadius { get; init; } = 1.1f;
}
