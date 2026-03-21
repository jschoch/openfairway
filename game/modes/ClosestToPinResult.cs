/// <summary>
/// Evaluation result for a single shot in a Closest to the Pin session.
/// </summary>
public sealed class ClosestToPinResult
{
    /// <summary>1-based shot number within the session.</summary>
    public int ShotNumber { get; set; }

    /// <summary>
    /// Straight-line 2D distance from the landing spot to the target point (yards).
    /// distanceToTarget = sqrt(carryDelta² + offline²)
    /// </summary>
    public float DistanceToTargetYards { get; set; }

    /// <summary>
    /// Carry minus target distance (yards). Positive = long, negative = short.
    /// </summary>
    public float CarryDeltaYards { get; set; }

    /// <summary>
    /// Lateral distance from the target line (yards). Positive = right, negative = left.
    /// </summary>
    public float OfflineYards { get; set; }

    /// <summary>True when DistanceToTargetYards ≤ WinDistanceYards.</summary>
    public bool IsHit { get; set; }

    /// <summary>Rank within the session — 1 = closest to the pin.</summary>
    public int Rank { get; set; }
}
