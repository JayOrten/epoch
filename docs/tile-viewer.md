# Tile Viewer

A browser-based tool for previewing, composing, and saving entity tile stacks with live color masking and pseudo-3D perspective.

## Quick Start

From the repo root:

```bash
./tools/tile-viewer/run.sh
```

This starts the tile viewer server and opens the viewer in your browser. Press `Ctrl+C` to stop.

## Manual Start

```bash
cd /path/to/epoch
python3 tools/tile-viewer/server.py 8080
```

Then open http://localhost:8080/tools/tile-viewer/

## Layout

| Panel | Purpose |
|-------|---------|
| **Menu bar** | File, Edit, View menus (save, undo/redo, display settings) |
| **Toolbar** | Entity selector and entity name |
| **Tile Stack** (left) | Draggable list of tiles in the current entity. Click to select, x to remove, + Add to append |
| **Canvas** (center) | Live preview with color masking and perspective stacking |
| **Entity Tile** (right) | Tile def selector, offset, border toggle for the selected tile |
| **Tile Definition** (right) | Name, tileset index, and 5 color channels (bg1, bg2, base, accent, border + alpha) |
| **Tileset Grid** (right, top) | Visual picker -- click a tile to change the selected tile's index |
| **XML Sidebar** (far right) | Live XML preview with save buttons. Toggle via View > XML Panel |
| **Bottom bar** | Vanishing point joystick, depth strength slider |

## Menus

| Menu | Items |
|------|-------|
| **File** | New Entity, Save Entity (`Ctrl+S`), Save Tile Def, Save as New Tile Def |
| **Edit** | Undo (`Ctrl+Z`), Redo (`Ctrl+Y` / `Ctrl+Shift+Z`) |
| **View** | XML Panel toggle, CRT Effect toggle, BG color, render scale |

## Usage

### Viewing an entity

Select an entity from the dropdown. Its `GraphicalTileList` tiles populate the stack panel and render on the canvas. Composite entities (like `player`) resolve their children automatically.

### Editing tiles

1. Click a tile in the stack to select it
2. Change the **Tile Def** dropdown to switch which tile definition is used
3. Use the color pickers to change bg1/bg2/base/accent/border -- changes appear instantly
4. A pink pip appears next to any color that differs from the base tile definition; click the reset button to revert it
5. Adjust the alpha slider next to each color for transparency
6. Change the tileset index by typing a number or clicking a tile in the grid
7. Adjust the offset slider or number input to control the tile's depth in the stack

### Reordering tiles

Drag and drop tiles in the stack to reorder them. Offsets are automatically redistributed after a reorder.

### Creating new entities

1. File > **New Entity** (or use the menu bar)
2. Edit the entity name in the toolbar text input
3. Add tiles to the stack, configure colors and offsets
4. File > **Save Entity** (`Ctrl+S`) to write to `entity-definitions.xml`

### Creating new tile definitions

1. Select a tile in the stack and edit its properties
2. Open the XML sidebar and switch to the **Tile Def XML** tab
3. File > **Save as New Tile Def** to save as a new tile definition (auto-assigns next ID)
4. Or File > **Save Tile Def** to update the existing definition

### XML sidebar

Toggle via View > **XML Panel**. Two tabs:

- **Entity XML** -- shows the generated `<entity>` block for the current design. Updates live as you edit. What you see is what gets saved.
- **Tile Def XML** -- shows the `<tile>` definition for the currently selected tile.

Save buttons are also available at the bottom of the XML sidebar.

### Undo/Redo

All edits (color changes, tile adds/removes, reorders, entity switches) are undoable. Continuous controls like sliders and color pickers are grouped into single undo steps.

- `Ctrl+Z` to undo, `Ctrl+Y` or `Ctrl+Shift+Z` to redo
- Also available via Edit menu
- 50-step undo history

### Saving

- **Save Entity** writes the current entity to `entity-definitions.xml`. Preserves non-graphical components (Position, PlayerTag, etc.) on existing entities. New entities get `<GraphicalTileList>` + `<Position/>`.
- **Save Tile Def** writes the current tile definition to `tile-definitions.xml`.
- **Save as New Tile Def** saves as a new definition with an auto-assigned ID.

### Perspective controls

The vanishing point controls where tiles converge, mimicking the in-game camera look direction.

**Keyboard (same as in-game):**

| Key | Action |
|-----|--------|
| `U` | Look up |
| `J` | Look down |
| `H` | Look left |
| `K` | Look right |
| `Esc` | Reset VP to center |

Hold keys to smoothly move the vanishing point, with circular clamping at radius 1500.

**Joystick pad:** Click and drag the 2D pad in the bottom bar. Double-click to reset.

- **Depth Strength** -- how aggressively tiles scale with depth (game default: 0.030)
- **Scale** -- pixel zoom for the preview (default 8x, since tiles are 24x24). Adjust via View menu.

### Adding/removing tiles

- **+ Add** appends a new tile at the next offset increment
- **x** on a stack item removes it
- Drag to reorder

## How it works

The viewer ports the game's rendering pipeline to JS canvas:

1. Loads `tilesheet_24x24.png` and reads pixel data
2. Classifies each pixel using the same `step(0.5)` thresholds as `RenderShader.fx`:
   - Magenta (R>=128, G<128, B>=128) -> bg1
   - Cyan (R<128, G>=128, B>=128) -> bg2
   - White (R>=128, G>=128, B>=128) -> base
   - Yellow (R>=128, G>=128, B<128) -> accent
3. Replaces mask colors with the tile's defined colors (with per-tile overrides from entity)
4. Stacks tiles using the same perspective formula as `DrawSystem.ComputeTileTransform`

The tileset grid preview uses the first tile definition's colors for readability.

## Server

The tile viewer uses a custom Python server (`tools/tile-viewer/server.py`) that provides:

- Static file serving from the repo root
- `POST /api/save-tile` -- save/update tile definitions
- `POST /api/save-entity` -- save/update entity definitions (preserves non-graphical components)

## Data sources

- `epoch/Content/config/tile-definitions.xml` -- tile color definitions
- `epoch/Content/config/entity-definitions.xml` -- entity tile stacks
- `epoch/Content/images/tilesheet_24x24.png` -- tile sprite sheet
