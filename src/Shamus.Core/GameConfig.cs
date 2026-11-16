using System.Text.Json;
using System.Text.Json.Nodes;

namespace Shamus.Core;

/// <summary>Gameplay constants. Units: room cells (RoomGrid.W x RoomGrid.H), time: ticks at 60 Hz.</summary>
public enum Difficulty { Novice, Advanced, Expert }

public sealed class GameConfig
{
    public int StartLives { get; init; } = 5;
    public float PlayerSpeed { get; init; } = 0.133f;
    public float PlayerRadius { get; init; } = 0.4f;
    /// <summary>Share of the remaining heading error the hull closes per tick (the tank turns in place).</summary>
    public float TurnEasePerTick { get; init; } = 0.45f;
    /// <summary>The hull drives only once it is this close to the pressed direction, in degrees.</summary>
    public float TurnToleranceDeg { get; init; } = 9f;
    /// <summary>Where a shot leaves the tank, in cells from its centre: the barrel tip (see SceneRenderer.DrawTank).</summary>
    public float MuzzleLength { get; init; } = 0.74f;
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
    public float EnemySpeedScale { get; init; } = 1f;
    public bool GodMode { get; init; }   // test aid: walls and enemies do not kill
    public int SnapJumpInterval { get; init; } = 60;   // ticks between teleports
    /// <summary>How far a snap jumper blinks per teleport, in cells.</summary>
    public float SnapJumpDistance { get; init; } = 2f;
    /// <summary>Length of the teleport cross-fade: the old spot fades out while the body fades in.</summary>
    public int SnapBlinkTicks { get; init; } = 12;
    /// <summary>Opacity range a waiting jumper shimmers between (1 = solid, 0.35 = mostly ghost).</summary>
    public float SnapJumperAlphaMin { get; init; } = 0.35f;
    public float SnapJumperAlphaMax { get; init; } = 1f;
    public int Seed { get; init; } = 1;
    public float EnemyRadius { get; init; } = 0.5f;
    /// <summary>How many body colours enemies are rolled from at spawn (the renderer wraps if it has fewer).</summary>
    public int EnemyColorCount { get; init; } = 4;
    public float EnemyBulletSpeed { get; init; } = 0.28f;
    public int EnemyShotCooldown { get; init; } = 90;
    public float ClearedSpeedBonus { get; init; } = 1.25f;
    public int MinSpawnDistance { get; init; } = 14;    // cells from the hero (relaxed only if the room is too crowded)
    public int[] EnemyCountMin { get; init; } = { 1, 2, 3, 4 };   // per level
    public int[] EnemyCountMax { get; init; } = { 5, 7, 9, 11 };

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

    /// <summary>Copy of this config adjusted for a difficulty level.</summary>
    public GameConfig WithDifficulty(Difficulty d)
    {
        var o = JsonSerializer.SerializeToNode(this, Json)!.AsObject();
        switch (d)
        {
            case Difficulty.Novice:
                o["EnemySpeedScale"] = EnemySpeedScale * 0.8f; o["EnemyShotCooldown"] = (int)(EnemyShotCooldown * 1.3f); o["StartLives"] = StartLives + 1; break;
            case Difficulty.Expert:
                o["EnemySpeedScale"] = EnemySpeedScale * 1.25f; o["EnemyShotCooldown"] = (int)(EnemyShotCooldown * 0.75f); o["StartLives"] = Math.Max(3, StartLives - 1); break;
        }
        return o.Deserialize<GameConfig>(Json)!;
    }

    public GameConfig WithGodMode(bool god)
    {
        var o = JsonSerializer.SerializeToNode(this, Json)!.AsObject();
        o["GodMode"] = god;
        return o.Deserialize<GameConfig>(Json)!;
    }

    public string ToJson() => JsonSerializer.Serialize(this, Json);
}
