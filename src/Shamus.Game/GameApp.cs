using Raylib_cs;
using Shamus.Core;
using Shamus.Core.Mazes;
using Shamus.Core.Simulation;
using Shamus.Game.Audio;
using Shamus.Game.Rendering;
using Shamus.Game.Ui;

namespace Shamus.Game;

public enum AppState { Title, Playing, Paused, GameOver, Map }

/// <summary>Window, state machine (title / playing / paused / game over), fixed-step loop.</summary>
public sealed class GameApp
{
    private const int W = 1280, H = 720;
    private const float Step = 1f / 60f;

    private static readonly string[] MazeOrder = { "c64", "atari", "holmes", "cluseau", "marlowe", "bond" };
    private static readonly Dictionary<string, string> MazeLabels = new()
    {
        ["c64"] = "ORIGINAL C64", ["atari"] = "ORIGINAL ATARI", ["holmes"] = "HOLMES", ["cluseau"] = "CLUSEAU", ["marlowe"] = "MARLOWE", ["bond"] = "BOND", ["random"] = "PROCEDURAL",
    };

    private readonly string[] _args;
    private readonly Settings _settings = Settings.Load();
    private readonly AudioManager _audio = new();
    private SceneRenderer _scene = null!;
    private readonly Hud _hud = new();
    private readonly Menu _titleMenu = new(), _pauseMenu = new();
    private readonly List<string> _mazes = new();
    private readonly bool _debug;

    private AppState _state = AppState.Title;
    private GameConfig _config = new();
    private MazeData _maze = null!;
    private MapLayout _map = null!;
    private GameWorld _world = null!;
    private int _startRoom;
    private float _acc, _gameOverTimer;
    private bool _newHigh, _quit;

    public GameApp(string[] args)
    {
        _args = args;
        _debug = args.Contains("--debug");
    }

    private string Arg(string name) { int i = Array.IndexOf(_args, name); return i >= 0 && i + 1 < _args.Length ? _args[i + 1] : ""; }

