using Engine;
using Engine.Scenes;
using Engine.Graphics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Audio;
using MonoGame.Extended;
using MonoGame.Extended.ViewportAdapters;

namespace epoch.Scenes;

public class WorldScene : Scene
{
    private Tilemap _tilemap;

    private OrthographicCamera _camera;
    
    public override void Initialize()
    {
        base.Initialize();

        var viewportAdapter = new BoxingViewportAdapter(Core.Instance.Window, Core.GraphicsDevice, 1280, 720);
        _camera = new OrthographicCamera(viewportAdapter);
    }

    public override void LoadContent()
    {
        // Load textures
        TextureAtlas atlas = TextureAtlas.FromFile(Core.Content, "images/atlas-definition.xml");

        // Create the tilemap from the XML file
        _tilemap = Tilemap.FromFile(Content, "images/tilemap-definition.xml");
        _tilemap.Scale = new Vector2(8.0f, 8.0f);
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

    public override void Update(GameTime gameTime)
    {
        const float movementSpeed = 200;
        _camera.Move(GetMovementDirection() * movementSpeed * gameTime.GetElapsedSeconds());
    }

    public override void Draw(GameTime gameTime)
    {
        Core.GraphicsDevice.Clear(Color.CornflowerBlue);

        var transformMatrix = _camera.GetViewMatrix();
        
        Core.SpriteBatch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: transformMatrix);
        _tilemap.Draw(Core.SpriteBatch);
        Core.SpriteBatch.End();
    }
}
