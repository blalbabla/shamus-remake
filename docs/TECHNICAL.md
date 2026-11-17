# Shamus 3D: Technical Documentation

## 1. Stack and Build

- .NET 10 (SDK 10.0.x), C#, Raylib-cs 8.1.0 (version pinned in `Shamus.Game.csproj`), xUnit.
- `dotnet build` builds the `Shamus.sln` solution, `dotnet test` runs the core tests (88).
- Assets (`assets/mazes/*.maze`, `assets/maps/*.map`, `assets/fonts/*`, `assets/config.json`) are declared
  `<Content>` with `CopyToOutputDirectory` and `CopyToPublishDirectory`, so they end up in both `bin` and publish.
  In a dev run, `GameApp.ConfigPath` still prefers the source tree (the directory that contains both `assets`
  and `src`) so config edits are picked up on F5 without a rebuild.

## 2. Project Structure

```
Shamus.Core        no dependency on Raylib
  GameConfig            all game constants, JSON loading, difficulty presets
  InputState            input for a single tick (MoveX, MoveY, Fire)
  Mazes/
    MazeData            128 rooms in 640 bytes, field access
    MazeLoader          reads .maze
    RoomGrid            40x22 cell grid for a single room (Thick/Thin)
    RoomWalls           vector walls: wall segments and generator posts
    RoomFloor           accessible area (BFS from the hero + wall ring + generator post cells) and its outline for isometric
    MapLayout           map layout per level (from .map or automatic)
    ProceduralMaze      maze generator
    MazeValidator       structure and passability validation
  Simulation/
    GameWorld           deterministic simulation, events
    Enemy, RoomObject   entities
    WorldEvent          outbound events (sound, HUD)
Shamus.Game        Raylib frontend
  Program, GameApp      entry point, window, states, loop
  Rendering/            SceneRenderer (3D), RoomView (walls, posts, floor, room name), CameraRig, LightingShader, ShadowMap,
                        GlowMap (bloom), RoomMetrics (single height scale), AtariPalette
  Ui/                   Hud, Menu, SettingsMenu, Settings, MapView, GameInput
  Audio/                Dsp, ProceduralSfx, AmbientSynth, AudioManager
tools/
  Shamus.MazeExtract    ATR -> assets/mazes/*.maze
  Shamus.RoomCompare    compares RoomGrid against room*.png screenshots
tests/Shamus.Core.Tests
```

Dependencies are one-way: Game, tools, and tests reference Core. Core knows nothing about Raylib or sound.

## 3. Loop and Timing

- Fixed 60 Hz step (`Step = 1/60`) with an accumulator, at most 0.1 s accumulated per frame.
- On every `GameWorld.Tick(InputState)` the frontend passes `world.Events` to `AudioManager.Handle` and `Hud.OnEvents`.
- Rendering interpolates the player position between `PrevPlayerX/Y` and `PlayerX/Y` by `alpha = acc / Step`.
- World units: room cells (`RoomGrid.W x RoomGrid.H` = 40 x 22), time in ticks.
- **Input:** `Ui/GameInput` is polled once per frame and merges the keyboard with the first available gamepad:
  left stick/D-pad — movement and menu navigation, A — confirm in menus and fire, B — back
  (in game — pause), View — map. Menu edges are computed from the previous state; the menu stick
  triggers on the dominant axis with a 0.6 dead zone. The keyboard works in parallel.
- **Screen directions (stick only):** `CameraRig.ScreenToRoom` converts a screen vector
  (x right, y down) into room axes for the current camera; in isometric, "right" is a down-diagonal across the room,
  "up-right" is straight along the X axis, and up is 1:1. The basis (`ScreenBasis`) comes from the camera matrices, so
  while the view-switch animation runs the direction changes smoothly. The keyboard does not use this conversion:
  it always sets room axes, as before gamepad support (`GameInput.KeyboardX/Y` vs `PadX/Y`).

## 4. Simulation (`GameWorld`)

Tick order (`Tick`):

