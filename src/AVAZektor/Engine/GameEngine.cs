namespace AVAZektor.Engine;

public enum GameState { Title, Attract, Playing, Paused, GameOver, EnterInitials, HighScores }

/// <summary>How a zone is going.</summary>
public enum Phase
{
    /// <summary>The zone named, the ship at the ready.</summary>
    Start,
    /// <summary>Fighting.</summary>
    Play,
    /// <summary>The zone cleared: the towers still standing counted up.</summary>
    Clear,
    /// <summary>The ship lost: a moment, then the next (or the game is over).</summary>
    Dying,
}

/// <summary>Sounds the engine asks for; the host decides how they sound.</summary>
public enum Sfx
{
    Fire, EnemyFire, Thrust, ProbeHit, Dive, SeekerCome, SeekerHit, Explode, TowerFall, FortressHit, Breach, FortressDie, ExtraShip, Bonus,
    /// <summary>Mission control: "zone one" to "zone eight", and the warnings.</summary>
    Zone1, Zone2, Zone3, Zone4, Zone5, Zone6, Zone7, Zone8, SayFortress, SaySeekers, SayCity, SayClear, SayVictory, SayDefend,
    TitleTune, GameOver,
}

/// <summary>
/// The whole game with no UI. Call <see cref="Update"/> with the elapsed time and the controls; it runs in fixed
/// 60 Hz steps, so it plays the same at any frame rate.
/// </summary>
public sealed partial class GameEngine
{
    public const double TitleIdleSeconds = 18;
    public const double AttractSeconds = 50;
    public const double HighScoreSeconds = 9;
    /// <summary>How long the title's spoken line lasts before its tune.</summary>
    public const double VoiceSeconds = 2.2;

    private double _accumulator, _idle;
    private bool _startPrev;
    private bool _voicePending = true, _tunePending = true;
    private readonly int _seed;
    private Random _rng;

    /// <summary>A game whose happenings come from <paramref name="seed"/> (the same seed plays the same way).</summary>
    public GameEngine(HighScoreTable? table = null, int seed = 1)
    {
        HighScores = table ?? HighScoreTable.Load();
        _seed = seed;
        _rng = new Random(seed);
        NewGame();
        State = GameState.Title;
    }

    public event Action<Sfx>? Sound;

    public GameState State { get; private set; } = GameState.Title;
    public Phase Phase { get; private set; } = Phase.Start;
    public double StateTime { get; private set; }
    public double PhaseTime { get; private set; }
    public HighScoreTable HighScores { get; }
    public InitialsEntry? Initials { get; private set; }
    public int NewEntryIndex { get; private set; } = -1;
    public bool Silent { get; set; }

    public List<Effect> Effects { get; } = [];

    /// <summary>A word across the screen, and for how much longer.</summary>
    public string? Message { get; private set; }
    public string? SubMessage { get; private set; }
    public double MessageTime { get; private set; }

    private void Play(Sfx s)
    {
        if (!Silent) Sound?.Invoke(s);
    }

    private void Say(string text, string? sub = null, double seconds = 2.5)
    {
        Message = text;
        SubMessage = sub;
        MessageTime = seconds;
    }

    public void Update(double dt, Controls c, bool start)
    {
        _accumulator += Math.Min(dt, 0.25);
        while (_accumulator >= Field.Step)
        {
            _accumulator -= Field.Step;
            Tick(c, start);
        }
    }

    /// <summary>One 1/60 s step.</summary>
    public void Tick(Controls c, bool start = false)
    {
        double dt = Field.Step;
        StateTime += dt;
        bool startPressed = start && !_startPrev;
        _startPrev = start;
        foreach (var e in Effects) e.Age += dt;
        Effects.RemoveAll(e => e.Done);
        foreach (var d in Wreckage) { d.Age += dt; d.X = Field.WrapX(d.X + d.Vx * dt); d.Y += d.Vy * dt; d.Angle += d.Spin * dt; }
        Wreckage.RemoveAll(d => d.Age >= d.Life);
        if (MessageTime > 0) { MessageTime -= dt; if (MessageTime <= 0) Message = SubMessage = null; }

        switch (State)
        {
            case GameState.Title:
                if (_voicePending) { _voicePending = false; Play(Sfx.SayDefend); }
                if (_tunePending && StateTime > VoiceSeconds) { _tunePending = false; Play(Sfx.TitleTune); }
                _idle += dt;
                if (startPressed || c.Fire && StateTime > 0.5) StartGame();
                else if (_idle > TitleIdleSeconds) StartAttract();
                break;
            case GameState.Attract:
                Simulate(dt, Pilot.Decide(this));
                if (startPressed) { StartGame(); break; }
                if (State == GameState.Attract && StateTime > AttractSeconds) ShowHighScores(-1);
                break;
            case GameState.Playing:
                Simulate(dt, c);
                break;
            case GameState.GameOver:
                if (StateTime > 3.5)
                {
                    if (HighScores.Qualifies(Score))
                    {
                        State = GameState.EnterInitials;
                        StateTime = 0;
                        Initials = new InitialsEntry(HighScores.LastInitials);
                    }
                    else ShowHighScores(-1);
                }
                break;
            case GameState.EnterInitials:
                if (Initials is { IsDone: true } done)
                {
                    int place = HighScores.Insert(done.Initials, Score, ZonesCleared);
                    HighScores.Save();
                    ShowHighScores(place);
                }
                break;
            case GameState.HighScores:
                if (startPressed) StartGame();
                else if (StateTime > HighScoreSeconds) ReturnToTitle();
                break;
        }
    }

    public void StartGame()
    {
        NewGame();
        State = GameState.Playing;
        StateTime = 0;
        NewEntryIndex = -1;
        BeginZone(1);
    }

    /// <summary>The demonstration: the computer flies the first zones.</summary>
    private void StartAttract()
    {
        NewGame();
        State = GameState.Attract;
        StateTime = 0;
        Pilot.Reset();
        BeginZone(1);
    }

    public void ReturnToTitle()
    {
        State = GameState.Title;
        StateTime = 0;
        _idle = 0;
        _voicePending = _tunePending = true;
        Effects.Clear();
        Message = SubMessage = null;
        NewGame();
    }

    private void ShowHighScores(int place)
    {
        State = GameState.HighScores;
        StateTime = 0;
        NewEntryIndex = place;
    }

    public void Pause()
    {
        if (State == GameState.Playing) State = GameState.Paused;
    }

    public void Resume()
    {
        if (State == GameState.Paused) State = GameState.Playing;
    }

    private void GameOver()
    {
        Play(Sfx.GameOver);
        if (State == GameState.Attract)
        {
            ShowHighScores(-1);
            return;
        }
        State = GameState.GameOver;
        StateTime = 0;
    }

    private void SetPhase(Phase p)
    {
        Phase = p;
        PhaseTime = 0;
    }
}
