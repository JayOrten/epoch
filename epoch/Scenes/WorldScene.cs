using Engine;
using Engine.Graphics;
using Engine.Scenes;
using epoch.Utilities;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MonoGame.Extended;
using MonoGame.Extended.ViewportAdapters;

namespace epoch.Scenes;

public class WorldScene : Scene
{
    private OrthographicCamera _camera;

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
        string tileSetPath = ContentPaths.Image($"{mode}-tileset");

        // Load tile definitions
        TileDefinitions tileDefinitions = TileDefinitions.FromFile(Content, tileDefinitionsPath);

        // Load tileset
        Tileset tileset = Tileset.FromFile(Content, tileSetPath);

        // Create the tile manager
        TileManager tileManager = new TileManager(tileset, tileDefinitions, mode);
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
        Core.SpriteBatch.End();
    }
}
