namespace Shamus.Core.Simulation;

public enum EnemyKind { RoboDroid, SpiralDrone, SnapJumper }

public sealed class Enemy
{
    public EnemyKind Kind;
    public float X, Y;
    public float Phase;       // spiral phase / snap timer
    public int Timer;         // generic tick counter
    public int DashLeft;      // snap jumper dash ticks left
    public float DashX, DashY;
    public int ShotCooldown;
    public bool Alive = true;
}
