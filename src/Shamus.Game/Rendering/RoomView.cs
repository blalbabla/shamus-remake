using System.Numerics;
using Raylib_cs;
using Shamus.Core.Mazes;

namespace Shamus.Game.Rendering;

/// <summary>
/// Draws the room itself: floor, darker "closed" areas, the room name on the floor, energy-wall beams between generator posts.
/// The same models are used for every camera; only the camera changes.
/// </summary>
public sealed class RoomView : IDisposable
{
    // neon beam colour per level (black/first level = red as in the concept sketch)
    public static readonly Color[] Neon =
    {
        new(255, 70, 25, 255), new(70, 130, 255, 255), new(70, 255, 110, 255), new(255, 60, 70, 255),
    };
    private static readonly Color FloorColor = new(34, 36, 46, 255), ClosedColor = new(18, 18, 26, 255);
    private static readonly Color PostBody = new(40, 66, 160, 255);

    private const float Ox = -RoomGrid.W / 2f, Oz = -RoomGrid.H / 2f;
    private static Vector3 P(float x, float y, float h) => new(Ox + x, h, Oz + y);
    private static readonly float[] BeamHeights = { 0.55f, 1.05f };

    private Font _font;
    private bool _fontLoaded;
    private readonly Dictionary<int, Texture2D> _titles = new();

