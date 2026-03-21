using System.Collections.Generic;
using Godot;

public sealed class RangeSpikeShotTrace
{
    public RangeSpikeShotTrace(string setLabel, string shotLabel, string clubLabel, Color color, List<Vector3> points)
    {
        SetLabel = setLabel;
        ShotLabel = shotLabel;
        ClubLabel = clubLabel;
        Color = color;
        DisplayColor = color;
        Points = points ?? new List<Vector3>();
        LandingPoint = Points.Count == 0 ? Vector3.Zero : Points[Points.Count - 1];
    }

    public string SetLabel { get; }
    public string ShotLabel { get; }
    public string ClubLabel { get; }
    // Palette color assigned at creation (never changes).
    public Color Color { get; }
    // Rendering color — can be overridden to highlight the most-recent shot.
    public Color DisplayColor { get; set; }
    public List<Vector3> Points { get; }
    public Vector3 LandingPoint { get; }
    // Launch parameters — set by factory methods, 0 if unknown (e.g. older traces).
    public float SpeedMph { get; set; }
    public float LaunchAngleDeg { get; set; }   // VLA
    public float DirectionDeg { get; set; }      // HLA
    public float BackspinRpm { get; set; }
    public float SidespinRpm { get; set; }
    // Launch-monitor reported carry distance (yards). 0 if not from a real LM.
    public float LmCarryDistanceYd { get; set; }
    // Smash factor (ball speed / club head speed) reported by the LM. 0 = not available.
    public float SmashFactor { get; set; }
}

public sealed class RangeSpikeShotSet
{
    public RangeSpikeShotSet(string label, RangeSpikeShotPreset preset, List<RangeSpikeShotTrace> traces)
    {
        Label = label;
        Preset = preset;
        Traces = traces ?? new List<RangeSpikeShotTrace>();
    }

    public string Label { get; }
    public RangeSpikeShotPreset Preset { get; }
    public List<RangeSpikeShotTrace> Traces { get; }

    /// <summary>
    /// User-assigned tag for this set (e.g. "7I stock", "draw shot").
    /// Empty means no tag — the Label is shown as-is.
    /// </summary>
    public string Tag { get; set; } = string.Empty;

    /// <summary>Display name: tag if set, otherwise the generated label.</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(Tag) ? Label : Tag;
}

public sealed class RangeSpikeTrajectorySimulator
{
    private const float StartHeight = 0.02f;
    private const float MaxTime = 12.0f;
    private const float DefaultTempF = 75.0f;
    private const float DefaultAltitudeFt = 0.0f;
    private const float FeetPerMeter = ShotSetup.FEET_PER_METER;
    private const float YardsPerMeter = ShotSetup.YARDS_PER_METER;

    private readonly ShotSetup _shotSetup = new();
    private readonly Aerodynamics _aerodynamics = new();
    // Uses the same default profile as PhysicsAdapter so regime-calibrated
    // drag/lift overrides (e.g. drag×0.82 for slow irons) are applied.
    private readonly BallPhysicsProfile _ballProfile = new();

    // Variation constants (uniform ±amplitude):
    //   Ball speed   : ±3.5 mph
    //   VLA          : ±1.3°
    //   HLA          : ±1.7°
    //   Backspin     : ±8% of nominal
    //   Sidespin     : ±220 rpm
    public (float speed, float vla, float hla, float backspin, float sidespin) RandomizeParams(
        RangeSpikeShotPreset preset, int seed)
    {
        var rng = new RandomNumberGenerator();
        rng.Seed = (ulong)seed;
        return (
            preset.SpeedMph        + NextCentered(rng, 3.5f),
            preset.LaunchAngleDeg  + NextCentered(rng, 1.3f),
            preset.LaunchDirectionDeg + NextCentered(rng, 1.7f),
            Mathf.Max(200.0f, preset.BackspinRpm + NextCentered(rng, preset.BackspinRpm * 0.08f)),
            preset.SidespinRpm     + NextCentered(rng, 220.0f)
        );
    }

