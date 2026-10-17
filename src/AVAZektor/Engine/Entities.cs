namespace AVAZektor.Engine;

/// <summary>Rotate left/right (Dx), thrust (Dy -1), and fire.</summary>
public readonly record struct Controls(int Dx = 0, int Dy = 0, bool Fire = false);

/// <summary>
/// The sky over a city: the screen between the score bar and the ground, wrapping left and right (but not up and
/// down: the city is underneath).
/// </summary>
public static class Field
{
    public const double Step = 1.0 / 60;
    public const double Width = 1024, Height = 768, Top = 70, Ground = 716;
    public const double CentreX = Width / 2, CentreY = (Top + Ground) / 2;

    public static double WrapX(double x) => ((x % Width) + Width) % Width;

    /// <summary>The shortest offset from one point to another, round the wrap if that's nearer.</summary>
    public static (double X, double Y) Delta(double x0, double y0, double x1, double y1)
    {
        double dx = x1 - x0;
        if (dx > Width / 2) dx -= Width; else if (dx < -Width / 2) dx += Width;
        return (dx, y1 - y0);
    }

    public static double Distance(double x0, double y0, double x1, double y1)
    {
        var (dx, dy) = Delta(x0, y0, x1, y1);
        return Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>The heading from one point toward another: 0 up, growing clockwise.</summary>
    public static double Bearing(double x0, double y0, double x1, double y1)
    {
        var (dx, dy) = Delta(x0, y0, x1, y1);
        return Math.Atan2(dx, -dy);
    }

    public static double AngleDiff(double target, double current) => Math.IEEERemainder(target - current, Math.PI * 2);
}

public sealed class Ship
{
    public const double Radius = 13;
    public double X = Field.CentreX, Y = Field.CentreY, Vx, Vy, Angle;
    public bool Alive = true, Thrusting;
    public double FireCool, Safe;
}

public sealed class Shot
{
    public double X, Y, Vx, Vy, Life;
    public bool Mine, Dead;
}

/// <summary>
/// A probe: it comes in with its formation, sweeping across the sky in a wave, and sooner or later breaks off to
/// dive, on a tower of the city or on the ship.
/// </summary>
public sealed class Probe
{
    public const double Radius = 15;
    public double X, Y, Vx, Vy, Angle, T, FireIn;
    /// <summary>Where its formation is (it keeps its place in the line), which way the line goes, and its place in it.</summary>
    public double BaseX, BaseY, Phase;
    /// <summary>The height its line settles at, and when it breaks off to dive.</summary>
    public double Settle, DiveAt;
    public int Slot;
    public bool Diving, Dead;
    /// <summary>The tower it's diving on (-1: the ship).</summary>
    public int Target = -1;
}

/// <summary>A seeker: a homing mine that hunts the ship, burning out after a while.</summary>
public sealed class Seeker
{
    public const double Radius = 12;
    public double X, Y, Vx, Vy, Life, Spin;
    public bool Dead;
}

/// <summary>A tower of the city below.</summary>
public sealed class Tower
{
    public double X, Width, Height;
    public int Style;
    public bool Standing = true;
    public double Fall;
}

/// <summary>
/// The fortress at the end of the eighth zone: a core inside two rings of armour turning opposite ways. Shoot
/// pieces of the rings away (two hits each; they grow back) to get a shot at the core, which can only be hurt
/// while its eye is open.
/// </summary>
public sealed class Fortress
{
    public const int Pieces = 16;
    public const double Inner = 70, Outer = 110, CoreRadius = 34;
    public const int PieceHp = 2;
    public const double EyeCycle = 4, EyeOpen = 1.6;
    public double X = Field.CentreX, Y = Field.CentreY - 30, Spin, FireIn = 2, Hurt, Regrow = 3, Eye;
    /// <summary>What's left of each piece of armour (0: gone).</summary>
    public int[] InnerRing = Enumerable.Repeat(PieceHp, Pieces).ToArray();
    public int[] OuterRing = Enumerable.Repeat(PieceHp, Pieces).ToArray();
    public bool Open => Eye % EyeCycle > EyeCycle - EyeOpen;
    public int CoreHp = 5;
    public bool Dead;
}

/// <summary>A line of something destroyed, spinning away.</summary>
public sealed class Debris
{
    public double X, Y, Vx, Vy, Angle, Spin, Length, Age, Life = 1;
    public uint Colour;
}

public enum EffectKind { Score, Burst, Word }

public sealed class Effect
{
    public EffectKind Kind;
    public double X, Y, Age, Life = 0.8, Size = 1;
    public string Text = "";
    public uint Colour = 0xFFFFFFFF;
    public bool Done => Age >= Life;
}
