using Raylib_cs;

namespace Shamus.Game.Ui;

/// <summary>One row of the settings screen: a section header, or a value the player can change.</summary>
public sealed class SettingsRow
{
    public required Func<string> Label;
    public Func<bool>? Visible;   // dynamic rows (the seed shows only for procedural mazes)
    public bool Header;
    public Action<int>? Change;   // -1 / +1 (left/right)
    public Action? Activate;      // enter / space
}

/// <summary>Sectioned settings screen: headers are skipped by the cursor, changes apply immediately.</summary>
public sealed class SettingsMenu
{
    public List<SettingsRow> Rows { get; } = new();
    public int Selected { get; private set; }
    public Action? OnMove; // for UI blips

    private bool IsSelectable(int i) => !Rows[i].Header && (Rows[i].Visible?.Invoke() ?? true);

    public void Reset()
    {
        for (int i = 0; i < Rows.Count; i++)
            if (IsSelectable(i)) { Selected = i; return; }
        Selected = 0;
    }

    private int NextSelectable(int from, int dir)
    {
        for (int k = 1; k <= Rows.Count; k++)
        {
            int i = ((from + dir * k) % Rows.Count + Rows.Count) % Rows.Count;
            if (IsSelectable(i)) return i;
        }
        return from;
    }

    public void Update()
    {
        if (Rows.Count == 0) return;
        if (!IsSelectable(Selected)) Reset();
        if (GameInput.DownPressed) { Selected = NextSelectable(Selected, 1); OnMove?.Invoke(); }
        if (GameInput.UpPressed) { Selected = NextSelectable(Selected, -1); OnMove?.Invoke(); }
        var row = Rows[Selected];
        if (GameInput.LeftPressed) row.Change?.Invoke(-1);
        if (GameInput.RightPressed) row.Change?.Invoke(1);
        if (GameInput.ConfirmPressed) row.Activate?.Invoke();
    }

    public void Draw(int top, int screenW, int size = 28, int step = 40, int headerSize = 22)
    {
        int y = top;
        for (int i = 0; i < Rows.Count; i++)
        {
            var row = Rows[i];
            if (!(row.Visible?.Invoke() ?? true)) continue;
            if (row.Header)
            {
                Hud.TextCentered(row.Label(), y + 6, headerSize, new Color(110, 150, 220, 255), screenW);
                y += step;
                continue;
            }
            bool sel = i == Selected;
            string label = sel ? ">  " + row.Label() + "  <" : row.Label();
            Hud.TextCentered(label, y, size, sel ? Color.Gold : Color.LightGray, screenW);
            y += step;
        }
    }
}
