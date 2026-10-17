namespace AVAZektor.Engine;

public sealed partial class GameEngine
{
    public const double RotateSpeed = 4.2, Thrust = 360, MaxSpeed = 430, Drag = 0.35;
    public const double ShotSpeed = 700, ShotLife = 0.8, FireRepeat = 0.18, EnemyShotSpeed = 260;
    public const int MaxShots = 5;
    public const double StartSeconds = 2.6, ClearSeconds = 3.5, DyingSeconds = 2.2, SafeSeconds = 2.5;
    public const int FirstExtraShip = 10000, ExtraShipEvery = 30000;
    public const int ProbePoints = 150, DiverPoints = 300, SeekerPoints = 300, RingPoints = 50, CorePoints = 5000, TowerBonus = 100;
    public const double SeekerLife = 12;
    public const int Zones = 8, FortressZone = 9;

    public static readonly string[] Cities =
        ["AURORA", "BASTION", "CINDERFALL", "DUSKHAVEN", "EMBERLY", "FROSTGATE", "GLASSWATER", "HIGHSPIRE", "THE FORTRESS"];

    public int Score { get; private set; }
    public int Lives { get; private set; }
    /// <summary>The zone (1 to 8; 9 is the fortress), and how many times round.</summary>
    public int Zone { get; private set; } = 1;
    public int Round { get; private set; } = 1;
    public int ZonesCleared { get; private set; }
    public Ship Ship { get; } = new();
    public List<Shot> Shots { get; } = [];
    public List<Probe> Probes { get; } = [];
    public List<Seeker> Seekers { get; } = [];
    public List<Tower> Towers { get; } = [];
    public Fortress? Fortress { get; private set; }
    public List<Debris> Wreckage { get; } = [];
    public Pilot Pilot { get; } = new();
    public string Cause { get; private set; } = "";
    public double ZoneTime { get; private set; }
    /// <summary>Probes the zone sends in all, and how many have come so far.</summary>
    public int Quota { get; private set; }
    public int Sent { get; private set; }
    /// <summary>For tests: nothing comes, dives or fires by itself.</summary>
    internal bool Calm { get; set; }

    private int _nextExtra, _bonusLeft;
    private double _formationIn, _seekerIn, _thrustSound;
    private bool _firePrev, _warnedSeekers, _warnedCity;

    /// <summary>How hard things push: more each zone, and more again each time round.</summary>
    public double Pace => (1 + 0.06 * (Zone - 1)) * (1 + 0.18 * (Round - 1));
    public string City => Cities[Zone - 1];
    public int Standing => Towers.Count(t => t.Standing);

    private void NewGame()
    {
        _rng = new Random(_seed);
        Score = 0;
        Lives = 3;
        Zone = 1;
        Round = 1;
        ZonesCleared = 0;
        _nextExtra = FirstExtraShip;
        ResetShip();
        Clear();
        Fortress = null;
        SetPhase(Phase.Start);
    }

    private void Clear()
    {
        Shots.Clear();
        Probes.Clear();
        Seekers.Clear();
    }

    /// <summary>A zone: a city of towers below, mission control's word, then the probes.</summary>
    private void BeginZone(int zone)
    {
        Zone = zone;
        ZoneTime = 0;
        Clear();
        Quota = zone == FortressZone ? 0 : 14 + 2 * zone + 2 * (Round - 1);
        Sent = 0;
        _formationIn = 0.5;
        _seekerIn = 2;
        _warnedSeekers = _warnedCity = false;
        BuildCity();
        Fortress = zone == FortressZone ? new Fortress { CoreHp = 10 + 3 * Round } : null;
        ResetShip();
        SetPhase(Phase.Start);
        Say(zone == FortressZone ? "THE FORTRESS" : $"ZONE {zone}", zone == FortressZone ? "SHOOT THROUGH THE RINGS TO THE CORE" : $"DEFEND {City}", StartSeconds);
        Play(zone == FortressZone ? Sfx.SayFortress : Sfx.Zone1 + (zone - 1));
    }

