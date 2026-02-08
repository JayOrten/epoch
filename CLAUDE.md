# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Epoch is a tile-based 2D game engine written in C# using MonoGame and the Arch ECS framework.

Epoch draws 2d tiles, stacked on top of each other, to convey a pseudo-3d top-down view.

Epoch batches tiles together and performs the majority of drawing logic shader side.

**Key Technologies**: C# 14.0, .NET 10.0, MonoGame 3.8+, Arch ECS, Roslyn source generators

## Build & Run Commands

```bash
# Build
dotnet build epoch.sln
dotnet build -c Release epoch.sln

# Run
dotnet run --project epoch/epoch.csproj

# Test
dotnet test epoch.sln
dotnet test epoch.Tests/epoch.Tests.csproj
```

## Architecture

### Directory Structure

1. **Core** (`epoch/Core.cs`) - Game loop, singleton for global resources
2. **Input** (`epoch/Input/`) - Input devices, GameController action mapping
3. **Audio** (`epoch/Audio/`) - Audio playback management
4. **Graphics** (`epoch/Graphics/`) - Tile rendering, texture regions, GPU instancing
5. **Scenes** (`epoch/Scenes/`) - Scene base class and implementations (WorldScene)
6. **ECS** (`epoch/ECS/`) - Components, entity management, definitions via Arch
7. **ECS/Systems** (`epoch/ECS/Systems/`) - Individual system files (Input, Movement, Draw, Camera, TileAdjacency)
8. **Utilities** (`epoch/Utilities/`) - Logging, content paths, math helpers

### ECS Pattern (Arch Framework)

**Components** (`ECS/Components.cs`) - All structs with `[Component]` attribute:
- Tag components: `PlayerTag`, `AirTag`, `DirtyTag`
- Data components: `Position`, `GraphicalTileList`, `Movement`, `Direction`
- Camera components: `CameraInput`, `CameraState`, `CameraPreviousState`

**Systems** (`ECS/Systems/`) - Each in its own file, inherit from `SystemBase<GameTime>`:
- `InputSystem` → `MovementSystem` → `TileAdjacencySystem` → `DrawSystem`
- `CameraLogicSystem` → `CameraApplySystem`

**Entity spawning**: XML-defined templates in `Content/config/entity-definitions.xml`

### Key Patterns

```csharp
// Component access (Arch pattern)
ref var pos = ref entity.Get<Position>();
if (entity.Has<PlayerTag>()) { ... }

// World queries
world.Query(in query, (Entity entity, ref Position pos, ref Movement mov) => { ... });
```

### Coordinate Systems
- **Grid**: Integer tile positions (X, Y, Z)
- **World**: Pixel coordinates for rendering
- Convert via `Utils.ConvertGridToWorldCoordinate()`

### MapRegistry
3D spatial hash (80×80×9) for entity lookups, collision, adjacency queries.

## Content Configuration

| File | Purpose |
|------|---------|
| `Content/config/entity-definitions.xml` | Entity templates with components |
| `Content/config/tile-definitions.json` | Tile colors and visuals |
| `Content/config/tilemap.txt` | Level layout (digits = entity types) |
| `Content/config/tileset.json` | Tile texture atlas info |

## Key Entry Points

- `Game1.cs` - Game initialization
- `Core.cs` - Static singleton for global resources (`Core.Instance`, `Core.GraphicsDevice`)
- `Scenes/WorldScene.cs` - Main scene, orchestrates all systems
- `ECS/Entities.cs` - EntityManager for spawning from XML definitions

## Source Generators

`epoch.Generators/ComponentFactoryGenerator.cs` auto-generates component factory code via Roslyn.

## Testing

xUnit test suite in `epoch.Tests/`. See `docs/testing.md` for full details.

```bash
dotnet test epoch.sln                                              # all tests
dotnet test epoch.Tests/epoch.Tests.csproj --filter "FullyQualifiedName~MapRegistryTests"  # one class
```

**Testability tiers:**
- **Tier 1** (pure functions): No dependencies — parsing, bitmask ops, perspective math
- **Tier 2** (Arch World): Needs `World.Create()` — MapRegistry, movement collision, space masks
- **Tier 3** (MonoGame): Needs `Core` singleton — ContentPaths, rendering, scenes. **Skip these.**

`epoch.csproj` has `InternalsVisibleTo("epoch.Tests")` for access to `internal` methods.

**When modifying code, update corresponding tests.** When adding new static/pure functions extracted for testability, add tests.

## Dependencies

- MonoGame.Framework.DesktopGL 3.8
- MonoGame.Extended 5.1.1
- Arch ECS (local reference at `../../Arch/src/Arch`)
- Microsoft.Extensions.Logging 9.0.10
