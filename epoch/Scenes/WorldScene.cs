using System;
using System.Collections.Generic;
using Arch.Core;
using Arch.Core.Extensions;
using epoch.ECS;
using epoch.Graphics.Tiles;
using epoch.Input;
using epoch.Utilities;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MonoGame.Extended;
using MonoGame.Extended.ViewportAdapters;

namespace epoch.Scenes;

/// <summary>
/// Main gameplay scene. Wires up the ECS world, loads the tilemap and entity definitions,
/// spawns the player and camera, configures shaders, and drives the system update order:
/// Input → TileAdjacency → Movement → CameraLogic → CameraApply (update) and Draw (draw).
/// </summary>
public class WorldScene : Scene
{
    private World _world;

    private DrawSystem _drawSystem;

    private MovementSystem _movementSystem;

    private TileAdjacencySystem _tileAdjacencySystem;

    private InputSystem _inputSystem;

    private CameraLogicSystem _cameraLogicSystem;

    private CameraApplySystem _cameraApplySystem;

    private GenerationSystem _generationSystem;

    public override void Initialize()
    {
        base.Initialize();

        var viewportAdapter = new BoxingViewportAdapter(
            Core.Instance.Window,
            Core.GraphicsDevice,
            Core.Graphics.PreferredBackBufferWidth,
            Core.Graphics.PreferredBackBufferHeight
        );

        GlobalContext.Camera = new OrthographicCamera(viewportAdapter);
        GlobalContext.Camera.LookAt(new Vector2(0, 0));
    }

    public override void LoadContent()
    {
        string tileDefinitionsPath = ContentPaths.Config("tile-definitions");
        string tileSetPath = ContentPaths.Config("tileset");
        string entityDefinitionsPath = ContentPaths.Config("entity-definitions");

        // Load tuning config (hot-reloads on save)
        TuningConfig.Load(ContentPaths.Config("tuning"));

        // Load tileset
        Tileset tileset = Tileset.FromFile(Content, tileSetPath);

        // Load tile definitions
        GlobalContext.TileManager = TileManager.FromFile(tileset, tileDefinitionsPath);

        // Create the world
        _world = World.Create();

        // Create terrain generator and chunk registry
        var terrainGenerator = new TerrainGenerator(16);
        GlobalContext.ChunkRegistry = new ChunkRegistry(
            _world,
            16,
            GlobalContext.MaxZ,
            3,
            terrainGenerator
        );

        // Create the entity manager, loading in entity definitions from file
        GlobalContext.EntityManager = new EntityManager(_world, entityDefinitionsPath);

        // Create shaders and draw system
        Effect renderShader = Content.Load<Effect>("RenderShader");
        Effect effectShader = Content.Load<Effect>("EffectShader");

        // Load shader values that don't change
        var textureSizeParam = renderShader.Parameters["TextureSize"];
        var tileSizeParam = renderShader.Parameters["TileSize"];
        var viewportParam = renderShader.Parameters["ViewportSize"];
        var spriteSheetParam = renderShader.Parameters["SpriteTexture"];

        if (textureSizeParam != null)
            textureSizeParam.SetValue(
                new Vector2(
                    GlobalContext.TileManager.Tileset.Columns
                        * GlobalContext.TileManager.Tileset.TileWidth,
                    GlobalContext.TileManager.Tileset.Rows
                        * GlobalContext.TileManager.Tileset.TileHeight
                )
            );
        if (tileSizeParam != null)
            tileSizeParam.SetValue(
                new Vector2(
                    GlobalContext.TileManager.Tileset.TileHeight,
                    GlobalContext.TileManager.Tileset.TileWidth
                )
            ); // Size of ONE
        if (viewportParam != null)
            viewportParam.SetValue(
                new Vector2(Core.GraphicsDevice.Viewport.Width, Core.GraphicsDevice.Viewport.Height)
            );
        if (spriteSheetParam != null) // TODO: pass sprite sheet here
            spriteSheetParam.SetValue(GlobalContext.TileManager.Tileset.GetTile(0).Texture); // TODO: this is super hacky, need to rework these classes.

        _drawSystem = new DrawSystem(_world, renderShader, effectShader);

        // Pre-allocate instancing buffers to avoid runtime doubling resizes
        Core.TileInstancing.SetInternalArraySizes(200_000);
    }

    public override void BeginRun()
    {
        base.BeginRun();

        // Create systems
        _movementSystem = new MovementSystem(_world);

        _tileAdjacencySystem = new TileAdjacencySystem(_world);

        _inputSystem = new InputSystem(_world);

        _cameraLogicSystem = new CameraLogicSystem(_world);

        _cameraApplySystem = new CameraApplySystem(_world);

        _generationSystem = new GenerationSystem(_world);

        // Spawn Player
        // Create entity with desired position
        EntityDefinition spawnPosition = new EntityDefinition(
            new ComponentDefinition(
                "Position",
                new Dictionary<string, string> { { "WorldCoordinate", "0,0,25" }, { "Top", "0.9" } }
            )
        );

        GlobalContext.PlayerEntity = GlobalContext.EntityManager.Spawn("player", spawnPosition);

        var compositeEntities = GlobalContext.PlayerEntity.Get<CompositeControllerComponent>();
        foreach (var value in compositeEntities.Parts.Values)
        {
            Entity child = value;
            if (child.Has<GraphicalTile>())
            {
                ref var graphicalTile = ref child.Get<GraphicalTile>();
                // Change background2color
                graphicalTile.Background2Color = new Color(30, 32, 48, 255);
            }
        }

        // Spawn Camera entity
        GlobalContext.CameraEntity = _world.Create(
            new CameraInput(),
            new CameraState(),
            new CameraPreviousState()
        );

        // Center camera on player
        ref var pos = ref GlobalContext.PlayerEntity.Get<Position>();

        GlobalContext.Camera.LookAt(
            Utils.ConvertGridToWorldCoordinate(
                new Vector2(pos.WorldCoordinate.X, pos.WorldCoordinate.Y),
                GlobalContext.TileManager.Tileset.TileWidth * GlobalContext.GlobalScale,
                GlobalContext.TileManager.Tileset.TileHeight * GlobalContext.GlobalScale
            )
        );

        // string tileMapPath = ContentPaths.Config("tilemap");
        // TileMap.LoadTileMap(tileMapPath, _world);
        // _proceduralGenerationSystem.Update(new GameTime());
    }

    public override void FixedUpdate(GameTime gameTime)
    {
        // Snapshot camera state before systems modify it
        ref var camState = ref GlobalContext.CameraEntity.Get<CameraState>();
        ref var prevState = ref GlobalContext.CameraEntity.Get<CameraPreviousState>();
        prevState.Position = camState.Position;
        prevState.Zoom = GlobalContext.Camera.Zoom;
        prevState.Rotation = camState.Rotation;
        prevState.VpDistance = camState.VpDistance;

        _generationSystem.Update(gameTime);

        _inputSystem.Update(gameTime);

        _tileAdjacencySystem.Update(gameTime);

        _movementSystem.Update(gameTime);

        _cameraLogicSystem.Update(gameTime);

        _cameraApplySystem.Update(gameTime);
    }

    public override void Draw(GameTime gameTime)
    {
        _drawSystem.Update(in gameTime);
    }
}
