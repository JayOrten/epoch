using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace epoch.Input;

/// <summary>
/// Provides a game-specific input abstraction that maps physical inputs
/// to game actions, bridging our input system with game-specific functionality.
/// </summary>
public static class GameController
{
    private static KeyboardInfo s_keyboard => Core.Input.Keyboard;
    private static GamePadInfo s_gamePad => Core.Input.GamePads[(int)PlayerIndex.One];
    private static MouseInfo s_mouse => Core.Input.Mouse;

    public static Point MousePosition()
    {
        return s_mouse.Position;
    }

    // -- MOVEMENT --

    /// <summary>
    /// Returns true if the player has triggered the "move up" action.
    /// </summary>
    public static bool MoveUp()
    {
        return s_keyboard.WasKeyJustPressed(Keys.Up)
            || s_keyboard.WasKeyJustPressed(Keys.W)
            || s_gamePad.WasButtonJustPressed(Buttons.DPadUp)
            || s_gamePad.WasButtonJustPressed(Buttons.LeftThumbstickUp);
    }

    /// <summary>
    /// Returns true if the player has triggered the "move up" action.
    /// </summary>
    public static bool MoveUpHeld()
    {
        return s_keyboard.IsKeyDown(Keys.Up)
            || s_keyboard.IsKeyDown(Keys.W)
            || s_gamePad.IsButtonDown(Buttons.DPadUp)
            || s_gamePad.IsButtonDown(Buttons.LeftThumbstickUp);
    }

    /// <summary>
    /// Returns true if the player has triggered the "move down" action.
    /// </summary>
    public static bool MoveDown()
    {
        return s_keyboard.WasKeyJustPressed(Keys.Down)
            || s_keyboard.WasKeyJustPressed(Keys.S)
            || s_gamePad.WasButtonJustPressed(Buttons.DPadDown)
            || s_gamePad.WasButtonJustPressed(Buttons.LeftThumbstickDown);
    }

    /// <summary>
    /// Returns true if the player has triggered the "move down" action.
    /// </summary>
    public static bool MoveDownHeld()
    {
        return s_keyboard.IsKeyDown(Keys.Down)
            || s_keyboard.IsKeyDown(Keys.S)
            || s_gamePad.IsButtonDown(Buttons.DPadDown)
            || s_gamePad.IsButtonDown(Buttons.LeftThumbstickDown);
    }

    /// <summary>
    /// Returns true if the player has triggered the "move left" action.
    /// </summary>
    public static bool MoveLeft()
    {
        return s_keyboard.WasKeyJustPressed(Keys.Left)
            || s_keyboard.WasKeyJustPressed(Keys.A)
            || s_gamePad.WasButtonJustPressed(Buttons.DPadLeft)
            || s_gamePad.WasButtonJustPressed(Buttons.LeftThumbstickLeft);
    }

    /// <summary>
    /// Returns true if the player has triggered the "move left" action.
    /// </summary>
    public static bool MoveLeftHeld()
    {
        return s_keyboard.IsKeyDown(Keys.Left)
            || s_keyboard.IsKeyDown(Keys.A)
            || s_gamePad.IsButtonDown(Buttons.DPadLeft)
            || s_gamePad.IsButtonDown(Buttons.LeftThumbstickLeft);
    }

    /// <summary>
    /// Returns true if the player has triggered the "move right" action.
    /// </summary>
    public static bool MoveRight()
    {
        return s_keyboard.WasKeyJustPressed(Keys.Right)
            || s_keyboard.WasKeyJustPressed(Keys.D)
            || s_gamePad.WasButtonJustPressed(Buttons.DPadRight)
            || s_gamePad.WasButtonJustPressed(Buttons.LeftThumbstickRight);
    }

    /// <summary>
    /// Returns true if the player has triggered the "move right" action.
    /// </summary>
    public static bool MoveRightHeld()
    {
        return s_keyboard.IsKeyDown(Keys.Right)
            || s_keyboard.IsKeyDown(Keys.D)
            || s_gamePad.IsButtonDown(Buttons.DPadRight)
            || s_gamePad.IsButtonDown(Buttons.LeftThumbstickRight);
    }

    // -- LOOK --

    public static bool LookUpHeld()
    {
        return s_keyboard.IsKeyDown(Keys.U) || s_gamePad.IsButtonDown(Buttons.RightThumbstickUp);
    }

    public static bool LookDownHeld()
    {
        return s_keyboard.IsKeyDown(Keys.J) || s_gamePad.IsButtonDown(Buttons.RightThumbstickDown);
    }

    public static bool LookLeftHeld()
    {
        return s_keyboard.IsKeyDown(Keys.H) || s_gamePad.IsButtonDown(Buttons.RightThumbstickLeft);
    }

    public static bool LookRightHeld()
    {
        return s_keyboard.IsKeyDown(Keys.K) || s_gamePad.IsButtonDown(Buttons.RightThumbstickRight);
    }

    public static bool ZoomInHeld()
    {
        return s_keyboard.IsKeyDown(Keys.Z);
    }

    public static bool ZoomOutHeld()
    {
        return s_keyboard.IsKeyDown(Keys.X);
    }

    public static bool FDown()
    {
        return s_keyboard.WasKeyJustPressed(Keys.F);
    }

    public static bool RDown()
    {
        return s_keyboard.WasKeyJustPressed(Keys.R);
    }

    // -- DEBUG --

    public static bool ToggleDebugOverlay()
    {
        return s_keyboard.WasKeyJustPressed(Keys.F3)
            || s_gamePad.WasButtonJustPressed(Buttons.Back);
    }

    /// <summary>
    /// Returns true if the player has triggered the "pause" action.
    /// </summary>
    public static bool Pause()
    {
        return s_keyboard.WasKeyJustPressed(Keys.Escape)
            || s_gamePad.WasButtonJustPressed(Buttons.Start);
    }

    /// <summary>
    /// Returns true if the player has triggered the "action" button,
    /// typically used for menu confirmation.
    /// </summary>
    public static bool Action()
    {
        return s_keyboard.WasKeyJustPressed(Keys.Enter)
            || s_gamePad.WasButtonJustPressed(Buttons.A);
    }
}
