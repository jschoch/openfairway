using System.Collections.Generic;
using Godot;

// Coordinate convention from physics engine:
//   p.X = carry distance (forward)
//   p.Y = height (up)
//   p.Z = lateral / offline deviation  (negative = left, positive = right)

public partial class SpikePlotPanel : Control
{
    public enum PlotMode
    {
        TopDown,
        Side,
        Distribution
    }

    private const float MetersPerYard = 0.9144f;
    private const float YardsPerMeter = 1.09361f;

    private readonly List<RangeSpikeShotSet> _shotSets = new();

    [Export]
    public PlotMode Mode { get; set; } = PlotMode.TopDown;

    public void SetShotSets(IEnumerable<RangeSpikeShotSet> shotSets)
    {
        _shotSets.Clear();
        if (shotSets != null)
        {
            foreach (RangeSpikeShotSet shotSet in shotSets)
                _shotSets.Add(shotSet);
        }

        QueueRedraw();
    }

    public override void _Draw()
    {
        Rect2 fullRect = GetRect().Grow(-12.0f);
        DrawRect(fullRect, new Color("07090d"), filled: true);
        DrawRect(fullRect, new Color("2a3440"), filled: false, width: 2.0f);

        if (_shotSets.Count == 0)
        {
            DrawEmptyTopDown(fullRect);
            return;
        }

        ComputeBounds(out float maxCarry, out float maxHeight, out float maxOffline);

        switch (Mode)
        {
            case PlotMode.TopDown:
                DrawTopDownView(FitRect(fullRect, (maxOffline * 2f) / maxCarry), fullRect, maxCarry, maxOffline);
                break;
            case PlotMode.Side:
                DrawSideView(FitRect(fullRect, maxCarry / maxHeight), maxCarry, maxHeight);
                break;
            case PlotMode.Distribution:
            {
                ComputeDistributionBounds(out float minC, out float maxC, out float minO, out float maxO);
                float aspect = (maxO - minO) / (maxC - minC);
                DrawDistributionView(FitRect(fullRect, aspect), minC, maxC, minO, maxO);
                break;
            }
        }
    }

    // Returns the largest sub-rect with the given data aspect ratio (width/height)
    // centered inside available, preserving 1 meter = 1 pixel in both axes.
    private static Rect2 FitRect(Rect2 available, float dataAspect)
    {
        float availAspect = available.Size.X / available.Size.Y;
        if (dataAspect > availAspect)
        {
            // Data wider than available — constrain by width, letterbox vertically.
            float h = available.Size.X / dataAspect;
            return new Rect2(available.Position.X, available.Position.Y + (available.Size.Y - h) * 0.5f, available.Size.X, h);
        }

        // Data taller than available — constrain by height, pillarbox horizontally.
        float w = available.Size.Y * dataAspect;
        return new Rect2(available.Position.X + (available.Size.X - w) * 0.5f, available.Position.Y, w, available.Size.Y);
    }

    // ── Top-Down ──────────────────────────────────────────────────────────────
    // Bird's-eye looking down. Screen-X = offline (Z axis, ← left, right →).
    // Screen-Y = carry (X axis, tee at bottom, target at top).

    private void DrawTopDownView(Rect2 rect, Rect2 tileRect, float maxCarryM, float maxOfflineM)
    {
        DrawTopDownYardageGrid(rect, maxCarryM, maxOfflineM);

        // Trajectory lines + landing dots for every trace.
        // Track the annotation target: prefer a highlighted (focused/last-shot) trace
        // over a plain palette-colour trace so arrow-key navigation keeps it in sync.
        RangeSpikeShotTrace annotationTrace = null;
        foreach (RangeSpikeShotSet shotSet in _shotSets)
        {
            foreach (RangeSpikeShotTrace trace in shotSet.Traces)
            {
                DrawTopDownTrace(trace, rect, maxCarryM, maxOfflineM);
                // DisplayColor != Color means navigation or new-shot highlight is active.
                if (trace.DisplayColor != trace.Color || annotationTrace == null)
                    annotationTrace = trace;
            }
        }

        // Tee marker on top of everything.
        DrawTeeMarker(rect, maxCarryM, maxOfflineM);

        if (annotationTrace != null)
        {
            DrawLastShotBubble(annotationTrace, rect, maxCarryM, maxOfflineM);
            DrawOfflineBanner(annotationTrace, tileRect, maxOfflineM);
        }
    }

