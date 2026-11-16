namespace Shamus.Game.Rendering;

/// <summary>
/// One scale for everything that stands in a room. One unit equals one room cell
/// (<see cref="Shamus.Core.Mazes.RoomGrid.W"/> x <see cref="Shamus.Core.Mazes.RoomGrid.H"/> cells fill the floor).
/// Heights are measured from the floor plane (y = 0) and all of them are expressed through
/// <see cref="WallHeight"/>, so the walls stay the reference: the tank, the enemies and the pickups
/// fit under the top of an energy wall instead of poking through it.
/// </summary>
public static class RoomMetrics
{
    /// <summary>Top of the floor slab. Everything stands on this plane.</summary>
    public const float Floor = 0f;

    /// <summary>Energy-wall height; also the generator-post height. The reference for every other height.</summary>
    public const float WallHeight = 1.0f;

    /// <summary>Generator post footprint (square side). Walls start at the post faces, see <c>RoomView.PostHalf</c>.</summary>
    public const float PostSize = 0.9f;

    /// <summary>Pod-room barriers use the same band as the energy walls.</summary>
    public const float PodBarrierHeight = WallHeight;

    /// <summary>Tank, dome and barrel included. Slightly lower than the walls, so it reads as standing inside.</summary>
    public const float PlayerHeight = 0.92f;

    /// <summary>Tallest enemy, dome/head included.</summary>
    public const float EnemyHeight = 0.96f;

    /// <summary>Pickups hover / stand at most this high.</summary>
    public const float PropHeight = EnemyHeight;

    /// <summary>Bullets fly above the pickups and the tank, below the wall tops.</summary>
    public const float BulletY = 0.86f;

    /// <summary>The Shadow deliberately towers over the walls: it should feel like it does not belong to the room.</summary>
    public const float ShadowHeight = 1.45f;
}
