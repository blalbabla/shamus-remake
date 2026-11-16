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
    public int ModeLeft;      // ticks until the next behaviour decision
    public float Offset;      // angle offset from "straight at player" (circling / retreating)
    public int DirX, DirY;    // current axis direction (robo droid)
    public float JumpX, JumpY; // snap jumper: unit vector of the next jump
    /// <summary>
    /// Hull heading in degrees (90 = +x, 0 = +y), eased in place like the player's tank so a robo-droid swings
    /// around its turn instead of snapping. Only the rendering reads it; movement stays axis-based.
    /// </summary>
    public float Heading;
    /// <summary>Heading before this tick, for render interpolation.</summary>
    public float PrevHeading;
    public bool CanShoot = true;
    public bool Alive = true;
    /// <summary>
    /// Palette slot the renderer paints this enemy with. Rolled at spawn so a room shows a mix of colours, exactly
    /// like the original, where the paint said nothing about behaviour. The kind is read from the shape, not the colour.
    /// </summary>
    public int ColorIndex;
}
