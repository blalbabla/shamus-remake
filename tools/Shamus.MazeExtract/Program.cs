// Extracts maze data from Shamus+.atr (DOS 2.x disk image) into assets/mazes/*.maze (640 bytes each).
// Usage: MazeExtract <Shamus+.atr> <outDir>
if (args.Length < 2) { Console.WriteLine("usage: MazeExtract <atr> <outDir>"); return 1; }
var disk = File.ReadAllBytes(args[0]);
byte[] Sector(int n) => disk.AsSpan(16 + (n - 1) * 128, 128).ToArray();

var files = new Dictionary<string, byte[]>();
for (int s = 361; s <= 368; s++)
{
    var b = Sector(s);
    for (int i = 0; i < 8; i++)
    {
        var e = b.AsSpan(i * 16, 16);
        if ((e[0] & 0x40) == 0 || (e[0] & 0x80) != 0) continue;
        int n = e[3] | e[4] << 8;
        var name = System.Text.Encoding.Latin1.GetString(e.Slice(5, 11)).Trim();
        var ms = new MemoryStream();
        while (n != 0)
        {
            var x = Sector(n);
            ms.Write(x, 0, x[127]);
            n = ((x[125] & 3) << 8) | x[126];
        }
        files[name] = ms.ToArray();
    }
}

// XEX -> memory image
var xex = files["SHAMUSPLXEX"];
var mem = new byte[0x10000];
for (int i = 0; i < xex.Length;)
{
    if (xex[i] == 0xFF && xex[i + 1] == 0xFF) i += 2;
    int a = xex[i] | xex[i + 1] << 8, e = xex[i + 2] | xex[i + 3] << 8; i += 4;
    Array.Copy(xex, i, mem, a, e - a + 1); i += e - a + 1;
}

// The game relocates code down $3000: runtime address -> image address + $3000.
const int Reloc = 0x3000;
byte[] Slice(int rt, int n) => mem.AsSpan(rt + Reloc, n).ToArray();
var atari = new byte[640];
Array.Copy(Slice(0x23F2, 128), 0, atari, 0, 128);   // horizontal walls
Array.Copy(Slice(0x2472, 128), 0, atari, 128, 128); // vertical walls / corridor link
Array.Copy(Slice(0x1D43, 128), 0, atari, 256, 128); // objects
Array.Copy(Slice(0x1DC3, 128), 0, atari, 384, 128); // colors
atari[512] = 8;                                      // pod count
Array.Copy(Slice(0x1EE3, 8), 0, atari, 513, 8);      // pod rooms
Array.Copy(Slice(0x24F8, 3), 0, atari, 512 + 0x7D, 3); // level ends

Directory.CreateDirectory(args[1]);
File.WriteAllBytes(Path.Combine(args[1], "atari.maze"), atari);
string[] names = { "c64", "holmes", "cluseau", "marlowe", "bond" };
var a8 = files["SHAM_A8 MAP"];
for (int k = 0; k < 5; k++)
    File.WriteAllBytes(Path.Combine(args[1], names[k] + ".maze"), a8.AsSpan(k * 640, 640).ToArray());
Console.WriteLine("ok: atari + " + string.Join(", ", names));
return 0;
