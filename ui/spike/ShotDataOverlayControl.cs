using System.Collections.Generic;
using Godot;

// 2D HUD overlay drawn on top of the 3D viewport texture.
// Shows carry, apex, offline, and (when CTP is active) distance-to-pin
// for the most recent shot.
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
        const int FontSize  = 13;
        const float RowH    = 21f;
        const float PadX    = 10f;
        const float PadY    = 8f;
        const float MarginL = 12f;
        const float MarginB = 12f;

        string offStr = Mathf.Abs(_data.OfflineYards) < 0.3f
            ? "on line"
            : $"{Mathf.Abs(_data.OfflineYards):F1} yd {(_data.OfflineYards < 0f ? "L" : "R")}";

        var rows = new List<(string label, string value, Color col)>
        {
            ("Carry",   $"{_data.CarryYards:F0} yd", ColCarry),
            ("Height",  $"{_data.ApexFeet:F0} ft",   ColStat),
            ("Offline", offStr,                        ColStat),
        };
        if (_data.HasCtp)
        {
            Color ctpCol = _data.CtpIsHit ? ColHit : ColMiss;
            string mark  = _data.CtpIsHit ? " \u2713" : " \u2717";
            rows.Add(("To pin", $"{_data.CtpDistanceYards:F1} yd{mark}", ctpCol));
        }

        float maxLW = 0f, maxVW = 0f;
        foreach (var (lbl, val, _) in rows)
        {
            float lw = font.GetStringSize(lbl + ":", fontSize: FontSize).X;
            float vw = font.GetStringSize(val,       fontSize: FontSize).X;
            if (lw > maxLW) maxLW = lw;
            if (vw > maxVW) maxVW = vw;
        }

        const float GapLV = 8f;
        float bgW  = PadX * 2f + maxLW + GapLV + maxVW;
        float bgH  = PadY * 2f + rows.Count * RowH;
        float bgX  = MarginL;
        float bgY  = Size.Y - MarginB - bgH;

        DrawRect(new Rect2(bgX, bgY, bgW, bgH), ColBg);
        DrawRect(new Rect2(bgX, bgY, bgW, bgH), ColBorder, filled: false, width: 1f);

        float asc = font.GetAscent(FontSize);
        for (int i = 0; i < rows.Count; i++)
        {
            var (lbl, val, col) = rows[i];
            float rowY = bgY + PadY + i * RowH + asc;
            DrawString(font, new Vector2(bgX + PadX,               rowY), lbl + ":", fontSize: FontSize, modulate: ColDim);
            DrawString(font, new Vector2(bgX + PadX + maxLW + GapLV, rowY), val,    fontSize: FontSize, modulate: col);
        }
    }
}