1. The `GameOver` / `Won` (countdown to a new cycle) / `Dying` (countdown to respawn) states are handled separately.
2. Pod room slit update, hull rotation, player movement (collisions, room transitions, death from walls).
3. Fire, item pickup, player bullets (collisions with enemy bullets, enemies, barriers, the goal, walls).
4. Enemies (movement, shooting, contact), enemy bullets, Shadow.

Key points:

- **Enemy spawn:** `RoomGrid.IsAreaFree(x, y, r)` checks **all** cells under the clearance square, not just the four
  corners: a wall can cross the middle of a square side, and the old corner test missed such a spot.
  `GameWorld.OpenArea` is called through it; the spawn clearance is `max(0.7, EnemyRadius + 0.2)`, so the enemy body
  is entirely on the floor and does not get stuck in an energy wall.
- **Player projectiles:** `Fire` creates the projectile not at the hull center but at the muzzle — `GameConfig.MuzzleLength` (0.74) forward
  along `FaceX/FaceY`, so what is drawn and what is simulated match. If the muzzle ends up in a solid cell (a point-blank
  shot into a wall), the projectile starts from the hull and dies on the first step. The `Shot` event also carries the muzzle point.
  Enemy projectiles (`EnemyBullets`) still spawn at the enemy center — that is their "muzzle".
- **Tank rotation:** the world has a `Heading` (degrees: 90 = +x, 0 = +y) and `PrevHeading` for interpolation. `UpdateHeading`
  each tick turns the heading toward the pressed direction (`FaceX/FaceY`) by a fraction of the remainder, `TurnEasePerTick` (0.45). While
  `Turning` (the remainder is greater than `TurnToleranceDeg` = 9°), `MovePlayer` returns before movement is computed: **the tank first
  turns in place and only then drives**. Releasing the key does not cancel the turn — one press is one turn.
  Firing is not blocked: `Fire` uses `FaceX/FaceY`, so the bullet flies in the pressed direction immediately. Timings
  (the tolerance check runs before the step, so there is one more tick than the "pure" geometry): 45° ≈ 4 ticks (67 ms),
  90° ≈ 5 (83 ms), 135° ≈ 6 (100 ms), 180° ≈ 7 (117 ms). Everything is in ticks, not frame time, so the simulation
  stays deterministic.
- **Determinism:** all randomness goes through a single `Random(config.Seed)` inside the world; identical inputs produce an identical state.
- **Events** (`WorldEventKind`): Shot, BulletHitWall/Barrier/Shadow, BulletsCancelled, EnemyShot/Killed, PlayerDied, GameOver, KeyPicked,
  DoorOpened, PotionPicked, BonusPicked, PodOpened, RoomCleared, RoomEntered, ShadowWarning/Appeared/Killed, CycleStarted.
- **Transitions:** going past `x >= W` enters room r+1 (x=1.5), past `x < 0` enters r-1; corridors: going past the top/bottom leads to `VerticalLink`
  (y = 1.5 or H-1.5). In non-corridors a vertical exit kills.
- **Maze boundary:** `EnterRoom` seals the left door of room 0 and the right door of room 127 (`RoomGrid.SetSolid`).
- **Doors:** in a room with a keyhole the closed door is a block of solid cells (column 0 or W-1, rows 8..13); the side comes from bit 4 of the color byte.
- **Pod rooms:** `InBarrier(x, y)` checks barriers with a slit; the `_podsOpened` state remembers opened rooms.
- **Shadow:** the path is built by BFS from the player cell (`NextWaypoint`), speed `PlayerSpeed * ShadowSpeedFactor * (1 + 0.1 * min(Cycle, 4))`.
- **New cycle:** `NextCycle` resets picked-up items and locks, increments `Cycle`, and returns to room 1.
- **God mode:** `Die()` does nothing; walls block with sliding (`MovePlayer`), and the player cannot leave the vertical bounds.

