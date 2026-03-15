using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;

// Wraps the libgolf-bridge CLI executable, converting its JSON trajectory
// output into the OpenFairway coordinate convention:
//   Vector3.X = carry distance forward (metres)
//   Vector3.Y = height above ground (metres)
//   Vector3.Z = lateral offline deviation (metres, positive = right)
//
// Build the bridge first:
//   cd tools/libgolf-bridge && ./build.sh
//
// The expected binary location (relative to the Godot project root) is:
//   tools/libgolf-bridge/build/libgolf-bridge

public sealed class LibgolfBridgeClient
{
    private readonly string _binaryPath;

    public LibgolfBridgeClient(string projectRoot)
    {
        _binaryPath = Path.Combine(projectRoot, "tools", "libgolf-bridge", "build", "libgolf-bridge");
    }

    public bool IsAvailable() => File.Exists(_binaryPath);

    // Runs the bridge on a background thread.
    // onComplete is invoked on the CALLER'S thread via the returned Task —
    // callers must dispatch back to the Godot main thread themselves
    // (e.g. via Callable.From(...).CallDeferred()).
    // Returns null in the result on any error.
    public Task<List<Vector3>> RunSimulationAsync(
        float speedMph,
        float launchAngleDeg,
        float directionDeg,
        float backspinRpm,
        float sidespinRpm)
    {
        return Task.Run(() => RunSimulationBlocking(speedMph, launchAngleDeg, directionDeg, backspinRpm, sidespinRpm));
    }

    // Blocking version — safe to call from a background thread.
    // Do NOT call from the Godot main thread.
    private List<Vector3> RunSimulationBlocking(
        float speedMph,
        float launchAngleDeg,
        float directionDeg,
        float backspinRpm,
        float sidespinRpm)
    {
        if (!IsAvailable())
        {
            GD.PrintErr($"[LibgolfBridge] Binary not found: {_binaryPath}");
            GD.PrintErr("[LibgolfBridge] Run tools/libgolf-bridge/build.sh to build it.");
            return null;
        }

        string args = $"--speed {speedMph:F2} " +
                      $"--angle {launchAngleDeg:F2} " +
                      $"--direction {directionDeg:F2} " +
                      $"--backspin {backspinRpm:F0} " +
                      $"--sidespin {sidespinRpm:F0}";

        string stdout;
        string stderr;
        int exitCode;

        try
        {
            var psi = new ProcessStartInfo(_binaryPath, args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);

            // Read stdout and stderr concurrently to prevent deadlock.
            // Sequential reads (stdout then stderr) deadlock when the process
            // fills the stderr pipe buffer while the parent blocks on stdout.
            var stdoutTask = proc.StandardOutput.ReadToEndAsync();
            var stderrTask = proc.StandardError.ReadToEndAsync();
            Task.WaitAll(stdoutTask, stderrTask);
            proc.WaitForExit();

            stdout = stdoutTask.Result;
            stderr = stderrTask.Result;
            exitCode = proc.ExitCode;
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"[LibgolfBridge] Failed to launch bridge: {ex.Message}");
            return null;
        }

        if (exitCode != 0)
        {
            GD.PrintErr($"[LibgolfBridge] Bridge exited with code {exitCode}. stderr: {stderr}");
            return null;
        }

        return ParsePoints(stdout.Trim());
    }

    private static List<Vector3> ParsePoints(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var arr = root.GetProperty("points");
            var points = new List<Vector3>(arr.GetArrayLength());
            foreach (var pt in arr.EnumerateArray())
            {
                float x = pt[0].GetSingle(); // carry (m)  → our X
                float y = pt[1].GetSingle(); // height (m) → our Y
                float z = pt[2].GetSingle(); // offline (m)→ our Z
                points.Add(new Vector3(x, y, z));
            }

            return points;
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"[LibgolfBridge] Failed to parse output: {ex.Message}");
            GD.PrintErr($"[LibgolfBridge] Raw output: {json}");
            return null;
        }
    }
}
