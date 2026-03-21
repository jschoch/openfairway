/// <summary>
/// Immutable configuration for a Closest to the Pin session.
/// </summary>
public sealed class ClosestToPinConfig
{
    public string Club { get; }
    public float TargetDistanceYards { get; }
    public float WinDistanceYards { get; }

    public ClosestToPinConfig(string club, float targetDistanceYards, float winDistanceYards)
    {
        Club = club;
        TargetDistanceYards = targetDistanceYards;
        WinDistanceYards = winDistanceYards;
    }
}
