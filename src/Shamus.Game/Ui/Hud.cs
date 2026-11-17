using Raylib_cs;
using Shamus.Core.Simulation;
using Shamus.Game.Rendering;

namespace Shamus.Game.Ui;

/// <summary>2D heads-up display: score, lives, keys, room info, warnings and short toast messages.</summary>
public sealed class Hud
{
    private readonly List<(string text, Color color, float ttl)> _toasts = new();

    public static void Text(string t, int x, int y, int size, Color c)
    {
        Raylib.DrawText(t, x + 2, y + 2, size, new Color(0, 0, 0, 200));
        Raylib.DrawText(t, x, y, size, c);
    }

    public static void TextCentered(string t, int y, int size, Color c, int width = 1280)
    {
        int w = Raylib.MeasureText(t, size);
        Text(t, (width - w) / 2, y, size, c);
    }

    public void OnEvents(IReadOnlyList<WorldEvent> events)
    {
        foreach (var e in events)
        {
            switch (e.Kind)
            {
                case WorldEventKind.KeyPicked: Toast("KEY", Color.Yellow); break;
                case WorldEventKind.DoorOpened: Toast("DOOR OPENED", Color.SkyBlue); break;
                case WorldEventKind.PotionPicked: Toast("EXTRA LIFE", Color.Lime); break;
                case WorldEventKind.BonusPicked: Toast($"BONUS +{e.Value}", Color.Violet); break;
                case WorldEventKind.PodOpened: Toast("POD OPEN", Color.Orange); break;
                case WorldEventKind.CycleStarted: Toast($"LOOP {e.Value + 1}  - ENEMIES FASTER", Color.Red); break;
            }
        }
    }

    private void Toast(string t, Color c) { _toasts.Add((t, c, 2.0f)); if (_toasts.Count > 4) _toasts.RemoveAt(0); }

    public void Update(float dt)
    {
        for (int i = _toasts.Count - 1; i >= 0; i--)
        {
            var t = _toasts[i];
            t.ttl -= dt;
            if (t.ttl <= 0) _toasts.RemoveAt(i); else _toasts[i] = t;
        }
    }

    public void Draw(GameWorld w, string mazeName, int highScore, int screenW, int screenH, bool showHelp)
    {
        // top bar
        Raylib.DrawRectangle(0, 0, screenW, 44, new Color(0, 0, 0, 170));
        Text($"SCORE {w.Score:D6}", 16, 10, 26, Color.White);
        Text($"HI {Math.Max(highScore, w.Score):D6}", 260, 10, 26, Color.Gold);
        // lives as small shamus icons
        int lx = screenW - 16;
        for (int i = 0; i < Math.Min(w.Lives, 12); i++)
        {
            lx -= 26;
            Raylib.DrawRectangle(lx, 10, 14, 18, Color.Gold);
            Raylib.DrawCircle(lx + 7, 8, 7, Color.Orange);
        }
        if (w.Lives > 12) Text($"x{w.Lives}", lx - 50, 10, 22, Color.Gold);
        Text("LIVES", lx - 90, 12, 20, Color.Gray);

        // keys carried
        for (int i = 0; i < w.Keys.Count; i++)
        {
            int kx = 16 + i * 34, ky = 54;
            var kc = AtariPalette.ToColor(w.Keys[i] & 0x0F, (w.Keys[i] >> 4) & 0x0E);
            Raylib.DrawCircle(kx + 8, ky + 10, 8, kc);
            Raylib.DrawRectangle(kx + 12, ky + 8, 18, 4, kc);
            Raylib.DrawRectangle(kx + 24, ky + 8, 4, 10, kc);
        }

        // bottom bar: like the original "ROOM: n  LEVEL: n"
        Raylib.DrawRectangle(0, screenH - 40, screenW, 40, new Color(0, 0, 0, 170));
        int lvl = w.Maze.Level(w.Room);
        Text($"ROOM {w.Room:D3}", 16, screenH - 32, 24, Color.Yellow);
        Text($"LEVEL {lvl + 1} {SceneRenderer.LevelNames[lvl]}", 190, screenH - 32, 24, SceneRenderer.LevelColors[lvl]);
        Text($"{w.Maze.Name.ToUpperInvariant()}" + (w.Cycle > 0 ? $"   LOOP {w.Cycle + 1}" : ""), 480, screenH - 32, 24, Color.LightGray);
        string status = w.Enemies.Count > 0 ? $"ENEMIES {w.Enemies.Count}" : w.IsPodRoom && !w.PodOpen ? "SHOOT THE TARGET" : "CLEAR";
        Text(status, screenW - 250, screenH - 32, 24, w.Enemies.Count > 0 ? Color.Orange : Color.Lime);
        if (showHelp) Text("WASD move   SPACE fire   P pause   M map   V view   F2 mute", 16, screenH - 64, 18, Color.Gray);

        if (w.Config.GodMode) Text("GOD MODE", screenW / 2 - 70, 12, 24, Color.Lime);
        if (w.ShadowWarned && !w.ShadowActive && !w.IsPodRoom && (w.TickCount / 20) % 2 == 0)
            TextCentered("!!  THE SHADOW APPROACHES  !!", 70, 32, Color.Red, screenW);
        if (w.ShadowActive) TextCentered("RUN!", 70, 32, Color.Red, screenW);
        if (w.Phase == GamePhase.Won) TextCentered("SHADOW DEFEATED", screenH / 2 - 30, 56, Color.Gold, screenW);

        for (int i = 0; i < _toasts.Count; i++)
        {
            var t = _toasts[i];
            byte a = (byte)Math.Clamp(t.ttl * 255f, 0, 255);
            TextCentered(t.text, screenH - 120 - i * 32, 26, new Color(t.color.R, t.color.G, t.color.B, a), screenW);
        }
    }
}
