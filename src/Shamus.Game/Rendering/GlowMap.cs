using System.Numerics;
using Raylib_cs;

namespace Shamus.Game.Rendering;

/// <summary>
/// Cheap bloom for the emissive parts of the scene (energy fields, generator caps, bullets, pod barriers).
/// Three steps: the emitters are rendered into a small offscreen target, the target is blurred by two separable
/// gaussian passes, and the result is added over the finished 3D scene. The emitter pass has no geometry to test
/// depth against, so it is an approximation: a glow is drawn even when its source stands behind a wall.
/// </summary>
public sealed class GlowMap : IDisposable
{
    /// <summary>The target is the 1280x720 design frame divided by this.</summary>
    private const int Downscale = 4;

    private const string Fragment = """
        #version 330
        in vec2 fragTexCoord;
        in vec4 fragColor;
        uniform sampler2D texture0;
        uniform vec4 colDiffuse;
        uniform vec2 texel;   // 1 / target size
        uniform vec2 dir;     // (1,0) horizontal or (0,1) vertical
        out vec4 finalColor;

        // 9-tap gaussian, weights of a standard [_1 4 6 4 1] kernel
        void main()
        {
            vec2 s = texel * dir;
            vec3 c = texture(texture0, fragTexCoord).rgb * 0.227027;
            c += (texture(texture0, fragTexCoord + s).rgb + texture(texture0, fragTexCoord - s).rgb) * 0.194595;
            c += (texture(texture0, fragTexCoord + s * 2.0).rgb + texture(texture0, fragTexCoord - s * 2.0).rgb) * 0.121622;
            c += (texture(texture0, fragTexCoord + s * 3.0).rgb + texture(texture0, fragTexCoord - s * 3.0).rgb) * 0.054054;
            c += (texture(texture0, fragTexCoord + s * 4.0).rgb + texture(texture0, fragTexCoord - s * 4.0).rgb) * 0.016216;
            finalColor = vec4(c, 1.0);
        }
        """;

    private readonly int _width, _height;
    private readonly RenderTexture2D _emitters, _blurred;
    private readonly Shader _shader;
    private readonly int _locTexel, _locDir;

    public bool IsValid { get; }

    /// <summary>How much of the blurred emitters is added to the frame (0 = off, 1 = full).</summary>
    public float Strength { get; set; } = 0.55f;

    public GlowMap()
    {
        _width = SceneRenderer.VirtualWidth / Downscale;
        _height = SceneRenderer.VirtualHeight / Downscale;
        _emitters = Raylib.LoadRenderTexture(_width, _height);
        _blurred = Raylib.LoadRenderTexture(_width, _height);
        IsValid = _emitters.Texture.Id != 0 && _blurred.Texture.Id != 0;
        if (!IsValid) return;
        Raylib.SetTextureFilter(_emitters.Texture, TextureFilter.Bilinear);
        Raylib.SetTextureFilter(_blurred.Texture, TextureFilter.Bilinear);

        _shader = Raylib.LoadShaderFromMemory(null, Fragment);   // raylib's default vertex shader
        IsValid = Raylib.IsShaderValid(_shader);
        if (!IsValid) return;
        _locTexel = Raylib.GetShaderLocation(_shader, "texel");
        _locDir = Raylib.GetShaderLocation(_shader, "dir");
    }

    /// <summary>Binds the emitter target and clears it to black; draw the emissive geometry, then call <see cref="EndEmitters"/>.</summary>
    public void BeginEmitters()
    {
        if (!IsValid) return;
        Rlgl.DrawRenderBatchActive();
        Raylib.BeginTextureMode(_emitters);
        Raylib.ClearBackground(Color.Black);
    }

    /// <summary>Unbinds the emitter target and blurs it in place (into <see cref="_blurred"/> and back).</summary>
    public void EndEmitters()
    {
        if (!IsValid) return;
        Raylib.EndTextureMode();
        Rlgl.DrawRenderBatchActive();
        BlurPass(_emitters.Texture, horizontal: true);    // emitters -> blurred
        BlurPass(_blurred.Texture, horizontal: false);    // blurred -> emitters
    }

    private void BlurPass(Texture2D source, bool horizontal)
    {
        Raylib.BeginTextureMode(horizontal ? _blurred : _emitters);
        Raylib.BeginShaderMode(_shader);
        Raylib.SetShaderValue(_shader, _locTexel, new Vector2(1f / _width, 1f / _height), ShaderUniformDataType.Vec2);
        Raylib.SetShaderValue(_shader, _locDir, horizontal ? Vector2.UnitX : Vector2.UnitY, ShaderUniformDataType.Vec2);
        Blit(source);
        Raylib.EndShaderMode();
        Raylib.EndTextureMode();
    }

    /// <summary>Adds the blurred emitters over the current 2D frame (the 1280x720 design space must be active).</summary>
    public void Composite()
    {
        if (!IsValid || Strength <= 0.001f) return;
        var tint = new Color((byte)255, (byte)255, (byte)255, (byte)Math.Clamp((int)(Strength * 255f), 0, 255));
        Raylib.BeginBlendMode(BlendMode.Additive);
        Raylib.DrawTexturePro(_emitters.Texture,
            new Rectangle(0, 0, _width, -_height),                                  // render textures come out flipped
            new Rectangle(0, 0, SceneRenderer.VirtualWidth, SceneRenderer.VirtualHeight),
            Vector2.Zero, 0f, tint);
        Raylib.EndBlendMode();
    }

    /// <summary>Draws a render-texture into the whole bound target, unflipped so the blur passes stay upright.</summary>
    private void Blit(Texture2D source) =>
        Raylib.DrawTexturePro(source, new Rectangle(0, 0, _width, -_height), new Rectangle(0, 0, _width, _height), Vector2.Zero, 0f, Color.White);

    public void Dispose()
    {
        if (IsShaderValid(_shader)) Raylib.UnloadShader(_shader);
        if (_emitters.Texture.Id != 0) Raylib.UnloadRenderTexture(_emitters);
        if (_blurred.Texture.Id != 0) Raylib.UnloadRenderTexture(_blurred);
    }

    private static bool IsShaderValid(Shader s) => s.Id != 0;
}
