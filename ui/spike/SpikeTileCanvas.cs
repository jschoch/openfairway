using System;
using System.Collections.Generic;
using Godot;

public partial class SpikeTileCanvas : Control
{
    [Signal]
    public delegate void StatusChangedEventHandler(string message);

    [Signal]
    public delegate void TileDeleteRequestedEventHandler(string tileId);

    // Emitted after a drag or resize is committed successfully.
    // Connect to trigger layout persistence.
    [Signal]
    public delegate void TileLayoutChangedEventHandler();

    private const float Gap = 12.0f;
    private const float HeaderHeight = 30.0f;
    private const float ResizeOverlaySize = 38.0f;
    private const float DeleteOverlaySize = 38.0f;

    private int _gridColumns = 4;
    private int _gridRows = 5;

    private readonly List<SpikeTileSpec> _tiles = new();
    private readonly Dictionary<string, SpikeTilePanel> _panelsById = new();
    private readonly Dictionary<string, Control> _contentById = new();
    private readonly Dictionary<string, Control> _resizeHandlesById = new();
    private readonly Dictionary<string, Control> _deleteHandlesById = new();
    private SpikeTileLayoutEngine _layoutEngine;
    private ActiveInteraction _activeInteraction = ActiveInteraction.None;
    private SpikeTileSpec _activeTile;
    private SpikeTilePanel _activePanel;
    private Vector2 _interactionStartMouse;
    private int _interactionStartColumn;
    private int _interactionStartRow;
    private int _interactionStartColumnSpan;
    private int _interactionStartRowSpan;

