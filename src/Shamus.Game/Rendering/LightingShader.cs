using System.Numerics;
using Raylib_cs;

namespace Shamus.Game.Rendering;

/// <summary>
/// One directional light plus ambient for Raylib's immediate-mode primitives, with a shadow map.
/// Raylib's primitives carry per-vertex normals, so the fragment shader does plain Lambert shading and samples
/// the depth map with a small PCF kernel. Translucent force fields and 2D UI are drawn outside the shader.
/// If the shader cannot be built, <see cref="IsAvailable"/> is false and everything stays unlit.
/// </summary>
public sealed class LightingShader : IDisposable
{
    private const string Vertex = """
        #version 330
        in vec3 vertexPosition;
        in vec2 vertexTexCoord;
        in vec3 vertexNormal;
        in vec4 vertexColor;
        uniform mat4 mvp;
        out vec3 fragPos;
        out vec2 fragTexCoord;
        out vec3 fragNormal;
        out vec4 fragColor;
        void main()
        {
            fragPos = vertexPosition;
            fragTexCoord = vertexTexCoord;
            fragNormal = vertexNormal;
            fragColor = vertexColor;
            gl_Position = mvp * vec4(vertexPosition, 1.0);
        }
        """;

    private const string Fragment = """
        #version 330
        in vec3 fragPos;
        in vec2 fragTexCoord;
        in vec3 fragNormal;
        in vec4 fragColor;
        uniform sampler2D texture0;
        uniform vec4 colDiffuse;
        uniform vec3 lightDir;   // unit vector pointing toward the light
        uniform float ambient;
        uniform float diffuse;
        uniform sampler2D shadowMap;
        uniform mat4 lightVP;
        uniform int shadows;     // 1 = sample the shadow map
        uniform float shadowTexel;
        out vec4 finalColor;

        // 1.0 = lit, 0.0 = fully in shadow. PCF over a 3x3 kernel for soft edges.
        float shadowFactor(vec3 n)
        {
            if (shadows == 0) return 1.0;
            float ndl = dot(n, normalize(lightDir));
            if (ndl <= 0.0) return 1.0; // faces turned away are already dark
            vec4 lp = lightVP * vec4(fragPos + n * 0.02, 1.0);
            vec3 pc = lp.xyz / lp.w * 0.5 + 0.5;
            if (pc.x < 0.0 || pc.x > 1.0 || pc.y < 0.0 || pc.y > 1.0 || pc.z > 1.0) return 1.0;
            float bias = 0.0006 + 0.0016 * (1.0 - ndl);
            float lit = 0.0;
            for (int x = -1; x <= 1; x++)
                for (int y = -1; y <= 1; y++)
                {
                    float d = texture(shadowMap, pc.xy + vec2(x, y) * shadowTexel * 1.3).r;
                    lit += pc.z - bias > d ? 0.0 : 1.0;
                }
            return lit / 9.0;
        }

        void main()
        {
            vec4 base = texture(texture0, fragTexCoord) * colDiffuse * fragColor;
            vec3 n = normalize(fragNormal);
            if (dot(n, n) < 0.5)
            {
                finalColor = base;   // lines and other normal-less geometry stay unlit
                return;
            }

            vec3 L = normalize(lightDir);
            float sh = shadowFactor(n);
            float ndl = max(dot(n, L), 0.0);
            vec3 col = base.rgb * (ambient + diffuse * ndl * sh);
            finalColor = vec4(col, base.a);
        }
        """;

    private readonly Shader _shader;
    private readonly int _locLightDir, _locAmbient, _locDiffuse, _locColDiffuse,
        _locShadowMap, _locLightVP, _locShadows, _locShadowTexel;
    private ShadowMap? _shadow;

    public bool IsAvailable { get; }

    /// <summary>Direction toward the light in world space: above the scene, to its left and front.</summary>
    public Vector3 LightDirection { get; set; } = Vector3.Normalize(new Vector3(-1f, 1f, -1f));
    public float Ambient { get; set; } = 0.55f;
    public float Diffuse { get; set; } = 0.65f;

    public LightingShader()
    {
        _shader = Raylib.LoadShaderFromMemory(Vertex, Fragment);
        IsAvailable = Raylib.IsShaderValid(_shader);
        if (!IsAvailable) return;
        _locLightDir = Raylib.GetShaderLocation(_shader, "lightDir");
        _locAmbient = Raylib.GetShaderLocation(_shader, "ambient");
        _locDiffuse = Raylib.GetShaderLocation(_shader, "diffuse");
        _locColDiffuse = Raylib.GetShaderLocation(_shader, "colDiffuse");
        _locShadowMap = Raylib.GetShaderLocation(_shader, "shadowMap");
        _locLightVP = Raylib.GetShaderLocation(_shader, "lightVP");
        _locShadows = Raylib.GetShaderLocation(_shader, "shadows");
        _locShadowTexel = Raylib.GetShaderLocation(_shader, "shadowTexel");
    }

    /// <summary>Shadow map rendered this frame, or null to draw without shadows.</summary>
    public void UseShadows(ShadowMap? map) => _shadow = map is { IsValid: true } ? map : null;

    /// <summary>Binds the shadow depth texture through raylib's texture-id sampler convention.</summary>
    private void BindShadowTexture()
    {
        if (_shadow is null) return;
        Raylib.SetShaderValueTexture(_shader, _locShadowMap,
            new Texture2D { Id = _shadow.DepthTextureId, Width = ShadowMap.MapSize, Height = ShadowMap.MapSize, Mipmaps = 1 });
    }

    public void Begin()
    {
        if (!IsAvailable) return;
        Raylib.BeginShaderMode(_shader);
        Raylib.SetShaderValue(_shader, _locLightDir, Vector3.Normalize(LightDirection), ShaderUniformDataType.Vec3);
        Raylib.SetShaderValue(_shader, _locAmbient, Ambient, ShaderUniformDataType.Float);
        Raylib.SetShaderValue(_shader, _locDiffuse, Diffuse, ShaderUniformDataType.Float);
        Raylib.SetShaderValue(_shader, _locColDiffuse, new Vector4(1f, 1f, 1f, 1f), ShaderUniformDataType.Vec4);
        Raylib.SetShaderValue(_shader, _locShadows, _shadow is null ? 0 : 1, ShaderUniformDataType.Int);
        if (_shadow is not null)
        {
            Raylib.SetShaderValueMatrix(_shader, _locLightVP, _shadow.LightViewProjection);
            Raylib.SetShaderValue(_shader, _locShadowTexel, 1f / ShadowMap.MapSize, ShaderUniformDataType.Float);
            BindShadowTexture();
        }
    }

    public void End()
    {
        if (IsAvailable) Raylib.EndShaderMode();
    }

    public void Dispose()
    {
        if (IsAvailable) Raylib.UnloadShader(_shader);
    }
}
