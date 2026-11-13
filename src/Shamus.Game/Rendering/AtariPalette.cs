using Raylib_cs;

namespace Shamus.Game.Rendering;

/// <summary>Approximate Atari 8-bit colour (hue 0-15, luma 0-14) to RGB.</summary>
public static class AtariPalette
{
    static readonly (int r, int g, int b)[] Hues =
    {
        (160,160,160), (230,130,40), (220,60,50), (210,70,100), (200,70,150), (170,70,200), (120,80,220), (80,90,230),
        (60,120,230), (60,170,220), (60,190,190), (70,200,110), (80,200,80), (140,200,70), (190,170,60), (210,140,60),
    };

    public static Color ToColor(int hue, int luma)
    {
        var (r, g, b) = Hues[hue & 15];
        float f = hue == 0 ? 0.25f + luma / 14f * 0.75f : 0.45f + luma / 14f * 0.75f;
        return new Color((byte)Math.Min(255, r * f), (byte)Math.Min(255, g * f), (byte)Math.Min(255, b * f), (byte)255);
    }
}