    private enum ActiveInteraction
    {
        None,
        Move,
        Resize
    }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        ClipContents = true;
        _layoutEngine = new SpikeTileLayoutEngine(_gridColumns, _gridRows, maxColumnSpan: _gridColumns, maxRowSpan: _gridRows);
    }

    public void ConfigureGrid(int columns, int rows)
    {
        _gridColumns = Mathf.Max(1, columns);
        _gridRows = Mathf.Max(1, rows);
        _layoutEngine = new SpikeTileLayoutEngine(_gridColumns, _gridRows, maxColumnSpan: _gridColumns, maxRowSpan: _gridRows);
        ApplyTileLayout();
        QueueRedraw();
    }

    public void ConfigureTiles(IEnumerable<SpikeTileSpec> tiles, Dictionary<string, Control> contentById)
    {
        foreach (Node child in GetChildren())
            child.QueueFree();

        _tiles.Clear();
        _panelsById.Clear();
        _contentById.Clear();
        _resizeHandlesById.Clear();
        _deleteHandlesById.Clear();

        // Build panels first.
        foreach (SpikeTileSpec tile in tiles)
        {
            _tiles.Add(tile);
            SpikeTilePanel panel = BuildTilePanel(tile);
            _panelsById[tile.Id] = panel;
            AddChild(panel);
        }

        // Add content into each panel body.
        foreach (KeyValuePair<string, Control> pair in contentById)
        {
            _contentById[pair.Key] = pair.Value;
            if (_panelsById.TryGetValue(pair.Key, out SpikeTilePanel panel))
            {
                Control body = panel.GetNode<Control>("Margin/Root/Body");
                if (pair.Value.GetParent() != null)
                    pair.Value.Reparent(body);
                else
                    body.AddChild(pair.Value);

                pair.Value.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                pair.Value.SizeFlagsVertical = SizeFlags.ExpandFill;
                ApplyPassiveMouseFilter(pair.Value);
            }
        }

        // Add canvas-level resize handles on top of everything (highest z-order).
        foreach (SpikeTileSpec tile in tiles)
        {
            Control handle = BuildResizeHandle(tile.Id);
            _resizeHandlesById[tile.Id] = handle;
            AddChild(handle);
        }

        // Add canvas-level delete handles.
        foreach (SpikeTileSpec tile in tiles)
        {
            Control handle = BuildDeleteHandle(tile.Id);
            _deleteHandlesById[tile.Id] = handle;
            AddChild(handle);
        }

        ApplyTileLayout();
        EnsureHandlesOnTop();
        QueueRedraw();
    }

    public override void _Draw()
    {
        DrawRect(GetRect(), new Color("050608"), filled: true);
        float cw = GetCellWidth();
        float ch = GetCellHeight();
        Color gridColor = new Color("11161d");

        for (int col = 0; col <= _gridColumns; col++)
        {
            float x = Gap * 0.5f + col * (cw + Gap);
            DrawLine(new Vector2(x, 0), new Vector2(x, Size.Y), gridColor, 1.0f);
        }

        for (int row = 0; row <= _gridRows; row++)
        {
            float y = Gap * 0.5f + row * (ch + Gap);
            DrawLine(new Vector2(0, y), new Vector2(Size.X, y), gridColor, 1.0f);
        }
    }

    public override void _Notification(int what)
    {
        if (what == NotificationResized)
        {
            ApplyTileLayout();
            QueueRedraw();
        }
    }

    public override void _Input(InputEvent @event)
    {
        if (_activeInteraction == ActiveInteraction.None || _activeTile == null || _activePanel == null)
            return;

        if (@event is InputEventMouseMotion mouseMotion)
        {
            UpdateInteractionPreview(mouseMotion.GlobalPosition);
            GetViewport().SetInputAsHandled();
            return;
        }

        if (@event is InputEventMouseButton mouseButton
            && mouseButton.ButtonIndex == MouseButton.Left
            && !mouseButton.Pressed)
        {
            CommitInteraction(mouseButton.GlobalPosition);
            GetViewport().SetInputAsHandled();
        }
    }

    private SpikeTilePanel BuildTilePanel(SpikeTileSpec tile)
    {
        var panel = new SpikeTilePanel
        {
            Name = tile.Id,
            TileId = tile.Id,
            Theme = new Theme()
        };

        panel.DragStarted += OnPanelDragStarted;
        panel.AddThemeStyleboxOverride("panel", BuildPanelStylebox(new Color("24303c")));

        var margin = new MarginContainer { Name = "Margin" };
        margin.MouseFilter = MouseFilterEnum.Ignore;
        margin.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        margin.SizeFlagsVertical = SizeFlags.ExpandFill;
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", 12);
        margin.AddThemeConstantOverride("margin_top", 10);
        margin.AddThemeConstantOverride("margin_right", 12);
        margin.AddThemeConstantOverride("margin_bottom", 10);
        panel.AddChild(margin);

        var root = new VBoxContainer { Name = "Root" };
        root.MouseFilter = MouseFilterEnum.Ignore;
        root.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        root.SizeFlagsVertical = SizeFlags.ExpandFill;
        root.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        root.AddThemeConstantOverride("separation", 8);
        margin.AddChild(root);

        var header = new HBoxContainer { Name = "Header" };
        header.MouseFilter = MouseFilterEnum.Ignore;
        header.CustomMinimumSize = new Vector2(0.0f, HeaderHeight);
        root.AddChild(header);

        var title = new Label
        {
            Name = "Title",
            Text = tile.Title,
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        title.MouseFilter = MouseFilterEnum.Ignore;
        title.AddThemeColorOverride("font_color", new Color("f4f7fb"));
        title.AddThemeFontSizeOverride("font_size", 16);
        header.AddChild(title);

        var body = new MarginContainer
        {
            Name = "Body",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(120.0f, 100.0f)
        };
        body.MouseFilter = MouseFilterEnum.Ignore;
        body.ClipContents = true;
        root.AddChild(body);

        return panel;
    }

    private Control BuildResizeHandle(string tileId)
    {
        var handle = new Control
        {
            Name = $"Resize_{tileId}",
            MouseFilter = MouseFilterEnum.Stop,
            ZIndex = 100,
            MouseDefaultCursorShape = CursorShape.Fdiagsize
        };

        if (_panelsById.TryGetValue(tileId, out SpikeTilePanel hoveredPanel))
        {
            handle.MouseEntered += () => hoveredPanel.SetResizeHovered(true);
            handle.MouseExited += () => hoveredPanel.SetResizeHovered(false);
        }

        handle.GuiInput += (InputEvent ev) =>
        {
            if (ev is InputEventMouseButton btn && btn.ButtonIndex == MouseButton.Left && btn.Pressed)
            {
                if (_panelsById.TryGetValue(tileId, out SpikeTilePanel panel))
                    OnPanelResizeStarted(panel, btn.GlobalPosition);
                handle.AcceptEvent();
            }
        };

        return handle;
    }

    private Control BuildDeleteHandle(string tileId)
    {
        var handle = new Control
        {
            Name = $"Delete_{tileId}",
            MouseFilter = MouseFilterEnum.Stop,
            ZIndex = 100,
            MouseDefaultCursorShape = CursorShape.Forbidden
        };

        if (_panelsById.TryGetValue(tileId, out SpikeTilePanel hoveredPanel))
        {
            handle.MouseEntered += () => hoveredPanel.SetDeleteHovered(true);
            handle.MouseExited += () => hoveredPanel.SetDeleteHovered(false);
        }

        handle.GuiInput += (InputEvent ev) =>
        {
            if (ev is InputEventMouseButton btn && btn.ButtonIndex == MouseButton.Left && btn.Pressed)
            {
                EmitSignal(SignalName.TileDeleteRequested, tileId);
                handle.AcceptEvent();
            }
        };

        return handle;
    }

    // After any interaction that reorders children, push all resize and delete
    // handles back to the end of the sibling list so they always win input focus.
    private void EnsureHandlesOnTop()
    {
        foreach (Control handle in _resizeHandlesById.Values)
            MoveChild(handle, GetChildCount() - 1);
        foreach (Control handle in _deleteHandlesById.Values)
            MoveChild(handle, GetChildCount() - 1);
    }

    public void RemoveTile(string tileId)
    {
        SpikeTileSpec tileToRemove = null;
        foreach (SpikeTileSpec tile in _tiles)
        {
            if (tile.Id == tileId)
            {
                tileToRemove = tile;
                break;
            }
        }

        if (tileToRemove == null)
            return;

        _tiles.Remove(tileToRemove);

        if (_panelsById.TryGetValue(tileId, out SpikeTilePanel panel))
        {
            panel.QueueFree();
            _panelsById.Remove(tileId);
        }

        if (_resizeHandlesById.TryGetValue(tileId, out Control resizeHandle))
        {
            resizeHandle.QueueFree();
            _resizeHandlesById.Remove(tileId);
        }

        if (_deleteHandlesById.TryGetValue(tileId, out Control deleteHandle))
        {
            deleteHandle.QueueFree();
            _deleteHandlesById.Remove(tileId);
        }

        _contentById.Remove(tileId);

        ApplyTileLayout();
        EnsureHandlesOnTop();
        QueueRedraw();
    }

    private void OnPanelDragStarted(SpikeTilePanel panel, Vector2 globalMousePosition)
    {
        BeginInteraction(panel, globalMousePosition, ActiveInteraction.Move);
    }

    private void OnPanelResizeStarted(SpikeTilePanel panel, Vector2 globalMousePosition)
    {
        BeginInteraction(panel, globalMousePosition, ActiveInteraction.Resize);
    }

    private void BeginInteraction(SpikeTilePanel panel, Vector2 globalMousePosition, ActiveInteraction mode)
    {
        SpikeTileSpec tile = FindTile(panel.TileId);
        if (tile == null)
            return;

        _activeInteraction = mode;
        _activeTile = tile;
        _activePanel = panel;
        _interactionStartMouse = globalMousePosition;
        _interactionStartColumn = tile.Column;
        _interactionStartRow = tile.Row;
        _interactionStartColumnSpan = tile.ColumnSpan;
        _interactionStartRowSpan = tile.RowSpan;
        MoveChild(panel, GetChildCount() - 1);
        panel.AddThemeStyleboxOverride("panel", BuildPanelStylebox(new Color("6ab8ff")));
        EmitSignal(SignalName.StatusChanged, mode == ActiveInteraction.Move
            ? $"Dragging {tile.Title}. Release to snap into the grid."
            : $"Resizing {tile.Title}. Drag the lower-right handle to snap span.");
    }

    private void UpdateInteractionPreview(Vector2 globalMousePosition)
    {
        if (_activeTile == null || _activePanel == null)
            return;

        Vector2 step = GetCellStep();
        int columnDelta = Mathf.RoundToInt((globalMousePosition.X - _interactionStartMouse.X) / step.X);
        int rowDelta = Mathf.RoundToInt((globalMousePosition.Y - _interactionStartMouse.Y) / step.Y);

        if (_activeInteraction == ActiveInteraction.Move)
        {
            int previewColumn = _interactionStartColumn + columnDelta;
            int previewRow = _interactionStartRow + rowDelta;
            Rect2 previewRect = GetGridRect(previewColumn, previewRow, _interactionStartColumnSpan, _interactionStartRowSpan);
            _activePanel.Position = previewRect.Position;
            _activePanel.Size = previewRect.Size;

            bool valid = _layoutEngine.CanOccupy(
                _tiles,
                _activeTile.Id,
                previewColumn,
                previewRow,
                _interactionStartColumnSpan,
                _interactionStartRowSpan,
                out string reason);
            ApplyPreviewStyle(valid);
            if (!valid)
                EmitSignal(SignalName.StatusChanged, reason);
            return;
        }

        int previewColumnSpan = Mathf.Clamp(_interactionStartColumnSpan + columnDelta, _layoutEngine.MinSpan, _layoutEngine.MaxColumnSpan);
        int previewRowSpan = Mathf.Clamp(_interactionStartRowSpan + rowDelta, _layoutEngine.MinSpan, _layoutEngine.MaxRowSpan);
        Rect2 resizeRect = GetGridRect(_interactionStartColumn, _interactionStartRow, previewColumnSpan, previewRowSpan);
        _activePanel.Position = resizeRect.Position;
        _activePanel.Size = resizeRect.Size;

        bool resizeValid = _layoutEngine.CanOccupy(
            _tiles,
            _activeTile.Id,
            _interactionStartColumn,
            _interactionStartRow,
            previewColumnSpan,
            previewRowSpan,
            out string resizeReason);
        ApplyPreviewStyle(resizeValid);
        if (!resizeValid)
            EmitSignal(SignalName.StatusChanged, resizeReason);
    }

    private void CommitInteraction(Vector2 globalMousePosition)
    {
        if (_activeTile == null || _activePanel == null)
            return;

        Vector2 step = GetCellStep();
        int columnDelta = Mathf.RoundToInt((globalMousePosition.X - _interactionStartMouse.X) / step.X);
        int rowDelta = Mathf.RoundToInt((globalMousePosition.Y - _interactionStartMouse.Y) / step.Y);

        bool changed;
        string reason;
        if (_activeInteraction == ActiveInteraction.Move)
        {
            changed = _layoutEngine.TryMove(
                _tiles,
                _activeTile.Id,
                _interactionStartColumn + columnDelta,
                _interactionStartRow + rowDelta,
                out reason);
            if (changed)
                EmitSignal(SignalName.StatusChanged, $"{_activeTile.Title} moved to {_activeTile.Column + 1},{_activeTile.Row + 1}.");
        }
        else
        {
            changed = _layoutEngine.TryResize(
                _tiles,
                _activeTile.Id,
                _interactionStartColumnSpan + columnDelta,
                _interactionStartRowSpan + rowDelta,
                out reason);
            if (changed)
                EmitSignal(SignalName.StatusChanged, $"{_activeTile.Title} resized to {_activeTile.ColumnSpan}x{_activeTile.RowSpan}.");
        }

        if (changed)
            EmitSignal(SignalName.TileLayoutChanged);
        else
            EmitSignal(SignalName.StatusChanged, reason);

        _activeInteraction = ActiveInteraction.None;
        _activeTile = null;
        _activePanel = null;
        ApplyTileLayout();
        EnsureHandlesOnTop();
    }

    private void ApplyTileLayout()
    {
        if (_layoutEngine == null || _panelsById.Count == 0)
            return;

        foreach (SpikeTileSpec tile in _tiles)
        {
            if (!_panelsById.TryGetValue(tile.Id, out SpikeTilePanel panel))
                continue;

            Rect2 rect = GetGridRect(tile.Column, tile.Row, tile.ColumnSpan, tile.RowSpan);
            panel.Position = rect.Position;
            panel.Size = rect.Size;
            panel.AddThemeStyleboxOverride("panel", BuildPanelStylebox(new Color("24303c")));
            panel.QueueRedraw();

            // Position the resize handle overlay at the bottom-right of this tile.
            if (_resizeHandlesById.TryGetValue(tile.Id, out Control resizeHandle))
            {
                resizeHandle.Position = new Vector2(rect.End.X - ResizeOverlaySize, rect.End.Y - ResizeOverlaySize);
                resizeHandle.Size = new Vector2(ResizeOverlaySize, ResizeOverlaySize);
            }

            // Position the delete handle overlay at the bottom-left of this tile.
            if (_deleteHandlesById.TryGetValue(tile.Id, out Control deleteHandle))
            {
                deleteHandle.Position = new Vector2(rect.Position.X, rect.End.Y - DeleteOverlaySize);
                deleteHandle.Size = new Vector2(DeleteOverlaySize, DeleteOverlaySize);
            }
        }
    }

    // Cell width and height are computed independently to fill all available canvas space.
    private float GetCellWidth() => Mathf.Max(60.0f, (Size.X - Gap * (_gridColumns + 1)) / _gridColumns);
    private float GetCellHeight() => Mathf.Max(60.0f, (Size.Y - Gap * (_gridRows + 1)) / _gridRows);
    private Vector2 GetCellStep() => new(GetCellWidth() + Gap, GetCellHeight() + Gap);

    private Rect2 GetGridRect(int column, int row, int columnSpan, int rowSpan)
    {
        float cw = GetCellWidth();
        float ch = GetCellHeight();
        float width = columnSpan * cw + (columnSpan - 1) * Gap;
        float height = rowSpan * ch + (rowSpan - 1) * Gap;
        float x = Gap + column * (cw + Gap);
        float y = Gap + row * (ch + Gap);
        return new Rect2(x, y, width, height);
    }

    private void ApplyPreviewStyle(bool valid)
    {
        if (_activePanel == null)
            return;

        _activePanel.AddThemeStyleboxOverride("panel", BuildPanelStylebox(valid ? new Color("6ab8ff") : new Color("ff7b72")));
        _activePanel.QueueRedraw();
    }

    private static StyleBoxFlat BuildPanelStylebox(Color borderColor)
    {
        return new StyleBoxFlat
        {
            BgColor = new Color("0c1117"),
            BorderColor = borderColor,
            BorderWidthBottom = 2,
            BorderWidthLeft = 2,
            BorderWidthRight = 2,
            BorderWidthTop = 2,
            CornerRadiusBottomLeft = 8,
            CornerRadiusBottomRight = 8,
            CornerRadiusTopLeft = 8,
            CornerRadiusTopRight = 8
        };
    }

    private static void ApplyPassiveMouseFilter(Control root)
    {
        if (root is BaseButton)
            return;

        root.MouseFilter = MouseFilterEnum.Ignore;
        foreach (Node child in root.GetChildren())
        {
            if (child is Control childControl)
                ApplyPassiveMouseFilter(childControl);
        }
    }

    private SpikeTileSpec FindTile(string tileId)
    {
        foreach (SpikeTileSpec tile in _tiles)
        {
            if (tile.Id == tileId)
                return tile;
        }

        return null;
    }
}