    private void DrawTopDownYardageGrid(Rect2 rect, float maxCarryM, float maxOfflineM)
    {
        Font font = ThemeDB.FallbackFont;
        const int LabelFontSize = 10;

        (float offIntervalM, float offIntervalYd) = PickOfflineInterval(maxOfflineM);
        (float carIntervalM, float carIntervalYd) = PickCarryInterval(maxCarryM);

        // Carry lines — horizontal stripes, labeled on the left edge.
        for (float carry = carIntervalM; carry < maxCarryM - carIntervalM * 0.1f; carry += carIntervalM)
        {
            float sy = rect.End.Y - rect.Size.Y * (carry / maxCarryM);
            DrawLine(new Vector2(rect.Position.X, sy), new Vector2(rect.End.X, sy), new Color("192533"), 1.0f);
            float yd = carry * YardsPerMeter;
            DrawString(font, new Vector2(rect.Position.X + 3, sy - 2), $"{yd:F0}", fontSize: LabelFontSize, modulate: new Color("2d4455"));
        }

        // Offline lines — vertical stripes, labeled at the top edge.
        int maxOff = Mathf.CeilToInt(maxOfflineM / offIntervalM);
        for (int i = -maxOff; i <= maxOff; i++)
        {
            float offlineM = i * offIntervalM;
            if (Mathf.Abs(offlineM) > maxOfflineM * 1.01f)
                continue;

            float sx = OfflineToScreenX(offlineM, rect, maxOfflineM);
            bool isCenter = i == 0;

            Color lineColor = isCenter ? new Color("2a4a62") : new Color("182330");
            float lineWidth = isCenter ? 1.5f : 1.0f;
            DrawLine(new Vector2(sx, rect.Position.Y), new Vector2(sx, rect.End.Y), lineColor, lineWidth);

            // Label at top, centered on the line.
            string label = isCenter ? "CTR" : $"{(int)(Mathf.Abs(i) * offIntervalYd)}{(offlineM < 0 ? "L" : "R")}";
            Color labelColor = isCenter ? new Color("2e5575") : new Color("253a4a");
            float labelX = sx - font.GetStringSize(label, fontSize: LabelFontSize).X * 0.5f;
            DrawString(font, new Vector2(labelX, rect.Position.Y + 11), label, fontSize: LabelFontSize, modulate: labelColor);
        }
    }

    private void DrawTopDownTrace(RangeSpikeShotTrace trace, Rect2 rect, float maxCarryM, float maxOfflineM)
    {
        if (trace.Points.Count < 2)
            return;

        for (int i = 1; i < trace.Points.Count; i++)
        {
            Vector2 from = TopDownProject(trace.Points[i - 1], rect, maxCarryM, maxOfflineM);
            Vector2 to = TopDownProject(trace.Points[i], rect, maxCarryM, maxOfflineM);
            DrawLine(from, to, trace.DisplayColor with { A = 0.7f }, 1.8f);
        }

        // Small landing dot.
        Vector2 landingPos = TopDownProject(trace.LandingPoint, rect, maxCarryM, maxOfflineM);
        DrawCircle(landingPos, 3.0f, trace.DisplayColor);
    }

    private void DrawTeeMarker(Rect2 rect, float maxCarryM, float maxOfflineM)
    {
        Vector2 tee = TopDownProject(Vector3.Zero, rect, maxCarryM, maxOfflineM);
        DrawCircle(tee, 5.5f, new Color("1e3a52"));
        DrawCircle(tee, 3.5f, new Color("4a80a8"));
        DrawCircle(tee, 1.5f, new Color("c0dff0"));
    }

