using Microsoft.Xna.Framework;

namespace epoch.Components;

public struct GlobalSettings
{
    public GlobalSettings()
    {
        GlobalScale = 8.0f;
    }

    public float GlobalScale;
}

// TODO: need to reconcile with the tilemanager stuff
public struct GraphicalTile
{
    public GraphicalTile()
    {
        Name = "default";
        Color = Color.White;
        Scale = 1.0f;
    }

    public string Name;
    public Color Color;
    public float Scale;
}

public struct Position
{
    public Vector2 Vec2;
}
