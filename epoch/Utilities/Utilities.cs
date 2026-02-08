using System;
using System.Globalization;
using Microsoft.Xna.Framework;

namespace epoch.Utilities;

/// <summary>
/// String-to-CLR-type conversion helpers used by the component factory to hydrate
/// component properties from XML/definition strings. Also provides coordinate conversion.
/// </summary>
public static class Utils
{
    /// <summary>
    /// Converts a string <paramref name="value"/> to the given <paramref name="targetType"/>.
    /// Supports primitives, <see cref="Vector2"/>, <see cref="Vector3"/>, and <see cref="Color"/>.
    /// Falls back to <see cref="Convert.ChangeType"/> for other types.
    /// </summary>
    public static object ConvertValue(string value, Type targetType)
    {
        if (targetType == null)
        {
            Log.Error("ConvertValue called with null targetType for value {0}", value);
            throw new ArgumentNullException(nameof(targetType));
        }

        if (targetType == typeof(string))
            return value;
        if (targetType == typeof(int))
            return ParseInt(value);
        if (targetType == typeof(float))
            return ParseFloat(value);
        if (targetType == typeof(double))
            return ParseDouble(value);
        if (targetType == typeof(bool))
            return ParseBool(value);
        if (targetType == typeof(Vector2))
            return ParseVector2(value);
        if (targetType == typeof(Vector3))
            return ParseVector3(value);
        if (targetType == typeof(Color?))
            return ParseColor(value);

        // Fallback: try Convert.ChangeType for simple convertible types
        return System.Convert.ChangeType(value, targetType);
    }

    public static int ParseInt(string value) => int.Parse(value, CultureInfo.InvariantCulture);

    public static float ParseFloat(string value) =>
        float.Parse(value, CultureInfo.InvariantCulture);

    public static double ParseDouble(string value) =>
        double.Parse(value, CultureInfo.InvariantCulture);

    public static bool ParseBool(string value) => bool.Parse(value);

    public static string ParseString(string value) => value;

    public static T ParseEnum<T>(string value) where T : struct, Enum =>
        Enum.Parse<T>(value, ignoreCase: true);

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

    /// <summary>
    /// Parses a color from either "R,G,B[,A]" byte format or a named XNA color (e.g. "White").
    /// </summary>
    public static Color ParseColor(string value)
    {
        if (value.Contains(","))
        {
            var parts = value.Split(',');
            byte r = byte.Parse(parts[0]);
            byte g = byte.Parse(parts[1]);
            byte b = byte.Parse(parts[2]);
            byte a = parts.Length > 3 ? byte.Parse(parts[3]) : (byte)255;
            return new Color(r, g, b, a);
        }
        else
        {
            // Try to parse named color
            var prop = typeof(Color).GetProperty(value);
            if (prop != null)
                return (Color)prop.GetValue(null);
            throw new ArgumentException($"Invalid color format: {value}");
        }
    }

    /// <summary>
    /// Converts integer grid position to pixel-center world coordinates.
    /// </summary>
    public static Vector2 ConvertGridToWorldCoordinate(
        Vector2 gridCoordinate,
        float tileWidth,
        float tileHeight
    )
    {
        // Converts unit grid position to center of tile in world coordinates (pixels)
        Vector2 tileSize = new Vector2(tileWidth, tileHeight);

        Vector2 worldCoordinate = (gridCoordinate * tileSize) + (tileSize * 0.5f);

        return worldCoordinate;
    }
}

/// <summary>
/// Camera math utilities. <see cref="SmoothDamp"/> implements Unity-style critically-damped
/// spring interpolation for smooth camera following.
/// </summary>
public static class CameraUtils
{
    /// <summary>
    /// Smoothly moves <paramref name="current"/> toward <paramref name="target"/> using
    /// a critically-damped spring. Modifies <paramref name="currentVelocity"/> in place.
    /// </summary>
    public static Vector2 SmoothDamp(
        Vector2 current,
        Vector2 target,
        ref Vector2 currentVelocity,
        float smoothTime,
        float maxSpeed,
        float deltaTime
    )
    {
        smoothTime = Math.Max(0.0001f, smoothTime);
        float num = 2f / smoothTime;
        float num2 = num * deltaTime;
        float num3 = 1f / (1f + num2 + 0.48f * num2 * num2 + 0.235f * num2 * num2 * num2);

        Vector2 change = current - target;
        Vector2 originalTo = target;

        // Clamp maximum speed
        float maxChange = maxSpeed * smoothTime;
        float sqrMag = change.LengthSquared();
        if (sqrMag > maxChange * maxChange)
        {
            float mag = (float)Math.Sqrt(sqrMag);
            change = change / mag * maxChange;
        }

        target = current - change;

        Vector2 temp = (currentVelocity + num * change) * deltaTime;
        currentVelocity = (currentVelocity - num * temp) * num3;
        Vector2 output = target + (change + temp) * num3;

        // Prevent overshooting
        if (Vector2.Dot(originalTo - current, output - originalTo) > 0f)
        {
            output = originalTo;
            currentVelocity = (output - originalTo) / deltaTime;
        }

        return output;
    }
}