    /// <summary>The city under this zone's sky: towers of the zone's own heights and shapes.</summary>
    private void BuildCity()
    {
        Towers.Clear();
        var r = new Random(Zone * 97 + 13);
        int n = 9;
        for (int i = 0; i < n; i++)
            Towers.Add(new Tower { X = (i + 0.5) * Field.Width / n + r.Next(-20, 21), Width = 44 + r.Next(26), Height = 40 + r.Next(80), Style = r.Next(4) });
    }

    private void ResetShip()
    {
        var s = Ship;
        // Over the city's middle: high in the sky, or (with the fortress up there) low, well clear of it.
        s.X = Field.CentreX;
        s.Y = Fortress is { Dead: false } ? Field.Ground - 170 : Field.Top + 140;
        s.Vx = s.Vy = s.Angle = 0;
        s.Alive = true;
        s.FireCool = 0;
        s.Safe = SafeSeconds;
    }

    // ───────────────────────────── a step of play ─────────────────────────────

    private void Simulate(double dt, Controls c)
    {
        PhaseTime += dt;
        switch (Phase)
        {
            case Phase.Start:
                FlyShip(c, dt, canFire: false);
                MoveShots(dt);
                if (PhaseTime > StartSeconds) SetPhase(Phase.Play);
                break;
            case Phase.Play:
                ZoneTime += dt;
                FlyShip(c, dt, canFire: true);
                MoveShots(dt);
                if (!Calm) Send(dt);
                MoveProbes(dt);
                MoveSeekers(dt);
                MoveFortress(dt);
                Hits();
                if (Phase == Phase.Play) Progress();
                break;
            case Phase.Clear:
                FlyShip(c, dt, canFire: false);
                MoveShots(dt);
                // The towers left standing, counted up one by one.
                if (_bonusLeft > 0 && PhaseTime > 0.8 + (Standing - _bonusLeft) * 0.25)
                {
                    _bonusLeft--;
                    Award(TowerBonus * Math.Min(Zone, Zones));
                    Play(Sfx.Bonus);
                }
                if (PhaseTime > ClearSeconds && _bonusLeft == 0) NextZone();
                break;
            case Phase.Dying:
                MoveShots(dt);
                MoveProbes(dt);
                MoveSeekers(dt);
                MoveFortress(dt);
                if (PhaseTime > DyingSeconds)
                {
                    if (Lives <= 0) { GameOver(); return; }
                    ResetShip();
                    Shots.RemoveAll(s => !s.Mine);
                    Seekers.Clear();
                    SetPhase(Phase.Play);
                }
                break;
        }
    }

    private void NextZone()
    {
        ZonesCleared++;
        if (Zone == FortressZone)
        {
            Round++;
            BeginZone(1);
        }
        else BeginZone(Zone + 1);
    }

    /// <summary>The zone's done when every probe it sends has been dealt with (or the fortress has fallen).</summary>
    private void Progress()
    {
        bool done = Zone == FortressZone ? Fortress is { Dead: true } : Sent >= Quota && Probes.Count == 0;
        if (!done) return;
        SetPhase(Phase.Clear);
        _bonusLeft = Standing;
        Seekers.Clear();
        Shots.RemoveAll(s => !s.Mine);
        if (Zone == FortressZone) Say("THE FORTRESS HAS FALLEN", $"ROUND {Round + 1} NEXT", ClearSeconds);
        else Say($"ZONE {Zone} CLEAR", $"{Standing} TOWERS STANDING", ClearSeconds);
        Play(Zone == FortressZone ? Sfx.SayVictory : Sfx.SayClear);
    }

