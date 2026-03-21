using System.Collections.Generic;
using Godot;

/// <summary>
/// Scoreboard tile for a Closest to the Pin session.
/// Draws a ranked shot list and summary using the same canvas-draw style as SpikeStatPanel.
/// </summary>
public sealed partial class ClosestToPinTile : Control
{
    private static readonly Color ColBg      = new(0.04f, 0.07f, 0.11f, 1.0f);
    private static readonly Color ColHeader  = new(0.77f, 0.88f, 1.00f, 1.0f);
    private static readonly Color ColHit     = new(0.28f, 0.85f, 0.48f, 1.0f);  // green
    private static readonly Color ColMiss    = new(0.95f, 0.28f, 0.28f, 1.0f);  // red
    private static readonly Color ColDim     = new(0.45f, 0.55f, 0.65f, 0.6f);
    private static readonly Color ColVal     = new(0.80f, 0.88f, 0.96f, 1.0f);
    private static readonly Color ColGold    = new(1.00f, 0.80f, 0.20f, 1.0f);  // #1 rank
    private static readonly Color ColGrid    = new(0.12f, 0.18f, 0.26f, 0.5f);

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

        float pad = 12f;
        float x0 = full.Position.X + pad;
        float y = full.Position.Y + pad;
        float w = full.Size.X - pad * 2f;

        if (_config == null)
        {
            DrawStr("No active session.", new Vector2(x0, y + 16f), 12, ColDim);
            return;
        }

        // Header
        string header = $"Closest to the Pin  ·  {_config.Club}  @  {_config.TargetDistanceYards:F0} yd  (win ≤ {_config.WinDistanceYards:F0} yd)";
        DrawStr(header, new Vector2(x0, y + 13f), 13, ColHeader);
        y += 26f;

        DrawLine(new Vector2(x0, y), new Vector2(x0 + w, y), ColGrid, 1f);
        y += 6f;

        if (_results.Count == 0)
        {
            DrawStr("No shots yet — hit a shot to begin.", new Vector2(x0, y + 14f), 11, ColDim);
            return;
        }

        // Column headers
        float rankX    = x0;
        float shotX    = x0 + 36f;
        float distX    = x0 + 80f;
        float deltaX   = x0 + 160f;
        float offX     = x0 + 240f;
        float badgeX   = x0 + w - 40f;

        DrawStr("#",      new Vector2(rankX,  y + 11f), 10, ColDim);
        DrawStr("Shot",   new Vector2(shotX,  y + 11f), 10, ColDim);
        DrawStr("Dist",   new Vector2(distX,  y + 11f), 10, ColDim);
        DrawStr("Δ Carry",new Vector2(deltaX, y + 11f), 10, ColDim);
        DrawStr("Offline",new Vector2(offX,   y + 11f), 10, ColDim);
        y += 16f;
        DrawLine(new Vector2(x0, y), new Vector2(x0 + w, y), ColGrid, 1f);
        y += 4f;

        // Sort by shot number for display
        float rowH = 18f;
        int maxRows = (int)((full.End.Y - pad * 2f - 80f) / rowH);

        foreach (var r in _results)
        {
            if (y + rowH > full.End.Y - pad - 22f) break;

            Color rankColor = r.Rank == 1 ? ColGold : ColDim;
            Color rowColor  = r.IsHit ? ColHit : ColVal;

            DrawStr(r.Rank.ToString(),         new Vector2(rankX,  y + 12f), 11, rankColor);
            DrawStr($"#{r.ShotNumber}",        new Vector2(shotX,  y + 12f), 11, rowColor);
            DrawStr($"{r.DistanceToTargetYards:F1} yd", new Vector2(distX, y + 12f), 11, rowColor);

            string deltaStr = r.CarryDeltaYards >= 0
                ? $"+{r.CarryDeltaYards:F1}"
                : $"{r.CarryDeltaYards:F1}";
            DrawStr(deltaStr, new Vector2(deltaX, y + 12f), 11, ColDim);

            string offStr = Mathf.Abs(r.OfflineYards) < 0.1f
                ? "on line"
                : $"{Mathf.Abs(r.OfflineYards):F1} {(r.OfflineYards < 0 ? "L" : "R")}";
            DrawStr(offStr, new Vector2(offX, y + 12f), 11, ColDim);

            // Hit badge
            if (r.IsHit)
                DrawStr("HIT", new Vector2(badgeX, y + 12f), 10, ColHit);

            // Crown for leader
            if (r.Rank == 1)
                DrawStr("★", new Vector2(rankX - 14f, y + 12f), 11, ColGold);

            y += rowH;
        }

        // Summary footer
        DrawLine(new Vector2(x0, full.End.Y - pad - 18f), new Vector2(x0 + w, full.End.Y - pad - 18f), ColGrid, 1f);
        int hits = 0;
        foreach (var r in _results) if (r.IsHit) hits++;
        string summary = $"{hits} of {_results.Count} shots within {_config.WinDistanceYards:F0} yd";
        DrawStr(summary, new Vector2(x0, full.End.Y - pad - 4f), 11, hits > 0 ? ColHit : ColDim);
    }

    private void DrawStr(string text, Vector2 pos, int size, Color color)
    {
        DrawString(ThemeDB.FallbackFont, pos, text, HorizontalAlignment.Left, -1, size, color);
    }
}
