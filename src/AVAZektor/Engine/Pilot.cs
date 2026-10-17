namespace AVAZektor.Engine;

/// <summary>
/// The computer pilot, for the demonstration (and the tests). It keeps up out of the rooftops, thrusts out of the
/// way of anything about to hit it, and otherwise turns to lead the most pressing target (a seeker close by, a
/// probe diving on the city, the nearest of the rest) and fires when it's lined up. At the fortress it aims at
/// the core: the shots break whatever pieces of the rings are in the way.
/// </summary>
public sealed class Pilot
{
    /// <summary>What it is up to, for the probe.</summary>
    public string Doing { get; private set; } = "";

    private int _tick;

    public void Reset()
    {
        _tick = 0;
        Doing = "";
    }

    public Controls Decide(GameEngine g)
    {
        _tick++;
        var s = g.Ship;
        if (!s.Alive || g.Phase is not (Phase.Play or Phase.Start)) return default;
        double speed = Math.Sqrt(s.Vx * s.Vx + s.Vy * s.Vy);

        // Anything about to run into us: get out of its way.
        double soonest = 99, awayFrom = 0;
        void Threat(double x, double y, double vx, double vy, double reach)
        {
            for (double t = 0; t <= 0.8; t += 0.05)
            {
                double tx = x + vx * t, ty = y + vy * t, sx = s.X + s.Vx * t, sy = s.Y + s.Vy * t;
                if (Field.Distance(tx, ty, sx, sy) < reach)
                {
                    if (t < soonest) { soonest = t; awayFrom = Field.Bearing(x, y, s.X, s.Y); }
                    return;
                }
            }
        }
        if (s.Safe <= 0.3)
        {
            foreach (var shot in g.Shots.Where(x => !x.Mine)) Threat(shot.X, shot.Y, shot.Vx, shot.Vy, 24);
            foreach (var p in g.Probes) Threat(p.X, p.Y, p.Diving ? p.Vx : 0, p.Diving ? p.Vy : 0, 34);
            foreach (var k in g.Seekers) Threat(k.X, k.Y, k.Vx, k.Vy, 34);
            if (g.Fortress is { Dead: false } f && Field.Distance(s.X, s.Y, f.X, f.Y) < Fortress.Outer + 70)
                (soonest, awayFrom) = (0, Field.Bearing(f.X, f.Y, s.X, s.Y) + Math.PI / 2);
        }
        if (soonest < 0.55)
        {
            // Across its path, whichever way round is quicker.
            double a1 = awayFrom + Math.PI / 2, a2 = awayFrom - Math.PI / 2;
            double away = Math.Abs(Field.AngleDiff(a1, s.Angle)) < Math.Abs(Field.AngleDiff(a2, s.Angle)) ? a1 : a2;
            // Not into the rooftops.
            if (s.Y > Field.Ground - 140 && Math.Cos(away) < 0) away = Math.Abs(Field.AngleDiff(0, a1)) < Math.Abs(Field.AngleDiff(0, a2)) ? a1 : a2;
            Doing = "evade";
            return Steer(s, away, thrust: true, fire: true, loose: 0.9);
        }

        // Too low, or too fast: up, and steady.
        if (s.Y > Field.Ground - 150)
        {
            Doing = "climb";
            return Steer(s, 0, thrust: true, fire: false);
        }

        // The best target, led so the shot meets it.
        double best = double.MaxValue, aim = s.Angle;
        bool any = false, near = false;
        void Consider(double x, double y, double vx, double vy, double bias)
        {
            double d = Field.Distance(s.X, s.Y, x, y);
            double t = d / GameEngine.ShotSpeed;
            double ax = x + vx * t - s.Vx * t, ay = y + vy * t - s.Vy * t;
            double a = Field.Bearing(s.X, s.Y, ax, ay);
            double cost = d + Math.Abs(Field.AngleDiff(a, s.Angle)) * 120 - bias;
            if (cost < best) { best = cost; aim = a; any = true; near = d < 520; }
        }
        foreach (var k in g.Seekers) Consider(k.X, k.Y, k.Vx, k.Vy, Field.Distance(s.X, s.Y, k.X, k.Y) < 260 ? 400 : 100);
        foreach (var p in g.Probes)
        {
            double vx = p.Diving ? p.Vx : (p.Phase * 80 * g.Pace), vy = p.Diving ? p.Vy : 0;
            Consider(p.X, p.Y, vx, vy, p.Diving ? (p.Target >= 0 ? 260 : 180) : 0);
        }
        if (g.Fortress is { Dead: false } fort) Consider(fort.X, fort.Y, 0, 0, 0);
        if (!any)
        {
            // Nothing about: back to the middle of the sky.
            Doing = "wait";
            bool off = Field.Distance(s.X, s.Y, Field.CentreX, Field.Top + 200) > 160 && speed < 120;
            return Steer(s, off ? Field.Bearing(s.X, s.Y, Field.CentreX, Field.Top + 200) : s.Angle, thrust: off, fire: false);
        }
        Doing = "attack";
        bool lined = Math.Abs(Field.AngleDiff(aim, s.Angle)) < 0.07 && near;
        // Creep closer if everything's far off, but never get going too fast.
        bool thrust = !near && speed < 120 && Math.Abs(Field.AngleDiff(aim, s.Angle)) < 0.3;
        return Steer(s, aim, thrust, fire: lined && _tick % 2 == 0);
    }

    private static Controls Steer(Ship s, double want, bool thrust, bool fire, double loose = 0.04)
    {
        double diff = Field.AngleDiff(want, s.Angle);
        int dx = Math.Abs(diff) > 0.03 ? Math.Sign(diff) : 0;
        return new Controls(dx, thrust && Math.Abs(diff) < loose + 0.4 ? -1 : 0, fire);
    }
}
