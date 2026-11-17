# Shamus 3D: Design Document

## 1. Goals

- **Stage 1 (MVP, done):** reproduce the core Shamus gameplay as closely as possible to the original, but in 3D with a top-down camera.
  Objects are made of 3D primitives, sounds are generated procedurally.
- **Next:** procedural maze generation (done in basic form), new gameplay features, better graphics.

Principles: mechanics fidelity first, extensions later; logic is separated from rendering; everything
is configured with numbers from the config.

## 2. Game Loop

1. Title screen: maze selection, seed (for procedural), difficulty, volume, God mode.
2. The player spawns in room 0 (or a given room) and goes through rooms in numeric order and via vertical transitions.
3. Death: lose a life, respawn at the room entrance, enemies in the room are generated anew. At zero lives, Game Over.
4. Winning a round: Shadow in room 127 is killed, a new round starts from room 1 (harder).

## 3. World

- **128 rooms**, 4 levels (black, blue, green, red); level boundaries are set by the map (37, 66, 92 in the original).
- Left/right: the room with number ±1. Up/down: only from corridors, to the linked room.
- Rooms: **chamber** (wide, with a grid of internal walls `#`), **corridor** (narrow passage, 8 shapes), **pod room** (barriers).
- You cannot leave the maze: there is a deadly wall to the left of room 0 and to the right of room 127.
- Electrified walls: by default, touching them only blocks (WALLS DON'T KILL is on); if the option is turned off, it kills. In God mode it also only blocks.
- The map (M) shows the rooms the player has already visited, and also, in a muted dark gray, unvisited rooms reachable through an open exit of a visited room (unexplored exits).
- In isometric view, a room is a slab plateau: its top follows the accessible area (chamber — a thick "+", corridor — the shape of the passage), dark vertical walls run down along the entire closed contour, and outside is a drop-off. The top view does not change.

## 4. Player

- Movement in 8 directions. The gamepad stick sets the screen direction, and `CameraRig.ScreenToRoom`
  converts it into room axes based on the current camera (in isometric view, "right" is a diagonal down the room,
  "up-right" is straight along the X axis; in top view, the usual sides). The keyboard moves along room axes,
  as before gamepad support. The tank does not strafe sideways: when a new direction is pressed, it **first
  turns in place** and only then drives. Turning: 45° ≈ 0.07 s, 90° ≈ 0.08 s, 180° ≈ 0.12 s. If the key is
  released immediately, the tank still completes the turn — one press is a turn; if held, it drives right after the turn completes.
- Shooting does not wait for the turn: the bullet leaves in the pressed direction immediately.
- Up to 3 bullets at a time, a pause of 8 ticks between shots (60 Hz).
- Speed: 0.133 cells per tick (about 8 cells per second); after the room is cleared it is multiplied by 1.25.
- Starting lives 5 (adjusted by difficulty).

## 5. Enemies

The count at entry is random within a level range: 1–5, 2–7, 3–9, 4–11 (`EnemyCountMin/Max` in `config.json`). Enemies spawn no closer than 14 cells to the player (if the room is too cramped, the distance smoothly decreases, but not below 6) and no closer than 2 cells to each other; the position is checked over the whole body area, so an enemy cannot spawn in an energy wall. All three types appear from the first level; jumpers are rarer on the first level (about 2 out of 11). The color of each enemy is chosen randomly from four colors (no green) and affects nothing — see §10. Enemies are killed by the player's bullet and
do not die from walls (they slide along them). Contact with an enemy or its bullet kills the player. Enemy bullets also kill other enemies if they get caught in the shot (no points are awarded to the player); the shooter is not hit by its own bullet. The player's bullet destroys an enemy bullet.

| Enemy | Points | Behavior | Shooting |
|---|---|---|---|
| Robo-Droid | 50 | slow and methodical: 75% of decisions toward the player along the main axis, 15% along the other, 10% random | in a straight line when it is on the same line as the player |
| Spiral Drone | 100 | chaotic trajectories: straight course, circling the player, retreat, drift, plus wobble | half of the drones do not shoot; those that shoot fire a fan of three bullets |
| Snap Jumper (rarer on the first level) | 150 | the most aggressive: a teleport jump of about 2 cells once per second straight at the player, sometimes sideways | does not shoot |

Enemy speed grows by 25% per level and by 20% per completed round.

## 6. Objects

| Object | Effect |
|---|---|
| Key | goes into the inventory (colored icon in the HUD); a collected key no longer appears |
| Keyhole | one door of the room is closed by a colored block; touching the keyhole with a key of the same color opens the door permanently, and the keyhole disappears after that |
| Potion | +1 life, +100 points |
| "?" (mystery) | random bonus from 250 to 1000 points, step 50 |
| Shadow (room 127) | the target of the pod room, killing it starts a new round, +5000 points |

Objects appear when entering a room in one of the 9 cells of the `#` grid (randomly, on a free spot, no closer than 4 cells from the entrance).

All items levitate above the floor and slowly rotate (except the keyhole). The key and the keyhole lie flat in both views:
the key shows a ring head, a shaft and a bit; the keyhole shows a disc with a black keyhole. The potion is drawn as a large plus,
"?" as a question mark: in isometric view they stand upright and rotate around the vertical axis, in top view they lie flat and do not rotate.
Orientation follows the camera animation, so when switching views the items smoothly lie down and stand up.

### Pod Rooms

Two vertical barriers, each with a moving gap; in the center is the target (key, potion, "?" or Shadow). The barriers do not let the player through, and bullets
pass only through the gaps. Hitting the target removes the barriers and spawns enemies in the room; the object itself remains and can be picked up.
Opened pod rooms stay open on re-entry.

## 7. Shadow

If you linger in a room, a warning blinks after 10 seconds, and after 15 seconds Shadow appears: an invulnerable pursuer
walking to the player by the shortest path (BFS over cells), 15% slower than the player. Contact kills; leaving the room or dying resets the timer.
Shadow does not appear in pod rooms. In room 127 Shadow stands as the target: killing it completes the round.

## 8. Difficulty

| | Novice | Advanced | Expert |
|---|---|---|---|
| Enemy speed | x0.8 | x1.0 | x1.25 |
| Enemy fire interval | x1.3 | x1.0 | x0.75 |
| Lives | +1 | 5 | -1 (minimum 3) |

**God mode** (test option): walls and enemies do not kill, high scores are not saved.

## 9. Interface

- HUD: score, high score, lives, keys; at the bottom the room, level, maze, round, number of enemies; Shadow warnings; pop-up messages.
- Pause (Resume / Restart / Main menu), Game Over screen with the high score, map (4 level panels, as in the original diagram).
- Main menu: Start game, Settings, Quit. The Settings screen is an opaque rounded card with sections: Gameplay (maze, seed for procedural, difficulty,
  WALLS DON'T KILL, God mode), Video (window / fullscreen) and Audio (effects, music). The title screen draws only the menu, without the room.
- The window can be resized, the frame keeps a 16:9 aspect ratio (black bars), fullscreen runs at the display resolution.
- High scores are stored separately for each maze in `%APPDATA%\Shamus3D\settings.json`.

## 10. Graphics and Sound

- **Two views** of the same scene, key V (smooth transition 0.35 s, the view is saved in the settings): top and true isometric with parallel
  projection (edges at 30°). The models are the same, only the camera changes. Controls do not depend on the view: keys follow room axes, so in isometric view
  "right" is up-right on the screen.
- **Walls are energy force fields** between generators: solid blue posts with a bright "cap" (one per corner) at the vertices and at the ends of walls,
  between them a single vertical translucent pink wall (force field) with a bright top edge. The color of the generator "caps" depends on the level: red, blue, green, red.
  A closed door is a wall of the key's color.
  Large closed areas (room corners, corridor fills) are outlined by a wall along the contour and are slightly darker than the floor; thin walls (the `#` grid, the frame of the central
  column) are drawn as a single wall along the axis.
- **Room name** (`ROOM n`) on the floor in the center, outline letters only (no fill), stencil font Black Ops One (SIL OFL, `assets/fonts`); the size is chosen so that the text fits into the central square of the `#` grid and does not go beyond the walls.
- **The player's projectile** is a small yellow bullet: a cylinder with the diameter of the cannon muzzle and a bright cone at the nose; it flies at the height of
  the barrel and starts exactly from the muzzle. When firing there is a flash at the muzzle, a smoky trail with a light at the tail follows the projectile
  ("the engine is running"), and on impact there is an explosion: a flash, sparks flying sideways and a puff of smoke. Sound: a short launch with a low
  boom, a breech clank and a hiss of burning fuel that is audible while the projectile flies; an impact is an explosion with shards,
  a robot's death is an explosion with electric sparks. No animated textures are used: sparks, smoke and flashes are the same
  primitives (spheres) stacked additively and with normal alpha blending.
- **The enemy's projectile** is the same size, but a round orange "plasma" ball with a pale core and a short trail of
  embers (no smoke), and when an enemy fires, a small orange flash blinks on it. The shot sound is a servo click, a discharge and a plasma hiss that is audible in flight; an impact is a short electric zap. Own and enemy projectiles cannot be
  confused by shape, color or trail.
- **Spiral Drone** looks like a low wide "saucer" with six spikes around the rim and a tapering tower on top. In the tower
  there are three vertical slits — these are gun ports: the drone turns to face the player, and the fan of three shots flies out
  precisely from the slits, with a flash in each.
- **Robo-Droid** is simple: two tracks with a light top slab, a hull and a round drum turret overhanging
  the tracks. A dark horizontal groove runs around the turret, and a red light runs around it in a circle (like a beacon, and it
  glows). The shot comes from the turret — from the height of this groove. The tracks are always neutral gray, the hull color is random.
- **Snap Jumper** is a floating faceted crystal: a hexagonal "double pyramid", with a bright core slowly rotating inside,
  and two shards flying in orbit around it. It is translucent: it shimmers while waiting, transparency fluctuates roughly from 0% to 65%
  (configurable via `SnapJumperAlphaMin`/`Max`). A jump is a teleport: at the old place the crystal "fades" to complete
  disappearance, and at the new place it simultaneously fades in from transparency to full. It jumps about 2 cells once per second,
  most often toward the player, sometimes sideways.
- **Enemy coloring is random.** As in the original, where robots were painted during room generation for variety: we have
  four base colors (magenta, amber, cyan, crimson), the color is chosen for each enemy on spawn and means nothing
  — the type is read from the silhouette. There is no green in the palette: green is the player's tank.
- **Lighting**: one directional light from the upper left of the scene and soft shadows (shadow map 2048, PCF 5x5):
  the floor, posts, player, enemies and items are lit and cast shadows. Force fields, pod room barriers and the interface
  are not lit — they have their own emission.
- **Glow**: force fields and their bright edge, generator "caps", pod room barriers, bullets, the "?" cube and Shadow's eyes
  give a soft halo (bloom). It is drawn in a separate pass at 1/4 resolution and combined with the scene; the interface
  does not glow.
- **Unified scale**: everything in the room follows one scale — one room cell. Energy walls
  and generator posts are deliberately low (half a cell): they are lower than the tank and enemies so that in isometric view they do not obscure them
  with a translucent panel. Pod room barriers are the same height, bullets fly in the band of the walls, and Shadow is taller than everything — it must not
  look like part of the room.
- Pod room barriers: translucent amber energy slabs. Object colors follow the Atari palette (hue and brightness from the map).
- **The player** is a small square tank: a box hull, two tracks on the sides, and a hemisphere turret with a cannon on top in the center.
  The hull sits between the tracks, not on top of them. Hull rotation is part of the simulation: the tank turns in place before
  moving (see §4), and the renderer draws exactly this angle, so the view and the rules do not diverge.
  Enemies, Shadow, bullets and items are also assembled from primitives (cube, sphere, cylinder).
- Sound is fully synthesized: 20 effects, 2–5 variants of each.
- The background atmosphere (electronic ambient) is generated in real time and never repeats: a pulsating bass sequencer,
  a sawtooth pad, arpeggios over the chord tones, machine noise and electric hum, digital blips/chirps/sonar, a soft kick and echo.
  It reacts to the game: the more enemies and the more dangerous Shadow, the tenser it gets (filters open up, tempo rises, bass and kick more often); in the menu,
  pause and on the map it is muted. Music volume is separate (the MUSIC item).

## 11. Differences from the Original

- 3D camera and 3D primitives instead of pixel graphics.
- Enemy counts, speeds, Shadow timings, points and pod barrier parameters are estimated by eye from descriptions (the original values are unknown).
- Enemy behavior is described from general characteristics, not from a disassembler.
- Automatic maps and procedural mazes are absent in the original.
- Rooms 0 and 127 are sealed at the edges of the maze.

## 12. Known Limitations and Plans

- No footsteps and no sounds of electrified walls.
- For mazes other than the original C64 one, there is no schematic map in the gif style: an automatic layout of chains is used.
- In the Cluseau maze, room 117 contains a lock without a key (a quirk of the source data).
- The Atari and C64 "Original" data differ in 29 rooms out of 128; the accuracy of extracting the Atari map addresses from the XEX is not fully confirmed.
- Plans: player and enemy graphics (after the level view), new enemies and weapons, better graphics (lighting, particles), a map editor, tournament mode, balance tuning.