    private void FlyShip(Controls c, double dt, bool canFire)
    {
        var s = Ship;
        bool fresh = c.Fire && !_firePrev;
        _firePrev = c.Fire;
        if (!s.Alive) return;
        s.Safe = Math.Max(0, s.Safe - dt);
        s.Angle += Math.Sign(c.Dx) * RotateSpeed * dt;
        s.Thrusting = c.Dy < 0;
        if (s.Thrusting)
        {
            s.Vx += Math.Sin(s.Angle) * Thrust * dt;
            s.Vy -= Math.Cos(s.Angle) * Thrust * dt;
            _thrustSound -= dt;
            if (_thrustSound <= 0) { _thrustSound = 0.25; Play(Sfx.Thrust); }
        }
        double k = Math.Max(0, 1 - Drag * dt);
        s.Vx *= k;
        s.Vy *= k;
        double v = Math.Sqrt(s.Vx * s.Vx + s.Vy * s.Vy);
        if (v > MaxSpeed) { s.Vx *= MaxSpeed / v; s.Vy *= MaxSpeed / v; }
        s.X = Field.WrapX(s.X + s.Vx * dt);
        s.Y += s.Vy * dt;
        // The sky has a ceiling, and the city a floor of rooftops: the ship stops at either.
        double low = Field.Ground - 24;
        if (s.Y < Field.Top + 16) { s.Y = Field.Top + 16; s.Vy = Math.Max(0, s.Vy); }
        if (s.Y > low) { s.Y = low; s.Vy = Math.Min(0, s.Vy); }
        s.FireCool -= dt;
        if (canFire && c.Fire && (fresh || s.FireCool <= 0) && Shots.Count(x => x.Mine) < MaxShots)
        {
            s.FireCool = FireRepeat;
            double fx = Math.Sin(s.Angle), fy = -Math.Cos(s.Angle);
            Shots.Add(new Shot { X = s.X + fx * 16, Y = s.Y + fy * 16, Vx = fx * ShotSpeed + s.Vx, Vy = fy * ShotSpeed + s.Vy, Life = ShotLife, Mine = true });
            Play(Sfx.Fire);
        }
    }

    private void MoveShots(double dt)
    {
        foreach (var s in Shots)
        {
            s.X = Field.WrapX(s.X + s.Vx * dt);
            s.Y += s.Vy * dt;
            s.Life -= dt;
            if (s.Life <= 0 || s.Y < Field.Top || s.Y > Field.Ground) s.Dead = true;
        }
        Shots.RemoveAll(s => s.Dead);
    }

    // ───────────────────────────── probes ─────────────────────────────

    /// <summary>Formations of probes in from the top until the zone's quota has come; seekers after a while.</summary>
    private void Send(double dt)
    {
        if (Zone != FortressZone && Sent < Quota && (_formationIn -= dt) <= 0)
        {
            int size = Math.Min(Quota - Sent, 5 + (Zone >= 4 ? 1 : 0) + (Zone >= 7 ? 1 : 0));
            double x = _rng.Next((int)Field.Width), y = Field.Top + 70 + _rng.Next(4) * 55;
            int dir = _rng.Next(2) == 0 ? -1 : 1;
            for (int i = 0; i < size; i++)
                Probes.Add(new Probe
                {
                    BaseX = x, BaseY = Field.Top - 40, Slot = i, Phase = dir, T = 0, Settle = y,
                    FireIn = 2 + _rng.NextDouble() * 3,
                    DiveAt = 2.5 + _rng.NextDouble() * 4 / Pace,
                });
            Sent += size;
            _formationIn = Math.Max(2.5, 5.5 - Zone * 0.35) / Math.Sqrt(Pace);
        }
        if (ZoneTime > Math.Max(4, 10 - Zone) && (_seekerIn -= dt) <= 0 && Seekers.Count < 1 + (Zone + Round) / 3)
        {
            _seekerIn = Math.Max(3.5, 8 - Zone * 0.5) / Math.Sqrt(Pace);
            bool side = _rng.Next(2) == 0;
            double sx = side ? (_rng.Next(2) == 0 ? 0 : Field.Width - 1) : _rng.Next((int)Field.Width), sy = side ? Field.Top + 40 + _rng.Next(300) : Field.Top + 4;
            Seekers.Add(new Seeker { X = sx, Y = sy, Life = SeekerLife });
            Play(Sfx.SeekerCome);
            if (!_warnedSeekers) { _warnedSeekers = true; Play(Sfx.SaySeekers); }
        }
    }