Enemies (`Enemy`): the shared `Decide` function picks an intent randomly per type (weights in code), `Move` moves with cell checks
(`OpenArea`), with no death from walls. Shooting depends on the type (`CanShoot` for drones). There is `DebugTeleport` for tests.

## 5. Configuration

`assets/config.json` is deserialized into `GameConfig` (`init` properties). The game looks for the file first in the source tree (next to `src`)
so F5 picks up edits without a rebuild. Difficulty, God mode, and the walls-don't-kill option are applied by cloning: `WithDifficulty`, `WithGodMode`, `WithWallsKill`
(serialize to `JsonNode`, edit fields, deserialize). User settings (`Settings`): maze, difficulty, volume,
mute, God mode, seed, high scores: `%APPDATA%\Shamus3D\settings.json`.

## 6. Rendering

**Vector walls.** `RoomGrid` marks each solid cell as `Thick` (filled area) or `Thin` (a wall one cell wide). `RoomWalls.Build`
(cached in `RoomGrid.Walls`, reset on any grid change, for example when a door opens) builds a `WallSet`:
lines through the centers of border `Thick` cells (convex corners close through the corner cell) and through the centers of `Thin` cell runs;
a line that ends at a neighboring `Thick` cell is extended to its center. All endpoints lie on one "cell centers" grid, so each corner has exactly
one post (a test checks that posts are no closer than one cell to each other). Collision logic still uses cells.

**Camera.** `CameraRig`: a single orthographic camera with two modes (`ViewMode.Top` and `ViewMode.Isometric`, view direction (1,-1,-1)); the transition
interpolates the direction, the `Up` vector, and the view height. Debug: `--view top|iso`, `--shot file.png` saves a frame (on frame 8) and exits the game,
which lets you compare the image with sketches without a manual launch.

**Window and scale.** The window can be freely resized (fullscreen mode also uses the display resolution): the frame renders directly
into the window with MSAA at native resolution, but is fitted into a fixed 16:9 with black bars, and the 2D interface is scaled from the
1280x720 space (ortho projection `Rlgl.MultMatrixf`, viewport and scissor in `GameApp.Run`). The 3D projection is set manually in `CameraRig.BeginView`/`EndView`,
because `BeginMode3D` takes the aspect from the window size and would distort the scene in a non-16:9 window. The MSAA engine (4x) is enabled on the window.

**Level.** `RoomView.DrawSolid` draws the floor, dark closed zones, the room name (a texture from text in the `assets/fonts/BlackOpsOne-Regular.ttf` font,
drawn as a flat quad through `Rlgl`; letter fill is disabled, leaving only the outline) and the generator posts; `RoomView.DrawWalls` after the opaque
geometry draws vertical semi-transparent pink walls (one panel per wall segment, bright top edge) without depth writes; before disabling depth writes
the batch is flushed via `Rlgl.DrawRenderBatchActive()`, otherwise the posts are rasterized without depth and the walls are drawn over them.
The color of the generator "caps" depends on the level (`RoomView.Neon`); a closed door is drawn in the key color.

`SceneRenderer` reads the world state and draws: floor, walls (horizontal cell runs merge into a single cube), door, item,
pod room barriers, the player (a tank: hull, tracks, dome turret and gun in `DrawTank`), enemies, Shadow, bullets.

**Plateau in isometric.** `RoomFloor.Mask(grid, seedX, seedY)` does a BFS from the player cell over `Empty` cells, adds one ring of
neighboring `Thin`/`Thick` cells and the cells under all generator platforms so no post hangs over a cliff. `RoomFloor.MergedBoundary(mask)` returns the
axis-aligned boundary edges merged into long segments (`Boundary` is the same edges per cell, for tests).
`RoomView.DrawPlateauFloor` draws the floor from merged runs over the mask, and on top of it dark metal plates (2×2 cells, 1×1 on uneven edges) with a bevel, seams, and a deterministic top tint; `DrawPlateauSides` draws a wall on each edge and
a corner cube at each vertex. The sides are drawn in the **unlit pass** (after `LightingShader.End`) in two flat
tones: south faces (they run bottom-left to top-right on screen) are filled with dark `CliffShade`, west faces with the base
`CliffColor`. Corner cubes and the wide column under each generator (1.02 × depth) use the base color, with no dark overlay: the post stands on a column, and the closed corner reads as an end face at the exit, not as a dark recess. On visible sides `Veins` draws dark vertical veins; positions, width, and length come from an RNG seeded with the room number, so the pattern is stable and does not flicker. Lighting and shadows are not computed for them: otherwise the narrow return face of a convex corner reads as a
vertical recess. In a chamber the result is a thick "+", in a corridor a passage; outside is a cliff. The plateau is enabled
at `Rig.IsometricBlend > 0.5`; in top view the previous floor is drawn.

