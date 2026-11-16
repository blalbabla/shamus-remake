using Raylib_cs;

namespace Shamus.Game.Ui;

/// <summary>
/// Keyboard plus the first connected gamepad, sampled once per frame. The left stick (and the D-pad) drives
/// movement and menu navigation; A confirms in menus and fires in game; B goes back (and pauses in game);
/// the View button (the two-windows one) opens the map. Keyboard stays fully usable.
/// </summary>
public static class GameInput
{
    private const float MoveDeadzone = 0.3f;   // stick travel before it counts as movement
    private const float MenuDeadzone = 0.6f;   // menus need a firmer push, so a resting stick does not scroll

    private static int _pad = -1;
    private static bool _up, _down, _left, _right;

    /// <summary>Keyboard movement, room axes (the keyboard never changes with the camera).</summary>
    public static int KeyboardX { get; private set; }
    public static int KeyboardY { get; private set; }
    /// <summary>Gamepad stick/D-pad movement, screen axes; GameApp maps it through the camera.</summary>
    public static int PadX { get; private set; }
    public static int PadY { get; private set; }
    public static bool FireHeld { get; private set; }
    public static bool UpPressed { get; private set; }  // edges for menus
    public static bool DownPressed { get; private set; }
    public static bool LeftPressed { get; private set; }
    public static bool RightPressed { get; private set; }
    public static bool ConfirmPressed { get; private set; }   // A / Enter / Space
    public static bool BackPressed { get; private set; }      // B / Esc
    public static bool MapPressed { get; private set; }       // View / M / Tab
    public static bool PausePressed { get; private set; }     // B / Esc / P

