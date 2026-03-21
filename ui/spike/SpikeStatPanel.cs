using System.Collections.Generic;
using Godot;

// Stat comparison tile.
//
// Carry Distance shows three columns: LM (launch-monitor reported), OF (OpenFairway
// simulation), and LG (libgolf simulation).  LM carry is only present on TCP shots
// that include a CarryDistance field.
//
// All other computed stats (Peak Height, Offline) compare OF vs LG only.
//
// Launch-parameter stats (Ball Speed, VLA, HLA, Backspin, Sidespin) are inputs —
// the same value drives all engines — so they are shown once without engine labels.
public sealed partial class SpikeStatPanel : Control
{
    private static readonly Color ColOF  = new(0.95f, 0.28f, 0.28f, 1.0f); // OpenFairway — red
    private static readonly Color ColLG  = new(0.28f, 0.85f, 0.48f, 1.0f); // libgolf     — green
    private static readonly Color ColLM  = new(1.00f, 0.72f, 0.20f, 1.0f); // LM reported — orange
    private static readonly Color ColDim = new(0.45f, 0.55f, 0.65f, 0.6f);
    private static readonly Color ColVal = new(0.80f, 0.88f, 0.96f, 1.0f); // single-source value
    private static readonly Color ColBg  = new(0.04f, 0.07f, 0.11f, 1.0f);
    private static readonly Color ColGrid= new(0.12f, 0.18f, 0.26f, 0.6f);

    private List<RangeSpikeShotSet> _sets = new();
    private HashSet<string> _enabled = new(SpikeStatsDialog.DefaultEnabled);
    private bool _apexInFeet = true;
    private const float MetersToFeet = ShotSetup.FEET_PER_METER;

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

    public void SetApexUnit(bool inFeet)
    {
        _apexInFeet = inFeet;
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

        int statsPerGroup = CountEnabled();
        if (statsPerGroup == 0)
        {
            DrawNoData(full, "No stats selected — use the Stats button.");
            return;
        }

        float padding = 12f;
        float innerW  = full.Size.X - padding * 2f;
        float innerH  = full.Size.Y - padding * 2f - 16f;
        int totalRows = groups.Count * (statsPerGroup + 1) + groups.Count;
        float rowH    = Mathf.Max(20f, innerH / totalRows);
        float x0      = full.Position.X + padding;
        float y0      = full.Position.Y + padding;

        // Column layout: label | val-A | val-B | bars
        float labelW = innerW * 0.30f;
        float valW   = innerW * 0.13f;
        float barX   = x0 + labelW + valW * 2f;
        float barMaxW= innerW - labelW - valW * 2f;

        float yPos = y0;

        foreach (var g in groups)
        {
            // Group header
            DrawLabel(g.PresetName, new Vector2(x0, yPos), 15, new Color("c4d4e5"));
            yPos += rowH * 1.1f;

            // ── Carry: three-column LM / OF / LG ──────────────────────────────
            if (_enabled.Contains("carry"))
            {
                float maxC = Mathf.Max(Mathf.Max(Mathf.Max(g.CarryLm, g.CarryOF), g.CarryLG), 0.01f);
                DrawCarryRow(g, x0, yPos, rowH, labelW, valW, barX, barMaxW, maxC);
                yPos += rowH;
            }

            // ── Computed stats: OF vs LG ───────────────────────────────────────
            if (_enabled.Contains("height"))
            {
                float mx = Mathf.Max(Mathf.Max(g.HeightOF, g.HeightLG), 0.01f);
                string unit = _apexInFeet ? "ft" : "m";
                float ofVal = _apexInFeet ? g.HeightOF * MetersToFeet : g.HeightOF;
                float lgVal = _apexInFeet ? g.HeightLG * MetersToFeet : g.HeightLG;
                DrawDualRow($"Peak Height ({unit})",
                    g.HasOF ? $"{ofVal:F1} {unit}" : null, g.HeightOF / mx, ColOF,
                    g.HasLG  ? $"{lgVal:F1} {unit}"  : null, g.HeightLG  / mx, ColLG,
                    x0, yPos, rowH, labelW, valW, barX, barMaxW);
                yPos += rowH;
            }

            if (_enabled.Contains("offline"))
            {
                float mx = Mathf.Max(Mathf.Max(Mathf.Abs(g.OfflineOF), Mathf.Abs(g.OfflineLG)), 0.01f);
                DrawDualRow("Offline",
                    g.HasOF ? FormatOff(g.OfflineOF) : null, Mathf.Abs(g.OfflineOF) / mx, ColOF,
                    g.HasLG  ? FormatOff(g.OfflineLG)  : null, Mathf.Abs(g.OfflineLG)  / mx, ColLG,
                    x0, yPos, rowH, labelW, valW, barX, barMaxW);
                yPos += rowH;
            }

            // ── Single-source launch params ────────────────────────────────────
            if (_enabled.Contains("speed") && g.Speed > 0)
            {
                DrawSingleRow("Ball Speed", $"{g.Speed:F0} mph",
                    x0, yPos, rowH, labelW);
                yPos += rowH;
            }
            if (_enabled.Contains("smash") && g.SmashFactor > 0)
            {
                DrawSingleRow("Smash Factor", $"{g.SmashFactor:F2}",
                    x0, yPos, rowH, labelW);
                yPos += rowH;
            }
            if (_enabled.Contains("vla") && g.Vla > 0)
            {
                DrawSingleRow("VLA", $"{g.Vla:F1}°",
                    x0, yPos, rowH, labelW);
                yPos += rowH;
            }
            if (_enabled.Contains("hla"))
            {
                DrawSingleRow("HLA", $"{g.Hla:+0.0;-0.0;0.0}°",
                    x0, yPos, rowH, labelW);
                yPos += rowH;
            }
            if (_enabled.Contains("backspin") && g.Backspin > 0)
            {
                DrawSingleRow("Backspin", $"{g.Backspin:F0} rpm",
                    x0, yPos, rowH, labelW);
                yPos += rowH;
            }
            if (_enabled.Contains("sidespin"))
            {
                DrawSingleRow("Sidespin", $"{g.Sidespin:+0;-0;0} rpm",
                    x0, yPos, rowH, labelW);
                yPos += rowH;
            }

            DrawLine(new Vector2(x0, yPos + rowH * 0.25f),
                     new Vector2(full.End.X - padding, yPos + rowH * 0.25f), ColGrid, 1f);
            yPos += rowH * 0.65f;
        }

        // Legend
        float ly = full.End.Y - padding - 12f;
        DrawRect(new Rect2(x0,          ly + 1f, 10f, 8f), ColLM);
        DrawLabel("Launch Monitor",  new Vector2(x0 + 14f,   ly), 10, ColLM);
        DrawRect(new Rect2(x0 + 114f, ly + 1f, 10f, 8f), ColOF);
        DrawLabel("OpenFairway",     new Vector2(x0 + 128f,  ly), 10, ColOF);
        DrawRect(new Rect2(x0 + 224f, ly + 1f, 10f, 8f), ColLG);
        DrawLabel("libGolf",         new Vector2(x0 + 238f,  ly), 10, ColLG);
        DrawLabel("— single-source: input params", new Vector2(x0 + 300f, ly), 9, ColDim);
    }

