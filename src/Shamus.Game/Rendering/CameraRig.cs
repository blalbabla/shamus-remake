using System.Numerics;
using Raylib_cs;
using Shamus.Core.Mazes;

namespace Shamus.Game.Rendering;

public enum ViewMode { Top, Isometric }

/// <summary>
/// One orthographic camera with two presets: straight down, and true isometric (parallel projection).
/// The scene is drawn identically in both; switching animates the viewing direction.
/// </summary>
public sealed class CameraRig
{
    private const float Distance = 60f;
    private const float TopHeight = RoomGrid.W * 0.62f;   // ortho view height in world units
    private const float IsoHeight = 33f;
    private static readonly Vector3 TopDir = Vector3.Normalize(new Vector3(0, 1, 0.0005f));      // camera offset direction (towards the camera)
    private static readonly Vector3 IsoDir = Vector3.Normalize(new Vector3(-1, 1, 1));
    private static readonly Vector3 TopUp = new(0, 0, -1), IsoUp = new(0, 1, 0);
    private static readonly Vector3 IsoTarget = new(0, 0.4f, 0);

    private float _t;   // 0 = top, 1 = isometric
    public ViewMode Mode { get; private set; }
    /// <summary>Debug: zoom factor and a point (room cell coordinates) to centre on, for close-up screenshots.</summary>
    public float Zoom { get; set; } = 1f;
    public Vector3 Look { get; set; }
    public Camera3D Camera { get; private set; }

    public CameraRig(ViewMode mode) { SetMode(mode, instant: true); }

    public void Toggle() => SetMode(Mode == ViewMode.Top ? ViewMode.Isometric : ViewMode.Top);

    public void SetMode(ViewMode mode, bool instant = false)
    {
        Mode = mode;
        if (instant) _t = mode == ViewMode.Isometric ? 1f : 0f;
        Rebuild();
    }

    public void Update(float dt)
    {
        float goal = Mode == ViewMode.Isometric ? 1f : 0f;
        if (MathF.Abs(_t - goal) > 1e-4f)
        {
            float step = dt / 0.35f;
            _t = _t < goal ? MathF.Min(goal, _t + step) : MathF.Max(goal, _t - step);
        }
        Rebuild();
    }

    private void Rebuild()
    {
        float s = _t * _t * (3 - 2 * _t); // smoothstep
        var dir = Vector3.Normalize(Vector3.Lerp(TopDir, IsoDir, s));
        var target = Vector3.Lerp(Vector3.Zero, IsoTarget, s) + Look;
        Camera = new Camera3D
        {
            Position = target + dir * Distance,
            Target = target,
            Up = Vector3.Normalize(Vector3.Lerp(TopUp, IsoUp, s)),
            FovY = (TopHeight + (IsoHeight - TopHeight) * s) / Zoom,
            Projection = CameraProjection.Orthographic,
        };
    }
}
