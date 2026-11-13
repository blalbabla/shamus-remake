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
    private static readonly Color PostBody = new(46, 88, 235, 255), PostTop = new(110, 160, 255, 255), PostEdge = new(20, 40, 130, 255);

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
        Outline(ref img, Math.Max(5, img.Width / 70));
        t = Raylib.LoadTextureFromImage(img);
        Raylib.UnloadImage(img);
        Raylib.SetTextureFilter(t, TextureFilter.Bilinear);
        _titles[room] = t;
        return t;
    }

    /// <summary>Replaces the glyph fill by an outline of the given stroke width (pixels), in place.</summary>
    private static unsafe void Outline(ref Image img, int stroke)
    {
        Raylib.ImageFormat(ref img, PixelFormat.UncompressedR8G8B8A8);
        int w = img.Width, h = img.Height;
        var px = (Color*)img.Data;
        var mask = new bool[w * h];
        for (int i = 0; i < mask.Length; i++) mask[i] = px[i].A > 128;
        // erode with a square window (separable min), then outline = mask minus eroded mask
        var tmp = new bool[w * h]; var eroded = new bool[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                bool all = mask[y * w + x];
                for (int k = -stroke; k <= stroke && all; k++) { int xx = x + k; if (xx < 0 || xx >= w || !mask[y * w + xx]) all = false; }
                tmp[y * w + x] = all;
            }
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                bool all = tmp[y * w + x];
                for (int k = -stroke; k <= stroke && all; k++) { int yy = y + k; if (yy < 0 || yy >= h || !tmp[yy * w + x]) all = false; }
                eroded[y * w + x] = all;
            }
        var line = new Color((byte)200, (byte)130, (byte)25, (byte)255);
        for (int i = 0; i < mask.Length; i++) px[i] = mask[i] && !eroded[i] ? line : new Color((byte)0, (byte)0, (byte)0, (byte)0);
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
            Raylib.DrawCube(P(x, y, 1.62f), 0.9f, 0.04f, 0.9f, PostTop);                       // solid lighter top face
            //Raylib.DrawCubeWires(c, 0.9f, 1.6f, 0.9f, PostEdge);
            Raylib.DrawCube(P(x, y, 1.68f), 0.4f, 0.1f, 0.4f, Brighten(neon, 0.75f + 0.25f * pulse));   // solid neon cap
        }

        foreach (var s in walls.Segments)
        {
            var col = IsDoor(s, doorSide) && doorColor is Color dc ? dc : neon;
            foreach (float h in BeamHeights)
                Beam(s, h, 0.20f, Brighten(col, pulse), 1f);      // pass 1: bright central beam
        }
    }

    /// <summary>
    /// Beam passes 2 and 3: a wider semi-transparent body and a very wide, partially transparent halo.
    /// Drawn additively with depth writes off (after flushing the opaque batch) so the light does not occlude solids.
    /// </summary>
    public void DrawBeamShells(RoomGrid grid, int level, Color? doorColor, int doorSide)
    {
        var neon = Neon[Math.Clamp(level, 0, 3)];
        var walls = grid.Walls;
        // Flush the queued opaque geometry (posts, beams, floor) while depth writes are still on.
        // DisableDepthMask only changes GL state and does not flush raylib's batch: without this call
        // the posts would be rasterised with depth writes off, so the glow would draw over them.
        Rlgl.DrawRenderBatchActive();
        Rlgl.DisableDepthMask();
        Raylib.BeginBlendMode(BlendMode.Additive);
        foreach (var s in walls.Segments)
        {
            var col = IsDoor(s, doorSide) && doorColor is Color dc ? dc : neon;
            var body = new Color(col.R, col.G, col.B, (byte)150);
            var halo = new Color(col.R, col.G, col.B, (byte)40);
            foreach (float h in BeamHeights)
            {
                Beam(s, h, 0.32f, body, 1f);   // pass 2: less bright, thicker, semi-transparent
                Beam(s, h, 0.40f, halo, 1f);   // pass 3: thick, almost transparent
            }
        }
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

    private const float PostHalf = 0.42f;   // beams start at the side faces of the generators, not at their centres

    private static void Beam(WallSegment s, float height, float thickness, Color color, float _)
    {
        float cx = (s.X0 + s.X1) / 2f, cy = (s.Y0 + s.Y1) / 2f;
        float lx = MathF.Abs(s.X1 - s.X0), ly = MathF.Abs(s.Y1 - s.Y0);
        if (lx > ly) lx -= 2 * PostHalf; else ly -= 2 * PostHalf;   // shorten by the half-width of the posts at both ends
        if (MathF.Max(lx, ly) < 0.05f) return;
        Raylib.DrawCube(P(cx, cy, height), MathF.Max(lx, thickness), thickness, MathF.Max(ly, thickness), color);
    }

    private void DrawTitle(int room)
    {
        var tex = Title(room);
        // must fit inside the central square of the '#' grid (x 16.5..23.5), clear of the walls
        float w = 5.6f, h = w * tex.Height / tex.Width;
        float cx = Ox + RoomGrid.W / 2f, cz = Oz + RoomGrid.H / 2f, y = 0.04f;
        Rlgl.SetTexture(tex.Id);
        Rlgl.Begin(DrawMode.Quads);
        Rlgl.Color4ub(255, 255, 255, 190);
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
