using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace epoch.Utilities;

public class TuningConfig
{
    private static TuningConfig _instance = new();
    private static string _filePath;
    private static FileSystemWatcher _watcher;
    private static long _lastReloadTicks;

    public static TuningConfig Instance => _instance;

    [JsonPropertyName("depthStrength")]
    public float DepthStrength { get; set; } = 0.04f;

    [JsonPropertyName("maxZScale")]
    public float MaxZScale { get; set; } = 1.6f;

    [JsonPropertyName("maxStackHeight")]
    public float MaxStackHeight { get; set; } = 24f;

    [JsonPropertyName("vpBlendFactor")]
    public float VpBlendFactor { get; set; } = 0.4f;

    [JsonPropertyName("zLerpRate")]
    public float ZLerpRate { get; set; } = 0.0001f;

    [JsonPropertyName("cameraSmoothTime")]
    public float CameraSmoothTime { get; set; } = 0.45f;

    [JsonPropertyName("zoomSpeed")]
    public float ZoomSpeed { get; set; } = 0.01f;

    [JsonPropertyName("rotationSpeed")]
    public float RotationSpeed { get; set; } = 2.0f;

    /// <summary>
    /// Exponential smoothing coefficient for rotation velocity (higher = snappier, lower = more inertia).
    /// Controls how quickly velocity ramps up on input and coasts to zero on release.
    /// </summary>
    [JsonPropertyName("rotationSmoothTime")]
    public float RotationSmoothTime { get; set; } = 8.0f;

    [JsonPropertyName("elevationSpeed")]
    public float ElevationSpeed { get; set; } = 1000.0f;

    /// <summary>
    /// Exponential smoothing coefficient for elevation velocity (higher = snappier, lower = more inertia).
    /// </summary>
    [JsonPropertyName("elevationSmoothTime")]
    public float ElevationSmoothTime { get; set; } = 8.0f;

    [JsonPropertyName("minVpDistance")]
    public float MinVpDistance { get; set; } = 0f;

    [JsonPropertyName("maxVpDistance")]
    public float MaxVpDistance { get; set; } = 1200f;

    [JsonPropertyName("leadRampUp")]
    public float LeadRampUp { get; set; } = 1.5f;

    [JsonPropertyName("leadRampDown")]
    public float LeadRampDown { get; set; } = 2.0f;

    public static void Load(string path)
    {
        _filePath = path;
        Reload();

        string dir = Path.GetDirectoryName(Path.GetFullPath(path));
        string filename = Path.GetFileName(path);

        _watcher = new FileSystemWatcher(dir, filename)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true,
        };

        _watcher.Changed += OnFileChanged;
    }

    private static void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        long now = Stopwatch.GetTimestamp();
        long debounceThreshold = Stopwatch.Frequency / 5; // 200ms
        if (now - _lastReloadTicks < debounceThreshold)
            return;
        _lastReloadTicks = now;

        Reload();
    }

    private static void Reload()
    {
        try
        {
            string json = File.ReadAllText(_filePath);
            var config = JsonSerializer.Deserialize<TuningConfig>(json);
            if (config != null)
            {
                _instance = config;
                Log.Info("TuningConfig reloaded from {0}", _filePath);
            }
        }
        catch (Exception ex)
        {
            Log.Error("TuningConfig reload failed: {0}", ex.Message);
        }
    }
}
