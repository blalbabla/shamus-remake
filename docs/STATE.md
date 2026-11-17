# Shamus 3D: Development State (Handoff)

A document for continuing work on another machine. Date: 2026-11-17.
Repository: <https://github.com/blalbabla/shamus-remake>, branch `main`.

This is a snapshot of the current state, not a replacement for [DESIGN.md](DESIGN.md), [TECHNICAL.md](TECHNICAL.md) and
[MAZE_FORMAT.md](MAZE_FORMAT.md). If you change mechanics or architecture, update those as well.

## 1. What This Is

A free remake of Shamus (Synapse, 1982): 128 rooms, 4 levels, enemies, keys and locks, pod rooms,
Shadow, procedural mazes. Top-down or isometric view, everything built from 3D primitives, sound synthesized in code.
Stack: .NET 10, C#, Raylib-cs 8.1.0, xUnit.

Status: playable MVP. All the basic mechanics of the original are in place, plus procedural mazes,
a map, high scores, settings, lighting with shadows. The player and all three enemies are visually "dressed", there are projectiles
with effects (flash, trail, explosion). The score and weapon sounds were reworked in the latest iteration: an
electronic score and layered launch/flight/impact effects for the tank and the robots.

## 2. Quick Start on a New Machine

You need:

- Windows 10/11 (the project was tested on Windows; the Raylib package pulls in the native libraries itself).
- .NET SDK 10.0.x (verified on 10.0.301/10.0.302). Check: `dotnet --version`.
- A GPU/driver with OpenGL 3.3 (lighting shader). Without it the game starts without lighting (fallback).
- Internet access for `dotnet restore` (NuGet: Raylib-cs, xUnit).

```bash
git clone https://github.com/blalbabla/shamus-remake.git
cd shamus-remake
dotnet build          # Shamus.sln solution
dotnet test           # 88 core tests
dotnet run --project src/Shamus.Game
```

> The directory is named `shamus-remake` after cloning; in this document paths are given from the repository root.

Useful flags:

| Flag | Purpose |
|---|---|
| `--maze c64\|atari\|holmes\|cluseau\|marlowe\|bond\|random` | maze |
| `--seed N` | seed for `random` |
| `--room N` | go straight to room N, skipping the title screen |
| `--view top\|iso` | camera view |
| `--settings` | open the settings screen (for debugging/screenshots) |
| `--winsize WxH` | initial window size (checking 16:9 letterbox) |
| `--shot file.png` | save a frame (frame 8 by default) and exit |
| `--shotframe N` | which frame `--shot` captures (catching effects: shot, hit) |
| `--fire` | debug: hold fire continuously so projectiles and effects appear in the screenshot |
| `--zoom F`, `--look X,Y` | debug zoom and look-at point |
| `--mute` | no sound |
| `--debug` | `[` / `]` switch rooms |

User settings and high scores: `%APPDATA%\Shamus3D\settings.json` (recreated from scratch on a new machine).
They also store `Fullscreen`, volume, the selected maze, difficulty, `WallsDontKill` (default `true`), God mode, camera view.

`assets/config.json` is read from the source tree (the game looks for a directory that has both `assets` and `src`), so
in a dev run config edits are picked up without a rebuild (F5 in the game restarts the room with the new
config). For publish, a copy is also placed next to the exe — see §7.

## 3. Structure

