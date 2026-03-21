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

    private static readonly Color ColFocus = new(1.00f, 1.00f, 1.00f, 0.85f); // focused-shot tick

    private List<RangeSpikeShotSet> _sets = new();
    private HashSet<string> _enabled = new(SpikeStatsDialog.DefaultEnabled);
    private bool _apexInFeet = true;
    private const float MetersToFeet = ShotSetup.FEET_PER_METER;
    // Focused / latest individual shot shown as a tick overlay on bars.
    private RangeSpikeShotTrace _focusedTrace;
    private string _focusedGroupKey = "";

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

    /// <summary>
    /// Sets the focused/latest individual shot for bar tick overlay.
    /// groupKey must match the preset DisplayName used as the BuildGroups() key.
    /// Pass null trace to clear.
    /// </summary>
    public void SetFocusedTrace(RangeSpikeShotTrace trace, string groupKey)
    {
        _focusedTrace    = trace;
        _focusedGroupKey = groupKey ?? "";
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
            bool hasFocus = _focusedTrace != null && g.PresetName == _focusedGroupKey;

            // Group header
            DrawLabel(g.PresetName, new Vector2(x0, yPos), 15, new Color("c4d4e5"));
            yPos += rowH * 1.1f;

            // ── Carry: three-column LM / OF / LG ──────────────────────────────
            if (_enabled.Contains("carry"))
            {
                float maxC = Mathf.Max(Mathf.Max(Mathf.Max(g.CarryLm, g.CarryOF), g.CarryLG), 0.01f);
                float focFrac = hasFocus ? (_focusedTrace.LandingPoint.X / 0.9144f) / maxC : -1f;
                DrawCarryRow(g, x0, yPos, rowH, labelW, valW, barX, barMaxW, maxC, focFrac);
                yPos += rowH;
            }

            // ── Computed stats: OF vs LG ───────────────────────────────────────
            if (_enabled.Contains("height"))
            {
                float mx = Mathf.Max(Mathf.Max(g.HeightOF, g.HeightLG), 0.01f);
                string unit = _apexInFeet ? "ft" : "m";
                float ofVal = _apexInFeet ? g.HeightOF * MetersToFeet : g.HeightOF;
                float lgVal = _apexInFeet ? g.HeightLG * MetersToFeet : g.HeightLG;
                string ofSd = SdSuffix(g.StdDevHeightOF * (_apexInFeet ? MetersToFeet : 1f), g.TraceCountOF, "F0");
                float focPeak = -1f;
                if (hasFocus && _focusedTrace.Points.Count > 0)
                {
                    focPeak = 0f;
                    foreach (var pt in _focusedTrace.Points) if (pt.Y > focPeak) focPeak = pt.Y;
                }
                float focFrac = focPeak >= 0f ? focPeak / mx : -1f;
                DrawDualRow($"Peak Height ({unit})",
                    g.HasOF ? $"{ofVal:F1}{ofSd} {unit}" : null, g.HeightOF / mx, ColOF,
                    g.HasLG  ? $"{lgVal:F1} {unit}"        : null, g.HeightLG  / mx, ColLG,
                    x0, yPos, rowH, labelW, valW, barX, barMaxW, focFrac);
                yPos += rowH;
            }

            if (_enabled.Contains("offline"))
            {
                float mx = Mathf.Max(Mathf.Max(Mathf.Abs(g.OfflineOF), Mathf.Abs(g.OfflineLG)), 0.01f);
                float focFrac = hasFocus ? Mathf.Abs(_focusedTrace.LandingPoint.Z / 0.9144f) / mx : -1f;
                DrawDualRow("Offline",
                    g.HasOF ? FormatOff(g.OfflineOF) : null, Mathf.Abs(g.OfflineOF) / mx, ColOF,
                    g.HasLG  ? FormatOff(g.OfflineLG)  : null, Mathf.Abs(g.OfflineLG)  / mx, ColLG,
                    x0, yPos, rowH, labelW, valW, barX, barMaxW, focFrac);
                yPos += rowH;
            }

            // ── Single-source launch params ────────────────────────────────────
            if (_enabled.Contains("speed") && g.Speed > 0)
            {
                string speedSd = SdSuffix(g.StdDevSpeed, g.TraceCountOF, "F1");
                string nowStr  = hasFocus && _focusedTrace.SpeedMph > 0 ? $"  (now: {_focusedTrace.SpeedMph:F0})" : "";
                DrawSingleRow("Ball Speed", $"{g.Speed:F0}{speedSd}{nowStr} mph",
                    x0, yPos, rowH, labelW);
                yPos += rowH;
            }
            if (_enabled.Contains("smash"))
            {
                string smashVal = g.SmashFactor > 0 ? $"{g.SmashFactor:F2}" : "N/A";
                DrawSingleRow("Smash Factor", smashVal, x0, yPos, rowH, labelW);
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
        float labelW, float valW, float barX, float barMaxW, float maxC,
        float focusedFrac = -1f)
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
            float ofYd = g.CarryOF / 0.9144f;
            string sdStr = SdSuffix(g.StdDevCarryOF / 0.9144f, g.TraceCountOF, "F0");
            DrawLabel($"{ofYd:F0}{sdStr} yd", new Vector2(x0 + labelW + valW * 0, ty), 13, ColOF);
            DrawRect(new Rect2(barX, barTop + lane * step, barMaxW * ofYd / (maxC > 0.01f ? maxC : 1f), barH), ColOF with { A = 0.8f });
            if (focusedFrac >= 0f)
                DrawFocusTick(barX + barMaxW * Mathf.Clamp(focusedFrac, 0f, 1f), barTop + lane * step, barH);
            lane++;
        }
        if (g.HasLG)
        {
            float ty = lane == 0 ? yPos + rowH * 0.05f : yPos + rowH * 0.05f + lane * (rowH * 0.38f);
            float lgYd = g.CarryLG / 0.9144f;
            string sdStr = SdSuffix(g.StdDevCarryLG / 0.9144f, g.TraceCountLG, "F0");
            DrawLabel($"{lgYd:F0}{sdStr} yd", new Vector2(x0 + labelW + valW * 1, ty), 13, ColLG);
            DrawRect(new Rect2(barX, barTop + lane * step, barMaxW * lgYd / (maxC > 0.01f ? maxC : 1f), barH), ColLG with { A = 0.8f });
        }
    }

    private void DrawDualRow(
        string label,
        string valA, float fracA, Color colA,
        string valB, float fracB, Color colB,
        float x0, float yPos, float rowH,
        float labelW, float valW, float barX, float barMaxW,
        float focusedFrac = -1f)
    {
        float barH  = Mathf.Max(4f, rowH * 0.25f);
        float barTop= yPos + rowH * 0.45f;

        DrawLabel(label, new Vector2(x0, yPos + rowH * 0.05f), 13, ColDim);
        if (valA != null)
        {
            DrawLabel(valA, new Vector2(x0 + labelW, yPos + rowH * 0.05f), 13, colA);
            DrawRect(new Rect2(barX, barTop - barH - 1f, barMaxW * Mathf.Clamp(fracA, 0f, 1f), barH), colA with { A = 0.8f });
            if (focusedFrac >= 0f)
                DrawFocusTick(barX + barMaxW * Mathf.Clamp(focusedFrac, 0f, 1f), barTop - barH - 1f, barH);
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

    // Bright vertical tick mark on top of an average bar at the focused shot's position.
    private void DrawFocusTick(float x, float barTop, float barH)
    {
        DrawLine(new Vector2(x, barTop - 2f), new Vector2(x, barTop + barH + 2f), ColFocus, 1.5f);
    }

    // Returns " ±N" suffix when count >= 2 and sd > 0, else empty string.
    private static string SdSuffix(float sd, int count, string fmt)
        => count >= 2 && sd > 0.5f ? $" \u00b1{sd.ToString(fmt)}" : "";

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
        // Carry: three sources (averages)
        public float CarryLm;   // LM reported (yards already)
        public float CarryOF;   // metres
        public float CarryLG;   // metres
        // Std dev of carry (yards) — 0 when n < 2
        public float StdDevCarryOF, StdDevCarryLG;
        // Other computed averages
        public float HeightOF, HeightLG;
        public float StdDevHeightOF;        // metres
        public float OfflineOF, OfflineLG;
        public float StdDevOfflineOF;       // metres (absolute)
        // Launch params (single-source, averages)
        public float Speed, StdDevSpeed;
        public float Vla, Hla, Backspin, Sidespin;
        public float SmashFactor; // 0 = not available
        public bool HasOF, HasLG;
        public int TraceCountOF, TraceCountLG;
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

            int n = set.Traces.Count;

            // First pass: accumulate sums and sums-of-squares for std dev.
            float sumCarry = 0, sumCarrySq = 0;
            float sumHeight = 0, sumHeightSq = 0;
            float sumOffline = 0, sumOfflineSq = 0;
            float sumSpeed = 0, sumSpeedSq = 0;
            float lmCarry = 0; int lmCount = 0;
            float vla = 0, hla = 0, bs = 0, ss = 0;
            float smash = 0; int smashCount = 0;

            foreach (var t in set.Traces)
            {
                float carry  = t.LandingPoint.X;
                float off    = t.LandingPoint.Z;
                float speed  = t.SpeedMph;
                float peak   = 0f;
                foreach (var pt in t.Points) peak = Mathf.Max(peak, pt.Y);

                sumCarry    += carry;  sumCarrySq   += carry * carry;
                sumHeight   += peak;   sumHeightSq  += peak * peak;
                sumOffline  += off;    sumOfflineSq += off * off;
                sumSpeed    += speed;  sumSpeedSq   += speed * speed;
                vla += t.LaunchAngleDeg; hla += t.DirectionDeg;
                bs  += t.BackspinRpm;    ss  += t.SidespinRpm;
                if (t.SmashFactor > 0) { smash += t.SmashFactor; smashCount++; }
                if (t.LmCarryDistanceYd > 0) { lmCarry += t.LmCarryDistanceYd; lmCount++; }
            }

            float carry_  = sumCarry / n;
            float height_ = sumHeight / n;
            float off_    = sumOffline / n;
            float speed_  = sumSpeed / n;
            vla /= n; hla /= n; bs /= n; ss /= n;
            if (lmCount > 0) lmCarry /= lmCount;

            static float Sd(float sumSq, float mean, int count) =>
                count < 2 ? 0f : Mathf.Sqrt(Mathf.Max(0f, sumSq / count - mean * mean));

            float sdCarry   = Sd(sumCarrySq,   carry_,  n);
            float sdHeight  = Sd(sumHeightSq,  height_, n);
            float sdOffline = Sd(sumOfflineSq, off_,    n);
            float sdSpeed   = Sd(sumSpeedSq,   speed_,  n);

            if (isLG)
            {
                Avg(ref g.CarryLG,  carry_,  g.HasLG);
                Avg(ref g.HeightLG, height_, g.HasLG);
                Avg(ref g.OfflineLG, off_,   g.HasLG);
                g.StdDevCarryLG = sdCarry;
                g.TraceCountLG += n;
                g.HasLG = true;
            }
            else
            {
                Avg(ref g.CarryOF,  carry_,  g.HasOF);
                Avg(ref g.HeightOF, height_, g.HasOF);
                Avg(ref g.OfflineOF, off_,   g.HasOF);
                g.StdDevCarryOF  = sdCarry;
                g.StdDevHeightOF = sdHeight;
                g.StdDevOfflineOF = sdOffline;
                g.StdDevSpeed    = sdSpeed;
                if (lmCarry > 0) Avg(ref g.CarryLm, lmCarry, g.CarryLm > 0);
                if (speed_ > 0) { g.Speed = speed_; g.Vla = vla; g.Hla = hla; g.Backspin = bs; g.Sidespin = ss; }
                if (smashCount > 0) g.SmashFactor = smash / smashCount;
                g.TraceCountOF += n;
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
