using System;
using System.Collections.Generic;
using System.Diagnostics;
using Arch;
using Arch.Core;
using Arch.Core.Extensions;
using Arch.Core.Extensions.Dangerous;
using Arch.Core.Utils;
using epoch.ECS;
using epoch.Engine;
using epoch.Engine.Graphics;
using epoch.Engine.Input;
using epoch.Engine.Scenes;
using epoch.Utilities;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MonoGame.Extended;
using MonoGame.Extended.ViewportAdapters;
using Schedulers;

namespace epoch.Scenes;

public class WorldScene : Scene
{
    private OrthographicCamera _camera;

    private World _world;

    private DrawSystem _drawSystem;

    private PlayerMovementSystem _playerMovementSystem;

    private TileManager _tileManager;

    private EntityManager _entityManager;

    private GlobalSettings _globalSettings;

    private int _currentZLevel = 0;

    private Entity _playerEntity;

    public override void Initialize()
    {
        base.Initialize();

        var viewportAdapter = new BoxingViewportAdapter(
            Core.Instance.Window,
            Core.GraphicsDevice,
            Core.Graphics.PreferredBackBufferWidth,
            Core.Graphics.PreferredBackBufferHeight
        );
        _camera = new OrthographicCamera(viewportAdapter);

        _globalSettings = new GlobalSettings();

        _currentZLevel = 0;
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
        _tileManager = new TileManager(tileset, tileDefinitions, mode);

        // Create the world
        _world = World.Create();

        // Create the entity manager, loading in entity definitions from file
        _entityManager = new EntityManager(_world, entityDefinitionsPath);
    }

    public override void BeginRun()
    {
        base.BeginRun();

        // Spawn entities

        // Load tilemap
        string tileMapPath = ContentPaths.Config("tilemap");
        TileMap.LoadTileMap(tileMapPath, _world);

        // Spawn Player

        // Create entity with desired positio
        EntityDefinition spawnPosition = new EntityDefinition(
            new ComponentDefinition(
                "Position",
                new Dictionary<string, string> { { "WorldCoordinate", "1,1,0" } }
            )
        );

        _playerEntity = _entityManager.Spawn("player", spawnPosition);

        // Center camera on player
        ref var pos = ref _playerEntity.Get<Position>();

        _camera.LookAt(
            Utils.ConvertGridToWorldCoordinate(
                new Vector2(pos.WorldCoordinate.X, pos.WorldCoordinate.Y),
                _tileManager.TileWidth * _globalSettings.GlobalScale,
                _tileManager.TileHeight * _globalSettings.GlobalScale
            )
        );

        // Create systems
        _drawSystem = new DrawSystem(_world, _tileManager);

        _playerMovementSystem = new PlayerMovementSystem(_world, _camera, _playerEntity);

        // Center the camera on the player

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

    private void AdjustZoom()
    {
        var state = Keyboard.GetState();
        float zoomPerTick = 0.01f;
        if (state.IsKeyDown(Keys.Z))
        {
            _camera.ZoomIn(zoomPerTick);
        }
        if (state.IsKeyDown(Keys.X))
        {
            _camera.ZoomOut(zoomPerTick);
        }
    }

    private void AdjustZLevel()
    {
        if (GameController.FDown())
        {
            // Move down a level
            _currentZLevel--;
        }
        if (GameController.RDown())
        {
            // Move up a level
            _currentZLevel++;
        }
    }

    public override void Update(GameTime gameTime)
    {
        PlayerMovementContext playerMovementContext = new PlayerMovementContext(
            gameTime,
            _globalSettings.GlobalScale * _tileManager.TileHeight // assumption here that height equals width.
        );

        _playerMovementSystem.Update(playerMovementContext);

        AdjustZoom();

        AdjustZLevel();
    }

    public override void Draw(GameTime gameTime)
    {
        Core.GraphicsDevice.Clear(Color.Black);

        var transformMatrix = _camera.GetViewMatrix();

        Core.SpriteBatch.Begin(
            samplerState: SamplerState.PointClamp,
            transformMatrix: transformMatrix
        );

        // Eliminate potential shimmering
        transformMatrix.Translation = new Vector3(
            (int)Math.Round(transformMatrix.Translation.X),
            (int)Math.Round(transformMatrix.Translation.Y),
            0
        );

        DrawContext drawContext = new DrawContext(
            gameTime,
            _currentZLevel,
            _globalSettings.GlobalScale,
            _camera.Center
        );

        _drawSystem.Update(in drawContext);

        Core.SpriteBatch.End();
    }
}
