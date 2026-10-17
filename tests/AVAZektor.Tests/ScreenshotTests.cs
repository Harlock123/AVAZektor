using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVAZektor.Engine;
using AVAZektor.Views;
using Controls = AVAZektor.Engine.Controls;

namespace AVAZektor.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set ZEKTOR_SHOTS to a folder to
/// save them as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests
{
    private static (Window W, GameCanvas Canvas, GameEngine G) Open(int seed = 11)
    {
        var g = new GameEngine(HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avazektor-ui-" + Guid.NewGuid().ToString("N"))), seed) { Silent = true };
        var canvas = new GameCanvas(g, new SoundManager(silent: true), startTimer: false);
        var w = new Window { Width = 1024, Height = 768, Content = canvas };
        w.Show();
        Pump();
        return (w, canvas, g);
    }

    private static void Pump()
    {
        for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
    }

    private static void Shot(Window w, GameCanvas c, string name)
    {
        c.InvalidateVisual();
        Pump();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Environment.GetEnvironmentVariable("ZEKTOR_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVAZektor-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    /// <summary>Lets the computer pilot play until something worth a picture is on screen.</summary>
    private static bool PlayUntil(GameEngine g, GameCanvas c, Func<GameEngine, bool> done, int maxSeconds = 150)
    {
        for (int t = 0; t < 60 * maxSeconds; t++)
        {
            if (done(g)) { c.Advance(0.016); return true; }
            var move = g.Pilot.Decide(g);
            g.Tick(move);
            if (t % 3 == 0) c.Advance(1 / 20.0);
        }
        return false;
    }
    [AvaloniaFact]
    public void The_title_and_the_high_scores()
    {
        var (w, c, g) = Open();
        g.Idle(30);
        c.Advance(2.1);
        Shot(w, c, "title");
        g.HighScores.Insert("ACE", 98300, 9);
        g.HighScores.Insert("AVA", 41500, 5);
        g.Idle((int)(60 * (GameEngine.TitleIdleSeconds + GameEngine.AttractSeconds)) + 10);
        for (int i = 0; i < 60 * 30 && g.State != GameState.HighScores; i++) g.Idle(1);
        Shot(w, c, "high-scores");
    }

    /// <summary>A game flown by the computer pilot until something worth showing.</summary>
    private static void Fly(int zone, string name, Func<GameEngine, bool> moment, int seconds = 120)
    {
        var (w, c, g) = Open();
        g.Go(zone);
        g.SetLives(20);
        g.Pilot.Reset();
        Assert.True(PlayUntil(g, c, moment, seconds), $"never saw the moment for {name}");
        Shot(w, c, name);
    }

    [AvaloniaFact]
    public void A_formation() => Fly(1, "formation", g => g.Probes.Count(p => !p.Diving) >= 5 && g.Probes.All(p => p.Y > Field.Top + 60));

    [AvaloniaFact]
    public void A_dive() => Fly(4, "dive", g => g.Probes.Any(p => p.Diving && p.Target >= 0 && p.Y > Field.Ground - 260) && g.Shots.Count(s => s.Mine) >= 2);

    [AvaloniaFact]
    public void Seekers() => Fly(6, "seekers", g => g.Seekers.Count >= 1 && g.Seekers.All(k => k.Y > Field.Top + 80 && Field.Distance(k.X, k.Y, g.Ship.X, g.Ship.Y) < 260) && g.Probes.Count >= 3);

    [AvaloniaFact]
    public void A_tower_falls() => Fly(7, "tower-falls", g => g.Wreckage.Count(d => d.Colour == 0xFF6A9AFF && d.Age < 0.3) >= 5);

    [AvaloniaFact]
    public void The_fortress() => Fly(GameEngine.FortressZone, "fortress", g => g.Fortress is { Open: true } f && f.OuterRing.Count(x => x == 0) >= 2 && g.Shots.Count(s => s.Mine) >= 2);

    [AvaloniaFact]
    public void Zone_clear() => Fly(2, "zone-clear", g => g.Phase == Phase.Clear && g.PhaseTime > 1.2);

    [AvaloniaFact]
    public void A_zone_begins()
    {
        var (w, c, g) = Open();
        g.StartGame();
        g.Idle(60);
        c.Advance(1);
        Shot(w, c, "zone-one");
    }
}
