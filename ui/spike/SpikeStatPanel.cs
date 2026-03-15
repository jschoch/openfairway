using System.Collections.Generic;
using Godot;

// Stat comparison tile: shows per-preset carry/height/offline for each physics engine.
// OpenFairway sets are drawn in red; libgolf reference sets in green.
// A set is treated as a libgolf set when its Label starts with "libgolf".
public sealed partial class SpikeStatPanel : Control
{
    private static readonly Color ColA   = new(0.95f, 0.28f, 0.28f, 1.0f); // OpenFairway — red
    private static readonly Color ColB   = new(0.28f, 0.85f, 0.48f, 1.0f); // libgolf     — green
    private static readonly Color ColDim = new(0.45f, 0.55f, 0.65f, 0.6f);
    private static readonly Color ColBg  = new(0.04f, 0.07f, 0.11f, 1.0f);
    private static readonly Color ColGrid= new(0.12f, 0.18f, 0.26f, 0.6f);

    private List<RangeSpikeShotSet> _sets = new();

    public void SetShotSets(List<RangeSpikeShotSet> sets)
    {
        _sets = sets ?? new List<RangeSpikeShotSet>();
        QueueRedraw();
    }

    public override void _Draw()
    {
        Rect2 full = new(Vector2.Zero, Size);
        DrawRect(full, ColBg);

        if (_sets.Count == 0)
        {
            DrawNoData(full, "Add a shot set to see stats.");
            return;
        }

        // Group sets by preset display name.
        var groups = BuildGroups();
        if (groups.Count == 0)
        {
            DrawNoData(full, "No stat data available.");
            return;
        }

        float padding   = 12f;
        float innerW    = full.Size.X - padding * 2f;
        float innerH    = full.Size.Y - padding * 2f;
        float rowH      = Mathf.Max(18f, innerH / (groups.Count * 4 + 1)); // 4 stat rows per group + header
        float x0        = full.Position.X + padding;
        float y0        = full.Position.Y + padding;

        float barMaxW   = innerW * 0.35f;
        float labelColW = innerW * 0.38f;
        float valColW   = innerW * 0.14f;
        float barColX   = x0 + labelColW + valColW;

        float yPos = y0;

        foreach (var group in groups)
        {
            // Group header
            DrawLabel(group.PresetName, new Vector2(x0, yPos), 13, new Color("c4d4e5"));
            DrawLabel($"LA {group.LaunchAngle:F1}°  BS {group.Backspin:F0}rpm  SS {group.Sidespin:+0;-0}rpm",
                new Vector2(x0 + innerW * 0.38f, yPos), 11, ColDim);
            yPos += rowH * 1.2f;

            float maxCarry = Mathf.Max(Mathf.Max(group.CarryA, group.CarryB), 0.01f);
            float maxHeight= Mathf.Max(Mathf.Max(group.HeightA, group.HeightB), 0.01f);
            float maxOff   = Mathf.Max(Mathf.Max(Mathf.Abs(group.OfflineA), Mathf.Abs(group.OfflineB)), 0.01f);

            DrawStatRow("Carry",
                $"{group.CarryA / 0.9144f:F0} yd", group.CarryA / maxCarry, group.HasA,
                $"{group.CarryB / 0.9144f:F0} yd", group.CarryB / maxCarry, group.HasB,
                x0, yPos, rowH, labelColW, valColW, barColX, barMaxW);
            yPos += rowH;

            DrawStatRow("Peak Height",
                $"{group.HeightA:F1} m", group.HeightA / maxHeight, group.HasA,
                $"{group.HeightB:F1} m", group.HeightB / maxHeight, group.HasB,
                x0, yPos, rowH, labelColW, valColW, barColX, barMaxW);
            yPos += rowH;

            string offA = FormatOffline(group.OfflineA);
            string offB = FormatOffline(group.OfflineB);
            float offFracA = group.OfflineA == 0f ? 0f : Mathf.Abs(group.OfflineA) / maxOff;
            float offFracB = group.OfflineB == 0f ? 0f : Mathf.Abs(group.OfflineB) / maxOff;
            DrawStatRow("Offline",
                offA, offFracA, group.HasA,
                offB, offFracB, group.HasB,
                x0, yPos, rowH, labelColW, valColW, barColX, barMaxW);
            yPos += rowH;

            // Divider between groups
            DrawLine(new Vector2(x0, yPos + rowH * 0.3f),
                     new Vector2(full.End.X - padding, yPos + rowH * 0.3f),
                     ColGrid, 1f);
            yPos += rowH * 0.8f;
        }

        // Legend
        float legY = full.End.Y - padding - 12f;
        DrawRect(new Rect2(x0, legY + 1f, 12f, 8f), ColA);
        DrawLabel("OpenFairway", new Vector2(x0 + 16f, legY), 10, ColA);
        DrawRect(new Rect2(x0 + 110f, legY + 1f, 12f, 8f), ColB);
        DrawLabel("libgolf", new Vector2(x0 + 126f, legY), 10, ColB);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private void DrawStatRow(
        string label,
        string valA, float fracA, bool hasA,
        string valB, float fracB, bool hasB,
        float x0, float yPos, float rowH,
        float labelColW, float valColW, float barColX, float barMaxW)
    {
        float textY = yPos + rowH * 0.1f;
        float barY  = yPos + rowH * 0.55f;
        float barH  = Mathf.Max(4f, rowH * 0.28f);
        float gap   = barH + 2f;

        DrawLabel(label, new Vector2(x0, textY), 11, ColDim);

        // Engine A (red) — upper bar
        if (hasA)
        {
            DrawLabel(valA, new Vector2(x0 + labelColW, textY), 11, ColA);
            DrawRect(new Rect2(barColX, barY - gap, barMaxW * Mathf.Clamp(fracA, 0f, 1f), barH), ColA with { A = 0.8f });
        }

        // Engine B (green) — lower bar
        if (hasB)
        {
            float bTextY = hasA ? textY + rowH * 0.45f : textY;
            DrawLabel(valB, new Vector2(x0 + labelColW + valColW, bTextY), 11, ColB);
            DrawRect(new Rect2(barColX, barY, barMaxW * Mathf.Clamp(fracB, 0f, 1f), barH), ColB with { A = 0.8f });
        }
    }

    private void DrawLabel(string text, Vector2 pos, int size, Color color)
    {
        DrawString(ThemeDB.FallbackFont, pos + new Vector2(0, size), text,
            HorizontalAlignment.Left, -1, size, color);
    }

    private void DrawNoData(Rect2 rect, string msg)
    {
        DrawString(ThemeDB.FallbackFont,
            rect.GetCenter() + new Vector2(-60, 6),
            msg, HorizontalAlignment.Left, -1, 12, ColDim);
    }

    private static string FormatOffline(float metres)
    {
        if (Mathf.Abs(metres) < 0.05f) return "on line";
        float yd = Mathf.Abs(metres) / 0.9144f;
        return $"{yd:F1} yd {(metres < 0 ? "L" : "R")}";
    }

    // ── Group computation ──────────────────────────────────────────────────────

    private sealed class StatGroup
    {
        public string PresetName;
        public float LaunchAngle, Backspin, Sidespin;
        public float CarryA, HeightA, OfflineA;
        public float CarryB, HeightB, OfflineB;
        public bool HasA, HasB;
    }

    private List<StatGroup> BuildGroups()
    {
        // Collect per-preset data keyed by preset display name.
        var byPreset = new Dictionary<string, StatGroup>();

        foreach (var set in _sets)
        {
            if (set.Traces.Count == 0) continue;
            bool isLibgolf = set.Label.StartsWith("libgolf");
            string key = set.Preset?.DisplayName ?? set.Label;

            if (!byPreset.TryGetValue(key, out var g))
            {
                g = new StatGroup
                {
                    PresetName = key,
                    LaunchAngle = set.Preset?.LaunchAngleDeg ?? 0f,
                    Backspin    = set.Preset?.BackspinRpm   ?? 0f,
                    Sidespin    = set.Preset?.SidespinRpm   ?? 0f,
                };
                byPreset[key] = g;
            }

            float carry  = 0f, height = 0f, offline = 0f;
            int n = 0;
            foreach (var trace in set.Traces)
            {
                carry   += trace.LandingPoint.X;
                offline += trace.LandingPoint.Z;
                float peak = 0f;
                foreach (var pt in trace.Points)
                    peak = Mathf.Max(peak, pt.Y);
                height += peak;
                n++;
            }
            if (n == 0) continue;
            carry   /= n;
            height  /= n;
            offline /= n;

            if (isLibgolf)
            {
                g.CarryB   = carry;
                g.HeightB  = height;
                g.OfflineB = offline;
                g.HasB     = true;
            }
            else
            {
                // Average multiple OpenFairway sets for the same preset.
                if (g.HasA)
                {
                    g.CarryA   = (g.CarryA   + carry)   / 2f;
                    g.HeightA  = (g.HeightA  + height)  / 2f;
                    g.OfflineA = (g.OfflineA + offline)  / 2f;
                }
                else
                {
                    g.CarryA   = carry;
                    g.HeightA  = height;
                    g.OfflineA = offline;
                    g.HasA     = true;
                }
            }
        }

        var result = new List<StatGroup>(byPreset.Values);
        result.Sort((a, b) => string.Compare(a.PresetName, b.PresetName, System.StringComparison.Ordinal));
        return result;
    }
}
