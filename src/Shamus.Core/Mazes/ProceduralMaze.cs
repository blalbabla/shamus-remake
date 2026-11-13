namespace Shamus.Core.Mazes;

public sealed class MazeGenOptions
{
    public int Locks { get; init; } = 7;      // key/keyhole pairs (max 7: one colour each)
    public int Pods { get; init; } = 7;       // pod rooms including the final one
    public int Potions { get; init; } = 7;
    public int Bonuses { get; init; } = 8;
}

/// <summary>
/// Generates a complete 128-room maze in the same format as the original ones.
/// Rooms form horizontal runs (room n leads to room n+1); runs are stitched together by paired
/// vertical corridors; locks and keys are placed so that the maze is always solvable.
/// </summary>
public static class ProceduralMaze
{
    private enum Spec { Chamber, Pod, Start, End, T, Single }

    // key colours: blue, orange, red, purple, green, cyan, brown (hue | luma << 4)
    private static readonly byte[] Palette = { 0x67, 0x61, 0x62, 0x65, 0x6B, 0x69, 0x6E };

    public static MazeData Generate(int seed, MazeGenOptions? options = null)
    {
        options ??= new MazeGenOptions();
        for (int attempt = 0; attempt < 400; attempt++)
        {
            var rng = new Random(unchecked(seed * 7919 + attempt * 104729));
            var bytes = TryGenerate(rng, options);
            if (bytes == null) continue;
            var maze = new MazeData($"procedural {seed}", bytes);
            if (MazeValidator.Validate(maze).Count == 0) return maze;
        }
        throw new InvalidOperationException($"could not generate a valid maze for seed {seed}");
    }

    private static int PickRunLength(Random rng)
    {
        double[] w = { 0.05, 0.12, 0.12, 0.14, 0.14, 0.12, 0.10, 0.08, 0.07, 0.06 }; // lengths 1..10
        double x = rng.NextDouble() * w.Sum();
        for (int i = 0; i < w.Length; i++) { x -= w[i]; if (x <= 0) return i + 1; }
        return 5;
    }

