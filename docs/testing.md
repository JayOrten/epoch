# Testing

## Overview

xUnit tests in `epoch.Tests/` covering extracted static/pure functions. The goal is regression-catching heuristics on core logic, not exhaustive coverage.

## Running Tests

```bash
# All tests
dotnet test epoch.sln

# Just the test project
dotnet test epoch.Tests/epoch.Tests.csproj

# Single test class
dotnet test epoch.Tests/epoch.Tests.csproj --filter "FullyQualifiedName~MapRegistryTests"

# Single test method
dotnet test epoch.Tests/epoch.Tests.csproj --filter "FullyQualifiedName~Register_GetEntityAt_Roundtrip"
```

## Test File Structure

```
epoch.Tests/
├── UtilitiesTests.cs              # Placeholder (ContentPaths needs Core — Tier 3)
├── ParsingTests.cs                # ConvertValue, ParseColor, ParseVector, ParseComponentElement
├── ECS/
│   ├── DefinitionTests.cs         # EntityDefinition Clone/Merge
│   ├── ComponentTests.cs          # GraphicalTileList Set/Remove bitmask ops
│   ├── MapRegistryTests.cs        # Spatial hash CRUD, bounds, passability
│   └── Systems/
│       ├── TileAdjacencyTests.cs  # CalculateBorderMasks, CalculateSpaceMask
│       ├── MovementTests.cs       # ResolveMovement, CheckCompositeCollision
│       └── DrawTests.cs           # ComputeTileTransform perspective math
```

## Testability Tiers

### Tier 1: Pure functions (no Arch, no MonoGame)
Zero-dependency, highest value. These test static methods with no side effects.

| Function | Source File |
|----------|------------|
| `Utils.ConvertValue(string, Type)` | `Utilities/Utilities.cs` |
| `Utils.ParseColor/ParseVector2/ParseVector3` | `Utilities/Utilities.cs` |
| `EntityManager.ParseComponentElement(XElement)` | `ECS/Entities.cs` (internal) |
| `EntityDefinition.Clone() / .Merge()` | `ECS/Definitions.cs` |
| `GraphicalTileList.Set/Remove` | `ECS/Components.cs` |
| `TileAdjacencySystem.CalculateBorderMasks(int)` | `ECS/Systems/TileAdjacencySystem.cs` |
| `DrawSystem.ComputeTileTransform(...)` | `ECS/Systems/DrawSystem.cs` |

### Tier 2: Needs Arch ECS World (no MonoGame)
`World.Create()` works standalone. Tests create a World, populate a MapRegistry, and exercise logic.

| Function | Source File |
|----------|------------|
| `MapRegistry` (register, lookup, bounds, passability) | `ECS/MapRegistry.cs` |
| `MovementSystem.ResolveMovement(...)` | `ECS/Systems/MovementSystem.cs` |
| `MovementSystem.CheckCompositeCollision(...)` | `ECS/Systems/MovementSystem.cs` |
| `TileAdjacencySystem.CalculateSpaceMask(...)` | `ECS/Systems/TileAdjacencySystem.cs` |

### Tier 3: Needs Core singleton (MonoGame) — DO NOT EXPAND
`ContentPaths` has a static initializer that dereferences `Core.Content.RootDirectory`, so even touching the type crashes without a full MonoGame runtime. Not worth the ceremony.

## Conventions

- **InternalsVisibleTo**: `epoch.csproj` exposes internals to `epoch.Tests` for testing `internal` methods like `ParseComponentElement`
- **Test naming**: `MethodName_Scenario` (e.g. `ResolveMovement_StepUp`, `ParseColor_RGBA`)
- **Arch World tests**: Create `World.Create()` and `MapRegistry` per test class. Use helper methods like `PlaceSolid(Vector3)` for setup.
- **No mocking framework**: Tests use real Arch World instances — lightweight enough to not need mocks

## Adding Tests for New Code

When extracting a new static function for testability:

1. Determine the tier (does it need Arch? MonoGame?)
2. Place the test file mirroring the source structure under `epoch.Tests/`
3. For Tier 2 tests, create a fresh `World` and `MapRegistry` in the constructor
4. Run `dotnet test epoch.sln` to verify