```
src/Shamus.Core/                 logic without Raylib (~1.4k lines)
  GameConfig.cs                  constants, JSON, difficulty presets
  Simulation/GameWorld.cs        deterministic simulation (60 Hz tick), events, tank heading
  Simulation/Enemy.cs            enemies: position, DirX/DirY, Heading/PrevHeading, Blink*, ColorIndex
  Simulation/RoomObject.cs       room objects
  Simulation/WorldEvent.cs       events to the outside world (sound, HUD, effects)
  Mazes/MazeData.cs              640 bytes, 128 rooms
  Mazes/RoomGrid.cs              40x22 room grid (Thick/Thin)
  Mazes/RoomWalls.cs             wall segments + generator posts
  Mazes/RoomFloor.cs             walkable area (BFS from the hero) + plateau outline for isometric view
  Mazes/ProceduralMaze.cs        generator
  Mazes/MazeValidator.cs         structure/passability checks
  Mazes/MapLayout.cs             map by level
src/Shamus.Game/                 Raylib frontend (~2.6k lines)
  GameApp.cs                     window, states, letterbox, menus, settings, debug flags
  Program.cs                     entry point
  Rendering/SceneRenderer.cs     3D scene: floor, objects, tank, enemies, projectiles, effects, glow emission
  Rendering/RoomView.cs          floor/closed zones, plateau slab in isometric view, posts, energy walls, room name
  Rendering/CameraRig.cs         orthographic top/iso camera + custom 16:9 projection
  Rendering/LightingShader.cs    Lambert + ambient + 5x5 PCF shadows
  Rendering/ShadowMap.cs         depth FBO 2048, orthographic light camera
  Rendering/GlowMap.cs           emission at 1/4 frame, 2 blurs, additive composition
  Rendering/ParticleFx.cs        cosmetics: muzzle flash, projectile trail, hit explosion
  Rendering/RoomMetrics.cs       single height scale (wall = 0.5 cell, lower than the characters)
  Rendering/EnemyPalette.cs      four enemy color schemes (no green) by Enemy.ColorIndex
  Rendering/AtariPalette.cs      Atari palette
  Ui/                            Hud, Menu, SettingsMenu, Settings, MapView, GameInput
  Audio/                         Dsp, ProceduralSfx, AmbientSynth, AudioManager
tools/Shamus.MazeExtract/        ATR -> .maze
tools/Shamus.RoomCompare/        compare RoomGrid against screenshots
tests/Shamus.Core.Tests/         xUnit, 88 tests
assets/                          mazes/*.maze, maps/*.map, config.json, fonts
docs/                            DESIGN.md, TECHNICAL.md, MAZE_FORMAT.md, this file
artifacts/                       source material and screenshots; in .gitignore, absent on a new machine
```

## 4. What Is Implemented

- **World:** 128 rooms, 4 levels; chamber/corridor/pod; doors with keys; room boundaries 0 and 127 are sealed.
- **Simulation:** deterministic `GameWorld.Tick` (60 Hz), 3 enemy types, bullets, objects, Shadow,
  pod barriers, new loops; difficulty, God mode and the walls-don't-kill option via `GameConfig`. The "turn first,
  then move" rule for the tank is part of the simulation, not of the view.
- **Mazes:** 6 original `.maze` files + a procedural generator with a validator; `.map` map layout.
- **Player:** tank (tracks, hull, dome turret, barrel), smooth turning, projectile from the muzzle.
- **Enemies:** Robo-Droid (tracks + drum with a slot and a running beacon), Spiral Drone ("saucer" with
  spikes and a turret with three slits/ports), Snap Jumper (faceted crystal: teleport + flicker).
  The color scheme is random out of four colors without green, deterministically.
- **Projectiles and effects:** the player bullet is a small cylinder with a cone, launched from the muzzle; enemy bolts are
  plasma balls from their own ports; muzzle flash, smoky trail with fire, explosion with sparks and smoke.
- **Items:** key (ring, shaft, bit) and keyhole (disk with a black keyhole) lie flat in both
  projections; the potion is a big plus, "?" is a question mark. All levitate; the key rotates, the plus and "?" in
  isometric view stand upright and rotate, in top view they lie flat and do not rotate.
- **Rendering:** orthographic top/isometric camera; 16:9 letterbox in a window of any size and in fullscreen
  (rendered at the native window/display resolution, MSAA 4x); walls are vertical semi-transparent pink
  energy walls; generator posts; directional light with shadows (2048, PCF 5x5); glow
  of energy walls, generator "caps", pod room barriers, projectiles and effects. The heights of everything
  standing in the room are consistent via `RoomMetrics`. In isometric view the room is a plateau slab over the walkable area
  (chamber is a thick "+", corridor is a passage), walls along the outline, a cliff outside.