    private void DrawLastShotBubble(RangeSpikeShotTrace trace, Rect2 rect, float maxCarryM, float maxOfflineM)
    {
        Vector3 landing = trace.LandingPoint;
        Vector2 landingScreen = TopDownProject(landing, rect, maxCarryM, maxOfflineM);

        // Highlighted landing dot.
        DrawCircle(landingScreen, 5.5f, trace.DisplayColor);
        DrawCircle(landingScreen, 2.5f, new Color("050809"));

        // Build label text: offline deviation + carry distance.
        float offlineYd = landing.Z * YardsPerMeter;
        float carryYd = landing.X * YardsPerMeter;
        string offLine = Mathf.Abs(offlineYd) < 0.4f
            ? "on line"
            : $"{Mathf.Abs(offlineYd):F1} yd {(offlineYd < 0 ? "L" : "R")}";
        string label = $"{carryYd:F0} yd  {offLine}";

        Font font = ThemeDB.FallbackFont;
        const int FontSize = 11;
        float ascent = font.GetAscent(FontSize);
        float descent = font.GetDescent(FontSize);
        float lineH = ascent + descent;
        float textW = font.GetStringSize(label, fontSize: FontSize).X;
        const float PadX = 6f;
        const float PadY = 4f;

        // Position bubble above and to the right, clamped inside the rect.
        float bx = Mathf.Clamp(landingScreen.X + 8, rect.Position.X + 2, rect.End.X - textW - PadX * 2 - 2);
        float by = Mathf.Clamp(landingScreen.Y - lineH - PadY * 2 - 4, rect.Position.Y + 2, rect.End.Y - lineH - PadY * 2 - 2);

        Rect2 bg = new Rect2(bx, by, textW + PadX * 2, lineH + PadY * 2);
        DrawRect(bg, new Color(0.03f, 0.06f, 0.11f, 0.92f));
        DrawRect(bg, trace.DisplayColor with { A = 0.55f }, filled: false, width: 1.0f);
        DrawString(font, new Vector2(bx + PadX, by + PadY + ascent), label, fontSize: FontSize, modulate: trace.DisplayColor);
    }

    // Large offline indicator anchored to the miss-side edge of the tile.
    // Font is scaled to fill the available side strip as generously as possible.
    private void DrawOfflineBanner(RangeSpikeShotTrace trace, Rect2 tileRect, float maxOfflineM)
    {
        float offlineYd = trace.LandingPoint.Z * YardsPerMeter;
        if (Mathf.Abs(offlineYd) < 0.4f) return; // on line — nothing to show

        bool isRight = offlineYd > 0;
        Color col    = trace.DisplayColor;
        Font  font   = ThemeDB.FallbackFont;

        // Two-line label: distance on top, direction below.
        string distText = $"{Mathf.Abs(offlineYd):F1} yd";
        string dirText  = isRight ? "RIGHT" : "LEFT";

        // Available strip: the outer fraction of the tile on the miss side.
        // Width budget: proportional to how much of the grid is on that side.
        float missRatio  = Mathf.Clamp(Mathf.Abs(trace.LandingPoint.Z) / Mathf.Max(maxOfflineM, 0.01f), 0f, 1f);
        float sideRatio  = 1f - missRatio;           // fraction of grid on the far (empty) side
        float stripW     = Mathf.Max(tileRect.Size.X * sideRatio * 0.88f, 40f);
        float stripH     = tileRect.Size.Y * 0.44f;  // use up to 44% of tile height

        // Fit the largest integer font size where both lines stay inside the strip.
        int fontSize = 8;
        for (int fs = 80; fs >= 8; fs--)
        {
            float w1 = font.GetStringSize(distText, fontSize: fs).X;
            float w2 = font.GetStringSize(dirText,  fontSize: fs).X;
            float h  = (fs + 2f) * 2f;
            if (w1 <= stripW && w2 <= stripW && h <= stripH)
            {
                fontSize = fs;
                break;
            }
        }

        float asc  = font.GetAscent(fontSize);
        float lineH = fontSize + 2f;
        float w1f   = font.GetStringSize(distText, fontSize: fontSize).X;
        float w2f   = font.GetStringSize(dirText,  fontSize: fontSize).X;

        // Anchor text to the miss-side edge, near the top.
        float yTop = tileRect.Position.Y + 8f + asc;
        float x1, x2;
        if (isRight)
        {
            x1 = tileRect.End.X - w1f - 6f;
            x2 = tileRect.End.X - w2f - 6f;
        }
        else
        {
            x1 = tileRect.Position.X + 6f;
            x2 = tileRect.Position.X + 6f;
        }

        DrawString(font, new Vector2(x1, yTop),          distText, fontSize: fontSize, modulate: col with { A = 0.90f });
        DrawString(font, new Vector2(x2, yTop + lineH),  dirText,  fontSize: fontSize, modulate: col with { A = 0.65f });
    }

