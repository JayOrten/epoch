using System;
using System.Collections.Generic;
using Arch.Core;
using Arch.Core.Extensions;
using epoch.ECS;
using epoch.Engine;
using epoch.Engine.Graphics.Tiles;
using epoch.Engine.Scenes;
using epoch.Utilities;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MonoGame.Extended;
using MonoGame.Extended.ViewportAdapters;

namespace epoch.Scenes;

public class WorldScene : Scene
{
    private World _world;

    private DrawSystem _drawSystem;

    private MovementSystem _movementSystem;

    private TileAdjacencySystem _tileAdjacencySystem;

    private InputSystem _inputSystem;

    private CameraLogicSystem _cameraLogicSystem;

    private CameraApplySystem _cameraApplySystem;

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
        // Load textures; unnecessary for now.
        // TextureAtlas atlas = TextureAtlas.FromFile(Core.Content, "images/atlas-definition.xml");

        // Create the tilemap from the XML file
        // _tilemap = Tilemap.FromFile(Content, "images/tilemap-definition.xml");
        // _tilemap.Scale = new Vector2(8.0f, 8.0f);

        TileMode mode = TileMode.Ascii;
        string tileDefinitionsPath = ContentPaths.Config("tile-definitions");
        string tileSetPath = ContentPaths.Config("ascii-tileset");
        string entityDefinitionsPath = ContentPaths.Config("entity-definitions");

        // Load tile definitions
        TileDefinitions tileDefinitions = TileDefinitions.FromFile(tileDefinitionsPath);

        // Load tileset
        Tileset tileset = Tileset.FromFile(Content, tileSetPath);

        // Create the tile manager
        GlobalContext.TileManager = new TileManager(tileset, tileDefinitions, mode);

        // Create the world
        _world = World.Create();

        // Create empty map registry
        GlobalContext.MapRegistry = new MapRegistry();

        // Create the entity manager, loading in entity definitions from file
        GlobalContext.EntityManager = new EntityManager(_world, entityDefinitionsPath);

        // Create shaders and draw system
        Effect uberShader = Content.Load<Effect>("UberShader");
        Effect screenEffect = Content.Load<Effect>("ScreenEffect");

        _drawSystem = new DrawSystem(_world, uberShader, screenEffect);
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

        // Spawn entities
        // Load tilemap
        string tileMapPath = ContentPaths.Config("tilemap");
        TileMap.LoadTileMap(tileMapPath, _world);

        // Spawn Player
        // Create entity with desired position
        EntityDefinition spawnPosition = new EntityDefinition(
            new ComponentDefinition(
                "Position",
                new Dictionary<string, string>
                {
                    { "WorldCoordinate", "1,1" },
                    { "zLevel", "0" },
                    { "top", "0.9" },
                }
            )
        );

        GlobalContext.PlayerEntity = GlobalContext.EntityManager.Spawn("player", spawnPosition);

        // Spawn Camera entity
        GlobalContext.CameraEntity = _world.Create(new CameraInput(), new CameraState());

        // Center camera on player
        ref var pos = ref GlobalContext.PlayerEntity.Get<Position>();

        GlobalContext.Camera.LookAt(
            Utils.ConvertGridToWorldCoordinate(
                pos.WorldCoordinate,
                GlobalContext.TileManager.TileWidth * GlobalContext.GlobalScale,
                GlobalContext.TileManager.TileHeight * GlobalContext.GlobalScale
            )
        );

        // TODO: extract to example doc
        // random map generation:
        // EntityDefinition entityDefinition = new EntityDefinition(
        //     new ComponentDefinition("Position")
        // );
        // List<string> comps = ["empty", "grass", "tree", "dirt", "water"];
        // Random RandomUtil = new Random();

        // var sw = Stopwatch.StartNew();
        // for (int i = 0; i < 9; i++)
        // {
        //     for (int j = 0; j < 9; j++)
        //     {
        //         for (int k = 0; k < 1; k++)
        //         {
        //             // 3D coordinate string
        //             string coord3D = string.Concat(i, ",", j, ",", k);

        //             // pick a random component to add
        //             string compToAdd = comps[RandomUtil.Next(0, comps.Count)];

        //             _entityManager.Spawn(
        //                 compToAdd,
        //                 entityDefinition.Add("Position", "WorldCoordinate", coord3D)
        //             );
        //         }
        //     }
        // }
        // sw.Stop();
        // Log.Info($"Spawned grass in {sw.ElapsedMilliseconds} ms");
    }

    public override void Update(GameTime gameTime)
    {
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