**Items.** `SceneRenderer.DrawItem` draws the key (`KeyModel`), the keyhole (`KeyholeModel`), the potion-plus (`PlusModel`) and the "?"
(`QuestionModel`). All of them levitate; heights are `RoomMetrics.PropHover` (flat ones), `KeyholeHover`, and `StandingPropHover`.
The key and the keyhole lie in the floor plane; the key slowly rotates about the vertical (`ItemTransformFlat`), the keyhole is static.
The plus and the "?" are built in the local XY plane: in isometric they stand upright and rotate about the vertical axis, in top view they
lie flat (`ItemTransformUpright`, tilt by `Rig.IsometricBlend`) and do not rotate. Tilt and rotation follow the camera
animation. The "?" is additionally fed to the glow pass; a separate 2D label is no longer needed.

Enemies are separate methods: `DrawRoboDroid`, `DrawSpiralDrone`, and the faceted crystal of the jumper. All three are colored from `Rendering/EnemyPalette`: four schemes (magenta, amber, cyan, crimson), each with
three shades — `Body`, `Light`, `Dark`; there is no green in the palette, that is the tank color. The slot is picked by the **simulation** when an
enemy appears (`Enemy.ColorIndex = _rng.Next(Config.EnemyColorCount)`), so the coloring is deterministic and visible in tests, and the renderer
only reads `ColorIndex` (the palette wraps the index if the config has more colors than it does). Color means nothing:
the type is read from the silhouette, as in the original, where robots were colored randomly when a room was generated.

The robot is assembled simply and deliberately: two neutral-gray tracks with a light top plate, the hull between them, and
a **cylindrical drum turret** that overhangs the tracks (`RoomMetrics.RobotTurretRadius` = 0.44 against
the outer track edge of 0.49 — the tracks stay visible on the sides). Around the whole drum diameter runs a **dark groove**
(a cylinder of slightly larger radius), and a **red beacon** runs along it (`DrawRobotBeacon`, angle `time * 2.2 rad`, the same
cube is drawn in the glow pass too, so it glows). The shot comes from the turret: bolts, their trail, and the flash are at the groove height
`RoomMetrics.RobotMuzzleY` = 0.57, and the flash is placed on the drum wall (`MuzzleOffset`: 0.46 for the robot, 0.30 for
the drone). The hull heading lives in the simulation (`Enemy.Heading`/`PrevHeading`) and is turned by the same `EaseHeading` as the
player tank: a fraction of the remainder `TurnEasePerTick` per tick, snap within `TurnToleranceDeg`. One setting controls the smoothness
of both the player and the robots; the renderer only interpolates the angle between ticks (`LerpHeading`), so the tracked vehicle turns
smoothly instead of snapping by 90°. The goal is the movement axis (`DirX/DirY`), so it does not drive sideways; on spawn the robot
immediately faces the player's direction (`AxisToward`) so the room does not start with a visible turn. The turret is round, so it does not
care where to shoot from. The tracks stay neutral gray with any hull coloring.

