using Engine;
using Engine.Scenes;
using Engine.Graphics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Audio;

namespace aegis.Scenes;

public class WorldScene : Scene
{
    private Tilemap _tilemap;
    
    public override void Initialize()
    {
        base.Initialize();
    }

    public override void LoadContent()
    {
        // Load textures
        TextureAtlas atlas = TextureAtlas.FromFile(Core.Content, "images/atlas-definition.xml");

        // Create the tilemap from the XML file
        _tilemap = Tilemap.FromFile(Content, "images/tilemap-definition.xml");
        _tilemap.Scale = new Vector2(8.0f, 8.0f);


    }

    public override void Update(GameTime gameTime)
    {

    }

    public override void Draw(GameTime gameTime)
    {
        Core.GraphicsDevice.Clear(Color.CornflowerBlue);
        Core.SpriteBatch.Begin(samplerState: SamplerState.PointClamp);
        _tilemap.Draw(Core.SpriteBatch);
        Core.SpriteBatch.End();
    }
}
