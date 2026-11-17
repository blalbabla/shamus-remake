# Maze format, room geometry, map, and generator

Format source: "New Mazes for Shamus" (slx, 2017), an extended Atari format.

## 1. The `.maze` file: 640 bytes

Five segments of 128 bytes each; the byte index within a segment equals the room number (0..127).

| Segment | Offset | Contents |
|---|---|---|
| 0 | 0 | horizontal walls; bit 7 = corridor, bits 4–6 = corridor type |
| 1 | 128 | vertical walls; for corridors the low 7 bits = number of the room the vertical exit leads to |
| 2 | 256 | object: 0 none, 1 keyhole, 2 key, 3 "?", 4 potion, 6 Shadow |
| 3 | 384 | object color: low nibble = Atari hue, bits 5–7 = brightness, bit 4 = which door is closed (1 = left, 0 = right) |
| 4 | 512 | byte 0 = number of pod rooms, bytes 1.. = their numbers; bytes `0x7D..0x7F` = the last rooms of levels 1–3 |

Key identifier: the color byte without bit 4 (`MazeData.KeyId`); a key and a keyhole of the same color match each other.

### The `#` grid (chamber)

Six horizontal wall pieces (bits 0–2 top line left to right, 3–5 bottom) and six vertical ones (bits 0–2 left line
top to bottom, 3–5 right). A pod room is a chamber without internal walls. An object at entry is placed randomly in one of the 9 grid cells.

### Corridor types (bits 4–6 of segment 0)

| Type | Code | Exits |
|---|---|---|
| 0 | `$80` | vertical down only (dead end) |
| 1 | `$90` | down and right |
| 2 | `$A0` | down and left |
| 3 | `$B0` | T: left, right, down |
| 4 | `$C0` | vertical up only (dead end) |
| 5 | `$D0` | up and right |
| 6 | `$E0` | up and left |
| 7 | `$F0` | T: left, right, up |

Shapes 0 and 4 are understood in the game as "vertical only"; the exact shapes have only partially been verified against screenshots.

## 2. Connectivity rules

- The right exit of room r is open if and only if the left exit of room r+1 is open (except at the maze edges).
- For a corridor, the vertical exit leads to a corridor room of the opposite direction, and the connection is mutual.
- Objects are placed in chambers and pod rooms; every pod room must have a goal; room 127 contains Shadow and is a pod room.
- This is checked by `MazeValidator.Validate`.

## 3. Room geometry (`RoomGrid`)

A 40 x 22 cell grid (one cell is about 8 pixels of the original).

- **Chamber:** four "wallpaper" corner blocks (columns 0–8 and 32–39, rows 0–7 and 14–21), a central column 9–31 with a top and bottom wall,
  and the middle band of rows 8–13 open to the screen edges (doors left and right). Inside is the `#` grid: vertical lines in columns 16 and 23,
  horizontal ones in rows 7 and 14.
- **Corridor:** the whole room is solid and a passage is carved out: the center (columns 17–23, rows 8–13) is always present, plus branches by type.
- The geometry was compared against C64 version screenshots (`tools/Shamus.RoomCompare`): the average difference is about 7%, the rest is accounted for by pod barriers and wall thickness.

### Vector wall geometry

For drawing, the room grid is converted into line segments (`RoomWalls`). Cells are either `Thick` (chamber corner blocks, corridor fill) or `Thin`
(the central column frame, `#` grid pieces, closed doors, sealed edges of rooms 0 and 127). For `Thick` regions an outline is drawn along the border
with empty cells, for runs of `Thin` cells a single center line; all lines run through cell centers, and one generator sits at each segment end. A test checks that every
"solid / empty cell" border has a wall segment next to it (for all mazes and 20 generated ones).

## 4. The `.map` map file

A text layout like `shamus_map_simplified.gif`. Blocks `level N`, with space-separated tokens on each line:

| Token | Meaning |
|---|---|
| `NNN` | room (three digits) |
| `---` | stretched horizontal transition |
| `\|` | vertical transition |
| `..` | empty |
| `toN` | exit to level N |

Lines starting with `#` are comments. `assets/maps/c64.map` was transcribed from the gif; the `HandMadeMapMatchesMazeData` test verifies it against the data
(transitions from r to r+1 lie on the same line, vertical connections in the same column and in the right direction). If the file is absent, `MapLayout.Auto` puts
each chain of connected rooms on its own line. The in-game map shows visited rooms and, dimmed, adjacent unvisited rooms reachable through an open exit.

## 5. The generator (`ProceduralMaze.Generate(seed)`)

The result is reproducible: the same seed yields the same maze.

1. **Chains.** The 128 rooms are split into chains of length 1..10 (the first and last are at least 2 long). The first room of a chain is a corner corridor (except
   room 0), and so is the last; the intermediate ones are chambers, some pod rooms (7 in total), and some T junctions (about 12%). Room 127 is a pod.
2. **Levels.** Boundaries are chosen near 36/65/92 inside the chains (so that the levels are connected).
3. **Verticals.** "Slots" (corners, T junctions, single rooms) are grouped by level and stitched together in pairs; for each pair one side is chosen at random
   to face down; corridor types follow from the roles and directions. An odd number of slots is fixed by adding a T junction.
4. **Walls.** Random `#` grid pieces are thrown into chambers; a variant is accepted if the room remains passable (`MazeValidator.RoomPlayable`).
5. **Locks.** Up to 7 keyholes in random chambers (colors from the palette: blue, orange, red, purple, green, cyan, brown; the door side is random).
6. **Keys.** For each lock, in order of reachability, a key is placed only where the player can get to before opening that lock (`MazeValidator.Reachable`);
   unreachable locks are removed; at least four working locks are required.
7. **Misc.** Potions (7) and "?" (8) in free chambers; pod room goals are randomly a potion or a "?".
8. **Validation.** `MazeValidator.Validate`: exit connectivity, mutual vertical links, pod goals, passability of every room, and maze passability
   by "greedy" key collection. Failed variants are discarded (up to 400 attempts).

Options (`MazeGenOptions`): number of locks, pod rooms, potions, bonuses. In the menu the maze is "PROCEDURAL" plus a SEED line; from the command line, `--maze random --seed N`.