Snap Jumper is a ghost, so it is **never drawn as solid geometry**. The model (`JumperModel`) is a floating
faceted crystal: a hexagonal bipyramid (two cones facing each other) with radius 0.36 and height ±0.46, with a bright core
(a 0.22 cube) slowly rotating inside, and two shards orbiting around it. The auto-rotation and shards make it feel alive even at rest,
and the faceted shape is not confused with either the robot box or the "saucer" drone. It is drawn **inside the lit pass**
(so the facets get real light), but with depth writes disabled — otherwise the semi-transparent crystal would cut a hole
in the buffer; in the glow pass it glows faintly. Opacity is computed by `JumperFade`: at rest it flickers between
`SnapJumperAlphaMin` (0.35 — based on test results, noticeably more ghostly) and `SnapJumperAlphaMax` (1.0) via a sine of `time` and
`Phase`; during a jump it returns a pair (body, ghost) — the body fades in `0 → 1`, and the ghost at the old spot fades out
`1 → 0` over `SnapBlinkTicks` (12 ticks, 0.2 s). In the shadow pass the jumper remains a caster (`JumperShadow` draws
the crystal silhouette): a semi-transparent enemy with a shadow reads better than one without.

The jump itself is a **teleport, not a dash**: in `GameWorld` the jumper once every `SnapJumpInterval` (60 ticks) picks a direction
(`Decide`, usually toward the player, sometimes sideways), checks the point `X + JumpX * SnapJumpDistance` against `OpenArea(0.45)` and, if
it is free, is moved instantly, remembering `BlinkFromX/Y` and setting `BlinkTicks`. This gives the renderer two
distinct points for the cross-fade — a dash would not provide them. The distance (`SnapJumpDistance` = 2) and the fade length are configured
in `config.json`.

The drone is built from a reference: a low wide "saucer" (cylinder R 0.58, h 0.20) with a light top disk,
six conical studs around the rim, and a tapering turret (a truncated cone, R 0.34 → 0.235, y 0.53..0.86). The drone is **turned
to face the player** (`yaw = atan2(player - enemy)`), and on that face are three vertical slit ports at ±0.35 rad — exactly the angles
of the drone's firing fan, so bolts fly out of the slits, not from the middle of the hull. The `EnemyShot` flash is now
spawned **for each fresh bolt** (`Age == 1`), at its shooter and at its height: for the drone that is three flashes in the slits, for
the robot one in the turret groove. Shot heights are moved into `RoomMetrics.DroneSlitY` (0.69) and `RoomMetrics.RobotMuzzleY` (0.57),
and the projectile, its trail, and the flash take their height from `Bullet.Owner.Kind` (`EnemyShellY`).
The renderer does not invent the tank heading: `DrawTank` receives `LerpHeading(PrevHeading, Heading, alpha)` — the angle from the simulation,
interpolated between ticks the same way as the player position (by the short path through ±180). The turning logic and the
"turn first, then drive" rule live in `GameWorld` (see §4), so visuals and gameplay cannot drift apart.
The camera is orthographic, with two presets (`CameraRig`): top and isometric. The separate 2D "?" label is gone — the question
mark is now part of the 3D model. `AtariPalette` converts (hue 0..15, brightness 0..14) to RGB.

**Projectiles and effects.** `SceneRenderer.DrawShells` draws the player projectile as `DrawCylinderEx`: a cylinder of radius 0.055
(the muzzle radius) plus a cone at the nose, oriented along `DX/DY` and at `RoomMetrics.BarrelY` (barrel height; the same value
in `DrawTank`). The enemy projectile (`DrawEnemyShells`) is a plasma ball of the same size: a 0.085 sphere with a pale 0.045 core
at `RoomMetrics.EnemyBulletY` (0.5 — the robot's middle and the drone's center). Round and orange versus a yellow bullet,
so friend and foe cannot be confused.
Projectiles and effects are not lit and are drawn in the unlit pass after `LightingShader.End()`; they also take part
in the glow pass, projectiles with intensity 0.7 (player) and 0.85 (enemies), otherwise a small bullet would balloon into a halo bigger than the tank.

