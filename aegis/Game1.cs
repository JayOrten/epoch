using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Engine;
using Engine.Scenes;
using aegis.Scenes;

namespace aegis;

public class Game1 : Core
{

    public Game1(): base("aegis", 1280, 720, false)
    {

    }

    protected override void Initialize()

    {
        base.Initialize();

        ChangeScene(new WorldScene());
    }

    protected override void LoadContent()
    {
        // TODO: use this.Content to load your game content here
    }
}
