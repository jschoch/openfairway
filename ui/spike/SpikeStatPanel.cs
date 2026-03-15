using System.Collections.Generic;
using Godot;

// Stat comparison tile: shows per-preset metrics for each physics engine.
// OpenFairway sets are drawn in red; libgolf reference sets in green.
// A set is treated as a libgolf set when its Label starts with "libgolf".
// Call SetEnabledStats() to control which rows appear.
public sealed partial class SpikeStatPanel : Control
{
    private static readonly Color ColA   = new(0.95f, 0.28f, 0.28f, 1.0f); // OpenFairway — red
    private static readonly Color ColB   = new(0.28f, 0.85f, 0.48f, 1.0f); // libgolf     — green
    private static readonly Color ColDim = new(0.45f, 0.55f, 0.65f, 0.6f);
    private static readonly Color ColBg  = new(0.04f, 0.07f, 0.11f, 1.0f);
    private static readonly Color ColGrid= new(0.12f, 0.18f, 0.26f, 0.6f);

    private List<RangeSpikeShotSet> _sets = new();
    private HashSet<string> _enabled = new(SpikeStatsDialog.DefaultEnabled);

    public void SetShotSets(List<RangeSpikeShotSet> sets)
    {
        _sets = sets ?? new List<RangeSpikeShotSet>();
        QueueRedraw();
    }

