using System;
using System.Collections.Generic;
using Godot;

/// <summary>A frozen CTP session kept for comparison after FreezeSession() is called.</summary>
public sealed class CtpSession
{
    public CtpSession(string label, ClosestToPinConfig config, List<ClosestToPinResult> results)
    {
        Label   = label;
        Config  = config;
        Results = results;
    }
    public string Label  { get; }
    public ClosestToPinConfig Config  { get; }
    public IReadOnlyList<ClosestToPinResult> Results { get; }
}

/// <summary>
/// Evaluates shots against a Closest to the Pin configuration.
///
/// Call Activate() to begin a session, then AddTrace() for each incoming shot.
/// FreezeSession() archives the current results and starts a new empty session.
/// The Results list is re-ranked after every addition.
/// </summary>
public sealed class ClosestToPinEvaluator
{
    private const float MetersToYards = 1f / 0.9144f;

    private readonly List<ClosestToPinResult> _results = new();
    private readonly List<CtpSession> _archivedSessions = new();

    public event Action ResultsChanged;

    public bool IsActive { get; private set; }
    public ClosestToPinConfig Config { get; private set; }
    public IReadOnlyList<ClosestToPinResult> Results => _results;
    /// <summary>Previously frozen sessions, oldest first.</summary>
    public IReadOnlyList<CtpSession> ArchivedSessions => _archivedSessions;

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

    /// <summary>Clears active session results. Archived sessions are unaffected.</summary>
    public void Clear()
    {
        _results.Clear();
        ResultsChanged?.Invoke();
    }

    /// <summary>
    /// Archives the current results as a named session and resets the active list.
    /// Session count is 1-based: "Session 1", "Session 2", …
    /// </summary>
    public void FreezeSession(string label = null)
    {
        if (!IsActive || Config == null) return;
        int num = _archivedSessions.Count + 1;
        string sessionLabel = string.IsNullOrWhiteSpace(label) ? $"Session {num}" : label;
        _archivedSessions.Add(new CtpSession(sessionLabel, Config, new List<ClosestToPinResult>(_results)));
        _results.Clear();
        ResultsChanged?.Invoke();
    }

    /// <summary>Clears the active session results AND all archived sessions.</summary>
    public void ClearAll()
    {
        _results.Clear();
        _archivedSessions.Clear();
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