    // Empty-state placeholder with faint centerline so the layout is clear.
    private void DrawEmptyTopDown(Rect2 rect)
    {
        float cx = rect.Position.X + rect.Size.X * 0.5f;
        DrawLine(new Vector2(cx, rect.Position.Y), new Vector2(cx, rect.End.Y), new Color("1c2e3d"), 1.5f);
        DrawString(ThemeDB.FallbackFont, rect.Position + new Vector2(12.0f, 24.0f),
            "Add a shot set to render this view.", fontSize: 14, modulate: new Color("9ba8b4"));
    }

    private static Vector2 TopDownProject(Vector3 p, Rect2 rect, float maxCarryM, float maxOfflineM)
    {
        float x = OfflineToScreenX(p.Z, rect, maxOfflineM);
        float y = rect.End.Y - rect.Size.Y * Mathf.Clamp(p.X / maxCarryM, 0f, 1f);
        return new Vector2(x, y);
    }

    private static float OfflineToScreenX(float offlineM, Rect2 rect, float maxOfflineM)
        => rect.Position.X + rect.Size.X * Mathf.Clamp((offlineM + maxOfflineM) / (maxOfflineM * 2f), 0f, 1f);

    // ── Side Profile ──────────────────────────────────────────────────────────
    // Screen-X = carry (X axis, tee at left). Screen-Y = height (Y, ground at bottom).

    private void DrawSideView(Rect2 rect, float maxCarryM, float maxHeightM)
    {
        // Ground line.
        DrawLine(new Vector2(rect.Position.X, rect.End.Y), rect.End, new Color("4c5a69"), 2.0f);
        // Light grid.
        Color grid = new Color("1a2535");
        for (int i = 1; i < 6; i++)
        {
            float x = rect.Position.X + rect.Size.X / 6.0f * i;
            DrawLine(new Vector2(x, rect.Position.Y), new Vector2(x, rect.End.Y), grid, 1.0f);
        }
        for (int i = 1; i < 4; i++)
        {
            float y = rect.Position.Y + rect.Size.Y / 4.0f * i;
            DrawLine(new Vector2(rect.Position.X, y), new Vector2(rect.End.X, y), grid, 1.0f);
        }

        foreach (RangeSpikeShotSet shotSet in _shotSets)
            foreach (RangeSpikeShotTrace trace in shotSet.Traces)
                DrawSideTrace(trace, rect, maxCarryM, maxHeightM);
    }

    private void DrawSideTrace(RangeSpikeShotTrace trace, Rect2 rect, float maxCarryM, float maxHeightM)
    {
        if (trace.Points.Count < 2)
            return;

        for (int i = 1; i < trace.Points.Count; i++)
        {
            Vector2 from = SideProject(trace.Points[i - 1], rect, maxCarryM, maxHeightM);
            Vector2 to = SideProject(trace.Points[i], rect, maxCarryM, maxHeightM);
            DrawLine(from, to, trace.DisplayColor, 2.0f);
        }
    }

    private static Vector2 SideProject(Vector3 p, Rect2 rect, float maxCarryM, float maxHeightM)
    {
        float x = rect.Position.X + rect.Size.X * Mathf.Clamp(p.X / maxCarryM, 0f, 1f);
        float y = rect.End.Y - rect.Size.Y * Mathf.Clamp(p.Y / maxHeightM, 0f, 1f);
        return new Vector2(x, y);
    }

