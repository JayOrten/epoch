using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Engine;
using Engine.Scenes;
using epoch.Scenes;

namespace epoch;

public class Game1 : Core
{

    public Game1(): base("epoch", 1280, 720, false)
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
