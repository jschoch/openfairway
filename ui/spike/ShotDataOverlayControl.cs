using System.Collections.Generic;
using Godot;

// 2D HUD overlay drawn on top of the 3D viewport texture.
// Shows carry, apex, offline, and (when CTP is active) distance-to-pin
// for the most recent shot.
//
// Font size scales dynamically with tile height so the overlay stays readable
// in large portrait-mode tiles. When the tile is wide (landscape ratio) the
// stats are split into two corner pills — left for primary data, right for
// offline / CTP — so both sides of the screen are used.
public partial class ShotDataOverlayControl : Control
{
    private static readonly Color ColBg     = new(0.00f, 0.00f, 0.00f, 0.65f);
    private static readonly Color ColBorder = new(1.00f, 1.00f, 1.00f, 0.12f);
    private static readonly Color ColDim    = new(0.55f, 0.65f, 0.75f, 1.00f);
    private static readonly Color ColCarry  = new(1.00f, 0.72f, 0.20f, 1.00f);
    private static readonly Color ColStat   = new(0.80f, 0.88f, 0.96f, 1.00f);
    private static readonly Color ColHit    = new(0.28f, 0.85f, 0.48f, 1.00f);
    private static readonly Color ColMiss   = new(0.95f, 0.28f, 0.28f, 1.00f);

    private ShotDataOverlay _data;

    public void SetData(ShotDataOverlay data)
    {
        _data = data;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (!_data.HasData) return;

        Font font = ThemeDB.FallbackFont;

        // Scale font with tile height: ~16pt baseline at 180 px, caps at 48pt.
        int fontSize = Mathf.Clamp((int)(Size.Y / 10f), 16, 48);
        float rowH   = fontSize * 1.6f;
        float padX   = fontSize * 0.75f;
        float padY   = fontSize * 0.55f;
        float margin = fontSize * 0.9f;
        float gapLV  = fontSize * 0.6f;

        string offStr = Mathf.Abs(_data.OfflineYards) < 0.3f
            ? "on line"
            : $"{Mathf.Abs(_data.OfflineYards):F1} yd {(_data.OfflineYards < 0f ? "L" : "R")}";
        Color ctpCol = _data.HasCtp ? (_data.CtpIsHit ? ColHit : ColMiss) : ColDim;
        string ctpVal = _data.HasCtp
            ? $"{_data.CtpDistanceYards:F1} yd{(_data.CtpIsHit ? " \u2713" : " \u2717")}"
            : "";

        // Wide tile (landscape ratio ≥ 1.4): split into two corner pills.
        bool wideLayout = Size.X >= Size.Y * 1.4f;

        if (wideLayout)
        {
            // Left pill: Carry + Height
            var left = new List<(string lbl, string val, Color col)>
            {
                ("Carry",  $"{_data.CarryYards:F0} yd", ColCarry),
                ("Height", $"{_data.ApexFeet:F0} ft",   ColStat),
            };
            DrawPill(font, fontSize, rowH, padX, padY, gapLV,
                left, margin, Size.Y - margin, anchorRight: false);

            // Right pill: Offline + To pin (when active)
            var right = new List<(string lbl, string val, Color col)>
            {
                ("Offline", offStr, ColStat),
            };
            if (_data.HasCtp)
                right.Add(("To pin", ctpVal, ctpCol));
            DrawPill(font, fontSize, rowH, padX, padY, gapLV,
                right, Size.X - margin, Size.Y - margin, anchorRight: true);
        }
        else
        {
            // Tall / square tile: single bottom-left pill with all stats.
            var rows = new List<(string lbl, string val, Color col)>
            {
                ("Carry",   $"{_data.CarryYards:F0} yd", ColCarry),
                ("Height",  $"{_data.ApexFeet:F0} ft",   ColStat),
                ("Offline", offStr,                        ColStat),
            };
            if (_data.HasCtp)
                rows.Add(("To pin", ctpVal, ctpCol));
            DrawPill(font, fontSize, rowH, padX, padY, gapLV,
                rows, margin, Size.Y - margin, anchorRight: false);
        }
    }

    // Draws a single pill (background rect + rows of label:value).
    // bgAnchorX / bgAnchorY = bottom-left corner (anchorRight=false) or
    // bottom-right corner (anchorRight=true) of the pill.
    private void DrawPill(
        Font font, int fontSize, float rowH, float padX, float padY, float gapLV,
        List<(string lbl, string val, Color col)> rows,
        float bgAnchorX, float bgAnchorY, bool anchorRight)
    {
        if (rows.Count == 0) return;

        float maxLW = 0f, maxVW = 0f;
        foreach (var (lbl, val, _) in rows)
        {
            float lw = font.GetStringSize(lbl + ":", fontSize: fontSize).X;
            float vw = font.GetStringSize(val,       fontSize: fontSize).X;
            if (lw > maxLW) maxLW = lw;
            if (vw > maxVW) maxVW = vw;
        }

        float bgW = padX * 2f + maxLW + gapLV + maxVW;
        float bgH = padY * 2f + rows.Count * rowH;
        float bgX = anchorRight ? bgAnchorX - bgW : bgAnchorX;
        float bgY = bgAnchorY - bgH;

        DrawRect(new Rect2(bgX, bgY, bgW, bgH), ColBg);
        DrawRect(new Rect2(bgX, bgY, bgW, bgH), ColBorder, filled: false, width: 1f);

        float asc = font.GetAscent(fontSize);
        for (int i = 0; i < rows.Count; i++)
        {
            var (lbl, val, col) = rows[i];
            float rowY = bgY + padY + i * rowH + asc;
            DrawString(font, new Vector2(bgX + padX,               rowY), lbl + ":", fontSize: fontSize, modulate: ColDim);
            DrawString(font, new Vector2(bgX + padX + maxLW + gapLV, rowY), val,    fontSize: fontSize, modulate: col);
        }
    }
}
