namespace Shamus.Core.Simulation;

public enum WorldEventKind { Shot, BulletHitWall, BulletHitBarrier, PodOpened, RoomEntered, PlayerDied, GameOver }

public readonly record struct WorldEvent(WorldEventKind Kind, float X = 0, float Y = 0, int Value = 0);
