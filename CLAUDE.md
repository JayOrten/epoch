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

### Layered Structure

1. **Engine Layer** (`epoch/Engine/`) - Game loop, scene management, graphics, input, audio
2. **ECS Layer** (`epoch/ECS/`) - Components, systems, entity management via Arch
3. **Game Layer** (`epoch/Scenes/`) - Scene implementations (WorldScene)
4. **Utilities** (`epoch/Utilities/`) - Logging, content paths, math helpers

### ECS Pattern (Arch Framework)

**Components** (`ECS/Components.cs`) - All structs with `[Component]` attribute:
- Tag components: `PlayerTag`, `AirTag`, `DirtyTag`
- Data components: `Position`, `GraphicalTileList`, `Movement`, `Direction`
- Camera components: `CameraInput`, `CameraState`, `CameraPreviousState`

**Systems** (`ECS/Systems.cs`) - Inherit from `SystemBase<GameTime>`:
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
- `Engine/Core.cs` - Static singleton for global resources (`Core.Instance`, `Core.GraphicsDevice`)
- `Scenes/WorldScene.cs` - Main scene, orchestrates all systems
- `ECS/Entities.cs` - EntityManager for spawning from XML definitions

## Source Generators

`epoch.Generators/ComponentFactoryGenerator.cs` auto-generates component factory code via Roslyn.

## Dependencies

- MonoGame.Framework.DesktopGL 3.8
- MonoGame.Extended 5.1.1
- Arch ECS (local reference at `../../Arch/src/Arch`)
- Microsoft.Extensions.Logging 9.0.10
