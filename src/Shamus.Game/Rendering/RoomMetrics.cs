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

    /// <summary>Pickups hover / stand at most this high.</summary>
    public const float PropHeight = EnemyHeight;

    /// <summary>
    /// Bullets fly inside the wall band, not above it: a shot stopped by a wall has to be drawn hitting the panel,
    /// and now that the walls are 0.5 high a bullet at the old 0.86 would look like it flies over them.
    /// </summary>
    public const float BulletY = 0.35f;

    /// <summary>The Shadow deliberately towers over the walls: it should feel like it does not belong to the room.</summary>
    public const float ShadowHeight = 1.45f;
}
