using Microsoft.Xna.Framework;

public struct GlobalSettings
{
    public float GlobalScale { get; set; }

    public GlobalSettings()
    {
        GlobalScale = 4.0f;
    }

    public GlobalSettings(float globalScale)
    {
        GlobalScale = globalScale;
    }
}

public readonly struct DrawContext
{
    public readonly GameTime GameTime;
    public readonly float ZLevel;
    public readonly float GlobalScale;
    public readonly Vector2 Center;

    public DrawContext(GameTime gameTime, float zLevel, float globalScale, Vector2 center)
    {
        GameTime = gameTime;
        ZLevel = zLevel;
        GlobalScale = globalScale;
        Center = center;
    }
}
