namespace Shamus.Core.Mazes;

public static class MazeLoader
{
    public static MazeData Load(string path) =>
        new(Path.GetFileNameWithoutExtension(path), File.ReadAllBytes(path));
}