    private static string ConfigPath()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            var p = Path.Combine(d.FullName, "assets", "config.json");
            if (File.Exists(p) && Directory.Exists(Path.Combine(d.FullName, "src"))) return p;
        }
        return Path.Combine(AppContext.BaseDirectory, "assets", "config.json");
    }

    private GameConfig BuildConfig() => GameConfig.Load(ConfigPath()).WithDifficulty(_settings.Difficulty).WithGodMode(_settings.GodMode);

    private MazeData LoadMaze() =>
        _settings.Maze == "random" ? ProceduralMaze.Generate(_settings.Seed) : MazeLoader.Load(MazePath(_settings.Maze));

    private static string MazePath(string name) => Path.Combine(AppContext.BaseDirectory, "assets", "mazes", name + ".maze");

    private void LoadMazeList()
    {
        foreach (var n in MazeOrder) if (File.Exists(MazePath(n))) _mazes.Add(n);
        _mazes.Add("random");
        if (_mazes.Count == 0) throw new FileNotFoundException("No maze files found in assets/mazes");
        if (Arg("--maze") is { Length: > 0 } m && _mazes.Contains(m)) _settings.Maze = m;
        if (int.TryParse(Arg("--seed"), out int seed) && seed > 0) _settings.Seed = seed;
        if (!_mazes.Contains(_settings.Maze)) _settings.Maze = _mazes[0];
    }

    private void BuildMenus()
    {
        _titleMenu.Items.Add(new MenuItem { Label = () => "START GAME", OnEnter = () => StartGame(0) });
        _titleMenu.Items.Add(new MenuItem
        {
            Label = () => "MAZE: " + MazeLabels.GetValueOrDefault(_settings.Maze, _settings.Maze),
            OnChange = d => { int i = (_mazes.IndexOf(_settings.Maze) + d + _mazes.Count) % _mazes.Count; _settings.Maze = _mazes[i]; _settings.Save(); LoadDemo(); },
        });
        _titleMenu.Items.Add(new MenuItem
        {
            Label = () => _settings.Maze == "random" ? $"SEED: {_settings.Seed}  (ENTER = random)" : "SEED: -",
            OnChange = d => { if (_settings.Maze == "random") { _settings.Seed = Math.Max(1, _settings.Seed + d); _settings.Save(); LoadDemo(); } },
            OnEnter = () => { if (_settings.Maze == "random") { _settings.Seed = Random.Shared.Next(1, 100000); _settings.Save(); LoadDemo(); } },
        });
        _titleMenu.Items.Add(new MenuItem
        {
            Label = () => "DIFFICULTY: " + _settings.Difficulty.ToString().ToUpperInvariant(),
            OnChange = d => { _settings.Difficulty = (Difficulty)(((int)_settings.Difficulty + d + 3) % 3); _settings.Save(); },
        });
        _titleMenu.Items.Add(new MenuItem
        {
            Label = () => "VOLUME: " + (_settings.Muted ? "OFF" : $"{(int)Math.Round(_settings.Volume * 100)}%"),
            OnChange = d =>
            {
                if (_settings.Muted && d > 0) _settings.Muted = false;
                else { _settings.Volume = Math.Clamp(_settings.Volume + d * 0.1f, 0f, 1f); _settings.Muted = _settings.Volume <= 0.001f; }
                ApplyAudioSettings(); _settings.Save(); _audio.Play(Sfx.RoomEnter);
            },
        });
        _titleMenu.Items.Add(new MenuItem
        {
            Label = () => "MUSIC: " + (_settings.MusicVolume <= 0.001f ? "OFF" : $"{(int)Math.Round(_settings.MusicVolume * 100)}%"),
            OnChange = d => { _settings.MusicVolume = Math.Clamp(_settings.MusicVolume + d * 0.1f, 0f, 1f); ApplyAudioSettings(); _settings.Save(); },
        });
        _titleMenu.Items.Add(new MenuItem
        {
            Label = () => "GOD MODE: " + (_settings.GodMode ? "ON (test)" : "OFF"),
            OnChange = _ => { _settings.GodMode = !_settings.GodMode; _settings.Save(); },
        });
        _titleMenu.Items.Add(new MenuItem { Label = () => "QUIT", OnEnter = () => _quit = true });

        _pauseMenu.Items.Add(new MenuItem { Label = () => "RESUME", OnEnter = () => _state = AppState.Playing });
        _pauseMenu.Items.Add(new MenuItem { Label = () => "RESTART", OnEnter = () => { SubmitScore(); StartGame(_startRoom); } });
        _pauseMenu.Items.Add(new MenuItem { Label = () => "MAIN MENU", OnEnter = ToTitle });
    }

    private void ApplyAudioSettings() { _audio.Muted = _settings.Muted || _args.Contains("--mute"); _audio.Volume = _settings.Volume; _audio.MusicVolume = _settings.MusicVolume; }

    private void LoadDemo()
    {
        _maze = LoadMaze();
        _config = BuildConfig();
        _world = new GameWorld(_maze, _config, 0);
        _map = MapLayout.Load(Path.Combine(AppContext.BaseDirectory, "assets", "maps"), _maze);
    }

    private void StartGame(int room)
    {
        _maze = LoadMaze();
        _config = BuildConfig();
        _startRoom = room;
        _world = new GameWorld(_maze, _config, room);
        _acc = 0; _gameOverTimer = 0; _newHigh = false;
        _state = AppState.Playing;
    }

    private void SubmitScore() { if (_world != null && _state != AppState.Title && !_config.GodMode) _newHigh |= _settings.Submit(_settings.Maze, _world.Score); }

    private void ToTitle() { SubmitScore(); _state = AppState.Title; _titleMenu.Reset(); LoadDemo(); }

    public void Run()
    {
        LoadMazeList();
        Raylib.SetConfigFlags(ConfigFlags.Msaa4xHint);
        Raylib.InitWindow(W, H, "Shamus 3D");
        Raylib.SetExitKey(KeyboardKey.Null);
        Raylib.SetTargetFPS(60);
        _scene = new SceneRenderer(_settings.View);
        string view = Arg("--view");
        if (view is "top" or "iso") _scene.Rig.SetMode(view == "iso" ? ViewMode.Isometric : ViewMode.Top, instant: true);
        if (float.TryParse(Arg("--zoom"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float zoom)) _scene.Rig.Zoom = zoom;
        var look = Arg("--look").Split(',');
        if (look.Length == 2 && float.TryParse(look[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float lx)
            && float.TryParse(look[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float ly))
            _scene.Rig.Look = new System.Numerics.Vector3(lx - 20f, 0, ly - 11f);
        string shot = Arg("--shot");
        int frame = 0;
        _audio.Init();
        ApplyAudioSettings();
        BuildMenus();
        _titleMenu.OnMove = _pauseMenu.OnMove = () => _audio.Play(Sfx.RoomEnter, 0.6f);
        LoadDemo();
        if (Arg("--room") is { Length: > 0 } r && int.TryParse(r, out int room)) StartGame(room);

        while (!Raylib.WindowShouldClose() && !_quit)
        {
            float dt = Raylib.GetFrameTime();
            Update(dt);
            Raylib.BeginDrawing();
            Raylib.ClearBackground(new Color(8, 8, 14, 255));
            Draw(dt);
            Raylib.EndDrawing();
            if (shot.Length > 0 && ++frame == 8)   // debug: save one frame and quit
            {
                var img = Raylib.LoadImageFromScreen();
                Raylib.ExportImage(img, shot);
                Raylib.UnloadImage(img);
                break;
            }
        }
        _scene.Dispose();
        SubmitScore();
        _settings.Save();
        _audio.Dispose();
        Raylib.CloseWindow();
    }

    private void Update(float dt)
    {
        if (Raylib.IsKeyPressed(KeyboardKey.F2)) { _settings.Muted = !_settings.Muted; ApplyAudioSettings(); _settings.Save(); }
        _hud.Update(dt);
        _scene.Rig.Update(dt);
        if (Raylib.IsKeyPressed(KeyboardKey.V))   // works in every state, including pause and map
        {
            _scene.Rig.Toggle(); _settings.View = _scene.Rig.Mode; _settings.Save();
        }
        UpdateAmbience();

        switch (_state)
        {
            case AppState.Title:
                _titleMenu.Update();
                if (Raylib.IsKeyPressed(KeyboardKey.Escape)) _quit = true;
                break;

            case AppState.Map:
                if (Raylib.IsKeyPressed(KeyboardKey.M) || Raylib.IsKeyPressed(KeyboardKey.Escape) || Raylib.IsKeyPressed(KeyboardKey.Tab)) _state = AppState.Playing;
                break;

            case AppState.Paused:
                _pauseMenu.Update();
                if (Raylib.IsKeyPressed(KeyboardKey.Escape) || Raylib.IsKeyPressed(KeyboardKey.P)) _state = AppState.Playing;
                break;

            case AppState.GameOver:
                if (Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.Space)) StartGame(_startRoom);
                if (Raylib.IsKeyPressed(KeyboardKey.Escape)) ToTitle();
                break;

            case AppState.Playing:
                if (Raylib.IsKeyPressed(KeyboardKey.M) || Raylib.IsKeyPressed(KeyboardKey.Tab)) { _state = AppState.Map; break; }
                if (Raylib.IsKeyPressed(KeyboardKey.Escape) || Raylib.IsKeyPressed(KeyboardKey.P)) { _pauseMenu.Reset(); _state = AppState.Paused; break; }
                if (Raylib.IsKeyPressed(KeyboardKey.F5)) { _config = BuildConfig(); _world = new GameWorld(_maze, _config, _world.Room); }
                if (_debug && Raylib.IsKeyPressed(KeyboardKey.RightBracket)) _world = new GameWorld(_maze, _config, _world.Room + 1);
                if (_debug && Raylib.IsKeyPressed(KeyboardKey.LeftBracket)) _world = new GameWorld(_maze, _config, _world.Room - 1);

                // WASD, arrows and the numpad (8/4/2/6 plus 7/9/1/3 diagonals; Home/PgUp/End/PgDn when NumLock is off)
                static bool Down(params KeyboardKey[] k) { foreach (var x in k) if (Raylib.IsKeyDown(x)) return true; return false; }
                bool left = Down(KeyboardKey.A, KeyboardKey.Left, KeyboardKey.Kp4, KeyboardKey.Kp7, KeyboardKey.Kp1, KeyboardKey.Home, KeyboardKey.End);
                bool right = Down(KeyboardKey.D, KeyboardKey.Right, KeyboardKey.Kp6, KeyboardKey.Kp9, KeyboardKey.Kp3, KeyboardKey.PageUp, KeyboardKey.PageDown);
                bool up = Down(KeyboardKey.W, KeyboardKey.Up, KeyboardKey.Kp8, KeyboardKey.Kp7, KeyboardKey.Kp9, KeyboardKey.Home, KeyboardKey.PageUp);
                bool dn = Down(KeyboardKey.S, KeyboardKey.Down, KeyboardKey.Kp2, KeyboardKey.Kp1, KeyboardKey.Kp3, KeyboardKey.End, KeyboardKey.PageDown);
                int mx = (right ? 1 : 0) - (left ? 1 : 0), my = (dn ? 1 : 0) - (up ? 1 : 0);
                bool fire = Down(KeyboardKey.Space, KeyboardKey.LeftControl, KeyboardKey.RightControl, KeyboardKey.Kp0, KeyboardKey.Kp5);

                _acc = MathF.Min(_acc + dt, 0.1f);
                while (_acc >= Step)
                {
                    _world.Tick(new InputState(mx, my, fire));
                    _audio.Handle(_world.Events);
                    _hud.OnEvents(_world.Events);
                    _acc -= Step;
                }
                if (_world.Phase == GamePhase.GameOver)
                {
                    _gameOverTimer += dt;
                    if (_gameOverTimer > 1.8f) { SubmitScore(); _state = AppState.GameOver; }
                }
                break;
        }
    }

    /// <summary>Feeds the ambient stream: more tension with enemies nearby, Shadow warning/chase; muffled in menus.</summary>
    private void UpdateAmbience()
    {
        float tension = 0, muffle = 0;
        switch (_state)
        {
            case AppState.Title: muffle = 0.55f; break;
            case AppState.Paused: case AppState.Map: muffle = 0.8f; tension = 0.1f; break;
            case AppState.GameOver: muffle = 0.7f; break;
            default:
                tension = Math.Min(1f, _world.Enemies.Count / 7f * 0.55f + (_world.ShadowWarned ? 0.25f : 0f) + (_world.ShadowActive ? 0.55f : 0f));
                if (_world.IsPodRoom && !_world.PodOpen) tension = Math.Max(tension, 0.3f);
                break;
        }
        _audio.UpdateMusic(tension, muffle);
    }

    private void Draw(float dt)
    {
        float alpha = _state == AppState.Playing ? _acc / Step : 1f;
        _scene.Draw(_world, _config, alpha);

        switch (_state)
        {
            case AppState.Title:
                Raylib.DrawRectangle(0, 0, W, H, new Color(0, 0, 0, 150));
                Hud.TextCentered("SHAMUS 3D", 70, 96, Color.Gold);
                Hud.TextCentered("a spiritual successor to the 1982 classic", 175, 24, Color.LightGray);
                _titleMenu.Draw(270, W);
                Hud.TextCentered("UP/DOWN select    LEFT/RIGHT change    ENTER confirm", H - 110, 20, Color.Gray);
                Hud.TextCentered($"HIGH SCORE ({MazeLabels.GetValueOrDefault(_settings.Maze, _settings.Maze)}): {_settings.HighScore(_settings.Maze):D6}", H - 70, 22, Color.Gold);
                break;

            case AppState.Playing:
                _hud.Draw(_world, _maze.Name, _settings.HighScore(_settings.Maze), W, H, _world.TickCount < 600);
                break;

            case AppState.Map:
                MapView.Draw(_map, _world, W, H);
                break;

            case AppState.Paused:
                _hud.Draw(_world, _maze.Name, _settings.HighScore(_settings.Maze), W, H, false);
                Raylib.DrawRectangle(0, 0, W, H, new Color(0, 0, 0, 170));
                Hud.TextCentered("PAUSED", 190, 72, Color.Gold);
                _pauseMenu.Draw(310, W);
                break;

            case AppState.GameOver:
                Raylib.DrawRectangle(0, 0, W, H, new Color(0, 0, 0, 190));
                Hud.TextCentered("GAME OVER", 190, 84, Color.Red);
                Hud.TextCentered($"SCORE {_world.Score:D6}", 320, 40, Color.White);
                Hud.TextCentered(_newHigh ? "NEW HIGH SCORE!" : $"HIGH SCORE {_settings.HighScore(_settings.Maze):D6}", 380, 30, Color.Gold);
                Hud.TextCentered($"ROOM {_world.Room}   LOOP {_world.Cycle + 1}", 430, 24, Color.LightGray);
                Hud.TextCentered("ENTER play again     ESC main menu", 520, 24, Color.Gray);
                break;
        }
        if (_audio.Muted && _state != AppState.Title) Hud.Text("MUTED", W - 110, 54, 20, Color.Gray);
    }
}
