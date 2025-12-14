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

    public static object ConvertValue(string value, Type targetType)
    {
        if (targetType == typeof(string))
            return value;
        if (targetType == typeof(int))
            return int.Parse(value);
        if (targetType == typeof(float))
            return float.Parse(value);
        if (targetType == typeof(double))
            return double.Parse(value);
        if (targetType == typeof(bool))
            return bool.Parse(value);

        if (targetType == typeof(Vector2))
        {
            // Expect "x,y"
            var parts = value.Split(',');
            return new Vector2(float.Parse(parts[0]), float.Parse(parts[1]));
        }

        if (targetType == typeof(Vector3))
        {
            // Expect "x,y,z"
            var parts = value.Split(',');
            return new Vector3(float.Parse(parts[0]), float.Parse(parts[1]), float.Parse(parts[2]));
        }

        if (targetType == typeof(Color?))
        {
            if (value.StartsWith("#"))
                return FromHex(value);
            else
                throw new ArgumentException("Color string must be in hex format starting with #");
        }

        if (targetType == null)
        {
            Log.Error(
                "Someone did an oopsie! Trying to convert value {0} to targetType {1}",
                value,
                targetType
            );
        }

        // Fallback: try Convert.ChangeType for simple convertible types
        return System.Convert.ChangeType(value, targetType);
    }

    public static Vector2 ConvertGridToWorldCoordinate(
        Vector2 gridCoordinate,
        int tileWidth,
        int tileHeight
    )
    {
        // Converts unit grid position to center of tile in world coordinates (pixels)
        Vector2 tileSize = new Vector2(tileWidth, tileHeight);

        return (gridCoordinate * tileSize) + (tileSize * 0.5f);
    }
}

public static class ComponentParsers
{
    public static int ParseInt(string value) => int.Parse(value, CultureInfo.InvariantCulture);

    public static float ParseFloat(string value) =>
        float.Parse(value, CultureInfo.InvariantCulture);

    public static bool ParseBool(string value) => bool.Parse(value);

    public static string ParseString(string value) => value;

    public static Vector2 ParseVector2(string value)
    {
        var parts = value.Split(',');
        return new Vector2(
            float.Parse(parts[0], CultureInfo.InvariantCulture),
            float.Parse(parts[1], CultureInfo.InvariantCulture)
        );
    }

    // Custom parsing for your Vector3 format (e.g. "1.0,2.5,0")
    public static Vector3 ParseVector3(string value)
    {
        var parts = value.Split(',');
        return new Vector3(
            float.Parse(parts[0], CultureInfo.InvariantCulture),
            float.Parse(parts[1], CultureInfo.InvariantCulture),
            float.Parse(parts[2], CultureInfo.InvariantCulture)
        );
    }

    // Handle Nullable types by returning the value type (assignment to Nullable works automatically)
    public static Color ParseColor(string value)
    {
        // Simple implementation: Assume Hex or Named color
        var prop = typeof(Color).GetProperty(value);
        if (prop != null)
            return (Color)prop.GetValue(null);
        return Color.White;
    }
}
