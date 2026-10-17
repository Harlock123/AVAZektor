using AVAZektor.Engine;

namespace AVAZektor.Tests;

/// <summary>
/// How the computer pilot gets on over a whole game (set ZEKTOR_PROBE to run it, for tuning: "1" for the pilot,
/// "lazy" for one that turns and fires all the time but never thrusts).
/// </summary>
public sealed class PilotProbe(ITestOutputHelper output)
{
    [Fact]
    public void How_the_computer_flies()
    {
        string? mode = Environment.GetEnvironmentVariable("ZEKTOR_PROBE");
        if (mode is null) return;
        foreach (int seed in new[] { 1, 2, 3, 4, 5 })
        {
            var g = new GameEngine(HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "zektor-probe-" + Guid.NewGuid().ToString("N"))), seed) { Silent = true };
            g.Go(1);
            g.SetLives(30);
            var deaths = new Dictionary<string, int>();
            var zones = new List<string>();
            var doing = new Dictionary<string, int>();
            var last = g.Phase;
            int zone = 1, t = 0, start = 0;
            for (; t < 60 * 1500 && g.State == GameState.Playing && g.Round == 1; t++)
            {
                var c = mode == "lazy" ? new Controls((t / 90) % 2 == 0 ? 1 : -1, 0, t % 10 < 5) : g.Pilot.Decide(g);
                g.Tick(c);
                doing[g.Pilot.Doing] = doing.GetValueOrDefault(g.Pilot.Doing) + 1;
                if (g.Phase == Phase.Dying && last != Phase.Dying) deaths[$"Z{g.Zone} {g.Cause}"] = deaths.GetValueOrDefault($"Z{g.Zone} {g.Cause}") + 1;
                if (g.Phase == Phase.Clear && last != Phase.Clear) zones.Add($"Z{zone}:{(t - start) / 60}s/{g.Standing}t");
                if (g.Zone != zone) { zone = g.Zone; start = t; }
                last = g.Phase;
            }
            output.WriteLine($"seed {seed}: round {g.Round} zone {g.Zone} after {t / 60}s, lives used {30 - g.Lives + (g.Score >= 10000 ? 1 : 0)}, score {g.Score} | {string.Join(" ", zones)}");
            output.WriteLine("   deaths " + string.Join(", ", deaths.OrderBy(d => d.Key).Select(d => $"{d.Key} x{d.Value}")));
            output.WriteLine("   doing " + string.Join(", ", doing.OrderByDescending(d => d.Value).Select(d => $"{d.Key} {d.Value / 60}s")));
        }
    }
}