    private void MoveProbes(double dt)
    {
        var ship = Ship;
        foreach (var p in Probes)
        {
            p.T += dt;
            p.Angle += dt * 3;
            if (!p.Diving)
            {
                // With the line: across the sky (round the wrap), settling to its height, weaving as it goes.
                double settle = p.Settle;
                p.BaseX = Field.WrapX(p.BaseX + p.Phase * 80 * Pace * dt);
                p.BaseY = Math.Min(settle, p.BaseY + 70 * dt);
                p.X = Field.WrapX(p.BaseX + (p.Slot - 2) * 46);
                p.Y = p.BaseY + Math.Sin(p.T * 2.2 + p.Slot * 0.9) * 16;
                if (!Calm && p.T > p.DiveAt && p.BaseY >= settle - 1) Dive(p);
            }
            else
            {
                // Down on its target, turning towards it.
                (double tx, double ty) = p.Target >= 0 && Towers[p.Target].Standing
                    ? (Towers[p.Target].X, Field.Ground - Towers[p.Target].Height)
                    : (ship.X, ship.Y);
                if (p.Target >= 0 && !Towers[p.Target].Standing) p.Target = -1;
                var (dx, dy) = Field.Delta(p.X, p.Y, tx, ty);
                double d = Math.Max(1, Math.Sqrt(dx * dx + dy * dy)), speed = 220 * Pace;
                double turn = Math.Min(1, 2.4 * dt);
                p.FireIn = Math.Max(p.FireIn, 0.3);
                p.Angle += dt * 6;
                double vx = p.Vx + (dx / d * speed - p.Vx) * turn, vy = p.Vy + (dy / d * speed - p.Vy) * turn;
                (p.Vx, p.Vy) = (vx, Math.Max(vy, 40));
                p.X = Field.WrapX(p.X + p.Vx * dt);
                p.Y += p.Vy * dt;
                if (p.Y >= Field.Ground - 6) Crash(p);
                else if (Towers.FirstOrDefault(t => t.Standing && Math.Abs(Field.Delta(t.X, 0, p.X, 0).X) < t.Width / 2 && p.Y > Field.Ground - t.Height) is { } hit)
                {
                    Topple(hit);
                    p.Dead = true;
                    Burst(p.X, p.Y, 0xFFFF8A40, 14);
                }
            }
            if (!Calm && ship.Alive && (p.FireIn -= dt) <= 0)
            {
                p.FireIn = (2.2 + _rng.NextDouble() * 2.5) / Pace;
                if (Shots.Count(s => !s.Mine) < 4 + Zone / 2 && Math.Abs(Field.Delta(p.X, p.Y, ship.X, ship.Y).X) < 500) Shoot(p.X, p.Y, ship.X, ship.Y);
            }
        }
        Probes.RemoveAll(p => p.Dead);
    }

    /// <summary>Breaking off to dive: on a tower still standing, mostly, or on the ship.</summary>
    private void Dive(Probe p)
    {
        p.Diving = true;
        var standing = Towers.Select((t, i) => (t, i)).Where(x => x.t.Standing).ToList();
        p.Target = standing.Count > 0 && _rng.NextDouble() < 0.7 ? standing[_rng.Next(standing.Count)].i : -1;
        p.Vx = 0;
        p.Vy = 60;
        Play(Sfx.Dive);
    }

    /// <summary>A probe into the ground: it takes the tower it lands on with it, if any.</summary>
    private void Crash(Probe p)
    {
        p.Dead = true;
        if (Towers.FirstOrDefault(t => t.Standing && Math.Abs(Field.Delta(t.X, 0, p.X, 0).X) < t.Width / 2) is { } t) Topple(t);
        Burst(p.X, Field.Ground - 4, 0xFFFF8A40, 12);
    }

    private void Topple(Tower t)
    {
        t.Standing = false;
        t.Fall = 0;
        Play(Sfx.TowerFall);
        for (int i = 0; i < 10; i++)
            Wreckage.Add(new Debris { X = t.X + _rng.Next(-20, 21), Y = Field.Ground - _rng.NextDouble() * t.Height, Vx = _rng.Next(-80, 81), Vy = -_rng.Next(40, 160), Angle = _rng.NextDouble() * 6, Spin = _rng.Next(-6, 7), Length = 8 + _rng.Next(14), Life = 1.2, Colour = 0xFF6A9AFF });
        if (!_warnedCity) { _warnedCity = true; Play(Sfx.SayCity); }
    }

