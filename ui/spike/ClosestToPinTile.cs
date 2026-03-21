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
    private List<CtpSession> _archived = new();

    public void Refresh(ClosestToPinConfig config, IReadOnlyList<ClosestToPinResult> results,
        IReadOnlyList<CtpSession> archivedSessions = null)
    {
        _config   = config;
        _results  = new List<ClosestToPinResult>(results);
        _archived = archivedSessions != null ? new List<CtpSession>(archivedSessions) : new List<CtpSession>();
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

        // Archived sessions first
        foreach (var session in _archived)
        {
            y = DrawSession(session.Label, session.Config, session.Results, x0, y, w, full, archived: true);
            if (y >= full.End.Y - pad - 22f) break;
        }

        // Active session
        string activeLabel = _archived.Count > 0
            ? $"Session {_archived.Count + 1} (active)"
            : "Active";
        DrawSession(activeLabel, _config, _results, x0, y, w, full, archived: false);
    }

    private float DrawSession(string label, ClosestToPinConfig cfg,
        IReadOnlyList<ClosestToPinResult> results,
        float x0, float y, float w, Rect2 full, bool archived)
    {
        // Session header
        Color hdrColor = archived ? ColDim : ColHeader;
        DrawStr($"{label}  ·  {cfg.Club} @ {cfg.TargetDistanceYards:F0} yd  (win ≤ {cfg.WinDistanceYards:F0} yd)",
            new Vector2(x0, y + 13f), archived ? 11 : 13, hdrColor);
        y += 22f;

        DrawLine(new Vector2(x0, y), new Vector2(x0 + w, y), ColGrid, 1f);
        y += 5f;

        if (results.Count == 0)
        {
            DrawStr("No shots yet — hit a shot to begin.", new Vector2(x0, y + 13f), 11, ColDim);
            return y + 22f;
        }

        // Column headers
        float rankX  = x0;
        float shotX  = x0 + 34f;
        float distX  = x0 + 76f;
        float deltaX = x0 + 155f;
        float offX   = x0 + 230f;
        float badgeX = x0 + w - 36f;

        DrawStr("#",       new Vector2(rankX,  y + 10f), 10, ColDim);
        DrawStr("Shot",    new Vector2(shotX,  y + 10f), 10, ColDim);
        DrawStr("Dist",    new Vector2(distX,  y + 10f), 10, ColDim);
        DrawStr("Δ Carry", new Vector2(deltaX, y + 10f), 10, ColDim);
        DrawStr("Offline", new Vector2(offX,   y + 10f), 10, ColDim);
        y += 14f;
        DrawLine(new Vector2(x0, y), new Vector2(x0 + w, y), ColGrid, 1f);
        y += 3f;

        const float rowH = 17f;
        const float innerPad = 12f;
        foreach (var r in results)
        {
            if (y + rowH > full.End.Y - innerPad - 22f) break;

            Color rankColor = r.Rank == 1 ? ColGold : ColDim;
            Color rowColor  = r.IsHit ? ColHit : ColVal;

            if (r.Rank == 1) DrawStr("★", new Vector2(rankX - 13f, y + 11f), 11, ColGold);
            DrawStr(r.Rank.ToString(),                        new Vector2(rankX,  y + 11f), 11, rankColor);
            DrawStr($"#{r.ShotNumber}",                       new Vector2(shotX,  y + 11f), 11, rowColor);
            DrawStr($"{r.DistanceToTargetYards:F1} yd",       new Vector2(distX,  y + 11f), 11, rowColor);
            DrawStr(r.CarryDeltaYards >= 0
                ? $"+{r.CarryDeltaYards:F1}"
                : $"{r.CarryDeltaYards:F1}",                  new Vector2(deltaX, y + 11f), 11, ColDim);
            string offStr = Mathf.Abs(r.OfflineYards) < 0.1f
                ? "on line"
                : $"{Mathf.Abs(r.OfflineYards):F1} {(r.OfflineYards < 0 ? "L" : "R")}";
            DrawStr(offStr,                                    new Vector2(offX,   y + 11f), 11, ColDim);
            if (r.IsHit) DrawStr("HIT", new Vector2(badgeX,   y + 11f), 10, ColHit);
            y += rowH;
        }

        // Footer summary
        int hits = 0; foreach (var r in results) if (r.IsHit) hits++;
        DrawStr($"{hits}/{results.Count} within {cfg.WinDistanceYards:F0} yd",
            new Vector2(x0, y + 10f), 10, hits > 0 ? ColHit : ColDim);
        y += 20f;
        DrawLine(new Vector2(x0, y), new Vector2(x0 + w, y), ColGrid, 0.5f);
        y += 6f;
        return y;
    }

    private void DrawStr(string text, Vector2 pos, int size, Color color)
    {
        DrawString(ThemeDB.FallbackFont, pos, text, HorizontalAlignment.Left, -1, size, color);
    }
}