    // ── Distribution ──────────────────────────────────────────────────────────
    // Landing scatter with zoom-to-fit bounds and per-set ellipses.

    private void ComputeDistributionBounds(out float minCarryM, out float maxCarryM, out float minOfflineM, out float maxOfflineM)
    {
        float minC = float.MaxValue, maxC = float.MinValue;
        float minO = float.MaxValue, maxO = float.MinValue;
        foreach (RangeSpikeShotSet set in _shotSets)
            foreach (RangeSpikeShotTrace trace in set.Traces)
            {
                Vector3 lp = trace.LandingPoint;
                if (lp.X < minC) minC = lp.X;
                if (lp.X > maxC) maxC = lp.X;
                float absZ = lp.Z;
                if (absZ < minO) minO = absZ;
                if (absZ > maxO) maxO = absZ;
            }
        if (minC > maxC) { minC = 0f; maxC = 50f; minO = -5f; maxO = 5f; } // fallback
        float cRange = Mathf.Max(maxC - minC, 5f);
        float oRange = Mathf.Max(maxO - minO, 5f);
        float cMargin = cRange * 0.18f;
        float oMargin = oRange * 0.18f;
        minCarryM = minC - cMargin;
        maxCarryM = maxC + cMargin;
        minOfflineM = minO - oMargin;
        maxOfflineM = maxO + oMargin;
    }

    private static Vector2 DistributionProject(Vector3 landing, Rect2 rect, float minCarryM, float maxCarryM, float minOfflineM, float maxOfflineM)
    {
        float x = rect.Position.X + rect.Size.X * Mathf.Clamp((landing.Z - minOfflineM) / (maxOfflineM - minOfflineM), 0f, 1f);
        float y = rect.End.Y - rect.Size.Y * Mathf.Clamp((landing.X - minCarryM) / (maxCarryM - minCarryM), 0f, 1f);
        return new Vector2(x, y);
    }

    private void DrawDistributionView(Rect2 rect, float minCarryM, float maxCarryM, float minOfflineM, float maxOfflineM)
    {
        Font font = ThemeDB.FallbackFont;
        const int LabelFontSize = 10;

        // Draw center offline line if 0 is within [minOfflineM, maxOfflineM].
        if (minOfflineM <= 0f && maxOfflineM >= 0f)
        {
            float ctrX = rect.Position.X + rect.Size.X * Mathf.Clamp((0f - minOfflineM) / (maxOfflineM - minOfflineM), 0f, 1f);
            DrawLine(new Vector2(ctrX, rect.Position.Y), new Vector2(ctrX, rect.End.Y), new Color("2a4a62"), 1.5f);
            string ctrLabel = "CTR";
            float ctrLabelX = ctrX - font.GetStringSize(ctrLabel, fontSize: LabelFontSize).X * 0.5f;
            DrawString(font, new Vector2(ctrLabelX, rect.Position.Y + 11), ctrLabel, fontSize: LabelFontSize, modulate: new Color("2e5575"));
        }

        // Draw carry labels at round yardages within the visible range.
        (float carIntervalM, float carIntervalYd) = PickCarryInterval(maxCarryM - minCarryM);
        float firstCarry = Mathf.Ceil(minCarryM / carIntervalM) * carIntervalM;
        for (float carry = firstCarry; carry < maxCarryM - carIntervalM * 0.1f; carry += carIntervalM)
        {
            float sy = rect.End.Y - rect.Size.Y * Mathf.Clamp((carry - minCarryM) / (maxCarryM - minCarryM), 0f, 1f);
            DrawLine(new Vector2(rect.Position.X, sy), new Vector2(rect.End.X, sy), new Color("192533"), 1.0f);
            float yd = carry * YardsPerMeter;
            DrawString(font, new Vector2(rect.Position.X + 3, sy - 2), $"{yd:F0}", fontSize: LabelFontSize, modulate: new Color("2d4455"));
        }

        // For each shot set: draw ellipse first, then landing dots on top.
        foreach (RangeSpikeShotSet shotSet in _shotSets)
        {
            DrawDistributionEllipse(shotSet, rect, minCarryM, maxCarryM, minOfflineM, maxOfflineM);
            foreach (RangeSpikeShotTrace trace in shotSet.Traces)
            {
                Vector2 pt = DistributionProject(trace.LandingPoint, rect, minCarryM, maxCarryM, minOfflineM, maxOfflineM);
                DrawCircle(pt, 4.5f, trace.DisplayColor);
            }
        }
    }