    public RangeSpikeShotTrace GenerateShotTrace(RangeSpikeShotPreset preset, string setLabel, int shotIndex, int seed)
    {
        var (speed, vla, hla, backspin, sidespin) = RandomizeParams(preset, seed);
        string shotLabel = $"{preset.ClubLabel} #{shotIndex}";
        var trace = new RangeSpikeShotTrace(setLabel, shotLabel, preset.ClubLabel, preset.Color,
            SimulateFlight(speed, vla, hla, backspin, sidespin));
        trace.SpeedMph = speed; trace.LaunchAngleDeg = vla; trace.DirectionDeg = hla;
        trace.BackspinRpm = backspin; trace.SidespinRpm = sidespin;
        return trace;
    }

    public RangeSpikeShotTrace GenerateShotTraceFromParams(
        string setLabel, int shotIndex, string clubLabel, Color color,
        float speedMph, float launchAngleDeg, float directionDeg, float backspinRpm, float sidespinRpm)
    {
        string shotLabel = $"{clubLabel} #{shotIndex}";
        var trace = new RangeSpikeShotTrace(setLabel, shotLabel, clubLabel, color,
            SimulateFlight(speedMph, launchAngleDeg, directionDeg, backspinRpm, sidespinRpm));
        trace.SpeedMph = speedMph; trace.LaunchAngleDeg = launchAngleDeg; trace.DirectionDeg = directionDeg;
        trace.BackspinRpm = backspinRpm; trace.SidespinRpm = sidespinRpm;
        return trace;
    }

    // Extracts launch parameters from a TcpServer HitBall payload (the BallData sub-dict).
    // Handles BackSpin/SideSpin or TotalSpin/SpinAxis interchangeably via ShotSetup.ParseSpin.
    public (float speed, float vla, float hla, float backspin, float sidespin) ExtractTcpParams(
        Godot.Collections.Dictionary data)
    {
        float speed = data.TryGetValue("Speed", out var sv) ? sv.AsSingle() : 0f;
        float vla   = data.TryGetValue("VLA",   out var vv) ? vv.AsSingle() : 0f;
        float hla   = data.TryGetValue("HLA",   out var hv) ? hv.AsSingle() : 0f;
        var spin    = _shotSetup.ParseSpin(data, emitConsistencyWarnings: false);
        float backspin = spin.TryGetValue("backspin", out var bs) ? bs.AsSingle() : 0f;
        float sidespin = spin.TryGetValue("sidespin", out var ss) ? ss.AsSingle() : 0f;
        return (speed, vla, hla, backspin, sidespin);
    }

    public RangeSpikeShotSet GenerateShotSet(RangeSpikeShotPreset preset, int shotCount, int seed)
    {
        var traces = new List<RangeSpikeShotTrace>();
        string setLabel = $"{preset.DisplayName} x{shotCount}";
        for (int i = 0; i < shotCount; i++)
            traces.Add(GenerateShotTrace(preset, setLabel, i + 1, seed + i));

        return new RangeSpikeShotSet(setLabel, preset, traces);
    }

    public string BuildSummary(IReadOnlyList<RangeSpikeShotSet> shotSets)
    {
        if (shotSets == null || shotSets.Count == 0)
            return "No shot sets loaded. Add a set to compare clubs and equipment variants.";

        var sb = new System.Text.StringBuilder();
        int totalShots = 0;

        foreach (RangeSpikeShotSet set in shotSets)
        {
            int n = set.Traces.Count;
            totalShots += n;
            string name = set.DisplayName;
            if (!string.IsNullOrWhiteSpace(set.Tag) && set.Tag != set.Label)
                name += $"  [{set.Label}]";
            sb.AppendLine($"{name}  ({n} shot{(n == 1 ? "" : "s")})");
        }

        sb.Append($"Total: {shotSets.Count} set{(shotSets.Count == 1 ? "" : "s")}, {totalShots} shot{(totalShots == 1 ? "" : "s")}");
        return sb.ToString();
    }

