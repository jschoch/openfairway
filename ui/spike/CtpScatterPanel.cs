using System.Collections.Generic;
using Godot;

/// <summary>
/// Target-centered scatter view for Closest to the Pin mode.
///
/// Origin = the target/pin. X axis = offline (left/right). Y axis = carry delta
/// (up = long, down = short). Win circle drawn around the origin. Each shot is
/// a dot colored green (hit) or red (miss) with its shot number.
/// </summary>
public sealed partial class CtpScatterPanel : Control
{
    private static readonly Color ColBg      = new(0.04f, 0.07f, 0.11f, 1.0f);
    private static readonly Color ColBorder  = new(0.16f, 0.24f, 0.34f, 1.0f);
    private static readonly Color ColGrid    = new(0.10f, 0.16f, 0.24f, 0.7f);
    private static readonly Color ColCtr     = new(0.18f, 0.34f, 0.52f, 0.9f);
    private static readonly Color ColWinFill = new(0.28f, 0.82f, 0.46f, 0.10f);
    private static readonly Color ColWinRing = new(0.28f, 0.82f, 0.46f, 0.55f);
    private static readonly Color ColPin     = new(0.95f, 0.88f, 0.20f, 1.0f);
    private static readonly Color ColHit     = new(0.28f, 0.85f, 0.48f, 1.0f);
    private static readonly Color ColMiss    = new(0.95f, 0.28f, 0.28f, 1.0f);
    private static readonly Color ColDim     = new(0.45f, 0.55f, 0.65f, 0.6f);
    private static readonly Color ColLabel   = new(0.75f, 0.88f, 1.00f, 1.0f);

    private ClosestToPinConfig _config;
    private List<ClosestToPinResult> _results = new();

    public void Refresh(ClosestToPinConfig config, IReadOnlyList<ClosestToPinResult> results)
    {
        _config = config;
        _results = new List<ClosestToPinResult>(results);
        QueueRedraw();
    }

    public override void _Draw()
    {
        Rect2 full = new(Vector2.Zero, Size);
        DrawRect(full, ColBg);
        DrawRect(full, ColBorder, filled: false, width: 2f);

        float pad = 14f;
        Rect2 inner = full.Grow(-pad);

        if (_config == null)
        {
            DrawStr("No CTP session active.", inner.Position + new Vector2(8, 20), 12, ColDim);
            return;
        }

        // Choose a scale that fits the win circle and all shots with margin.
        float winYd = _config.WinDistanceYards;
        float maxDelta = winYd * 1.4f; // start with win-radius view
        foreach (var r in _results)
        {
            float extent = Mathf.Max(Mathf.Abs(r.CarryDeltaYards), Mathf.Abs(r.OfflineYards));
            if (extent * 1.35f > maxDelta)
                maxDelta = extent * 1.35f;
        }
        maxDelta = Mathf.Max(maxDelta, 3f); // minimum 3 yd half-span

        // Pixels per yard — constrain to the smaller axis so the circle is round.
        float halfW = inner.Size.X * 0.5f;
        float halfH = inner.Size.Y * 0.5f;
        float ppy   = Mathf.Min(halfW, halfH) / maxDelta;

        Vector2 origin = inner.Position + inner.Size * 0.5f;

        DrawGrid(inner, origin, maxDelta, ppy);
        DrawWinCircle(origin, winYd, ppy);
        DrawPin(origin);
        DrawShots(origin, ppy);
        DrawHeader(full, pad);
        DrawAxisLabels(inner, origin, maxDelta, ppy);
    }

