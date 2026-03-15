using System;
using Godot;

public partial class SpikeTilePanel : PanelContainer
{
public event Action<SpikeTilePanel, Vector2> DragStarted;
    public event Action<SpikeTilePanel, Vector2> ResizeStarted;

    public string TileId { get; set; } = string.Empty;

    private bool _resizeHovered;
    private bool _deleteHovered;

    public void SetResizeHovered(bool hovered)
    {
        if (_resizeHovered == hovered)
            return;
        _resizeHovered = hovered;
        QueueRedraw();
    }

    public void SetDeleteHovered(bool hovered)
    {
        if (_deleteHovered == hovered)
            return;
        _deleteHovered = hovered;
        QueueRedraw();
    }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
    }

    // Called by the canvas-level resize handle overlay.
    public void TriggerResize(Vector2 globalMousePosition) => ResizeStarted?.Invoke(this, globalMousePosition);

    // Matches ResizeOverlaySize in SpikeTileCanvas — keep in sync.
    private const float ResizeExclusionSize = 48f;

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton mouseButton)
            return;

        if (mouseButton.ButtonIndex != MouseButton.Left || !mouseButton.Pressed)
            return;

        // Suppress drag if the click is inside the bottom-right resize zone.
        Vector2 local = mouseButton.Position;
        if (local.X >= Size.X - ResizeExclusionSize && local.Y >= Size.Y - ResizeExclusionSize)
        {
            AcceptEvent();
            return;
        }

        // Suppress drag if the click is inside the bottom-left delete zone.
        if (local.X <= ResizeExclusionSize && local.Y >= Size.Y - ResizeExclusionSize)
        {
            AcceptEvent();
            return;
        }

        DragStarted?.Invoke(this, mouseButton.GlobalPosition);
        AcceptEvent();
    }

    public override void _Draw()
    {
        // ── Resize triangle in lower-right ────────────────────────────────────
        const float Inset = 6f;
        const float TriSize = 22f;
        float r = Size.X - Inset;
        float b = Size.Y - Inset;
        Vector2 corner = new(r, b);
        Vector2 topPt = new(r, b - TriSize);
        Vector2 leftPt = new(r - TriSize, b);

        Color fill = _resizeHovered ? new Color(0.12f, 0.24f, 0.38f) : new Color(0.07f, 0.12f, 0.19f);
        Color edge = _resizeHovered ? new Color("5b8fad") : new Color("2d4a5e");
        Color grip = _resizeHovered ? new Color("7ab8d8") : new Color("4a7a96");

        DrawPolygon(new[] { corner, topPt, leftPt }, new[] { fill });
        DrawLine(topPt, leftPt, edge, 1.5f);

        for (int i = 1; i <= 3; i++)
        {
            float d = i * 5.5f;
            DrawLine(new Vector2(r, b - d), new Vector2(r - d, b), grip, 1.2f);
        }

        // ── Trashcan icon in lower-left ────────────────────────────────────────
        const float TrashInset = 6f;
        const float TrashOverallSize = 28f;
        float tx = TrashInset;
        float ty = Size.Y - TrashInset - TrashOverallSize;

        Color trashFill = _deleteHovered ? new Color(0.38f, 0.10f, 0.10f) : new Color(0.14f, 0.07f, 0.07f);
        Color trashBorder = _deleteHovered ? new Color("ad3b3b") : new Color("5e2a2a");
        Color trashLines = _deleteHovered ? new Color("d87878") : new Color("8a4a4a");

        // Body: rectangular base of the can
        float bodyW = TrashOverallSize * 0.72f;
        float bodyH = TrashOverallSize * 0.60f;
        float bodyX = tx + (TrashOverallSize - bodyW) * 0.5f;
        float bodyY = ty + TrashOverallSize - bodyH;
        DrawRect(new Rect2(bodyX, bodyY, bodyW, bodyH), trashFill, filled: true);
        DrawRect(new Rect2(bodyX, bodyY, bodyW, bodyH), trashBorder, filled: false, width: 1.2f);

        // Lid: slightly wider rect just above the body
        float lidW = bodyW + 4f;
        float lidH = TrashOverallSize * 0.14f;
        float lidX = tx + (TrashOverallSize - lidW) * 0.5f;
        float lidY = bodyY - lidH - 1f;
        DrawRect(new Rect2(lidX, lidY, lidW, lidH), trashFill, filled: true);
        DrawRect(new Rect2(lidX, lidY, lidW, lidH), trashBorder, filled: false, width: 1.2f);

        // Handle on the lid
        float handleW = bodyW * 0.36f;
        float handleH = TrashOverallSize * 0.12f;
        float handleX = tx + (TrashOverallSize - handleW) * 0.5f;
        float handleY = lidY - handleH;
        DrawRect(new Rect2(handleX, handleY, handleW, handleH), trashFill, filled: true);
        DrawRect(new Rect2(handleX, handleY, handleW, handleH), trashBorder, filled: false, width: 1.2f);

        // 3 vertical lines inside the body
        float lineSpacing = bodyW / 4f;
        for (int i = 1; i <= 3; i++)
        {
            float lx = bodyX + lineSpacing * i;
            float lineTop = bodyY + 3f;
            float lineBottom = bodyY + bodyH - 3f;
            DrawLine(new Vector2(lx, lineTop), new Vector2(lx, lineBottom), trashLines, 1.0f);
        }
    }
}
