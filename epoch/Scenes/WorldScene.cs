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
    private OrthographicCamera _camera;

    private World _world;

    private MapRegistry _mapRegistry;

    private DrawSystem _drawSystem;

    private PlayerMovementSystem _playerMovementSystem;

    private TileAdjacencySystem _tileAdjacencySystem;

    private TileManager _tileManager;

    private EntityManager _entityManager;

    private GlobalSettings _globalSettings;

    private int _currentZLevel = 0;

    private Entity _playerEntity;

    private RenderTarget2D _renderTarget2D;

    private Effect _screenEffect;

    private Effect _uberShader;

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
        _screenEffect = Content.Load<Effect>("ScreenEffect");
        _uberShader = Content.Load<Effect>("UberShader");

        _renderTarget2D = new RenderTarget2D(
            Core.GraphicsDevice,
            Core.Graphics.PreferredBackBufferWidth,
            Core.Graphics.PreferredBackBufferHeight
        );

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

        // Create empty map registry
        _mapRegistry = new MapRegistry();

        // Create the entity manager, loading in entity definitions from file
        _entityManager = new EntityManager(_world, _mapRegistry, entityDefinitionsPath);
    }

    public override void BeginRun()
    {
        base.BeginRun();

        // Spawn entities
        // Load tilemap
        string tileMapPath = ContentPaths.Config("tilemap");
        TileMap.LoadTileMap(tileMapPath, _world, _mapRegistry);

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

        _playerEntity = _entityManager.Spawn("player", spawnPosition);

        // Center camera on player
        ref var pos = ref _playerEntity.Get<Position>();

        _camera.LookAt(
            Utils.ConvertGridToWorldCoordinate(
                pos.WorldCoordinate,
                _tileManager.TileWidth * _globalSettings.GlobalScale,
                _tileManager.TileHeight * _globalSettings.GlobalScale
            )
        );

        // Create systems
        _drawSystem = new DrawSystem(_world, _tileManager, _playerEntity, _camera, _mapRegistry);

        _playerMovementSystem = new PlayerMovementSystem(_world, _camera, _playerEntity);

        _tileAdjacencySystem = new TileAdjacencySystem(_world, _mapRegistry);

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
        _tileAdjacencySystem.Update(gameTime);

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
        // -- Pass 1: render tiles to render target --
        Core.GraphicsDevice.SetRenderTarget(_renderTarget2D);

        Core.GraphicsDevice.Clear(new Color(24, 25, 38));

        // get the transformation for world -> screen space
        var viewMatrix = _camera.GetViewMatrix();

        // Get projection matrix for projecting to CLIP space (-1 to 1)
        var projectionMatrix = Matrix.CreateOrthographicOffCenter(
            0,
            Core.GraphicsDevice.Viewport.Width,
            Core.GraphicsDevice.Viewport.Height,
            0,
            0,
            -1
        );

        // Combine them (Order matters: View * Projection)
        var finalTransform = viewMatrix * projectionMatrix;

        Core.TileBatch.Begin(
            sortMode: SpriteSortMode.BackToFront,
            effect: _uberShader,
            samplerState: SamplerState.PointClamp
        );

        var transformParam = _uberShader.Parameters["WorldViewProjection"];
        var textureSizeParam = _uberShader.Parameters["TextureSize"];
        var tileSizeParam = _uberShader.Parameters["TileSize"];
        var cameraZoomParam = _uberShader.Parameters["CameraZoom"];
        var viewportParam = _uberShader.Parameters["ViewportSize"];

        if (transformParam != null)
            transformParam.SetValue(finalTransform);
        if (textureSizeParam != null)
            textureSizeParam.SetValue(new Vector2(112, 112));
        if (tileSizeParam != null)
            tileSizeParam.SetValue(new Vector2(7, 7)); // Size of ONE
        if (cameraZoomParam != null)
            cameraZoomParam.SetValue(_camera.Zoom);
        if (viewportParam != null)
            viewportParam.SetValue(
                new Vector2(Core.GraphicsDevice.Viewport.Width, Core.GraphicsDevice.Viewport.Height)
            );

        DrawContext drawContext = new DrawContext(
            gameTime,
            _currentZLevel,
            _globalSettings.GlobalScale
        );

        _drawSystem.Update(in drawContext);

        Core.TileBatch.End();

        // -- Pass 2: Render Target to Screen with post-processing shader --

        Core.GraphicsDevice.SetRenderTarget(null);

        Core.SpriteBatch.Begin(effect: _screenEffect);

        var timeParam = _screenEffect.Parameters["Time"];
        if (timeParam != null)
            timeParam.SetValue((float)gameTime.TotalGameTime.TotalSeconds);

        Core.SpriteBatch.Draw(
            _renderTarget2D,
            new Rectangle(
                0,
                0,
                Core.Graphics.PreferredBackBufferWidth,
                Core.Graphics.PreferredBackBufferHeight
            ),
            Color.White
        );

        Core.SpriteBatch.End();
    }
}
