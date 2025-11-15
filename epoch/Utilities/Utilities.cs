using System;
using System.Globalization;
using Microsoft.Xna.Framework;

namespace epoch.Utilities;

public static class Utils
{
    public static Color FromHex(string hex)
    {
        // Strip the leading # if present
        if (hex.StartsWith("#"))
            hex = hex.Substring(1);

        // Parse as RRGGBB or AARRGGBB
        if (hex.Length == 6)
        {
            byte r = byte.Parse(hex.Substring(0, 2), NumberStyles.HexNumber);
            byte g = byte.Parse(hex.Substring(2, 2), NumberStyles.HexNumber);
            byte b = byte.Parse(hex.Substring(4, 2), NumberStyles.HexNumber);
            return new Color(r, g, b);
        }
        else if (hex.Length == 8)
        {
            byte a = byte.Parse(hex.Substring(0, 2), NumberStyles.HexNumber);
            byte r = byte.Parse(hex.Substring(2, 2), NumberStyles.HexNumber);
            byte g = byte.Parse(hex.Substring(4, 2), NumberStyles.HexNumber);
            byte b = byte.Parse(hex.Substring(6, 2), NumberStyles.HexNumber);
            return new Color(r, g, b, a);
        }

        throw new ArgumentException("Invalid hex color format");
    }
}
