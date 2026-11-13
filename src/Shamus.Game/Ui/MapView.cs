using Raylib_cs;
using Shamus.Core.Mazes;
using Shamus.Core.Simulation;
using Shamus.Game.Rendering;

namespace Shamus.Game.Ui;

/// <summary>Full-screen schematic map (like shamus_map_simplified.gif) showing only the rooms the player has visited.</summary>
public static class MapView
{
    public static void Draw(MapLayout layout, GameWorld world, int screenW, int screenH)
    {
        Raylib.DrawRectangle(0, 0, screenW, screenH, new Color(10, 10, 16, 235));
        Hud.TextCentered("MAP", 14, 36, Color.Gold, screenW);
        Hud.TextCentered("M / ESC close      dark cells: unexplored exits", screenH - 34, 20, Color.Gray, screenW);

        int pw = (screenW - 60) / 2, ph = (screenH - 130) / 2;
        for (int li = 0; li < layout.Levels.Count && li < 4; li++)
        {
            int px = 20 + (li % 2) * (pw + 20), py = 64 + (li / 2) * (ph + 14);
            DrawPanel(layout.Levels[li], world, px, py, pw, ph);
        }
    }

    /// <summary>Rooms not yet entered that can be reached through an exit of a visited room.</summary>
    private static HashSet<int> Frontier(GameWorld w)
    {
        var m = w.Maze;
        var f = new HashSet<int>();
        foreach (int r in w.Visited)
        {
            if (r < MazeData.RoomCount - 1 && m.OpenRight(r) && m.OpenLeft(r + 1)) f.Add(r + 1);
            if (r > 0 && m.OpenLeft(r) && m.OpenRight(r - 1)) f.Add(r - 1);
            if (m.IsCorridor(r) && !m.IsPod(r)) f.Add(m.VerticalLink(r));
        }
        f.ExceptWith(w.Visited);
        return f;
    }

    private static void DrawPanel(LevelMap lv, GameWorld w, int px, int py, int pw, int ph)
    {
        Raylib.DrawRectangle(px, py, pw, ph, new Color(60, 60, 66, 255));
        Raylib.DrawRectangleLines(px, py, pw, ph, new Color(110, 110, 120, 255));
        Hud.Text($"Level {lv.Index + 1}", px + 10, py + 6, 22, SceneRenderer.LevelColors[lv.Index]);

        int top = 36;
        int cw = Math.Min(44, (pw - 20) / Math.Max(1, lv.Width));
        int ch = Math.Min(34, (ph - top - 10) / Math.Max(1, lv.Height));
        int ox = px + (pw - cw * lv.Width) / 2, oy = py + top;
        var maze = w.Maze;
        var frontier = Frontier(w);

        for (int y = 0; y < lv.Height; y++)
        {
            for (int x = 0; x < lv.Width; x++)
            {
                string t = lv.Cells[x, y];
                int cx = ox + x * cw, cy = oy + y * ch;
                if (LevelMap.IsRoomToken(t))
                {
                    int room = int.Parse(t);
                    if (!w.Visited.Contains(room))
                    {
                        // never entered, but reachable through an exit of a visited room: dim hint
                        if (!frontier.Contains(room)) continue;
                        Raylib.DrawRectangle(cx + 1, cy + 1, cw - 2, ch - 2, new Color(34, 34, 40, 255));
                        Raylib.DrawRectangleLines(cx + 1, cy + 1, cw - 2, ch - 2, new Color(95, 95, 108, 255));
                        int fs2 = Math.Max(10, ch - 14);
                        int tw2 = Raylib.MeasureText(t, fs2);
                        Raylib.DrawText(t, cx + (cw - tw2) / 2, cy + (ch - fs2) / 2, fs2, new Color(95, 95, 105, 255));
                        continue;
                    }
                    var bg = Color.Black;
                    var fg = new Color(200, 200, 200, 255);
                    var obj = maze.Object(room);
                    bool objColor = obj is ObjectKind.Key or ObjectKind.Keyhole;
                    var oc = AtariPalette.ToColor(maze.ObjectHue(room), maze.ObjectLuma(room));
                    if (objColor && !w.IsTaken(room))
                    {
                        if (obj == ObjectKind.Keyhole) bg = oc; else fg = oc;
                    }
                    Raylib.DrawRectangle(cx + 1, cy + 1, cw - 2, ch - 2, bg);
                    int fs = Math.Max(10, ch - 14);
                    int tw = Raylib.MeasureText(t, fs);
                    Raylib.DrawText(t, cx + (cw - tw) / 2, cy + (ch - fs) / 2, fs, fg);
                    if (room == w.Room && (int)(Raylib.GetTime() * 3) % 2 == 0)
                        Raylib.DrawRectangleLinesEx(new Rectangle(cx, cy, cw, ch), 3, Color.Yellow);
                }
                else if (t == "---")
                {
                    if (lv.Neighbour(x, y, -1, 0) is int a && a >= 0 && w.Visited.Contains(a) || lv.Neighbour(x, y, 1, 0) is int b && b >= 0 && w.Visited.Contains(b))
                        Raylib.DrawRectangle(cx, cy + ch / 2 - ch / 4, cw, ch / 2, Color.Black);
                }
                else if (t == "|")
                {
                    if (lv.Neighbour(x, y, 0, -1) is int a && a >= 0 && w.Visited.Contains(a) || lv.Neighbour(x, y, 0, 1) is int b && b >= 0 && w.Visited.Contains(b))
                        Raylib.DrawRectangle(cx + cw / 2 - cw / 6, cy, cw / 3, ch, Color.Black);
                }
                else if (t.StartsWith("to"))
                {
                    int nb = lv.Neighbour(x, y, -1, 0) >= 0 ? lv.Neighbour(x, y, -1, 0) : lv.Neighbour(x, y, 1, 0);
                    if (nb >= 0 && w.Visited.Contains(nb))
                        Raylib.DrawText(t, cx + 2, cy + (ch - 16) / 2, 16, Color.Yellow);
                }
            }
        }
    }
}