    private void Shoot(double x, double y, double tx, double ty)
    {
        double a = Field.Bearing(x, y, tx, ty) + (_rng.NextDouble() - 0.5) * 0.2;
        Shots.Add(new Shot { X = x, Y = y, Vx = Math.Sin(a) * EnemyShotSpeed * Math.Sqrt(Pace), Vy = -Math.Cos(a) * EnemyShotSpeed * Math.Sqrt(Pace), Life = 3 });
        Play(Sfx.EnemyFire);
    }

    // ───────────────────────────── seekers ─────────────────────────────

    private void MoveSeekers(double dt)
    {
        var ship = Ship;
        foreach (var s in Seekers)
        {
            s.Spin += dt * 9;
            if ((s.Life -= dt) <= 0)
            {
                // Burnt out.
                s.Dead = true;
                Burst(s.X, s.Y, 0xFFFF5AD8, 8);
                continue;
            }
            double speed = (150 + 10 * Zone) * Math.Sqrt(Pace);
            if (ship.Alive)
            {
                var (dx, dy) = Field.Delta(s.X, s.Y, ship.X, ship.Y);
                double d = Math.Max(1, Math.Sqrt(dx * dx + dy * dy)), turn = Math.Min(1, 1.6 * dt);
                s.Vx += (dx / d * speed - s.Vx) * turn;
                s.Vy += (dy / d * speed - s.Vy) * turn;
            }
            s.X = Field.WrapX(s.X + s.Vx * dt);
            s.Y = Math.Clamp(s.Y + s.Vy * dt, Field.Top + 4, Field.Ground - 8);
        }
        Seekers.RemoveAll(s => s.Dead);
    }

    // ───────────────────────────── the fortress ─────────────────────────────

    /// <summary>The rings turn opposite ways; the core fires out through whatever gaps there are.</summary>
    private void MoveFortress(double dt)
    {
        if (Fortress is not { Dead: false } f) return;
        f.Spin += dt * 0.7 * Math.Sqrt(Pace);
        f.Eye += dt;
        f.Hurt = Math.Max(0, f.Hurt - dt);
        // Its armour grows back, a piece at a time.
        if (!Calm && (f.Regrow -= dt) <= 0)
        {
            f.Regrow = 1.5 / Pace;
            var gaps = Enumerable.Range(0, Fortress.Pieces * 2).Where(i => (i < Fortress.Pieces ? f.OuterRing : f.InnerRing)[i % Fortress.Pieces] < Fortress.PieceHp).ToList();
            if (gaps.Count > 0)
            {
                int i = gaps[_rng.Next(gaps.Count)];
                (i < Fortress.Pieces ? f.OuterRing : f.InnerRing)[i % Fortress.Pieces] = Fortress.PieceHp;
            }
        }
        if (Calm || !Ship.Alive || (f.FireIn -= dt) > 0) return;
        f.FireIn = (1.1 + _rng.NextDouble() * 0.6) / Pace;
        for (int i = -1; i <= 1; i++)
        {
            double a = Field.Bearing(f.X, f.Y, Ship.X, Ship.Y) + i * 0.25;
            Shots.Add(new Shot { X = f.X, Y = f.Y, Vx = Math.Sin(a) * EnemyShotSpeed, Vy = -Math.Cos(a) * EnemyShotSpeed, Life = 3 });
        }
        Play(Sfx.EnemyFire);
    }

    /// <summary>The angle of ring piece <paramref name="i"/> (the inner ring turns one way, the outer the other).</summary>
    public static double PieceAngle(Fortress f, bool outer, int i) => i * Math.PI * 2 / Fortress.Pieces + (outer ? -f.Spin : f.Spin);

