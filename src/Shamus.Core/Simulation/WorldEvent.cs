namespace Shamus.Core.Simulation;

public enum WorldEventKind { Shot, BulletHitWall, BulletHitBarrier, PodOpened, RoomEntered, PlayerDied, GameOver, EnemyKilled, EnemyShot, RoomCleared, BulletsCancelled, KeyPicked, DoorOpened, PotionPicked, BonusPicked, ShadowWarning, ShadowAppeared, BulletHitShadow, ShadowKilled, CycleStarted }

public readonly record struct WorldEvent(WorldEventKind Kind, float X = 0, float Y = 0, int Value = 0);
