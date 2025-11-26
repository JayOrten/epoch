using epoch.Engine;
using epoch.Scenes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace epoch;

public class Game1 : Core
{
    public Game1()
        : base("epoch", 1280, 720, false) { }

    protected override void Initialize()
    {
        base.Initialize();

        Log.Initialize();

        Log.Info("Game initialized.");

        ChangeScene(new WorldScene());
    }

    protected override void LoadContent()
    {
        // TODO: use this.Content to load your game content here
    }
}