`ParticleFx` is cosmetics the simulation knows nothing about: a particle list (position, velocity, lifetime, radius, growth, color, type),
`Update(dt)` moves them with damping, `Draw()` draws spheres in two passes (smoke is alpha, sparks/fire/flashes are additive),
`DrawEmissive()` feeds glowing particles to the glow. It spawns from events (`SceneRenderer.OnEvents`: `Shot` — a muzzle flash,
`EnemyShot` — a small orange flash at the shooter, `BulletHitWall`/`BulletHitBarrier`/`BulletsCancelled`/`BulletHitShadow`/
`EnemyKilled`/`PodOpened` — an explosion with sparks and smoke) and from the live projectile list (`UpdateEffects`: the trail at tick
rate, not frame rate, so density does not depend on FPS). The player trail is smoke plus fire, the enemy trail is embers only: a plasma
bolt leaves a short fading trace, not powder smoke, and that is another way to tell the sides apart.
The cap is 700 particles; on overflow the oldest is dropped. When paused and on the map, `UpdateEffects` is called with `dt = 0`,
so effects freeze together with the game.

**Lighting and shadows.** `LightingShader` (Lambert + ambient, 5x5 PCF with radial weights) is applied to opaque
3D geometry; `ShadowMap` renders a 2048 depth map from an ortho camera along the light direction (`LightDirection`, from the top-left of the scene).
The map is rendered at the start of the frame, before the letterbox is set up, because it switches the FBO and resets matrices (`SceneRenderer.RenderShadow`).
Semi-transparent energy walls, pod room barriers, and the room label are drawn after the shader without lighting. If the shader failed to build,
`IsAvailable` = false and the scene stays unlit.

