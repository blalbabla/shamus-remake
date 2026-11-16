using System.Numerics;
using Raylib_cs;

namespace Shamus.Game.Rendering;

/// <summary>
/// Directional-light shadow map: a depth-only framebuffer rendered from an orthographic camera placed along the
/// light direction. The room is small and flat, so a fixed 1024 map and a box fitted around the whole level suffice.
/// </summary>
public sealed class ShadowMap : IDisposable
{
    public const int MapSize = 1024;
    private const float LightDistance = 60f;
    private const float NearPlane = 1f;
    private const float FarPlane = 160f;
    private const float HalfExtent = 27f;

    private readonly uint _fbo;
    private readonly uint _depthTex;
    private readonly RenderTexture2D _target;

    public bool IsValid { get; }

    /// <summary>OpenGL id of the depth texture.</summary>
    public uint DepthTextureId => _depthTex;

    /// <summary>World-to-light-clip transform, valid after <see cref="Begin"/>.</summary>
    public Matrix4x4 LightViewProjection { get; private set; } = Matrix4x4.Identity;

    public ShadowMap()
    {
        _fbo = Rlgl.LoadFramebuffer();
        if (_fbo == 0) return;

        _depthTex = Rlgl.LoadTextureDepth(MapSize, MapSize, false);
        Rlgl.FramebufferAttach(_fbo, _depthTex, FramebufferAttachType.Depth, FramebufferAttachTextureType.Texture2D, 0);
        IsValid = Rlgl.FramebufferComplete(_fbo);

        _target = new RenderTexture2D
        {
            Id = _fbo,
            Texture = new Texture2D { Id = 0, Width = MapSize, Height = MapSize, Mipmaps = 1 },
        };
    }

    /// <summary>Starts the depth pass; draw the shadow casters, then call <see cref="End"/>.</summary>
    public void Begin(Vector3 lightDirection)
    {
        var center = new Vector3(0f, 0.4f, 0f);
        var dir = Vector3.Normalize(lightDirection);
        var eye = center + dir * LightDistance;
        var view = Raymath.MatrixLookAt(eye, center, Vector3.UnitY);
        var proj = Raymath.MatrixOrtho(-HalfExtent, HalfExtent, -HalfExtent, HalfExtent, NearPlane, FarPlane);

        Rlgl.DrawRenderBatchActive();
        Raylib.BeginTextureMode(_target);
        Raylib.ClearBackground(Color.White);
        Rlgl.SetClipPlanes(NearPlane, FarPlane);
        Rlgl.EnableDepthTest();
        Rlgl.EnableDepthMask();

        Rlgl.MatrixMode(MatrixMode.Projection);
        Rlgl.LoadIdentity();
        Rlgl.MultMatrixf(proj);
        Rlgl.MatrixMode(MatrixMode.ModelView);
        Rlgl.LoadIdentity();
        Rlgl.MultMatrixf(view);

        // Raylib stores matrices column-vector style: its P x MV product is Numerics Multiply(P, MV).
        LightViewProjection = Matrix4x4.Multiply(Rlgl.GetMatrixProjection(), Rlgl.GetMatrixModelview());
    }

    public void End()
    {
        Rlgl.DrawRenderBatchActive();
        Raylib.EndTextureMode();   // restores the window viewport and 2D matrices
        Rlgl.SetClipPlanes(0.01, 1000.0);
        Rlgl.DisableDepthTest();
        Rlgl.EnableDepthMask();
    }

    public void Dispose()
    {
        if (_depthTex != 0) Rlgl.UnloadTexture(_depthTex);
        if (_fbo != 0) Rlgl.UnloadFramebuffer(_fbo);
    }
}