    private void DrawDistributionEllipse(RangeSpikeShotSet shotSet, Rect2 rect, float minCarryM, float maxCarryM, float minOfflineM, float maxOfflineM)
    {
        float sumC = 0f, sumO = 0f;
        int n = 0;
        foreach (RangeSpikeShotTrace trace in shotSet.Traces)
        {
            sumC += trace.LandingPoint.X;
            sumO += trace.LandingPoint.Z;
            n++;
        }
        if (n < 2) return;
        float meanC = sumC / n, meanO = sumO / n;
        float varC = 0f, varO = 0f;
        foreach (RangeSpikeShotTrace trace in shotSet.Traces)
        {
            float dc = trace.LandingPoint.X - meanC;
            float dz = trace.LandingPoint.Z - meanO;
            varC += dc * dc;
            varO += dz * dz;
        }
        float stdC = Mathf.Sqrt(varC / (n - 1));
        float stdO = Mathf.Sqrt(varO / (n - 1));
        if (stdC < 0.5f) stdC = 0.5f;
        if (stdO < 0.5f) stdO = 0.5f;
        float scale = 1.5f; // 1.5 sigma ellipse

        const int Steps = 48;
        var points = new Vector2[Steps + 1];
        for (int i = 0; i <= Steps; i++)
        {
            float theta = i * Mathf.Tau / Steps;
            float c = meanC + stdC * scale * Mathf.Sin(theta);  // carry on Y axis
            float o = meanO + stdO * scale * Mathf.Cos(theta);  // offline on X axis
            var lp = new Vector3(c, 0f, o);
            points[i] = DistributionProject(lp, rect, minCarryM, maxCarryM, minOfflineM, maxOfflineM);
        }
        DrawPolyline(points, shotSet.Traces.Count > 0 ? shotSet.Traces[0].Color with { A = 0.4f } : new Color(1, 1, 1, 0.3f), 1.5f);
    }

    // ── Shared helpers ────────────────────────────────────────────────────────

    private void ComputeBounds(out float maxCarryM, out float maxHeightM, out float maxOfflineM)
    {
        float carry = 0f, height = 0f, offline = 0f;
        foreach (RangeSpikeShotSet set in _shotSets)
            foreach (RangeSpikeShotTrace trace in set.Traces)
                foreach (Vector3 p in trace.Points)
                {
                    if (p.X > carry) carry = p.X;
                    if (p.Y > height) height = p.Y;
                    float abs = Mathf.Abs(p.Z);
                    if (abs > offline) offline = abs;
                }

        maxCarryM = Mathf.Max(carry * 1.1f, 20.0f);
        maxHeightM = Mathf.Max(height * 1.2f, 5.0f);
        maxOfflineM = Mathf.Max(offline * 1.3f, 3.0f);
    }

    // Pick a round yardage interval for offline grid lines.
    private static (float intervalM, float intervalYd) PickOfflineInterval(float maxOfflineM)
    {
        float maxYd = maxOfflineM * YardsPerMeter;
        float yd = maxYd <= 6f ? 2f : maxYd <= 15f ? 5f : maxYd <= 30f ? 10f : 20f;
        return (yd * MetersPerYard, yd);
    }

    // Pick a round yardage interval for carry grid lines.
    private static (float intervalM, float intervalYd) PickCarryInterval(float maxCarryM)
    {
        float maxYd = maxCarryM * YardsPerMeter;
        float yd = maxYd <= 60f ? 10f : maxYd <= 150f ? 25f : maxYd <= 300f ? 50f : 100f;
        return (yd * MetersPerYard, yd);
    }
}