    // ── Row renderers ──────────────────────────────────────────────────────────

    private void DrawCarryRow(StatGroup g,
        float x0, float yPos, float rowH,
        float labelW, float valW, float barX, float barMaxW, float maxC)
    {
        float barH = Mathf.Max(4f, rowH * 0.22f);
        float step = barH + 2f;
        float barTop = yPos + rowH * 0.35f;

        DrawLabel("Carry", new Vector2(x0, yPos + rowH * 0.05f), 13, ColDim);

        int lane = 0;
        if (g.CarryLm > 0)
        {
            DrawLabel($"{g.CarryLm:F0} yd", new Vector2(x0 + labelW, yPos + rowH * 0.05f), 13, ColLM);
            DrawRect(new Rect2(barX, barTop + lane * step, barMaxW * g.CarryLm / maxC, barH), ColLM with { A = 0.8f });
            lane++;
        }
        if (g.HasOF)
        {
            float ty = lane == 0 ? yPos + rowH * 0.05f : yPos + rowH * 0.05f + lane * (rowH * 0.38f);
            DrawLabel($"{g.CarryOF / 0.9144f:F0} yd", new Vector2(x0 + labelW + valW * 0, ty), 13, ColOF);
            DrawRect(new Rect2(barX, barTop + lane * step, barMaxW * (g.CarryOF / 0.9144f) / (maxC > 0.01f ? maxC : 1f), barH), ColOF with { A = 0.8f });
            lane++;
        }
        if (g.HasLG)
        {
            float ty = lane == 0 ? yPos + rowH * 0.05f : yPos + rowH * 0.05f + lane * (rowH * 0.38f);
            DrawLabel($"{g.CarryLG / 0.9144f:F0} yd", new Vector2(x0 + labelW + valW * 1, ty), 13, ColLG);
            DrawRect(new Rect2(barX, barTop + lane * step, barMaxW * (g.CarryLG / 0.9144f) / (maxC > 0.01f ? maxC : 1f), barH), ColLG with { A = 0.8f });
        }
    }

    private void DrawDualRow(
        string label,
        string valA, float fracA, Color colA,
        string valB, float fracB, Color colB,
        float x0, float yPos, float rowH,
        float labelW, float valW, float barX, float barMaxW)
    {
        float barH  = Mathf.Max(4f, rowH * 0.25f);
        float barTop= yPos + rowH * 0.45f;

        DrawLabel(label, new Vector2(x0, yPos + rowH * 0.05f), 13, ColDim);
        if (valA != null)
        {
            DrawLabel(valA, new Vector2(x0 + labelW, yPos + rowH * 0.05f), 13, colA);
            DrawRect(new Rect2(barX, barTop - barH - 1f, barMaxW * Mathf.Clamp(fracA, 0f, 1f), barH), colA with { A = 0.8f });
        }
        if (valB != null)
        {
            float ty = valA != null ? yPos + rowH * 0.48f : yPos + rowH * 0.05f;
            DrawLabel(valB, new Vector2(x0 + labelW + valW, ty), 13, colB);
            DrawRect(new Rect2(barX, barTop + 1f, barMaxW * Mathf.Clamp(fracB, 0f, 1f), barH), colB with { A = 0.8f });
        }
    }

