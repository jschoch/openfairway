using System;
using System.Collections.Generic;

public sealed class SpikeTileSpec
{
    public SpikeTileSpec(string id, string title, int column, int row, int columnSpan = 1, int rowSpan = 1)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Title = title ?? id;
        Column = column;
        Row = row;
        ColumnSpan = columnSpan;
        RowSpan = rowSpan;
    }

    public string Id { get; }
    public string Title { get; set; }
    public int Column { get; set; }
    public int Row { get; set; }
    public int ColumnSpan { get; set; }
    public int RowSpan { get; set; }
}

public sealed class SpikeTileLayoutEngine
{
    public SpikeTileLayoutEngine(int columns, int maxRows, int minSpan = 1, int maxColumnSpan = 4, int maxRowSpan = 4)
    {
        Columns = Math.Max(1, columns);
        MaxRows = Math.Max(1, maxRows);
        MinSpan = Math.Max(1, minSpan);
        MaxColumnSpan = Math.Max(MinSpan, maxColumnSpan);
        MaxRowSpan = Math.Max(MinSpan, maxRowSpan);
    }

    public int Columns { get; }
    public int MaxRows { get; }
    public int MinSpan { get; }
    public int MaxColumnSpan { get; }
    public int MaxRowSpan { get; }

    public bool TryResize(IList<SpikeTileSpec> tiles, string tileId, int newColumnSpan, int newRowSpan, out string reason)
    {
        reason = string.Empty;
        SpikeTileSpec tile = FindTile(tiles, tileId);
        if (tile == null)
        {
            reason = $"Tile '{tileId}' was not found.";
            return false;
        }

        int clampedColumnSpan = Clamp(newColumnSpan, MinSpan, MaxColumnSpan);
        int clampedRowSpan = Clamp(newRowSpan, MinSpan, MaxRowSpan);
        if (!CanOccupy(tiles, tile.Id, tile.Column, tile.Row, clampedColumnSpan, clampedRowSpan, out reason))
            return false;

        tile.ColumnSpan = clampedColumnSpan;
        tile.RowSpan = clampedRowSpan;
        return true;
    }

    public bool TryMove(IList<SpikeTileSpec> tiles, string tileId, int newColumn, int newRow, out string reason)
    {
        reason = string.Empty;
        SpikeTileSpec tile = FindTile(tiles, tileId);
        if (tile == null)
        {
            reason = $"Tile '{tileId}' was not found.";
            return false;
        }

        if (!CanOccupy(tiles, tile.Id, newColumn, newRow, tile.ColumnSpan, tile.RowSpan, out reason))
            return false;

        tile.Column = newColumn;
        tile.Row = newRow;
        return true;
    }

    public bool CanOccupy(IList<SpikeTileSpec> tiles, string ignoredTileId, int column, int row, int columnSpan, int rowSpan, out string reason)
    {
        reason = string.Empty;

        if (column < 0 || row < 0)
        {
            reason = "Tiles must stay inside the grid.";
            return false;
        }

        if (columnSpan < MinSpan || rowSpan < MinSpan)
        {
            reason = "Tile span is below the minimum size.";
            return false;
        }

        if (column + columnSpan > Columns)
        {
            reason = "Tile would extend past the right edge.";
            return false;
        }

        if (row + rowSpan > MaxRows)
        {
            reason = "Tile would extend past the bottom edge.";
            return false;
        }

        foreach (SpikeTileSpec other in tiles)
        {
            if (other == null || other.Id == ignoredTileId)
                continue;

            if (RectsOverlap(column, row, columnSpan, rowSpan, other.Column, other.Row, other.ColumnSpan, other.RowSpan))
            {
                reason = $"Tile overlaps '{other.Title}'.";
                return false;
            }
        }

        return true;
    }

    private static SpikeTileSpec FindTile(IList<SpikeTileSpec> tiles, string tileId)
    {
        foreach (SpikeTileSpec tile in tiles)
        {
            if (tile?.Id == tileId)
                return tile;
        }

        return null;
    }

    private static bool RectsOverlap(
        int leftA,
        int topA,
        int widthA,
        int heightA,
        int leftB,
        int topB,
        int widthB,
        int heightB)
    {
        return leftA < leftB + widthB
            && leftA + widthA > leftB
            && topA < topB + heightB
            && topA + heightA > topB;
    }

    private static int Clamp(int value, int min, int max) => Math.Min(Math.Max(value, min), max);
}
