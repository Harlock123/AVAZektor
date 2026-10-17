using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVAZektor.Engine;
using Effect = AVAZektor.Engine.Effect;

namespace AVAZektor.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), Pen> PenCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVAZektor/Assets/Fonts#VT323"));

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
            BrushCache[argb] = b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
        return b;
    }

    private static Pen Pen(uint argb, double width = 1)
    {
        if (!PenCache.TryGetValue((argb, width), out var p))
            PenCache[(argb, width)] = p = new Pen(Brush(argb), width, lineCap: PenLineCap.Round);
        return p;
    }

    private const uint White = 0xFFF2F2FF, Yellow = 0xFFFFE14A, Red = 0xFFFF4A3A, Dim = 0xFF8A9AC8, Stone = 0xFF3A3448, Moss = 0xFF6AA05A;

    private static void Text(DrawingContext dc, string text, Point at, double size, uint colour, bool center = false, bool right = false, bool shadow = true)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour));
        double x = center ? at.X - ft.Width / 2 : right ? at.X - ft.Width : at.X;
        if (shadow) dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(0xC0000000)), new Point(x + 2, at.Y - ft.Height / 2 + 2));
        dc.DrawText(ft, new Point(x, at.Y - ft.Height / 2));
    }

    private static void Poly(DrawingContext dc, uint fill, uint? edge, double width, params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], true);
            foreach (var p in pts.Skip(1)) c.LineTo(p);
            c.EndFigure(true);
        }
        dc.DrawGeometry(fill == 0 ? null : Brush(fill), edge is { } e ? Pen(e, width) : null, g);
    }

    private static uint Hash(int x, int y, int salt)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return h;
    }

    private static uint Alpha(uint colour, double a) => (uint)(Math.Clamp(a, 0, 1) * 255) << 24 | (colour & 0x00FFFFFF);


    private bool InGame => Engine.State is GameState.Attract or GameState.Playing or GameState.Paused or GameState.GameOver;

    private const uint ShipCyan = 0xFF6AE8FF, ProbeGreen = 0xFF6AFFB0, DiverOrange = 0xFFFFA040, SeekerPink = 0xFFFF5AD8, CityBlue = 0xFF6A9AFF, Gold = 0xFFFFC040;

    /// <summary>A line as a vector display draws it: a soft glow under a bright core.</summary>
    private static void Glow(DrawingContext dc, Point a, Point b, uint colour, double alpha = 1, double width = 2)
    {
        if (alpha <= 0.01) return;
        dc.DrawLine(Pen(Alpha(colour, 0.22 * alpha), width * 3.2), a, b);
        dc.DrawLine(Pen(Alpha(colour, alpha), width), a, b);
    }

    /// <summary>A closed outline of points given about (0, 0), turned and placed.</summary>
    private static void Shape(DrawingContext dc, double x, double y, double angle, double k, uint colour, double alpha, params (double X, double Y)[] pts)
    {
        double c = Math.Cos(angle), s = Math.Sin(angle);
        Point At((double X, double Y) p) => new(x + (p.X * c - p.Y * s) * k, y + (p.X * s + p.Y * c) * k);
        for (int i = 0; i < pts.Length; i++) Glow(dc, At(pts[i]), At(pts[(i + 1) % pts.Length]), colour, alpha);
    }

    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0xFF000004), new Rect(0, 0, Field.Width, Field.Height));
        DrawSky(dc);
        if (InGame)
        {
            DrawCity(dc);
            if (g.Fortress is { } f) DrawFortress(dc, f);
            foreach (var p in g.Probes) DrawProbe(dc, p.X, p.Y, p.Angle, p.Diving);
            foreach (var k in g.Seekers) DrawSeeker(dc, k.X, k.Y, k.Spin, k.Life < 2 && (int)(_clock * 10) % 2 == 0);
            foreach (var d in g.Wreckage)
            {
                double a = Math.Clamp(1 - d.Age / d.Life, 0, 1), c = Math.Cos(d.Angle) * d.Length / 2, s = Math.Sin(d.Angle) * d.Length / 2;
                Glow(dc, new Point(d.X - c, d.Y - s), new Point(d.X + c, d.Y + s), d.Colour, a, 1.6);
            }
            foreach (var s in g.Shots)
            {
                if (s.Mine) Glow(dc, new Point(s.X, s.Y), new Point(s.X - s.Vx * 0.014, s.Y - s.Vy * 0.014), 0xFFFFF4C0, 1, 2.4);
                else Shape(dc, s.X, s.Y, _clock * 8, 1, 0xFFFF4A4A, 1, (0, -5), (4, 0), (0, 5), (-4, 0));
            }
            var ship = g.Ship;
            if (ship.Alive && (ship.Safe <= 0 || (int)(_clock * 10) % 2 == 0)) DrawShip(dc, ship.X, ship.Y, ship.Angle, ship.Thrusting, 1);
        }
        foreach (var e in g.Effects) DrawEffect(dc, e);
        DrawHud(dc);
        switch (g.State)
        {
            case GameState.Title: DrawTitle(dc); break;
            case GameState.HighScores: DrawHighScores(dc); break;
            case GameState.EnterInitials: DrawInitials(dc); break;
            default: DrawMessages(dc); break;
        }
        if (g.State == GameState.Paused)
        {
            dc.FillRectangle(Brush(0x90000000), new Rect(0, 0, Field.Width, Field.Height));
            Text(dc, "PAUSED", new Point(512, 340), 80, Yellow, center: true);
            Text(dc, "ANY KEY TO CARRY ON - ESC TO QUIT", new Point(512, 400), 26, White, center: true);
        }
        if (g.State == GameState.GameOver)
            Text(dc, "GAME OVER", new Point(512, 360), 100, Red, center: true);
        if (g.State == GameState.Attract)
            Text(dc, "DEMONSTRATION - PRESS ENTER TO PLAY", new Point(512, Field.Height - 14), 24, Yellow, center: true);
    }

    /// <summary>Stars, and the planet's horizon: lines running off towards it.</summary>
    private void DrawSky(DrawingContext dc)
    {
        for (int i = 0; i < 70; i++)
        {
            uint h = Hash(i, 3, 7);
            double tw = 0.4 + 0.6 * Math.Abs(Math.Sin(_clock * (0.5 + h % 5 * 0.3) + i));
            dc.FillRectangle(Brush(Alpha(0xFFB0C0FF, tw * 0.7)), new Rect(h % 1024, Field.Top + (h >> 10) % (int)(Field.Ground - Field.Top - 80), 2, 2));
        }
        // The ground: a bright edge, and lines receding under it.
        Glow(dc, new Point(0, Field.Ground), new Point(Field.Width, Field.Ground), 0xFF3A6AFF, 0.9, 2);
        for (int i = 1; i < 4; i++)
        {
            double y = Field.Ground + i * i * 5;
            Glow(dc, new Point(0, y), new Point(Field.Width, y), 0xFF2A4ACF, 0.5 / i, 1.2);
        }
        for (int i = -8; i <= 8; i++)
        {
            double x0 = Field.CentreX + i * 70;
            Glow(dc, new Point(x0, Field.Ground), new Point(Field.CentreX + i * 150, Field.Height), 0xFF2A4ACF, 0.25, 1);
        }
    }

    private void DrawCity(DrawingContext dc)
    {
        foreach (var t in Engine.Towers)
        {
            double x = t.X, w = t.Width, h = t.Height, g = Field.Ground;
            if (!t.Standing)
            {
                // Rubble.
                var pts = new List<Point> { new(x - w / 2, g) };
                for (int i = 1; i < 6; i++) pts.Add(new Point(x - w / 2 + i * w / 6, g - 4 - Hash((int)x, i, 2) % 12));
                pts.Add(new Point(x + w / 2, g));
                for (int i = 0; i < pts.Count - 1; i++) Glow(dc, pts[i], pts[i + 1], 0xFF3A4A7A, 0.7, 1.5);
                continue;
            }
            uint c = CityBlue;
            switch (t.Style)
            {
                case 0:
                    Outline(dc, c, new Point(x - w / 2, g), new Point(x - w / 2, g - h), new Point(x + w / 2, g - h), new Point(x + w / 2, g));
                    Glow(dc, new Point(x, g - h), new Point(x, g - h - 18), c, 0.8, 1.4);
                    break;
                case 1:
                    Outline(dc, c, new Point(x - w / 2, g), new Point(x - w / 2, g - h * 0.6), new Point(x - w / 4, g - h * 0.6), new Point(x - w / 4, g - h),
                        new Point(x + w / 4, g - h), new Point(x + w / 4, g - h * 0.6), new Point(x + w / 2, g - h * 0.6), new Point(x + w / 2, g));
                    break;
                case 2:
                    Outline(dc, c, new Point(x - w / 2, g), new Point(x - w / 2, g - h * 0.75), new Point(x - w / 4, g - h), new Point(x + w / 4, g - h),
                        new Point(x + w / 2, g - h * 0.75), new Point(x + w / 2, g));
                    break;
                default:
                    Outline(dc, c, new Point(x - w / 2, g), new Point(x - w / 2, g - h * 0.8), new Point(x - w / 10, g - h * 0.8), new Point(x - w / 10, g - h),
                        new Point(x + w / 10, g - h), new Point(x + w / 10, g - h * 0.8), new Point(x + w / 2, g - h * 0.8), new Point(x + w / 2, g));
                    break;
            }
            // Lit windows.
            for (double wy = g - 12; wy > g - h * 0.7; wy -= 14)
                for (double wx = x - w / 2 + 8; wx < x + w / 2 - 6; wx += 12)
                    if (Hash((int)wx, (int)wy, t.Style) % 3 != 0)
                        dc.FillRectangle(Brush(Alpha(0xFFFFE08A, 0.6 + 0.3 * Math.Sin(_clock + wx))), new Rect(wx, wy, 4, 5));
        }
    }

    private static void Outline(DrawingContext dc, uint colour, params Point[] pts)
    {
        for (int i = 0; i < pts.Length - 1; i++) Glow(dc, pts[i], pts[i + 1], colour, 0.95, 1.8);
    }

    /// <summary>The ship: a swept arrowhead with twin fins, and its flame when thrusting.</summary>
    private void DrawShip(DrawingContext dc, double x, double y, double angle, bool thrust, double k)
    {
        Shape(dc, x, y, angle, k, ShipCyan, 1, (0, -16), (6, -2), (13, 10), (5, 6), (-5, 6), (-13, 10), (-6, -2));
        Shape(dc, x, y, angle, k, ShipCyan, 0.7, (0, -9), (3, 0), (-3, 0));
        if (thrust && (int)(_clock * 20) % 2 == 0)
            Shape(dc, x, y, angle, k, 0xFFFF8A2A, 1, (-4, 8), (0, 18 + Math.Sin(_clock * 40) * 3), (4, 8));
    }

    /// <summary>A probe: a diamond with arms that spin; orange when it dives.</summary>
    private static void DrawProbe(DrawingContext dc, double x, double y, double spin, bool diving)
    {
        uint c = diving ? DiverOrange : ProbeGreen;
        Shape(dc, x, y, 0, 1, c, 1, (0, -11), (11, 0), (0, 11), (-11, 0));
        Shape(dc, x, y, spin, 1, c, 0.8, (-16, -2), (-8, 0), (-16, 2));
        Shape(dc, x, y, spin, 1, c, 0.8, (16, -2), (8, 0), (16, 2));
        Glow(dc, new Point(x - 3, y), new Point(x + 3, y), 0xFFFFFFFF, 0.9, 2);
    }

    /// <summary>A seeker: a spinning star of points (it flickers as it burns out).</summary>
    private static void DrawSeeker(DrawingContext dc, double x, double y, double spin, bool flicker)
    {
        if (flicker) return;
        var pts = new (double, double)[10];
        for (int i = 0; i < 10; i++)
        {
            double a = i * Math.PI / 5, r = i % 2 == 0 ? 13 : 5;
            pts[i] = (Math.Sin(a) * r, -Math.Cos(a) * r);
        }
        Shape(dc, x, y, spin, 1, SeekerPink, 1, pts);
    }

    private void DrawFortress(DrawingContext dc, Fortress f)
    {
        if (f.Dead) return;
        double slice = Math.PI * 2 / Fortress.Pieces;
        foreach (bool outer in new[] { true, false })
        {
            var ring = outer ? f.OuterRing : f.InnerRing;
            double r = outer ? Fortress.Outer : Fortress.Inner;
            uint full = outer ? CityBlue : Gold;
            for (int i = 0; i < Fortress.Pieces; i++)
            {
                if (ring[i] == 0) continue;
                double a = GameEngine.PieceAngle(f, outer, i);
                Point P(double ang, double rad) => new(f.X + Math.Sin(ang) * rad, f.Y - Math.Cos(ang) * rad);
                double a0 = a - slice / 2 + 0.04, a1 = a + slice / 2 - 0.04;
                uint c = ring[i] < Fortress.PieceHp ? Alpha(full, 0.5) : full;
                Outline(dc, c, P(a0, r - 7), P(a0, r + 7), P(a1, r + 7), P(a1, r - 7), P(a0, r - 7));
            }
        }
        // The core: a pulsing eye.
        double pulse = 1 + Math.Sin(_clock * 5) * 0.08;
        uint core = f.Hurt > 0 ? 0xFFFFFFFF : 0xFFFF5A5A;
        var pts = new (double, double)[12];
        for (int i = 0; i < 12; i++) pts[i] = (Math.Sin(i * Math.PI / 6) * Fortress.CoreRadius * pulse, -Math.Cos(i * Math.PI / 6) * Fortress.CoreRadius * pulse);
        Shape(dc, f.X, f.Y, _clock, 1, core, 1, pts);
        // The eye: shut (a line), or open (a diamond with a pupil), when the core can be hurt.
        if (f.Open)
        {
            Shape(dc, f.X, f.Y, 0, 1, core, 1, (-20, 0), (0, -13), (20, 0), (0, 13));
            dc.FillEllipse(f.X, f.Y, 6, core);
        }
        else Glow(dc, new Point(f.X - 18, f.Y), new Point(f.X + 18, f.Y), Alpha(core, 0.7), 1, 2);
        Text(dc, new string('|', f.CoreHp), new Point(f.X, f.Y + Fortress.Outer + 26), 20, Gold, center: true);
    }

    private void DrawEffect(DrawingContext dc, Effect e)
    {
        double a = 1 - e.Age / e.Life;
        switch (e.Kind)
        {
            case EffectKind.Score:
                Text(dc, e.Text, new Point(e.X, e.Y - e.Age * 30), 22, Alpha(e.Colour, a), center: true);
                break;
            case EffectKind.Burst:
                dc.DrawEllipse(null, Pen(Alpha(e.Colour, a), 2), new Point(e.X, e.Y), 6 + e.Age * 90 * e.Size, 6 + e.Age * 90 * e.Size);
                break;
            case EffectKind.Word:
                Text(dc, e.Text, new Point(e.X, e.Y), 34, Alpha(e.Colour, Math.Min(1, a * 2)), center: true);
                break;
        }
    }

    // ───────────────────────────── around the edges ─────────────────────────────

    private void DrawHud(DrawingContext dc)
    {
        var g = Engine;
        Glow(dc, new Point(0, Field.Top - 2), new Point(Field.Width, Field.Top - 2), 0xFF3A6AFF, 0.6, 1.5);
        Text(dc, g.Score.ToString("000000"), new Point(24, 34), 42, Yellow);
        Text(dc, "HI " + Math.Max(g.HighScores.TopScore, g.Score).ToString("000000"), new Point(200, 34), 24, Dim);
        if (!InGame) return;
        string where = g.Zone == GameEngine.FortressZone ? "THE FORTRESS" : $"ZONE {g.Zone}  {g.City}";
        Text(dc, where, new Point(Field.Width / 2, 24), 30, ProbeGreen, center: true);
        if (g.Zone != GameEngine.FortressZone)
        {
            int left = g.Quota - g.Sent + g.Probes.Count;
            Text(dc, $"PROBES {left}   TOWERS {g.Standing}", new Point(Field.Width / 2, 52), 22, Dim, center: true);
        }
        else if (g.Fortress is { } f) Text(dc, $"CORE {f.CoreHp}", new Point(Field.Width / 2, 52), 22, Gold, center: true);
        if (g.Round > 1) Text(dc, $"ROUND {g.Round}", new Point(Field.Width / 2 + 230, 24), 22, Dim);
        for (int i = 0; i < Math.Min(g.Lives, 7); i++) DrawShip(dc, Field.Width - 30 - i * 30, 38, 0, false, 0.8);
    }

    private void DrawMessages(DrawingContext dc)
    {
        var g = Engine;
        if (g.Message is not { } msg) return;
        double cx = Field.Width / 2, cy = Field.Top + 230;
        Text(dc, msg, new Point(cx, cy - 12), msg.Length > 16 ? 50 : 66, ProbeGreen, center: true);
        if (g.SubMessage is { } sub) Text(dc, sub, new Point(cx, cy + 36), 26, White, center: true);
    }

    // ───────────────────────────── title and scores ─────────────────────────────

    private void DrawTitle(DrawingContext dc)
    {
        double cx = Field.Width / 2;
        // A probe formation weaving across, and the ship below.
        for (int i = 0; i < 5; i++)
        {
            double px = Field.WrapX(_clock * 90 + i * 46), py = 380 + Math.Sin(_clock * 2.2 + i * 0.9) * 16;
            DrawProbe(dc, px, py, _clock * 3, false);
        }
        DrawShip(dc, cx + Math.Sin(_clock * 0.8) * 200, 520, Math.Sin(_clock * 0.8 + 1.5) * 0.4, true, 1.6);
        string title = "AVA ZEKTOR";
        for (int i = 0; i < title.Length; i++)
            Text(dc, title[i].ToString(), new Point(cx - title.Length * 34 + 34 + i * 68, 170 + Math.Sin(_clock * 2.5 + i * 0.5) * 3), 120, i % 2 == 0 ? ShipCyan : ProbeGreen, center: true);
        Text(dc, "AFTER SEGA'S 1982 COLOUR VECTOR ARCADE GAME", new Point(cx, 248), 26, Dim, center: true);
        Text(dc, "DEFEND THE EIGHT CITIES FROM THE PROBES. SHOOT THE SEEKERS. BREAK THE FORTRESS.", new Point(cx, 300), 24, White, center: true);
        if ((int)(_clock * 2) % 2 == 0)
            Text(dc, "PRESS ENTER TO LAUNCH", new Point(cx, 620), 42, Yellow, center: true);
        Text(dc, "LEFT/RIGHT: TURN    UP: THRUST    SPACE: FIRE    P: PAUSE    M: SOUND", new Point(cx, 680), 22, 0xFF8AA0D0, center: true);
    }

    private void DrawHighScores(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        Text(dc, "TOP PILOTS", new Point(cx, 140), 64, ShipCyan, center: true);
        Text(dc, "RANK NAME    SCORE  ZONES", new Point(cx, 204), 30, Dim, center: true);
        var entries = g.HighScores.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool mine = i == g.NewEntryIndex && (int)(_clock * 4) % 2 == 0;
            Text(dc, $"{i + 1,2}.  {e.Initials}  {e.Score,8}   {e.Stage,4}", new Point(cx, 248 + i * 42), 34, mine ? Red : i == 0 ? Yellow : White, center: true);
        }
        Text(dc, "PRESS ENTER TO PLAY", new Point(cx, 710), 30, White, center: true);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        Text(dc, "A NEW HIGH SCORE!", new Point(cx, 200), 64, Yellow, center: true);
        Text(dc, g.Score.ToString(), new Point(cx, 270), 52, White, center: true);
        Text(dc, "ENTER YOUR INITIALS", new Point(cx, 350), 34, Dim, center: true);
        if (g.Initials is { } entry)
        {
            string s = entry.Initials;
            for (int i = 0; i < s.Length; i++)
            {
                bool cur = i == entry.Cursor && (int)(_clock * 3) % 2 == 0;
                Text(dc, s[i].ToString(), new Point(cx - 60 + i * 60, 440), 80, cur ? Red : Yellow, center: true);
                dc.FillRectangle(Brush(i == entry.Cursor ? Red : Dim), new Rect(cx - 82 + i * 60, 480, 44, 4));
            }
        }
        Text(dc, "TYPE, OR UP/DOWN AND SPACE; ENTER WHEN DONE", new Point(cx, 560), 26, White, center: true);
    }
}

internal static class DrawingExtensions
{
    public static void FillEllipse(this DrawingContext dc, double x, double y, double r, uint colour) =>
        dc.DrawEllipse(GameCanvas.Brush(colour), null, new Point(x, y), r, r);
}
