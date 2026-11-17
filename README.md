# Shamus 3D

A free spiritual successor to **Shamus** (Synapse Software, 1982, Atari 8-bit): a remake with a top-down 3D camera.
The level is drawn with energy walls and force fields between generators, and there are two camera views: top-down and isometric. All objects are assembled from 3D primitives, all sounds and the ambient background are synthesized in code, and mazes can be generated procedurally. Each enemy has its own silhouette (a tracked robot, a "saucer" drone, a crystal jumper) and a random color from four options; shots come from the muzzle and from ports, with a flash, a smoke trail and an explosion on impact.

> Stack: .NET 10, C#, [Raylib-cs](https://github.com/ChrisDill/Raylib-cs) 8.1.0, xUnit.

## Quick Start

```bash
dotnet build
dotnet test
dotnet run --project src/Shamus.Game
```

Launch parameters:

| Flag | Purpose |
|---|---|
| `--maze NAME` | maze: `c64` (default), `atari`, `holmes`, `cluseau`, `marlowe`, `bond`, `random` |
| `--seed N` | seed for `--maze random` |
| `--room N` | skip the title screen and start at room N |
| `--settings` | debug: open the settings screen (for screenshots) |
| `--mute` | disable sound |
| `--debug` | `[` and `]` keys switch rooms |
| `--view top\|iso` | camera view: top-down or isometric |
| `--winsize WxH` | debug: initial window size (checking 16:9 framing) |
| `--shot file.png` | debug: save a single frame and exit (together with `--room`, `--view`) |
| `--shotframe N` | debug: which frame to save (the 8th by default) |
| `--fire` | debug: hold fire so the frame captures projectiles and effects |

## Controls

| Action | Keys |
|---|---|
| Movement (8 directions) | WASD, arrows, numpad (8/4/2/6, diagonals 7/9/1/3; with NumLock off, Home/PgUp/End/PgDn) |
| Fire | Space, Ctrl, Num 0, Num 5 |
| Map of visited rooms | M or Tab |
| View: top-down / isometric | V |
| Pause | P or Esc |
| Sound on/off | F2 |
| Reload `assets/config.json` | F5 (restarts the current room) |

Menu: up/down navigates, left/right changes the value, Enter confirms.

Gamepad (Xbox layout; the keyboard works in parallel):

| Action | Button |
|---|---|
| Movement and menu navigation | left stick or D-pad |
| Confirm in menu / fire | A |
| Back (pause in game) | B |
| Map | View (the two-windows button) |

The stick in a menu triggers on the edge and only on the leading axis, so a diagonal does not scroll the list and change the value at the same time.

In isometric view only the stick is screen-relative: its direction is recalculated against the current camera, so "right" drives the tank diagonally down the room, while "up-right" moves it straight along the X axis. In isometric view the keyboard moves along the room axes, as it did before gamepad support.

## Rules in a Nutshell

- Walk the maze of 128 rooms (4 levels) and shoot enemies. Walls block movement; by default, touching them is safe (`WALLS DON'T KILL`), but you can turn that option off to make them lethal.
- Keys open locks of the same color; a locked door in a room with a keyhole opens when you touch the keyhole with the right key.
- In pod rooms you must hit the object through the gaps in the moving barriers; after that the barriers disappear and enemies appear.
- If you linger in a room, an invulnerable Shadow appears. In the last room (127) the Shadow can be shot: this starts a new, harder loop.

Details: [docs/DESIGN.md](docs/DESIGN.md).

## Repository Structure

```
src/Shamus.Core/        game logic without Raylib (mazes, simulation)
src/Shamus.Game/        Raylib frontend (window, rendering, UI, sound)
tools/                  MazeExtract (ATR -> .maze), RoomCompare (comparison against screenshots)
tests/Shamus.Core.Tests xUnit tests for the core
assets/                 mazes/*.maze, maps/*.map, config.json
docs/                   documentation
artifacts/              source materials (ATR image, PDF, room screenshots), not yet included in commits
```

## Documentation

- [docs/DESIGN.md](docs/DESIGN.md): design document (gameplay, rules, balance, differences from the original, plans)
- [docs/TECHNICAL.md](docs/TECHNICAL.md): technical description (architecture, simulation, rendering, sound, configuration, tests)
- [docs/MAZE_FORMAT.md](docs/MAZE_FORMAT.md): maze format, room geometry, map, generator
- [docs/STATE.md](docs/STATE.md): current development state and project handoff to another machine

## Origins and Licenses

Shamus was written by William Mataga and published by Synapse Software in 1982. The maze data comes from `Shamus+.atr`
(the Shamus+ patch by slx, 2017, with conversion of the Commodore 64 maps, presumably made by Jack L. Thornton).
Copyright in the original game remains with its owners; the repository uses only the map data and the
format description, and no source code from the original is ported. For the terms of use of the patch and converter see
`artifacts/Shamus-Source-Code/Source/README.TXT`.
