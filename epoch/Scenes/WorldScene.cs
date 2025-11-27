using System;
using System.Collections.Generic;
using System.Diagnostics;
using Arch;
using Arch.Core;
using Arch.Core.Extensions;
using Arch.Core.Extensions.Dangerous;
using Arch.Core.Utils;
using epoch.Components;
using epoch.Engine;
using epoch.Engine.Graphics;
using epoch.Engine.Input;
using epoch.Engine.Scenes;
using epoch.Entities;
using epoch.Systems;
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

    private TileManager _tileManager;

    private EntityManager _entityManager;

    public override void Initialize()
    {
        base.Initialize();

        var viewportAdapter = new BoxingViewportAdapter(
            Core.Instance.Window,
            Core.GraphicsDevice,
            1280,
            720
        );
        _camera = new OrthographicCamera(viewportAdapter);
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

        // TODO: load in the entity map
    }

    public record struct Position(float X, float Y);

    public record struct Velocity(float Dx, float Dy);

    public override void BeginRun()
    {
        base.BeginRun();

        // Create systems
        _drawSystem = new DrawSystem(_world, _tileManager);

        _world.Create(new GlobalSettings { GlobalScale = 8.0f });

        // Spawn entities
        _entityManager.Spawn("grass");

        _entityManager.Spawn(
            "tree",
            new EntityDefinition(new ComponentDefinition("Position").Add("vec2", "100,0"))
        );

        EntityDefinition entityDefinition = new EntityDefinition(
            new ComponentDefinition("Position")
        );

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 10000; i += 50)
        {
            for (int j = 0; j < 10000; j += 50)
            {
                // string coord = $"{i},{j}";
                string coord = string.Concat(i, ",", j);

                _entityManager.Spawn("grass", entityDefinition.Add("Position", "vec2", coord));
            }
        }
        sw.Stop();
        Log.Info($"Spawned grass in {sw.ElapsedMilliseconds} ms");
    }

    private Vector2 GetMovementDirection()
    {
        var movementDirection = Vector2.Zero;

        if (GameController.MoveDown())
        {
            movementDirection += Vector2.UnitY;
        }
        if (GameController.MoveUp())
        {
            movementDirection -= Vector2.UnitY;
        }
        if (GameController.MoveLeft())
        {
            movementDirection -= Vector2.UnitX;
        }
        if (GameController.MoveRight())
        {
            movementDirection += Vector2.UnitX;
        }

        return movementDirection;
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

    public override void Update(GameTime gameTime)
    {
        const float movementSpeed = 1000;
        _camera.Move(GetMovementDirection() * movementSpeed * gameTime.GetElapsedSeconds());

        AdjustZoom();
    }

    public override void Draw(GameTime gameTime)
    {
        Core.GraphicsDevice.Clear(Color.Black);

        var transformMatrix = _camera.GetViewMatrix();

        Core.SpriteBatch.Begin(
            samplerState: SamplerState.PointClamp,
            transformMatrix: transformMatrix
        );

        _drawSystem.Update(in gameTime);

        Core.SpriteBatch.End();
    }
}
