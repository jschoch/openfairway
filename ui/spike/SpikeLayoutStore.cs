using System.Collections.Generic;
using Godot;

// Persists the Range Spike dashboard layout across sessions:
//   - Window preset index and cap-to-preset toggle
//   - Visible tile IDs (the user's hide/show choices)
//   - All tile IDs ever seen (so hidden tiles stay hidden across orientation changes)
//   - Tile grid positions/spans, stored separately for landscape and portrait
//
// Save path: user://spike_layout.json
public static class SpikeLayoutStore
{
    private const string SavePath = "user://spike_layout.json";
    private const int CurrentVersion = 1;

    public sealed class Data
    {
        public int  WindowPresetIndex = 2;    // default 1728 × 972
        public int  LastPresetIndex   = 0;    // last selected club/preset in the dropdown
        // Tiles the user has explicitly set visible (hidden tiles are absent).
        public HashSet<string> VisibleTileIds = new();
        // All tile IDs ever presented to the user (across both orientations).
        // Lets us distinguish "new tile → default visible" from "user hid it → keep hidden".
        public HashSet<string> AllKnownTileIds = new();
        // Grid position/span per tile id: [col, row, colSpan, rowSpan]
        public Dictionary<string, int[]> LandscapeTiles = new();
        public Dictionary<string, int[]> PortraitTiles  = new();
    }

    public static void Save(Data data)
    {
        var visArr  = ToStringArray(data.VisibleTileIds);
        var knownArr = ToStringArray(data.AllKnownTileIds);

        var root = new Godot.Collections.Dictionary
        {
            ["version"]             = CurrentVersion,
            ["window_preset_index"] = data.WindowPresetIndex,
            ["last_preset_index"]   = data.LastPresetIndex,
            ["visible_tile_ids"]    = visArr,
            ["all_known_tile_ids"]  = knownArr,
            ["landscape_tiles"]     = SerializeTiles(data.LandscapeTiles),
            ["portrait_tiles"]      = SerializeTiles(data.PortraitTiles),
        };

        try
        {
            using var file = FileAccess.Open(SavePath, FileAccess.ModeFlags.Write);
            if (file == null)
            {
                GD.PrintErr($"[SpikeLayout] Cannot write {SavePath}: {FileAccess.GetOpenError()}");
                return;
            }
            file.StoreString(Json.Stringify(root, "\t"));
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"[SpikeLayout] Save failed: {ex.Message}");
        }
    }

    public static Data Load()
    {
        var data = new Data();
        if (!FileAccess.FileExists(SavePath))
            return data;

        try
        {
            using var file = FileAccess.Open(SavePath, FileAccess.ModeFlags.Read);
            if (file == null) return data;

            var json = new Json();
            if (json.Parse(file.GetAsText()) != Error.Ok) return data;

            var root = json.GetData().AsGodotDictionary();
            if (!root.TryGetValue("version", out var vv) || vv.AsInt32() != CurrentVersion)
                return data;

            if (root.TryGetValue("window_preset_index", out var pi))
                data.WindowPresetIndex = Mathf.Clamp(pi.AsInt32(), 0, 7);
            if (root.TryGetValue("last_preset_index", out var lpi))
                data.LastPresetIndex = Mathf.Max(0, lpi.AsInt32());
            if (root.TryGetValue("visible_tile_ids", out var vis))
                foreach (var v in vis.AsGodotArray()) data.VisibleTileIds.Add(v.AsString());
            if (root.TryGetValue("all_known_tile_ids", out var known))
                foreach (var v in known.AsGodotArray()) data.AllKnownTileIds.Add(v.AsString());
            if (root.TryGetValue("landscape_tiles", out var lt))
                data.LandscapeTiles = DeserializeTiles(lt.AsGodotArray());
            if (root.TryGetValue("portrait_tiles", out var pt))
                data.PortraitTiles = DeserializeTiles(pt.AsGodotArray());
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"[SpikeLayout] Load failed: {ex.Message}");
        }
        return data;
    }

    // ── Serialisation helpers ─────────────────────────────────────────────────

    private static Godot.Collections.Array ToStringArray(IEnumerable<string> ids)
    {
        var arr = new Godot.Collections.Array();
        foreach (var id in ids) arr.Add(id);
        return arr;
    }

    // Tiles serialized as an array of { id, col, row, cs, rs } objects.
    private static Godot.Collections.Array SerializeTiles(Dictionary<string, int[]> tiles)
    {
        var arr = new Godot.Collections.Array();
        foreach (var kv in tiles)
        {
            arr.Add(new Godot.Collections.Dictionary
            {
                ["id"]  = kv.Key,
                ["col"] = kv.Value[0],
                ["row"] = kv.Value[1],
                ["cs"]  = kv.Value[2],
                ["rs"]  = kv.Value[3],
            });
        }
        return arr;
    }

    private static Dictionary<string, int[]> DeserializeTiles(Godot.Collections.Array arr)
    {
        var result = new Dictionary<string, int[]>();
        foreach (var item in arr)
        {
            var d = item.AsGodotDictionary();
            if (!d.TryGetValue("id", out var idv)) continue;
            result[idv.AsString()] = new[]
            {
                d.TryGetValue("col", out var c)  ? c.AsInt32()  : 0,
                d.TryGetValue("row", out var r)  ? r.AsInt32()  : 0,
                d.TryGetValue("cs",  out var cs) ? cs.AsInt32() : 1,
                d.TryGetValue("rs",  out var rs) ? rs.AsInt32() : 1,
            };
        }
        return result;
    }
}
