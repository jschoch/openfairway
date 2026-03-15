using System.Collections.Generic;
using NUnit.Framework;

public class SpikeTileLayoutEngineTests
{
    [Test]
    public void TryResize_RejectsOverlap()
    {
        var engine = new SpikeTileLayoutEngine(columns: 4, maxRows: 4);
        var tiles = new List<SpikeTileSpec>
        {
            new("a", "A", 0, 0, 1, 1),
            new("b", "B", 1, 0, 1, 1),
        };

        bool result = engine.TryResize(tiles, "a", 2, 1, out string reason);

        Assert.That(result, Is.False);
        Assert.That(reason, Does.Contain("overlaps"));
        Assert.That(tiles[0].ColumnSpan, Is.EqualTo(1));
    }

    [Test]
    public void TryResize_AllowsExpansionIntoFreeCells()
    {
        var engine = new SpikeTileLayoutEngine(columns: 4, maxRows: 4);
        var tiles = new List<SpikeTileSpec>
        {
            new("a", "A", 0, 0, 1, 1),
            new("b", "B", 2, 0, 1, 1),
        };

        bool result = engine.TryResize(tiles, "a", 2, 2, out string reason);

        Assert.That(result, Is.True, reason);
        Assert.That(tiles[0].ColumnSpan, Is.EqualTo(2));
        Assert.That(tiles[0].RowSpan, Is.EqualTo(2));
    }

    [Test]
    public void TryMove_RejectsOutOfBoundsPlacement()
    {
        var engine = new SpikeTileLayoutEngine(columns: 3, maxRows: 3);
        var tiles = new List<SpikeTileSpec>
        {
            new("a", "A", 0, 0, 2, 1),
        };

        bool result = engine.TryMove(tiles, "a", 2, 2, out string reason);

        Assert.That(result, Is.False);
        Assert.That(reason, Does.Contain("right edge").Or.Contain("bottom edge"));
    }

    [Test]
    public void TryMove_RejectsOverlap()
    {
        var engine = new SpikeTileLayoutEngine(columns: 4, maxRows: 4);
        var tiles = new List<SpikeTileSpec>
        {
            new("a", "A", 0, 0, 1, 1),
            new("b", "B", 2, 1, 2, 2),
        };

        bool result = engine.TryMove(tiles, "a", 2, 2, out string reason);

        Assert.That(result, Is.False);
        Assert.That(reason, Does.Contain("overlaps"));
        Assert.That(tiles[0].Column, Is.EqualTo(0));
        Assert.That(tiles[0].Row, Is.EqualTo(0));
    }
}
