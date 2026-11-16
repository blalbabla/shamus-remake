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
    /// <summary>The game always shows a 16:9 view; the window letterboxes it instead of changing the camera.</summary>
    public const float Aspect = 16f / 9f;
    private static readonly Vector3 TopDir = Vector3.Normalize(new Vector3(0, 1, 0.0005f));      // camera offset direction (towards the camera)
    private static readonly Vector3 IsoDir = Vector3.Normalize(new Vector3(-1, 1, 1));
    private static readonly Vector3 TopUp = new(0, 0, -1), IsoUp = new(0, 1, 0);
    private static readonly Vector3 IsoTarget = new(0, 0.4f, 0);

    private float _t;   // 0 = top, 1 = isometric
    public ViewMode Mode { get; private set; }
    /// <summary>Smoothed view blend: 0 = top, 1 = isometric. Items use it to stand upright / lie flat with the camera.</summary>
    public float IsometricBlend { get; private set; }
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
        IsometricBlend = s;
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

    /// <summary>
    /// Screen directions projected onto the floor plane: +X of <c>Right</c> is "screen right", +Y of <c>Down</c>
    /// is "screen down". Normalized, so the stick always means the same thing on screen in both views.
    /// </summary>
    public (Vector2 Right, Vector2 Down) ScreenBasis()
    {
        var forward = Vector3.Normalize(Camera.Target - Camera.Position);
        var right = Vector3.Normalize(Vector3.Cross(forward, Camera.Up));
        var up = Vector3.Cross(right, forward);
        var r = new Vector2(right.X, right.Z);
        var u = new Vector2(up.X, up.Z);
        if (r.LengthSquared() > 1e-6f) r = Vector2.Normalize(r);
        if (u.LengthSquared() > 1e-6f) u = Vector2.Normalize(u);
        return (r, -u);
    }

    /// <summary>
    /// Converts screen-relative input (x right, y down, each -1/0/1) into room-axis signs. In the top view this is
    /// the identity; in isometric view "screen right" becomes a floor diagonal and "screen up-right" becomes +X.
    /// The basis follows the animated camera, so a held direction steers smoothly through a view toggle.
    /// </summary>
    public (int X, int Y) ScreenToRoom(int screenX, int screenY)
    {
        var (right, down) = ScreenBasis();
        float wx = right.X * screenX + down.X * screenY;
        float wz = right.Y * screenX + down.Y * screenY;
        return (MathF.Abs(wx) < 0.25f ? 0 : Math.Sign(wx), MathF.Abs(wz) < 0.25f ? 0 : Math.Sign(wz));
    }

    /// <summary>
    /// Starts the 3D pass with a fixed 16:9 orthographic projection. Raylib's BeginMode3D derives the aspect
    /// from the window, which would squeeze the scene in a non-16:9 window; this keeps the view constant.
    /// </summary>
    public void BeginView()
    {
        Rlgl.DrawRenderBatchActive();
        Rlgl.MatrixMode(MatrixMode.Projection);
        Rlgl.PushMatrix();
        Rlgl.LoadIdentity();
        float halfH = Camera.FovY / 2f, halfW = halfH * Aspect;
        Rlgl.MultMatrixf(Raymath.MatrixOrtho(-halfW, halfW, -halfH, halfH, 0.01, 1000.0));
        Rlgl.MatrixMode(MatrixMode.ModelView);
        Rlgl.LoadIdentity();
        Rlgl.MultMatrixf(Raymath.MatrixLookAt(Camera.Position, Camera.Target, Camera.Up));
        Rlgl.EnableDepthTest();
    }

    public void EndView()
    {
        Rlgl.DrawRenderBatchActive();
        Rlgl.MatrixMode(MatrixMode.ModelView);
        Rlgl.LoadIdentity();
        Rlgl.MatrixMode(MatrixMode.Projection);
        Rlgl.PopMatrix();
        Rlgl.DisableDepthTest();
    }
}