- **UI:** title screen (menu only), SETTINGS (Gameplay/Video/Audio), pause, Game Over, map,
  HUD, toasts, high scores per maze.
- **Input:** keyboard and gamepad (`Ui/GameInput.cs`): left stick/D-pad for movement and menus, A for
  confirm in menus and fire, B for back (in game — pause), View for map. The keyboard works
  in parallel and in isometric view moves along the room axes, as it did before the gamepad.
- **Sound:** 20 procedural effects (2–5 variants), live electronic score (bass sequencer, saw pad, arpeggio, machine noise,
  digital blips, kick), separate volumes for effects/music; weapon sounds are layered (launch, flight, impact).
- **Tools/tests:** map extraction from ATR, room comparison against screenshots, 88 xUnit tests.

## 5. How a Frame Is Built (Important When Changing Rendering)

Order in `GameApp.Run`:

1. `BeginDrawing`, clear to black.
2. `SceneRenderer.RenderShadow(world, alpha)` — the light's depth map into a 2048 FBO (it switches the FBO and
   resets matrices, so it is called before letterbox setup).
3. `SceneRenderer.RenderGlow(world, config)` — emitters only (energy walls, generator "caps",
   projectiles, pod barriers, Shadow's eyes, the robot's beacon, the jumper's crystal, particles) into a 320x180 FBO, then
   two separable blurs. It also switches the FBO, so it too comes before letterbox.
4. Compute viewport/scissor: a 1280x720 frame is fitted into the window with 16:9 bars.
5. 2D orthographic projection `0..1280x720` (UI is drawn in these coordinates and scaled into the viewport).
6. `Draw`: scene, HUD, menus.

`SceneRenderer.Draw`:

1. `Rig.BeginView()` — custom 16:9 orthographic projection (`Raymath.MatrixOrtho` + `MatrixLookAt`).
   `Raylib.BeginMode3D` is not used: it takes the aspect ratio from the window and would distort the scene.
2. `LightingShader.Begin()` → `DrawWorld(...)` — floor, closed zones, posts, objects, tank, enemies,
   Shadow (all opaque, lit and casting shadows).
3. In the same place, still under the shader: `DrawJumpers(...)` — jumpers are lit (crystal faces), but with depth writing
   disabled, because they are semi-transparent.
4. `LightingShader.End()`.
5. Without lighting: projectiles of both sides, particles (`ParticleFx.Draw`), room name, pod barriers
   (additive), energy walls.
6. `Rig.EndView()`; `_glow.Composite()` — the finished glow additively over the scene (a 2D pass in
   1280x720 coordinates, therefore strictly after `EndView`).
7. 2D "?" label for mystery.

`RenderShadow` draws only "casters" into the depth map: posts, objects, tank, enemies (the jumper —
as a crystal silhouette). The floor is not written to the map, otherwise it would shadow itself. `RenderGlow` writes into its own
FBO only emitters (no depth test: there is nothing to compare against), the blur is a 9-tap Gaussian horizontally and vertically.

Pitfalls already verified in practice:

- Before `Rlgl.DisableDepthMask()` you need `Rlgl.DrawRenderBatchActive()`. Otherwise raylib flushes
  the accumulated batch already without depth writing, and semi-transparent geometry lands on top of the posts. The same
  is required for jumpers and particles.
- A semi-transparent object **must not write depth**, otherwise it cuts a hole in the floor behind it. Jumpers
  are drawn in the lit pass (light on the faces is needed), but under `DisableDepthMask`; projectiles and particles —
  in the unlit pass.
- The shadow texture is bound via `Raylib.SetShaderValueTexture(...)`: raylib treats the texture id
  as a sampler unit. Manual binding to unit 1 via `Rlgl` did not work.
- `LightViewProjection = Matrix4x4.Multiply(Rlgl.GetMatrixProjection(), Rlgl.GetMatrixModelview())` —
  that is exactly the argument order (raylib stores matrices column-vector style).
- The tank, drone, robot and jumper are drawn via `Rlgl.PushMatrix/Translatef/Rotatef`. rlgl transforms
  vertices on the CPU, so `fragPos` in the shader stays in world space. If you replace this with a shader model matrix,
  shadows on the model will break.
- `DrawCylinder`/`DrawCylinderEx` in raylib: for `DrawCylinder` `position` is **the center of the base**, not
  the middle; for `DrawCylinderEx` you specify two points and two radii (that is how truncated cones and
  bipyramids are built). It is easy to get this wrong when tuning heights.
- MSAA 4x is enabled on the window; the shadow FBO and the glow FBO have no MSAA.
- The glow (`GlowMap`) is rasterized into a 320x180 FBO (1/4 frame). It is a separate pass without geometry,
  so there is nothing to compare depth against: an emitter behind a wall still glows. For bloom this is acceptable,
  but do not try to "fix" it with a depth test in this FBO — you need the main scene's depth buffer.
- `_glow.Composite()` is called only after `Rig.EndView()`: it is a 2D quad in 1280x720 coordinates,
  and in a 3D projection it would drift. And only before the HUD — the interface must not glow.

## 6. What Was Done in This Iteration (16.11.2026)

**Scale and Light**

- Introduced `RoomMetrics`: one scale for everything standing in the room (1 unit = 1 cell). Wall
  (energy wall) = 0.5, post 0.9x0.5, pod barrier = wall height, tank 0.73, enemies no taller than 0.96,
  items up to 0.96, Shadow 1.45.
- Walls and posts reduced by half (1.0 → 0.5): at 1.0 the panels in isometric view covered the tank and enemies.
- Glow (`GlowMap`): emission into a 1/4-frame FBO, two separable blurs, additive composition.
- Shadows: map 1024 → 2048, PCF 3x3 → 5x5 with radial weights, smaller bias.

**Player**

- The tank was reworked for a readable silhouette; the hull was lowered by half the track height (height 0.73).
- Turning became a simulation rule: `Heading`/`PrevHeading` + `UpdateHeading`; while the remainder is greater than
  `TurnToleranceDeg`, `MovePlayer` does not move — "turn first, then move". Timings:
  45° ≈ 4 ticks (67 ms), 90° ≈ 5 (83 ms), 180° ≈ 7 (117 ms). Firing does not wait for the turn to finish.
- The projectile leaves the muzzle (`GameConfig.MuzzleLength` = 0.74) and looks like a bullet: a cylinder with the
  diameter of the barrel + a cone on the nose, at barrel height `RoomMetrics.BarrelY`.

**Projectiles and Effects**

- `Rendering/ParticleFx.cs`: muzzle flash and sparks, smoky trail with fire at the tail (emission per tick,
  not per frame), explosion with flash, sparks and smoke. Smoke is alpha, light is additive, everything glows in the glow pass too,
  and freezes on pause. Cap of 700 particles.
- Enemy projectiles brought to the same scale: a plasma ball 0.085 with a core of 0.045, its own trail of
  embers, a flash at the shooter for every fresh bolt.

**Plateau in Isometric View**

- `RoomFloor.Mask` builds the walkable area: BFS from the player's cell over `Empty` + one ring of neighboring
  `Thin`/`Thick` cells + the cells under all generators, so that posts stand on the plateau and do not float.
  `RoomFloor.MergedBoundary` returns the axial edges merged into long segments (no per-cell seams on the walls).
- `RoomView.DrawPlateauFloor` draws the floor as merged runs over the mask, on top — dark metal slabs (2×2, 1×1 each on uneven edges) with bevel and seams; `DrawPlateauSides` — a wall on each edge and
  a corner cube at each vertex. The sides go into the **unlit pass** with two flat tones: the south faces
  (bottom-left to top-right on screen) — the dark `CliffShade`, the west ones — the base `CliffColor`; the corner cubes —
  the base color, without a dark overlay; under each generator — a column 1.02 × depth of the base color, with the
  post standing on it. On the sides — dark vertical veins (`Veins`), deterministic by room number.
  Lighting and shadows are not computed on them, so there is no shaded return face and no false "recesses". The chamber is a thick
  "+", the corridor is the shape of the passage; outside is a cliff.
- The plateau is enabled at `Rig.IsometricBlend > 0.5`; the top view is unchanged.

**Items**

- `SceneRenderer.DrawItem` and four models: `KeyModel` (ring head, shaft, bit), `KeyholeModel`
  (disk with a black keyhole), `PlusModel` (potion) and `QuestionModel` (mystery). All levitate, heights —
  `RoomMetrics.PropHover` / `KeyholeHover` / `StandingPropHover`.
- The key and the keyhole lie flat in both projections; the key rotates around the vertical axis (30°/s), the keyhole
  is motionless. The plus and "?" in isometric view stand upright and rotate around the vertical axis, in top view they lie flat and
  do not rotate; the tilt is computed from `CameraRig.IsometricBlend`, so switching views is smooth.
- The "?" is additionally drawn in the glow pass; the 2D "?" label was removed as unnecessary.

**Enemies**

- Random color scheme: `EnemyPalette` (magenta, amber, cyan, crimson; Body/Light/Dark), no green.
  The slot is chosen by the simulation on spawn (`Enemy.ColorIndex`, `EnemyColorCount` = 4) — deterministically.
- Spiral Drone per the reference: a "saucer" R 0.58 with spikes and a tapering turret with three slits; the drone
  looks at the player, the slits at ±0.35 rad match the fan angles — bolts fly out of the slits.
- Robo-Droid simplified: two tracks with a light plate, hull, drum turret with a dark slot and a running
  red beacon. Fires from the turret at slot height; the hull smoothly turns toward the movement axis
  via the shared `EaseHeading`.
- Snap Jumper: the jump became a real teleport (2 cells once per second, only into a free cell),
  the effect is a cross-fade: the ghost at the old spot fades out, the body fades in at the new one. The look is a faceted
  crystal with a rotating core and two shards; at rest it flickers (0.35…1.0 opacity).

**Infrastructure**

- `assets/config.json` added to `<Content>`: it now ends up both in `bin` and in `publish` (previously in publish
  the game silently used the defaults).
- Debug flags `--fire` and `--shotframe N`; debug runs (`--shot`, `--fire`) are no longer written
  to the high score table.
- Removed the unused `AudioManager._frame`: the build is warning-free.
- Documentation (DESIGN/TECHNICAL/README) updated along the way.

**Input**

- `Ui/GameInput.cs`: the keyboard and the first available gamepad are polled once per frame. Left stick/D-pad —
  movement and menu navigation, A — confirm/fire, B — back (in game — pause), View — map.
  The keyboard works in parallel; hot-plugging a pad is supported (scan 0..3 every frame).
- Menu edges are computed from the previous state. The stick in menus triggers on the leading axis with a dead
  zone of 0.6, so that a diagonal does not scroll the list and change a value in the same frame.
- In isometric view only the stick is screen-relative: `CameraRig.ScreenToRoom` converts its vector into room
  axes using the current camera ("right" — a diagonal down the room, "up-right" — straight along the X axis).
  The keyboard always moves along the room axes (as before the gamepad). The basis follows the camera animation,
  so when switching views a held stick smoothly turns the tank.

**Follow-up (17.11.2026)**

- The ambient score was reworked into electronic music: pulsing 16-step bass, detuned saw pad, chord arpeggio, machine-room
  noise and hum, digital blips/chirps/sonar, and a soft kick; tempo and filters react to tension.
- Weapon sounds are layered: tank launch plus burning-fuel hiss, wall impact with debris, robot servo/plasma launch,
  plasma flight whine, and the new `EnemyImpact` event for enemy bolts hitting a wall.
- Settings: new Gameplay option `WALLS DON'T KILL` (on by default; walls still block), the God mode label no longer says
  "(test)", and BACK is separated from the other rows.
- The documentation (README, DESIGN, TECHNICAL, MAZE_FORMAT, STATE) is now in English.

## 7. Known Limitations and Pitfalls

- `assets/config.json` is copied to the output directory and to publish, but the source tree takes priority:
  `GameApp.ConfigPath` searches upward for a directory that has both `assets` and `src`. That is the F5 scenario; if
  the rule is changed to a simple `File.Exists`, the game will start taking the copy from `bin` and edits will stop
  being picked up without a rebuild.
- The Snap Jumper jump is a teleport (it used to be a fast dash over 5 ticks). The cross-fade "fade out here,
  fade in there" requires exactly a teleport. If it turns out to be too harsh in gameplay — the dash can be restored
  in the `SnapJumper` branch in `UpdateEnemies`.
- Firing does not wait for the tank to finish turning: the bullet flies along `FaceX/FaceY`, so at the moment of a turn it goes slightly
  off to the side of the barrel. This is intentional; gate it on `Turning` in `Fire` if needed.
- Energy walls, pod barriers and the room name are not lit (intentionally), they have their own emission/additive,
  so shadows do not fall on them.
- The shader needs OpenGL 3.3. If it failed to build, `LightingShader.IsAvailable == false` and the scene
  is drawn without lighting (the game does not crash). The glow keeps working: `GlowMap` has its own shader,
  and `Composite` checks only its validity.
- The shadow map extent (`ShadowMap.HalfExtent = 27`) is sized for a 40x22 room. Change it if the
  room/camera size changes.
- `artifacts/` is in `.gitignore`: the ATRs, PDFs and room screenshots are absent on a new machine. To compare
  rooms, they need to be brought over separately.
- The local `ShamusRemake/` folder (publish output for running the game manually) is also in `.gitignore`.
- Rendering is not covered by tests: verify via `--shot` (and `--view`, `--room`, `--settings`, `--zoom`/`--look`,
  `--fire`, `--shotframe`).
- Settings and high scores live in `%APPDATA%\Shamus3D\settings.json` and are not transferred with the repository.
- The gamepad is not covered by automated tests: verify manually. B does nothing on the title screen (you cannot
  exit the game with the gamepad); the exit is the Esc key.
- There is no CI (GitHub Actions): build and tests are run locally.
- If you edit assets, remember: files are copied to the output directory on build.

## 8. Where to Look for Typical Tasks

| Task | File / location |
|---|---|
| Rules, timings, balance | `src/Shamus.Core/Simulation/GameWorld.cs`, `assets/config.json` |
| New enemy | `EnemyKind`, branches in `GameWorld.Decide/UpdateEnemies`, drawing in `SceneRenderer` |
| Enemy color schemes | `Rendering/EnemyPalette`, `Enemy.ColorIndex`, `EnemyColorCount` |
| Robo-Droid model | `SceneRenderer.DrawRoboDroid`, `DrawRobotBeacon`; heights — `RoomMetrics.RobotTurretRadius/_Y` |
| Spiral Drone model | `SceneRenderer.DrawSpiralDrone`; slits/fan — `RoomMetrics.DroneSlitY` |
| Snap Jumper model | `SceneRenderer.JumperModel`, `JumperShadow`, `JumperFade` |
| Projectiles | `SceneRenderer.DrawShells/DrawEnemyShells`, `GameConfig.MuzzleLength`, `RoomMetrics.BarrelY` |
| Effects | `Rendering/ParticleFx.cs`, `SceneRenderer.OnEvents/UpdateEffects` |
| Item models | `SceneRenderer.DrawItem` + `KeyModel/KeyholeModel/PlusModel/QuestionModel`; heights in `RoomMetrics` |
| Plateau in isometric view | `RoomFloor.Mask/MergedBoundary`, `RoomView.DrawPlateauFloor/DrawPlateauSides`; `SceneRenderer` passes the player's cell |
| Input (keyboard/gamepad), screen directions | `Ui/GameInput.cs`, `CameraRig.ScreenToRoom`, `GameApp.Update` |
| Wall appearance | `RoomView`: `FieldPink`, `FieldHeight`, `FieldThickness`, `FieldAlpha` |
| Generator posts | `RoomView.DrawPosts`, `PostBody/Top` |
| Heights in the room | `Rendering/RoomMetrics.cs` (single scale; wall = 0.5 cell, lower than the characters) |
| Light and shadows | `LightingShader` (`LightDirection`, `Ambient`, `Diffuse`), `ShadowMap` |
| Glow/bloom | `GlowMap` (`Strength`), `RoomView.DrawEmissive`, `SceneRenderer.DrawEmissive` |
| Tank | model `SceneRenderer.DrawTank`; heading — `GameWorld.Heading` (`MovePlayer`/`UpdateHeading`/`EaseHeading`) |
| Camera/letterbox | `CameraRig`, `GameApp.Run`, `SceneRenderer.VirtualWidth/Height` |
| Menus and settings | `GameApp.BuildMenus/BuildSettings`, `Ui/SettingsMenu.cs`, `Ui/Settings.cs` |
| Sound | `Audio/ProceduralSfx.cs`, `Audio/AmbientSynth.cs`, `Audio/AudioManager.cs` |
| Maze format | `docs/MAZE_FORMAT.md`, `MazeData`, `RoomGrid`, `RoomWalls` |
| Map | `MapLayout`, `Ui/MapView.cs`, `assets/maps/c64.map` |

## 9. Checks Before Committing

```bash
dotnet build Shamus.sln
dotnet test Shamus.sln --no-build
```

Expected: 0 errors, 0 warnings, 88/88 tests. For a visual check:

```bash
dotnet run --project src/Shamus.Game -- --maze c64 --room 3 --view iso --mute --shot shot.png
dotnet run --project src/Shamus.Game -- --settings --mute --shot settings.png
dotnet run --project src/Shamus.Game -- --winsize 1000x700 --room 3 --view iso --mute --shot bars.png
# effects: shot and hit
dotnet run --project src/Shamus.Game -- --maze c64 --room 18 --view iso --mute --fire --zoom 6 --look 15,11 --shotframe 21 --shot boom.png
# enemies in different color schemes
dotnet run --project src/Shamus.Game -- --maze c64 --room 80 --view iso --mute --shotframe 8 --shot room80.png
```

If `Fullscreen` is enabled in `settings.json`, `--shot` captures a fullscreen frame. For windowed
screenshots, turn off fullscreen in SETTINGS/VIDEO or temporarily edit `settings.json`.

## 10. Next Steps (Suggestions)

1. Sound: a separate iteration — new events (`EnemyShot` by type, hits, the jumper's teleport,
   enemy death) and volume balance.
2. A hit-on-enemy effect as a separate event (right now there is already an explosion on `EnemyKilled`, but not on
   every hit if the enemy survived).
3. CI: `dotnet build` + `dotnet test` on GitHub Actions.
4. Softer shadows: 2048 reads sharply on the edges; candidates are a larger PCF radius or a second
   blur pass over the depth map.
5. Items (key, keyhole, potion, "?") are primitives too; they can be "dressed" the same way.
6. Documentation: update DESIGN/TECHNICAL when mechanics change; screenshots go to `artifacts/shots` (locally).

## 11. Git

- Branch `main`, remote `origin` = `https://github.com/blalbabla/shamus-remake.git`.
- Message style: Conventional Commits, short (`feat(shells): ...`, `fix(render): ...`).
- Before moving to another machine: `git status` must be clean, then `git push`.
- `artifacts/`, `ShamusRemake/` and `bin/obj` are ignored.
- There are no branches with unfinished work: everything is in `main`.
