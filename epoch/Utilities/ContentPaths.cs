using System;
using System.Collections.Generic;
using System.IO;
using epoch.Engine;

namespace epoch.Utilities;

// TODO: cleanup the extension handling here, so you don't have to change the code in three places
public static class ContentPaths
{
    private static readonly Dictionary<string, string[]> _extensions = new()
    {
        { "images", new[] { ".png", ".jpg" } },
        { "config", new[] { ".json", ".xml", ".txt" } },
        { "audio", new[] { ".ogg", ".wav", ".mp3" } },
    };

    public static string Root { get; private set; } = Core.Content.RootDirectory;
    public static string ImagesDir = Path.Combine(Root, "images");
    public static string ConfigDir = Path.Combine(Root, "config");
    public static string AudioDir = Path.Combine(Root, "audio");
    public static string FontsDir = Path.Combine(Root, "fonts");

    public static void SetRoot(string root)
    {
        Root = root;
    }

    public static string Image(string name) => GetPath(ImagesDir, name);

    public static string Config(string name) => GetPath(ConfigDir, name);

    public static string Audio(string name) => GetPath(AudioDir, name);

    public static string Font(string name) => GetPath(FontsDir, name);

    private static string GetPath(string dir, string filename)
    {
        var path = FindExisting(dir, filename);
        if (path != null)
            return path;
        throw new FileNotFoundException($"Missing content file: {filename} in {dir}");
    }

#nullable enable
    public static string? FindExisting(string dir, string name)
#nullable disable
    {
        string category = dir switch
        {
            var d when d == ImagesDir => "images",
            var d when d == ConfigDir => "config",
            var d when d == AudioDir => "audio",
            var d when d == FontsDir => "fonts",
            _ => throw new ArgumentException($"Unknown content directory: {dir}"),
        };

        foreach (var ext in _extensions[category])
        {
            string path = Path.Combine(dir, $"{name}{ext}");
            Console.WriteLine($"Checking for file: {path}");
            if (File.Exists(path))
                // If it's a config file, return the full path with extension
                // Otherwise, return the path without extension
                if (category == "config")
                    return path;
                else
                    return Path.Combine(dir, $"{name}");
        }

        return null;
    }
}