    private static byte[]? TryGenerate(Random rng, MazeGenOptions o)
    {
        const int N = MazeData.RoomCount;
        // 1. partition into runs
        var runs = new List<(int start, int len)>();
        for (int r = 0; r < N;)
        {
            int len = PickRunLength(rng);
            if (runs.Count == 0) len = Math.Max(len, 2);
            if (r + len > N) len = N - r;
            if (N - (r + len) == 1) len++;           // never leave a single trailing room
            if (len == 1 && r + len >= N) len = 2;    // last run needs 2+ rooms
            len = Math.Min(len, N - r);
            runs.Add((r, len)); r += len;
        }
        if (runs[^1].len < 2) return null;

        var spec = new Spec[N];
        var runOf = new int[N];
        for (int i = 0; i < runs.Count; i++)
        {
            var (s, l) = runs[i];
            for (int k = 0; k < l; k++) { spec[s + k] = Spec.Chamber; runOf[s + k] = i; }
            if (l == 1) spec[s] = Spec.Single;
            else { spec[s] = i == 0 ? Spec.Chamber : Spec.Start; spec[s + l - 1] = Spec.End; }
        }
        spec[N - 1] = Spec.Pod;

        // 2. pods and T junctions among the run interiors
        var middles = Enumerable.Range(1, N - 2).Where(r => spec[r] == Spec.Chamber && runOf[r - 1] == runOf[r] && runOf[r + 1] == runOf[r]).OrderBy(_ => rng.Next()).ToList();
        int pods = 0;
        foreach (int r in middles) { if (pods >= o.Pods - 1) break; spec[r] = Spec.Pod; pods++; }
        foreach (int r in middles) if (spec[r] == Spec.Chamber && rng.NextDouble() < 0.12) spec[r] = Spec.T;

        // 3. level boundaries: inside runs, near the original proportions
        int[] target = { 36, 65, 92 };
        var ends = new int[3];
        for (int i = 0; i < 3; i++)
        {
            var cand = Enumerable.Range(target[i] - 5, 11).Where(r => r > (i > 0 ? ends[i - 1] + 8 : 8) && spec[r] is Spec.Chamber or Spec.Pod or Spec.T && runOf[r + 1] == runOf[r]).ToList();
            if (cand.Count == 0) return null;
            ends[i] = cand[rng.Next(cand.Count)];
        }
        int LevelOf(int r) => r <= ends[0] ? 0 : r <= ends[1] ? 1 : r <= ends[2] ? 2 : 3;

        // 4. vertical slots, paired inside each level
        var link = new int[N]; var down = new bool[N];
        for (int level = 0; level < 4; level++)
        {
            var slots = Enumerable.Range(0, N).Where(r => LevelOf(r) == level && spec[r] is Spec.Start or Spec.End or Spec.T or Spec.Single).ToList();
            if (slots.Count % 2 == 1)
            {
                var extra = Enumerable.Range(0, N).Where(r => LevelOf(r) == level && spec[r] == Spec.Chamber && r != 0).ToList();
                if (extra.Count == 0) return null;
                int t = extra[rng.Next(extra.Count)]; spec[t] = Spec.T; slots.Add(t);
            }
            if (slots.Count < 2) return null;
            List<int>? best = null; int bestBad = int.MaxValue;
            for (int tries = 0; tries < 25 && bestBad > 0; tries++)
            {
                var s = slots.OrderBy(_ => rng.Next()).ToList();
                int bad = 0;
                for (int i = 0; i < s.Count; i += 2) if (runOf[s[i]] == runOf[s[i + 1]]) bad++;
                if (bad < bestBad) { bestBad = bad; best = s; }
            }
            for (int i = 0; i < best!.Count; i += 2)
            {
                int a = best[i], b = best[i + 1];
                link[a] = b; link[b] = a;
                bool aDown = rng.Next(2) == 0; down[a] = aDown; down[b] = !aDown;
            }
        }

        // 5. rooms
        var h = new byte[N]; var v = new byte[N]; var obj = new byte[N]; var col = new byte[N];
        var podList = new List<int>();
        for (int r = 0; r < N; r++)
        {
            switch (spec[r])
            {
                case Spec.Pod: podList.Add(r); break;
                case Spec.Start: h[r] = (byte)(0x80 | ((down[r] ? 1 : 5) << 4)); v[r] = (byte)link[r]; break;
                case Spec.End: h[r] = (byte)(0x80 | ((down[r] ? 2 : 6) << 4)); v[r] = (byte)link[r]; break;
                case Spec.T: h[r] = (byte)(0x80 | ((down[r] ? 3 : 7) << 4)); v[r] = (byte)link[r]; break;
                case Spec.Single: h[r] = (byte)(0x80 | ((down[r] ? 0 : 4) << 4)); v[r] = (byte)link[r]; break;
            }
        }
        var bytes = new byte[MazeData.FileSize];
        void Store() { Array.Copy(h, 0, bytes, 0, N); Array.Copy(v, 0, bytes, 128, N); Array.Copy(obj, 0, bytes, 256, N); Array.Copy(col, 0, bytes, 384, N); }
        bytes[512] = (byte)podList.Count;
        for (int i = 0; i < podList.Count; i++) bytes[513 + i] = (byte)podList[i];
        bytes[512 + 0x7D] = (byte)ends[0]; bytes[512 + 0x7E] = (byte)ends[1]; bytes[512 + 0x7F] = (byte)ends[2];

        // interior walls of chambers: random '#' pieces, kept only if the room stays playable
        for (int r = 0; r < N; r++)
        {
            if (spec[r] != Spec.Chamber && spec[r] != Spec.T && spec[r] != Spec.Pod) continue;
            if (spec[r] != Spec.Chamber) continue;
            for (int t = 0; t < 40; t++)
            {
                int hb = 0, vb = 0;
                double p = 0.15 + rng.NextDouble() * 0.25;
                for (int b = 0; b < 6; b++) { if (rng.NextDouble() < p) hb |= 1 << b; if (rng.NextDouble() < p) vb |= 1 << b; }
                h[r] = (byte)hb; v[r] = (byte)vb; Store();
                if (MazeValidator.RoomPlayable(new MazeData("t", bytes), r)) break;
                h[r] = 0; v[r] = 0; Store();
            }
        }
        obj[N - 1] = (byte)ObjectKind.Shadow;
        foreach (int r in podList) if (r != N - 1) obj[r] = (byte)(rng.Next(2) == 0 ? ObjectKind.Potion : ObjectKind.Mystery);
        Store();

        // 6. locks, then keys placed inside the region reachable before each lock opens
        var chambers = Enumerable.Range(4, N - 8).Where(r => spec[r] == Spec.Chamber).OrderBy(_ => rng.Next()).ToList();
        var locks = new List<int>();
        int lockCount = Math.Min(o.Locks, Palette.Length);
        foreach (int r in chambers)
        {
            if (locks.Count >= lockCount) break;
            int idx = locks.Count;
            obj[r] = (byte)ObjectKind.Keyhole;
            col[r] = (byte)(Palette[idx] | (rng.Next(2) == 0 ? 0x10 : 0));
            locks.Add(r);
        }
        Store();
        var pending = new List<int>(locks);
        var opened = new HashSet<int>();
        var keyAt = new HashSet<int>();
        while (pending.Count > 0)
        {
            var maze = new MazeData("t", bytes);
            var reach = MazeValidator.Reachable(maze, opened);
            int pick = -1;
            foreach (int l in pending.OrderBy(_ => rng.Next())) if (reach.Contains(l)) { pick = l; break; }
            if (pick < 0) break;
            var cands = reach.Where(r => !keyAt.Contains(r) && obj[r] != (byte)ObjectKind.Keyhole && obj[r] != (byte)ObjectKind.Shadow
                                         && (spec[r] == Spec.Chamber || spec[r] == Spec.Pod) && r != 0).OrderBy(_ => rng.Next()).ToList();
            if (cands.Count == 0) break;
            int kr = cands[rng.Next(cands.Count)];
            obj[kr] = (byte)ObjectKind.Key; col[kr] = (byte)(col[pick] & 0xEF);
            keyAt.Add(kr); opened.Add(pick); pending.Remove(pick);
            Store();
        }
        if (locks.Count - pending.Count < Math.Min(4, lockCount)) return null; // too few working locks
        foreach (int l in pending) { obj[l] = 0; col[l] = 0; }   // unreachable locks are dropped
        Store();

        // 7. potions and mystery bonuses
        void Scatter(ObjectKind kind, int count)
        {
            var free = Enumerable.Range(1, N - 2).Where(r => spec[r] == Spec.Chamber && obj[r] == 0).OrderBy(_ => rng.Next()).Take(count).ToList();
            foreach (int r in free) obj[r] = (byte)kind;
        }
        Scatter(ObjectKind.Potion, o.Potions);
        Scatter(ObjectKind.Mystery, o.Bonuses);
        Store();
        return bytes;
    }
}
