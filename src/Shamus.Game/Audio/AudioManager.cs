using Raylib_cs;
using Shamus.Core.Simulation;

namespace Shamus.Game.Audio;

/// <summary>Owns the audio device, pre-renders all variants at startup and maps world events to sounds. Inactive without a device.</summary>
public sealed class AudioManager : IDisposable
{
    private readonly Dictionary<Sfx, Sound[]> _sounds = new();
    private readonly Random _rng = new(7);
    private readonly Dictionary<Sfx, float> _gain = new()
    {
        [Sfx.Shot] = 0.35f, [Sfx.HitWall] = 0.3f, [Sfx.HitBarrier] = 0.4f, [Sfx.BulletsClash] = 0.4f, [Sfx.EnemyShot] = 0.25f,
        [Sfx.EnemyDie] = 0.6f, [Sfx.PlayerDie] = 0.7f, [Sfx.GameOver] = 0.7f, [Sfx.RoomEnter] = 0.25f,
    };
    private readonly Dictionary<Sfx, int> _lastPlayed = new();

    // background ambience: a never-repeating real-time stream
    private AudioStream _music;
    private bool _musicOn;
    private readonly AmbientSynth _ambient = new();
    private readonly short[] _musicBuf = new short[ChunkFrames * 2];
    private const int ChunkFrames = 4096;
    public float MusicVolume { get; set; } = 0.6f;

    public bool Active { get; private set; }
    public bool Muted { get; set; }
    public float Volume { get; set; } = 0.8f;

    public void Init()
    {
        Raylib.InitAudioDevice();
        if (!Raylib.IsAudioDeviceReady()) return;

        // DSP is pure, so all variants render in parallel; uploading to Raylib happens on this (main) thread.
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var jobs = Enum.GetValues<Sfx>().SelectMany(s => Enumerable.Range(0, ProceduralSfx.VariantCount(s)).Select(v => (s, v))).ToList();
        var wavs = new byte[jobs.Count][];
        Parallel.For(0, jobs.Count, i => wavs[i] = Dsp.ToWav(ProceduralSfx.Create(jobs[i].s, jobs[i].v)));
        for (int i = 0; i < jobs.Count; i++)
        {
            var (s, v) = jobs[i];
            if (!_sounds.TryGetValue(s, out var arr)) _sounds[s] = arr = new Sound[ProceduralSfx.VariantCount(s)];
            var wave = Raylib.LoadWaveFromMemory(".wav", wavs[i]);
            arr[v] = Raylib.LoadSoundFromWave(wave);
            Raylib.UnloadWave(wave);
        }
        Active = true;
        Raylib.SetAudioStreamBufferSizeDefault(ChunkFrames);
        _music = Raylib.LoadAudioStream(AmbientSynth.Rate, 16, 2);
        _ambient.Render(_musicBuf, ChunkFrames);
        Push();
        Raylib.PlayAudioStream(_music);
        _musicOn = true;
        Console.WriteLine($"AUDIO: rendered {jobs.Count} procedural sounds in {sw.ElapsedMilliseconds} ms");
    }

    private unsafe void Push()
    {
        fixed (short* p = _musicBuf) Raylib.UpdateAudioStream(_music, p, ChunkFrames);
    }

    /// <summary>Call every frame: refills the ambience stream. tension 0..1 (danger), muffle 0..1 (menus, pause).</summary>
    public void UpdateMusic(float tension, float muffle)
    {
        if (!_musicOn) return;
        _ambient.TensionTarget = tension; _ambient.MuffleTarget = muffle;
        Raylib.SetAudioStreamVolume(_music, Muted ? 0f : MusicVolume * Volume);
        for (int i = 0; i < 2 && Raylib.IsAudioStreamProcessed(_music); i++)
        {
            _ambient.Render(_musicBuf, ChunkFrames);
            Push();
        }
    }

    public void Play(Sfx sfx, float volume = 1f)
    {
        if (!Active || Muted) return;
        var arr = _sounds[sfx];
        int idx = _rng.Next(arr.Length);
        // prefer a variant that is not currently playing so overlapping sounds do not cut each other off
        for (int k = 0; k < arr.Length; k++)
        {
            int c = (idx + k) % arr.Length;
            if (!Raylib.IsSoundPlaying(arr[c])) { idx = c; break; }
        }
        var snd = arr[idx];
        Raylib.SetSoundVolume(snd, Volume * volume * (_gain.TryGetValue(sfx, out var g) ? g : 0.6f));
        Raylib.SetSoundPitch(snd, 0.96f + (float)_rng.NextDouble() * 0.08f);
        Raylib.PlaySound(snd);
    }

    /// <summary>Maps simulation events (one tick's worth) to sounds.</summary>
    public void Handle(IReadOnlyList<WorldEvent> events)
    {
        foreach (var e in events)
        {
            switch (e.Kind)
            {
                case WorldEventKind.Shot: Play(Sfx.Shot); break;
                case WorldEventKind.BulletHitWall: Play(Sfx.HitWall); break;
                case WorldEventKind.BulletHitBarrier: Play(Sfx.HitBarrier); break;
                case WorldEventKind.BulletsCancelled: Play(Sfx.BulletsClash); break;
                case WorldEventKind.EnemyShot: Play(Sfx.EnemyShot); break;
                case WorldEventKind.EnemyKilled: Play(Sfx.EnemyDie); break;
                case WorldEventKind.PlayerDied: Play(Sfx.PlayerDie); break;
                case WorldEventKind.GameOver: Play(Sfx.GameOver); break;
                case WorldEventKind.KeyPicked: Play(Sfx.Key); break;
                case WorldEventKind.DoorOpened: Play(Sfx.Door); break;
                case WorldEventKind.PotionPicked: Play(Sfx.Potion); break;
                case WorldEventKind.BonusPicked: Play(Sfx.Bonus); break;
                case WorldEventKind.PodOpened: Play(Sfx.PodOpen); break;
                case WorldEventKind.ShadowWarning: Play(Sfx.ShadowWarn); break;
                case WorldEventKind.ShadowAppeared: Play(Sfx.ShadowAppear); break;
                case WorldEventKind.BulletHitShadow: Play(Sfx.HitBarrier, 0.6f); break;
                case WorldEventKind.ShadowKilled: Play(Sfx.ShadowDie); break;
                case WorldEventKind.CycleStarted: Play(Sfx.NewLoop); break;
                case WorldEventKind.RoomEntered: Play(Sfx.RoomEnter); break;
            }
        }
    }

    public void Dispose()
    {
        if (!Active) return;
        if (_musicOn) { Raylib.StopAudioStream(_music); Raylib.UnloadAudioStream(_music); }
        foreach (var arr in _sounds.Values) foreach (var s in arr) Raylib.UnloadSound(s);
        Raylib.CloseAudioDevice();
        Active = false;
    }
}
