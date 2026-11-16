using Shamus.Game.Rendering;
using Xunit;

public class ScreenMovementTests
{
    [Fact]
    public void TopViewKeepsScreenAxes()
    {
        var rig = new CameraRig(ViewMode.Top);
        Assert.Equal((1, 0), rig.ScreenToRoom(1, 0));
        Assert.Equal((-1, 0), rig.ScreenToRoom(-1, 0));
        Assert.Equal((0, 1), rig.ScreenToRoom(0, 1));
        Assert.Equal((0, -1), rig.ScreenToRoom(0, -1));
        Assert.Equal((0, 0), rig.ScreenToRoom(0, 0));
    }

    [Fact]
    public void IsometricMapsScreenDirectionsToRoomAxes()
    {
        var rig = new CameraRig(ViewMode.Isometric);
        Assert.Equal((1, 1), rig.ScreenToRoom(1, 0));     // screen right -> floor diagonal down-right
        Assert.Equal((-1, -1), rig.ScreenToRoom(-1, 0));  // screen left -> floor diagonal up-left
        Assert.Equal((1, 0), rig.ScreenToRoom(1, -1));    // screen up-right -> straight +X
        Assert.Equal((-1, 0), rig.ScreenToRoom(-1, 1));   // screen down-left -> straight -X
        Assert.Equal((0, 1), rig.ScreenToRoom(1, 1));     // screen down-right -> straight +Y
        Assert.Equal((0, -1), rig.ScreenToRoom(-1, -1));  // screen up-left -> straight -Y
    }

    [Fact]
    public void BasisIsNormalizedAndOrthogonal()
    {
        var rig = new CameraRig(ViewMode.Isometric);
        var (right, down) = rig.ScreenBasis();
        Assert.True(MathF.Abs(right.Length() - 1f) < 1e-3f);
        Assert.True(MathF.Abs(down.Length() - 1f) < 1e-3f);
        Assert.True(MathF.Abs(System.Numerics.Vector2.Dot(right, down)) < 1e-3f);
    }
}
