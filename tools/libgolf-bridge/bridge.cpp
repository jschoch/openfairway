// libgolf-bridge: CLI wrapper around libgolf FlightSimulator.
//
// Reads launch parameters from command-line flags, runs the simulation,
// and writes a JSON array of trajectory points to stdout.
//
// Output coordinate convention matches the OpenFairway physics engine:
//   X = carry distance forward (metres)
//   Y = height above ground (metres)
//   Z = lateral offline deviation (metres, positive = right)
//
// libgolf BallState position is in feet, axes:
//   position.x = lateral (positive right)
//   position.y = forward / carry
//   position.z = height
//
// Sidespin sign convention:
//   libgolf: positive = hook (curves left for RH golfer)
//   OpenFairway: positive sidespin = fade (curves right for RH golfer)
//   => negate sidespin when passing to libgolf

#include <libgolf.hpp>

#include <cmath>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <string>

static bool phaseIsAerial(const char* p) { return std::strcmp(p, "aerial") == 0; }

static float parseFlag(int argc, char** argv, const char* flag, float def)
{
    for (int i = 1; i + 1 < argc; i++)
        if (std::strcmp(argv[i], flag) == 0)
            return std::stof(argv[i + 1]);
    return def;
}

int main(int argc, char** argv)
{
    // ── Launch parameters ──────────────────────────────────────────────────
    float speedMph    = parseFlag(argc, argv, "--speed",      109.0f);
    float angleDeg    = parseFlag(argc, argv, "--angle",       20.5f);
    float dirDeg      = parseFlag(argc, argv, "--direction",    0.0f);
    float backspinRpm = parseFlag(argc, argv, "--backspin",  7000.0f);
    // Negate: libgolf positive=hook, OpenFairway positive=fade
    float sidespinRpm = -parseFlag(argc, argv, "--sidespin",  120.0f);

    // ── Atmospheric parameters ─────────────────────────────────────────────
    float tempF     = parseFlag(argc, argv, "--temp",      70.0f);
    float elevFt    = parseFlag(argc, argv, "--elevation",  0.0f);
    float windMph   = parseFlag(argc, argv, "--wind",       0.0f);
    float windDeg   = parseFlag(argc, argv, "--winddir",    0.0f);
    float humidity  = parseFlag(argc, argv, "--humidity",  50.0f);
    float pressure  = parseFlag(argc, argv, "--pressure",  29.92f);

    // ── Build libgolf objects ──────────────────────────────────────────────
    // golfBall: x0, y0, z0 (yards), exitSpeed (mph), launchAngle (deg),
    //           direction (deg), backspin (rpm), sidespin (rpm)
    const golfBall ball{0.0f, 0.0f, 0.0f, speedMph, angleDeg, dirDeg, backspinRpm, sidespinRpm};

    // atmosphericData: temp (°F), elevation (ft), windSpeed (mph),
    //                  windDir (deg), windHeight (ft), relHumidity (%), pressure (inHg)
    const atmosphericData atmos{tempF, elevFt, windMph, windDeg, 0.0f, humidity, pressure};

    GroundSurface ground;
    GolfBallPhysicsVariables physVars(ball, atmos);
    FlightSimulator sim(physVars, ball, atmos, ground);

    // fromLaunchParameters expects feet/second, not mph.
    float speedFps = speedMph * 1.46667f;
    BallState initialState = BallState::fromLaunchParameters(speedFps, angleDeg, dirDeg);
    sim.initialize(initialState);

    // ── Simulate and collect points ────────────────────────────────────────
    constexpr float FT_TO_M    = 0.3048f;
    constexpr float DT         = 0.01f;
    constexpr int   RECORD_EVERY = 2; // record every 2nd step (~50 Hz output)
    // Safety cap: 2000 steps × 0.01 s = 20 s of simulated flight.
    // No real golf shot exceeds ~12 s, so this is a guard against isComplete()
    // never returning true for edge-case parameters (which causes unbounded
    // memory growth in the json buffer and ultimately std::bad_alloc).
    constexpr int   MAX_STEPS  = 2000;

    // Pre-allocate output buffer (typical trajectory ~400 points, ~30 chars each)
    std::string json;
    json.reserve(16384);
    json += "{\"points\":[";

    // Record initial position
    {
        const auto& s = sim.getState();
        char buf[80];
        std::snprintf(buf, sizeof(buf), "[%.4f,%.4f,%.4f]",
            s.position[1] * FT_TO_M,   // [1]=y=carry  → our X
            s.position[2] * FT_TO_M,   // [2]=z=height → our Y
            s.position[0] * FT_TO_M);  // [0]=x=lateral→ our Z
        json += buf;
    }

    // Track aerial phase separately so we can record carry (first ground contact)
    // vs total distance (after roll).  getCurrentPhaseName() returns "AerialPhase",
    // "BouncePhase", or "RollPhase".
    bool inAerial = true;
    float carryX = 0.0f, carryZ = 0.0f; // carry landing coords in metres

    int step = 0;
    while (!sim.isComplete() && step < MAX_STEPS)
    {
        sim.step(DT);
        step++;

        // Guard against NaN propagation — once the physics diverges every
        // subsequent point is NaN and the loop runs to MAX_STEPS uselessly.
        {
            const auto& s = sim.getState();
            if (std::isnan(s.position[0]) || std::isnan(s.position[1]) || std::isnan(s.position[2]))
            {
                std::fprintf(stderr, "[libgolf-bridge] NaN detected at step %d — aborting simulation\n", step);
                break;
            }
        }

        const char* phase = sim.getCurrentPhaseName();

        // Detect aerial → ground transition; snapshot carry position.
        if (inAerial && !phaseIsAerial(phase))
        {
            inAerial = false;
            const auto& s = sim.getState();
            carryX = s.position[1] * FT_TO_M;
            carryZ = s.position[0] * FT_TO_M;
        }

        // Only record trajectory points during aerial phase.
        if (inAerial && (step % RECORD_EVERY == 0))
        {
            const auto& s = sim.getState();
            char buf[80];
            std::snprintf(buf, sizeof(buf), ",[%.4f,%.4f,%.4f]",
                s.position[1] * FT_TO_M,
                s.position[2] * FT_TO_M,
                s.position[0] * FT_TO_M);
            json += buf;
        }
    }

    // If ball never left aerial (very short shot), use final position as carry.
    if (inAerial)
    {
        const auto& s = sim.getState();
        carryX = s.position[1] * FT_TO_M;
        carryZ = s.position[0] * FT_TO_M;
    }

    // Append carry landing as the final trajectory point (Y=0 = ground).
    {
        char buf[96];
        std::snprintf(buf, sizeof(buf), ",[%.4f,0.0000,%.4f]", carryX, carryZ);
        json += buf;
    }

    json += "]}";
    std::puts(json.c_str());
    return 0;
}
