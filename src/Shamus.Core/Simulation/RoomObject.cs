using Shamus.Core.Mazes;

namespace Shamus.Core.Simulation;

public sealed class RoomObject
{
    public ObjectKind Kind;
    public float X, Y;
    public int KeyId;   // key / keyhole colour id (MazeData.KeyId)
    public int Hue, Luma;
}
