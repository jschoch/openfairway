using Godot;

/// <summary>
/// Loads and saves ClosestToPinSettings to disk using Godot's ConfigFile.
/// </summary>
public static class ClosestToPinPersistenceService
{
    public const string SavePath = "user://closest_to_pin.cfg";
    private const int SaveVersion = 1;

    public static ClosestToPinSettings Load()
    {
        var settings = new ClosestToPinSettings();

        var config = new ConfigFile();
        if (config.Load(SavePath) != Error.Ok)
            return settings;

        settings.LastClub = GetString(config, "session", "last_club", settings.LastClub);
        settings.LastTargetYards = GetFloat(config, "session", "last_target_yards", settings.LastTargetYards);
        settings.LastTargetWasOverridden = GetBool(config, "session", "last_target_overridden", settings.LastTargetWasOverridden);
        settings.LastWinDistanceYards = GetFloat(config, "session", "last_win_distance_yards", settings.LastWinDistanceYards);

        // Per-club averages
        foreach (string club in RangeClubCatalog.Labels)
        {
            string key = RangeClubCatalog.ToFileTag(club);
            if (config.HasSectionKey("averages", key))
                settings.ClubAverages[club] = GetFloat(config, "averages", key, settings.GetAverage(club));
        }

        return settings;
    }

    public static void Save(ClosestToPinSettings settings)
    {
        if (settings == null) return;

        var config = new ConfigFile();
        config.SetValue("meta", "version", SaveVersion);

        config.SetValue("session", "last_club", settings.LastClub);
        config.SetValue("session", "last_target_yards", settings.LastTargetYards);
        config.SetValue("session", "last_target_overridden", settings.LastTargetWasOverridden);
        config.SetValue("session", "last_win_distance_yards", settings.LastWinDistanceYards);

        foreach (var pair in settings.ClubAverages)
        {
            string key = RangeClubCatalog.ToFileTag(pair.Key);
            config.SetValue("averages", key, pair.Value);
        }

        Error error = config.Save(SavePath);
        if (error != Error.Ok)
            PhysicsLogger.Error($"ClosestToPinPersistenceService: failed saving {SavePath} ({error})");
    }

    private static string GetString(ConfigFile c, string section, string key, string fallback) =>
        c.HasSectionKey(section, key) ? c.GetValue(section, key).AsString() : fallback;

    private static float GetFloat(ConfigFile c, string section, string key, float fallback) =>
        c.HasSectionKey(section, key) ? c.GetValue(section, key).AsSingle() : fallback;

    private static bool GetBool(ConfigFile c, string section, string key, bool fallback) =>
        c.HasSectionKey(section, key) ? c.GetValue(section, key).AsBool() : fallback;
}
