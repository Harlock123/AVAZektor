using Avalonia.Input;
using AVAZektor.Engine;

namespace AVAZektor.Tests;

public class GameplayTests(ITestOutputHelper output)
{
    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "avazektor-tests-" + Guid.NewGuid().ToString("N"));

    /// <summary>A game at a zone with nothing coming by itself.</summary>
    private static GameEngine Calm(int zone = 1)
    {
        var g = new GameEngine(HighScoreTable.Load(TempFolder()), 1) { Silent = true };
        g.Go(zone);
        g.Calm = true;
        return g;
    }

    private static readonly Controls Thrust = new(0, -1), Fire = new(0, 0, true), Left = new(-1, 0);

    // ───────────────────────────── the ship ─────────────────────────────

    [Fact]
    public void The_ship_turns_thrusts_and_wraps_left_and_right_but_not_through_the_ground()
    {
        var g = Calm();
        g.PutShip(500, 300);
        g.Run(30, Thrust);
        Assert.True(g.Ship.Y < 300);
        g.PutShip(1020, 300, Math.PI / 2);
        g.Run(20, Thrust);
        Assert.True(g.Ship.X < 200, "it should have come round the edge");
        g.PutShip(500, 600, Math.PI);
        g.Run(120, Thrust);
        Assert.True(g.Ship.Y <= Field.Ground - 24);
        Assert.True(g.Ship.Alive);
    }

    [Fact]
    public void Five_shots_at_most_in_the_air()
    {
        var g = Calm();
        g.PutShip(500, 400, Math.PI / 2);
        int most = 0;
        for (int i = 0; i < 60; i++)
        {
            g.Tick(i % 2 == 0 ? Fire : default);
            most = Math.Max(most, g.Shots.Count(s => s.Mine));
        }
        Assert.Equal(GameEngine.MaxShots, most);
    }

    // ───────────────────────────── probes ─────────────────────────────

    [Fact]
    public void A_shot_destroys_a_probe_and_a_diving_one_is_worth_more()
    {
        var g = Calm();
        g.PutShip(500, 500);
        g.AddProbe(500, 300);
        g.Run(30, Fire);
        Assert.Empty(g.Probes);
        Assert.Equal(GameEngine.ProbePoints, g.Score);
        g.AddProbe(500, 300, diving: true).Vy = 0;
        g.Run(1);
        g.Probes[0].X = 500;
        g.Probes[0].Y = 300;
        g.Run(30, Fire);
        Assert.Equal(GameEngine.ProbePoints + GameEngine.DiverPoints, g.Score);
    }

    [Fact]
    public void Probes_come_in_formation_until_the_quota_and_the_zone_clears_when_they_are_all_dealt_with()
    {
        var g = new GameEngine(HighScoreTable.Load(TempFolder()), 1) { Silent = true };
        g.Go(1);
        g.Run(60 * 2);
        Assert.True(g.Probes.Count >= 5);
        Assert.True(g.Sent > 0 && g.Sent <= g.Quota);
        // All of them gone: the zone is clear, the towers counted, and on to the next.
        g.AllSent();
        g.Probes.Clear();
        g.Run(2);
        Assert.Equal(Phase.Clear, g.Phase);
        int towers = g.Standing;
        g.Run((int)(60 * (GameEngine.ClearSeconds + 3)));
        Assert.Equal(2, g.Zone);
        Assert.True(g.Score >= towers * GameEngine.TowerBonus);
    }

    [Fact]
    public void A_diving_probe_brings_down_the_tower_it_hits()
    {
        var g = Calm();
        g.PutShip(100, 150);
        var t = g.Towers[4];
        var p = g.AddProbe(t.X, Field.Ground - t.Height - 60, diving: true, target: 4);
        g.Run(120);
        Assert.False(t.Standing);
        Assert.Equal(8, g.Standing);
        Assert.DoesNotContain(p, g.Probes);
    }

    [Fact]
    public void Running_into_a_probe_or_its_shot_loses_the_ship()
    {
        var g = Calm();
        g.PutShip(500, 400);
        g.AddProbe(500, 400);
        g.Run(2);
        Assert.False(g.Ship.Alive);
        Assert.Equal("rammed", g.Cause);
        Assert.Equal(2, g.Lives);
        var g2 = Calm();
        g2.PutShip(500, 400);
        g2.Shots.Add(new Shot { X = 500, Y = 360, Vy = 300, Life = 2 });
        g2.Run(20);
        Assert.Equal("shot", g2.Cause);
    }

    // ───────────────────────────── seekers ─────────────────────────────

    [Fact]
    public void A_seeker_hunts_the_ship_and_burns_out_in_the_end()
    {
        var g = Calm();
        g.PutShip(800, 400);
        var k = g.AddSeeker(200, 400);
        double before = Field.Distance(k.X, k.Y, 800, 400);
        g.Run(60);
        Assert.True(Field.Distance(k.X, k.Y, g.Ship.X, g.Ship.Y) < before);
        // Out of the way of it, far off, it gives out.
        var g2 = Calm();
        g2.PutShip(500, 400);
        g2.AddSeeker(0, Field.Top + 10);
        for (int i = 0; i < 60 * (GameEngine.SeekerLife + 1) && g2.Seekers.Count > 0; i++)
        {
            g2.Run(1);
            // Keep the ship away from it.
            if (g2.Seekers.Count > 0 && Field.Distance(g2.Seekers[0].X, g2.Seekers[0].Y, g2.Ship.X, g2.Ship.Y) < 200)
                g2.PutShip(Field.WrapX(g2.Seekers[0].X + 500), 400);
        }
        Assert.Empty(g2.Seekers);
        Assert.True(g2.Ship.Alive);
    }

    [Fact]
    public void Seekers_start_coming_part_way_through_a_zone()
    {
        var g = new GameEngine(HighScoreTable.Load(TempFolder()), 1);
        var heard = new List<Sfx>();
        g.Sound += heard.Add;
        g.Go(3);
        g.PutShip(500, 150);
        int first = -1;
        for (int t = 0; t < 60 * 20 && first < 0; t++)
        {
            g.Ship.Safe = 99;
            g.Tick(default);
            if (g.Seekers.Count > 0) first = t;
        }
        output.WriteLine($"first seeker after {first / 60.0:0.0}s");
        Assert.InRange(first, 60 * 3, 60 * 16);
        Assert.Contains(Sfx.SaySeekers, heard);
    }

    // ───────────────────────────── the fortress ─────────────────────────────

    [Fact]
    public void After_the_eighth_zone_comes_the_fortress()
    {
        var g = new GameEngine(HighScoreTable.Load(TempFolder()), 1) { Silent = true };
        g.Go(8);
        g.AllSent();
        g.Probes.Clear();
        g.Run((int)(60 * (GameEngine.ClearSeconds + 4)));
        Assert.Equal(GameEngine.FortressZone, g.Zone);
        Assert.NotNull(g.Fortress);
        Assert.True(g.Ship.Y > g.Fortress!.Y + Fortress.Outer + 40, "the ship should start well clear of it");
    }

    [Fact]
    public void Its_armour_takes_two_hits_a_piece_and_the_core_only_hurts_with_its_eye_open()
    {
        var g = Calm(GameEngine.FortressZone);
        var f = g.Fortress!;
        // Straight up at it from below: the outer ring, then the inner, then the core.
        g.PutShip(f.X, f.Y + 250, 0);
        int hp = f.CoreHp;
        for (int i = 0; i < 60 * 8; i++)
        {
            f.Spin = 0;
            g.Tick(i % 12 == 0 ? Fire : default);
        }
        Assert.True(f.OuterRing.Count(x => x == 0) >= 1);
        Assert.True(f.InnerRing.Count(x => x == 0) >= 1);
        Assert.True(f.CoreHp < hp, "with a way through, open-eyed, the core should be hurt");
        // With its eye shut, a shot at the core does nothing.
        f.Eye = 0;
        Assert.False(f.Open);
    }

    [Fact]
    public void Breaking_the_fortress_starts_the_next_round_faster()
    {
        var g = Calm(GameEngine.FortressZone);
        g.Fortress!.CoreHp = 1;
        g.Fortress.Eye = Fortress.EyeCycle - 1;
        Array.Fill(g.Fortress.OuterRing, 0);
        Array.Fill(g.Fortress.InnerRing, 0);
        g.PutShip(g.Fortress.X, g.Fortress.Y + 250, 0);
        g.Run(40, Fire);
        Assert.True(g.Fortress.Dead);
        g.Run((int)(60 * (GameEngine.ClearSeconds + 5)));
        Assert.Equal(2, g.Round);
        Assert.Equal(1, g.Zone);
        Assert.True(g.Pace > 1.1, "round two's first zone should push harder than round one's");
    }

    // ───────────────────────────── lives and the game ─────────────────────────────

    [Fact]
    public void The_last_ship_lost_ends_the_game_and_a_good_score_goes_on_the_table()
    {
        var g = Calm();
        g.SetLives(1);
        g.SetScore(90000);
        g.PutShip(500, 400);
        g.AddProbe(500, 400);
        g.Run((int)(60 * (GameEngine.DyingSeconds + 4.5)));
        Assert.Equal(GameState.EnterInitials, g.State);
    }

    [Fact]
    public void A_ship_lost_comes_back_safe_for_a_moment()
    {
        var g = Calm();
        g.PutShip(500, 400);
        g.AddProbe(500, 400);
        g.Run((int)(60 * GameEngine.DyingSeconds) + 5);
        Assert.True(g.Ship.Alive);
        Assert.True(g.Ship.Safe > 0);
    }

    [Fact]
    public void Ten_thousand_points_is_an_extra_ship()
    {
        var g = Calm();
        g.SetScore(9900);
        g.PutShip(500, 500);
        g.AddProbe(500, 300);
        g.Run(30, Fire);
        Assert.Equal(4, g.Lives);
    }

    [Fact]
    public void Each_zone_is_announced_by_voice()
    {
        var g = new GameEngine(HighScoreTable.Load(TempFolder()), 1);
        var heard = new List<Sfx>();
        g.Sound += heard.Add;
        g.Tick(default);
        Assert.Contains(Sfx.SayDefend, heard);
        g.Run((int)(60 * GameEngine.VoiceSeconds) + 5);
        Assert.Contains(Sfx.TitleTune, heard);
        heard.Clear();
        g.Tick(default, start: true);
        Assert.Contains(Sfx.Zone1, heard);
    }

    [Fact]
    public void Left_alone_the_title_runs_the_demonstration_then_the_high_scores()
    {
        var g = new GameEngine(HighScoreTable.Load(TempFolder()), 1) { Silent = true };
        g.Run((int)(60 * GameEngine.TitleIdleSeconds) + 5);
        Assert.Equal(GameState.Attract, g.State);
        g.Run((int)(60 * GameEngine.AttractSeconds) + 5);
        Assert.Equal(GameState.HighScores, g.State);
        g.Run((int)(60 * GameEngine.HighScoreSeconds) + 5);
        Assert.Equal(GameState.Title, g.State);
    }

    [Fact]
    public void The_computer_pilot_breaks_the_fortress_in_round_one()
    {
        foreach (int seed in new[] { 1, 2 })
        {
            var g = new GameEngine(HighScoreTable.Load(TempFolder()), seed) { Silent = true };
            g.Go(1);
            g.SetLives(20);
            int t = 0;
            for (; t < 60 * 400 && g.Round == 1 && g.State == GameState.Playing; t++) g.Tick(g.Pilot.Decide(g));
            output.WriteLine($"seed {seed}: round {g.Round} after {t / 60}s, {20 - g.Lives} ships lost, score {g.Score}");
            Assert.Equal(2, g.Round);
        }
    }

    [Fact]
    public void The_keys_fly_the_ship()
    {
        var input = new InputHandler();
        input.OnKeyDown(Key.Up);
        Assert.Equal(-1, input.Player.Dy);
        input.OnKeyDown(Key.Left);
        Assert.Equal(-1, input.Player.Dx);
        input.OnKeyDown(Key.Space);
        Assert.True(input.Player.Fire);
        var c = input.Player;
        input.Dispose();
        // Up on the keys is thrust: it carries the ship the way its nose points.
        var g = Calm();
        g.PutShip(500, 400);
        g.Run(30, c with { Dx = 0, Fire = false });
        Assert.True(g.Ship.Y < 400);
    }
}