    /// <summary>Which ring piece, if any, a point lies in.</summary>
    private static (bool Outer, int Piece)? RingAt(Fortress f, double x, double y)
    {
        var (dx, dy) = Field.Delta(f.X, f.Y, x, y);
        double r = Math.Sqrt(dx * dx + dy * dy), a = Math.Atan2(dx, -dy);
        foreach (bool outer in new[] { true, false })
        {
            double radius = outer ? Fortress.Outer : Fortress.Inner;
            if (Math.Abs(r - radius) > 9) continue;
            double slice = Math.PI * 2 / Fortress.Pieces;
            double rel = ((a - (outer ? -f.Spin : f.Spin)) % (Math.PI * 2) + Math.PI * 2 + slice / 2) % (Math.PI * 2);
            int i = (int)(rel / slice) % Fortress.Pieces;
            if ((outer ? f.OuterRing : f.InnerRing)[i] > 0) return (outer, i);
        }
        return null;
    }

    // ───────────────────────────── hits ─────────────────────────────

    private void Hits()
    {
        var ship = Ship;
        foreach (var s in Shots.Where(s => s.Mine && !s.Dead))
        {
            foreach (var p in Probes)
                if (!p.Dead && Field.Distance(s.X, s.Y, p.X, p.Y) < Probe.Radius + 3)
                {
                    p.Dead = s.Dead = true;
                    int points = p.Diving ? DiverPoints : ProbePoints;
                    Award(points);
                    Pop(p.X, p.Y, points, 0xFF6AFFB0);
                    Burst(p.X, p.Y, 0xFF6AFFB0, 10);
                    Play(Sfx.ProbeHit);
                    break;
                }
            if (s.Dead) continue;
            foreach (var k in Seekers)
                if (!k.Dead && Field.Distance(s.X, s.Y, k.X, k.Y) < Seeker.Radius + 4)
                {
                    k.Dead = s.Dead = true;
                    Award(SeekerPoints);
                    Pop(k.X, k.Y, SeekerPoints, 0xFFFF5AD8);
                    Burst(k.X, k.Y, 0xFFFF5AD8, 10);
                    Play(Sfx.SeekerHit);
                    break;
                }
            if (s.Dead || Fortress is not { Dead: false } f) continue;
            if (RingAt(f, s.X, s.Y) is { } piece)
            {
                var ring = piece.Outer ? f.OuterRing : f.InnerRing;
                s.Dead = true;
                if (--ring[piece.Piece] == 0) Award(RingPoints);
                Burst(s.X, s.Y, 0xFFFFC040, 6);
                Play(Sfx.FortressHit);
            }
            else if (Field.Distance(s.X, s.Y, f.X, f.Y) < Fortress.CoreRadius)
            {
                s.Dead = true;
                // Shut, the eye turns a shot away.
                if (!f.Open) { Burst(s.X, s.Y, 0xFF8A9AC8, 3); Play(Sfx.FortressHit); continue; }
                f.Hurt = 0.25;
                Play(Sfx.Breach);
                if (--f.CoreHp <= 0)
                {
                    f.Dead = true;
                    Award(CorePoints * Round);
                    Pop(f.X, f.Y, CorePoints * Round, 0xFFFFE14A);
                    for (int i = 0; i < 4; i++) Burst(f.X + _rng.Next(-60, 61), f.Y + _rng.Next(-60, 61), i % 2 == 0 ? 0xFFFFC040 : 0xFFFF5A5A, 18);
                    Play(Sfx.FortressDie);
                }
            }
        }
        Seekers.RemoveAll(k => k.Dead);
        Probes.RemoveAll(p => p.Dead);
        if (!ship.Alive || ship.Safe > 0) return;
        foreach (var s in Shots.Where(s => !s.Mine && !s.Dead))
            if (Field.Distance(s.X, s.Y, ship.X, ship.Y) < Ship.Radius + 3) { s.Dead = true; Lose("shot"); return; }
        foreach (var p in Probes)
            if (Field.Distance(p.X, p.Y, ship.X, ship.Y) < Probe.Radius + Ship.Radius - 4) { p.Dead = true; Lose("rammed"); return; }
        foreach (var k in Seekers)
            if (Field.Distance(k.X, k.Y, ship.X, ship.Y) < Seeker.Radius + Ship.Radius - 3) { k.Dead = true; Lose("seeker"); return; }
        if (Fortress is { Dead: false } fort && (RingAt(fort, ship.X, ship.Y) != null || Field.Distance(ship.X, ship.Y, fort.X, fort.Y) < Fortress.CoreRadius + Ship.Radius))
            Lose("the fortress");
    }

