using System.Collections.Generic;
using Godot;

public sealed class RangeSpikeShotPreset
{
    public RangeSpikeShotPreset(
        string id,
        string displayName,
        string clubLabel,
        Color color,
        float speedMph,
        float launchAngleDeg,
        float launchDirectionDeg,
        float backspinRpm,
        float sidespinRpm)
    {
        Id = id;
        DisplayName = displayName;
        ClubLabel = clubLabel;
        Color = color;
        SpeedMph = speedMph;
        LaunchAngleDeg = launchAngleDeg;
        LaunchDirectionDeg = launchDirectionDeg;
        BackspinRpm = backspinRpm;
        SidespinRpm = sidespinRpm;
    }

    public string Id { get; }
    public string DisplayName { get; }
    public string ClubLabel { get; }
    public Color Color { get; }
    public float SpeedMph { get; }
    public float LaunchAngleDeg { get; }
    public float LaunchDirectionDeg { get; }
    public float BackspinRpm { get; }
    public float SidespinRpm { get; }
}

public static class RangeSpikeShotCatalog
{
    public static IReadOnlyList<RangeSpikeShotPreset> Presets { get; } = new List<RangeSpikeShotPreset>
    {
        new("mizuno_9i", "Mizuno 9i", "9i", new Color("63f2ff"), 109.0f, 20.5f, 0.2f, 7600.0f, 120.0f),
        new("callaway_9i", "Callaway 9i", "9i", new Color("ffb347"), 111.0f, 19.4f, -0.3f, 7100.0f, -80.0f),
        new("mizuno_8i", "Mizuno 8i", "8i", new Color("7dff8a"), 118.0f, 18.0f, 0.4f, 6500.0f, 140.0f),
        new("driver_shaft_a", "Driver Shaft A", "Driver", new Color("ff6b9d"), 156.0f, 11.6f, -0.6f, 2500.0f, -220.0f),
        new("driver_shaft_b", "Driver Shaft B", "Driver", new Color("d2a8ff"), 159.0f, 10.9f, 0.8f, 2280.0f, 260.0f),
    };

    public static RangeSpikeShotPreset FindById(string id)
    {
        foreach (RangeSpikeShotPreset preset in Presets)
        {
            if (preset.Id == id)
                return preset;
        }

        return Presets[0];
    }
}
