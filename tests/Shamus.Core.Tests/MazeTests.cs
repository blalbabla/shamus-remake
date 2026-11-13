using Shamus.Core.Mazes;
using Xunit;

public class MazeTests
{
    static string Dir
    {
        get
        {
            var d = new DirectoryInfo(AppContext.BaseDirectory);
            while (d != null && !Directory.Exists(Path.Combine(d.FullName, "assets", "mazes"))) d = d.Parent;
            return Path.Combine(d!.FullName, "assets", "mazes");
        }
    }

    [Theory]
    [InlineData("atari")] [InlineData("c64")] [InlineData("holmes")] [InlineData("cluseau")] [InlineData("marlowe")] [InlineData("bond")]
    public void AllMazesLoadAndBuild(string name)
    {
        var m = MazeLoader.Load(Path.Combine(Dir, name + ".maze"));
        for (int r = 0; r < MazeData.RoomCount; r++) Assert.NotNull(RoomGrid.Build(m, r));
    }

    [Fact]
    public void AtariMazeMatchesDocs()
    {
        var m = MazeLoader.Load(Path.Combine(Dir, "atari.maze"));
        Assert.Equal(0x0A, m.Horizontal(0));
        Assert.Contains(18, m.PodRooms);
        Assert.Contains(127, m.PodRooms);
        Assert.Equal(new[] { 37, 66, 92 }, m.LevelEnds);
        Assert.True(m.IsCorridor(1));
    }
}

public class MapLayoutTests
{
    static string Assets
    {
        get
        {
            var d = new DirectoryInfo(AppContext.BaseDirectory);
            while (d != null && !Directory.Exists(Path.Combine(d.FullName, "assets", "maps"))) d = d.Parent;
            return Path.Combine(d!.FullName, "assets");
        }
    }

    [Fact]
    public void HandMadeMapMatchesMazeData()
    {
        var m = MazeLoader.Load(Path.Combine(Assets, "mazes", "c64.maze"));
        var map = MapLayout.Load(Path.Combine(Assets, "maps"), m);
        var errors = new List<string>();
        var seen = new HashSet<int>();
        foreach (var lv in map.Levels)
            foreach (var (room, pos) in lv.Rooms)
            {
                if (!seen.Add(room)) errors.Add($"room {room} listed twice");
                if (m.Level(room) != lv.Index) errors.Add($"room {room} on level {lv.Index + 1}, data says {m.Level(room) + 1}");
                // horizontal passage to room+1
                if (room < 127 && m.Level(room + 1) == lv.Index && m.OpenRight(room) && m.OpenLeft(room + 1))
                {
                    if (!lv.Rooms.TryGetValue(room + 1, out var p2) || p2.Y != pos.Y || p2.X <= pos.X) errors.Add($"{room}->{room + 1} not on the same row");
                    else for (int x = pos.X + 1; x < p2.X; x++) if (lv.Cells[x, pos.Y] != "---") errors.Add($"{room}->{room + 1} gap is not a dash");
                }
                // vertical link
                if (m.IsCorridor(room) && !m.IsPod(room))
                {
                    int link = m.VerticalLink(room);
                    if (m.Level(link) == lv.Index && lv.Rooms.TryGetValue(link, out var pl))
                    {
                        if (pl.X != pos.X) errors.Add($"vertical {room}<->{link}: columns differ ({pos.X} vs {pl.X})");
                        else if (m.VerticalDown(room) ? pl.Y <= pos.Y : pl.Y >= pos.Y) errors.Add($"vertical {room}->{link}: wrong direction");
                    }
                }
            }
        for (int r = 0; r < 128; r++) if (!seen.Contains(r)) errors.Add($"room {r} missing");
        Assert.True(errors.Count == 0, string.Join("\n", errors));
    }

    [Theory]
    [InlineData("atari")] [InlineData("holmes")] [InlineData("bond")]
    public void AutoLayoutCoversAllRooms(string name)
    {
        var m = MazeLoader.Load(Path.Combine(Assets, "mazes", name + ".maze"));
        var map = MapLayout.Auto(m);
        Assert.Equal(128, map.Levels.Sum(l => l.Rooms.Count));
    }
}