**Bloom.** `GlowMap` is a separate pass before the letterbox: emitters (energy walls and edges, generator "caps",
pod room barriers, bullets, the mystery cube, Shadow's eyes) are drawn additively into an FBO at 1/4 frame size (320x180) with no depth test,
then two separable 9-tap Gaussian blurs (horizontal and vertical, shader `GlowMap.Fragment`), and the result is added
to the frame in 1280x720 coordinates right after `Rig.EndView()`. Density is controlled by `GlowMap.Strength`.
Approximation: the FBO has no geometry, so an emitter glows even behind a wall.

**Heights.** `RoomMetrics` sets a single scale: 1 unit = 1 room cell. The energy wall and the generator post
are 0.5 (half a cell), and that is deliberately **lower** than the characters: at 1.0 the semi-transparent panel in isometric
covered the tank and the enemies. Pod barriers equal the wall, the tank is 0.73 (the hull is lowered by half the track height —
`RoomMetrics.TrackHeight`), enemies up to 0.96, items up to 0.96, player projectiles at barrel height `BarrelY` (0.53),
enemy projectiles at 0.5, Shadow 1.45 (above everything: it must not look like part of the room). New objects must
express height through `RoomMetrics`, not their own numbers, or things will drift apart again.
UI: `Hud` (overlay, toasts), `Menu` (a simple vertical menu), `MapView` (4 level panels from `MapLayout`; the "frontier": rooms reachable from visited ones through open exits and vertical links are drawn in dark gray).

Application states (`GameApp`): Title, Playing, Paused, GameOver, Map.

## 7. Sound

Same approach as in the lode-runner-remake project: no files, everything is synthesized.

- `Dsp`: frequency sweep, filtered noise, bell timbre, normalization, packing into WAV (mono, 16 bit, 44.1 kHz). Pure functions without Raylib.
- `ProceduralSfx.Create(sfx, variant)`: a deterministic generator; the seed depends on the effect and the variant number. 20 effects, 2–5 variants.
- Tank shot: a launch wave (click, low boom, breech clank) and the hiss of burning fuel trailing behind the projectile;
  a wall hit — an explosion with debris (`HitWall`), a robot death — an explosion with electric sparks (`EnemyDie`).
- Robot shot: a servo click, a magnetic discharge, and plasma hiss with vibrato; a wall hit — `EnemyImpact`
  (a short charge and sparks), a hit on the player — `PlayerDie`.
- `AudioManager`: on startup renders all variants via `Parallel.For` (about 75 sounds, roughly 0.2 s), loads them into Raylib
  (`LoadWaveFromMemory`, `LoadSoundFromWave`), and on playback takes a free variant and adds a slight pitch spread.
  Without an audio device the manager is inactive. The event-to-sound mapping lives in `Handle`.
- `AmbientSynth`: a real-time electronic background atmosphere (pure DSP, no Raylib). Layers: a sub drone, a pad of four
  saw/square voices sliding smoothly to the next chord (the chord changes every 18–32 s), a 16-step
  bass sequencer, an arpeggio over the chord tones, machine noise and hum, digital blips (two-tone beeps, chirps, data bursts, sonar),
  and a kick; stereo echo with cross feedback, a final low-pass filter (`Muffle`), and a soft clipper.
  Four slow LFOs (periods 55/73/89/107 s) move layer volume between 20 and 100%.
- `AudioManager.UpdateMusic(tension, muffle)` is called every frame from `GameApp.UpdateAmbience`: when the Raylib stream has freed the buffer
  (`IsAudioStreamProcessed`), a block of 4096 frames is rendered and sent via `UpdateAudioStream`. Tension is computed from the enemy count,
  the Shadow warning and appearance, and a closed pod room; in menus, pause, and the map, muffling is enabled.

## 8. Tools

- `Shamus.MazeExtract <atr> <outDir>`: reads a DOS 2.x image (directory in sectors 361–368, sector chains of 125 data bytes),
  extracts `SHAM_A8.MAP` (5 mazes of 640 bytes each: c64, holmes, cluseau, marlowe, bond), and builds the original Atari maze from the XEX
  (code is relocated down to `$3000`: map addresses `$23F2/$2472/$1D43/$1DC3/$1EE3/$24F8`).
- `Shamus.RoomCompare <maze> <imagesDir> [room]`: builds a wall mask from a screenshot (320x200, 8x8 cell) and compares it with `RoomGrid`;
  with a room number it prints the ASCII view alongside. The screenshots correspond to the C64 version, so compare against `c64.maze`.

## 9. Tests

xUnit, 88 tests: loading all mazes, documentation conformance, the room grid, bullets and walls, pod rooms, determinism,
enemies (spawn, respawn, bullet collision, random coloring, smooth robot turning, jumper teleport),
tank rotation and the "turn first, then move" rule, projectile spawning from the muzzle, items (key and door, potion,
key-to-keyhole matching), Shadow (appearance, new cycle), the maze boundary, God mode, visited rooms,
the map (layout from the gif is verified against the data), the generator (60 seeds: structure and passability, reproducibility),
difficulty, walls without death (`WallsKill=false` blocks but does not kill), vector walls (coverage of all boundaries), sound (finiteness and range of the ambient and effects),
conversion of screen directions to room axes (`CameraRig.ScreenToRoom`, top and iso),
the plateau (`RoomFloor`: plus at a chamber, passage at a corridor, seed fallback, closure and axis-aligned boundary edges,
merging edges into long segments), spawn clearance (`RoomGrid.IsAreaFree`: wall at the middle of a side, enemies not in walls),
the keyhole disappearing after the door opens, finiteness of the wall-opening sound (`Sfx.Keyhole`).

## 10. How to Extend

- **New sound:** add a value to `Sfx`, a branch in `ProceduralSfx.Create`, the variant count in `VariantCount`, and an event in `AudioManager.Handle`.
- **New event:** a value in `WorldEventKind`, `Events.Add` in `GameWorld`, handling in `Hud.OnEvents` and/or `AudioManager.Handle`.
- **New enemy:** a value in `EnemyKind`, branches in `GameWorld.Decide/UpdateEnemies`, rendering in `SceneRenderer.Draw`.
- **New map:** put `NAME.maze` (640 bytes) into `assets/mazes`, add the name to `GameApp.MazeOrder`, and optionally `NAME.map` (format in MAZE_FORMAT.md).
- **Constants:** add a property to `GameConfig` and to `assets/config.json`.