    private void DrawSingleRow(string label, string value,
        float x0, float yPos, float rowH, float labelW)
    {
        DrawLabel(label, new Vector2(x0, yPos + rowH * 0.05f), 13, ColDim);
        DrawLabel(value, new Vector2(x0 + labelW, yPos + rowH * 0.05f), 13, ColVal);
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

    private static string FormatOff(float m)
    {
        if (Mathf.Abs(m) < 0.05f) return "on line";
        return $"{Mathf.Abs(m) / 0.9144f:F1} yd {(m < 0 ? "L" : "R")}";
    }

    // ── Grouping ───────────────────────────────────────────────────────────────

    private int CountEnabled()
    {
        int n = 0;
        string[] all = { "carry", "height", "offline", "speed", "smash", "vla", "hla", "backspin", "sidespin" };
        foreach (string s in all) if (_enabled.Contains(s)) n++;
        return n;
    }

    private sealed class StatGroup
    {
        public string PresetName;
        // Carry: three sources
        public float CarryLm;   // LM reported (yards already)
        public float CarryOF;   // metres
        public float CarryLG;   // metres
        // Other computed
        public float HeightOF, HeightLG;
        public float OfflineOF, OfflineLG;
        // Launch params (single-source)
        public float Speed, Vla, Hla, Backspin, Sidespin;
        public float SmashFactor; // 0 = not available
        public bool HasOF, HasLG;
    }

    private List<StatGroup> BuildGroups()
    {
        var byPreset = new Dictionary<string, StatGroup>();

        foreach (var set in _sets)
        {
            if (set.Traces.Count == 0) continue;
            bool isLG = set.Label.StartsWith("libgolf");
            string key = set.Preset?.DisplayName ?? TrimLiveLabel(set.Label);

            if (!byPreset.TryGetValue(key, out var g))
            {
                g = new StatGroup { PresetName = key };
                byPreset[key] = g;
            }

            float carry = 0, height = 0, offline = 0;
            float lmCarry = 0, lmCount = 0;
            float speed = 0, vla = 0, hla = 0, bs = 0, ss = 0;
            float smash = 0; int smashCount = 0;
            int n = 0;
            foreach (var t in set.Traces)
            {
                carry   += t.LandingPoint.X;
                offline += t.LandingPoint.Z;
                speed   += t.SpeedMph;
                vla     += t.LaunchAngleDeg;
                hla     += t.DirectionDeg;
                bs      += t.BackspinRpm;
                ss      += t.SidespinRpm;
                if (t.SmashFactor > 0) { smash += t.SmashFactor; smashCount++; }
                if (t.LmCarryDistanceYd > 0) { lmCarry += t.LmCarryDistanceYd; lmCount++; }
                float peak = 0f;
                foreach (var pt in t.Points) peak = Mathf.Max(peak, pt.Y);
                height += peak;
                n++;
            }
            if (n == 0) continue;
            carry /= n; height /= n; offline /= n;
            speed /= n; vla /= n; hla /= n; bs /= n; ss /= n;
            if (lmCount > 0) lmCarry /= lmCount;

            if (isLG)
            {
                Avg(ref g.CarryLG, carry, g.HasLG);
                Avg(ref g.HeightLG, height, g.HasLG);
                Avg(ref g.OfflineLG, offline, g.HasLG);
                g.HasLG = true;
            }
            else
            {
                Avg(ref g.CarryOF, carry, g.HasOF);
                Avg(ref g.HeightOF, height, g.HasOF);
                Avg(ref g.OfflineOF, offline, g.HasOF);
                if (lmCarry > 0) Avg(ref g.CarryLm, lmCarry, g.CarryLm > 0);
                // Launch params: always overwrite with latest (they're the same inputs)
                if (speed > 0) { g.Speed = speed; g.Vla = vla; g.Hla = hla; g.Backspin = bs; g.Sidespin = ss; }
                if (smashCount > 0) g.SmashFactor = smash / smashCount;
                g.HasOF = true;
            }
        }

        var result = new List<StatGroup>(byPreset.Values);
        result.Sort((a, b) => string.Compare(a.PresetName, b.PresetName, System.StringComparison.Ordinal));
        return result;
    }

    private static void Avg(ref float field, float value, bool hasExisting)
    {
        field = hasExisting ? (field + value) / 2f : value;
    }

    private static string TrimLiveLabel(string label)
    {
        const string lg = "libgolf — ";
        const string live = " Live";
        string s = label.StartsWith(lg) ? label[lg.Length..] : label;
        return s.EndsWith(live) ? s[..^live.Length] : s;
    }
}
