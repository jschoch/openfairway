using System.Collections.Generic;

/// <summary>
/// Persisted state for Closest to the Pin mode: per-club average distances
/// and the last-used session config.
/// </summary>
public sealed class ClosestToPinSettings
{
    // Default average distances (yards) per club — used on first run or reset.
    public static readonly IReadOnlyDictionary<string, float> DefaultAverages =
        new Dictionary<string, float>
        {
            { "DRIVER", 250f },
            { "3W",     230f },
            { "5W",     215f },
            { "4H",     205f },
            { "3I",     195f },
            { "4I",     185f },
            { "5I",     175f },
            { "6I",     165f },
            { "7I",     155f },
            { "8I",     145f },
            { "9I",     135f },
            { "PW",     125f },
            { "GW",     110f },
            { "SW",      90f },
            { "LW",      70f },
        };

    /// <summary>
    /// Per-club average distances in yards. Pre-populated from DefaultAverages;
    /// updated when the user edits the target distance and confirms.
    /// </summary>
    public Dictionary<string, float> ClubAverages { get; set; } = new(DefaultAverages);

    // Last-used session values
    public string LastClub { get; set; } = RangeClubCatalog.DefaultClubLabel;
    public float LastTargetYards { get; set; } = 250f;
    public bool LastTargetWasOverridden { get; set; } = false;
    public float LastWinDistanceYards { get; set; } = 10f;

    /// <summary>Returns the stored average for a club, falling back to the default.</summary>
    public float GetAverage(string club)
    {
        if (ClubAverages.TryGetValue(club, out float v)) return v;
        if (DefaultAverages.TryGetValue(club, out float d)) return d;
        return 150f;
    }
}
