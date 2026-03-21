using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// Evaluates shots against a Closest to the Pin configuration.
///
/// Call Activate() to begin a session, then AddTrace() for each incoming shot.
/// The Results list is re-ranked after every addition.
/// </summary>
public sealed class ClosestToPinEvaluator
{
    private const float MetersToYards = 1f / 0.9144f;

    private readonly List<ClosestToPinResult> _results = new();

    public event Action ResultsChanged;

    public bool IsActive { get; private set; }
    public ClosestToPinConfig Config { get; private set; }
    public IReadOnlyList<ClosestToPinResult> Results => _results;

    public void Activate(ClosestToPinConfig config)
    {
        Config = config ?? throw new ArgumentNullException(nameof(config));
        _results.Clear();
        IsActive = true;
        ResultsChanged?.Invoke();
    }

    public void Deactivate()
    {
        IsActive = false;
        ResultsChanged?.Invoke();
    }

    public void Clear()
    {
        _results.Clear();
        ResultsChanged?.Invoke();
    }

    /// <summary>
    /// Evaluates a shot trace and adds it to the session results.
    /// LandingPoint is in Godot's coordinate system: X = forward (carry), Z = lateral (offline).
    /// </summary>
    public void AddTrace(RangeSpikeShotTrace trace)
    {
        if (!IsActive || Config == null || trace == null) return;

        float carryYards = trace.LandingPoint.X * MetersToYards;
        float offlineYards = trace.LandingPoint.Z * MetersToYards;

        float carryDelta = carryYards - Config.TargetDistanceYards;
        float distToTarget = Mathf.Sqrt(carryDelta * carryDelta + offlineYards * offlineYards);

        var result = new ClosestToPinResult
        {
            ShotNumber = _results.Count + 1,
            DistanceToTargetYards = distToTarget,
            CarryDeltaYards = carryDelta,
            OfflineYards = offlineYards,
            IsHit = distToTarget <= Config.WinDistanceYards,
            Rank = 0
        };

        _results.Add(result);
        Rerank();
        ResultsChanged?.Invoke();
    }

    private void Rerank()
    {
        // Sort by distance ascending; break ties by shot number (earlier = better rank)
        _results.Sort((a, b) =>
        {
            int cmp = a.DistanceToTargetYards.CompareTo(b.DistanceToTargetYards);
            return cmp != 0 ? cmp : a.ShotNumber.CompareTo(b.ShotNumber);
        });

        for (int i = 0; i < _results.Count; i++)
            _results[i].Rank = i + 1;

        // Restore original display order (by shot number) for the scoreboard
        _results.Sort((a, b) => a.ShotNumber.CompareTo(b.ShotNumber));
    }
}
