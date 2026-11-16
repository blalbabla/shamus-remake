namespace Shamus.Game.Rendering;

/// <summary>
/// One scale for everything that stands in a room. One unit equals one room cell
/// (<see cref="Shamus.Core.Mazes.RoomGrid.W"/> x <see cref="Shamus.Core.Mazes.RoomGrid.H"/> cells fill the floor).
/// Heights are measured from the floor plane (y = 0).
/// The wall band is deliberately lower than the tank and the enemies: the energy panels are translucent but in the
/// isometric view a panel as tall as a character still hides it, so walls stay below the characters' shoulders.
/// </summary>
public static class RoomMetrics
{
    /// <summary>Top of the floor slab. Everything stands on this plane.</summary>
    public const float Floor = 0f;

    /// <summary>
    /// Energy-wall height; also the generator-post height. Half of the original 1.0 (one room cell) on purpose:
    /// at 1.0 the walls covered the tank and the enemies in the isometric view.
    /// </summary>
    public const float WallHeight = 0.5f;

    /// <summary>Generator post footprint (square side). Walls start at the post faces, see <c>RoomView.PostHalf</c>.</summary>
    public const float PostSize = 0.9f;

    /// <summary>Pod-room barriers use the same band as the energy walls.</summary>
    public const float PodBarrierHeight = WallHeight;

    /// <summary>
    /// Tank, dome and barrel included. The hull and everything above it sit half a track height lower than the
    /// track tops would suggest, so the tracks flank the hull instead of carrying it like a box on skis.
    /// </summary>
    public const float PlayerHeight = 0.73f;

    /// <summary>Tank track (caterpillar) height; it also sets how far the hull drops below the track tops.</summary>
    public const float TrackHeight = 0.34f;

    /// <summary>
    /// Barrel centre height: the gun sits at the dome centre, so it drops with the hull. Shells fly at this height,
    /// which is also where <c>GameConfig.MuzzleLength</c> puts them along the barrel.
    /// </summary>
    public const float BarrelY = 0.70f - TrackHeight / 2f;

    /// <summary>Tallest enemy, dome/head included.</summary>
    public const float EnemyHeight = 0.96f;

    /// <summary>Hover height of a flat pickup (the key); pickups float, they do not lie on the floor.</summary>
    public const float PropHover = 0.45f;

    /// <summary>The keyhole plate floats lower than the key: it belongs to the room rather than to the loot.</summary>
    public const float KeyholeHover = 0.24f;

    /// <summary>Centre height of a standing pickup (the plus and the question mark) in the isometric view.</summary>
    public const float StandingPropHover = 0.95f;

    /// <summary>
    /// Height of the three vertical slits in a spiral drone's tower. Their bolts leave from here, so a drone's shells
    /// visibly come out of the gun ports instead of its middle.
    /// </summary>
    public const float DroneSlitY = 0.69f;

    /// <summary>
    /// Height of the dark groove that runs around a robo-droid's turret. Its bolts leave from this band and the red
    /// beacon runs along it, so the drum is the gun port.
    /// </summary>
    public const float RobotMuzzleY = 0.57f;

    /// <summary>
    /// Radius of a robo-droid's turret drum. It overhangs the hull and reaches out over the tracks, while the tracks
    /// still show on both sides, so the machine reads as a tracked vehicle and not as a drum on skis.
    /// </summary>
    public const float RobotTurretRadius = 0.44f;

    /// <summary>The Shadow deliberately towers over the walls: it should feel like it does not belong to the room.</summary>
    public const float ShadowHeight = 1.45f;
}
