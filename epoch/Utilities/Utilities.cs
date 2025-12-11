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
}
