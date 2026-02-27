using System;
using System.Threading;
using epoch.Input;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace epoch.Utilities;

internal class DebugOverlay
{
    private readonly SpriteFont _font;
    private bool _visible;
    private float _fps;
    private bool _bordersOff;

    // GC tracking: detect collections by watching GC.CollectionCount changes
    private int _lastGc0;
    private int _lastGc1;
    private int _lastGc2;
    private int _gc0ThisSecond;
    private int _gc1ThisSecond;
    private int _gc2ThisSecond;
    private int _gc0Display;
    private int _gc1Display;
    private int _gc2Display;
    private float _gcTimer;

    // Frame time spike detection
    private float _maxFrameTime;
    private float _maxFrameTimeDisplay;
    private float _spikeTimer;

    public bool BordersOff => _bordersOff;

    public DebugOverlay(SpriteFont font)
    {
        _font = font;
        _lastGc0 = GC.CollectionCount(0);
        _lastGc1 = GC.CollectionCount(1);
        _lastGc2 = GC.CollectionCount(2);
    }

    public void Update(GameTime gameTime)
    {
        if (GameController.ToggleDebugOverlay())
            _visible = !_visible;

        if (!_visible)
            return;

        float elapsed = (float)gameTime.ElapsedGameTime.TotalSeconds;
        if (elapsed > 0)
            _fps = MathHelper.Lerp(_fps, 1f / elapsed, 0.05f);

        if (GameController.ToggleBorders())
            _bordersOff = !_bordersOff;

        // Track GC collections per second
        int gc0 = GC.CollectionCount(0);
        int gc1 = GC.CollectionCount(1);
        int gc2 = GC.CollectionCount(2);
        _gc0ThisSecond += gc0 - _lastGc0;
        _gc1ThisSecond += gc1 - _lastGc1;
        _gc2ThisSecond += gc2 - _lastGc2;
        _lastGc0 = gc0;
        _lastGc1 = gc1;
        _lastGc2 = gc2;

        _gcTimer += elapsed;
        if (_gcTimer >= 1.0f)
        {
            _gc0Display = _gc0ThisSecond;
            _gc1Display = _gc1ThisSecond;
            _gc2Display = _gc2ThisSecond;
            _gc0ThisSecond = 0;
            _gc1ThisSecond = 0;
            _gc2ThisSecond = 0;
            _gcTimer = 0;
        }

        // Track worst frame time over 1-second windows
        if (elapsed > _maxFrameTime)
            _maxFrameTime = elapsed;

        _spikeTimer += elapsed;
        if (_spikeTimer >= 1.0f)
        {
            _maxFrameTimeDisplay = _maxFrameTime;
            _maxFrameTime = 0;
            _spikeTimer = 0;
        }
    }

    public void Draw()
    {
        if (!_visible)
            return;

        float zoom = ECS.GlobalContext.Camera.Zoom;
        float tileScreenPx = 24f * ECS.GlobalContext.GlobalScale * zoom;
        float texelsPerPixel = 1f / (ECS.GlobalContext.GlobalScale * zoom);
        float snapMul = Math.Clamp(1f / zoom, 1f, 4f);
        float snapRes = 1f / snapMul;

        int instances = Core.TileInstancing.InstanceCount;
        int bufferCap = Core.TileInstancing.BufferCapacity;

        ThreadPool.GetAvailableThreads(out int workerAvail, out int ioAvail);
        ThreadPool.GetMaxThreads(out int workerMax, out int ioMax);
        int workerBusy = workerMax - workerAvail;

        string text =
            $"FPS: {_fps:F0}  worst: {_maxFrameTimeDisplay * 1000:F1}ms\n" +
            $"Zoom: {zoom:F3}  Tile: {tileScreenPx:F1}px\n" +
            $"Snap: 1/{snapMul:F0}px  Borders [F4]: {(_bordersOff ? "OFF" : "ON")}\n" +
            $"GC/s: gen0={_gc0Display} gen1={_gc1Display} gen2={_gc2Display}\n" +
            $"Instances: {instances}/{bufferCap}\n" +
            $"Threads busy: {workerBusy}";

        Core.SpriteBatch.Begin();
        Core.SpriteBatch.DrawString(_font, text, new Vector2(11, 11), Color.Black);
        Core.SpriteBatch.DrawString(_font, text, new Vector2(10, 10), Color.White);
        Core.SpriteBatch.End();
    }
}