    private List<Vector3> SimulateFlight(float speedMph, float launchAngleDeg, float launchDirectionDeg, float backspinRpm, float sidespinRpm)
    {
        var launch = _shotSetup.BuildLaunchVectorsFromComponents(speedMph, launchAngleDeg, launchDirectionDeg, backspinRpm, sidespinRpm);
        Vector3 velocity = (Vector3)launch["velocity"];
        Vector3 omega = (Vector3)launch["omega"];
        var points = new List<Vector3> { new Vector3(0.0f, StartHeight, 0.0f) };

        float airDensity = _aerodynamics.GetAirDensity(DefaultAltitudeFt, DefaultTempF, PhysicsEnums.Units.Imperial);
        float airViscosity = _aerodynamics.GetDynamicViscosity(DefaultTempF, PhysicsEnums.Units.Imperial);

        // Resolve regime-calibrated drag/lift scales — mirrors PhysicsAdapter.SimulateCarryOnlyInternal.
        float totalSpinRpm = Mathf.Sqrt(backspinRpm * backspinRpm + sidespinRpm * sidespinRpm);
        RegimeScaleOverride regimeScale = _ballProfile.ResolveScaleOverride(
            speedMph, launchAngleDeg, totalSpinRpm, out _, out _);
        float dragScale = _ballProfile.DragScaleMultiplier * regimeScale.DragScaleMultiplier;
        float liftScale = _ballProfile.LiftScaleMultiplier * regimeScale.LiftScaleMultiplier;
        FlightProfile fp = _ballProfile.ResolvedFlight;

        Vector3 position = points[0];
        int maxSteps = Mathf.RoundToInt(MaxTime / BallPhysics.SIMULATION_DT);
        for (int i = 0; i < maxSteps; i++)
        {
            FlightAerodynamicsSample sample = BallPhysics.SampleFlightAerodynamics(
                velocity, omega, airDensity, airViscosity,
                dragScale, liftScale, launchAngleDeg, fp);

            Vector3 gravity = new(0.0f, -9.81f * BallPhysics.MASS, 0.0f);
            Vector3 airForces = Vector3.Zero;
            if (sample.HasAerodynamics)
            {
                Vector3 drag = -0.5f * sample.DragCoefficient * airDensity * BallPhysics.CROSS_SECTION * velocity * sample.Speed;
                Vector3 magnus = Vector3.Zero;
                float omegaLength = omega.Length();
                if (omegaLength > 0.1f)
                {
                    Vector3 omegaCrossVelocity = omega.Cross(velocity);
                    magnus = 0.5f * sample.LiftCoefficient * airDensity * BallPhysics.CROSS_SECTION * omegaCrossVelocity * sample.Speed / omegaLength;
                }

                airForces = drag + magnus;
            }

            Vector3 force = gravity + airForces;
            Vector3 torque = -BallPhysics.MOMENT_OF_INERTIA * omega / BallPhysics.SPIN_DECAY_TAU;
            velocity += (force / BallPhysics.MASS) * BallPhysics.SIMULATION_DT;
            omega += (torque / BallPhysics.MOMENT_OF_INERTIA) * BallPhysics.SIMULATION_DT;
            position += velocity * BallPhysics.SIMULATION_DT;

            if (position.Y <= 0.0f && velocity.Y < 0.0f)
            {
                position.Y = 0.0f;
                points.Add(position);
                break;
            }

            if (i % 2 == 0)
                points.Add(position);
        }

        if (points.Count == 1)
            points.Add(new Vector3(0.0f, 0.0f, 0.0f));

        return points;
    }

    private static float NextCentered(RandomNumberGenerator rng, float amplitude)
    {
        return (rng.Randf() - 0.5f) * 2.0f * amplitude;
    }
}
