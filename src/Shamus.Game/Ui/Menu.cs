using Raylib_cs;

namespace Shamus.Game.Ui;

public sealed class MenuItem
{
    public required Func<string> Label;
    public Action? OnEnter;
    public Action<int>? OnChange; // -1 / +1 (left/right)
}

/// <summary>Simple vertical keyboard menu.</summary>
public sealed class Menu
{
    public List<MenuItem> Items { get; } = new();
    public int Selected { get; private set; }
    public Action? OnMove; // for UI blips

    public void Reset() => Selected = 0;

    public void Update()
    {
        if (Items.Count == 0) return;
        if (Raylib.IsKeyPressed(KeyboardKey.Down) || Raylib.IsKeyPressed(KeyboardKey.S)) { Selected = (Selected + 1) % Items.Count; OnMove?.Invoke(); }
        if (Raylib.IsKeyPressed(KeyboardKey.Up) || Raylib.IsKeyPressed(KeyboardKey.W)) { Selected = (Selected + Items.Count - 1) % Items.Count; OnMove?.Invoke(); }
        var it = Items[Selected];
        if (Raylib.IsKeyPressed(KeyboardKey.Left) || Raylib.IsKeyPressed(KeyboardKey.A)) { it.OnChange?.Invoke(-1); OnMove?.Invoke(); }
        if (Raylib.IsKeyPressed(KeyboardKey.Right) || Raylib.IsKeyPressed(KeyboardKey.D)) { it.OnChange?.Invoke(1); OnMove?.Invoke(); }
        if (Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.KpEnter) || (it.OnChange == null && Raylib.IsKeyPressed(KeyboardKey.Space))) it.OnEnter?.Invoke();
    }

    public void Draw(int top, int screenW, int size = 32, int step = 52)
    {
        for (int i = 0; i < Items.Count; i++)
        {
            bool sel = i == Selected;
            string label = Items[i].Label();
            if (sel && Items[i].OnChange != null) label = "<  " + label + "  >";
            var col = sel ? Color.Gold : Color.LightGray;
            Hud.TextCentered(sel && Items[i].OnChange == null ? "> " + label + " <" : label, top + i * step, size, col, screenW);
        }
    }
}