    public void SetEnabledStats(IEnumerable<string> ids)
    {
        _enabled = new HashSet<string>(ids);
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

        var groups = BuildGroups();
        if (groups.Count == 0)
        {
            DrawNoData(full, "No stat data available.");
            return;
        }

        // Count enabled stats to compute row heights.
        int statsPerGroup = CountEnabled();
        if (statsPerGroup == 0)
        {
            DrawNoData(full, "No stats selected. Use the Stats button.");
            return;
        }

        float padding  = 12f;
        float innerW   = full.Size.X - padding * 2f;
        float innerH   = full.Size.Y - padding * 2f - 16f; // reserve bottom for legend
        int totalRows  = groups.Count * (statsPerGroup + 1) + groups.Count; // stats + header + divider gap
        float rowH     = Mathf.Max(16f, innerH / totalRows);
        float x0       = full.Position.X + padding;
        float y0       = full.Position.Y + padding;

        float labelColW = innerW * 0.36f;
        float valColW   = innerW * 0.14f;
        float barColX   = x0 + labelColW + valColW * 2f;
        float barMaxW   = innerW - labelColW - valColW * 2f;

        float yPos = y0;

        foreach (var g in groups)
        {
            // Group header
            DrawLabel(g.PresetName, new Vector2(x0, yPos), 13, new Color("c4d4e5"));
            string spinInfo = $"BS {g.Backspin:F0}  SS {g.Sidespin:+0;-0} rpm";
            DrawLabel(spinInfo, new Vector2(x0 + innerW * 0.5f, yPos), 10, ColDim);
            yPos += rowH * 1.1f;

            // ── Trajectory-derived stats ───────────────────────────────────────
            if (_enabled.Contains("carry"))
            {
                float mx = Mathf.Max(Mathf.Max(g.CarryA, g.CarryB), 0.01f);
                DrawStatRow("Carry",
                    g.HasA ? $"{g.CarryA / 0.9144f:F0} yd" : null, g.CarryA / mx,
                    g.HasB ? $"{g.CarryB / 0.9144f:F0} yd" : null, g.CarryB / mx,
                    x0, yPos, rowH, labelColW, valColW, barColX, barMaxW);
                yPos += rowH;
            }

            if (_enabled.Contains("height"))
            {
                float mx = Mathf.Max(Mathf.Max(g.HeightA, g.HeightB), 0.01f);
                DrawStatRow("Peak Height",
                    g.HasA ? $"{g.HeightA:F1} m" : null, g.HeightA / mx,
                    g.HasB ? $"{g.HeightB:F1} m" : null, g.HeightB / mx,
                    x0, yPos, rowH, labelColW, valColW, barColX, barMaxW);
                yPos += rowH;
            }

            if (_enabled.Contains("offline"))
            {
                float mx = Mathf.Max(Mathf.Max(Mathf.Abs(g.OfflineA), Mathf.Abs(g.OfflineB)), 0.01f);
                DrawStatRow("Offline",
                    g.HasA ? FormatOffline(g.OfflineA) : null, Mathf.Abs(g.OfflineA) / mx,
                    g.HasB ? FormatOffline(g.OfflineB) : null, Mathf.Abs(g.OfflineB) / mx,
                    x0, yPos, rowH, labelColW, valColW, barColX, barMaxW);
                yPos += rowH;
            }

            // ── Launch-param stats (from trace fields) ─────────────────────────
            if (_enabled.Contains("speed"))
            {
                float mx = Mathf.Max(Mathf.Max(g.SpeedA, g.SpeedB), 0.01f);
                DrawStatRow("Ball Speed",
                    g.HasA && g.SpeedA > 0 ? $"{g.SpeedA:F0} mph" : null, g.SpeedA / mx,
                    g.HasB && g.SpeedB > 0 ? $"{g.SpeedB:F0} mph" : null, g.SpeedB / mx,
                    x0, yPos, rowH, labelColW, valColW, barColX, barMaxW);
                yPos += rowH;
            }

            if (_enabled.Contains("vla"))
            {
                float mx = Mathf.Max(Mathf.Max(g.VlaA, g.VlaB), 0.01f);
                DrawStatRow("VLA",
                    g.HasA && g.VlaA > 0 ? $"{g.VlaA:F1}°" : null, g.VlaA / mx,
                    g.HasB && g.VlaB > 0 ? $"{g.VlaB:F1}°" : null, g.VlaB / mx,
                    x0, yPos, rowH, labelColW, valColW, barColX, barMaxW);
                yPos += rowH;
            }

            if (_enabled.Contains("hla"))
            {
                float mxAbs = Mathf.Max(Mathf.Max(Mathf.Abs(g.HlaA), Mathf.Abs(g.HlaB)), 0.01f);
                DrawStatRow("HLA",
                    g.HasA && (g.HlaA != 0 || g.SpeedA > 0) ? $"{g.HlaA:+0.0;-0.0}°" : null,
                    Mathf.Abs(g.HlaA) / mxAbs,
                    g.HasB && (g.HlaB != 0 || g.SpeedB > 0) ? $"{g.HlaB:+0.0;-0.0}°" : null,
                    Mathf.Abs(g.HlaB) / mxAbs,
                    x0, yPos, rowH, labelColW, valColW, barColX, barMaxW);
                yPos += rowH;
            }

            if (_enabled.Contains("backspin"))
            {
                float mx = Mathf.Max(Mathf.Max(g.BackspinA, g.BackspinB), 0.01f);
                DrawStatRow("Backspin",
                    g.HasA && g.BackspinA > 0 ? $"{g.BackspinA:F0} rpm" : null, g.BackspinA / mx,
                    g.HasB && g.BackspinB > 0 ? $"{g.BackspinB:F0} rpm" : null, g.BackspinB / mx,
                    x0, yPos, rowH, labelColW, valColW, barColX, barMaxW);
                yPos += rowH;
            }

            if (_enabled.Contains("sidespin"))
            {
                float mxAbs = Mathf.Max(Mathf.Max(Mathf.Abs(g.SidespinA), Mathf.Abs(g.SidespinB)), 0.01f);
                DrawStatRow("Sidespin",
                    g.HasA && (g.SidespinA != 0 || g.SpeedA > 0) ? $"{g.SidespinA:+0;-0} rpm" : null,
                    Mathf.Abs(g.SidespinA) / mxAbs,
                    g.HasB && (g.SidespinB != 0 || g.SpeedB > 0) ? $"{g.SidespinB:+0;-0} rpm" : null,
                    Mathf.Abs(g.SidespinB) / mxAbs,
                    x0, yPos, rowH, labelColW, valColW, barColX, barMaxW);
                yPos += rowH;
            }

            // Divider
            DrawLine(new Vector2(x0, yPos + rowH * 0.3f),
                     new Vector2(full.End.X - padding, yPos + rowH * 0.3f), ColGrid, 1f);
            yPos += rowH * 0.7f;
        }

        // Legend
        float legY = full.End.Y - padding - 12f;
        DrawRect(new Rect2(x0, legY + 1f, 12f, 8f), ColA);
        DrawLabel("OpenFairway", new Vector2(x0 + 16f, legY), 10, ColA);
        DrawRect(new Rect2(x0 + 110f, legY + 1f, 12f, 8f), ColB);
        DrawLabel("libgolf", new Vector2(x0 + 126f, legY), 10, ColB);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private int CountEnabled()
    {
        int n = 0;
        string[] all = { "carry", "height", "offline", "speed", "vla", "hla", "backspin", "sidespin" };
        foreach (string s in all) if (_enabled.Contains(s)) n++;
        return n;
    }

    private void DrawStatRow(
        string label,
        string valA, float fracA,
        string valB, float fracB,
        float x0, float yPos, float rowH,
        float labelColW, float valColW, float barColX, float barMaxW)
    {
        float textY = yPos + rowH * 0.05f;
        float barH  = Mathf.Max(4f, rowH * 0.28f);
        float barY  = yPos + rowH * 0.55f;
        float gap   = barH + 2f;

        DrawLabel(label, new Vector2(x0, textY), 11, ColDim);

        if (valA != null)
        {
            DrawLabel(valA, new Vector2(x0 + labelColW, textY), 11, ColA);
            DrawRect(new Rect2(barColX, barY - gap, barMaxW * Mathf.Clamp(fracA, 0f, 1f), barH),
                ColA with { A = 0.8f });
        }

        if (valB != null)
        {
            float bTextY = valA != null ? textY + rowH * 0.45f : textY;
            DrawLabel(valB, new Vector2(x0 + labelColW + valColW, bTextY), 11, ColB);
            DrawRect(new Rect2(barColX, barY, barMaxW * Mathf.Clamp(fracB, 0f, 1f), barH),
                ColB with { A = 0.8f });
        }
    }

    private void DrawLabel(string text, Vector2 pos, int size, Color color)
    {
        DrawString(ThemeDB.FallbackFont, pos + new Vector2(0, size), text,
            HorizontalAlignment.Left, -1, size, color);
    }

    private void DrawNoData(Rect2 rect, string msg)
    {
        DrawString(ThemeDB.FallbackFont, rect.GetCenter() + new Vector2(-80, 6),
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
        public float Backspin, Sidespin;
        // Trajectory-derived
        public float CarryA, HeightA, OfflineA;
        public float CarryB, HeightB, OfflineB;
        // Launch params
        public float SpeedA, VlaA, HlaA, BackspinA, SidespinA;
        public float SpeedB, VlaB, HlaB, BackspinB, SidespinB;
        public bool HasA, HasB;
    }

    private List<StatGroup> BuildGroups()
    {
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
                    Backspin   = set.Preset?.BackspinRpm ?? 0f,
                    Sidespin   = set.Preset?.SidespinRpm ?? 0f,
                };
                byPreset[key] = g;
            }

            float carry = 0, height = 0, offline = 0;
            float speed = 0, vla = 0, hla = 0, bs = 0, ss = 0;
            int n = 0;
            foreach (var trace in set.Traces)
            {
                carry   += trace.LandingPoint.X;
                offline += trace.LandingPoint.Z;
                speed   += trace.SpeedMph;
                vla     += trace.LaunchAngleDeg;
                hla     += trace.DirectionDeg;
                bs      += trace.BackspinRpm;
                ss      += trace.SidespinRpm;
                float peak = 0f;
                foreach (var pt in trace.Points) peak = Mathf.Max(peak, pt.Y);
                height += peak;
                n++;
            }
            if (n == 0) continue;
            carry /= n; height /= n; offline /= n;
            speed /= n; vla /= n; hla /= n; bs /= n; ss /= n;

            if (isLibgolf)
            {
                g.CarryB = carry; g.HeightB = height; g.OfflineB = offline;
                g.SpeedB = speed; g.VlaB = vla; g.HlaB = hla;
                g.BackspinB = bs; g.SidespinB = ss;
                g.HasB = true;
            }
            else
            {
                if (g.HasA)
                {
                    g.CarryA = (g.CarryA + carry) / 2f;
                    g.HeightA = (g.HeightA + height) / 2f;
                    g.OfflineA = (g.OfflineA + offline) / 2f;
                    g.SpeedA = (g.SpeedA + speed) / 2f;
                    g.VlaA = (g.VlaA + vla) / 2f;
                    g.HlaA = (g.HlaA + hla) / 2f;
                    g.BackspinA = (g.BackspinA + bs) / 2f;
                    g.SidespinA = (g.SidespinA + ss) / 2f;
                }
                else
                {
                    g.CarryA = carry; g.HeightA = height; g.OfflineA = offline;
                    g.SpeedA = speed; g.VlaA = vla; g.HlaA = hla;
                    g.BackspinA = bs; g.SidespinA = ss;
                    g.HasA = true;
                }
            }
        }

        var result = new List<StatGroup>(byPreset.Values);
        result.Sort((a, b) => string.Compare(a.PresetName, b.PresetName,
            System.StringComparison.Ordinal));
        return result;
    }
}
