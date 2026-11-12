namespace Shamus.Core.Mazes;

/// <summary>Kind of room as encoded in segment 1 bit 7.</summary>
public enum RoomKind { Chamber, Corridor, Pod }

public enum ObjectKind { None = 0, Keyhole = 1, Key = 2, Mystery = 3, Potion = 4, Shadow = 6 }

/// <summary>
/// 128 rooms in the "expanded Atari" format (5 segments x 128 bytes = 640 bytes), see docs/MAZE_FORMAT.md.
/// Seg0 horizontal walls, Seg1 vertical walls, Seg2 objects, Seg3 colors, Seg4 pod list + level bounds.
/// </summary>
public sealed class MazeData
{
    public const int RoomCount = 128;
    public const int FileSize = 640;

    private readonly byte[] _d;
    public string Name { get; }

    public MazeData(string name, byte[] data)
    {
        if (data.Length != FileSize) throw new ArgumentException($"maze must be {FileSize} bytes", nameof(data));
        Name = name;
        _d = (byte[])data.Clone();
    }

    public byte[] Raw => (byte[])_d.Clone();

    public byte Horizontal(int room) => _d[room];
    public byte Vertical(int room) => _d[128 + room];
    public ObjectKind Object(int room) => (ObjectKind)_d[256 + room];
    public byte ColorByte(int room) => _d[384 + room];

    /// <summary>Pod rooms listed in segment 5 (byte 0 = count, then room numbers).</summary>
    public IReadOnlyList<int> PodRooms
    {
        get
        {
            int n = Math.Min((int)_d[512], 0x7C);
            var l = new List<int>(n);
            for (int i = 1; i <= n; i++) l.Add(_d[512 + i]);
            return l;
        }
    }

    /// <summary>Last room of levels 1..3 (Atari convention; level 4 ends at room 127).</summary>
    public IReadOnlyList<int> LevelEnds => new[] { (int)_d[512 + 0x7D], _d[512 + 0x7E], _d[512 + 0x7F] };

    public bool IsCorridor(int room) => (Horizontal(room) & 0x80) != 0;
    public bool IsPod(int room) => PodRooms.Contains(room);
    public RoomKind Kind(int room) => IsPod(room) ? RoomKind.Pod : IsCorridor(room) ? RoomKind.Corridor : RoomKind.Chamber;

    /// <summary>Corridor shape 0..7 = (h &amp; 0x70) &gt;&gt; 4 ($80..$F0 in the spec).</summary>
    public int CorridorType(int room) => (Horizontal(room) >> 4) & 7;

    /// <summary>For corridors: the room reached by leaving through the top or bottom exit.</summary>
    public int VerticalLink(int room) => Vertical(room) & 0x7F;

    /// <summary>0 = black, 1 = blue, 2 = green, 3 = red.</summary>
    public int Level(int room)
    {
        var e = LevelEnds;
        return room <= e[0] ? 0 : room <= e[1] ? 1 : room <= e[2] ? 2 : 3;
    }

    /// <summary>Horizontal wall piece (0..5; row-major: top row 0-2, bottom row 3-5).</summary>
    public bool HWall(int room, int i) => (Horizontal(room) & (1 << i)) != 0;
    /// <summary>Vertical wall piece (0..5).</summary>
    public bool VWall(int room, int i) => (Vertical(room) & (1 << i)) != 0;
}
