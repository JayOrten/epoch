using System;
using System.Collections.Generic;
using System.IO;

namespace epoch.Utilities;

// TODO: cleanup the extension handling here, so you don't have to change the code in three places
public static class ContentPaths
{
    private static readonly Dictionary<string, string[]> _extensions = new()
    {
        { "images", new[] { ".png", ".jpg" } },
        { "config", new[] { ".json", ".xml" } },
        { "audio", new[] { ".ogg", ".wav", ".mp3" } },
    };

    public static string Root { get; private set; } = "Content";
    public static readonly string ImagesDir = Path.Combine(Root, "images");
    public static readonly string ConfigDir = Path.Combine(Root, "config");
    public static readonly string AudioDir = Path.Combine(Root, "audio");
    public static readonly string FontsDir = Path.Combine(Root, "fonts");

    public static void SetRoot(string root)
    {
        Root = root;
    }

    public static string Image(string name) => GetPath(ImagesDir, name, "images");

    public static string Config(string name) => GetPath(ConfigDir, name, "config");

    public static string Audio(string name) => GetPath(AudioDir, name, "audio");

    public static string Font(string name) => GetPath(FontsDir, name, "fonts");

    private static string GetPath(string dir, string filename, string type)
    {
        var path = FindExisting(type, filename);
        if (path != null)
            return Require(Path.Combine(dir, path));
        throw new FileNotFoundException($"Missing content file: {filename} in {dir}");
    }

#nullable enable
    public static string? FindExisting(string category, string name)
#nullable disable
    {
        string dir = category switch
        {
            "images" => ImagesDir,
            "config" => ConfigDir,
            "audio" => AudioDir,
            "fonts" => FontsDir,
            _ => throw new ArgumentException($"Unknown category: {category}"),
        };

        foreach (var ext in _extensions[category])
        {
            string path = Path.Combine(dir, $"{name}{ext}");
            if (File.Exists(path))
                return path;
        }

        return null; // or throw an exception if preferred
    }

    public static string Require(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Missing content file: {path}");
        return path;
    }
}
