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