    private void DrawGrid(Rect2 inner, Vector2 origin, float maxDelta, float ppy)
    {
        // Centerlines
        DrawLine(new Vector2(inner.Position.X, origin.Y), new Vector2(inner.End.X, origin.Y), ColCtr, 1f);
        DrawLine(new Vector2(origin.X, inner.Position.Y), new Vector2(origin.X, inner.End.Y), ColCtr, 1f);

        // Round-yardage grid rings
        float interval = PickInterval(maxDelta);
        for (float d = interval; d <= maxDelta; d += interval)
        {
            float px = d * ppy;
            // Horizontal carry lines
            foreach (float sign in new[] { -1f, 1f })
            {
                float y = origin.Y + sign * px;
                if (y >= inner.Position.Y && y <= inner.End.Y)
                    DrawLine(new Vector2(inner.Position.X, y), new Vector2(inner.End.X, y), ColGrid, 1f);
            }
            // Vertical offline lines
            foreach (float sign in new[] { -1f, 1f })
            {
                float x = origin.X + sign * px;
                if (x >= inner.Position.X && x <= inner.End.X)
                    DrawLine(new Vector2(x, inner.Position.Y), new Vector2(x, inner.End.Y), ColGrid, 1f);
            }
        }
    }

    private void DrawWinCircle(Vector2 origin, float winYd, float ppy)
    {
        float r = winYd * ppy;
        // Filled interior
        DrawCircle(origin, r, ColWinFill);
        // Outline — approximate with a polyline
        const int Steps = 64;
        var pts = new Vector2[Steps + 1];
        for (int i = 0; i <= Steps; i++)
        {
            float theta = i * Mathf.Tau / Steps;
            pts[i] = origin + new Vector2(Mathf.Cos(theta), Mathf.Sin(theta)) * r;
        }
        DrawPolyline(pts, ColWinRing, 2f);
    }

    private void DrawPin(Vector2 origin)
    {
        // Flag pole
        DrawLine(origin, origin + new Vector2(0f, -22f), ColPin, 2f);
        // Triangular flag
        DrawColoredPolygon(new[]
        {
            origin + new Vector2(0f,  -22f),
            origin + new Vector2(12f, -17f),
            origin + new Vector2(0f,  -12f),
        }, ColPin);
        // Base dot
        DrawCircle(origin, 4f, ColPin with { A = 0.7f });
    }

    private void DrawShots(Vector2 origin, float ppy)
    {
        if (_results.Count == 0) return;

        Font font = ThemeDB.FallbackFont;
        const int NumFontSize = 9;

        // Find the last shot number to highlight it
        int lastShot = 0;
        foreach (var r in _results)
            if (r.ShotNumber > lastShot) lastShot = r.ShotNumber;

        // Draw crosshair lines for the last shot first (behind dots).
        foreach (var r in _results)
        {
            if (r.ShotNumber != lastShot) continue;
            Vector2 pt = origin + new Vector2(r.OfflineYards * ppy, -r.CarryDeltaYards * ppy);
            Color col = r.IsHit ? ColHit : ColMiss;
            Color lineCol = col with { A = 0.45f };
            // Horizontal line: shot → Y-axis (shows offline)
            DrawLine(new Vector2(origin.X, pt.Y), pt, lineCol, 1.0f);
            // Vertical line: shot → X-axis (shows carry delta)
            DrawLine(new Vector2(pt.X, origin.Y), pt, lineCol, 1.0f);
            break;
        }

        foreach (var r in _results)
        {
            // offline = X axis (right is positive), carry delta = Y axis (long is up → negative screen Y)
            Vector2 pt = origin + new Vector2(r.OfflineYards * ppy, -r.CarryDeltaYards * ppy);

            bool isLast = r.ShotNumber == lastShot;
            bool isRank1 = r.Rank == 1;
            Color col = r.IsHit ? ColHit : ColMiss;
            float radius = isLast ? 8f : (isRank1 ? 7f : 5.5f);

            // Outer glow for latest shot
            if (isLast)
                DrawCircle(pt, radius + 3f, col with { A = 0.25f });

            DrawCircle(pt, radius, col);

            // Dark inner so the number is readable
            DrawCircle(pt, radius - 2.5f, ColBg with { A = 0.7f });

            // Shot number
            string num = r.ShotNumber.ToString();
            float tw = font.GetStringSize(num, fontSize: NumFontSize).X;
            float asc = font.GetAscent(NumFontSize);
            DrawString(font, pt + new Vector2(-tw * 0.5f, asc * 0.5f), num,
                fontSize: NumFontSize, modulate: col);

            // ★ crown for the leader (off to the upper-right of the dot)
            if (isRank1)
                DrawString(font, pt + new Vector2(radius + 1f, -radius + 2f), "★",
                    fontSize: 9, modulate: new Color(1f, 0.82f, 0.15f));
        }
    }

