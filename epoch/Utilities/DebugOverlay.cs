using epoch.Input;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace epoch.Utilities;

internal class DebugOverlay
{
    private readonly SpriteFont _font;
    private bool _visible;
    private float _fps;

    public DebugOverlay(SpriteFont font)
    {
        _font = font;
    }

    public void Update(GameTime gameTime)
    {
        if (GameController.ToggleDebugOverlay())
            _visible = !_visible;

        if (!_visible)
            return;

        // Calculate FPS as an exponential moving average
        float elapsed = (float)gameTime.ElapsedGameTime.TotalSeconds;
        if (elapsed > 0)
            _fps = MathHelper.Lerp(_fps, 1f / elapsed, 0.05f);
    }

    public void Draw()
    {
        if (!_visible)
            return;

        Core.SpriteBatch.Begin();
        Core.SpriteBatch.DrawString(_font, $"FPS: {_fps:F0}", new Vector2(10, 10), Color.White);
        Core.SpriteBatch.End();
    }
}
