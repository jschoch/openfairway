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
        float sidespinRpm,
        float clubHeadSpeedMph = 0f)
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
        ClubHeadSpeedMph = clubHeadSpeedMph;
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
    /// <summary>Nominal club head speed (mph). Used to compute smash factor = ballSpeed / clubHeadSpeed. 0 = unknown.</summary>
    public float ClubHeadSpeedMph { get; }
}

public static class RangeSpikeShotCatalog
{
    // Generic presets — typical average launch conditions, 60° wedge → driver.
    // Ball speed values are approximate tour-average amateur figures.
    // Club head speeds derived from typical amateur smash factors per club.
    // CHS = nominal ball speed / typical smash factor for that club.
    public static IReadOnlyList<RangeSpikeShotPreset> GenericPresets { get; } = new List<RangeSpikeShotPreset>
    {
        new("gen_lw",  "60° Wedge",    "LW",     new Color("f0c040"),  82.0f, 28.0f, 0f, 9200.0f, 0f,  66.0f),
        new("gen_sw",  "Sand Wedge",   "SW",     new Color("f0c040"),  90.0f, 26.0f, 0f, 9000.0f, 0f,  70.0f),
        new("gen_gw",  "Gap Wedge",    "GW",     new Color("f0c040"),  98.0f, 24.0f, 0f, 8800.0f, 0f,  74.0f),
        new("gen_pw",  "Pitching Wedge","PW",    new Color("f0c040"), 104.0f, 22.0f, 0f, 8500.0f, 0f,  77.0f),
        new("gen_9i",  "9 Iron",       "9I",     new Color("7dcfff"), 109.0f, 20.5f, 0f, 8000.0f, 0f,  80.0f),
        new("gen_8i",  "8 Iron",       "8I",     new Color("7dcfff"), 112.0f, 19.5f, 0f, 7500.0f, 0f,  82.0f),
        new("gen_7i",  "7 Iron",       "7I",     new Color("7dcfff"), 116.0f, 18.5f, 0f, 7000.0f, 0f,  84.0f),
        new("gen_6i",  "6 Iron",       "6I",     new Color("7dcfff"), 120.0f, 17.5f, 0f, 6200.0f, 0f,  86.0f),
        new("gen_5i",  "5 Iron",       "5I",     new Color("7dcfff"), 124.0f, 16.5f, 0f, 5500.0f, 0f,  89.0f),
        new("gen_4i",  "4 Iron",       "4I",     new Color("7dcfff"), 128.0f, 16.0f, 0f, 5000.0f, 0f,  91.0f),
        new("gen_3i",  "3 Iron",       "3I",     new Color("7dcfff"), 132.0f, 15.5f, 0f, 4800.0f, 0f,  94.0f),
        new("gen_4h",  "4 Hybrid",     "4H",     new Color("a8e6a8"), 138.0f, 15.0f, 0f, 4500.0f, 0f,  97.0f),
        new("gen_5w",  "5 Wood",       "5W",     new Color("a8e6a8"), 145.0f, 14.0f, 0f, 4100.0f, 0f, 101.0f),
        new("gen_3w",  "3 Wood",       "3W",     new Color("a8e6a8"), 152.0f, 13.0f, 0f, 3500.0f, 0f, 106.0f),
        new("gen_dr",  "Driver",       "DRIVER", new Color("ff9966"), 160.0f, 12.0f, 0f, 2700.0f, 0f, 108.0f),
    };

    // Named equipment presets for head-to-head shaft/club comparisons.
    public static IReadOnlyList<RangeSpikeShotPreset> EquipmentPresets { get; } = new List<RangeSpikeShotPreset>
    {
        new("mizuno_9i",     "Mizuno 9i",     "9i",    new Color("63f2ff"), 109.0f, 20.5f,  0.2f, 7600.0f,  120.0f,  80.0f),
        new("callaway_9i",   "Callaway 9i",   "9i",    new Color("ffb347"), 111.0f, 19.4f, -0.3f, 7100.0f,  -80.0f,  81.0f),
        new("mizuno_8i",     "Mizuno 8i",     "8i",    new Color("7dff8a"), 118.0f, 18.0f,  0.4f, 6500.0f,  140.0f,  86.0f),
        new("driver_shaft_a","Driver Shaft A","Driver", new Color("ff6b9d"), 156.0f, 11.6f, -0.6f, 2500.0f, -220.0f, 107.0f),
        new("driver_shaft_b","Driver Shaft B","Driver", new Color("d2a8ff"), 159.0f, 10.9f,  0.8f, 2280.0f,  260.0f, 109.0f),
    };

    // Combined list — generic clubs first, then named equipment.
    public static IReadOnlyList<RangeSpikeShotPreset> Presets { get; } = BuildAll();

    private static List<RangeSpikeShotPreset> BuildAll()
    {
        var all = new List<RangeSpikeShotPreset>();
        all.AddRange(GenericPresets);
        all.AddRange(EquipmentPresets);
        return all;
    }

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