    private void DrawHeader(Rect2 full, float pad)
    {
        Font font = ThemeDB.FallbackFont;
        string title = _config != null
            ? $"Target scatter  ·  {_config.Club} @ {_config.TargetDistanceYards:F0} yd  (pin ±{_config.WinDistanceYards:F0} yd)"
            : "Target scatter";
        DrawString(font, new Vector2(full.Position.X + pad, full.Position.Y + pad + 11f),
            title, fontSize: 11, modulate: ColLabel);
    }

    private void DrawAxisLabels(Rect2 inner, Vector2 origin, float maxDelta, float ppy)
    {
        Font font = ThemeDB.FallbackFont;
        const int LabelSize = 9;
        float interval = PickInterval(maxDelta);

        for (float d = interval; d <= maxDelta; d += interval)
        {
            string ydStr = $"{d:F0}";

            // Carry (vertical) labels: "long" above, "short" below
            float yLong  = origin.Y - d * ppy;
            float yShort = origin.Y + d * ppy;
            if (yLong >= inner.Position.Y)
                DrawString(font, new Vector2(origin.X + 3f, yLong + 9f), ydStr, fontSize: LabelSize, modulate: ColGrid with { A = 0.9f });
            if (yShort <= inner.End.Y)
                DrawString(font, new Vector2(origin.X + 3f, yShort - 2f), ydStr, fontSize: LabelSize, modulate: ColGrid with { A = 0.9f });

            // Offline (horizontal) labels: L left, R right
            float xRight = origin.X + d * ppy;
            float xLeft  = origin.X - d * ppy;
            float labelW = font.GetStringSize(ydStr, fontSize: LabelSize).X;
            if (xRight <= inner.End.X)
                DrawString(font, new Vector2(xRight - labelW * 0.5f, origin.Y - 2f), ydStr, fontSize: LabelSize, modulate: ColGrid with { A = 0.9f });
            if (xLeft >= inner.Position.X)
                DrawString(font, new Vector2(xLeft - labelW * 0.5f, origin.Y - 2f), ydStr, fontSize: LabelSize, modulate: ColGrid with { A = 0.9f });
        }

        // Axis direction labels
        DrawString(font, new Vector2(inner.Position.X + 2f, origin.Y - 3f), "L",
            fontSize: LabelSize, modulate: ColCtr with { A = 0.8f });
        DrawString(font, new Vector2(inner.End.X - 10f, origin.Y - 3f), "R",
            fontSize: LabelSize, modulate: ColCtr with { A = 0.8f });
        DrawString(font, new Vector2(origin.X + 3f, inner.Position.Y + 12f), "LONG",
            fontSize: LabelSize, modulate: ColCtr with { A = 0.8f });
        DrawString(font, new Vector2(origin.X + 3f, inner.End.Y - 3f), "SHORT",
            fontSize: LabelSize, modulate: ColCtr with { A = 0.8f });

        // Win radius annotation on the circle
        if (_config != null)
        {
            float r = _config.WinDistanceYards * ppy;
            string winLabel = $"{_config.WinDistanceYards:F0} yd";
            float wlw = font.GetStringSize(winLabel, fontSize: LabelSize).X;
            DrawString(font, new Vector2(origin.X + r - wlw * 0.5f, origin.Y - r - 3f),
                winLabel, fontSize: LabelSize, modulate: ColWinRing);
        }
    }

    private static float PickInterval(float maxDelta)
    {
        if (maxDelta <= 3f)  return 1f;
        if (maxDelta <= 8f)  return 2f;
        if (maxDelta <= 20f) return 5f;
        if (maxDelta <= 50f) return 10f;
        return 25f;
    }

    private void DrawStr(string text, Vector2 pos, int size, Color color)
    {
        DrawString(ThemeDB.FallbackFont, pos, text, HorizontalAlignment.Left, -1, size, color);
    }
}
