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
    // neon colour of the generator caps per level (black/first level = red as in the concept sketch)
    public static readonly Color[] Neon =
    {
        new(255, 70, 25, 255), new(70, 130, 255, 255), new(70, 255, 110, 255), new(255, 60, 70, 255),
    };
    private static readonly Color FloorColor = new(34, 36, 46, 255), ClosedColor = new(18, 18, 26, 255);
    private static readonly Color CliffColor = new(48, 58, 88, 255), CliffShade = new(30, 36, 56, 255), CliffLip = new(95, 118, 170, 255);
    private const float CliffDepth = 24f;
    private static readonly Color PostBody = new(46, 88, 235, 255), PostTop = new(110, 160, 255, 255);
    // the electrified walls are pink force fields
    private static readonly Color FieldPink = new(255, 120, 190, 255);
    private const float FieldHeight = RoomMetrics.WallHeight;
    private const float FieldThickness = 0.30f;
    private const byte FieldAlpha = 35;

    private const float Ox = -RoomGrid.W / 2f, Oz = -RoomGrid.H / 2f;
    private static Vector3 P(float x, float y, float h) => new(Ox + x, h, Oz + y);

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

    /// <summary>
    /// Opaque parts: floor, closed areas and the generator posts. In the isometric view the room is a plateau
    /// shaped like the hero's reachable area: a thick plus in chambers, the passage in corridors. Dark side walls
    /// follow the whole closed contour and drop far down; everything outside the plateau is the drop.
    /// </summary>
    public void DrawSolid(RoomGrid grid, int level, double time, bool cliff, int seedX, int seedY)
    {
        if (cliff) DrawPlateauFloor(grid, seedX, seedY);
        else
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
        }

        DrawPosts(grid, level, time);
    }

    /// <summary>Isometric floor: merged runs of the reachable mask at the top of the slab.</summary>
    private static void DrawPlateauFloor(RoomGrid grid, int seedX, int seedY)
    {
        var mask = RoomFloor.Mask(grid, seedX, seedY);
        for (int y = 0; y < RoomGrid.H; y++)
            for (int x = 0; x < RoomGrid.W;)
            {
                if (!mask[x, y]) { x++; continue; }
                int x0 = x;
                while (x < RoomGrid.W && mask[x, y]) x++;
                Raylib.DrawCube(P((x0 + x) / 2f, y + 0.5f, -0.15f), x - x0, 0.30f, 1f, FloorColor);
            }
    }

    /// <summary>
    /// Vertical sides of the plateau. Drawn in the unlit pass (after <c>LightingShader.End</c>) so all faces keep
    /// the same flat colour: the light would otherwise shade the narrow face of a convex corner as a dark groove.
    /// </summary>
    public void DrawPlateauSides(RoomGrid grid, int seedX, int seedY)
    {
        var mask = RoomFloor.Mask(grid, seedX, seedY);
        var corners = new HashSet<(int X, int Y)>();
        foreach (var e in RoomFloor.MergedBoundary(mask))
        {
            var a = P(e.X0, e.Y0, 0f);
            var b = P(e.X1, e.Y1, 0f);
            float sx = MathF.Abs(b.X - a.X), sz = MathF.Abs(b.Z - a.Z);
            var mid = new Vector3((a.X + b.X) / 2f, -CliffDepth / 2f, (a.Z + b.Z) / 2f);
            if (sx > sz) CliffWall(mid, sx, 0.08f, shadeSouth: true);
            else CliffWall(mid, 0.08f, sz, shadeSouth: false);
            corners.Add((e.X0, e.Y0));
            corners.Add((e.X1, e.Y1));
        }

        // Flush corner cubes: the wall runs end at the vertex, so the end faces would leave a small outside notch.
        foreach (var (x, y) in corners)
        {
            var at = P(x, y, 0f);
            Raylib.DrawCube(new Vector3(at.X, -CliffDepth / 2f, at.Z), 0.082f, CliffDepth, 0.082f, CliffColor);
            Raylib.DrawCube(new Vector3(at.X, 0.02f, at.Z), 0.124f, 0.04f, 0.124f, CliffLip);
        }

        // A wide column under every generator post: the posts stand on the plateau corners, and the column fills
        // the notch that the thin wall return left in the corner.
        foreach (var (px, py) in grid.Walls.Posts)
        {
            var c = P(px, py, 0f);
            Raylib.DrawCube(new Vector3(c.X, -CliffDepth / 2f, c.Z), 1.02f, CliffDepth, 1.02f, CliffColor);
        }
    }

    /// <summary>
    /// One side of the plateau: the cube plus a thin coat on the face that looks toward the camera from the lower
    /// left (the south face of an x-running wall). Horizontal walls run bottom-left to top-right on screen and get
    /// the darker coat; vertical walls keep the base colour. This replaces directional lighting with two flat tones.
    /// </summary>
    private static void CliffWall(Vector3 at, float sx, float sz, bool shadeSouth)
    {
        Raylib.DrawCube(at, sx, CliffDepth, sz, CliffColor);
        if (shadeSouth) Raylib.DrawCube(new Vector3(at.X, at.Y, at.Z + 0.041f), sx, CliffDepth, 0.006f, CliffShade);
        Raylib.DrawCube(new Vector3(at.X, 0.02f, at.Z), sx + 0.04f, 0.04f, sz + 0.04f, CliffLip);
    }

    /// <summary>Opaque generator posts; also drawn into the shadow map as casters.</summary>
    public void DrawPosts(RoomGrid grid, int level, double time)
    {
        var neon = Neon[Math.Clamp(level, 0, 3)];
        var walls = grid.Walls;
        float pulse = 0.85f + 0.15f * MathF.Sin((float)time * 3f);

        foreach (var (x, y) in walls.Posts)
        {
            var c = P(x, y, RoomMetrics.WallHeight / 2f);
            Raylib.DrawCube(c, RoomMetrics.PostSize, RoomMetrics.WallHeight, RoomMetrics.PostSize, PostBody);
            Raylib.DrawCube(P(x, y, RoomMetrics.WallHeight + 0.02f), RoomMetrics.PostSize, 0.04f, RoomMetrics.PostSize, PostTop);   // solid lighter top face
            Raylib.DrawCube(P(x, y, RoomMetrics.WallHeight + 0.07f), 0.4f, 0.1f, 0.4f, Brighten(neon, 0.75f + 0.25f * pulse));    // solid neon cap
        }
    }

    /// <summary>Translucent pink force-field walls: one vertical panel per wall segment, drawn after opaque geometry.</summary>
    public void DrawWalls(RoomGrid grid, Color? doorColor, int doorSide)
    {
        var walls = grid.Walls;
        // Flush the queued opaque geometry while depth writes are still on.
        // DisableDepthMask only changes GL state and does not flush raylib's batch: without this call
        // the posts would be rasterised with depth writes off, so the walls would draw over them.
        Rlgl.DrawRenderBatchActive();
        Rlgl.DisableDepthMask();
        Raylib.BeginBlendMode(BlendMode.Alpha);
        foreach (var s in walls.Segments)
        {
            var col = IsDoor(s, doorSide) && doorColor is Color dc ? dc : FieldPink;
            Field(s, new Color(col.R, col.G, col.B, FieldAlpha));
            FieldEdge(s, new Color((byte)255, (byte)200, (byte)235, (byte)240));   // bright top rim: reads as the field boundary
        }
        Raylib.EndBlendMode();
        Rlgl.EnableDepthMask();
    }

    /// <summary>
    /// Emissive parts of the room for the glow pass: the neon caps of the generators, the force-field panels and
    /// their bright rims. Drawn additively into the glow target, which is why the panel is brighter here than in
    /// <see cref="DrawWalls"/>: the blur spreads it into a halo around the wall.
    /// </summary>
    public void DrawEmissive(RoomGrid grid, int level, double time, Color? doorColor, int doorSide)
    {
        var neon = Neon[Math.Clamp(level, 0, 3)];
        var walls = grid.Walls;
        float pulse = 0.85f + 0.15f * MathF.Sin((float)time * 3f);

        foreach (var (x, y) in walls.Posts)
            Raylib.DrawCube(P(x, y, RoomMetrics.WallHeight + 0.07f), 0.45f, 0.12f, 0.45f, Brighten(neon, 0.9f + 0.3f * pulse));

        foreach (var s in walls.Segments)
        {
            var col = IsDoor(s, doorSide) && doorColor is Color dc ? dc : FieldPink;
            Field(s, new Color(col.R, col.G, col.B, (byte)95));
            FieldEdge(s, new Color((byte)255, (byte)215, (byte)240, (byte)255));
        }
    }

    private static bool IsDoor(WallSegment s, int doorSide)
    {
        if (doorSide == 0 || s.IsHorizontal) return false;
        float x = doorSide < 0 ? 0.5f : RoomGrid.W - 0.5f;
        return MathF.Abs(s.X0 - x) < 0.01f && MathF.Min(s.Y0, s.Y1) >= RoomGrid.DoorY0 - 1.01f && MathF.Max(s.Y0, s.Y1) <= RoomGrid.DoorY1 + 1.01f;
    }

    private static Color Brighten(Color c, float f) => new((byte)Math.Min(255, c.R * f), (byte)Math.Min(255, c.G * f), (byte)Math.Min(255, c.B * f), c.A);

    private const float PostHalf = 0.42f;   // walls start at the side faces of the generators, not at their centres

    /// <summary>One vertical translucent panel along a wall segment, between the generator side faces.</summary>
    private static void Field(WallSegment s, Color color) => FieldBox(s, FieldHeight / 2f, FieldHeight, FieldThickness, color);

    /// <summary>Bright thin line along the top of a field wall.</summary>
    private static void FieldEdge(WallSegment s, Color color) => FieldBox(s, FieldHeight, 0.05f, 0.12f, color);

    private static void FieldBox(WallSegment s, float centerY, float height, float thickness, Color color)
    {
        float cx = (s.X0 + s.X1) / 2f, cy = (s.Y0 + s.Y1) / 2f;
        float lx = MathF.Abs(s.X1 - s.X0), ly = MathF.Abs(s.Y1 - s.Y0);
        if (lx > ly) lx -= 2 * PostHalf; else ly -= 2 * PostHalf;
        if (MathF.Max(lx, ly) < 0.05f) return;
        Raylib.DrawCube(P(cx, cy, centerY), MathF.Max(lx, thickness), height, MathF.Max(ly, thickness), color);
    }

    public void DrawTitle(int room)
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