    public static void Update()
    {
        _pad = FindPad();
        bool pad = _pad >= 0;
        float ax = pad ? Raylib.GetGamepadAxisMovement(_pad, GamepadAxis.LeftX) : 0f;
        float ay = pad ? Raylib.GetGamepadAxisMovement(_pad, GamepadAxis.LeftY) : 0f;

        // Menu edges: keyboard/D-pad as digital, the stick only on its dominant axis so a diagonal push
        // does not scroll and change a value in the same frame.
        bool stickLeft = pad && ax < -MenuDeadzone && MathF.Abs(ax) >= MathF.Abs(ay);
        bool stickRight = pad && ax > MenuDeadzone && MathF.Abs(ax) >= MathF.Abs(ay);
        bool stickUp = pad && ay < -MenuDeadzone && MathF.Abs(ay) > MathF.Abs(ax);
        bool stickDown = pad && ay > MenuDeadzone && MathF.Abs(ay) > MathF.Abs(ax);

        bool upHeld = KeyDown(KeyboardKey.W, KeyboardKey.Up, KeyboardKey.Kp8, KeyboardKey.Kp7, KeyboardKey.Kp9, KeyboardKey.Home, KeyboardKey.PageUp)
            || PadDown(GamepadButton.LeftFaceUp) || stickUp;
        bool downHeld = KeyDown(KeyboardKey.S, KeyboardKey.Down, KeyboardKey.Kp2, KeyboardKey.Kp1, KeyboardKey.Kp3, KeyboardKey.End, KeyboardKey.PageDown)
            || PadDown(GamepadButton.LeftFaceDown) || stickDown;
        bool leftHeld = KeyDown(KeyboardKey.A, KeyboardKey.Left, KeyboardKey.Kp4, KeyboardKey.Kp7, KeyboardKey.Kp1, KeyboardKey.Home, KeyboardKey.End)
            || PadDown(GamepadButton.LeftFaceLeft) || stickLeft;
        bool rightHeld = KeyDown(KeyboardKey.D, KeyboardKey.Right, KeyboardKey.Kp6, KeyboardKey.Kp9, KeyboardKey.Kp3, KeyboardKey.PageUp, KeyboardKey.PageDown)
            || PadDown(GamepadButton.LeftFaceRight) || stickRight;

        UpPressed = upHeld && !_up;
        DownPressed = downHeld && !_down;
        LeftPressed = leftHeld && !_left;
        RightPressed = rightHeld && !_right;
        _up = upHeld; _down = downHeld; _left = leftHeld; _right = rightHeld;

        // Held movement: keyboard and D-pad are digital, the stick is analog with a soft deadzone.
        int kx = (KeyDown(KeyboardKey.D, KeyboardKey.Right, KeyboardKey.Kp6, KeyboardKey.Kp9, KeyboardKey.Kp3, KeyboardKey.PageUp, KeyboardKey.PageDown) ? 1 : 0)
            - (KeyDown(KeyboardKey.A, KeyboardKey.Left, KeyboardKey.Kp4, KeyboardKey.Kp7, KeyboardKey.Kp1, KeyboardKey.Home, KeyboardKey.End) ? 1 : 0);
        int ky = (KeyDown(KeyboardKey.S, KeyboardKey.Down, KeyboardKey.Kp2, KeyboardKey.Kp1, KeyboardKey.Kp3, KeyboardKey.End, KeyboardKey.PageDown) ? 1 : 0)
            - (KeyDown(KeyboardKey.W, KeyboardKey.Up, KeyboardKey.Kp8, KeyboardKey.Kp7, KeyboardKey.Kp9, KeyboardKey.Home, KeyboardKey.PageUp) ? 1 : 0);
        int px = (PadDown(GamepadButton.LeftFaceRight) ? 1 : 0) - (PadDown(GamepadButton.LeftFaceLeft) ? 1 : 0);
        int py = (PadDown(GamepadButton.LeftFaceDown) ? 1 : 0) - (PadDown(GamepadButton.LeftFaceUp) ? 1 : 0);
        int sx = ax > MoveDeadzone ? 1 : ax < -MoveDeadzone ? -1 : 0;
        int sy = ay > MoveDeadzone ? 1 : ay < -MoveDeadzone ? -1 : 0;
        KeyboardX = kx;
        KeyboardY = ky;
        PadX = Math.Clamp(px + sx, -1, 1);
        PadY = Math.Clamp(py + sy, -1, 1);

        FireHeld = KeyDown(KeyboardKey.Space, KeyboardKey.LeftControl, KeyboardKey.RightControl, KeyboardKey.Kp0, KeyboardKey.Kp5)
            || PadDown(GamepadButton.RightFaceDown);

        ConfirmPressed = KeyPressed(KeyboardKey.Enter, KeyboardKey.KpEnter, KeyboardKey.Space)
            || PadPressed(GamepadButton.RightFaceDown);
        BackPressed = KeyPressed(KeyboardKey.Escape) || PadPressed(GamepadButton.RightFaceRight);
        MapPressed = KeyPressed(KeyboardKey.M, KeyboardKey.Tab) || PadPressed(GamepadButton.MiddleLeft);
        PausePressed = KeyPressed(KeyboardKey.P, KeyboardKey.Escape) || PadPressed(GamepadButton.RightFaceRight);
    }

    /// <summary>First connected gamepad, or -1. Checked every frame so hot-plugging works.</summary>
    private static int FindPad()
    {
        for (int i = 0; i < 4; i++)
            if (Raylib.IsGamepadAvailable(i)) return i;
        return -1;
    }

    private static bool PadDown(GamepadButton b) => _pad >= 0 && Raylib.IsGamepadButtonDown(_pad, b);

    private static bool PadPressed(GamepadButton b) => _pad >= 0 && Raylib.IsGamepadButtonPressed(_pad, b);

    private static bool KeyDown(params KeyboardKey[] keys)
    {
        foreach (var k in keys) if (Raylib.IsKeyDown(k)) return true;
        return false;
    }

    private static bool KeyPressed(params KeyboardKey[] keys)
    {
        foreach (var k in keys) if (Raylib.IsKeyPressed(k)) return true;
        return false;
    }
}
