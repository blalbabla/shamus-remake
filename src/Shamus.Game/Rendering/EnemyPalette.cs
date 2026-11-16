using Raylib_cs;

namespace Shamus.Game.Rendering;

/// <summary>
/// Enemy body colours. In the original the robots were painted at random when a room was generated: the paint said
/// nothing about behaviour, it just made a room look varied. Same here — the kind is read from the shape and the
/// paint comes from this palette, rolled per enemy at spawn (<c>Enemy.ColorIndex</c>).
/// Green is deliberately absent: that is the player's tank, and an enemy must never read as one.
/// </summary>
public static class EnemyPalette
{
    /// <summary>One paint job: main body, a lighter highlight and a darker shade for heads, towers and shadows.</summary>
    public readonly record struct Scheme(Color Body, Color Light, Color Dark);

    private static readonly Scheme[] Schemes =
    {
        new(new Color((byte)206, (byte)110, (byte)214, (byte)255),
            new Color((byte)238, (byte)160, (byte)246, (byte)255),
            new Color((byte)120, (byte)48, (byte)140, (byte)255)),   // magenta
        new(new Color((byte)232, (byte)160, (byte)70, (byte)255),
            new Color((byte)250, (byte)205, (byte)130, (byte)255),
            new Color((byte)140, (byte)80, (byte)30, (byte)255)),    // amber
        new(new Color((byte)110, (byte)190, (byte)225, (byte)255),
            new Color((byte)170, (byte)225, (byte)245, (byte)255),
            new Color((byte)45, (byte)95, (byte)135, (byte)255)),    // ice blue
        new(new Color((byte)225, (byte)95, (byte)95, (byte)255),
            new Color((byte)250, (byte)160, (byte)150, (byte)255),
            new Color((byte)130, (byte)40, (byte)45, (byte)255)),    // crimson
    };

    public static int Count => Schemes.Length;

    /// <summary>Paint for a palette index; wraps, so a config asking for more colours than the palette still varies.</summary>
    public static Scheme Of(int index)
    {
        int n = Schemes.Length;
        return Schemes[((index % n) + n) % n];
    }
}
