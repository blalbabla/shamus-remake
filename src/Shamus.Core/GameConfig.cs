using System.Text.Json;

namespace Shamus.Core;

/// <summary>Gameplay constants. Units: room cells (RoomGrid.W x RoomGrid.H), time: ticks at 60 Hz.</summary>
public sealed class GameConfig
{
    public int StartLives { get; init; } = 5;
    public float PlayerSpeed { get; init; } = 0.133f;
    public float PlayerRadius { get; init; } = 0.4f;
    public float BulletSpeed { get; init; } = 0.6f;
    public int MaxBullets { get; init; } = 3;
    public int FireCooldown { get; init; } = 8;
    public bool WallsKill { get; init; } = true;   // original: electrified walls
    public int DeathTicks { get; init; } = 60;

    public float DroneShooterChance { get; init; } = 0.5f; // share of spiral drones that fire
    public int PotionPoints { get; init; } = 100;
    public int BonusMin { get; init; } = 250;
    public int BonusMax { get; init; } = 1000;
    public float PickupRadius { get; init; } = 0.9f;
    public int ShadowWarningTicks { get; init; } = 600;   // time in a room before the warning
    public int ShadowDelayTicks { get; init; } = 900;     // time in a room before the Shadow shows up
    public float ShadowSpeedFactor { get; init; } = 0.85f; // relative to the player's speed
    public int ShadowKillPoints { get; init; } = 5000;
    public int WinTicks { get; init; } = 180;
    public float CycleEnemySpeedBonus { get; init; } = 0.2f;
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

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, IncludeFields = false };

    /// <summary>Loads overrides from a JSON file; missing file or fields keep defaults.</summary>
    public static GameConfig Load(string path) =>
        File.Exists(path) ? JsonSerializer.Deserialize<GameConfig>(File.ReadAllText(path), Json) ?? new GameConfig() : new GameConfig();

    public string ToJson() => JsonSerializer.Serialize(this, Json);
}