    public RoomView()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "assets", "fonts", "BlackOpsOne-Regular.ttf");
        if (File.Exists(path))
        {
            _font = Raylib.LoadFontEx(path, 160, null, 0);
            Raylib.SetTextureFilter(_font.Texture, TextureFilter.Bilinear);
            _fontLoaded = true;
        }
    }

    private Texture2D Title(int room)
    {
        if (_titles.TryGetValue(room, out var t)) return t;
        var font = _fontLoaded ? _font : Raylib.GetFontDefault();
        var img = Raylib.ImageTextEx(font, $"ROOM {room}", 160, 4, Color.White);
        t = Raylib.LoadTextureFromImage(img);
        Raylib.UnloadImage(img);
        Raylib.SetTextureFilter(t, TextureFilter.Bilinear);
        _titles[room] = t;
        return t;
    }

    /// <summary>Opaque parts: floor, closed areas, title, generator posts and the beam cores.</summary>
    public void DrawSolid(RoomGrid grid, int room, int level, double time, Color? doorColor, int doorSide)
    {
        Raylib.DrawCube(new Vector3(0, -0.5f, 0), RoomGrid.W, 1, RoomGrid.H, FloorColor);

        // closed (solid) regions are a little darker than the floor
        for (int y = 0; y < RoomGrid.H; y++)
            for (int x = 0; x < RoomGrid.W;)
            {
                if (grid.KindAt(x, y) != RoomGrid.Thick) { x++; continue; }
                int x0 = x; while (x < RoomGrid.W && grid.KindAt(x, y) == RoomGrid.Thick) x++;
                Raylib.DrawCube(P((x0 + x) / 2f, y + 0.5f, 0.01f), x - x0, 0.02f, 1f, ClosedColor);
            }

        DrawTitle(room);

        var neon = Neon[Math.Clamp(level, 0, 3)];
        var walls = grid.Walls;
        float pulse = 0.85f + 0.15f * MathF.Sin((float)time * 3f);

        foreach (var (x, y) in walls.Posts)
        {
            var c = P(x, y, 0.8f);
            Raylib.DrawCube(c, 0.9f, 1.6f, 0.9f, PostBody);
            Raylib.DrawCubeWires(c, 0.9f, 1.6f, 0.9f, new Color(15, 25, 70, 255));
            Raylib.DrawCube(P(x, y, 1.65f), 0.55f, 0.12f, 0.55f, Brighten(neon, 0.6f + 0.4f * pulse));
        }

        int i = 0;
        foreach (var s in walls.Segments)
        {
            var col = IsDoor(s, doorSide) && doorColor is Color dc ? dc : neon;
            foreach (float h in BeamHeights)
            {
                Beam(s, h, 0.20f, Brighten(col, pulse), 1f);       // neon body
                Beam(s, h, 0.08f, Color.White, 1f);               // hot core
            }
            i++;
        }
    }

    /// <summary>Additive glow around the beams and posts; draw last, after all opaque geometry.</summary>
    public void DrawGlow(RoomGrid grid, int level, double time, Color? doorColor, int doorSide)
    {
        var neon = Neon[Math.Clamp(level, 0, 3)];
        var walls = grid.Walls;
        Rlgl.DisableDepthMask();
        Raylib.BeginBlendMode(BlendMode.Additive);
        int i = 0;
        foreach (var s in walls.Segments)
        {
            var col = IsDoor(s, doorSide) && doorColor is Color dc ? dc : neon;
            float flick = 0.75f + 0.25f * MathF.Sin((float)time * 5f + i * 1.7f);
            var glow = new Color(col.R, col.G, col.B, (byte)(70 * flick));
            foreach (float h in BeamHeights) Beam(s, h, 0.65f, glow, 1f);
            i++;
        }
        foreach (var (x, y) in walls.Posts)
            Raylib.DrawSphere(P(x, y, 1.75f), 0.55f, new Color(neon.R, neon.G, neon.B, (byte)60));
        Raylib.EndBlendMode();
        Rlgl.EnableDepthMask();
    }

    private static bool IsDoor(WallSegment s, int doorSide)
    {
        if (doorSide == 0 || s.IsHorizontal) return false;
        float x = doorSide < 0 ? 0.5f : RoomGrid.W - 0.5f;
        return MathF.Abs(s.X0 - x) < 0.01f && MathF.Min(s.Y0, s.Y1) >= RoomGrid.DoorY0 - 1.01f && MathF.Max(s.Y0, s.Y1) <= RoomGrid.DoorY1 + 1.01f;
    }

    private static Color Brighten(Color c, float f) => new((byte)Math.Min(255, c.R * f), (byte)Math.Min(255, c.G * f), (byte)Math.Min(255, c.B * f), c.A);

    private static void Beam(WallSegment s, float height, float thickness, Color color, float _)
    {
        float cx = (s.X0 + s.X1) / 2f, cy = (s.Y0 + s.Y1) / 2f;
        float lx = MathF.Abs(s.X1 - s.X0), ly = MathF.Abs(s.Y1 - s.Y0);
        Raylib.DrawCube(P(cx, cy, height), MathF.Max(lx, thickness), thickness, MathF.Max(ly, thickness), color);
    }

    private void DrawTitle(int room)
    {
        var tex = Title(room);
        float h = 3.2f, w = h * tex.Width / tex.Height;
        if (w > 16f) { w = 16f; h = w * tex.Height / tex.Width; }
        float cx = Ox + RoomGrid.W / 2f, cz = Oz + RoomGrid.H / 2f, y = 0.04f;
        Rlgl.SetTexture(tex.Id);
        Rlgl.Begin(DrawMode.Quads);
        Rlgl.Color4ub(255, 170, 25, 210);
        Rlgl.Normal3f(0, 1, 0);
        Rlgl.TexCoord2f(0, 0); Rlgl.Vertex3f(cx - w / 2, y, cz - h / 2);
        Rlgl.TexCoord2f(0, 1); Rlgl.Vertex3f(cx - w / 2, y, cz + h / 2);
        Rlgl.TexCoord2f(1, 1); Rlgl.Vertex3f(cx + w / 2, y, cz + h / 2);
        Rlgl.TexCoord2f(1, 0); Rlgl.Vertex3f(cx + w / 2, y, cz - h / 2);
        Rlgl.End();
        Rlgl.SetTexture(0);
    }

    public void Dispose()
    {
        foreach (var t in _titles.Values) Raylib.UnloadTexture(t);
        _titles.Clear();
        if (_fontLoaded) Raylib.UnloadFont(_font);
    }
}