    private void Lose(string cause)
    {
        var s = Ship;
        s.Alive = false;
        Cause = cause;
        Lives--;
        for (int i = 0; i < 10; i++)
            Wreckage.Add(new Debris { X = s.X, Y = s.Y, Vx = s.Vx * 0.3 + _rng.Next(-160, 161), Vy = s.Vy * 0.3 + _rng.Next(-160, 161), Angle = _rng.NextDouble() * 6, Spin = _rng.Next(-8, 9), Length = 10 + _rng.Next(10), Life = 1.6, Colour = 0xFF6AE8FF });
        Burst(s.X, s.Y, 0xFFFFFFFF, 20);
        Play(Sfx.Explode);
        SetPhase(Phase.Dying);
    }

    private void Award(int points)
    {
        Score += points;
        while (Score >= _nextExtra)
        {
            Lives++;
            _nextExtra += ExtraShipEvery;
            Play(Sfx.ExtraShip);
            Effects.Add(new Effect { Kind = EffectKind.Word, X = Field.CentreX, Y = Field.Top + 60, Text = "EXTRA SHIP", Colour = 0xFF8FFF8F, Life = 1.6 });
        }
    }

    private void Pop(double x, double y, int points, uint colour) =>
        Effects.Add(new Effect { Kind = EffectKind.Score, X = x, Y = y, Text = points.ToString(), Colour = colour, Life = 0.9 });

    private void Burst(double x, double y, uint colour, int lines)
    {
        Effects.Add(new Effect { Kind = EffectKind.Burst, X = x, Y = y, Colour = colour, Life = 0.5, Size = lines / 10.0 });
        for (int i = 0; i < lines; i++)
        {
            double a = _rng.NextDouble() * Math.PI * 2, v = 60 + _rng.Next(160);
            Wreckage.Add(new Debris { X = x, Y = y, Vx = Math.Sin(a) * v, Vy = -Math.Cos(a) * v, Angle = a, Spin = _rng.Next(-8, 9), Length = 4 + _rng.Next(8), Life = 0.7, Colour = colour });
        }
    }

    // ───────────────────────────── for tests ─────────────────────────────

    public void Run(int steps, Controls c = default)
    {
        for (int i = 0; i < steps; i++) Tick(c);
    }

    public void Idle(int steps) => Run(steps);

    /// <summary>Straight into a game at a zone, past its announcement.</summary>
    public void Go(int zone = 1)
    {
        NewGame();
        State = GameState.Playing;
        BeginZone(zone);
        SetPhase(Phase.Play);
        Message = null;
    }

    public void PutShip(double x, double y, double angle = 0)
    {
        Ship.X = x;
        Ship.Y = y;
        Ship.Vx = Ship.Vy = 0;
        Ship.Angle = angle;
        Ship.Safe = 0;
    }

    public Probe AddProbe(double x, double y, bool diving = false, int target = -1)
    {
        // (In the middle of its line, so it stays where it's put.)
        var p = new Probe { X = x, Y = y, BaseX = x, BaseY = y, Settle = y, Slot = 2, DiveAt = 999, Diving = diving, Target = target, FireIn = 99, Vy = diving ? 60 : 0 };
        Probes.Add(p);
        return p;
    }

    public Seeker AddSeeker(double x, double y)
    {
        var s = new Seeker { X = x, Y = y, Life = SeekerLife };
        Seekers.Add(s);
        return s;
    }

    public void SetLives(int n) => Lives = n;
    public void SetScore(int n) => Score = n;
    /// <summary>Counts the zone's quota as all sent, so the zone ends when the probes about are gone.</summary>
    public void AllSent() => Sent = Quota;
}
