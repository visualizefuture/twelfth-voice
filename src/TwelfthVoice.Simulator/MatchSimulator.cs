using TwelfthVoice.Domain;

namespace TwelfthVoice.Simulator;

/// <summary>
/// Generates a full, football-realistic synthetic match as an ordered list of events.
///
/// The model is a possession-chain simulation: the team in possession repeatedly chooses to
/// pass, carry, dribble or shoot based on where the ball is, who has it, team style and
/// pressure from the opponent. Outcomes come from the transparent models in
/// <see cref="FootballMath"/>, so every stat the intelligence layer explains is grounded in
/// the same logic that produced it.
/// </summary>
public sealed class MatchSimulator
{
    private static readonly string[] Referees =
        { "M. Okafor", "L. Pemberton", "S. Ibáñez", "R. Castell", "J. Whitlock", "A. Novak" };

    private static readonly string[] Weathers =
        { "Clear, 14°C", "Light rain, 11°C", "Overcast, 9°C", "Floodlit evening, 12°C", "Windy, 10°C" };

    private readonly SimulationOptions _options;
    private readonly Rng _rng;
    private readonly List<MatchEvent> _events = new();
    private readonly Dictionary<string, PhysicalTracker> _physical = new();

    private MatchInfo _info = null!;
    private TeamRuntime _home = null!;
    private TeamRuntime _away = null!;

    // Ball state: the team in possession, the player on the ball and its location in that team's frame.
    private TeamRuntime _att = null!;
    private Player _carrier = null!;
    private PitchPoint _ball;

    private double _t;
    private int _period;
    private double _periodEnd;
    private int _seq;
    private double _nextTracking;
    private double _nextPhaseChange;
    private (string TeamId, string PlayerId, double Time)? _lastCompletedPass;

    public MatchSimulator(SimulationOptions? options = null)
    {
        _options = options ?? new SimulationOptions();
        _rng = new Rng(_options.Seed);
    }

    public static SimulatedMatch Run(SimulationOptions? options = null) => new MatchSimulator(options).Simulate();

    public SimulatedMatch Simulate()
    {
        if (_events.Count > 0)
            throw new InvalidOperationException("A MatchSimulator instance can only simulate one match.");

        Setup();

        _period = 1;
        Add(E(EventType.MatchStart) with { Qualifiers = new[] { $"{_info.Home.Name} vs {_info.Away.Name}" } });

        PlayPeriod(1, _home);
        PlayPeriod(2, _away);

        Add(E(EventType.MatchEnd) with { HomeScore = _home.Score, AwayScore = _away.Score });
        return new SimulatedMatch(_info, _events.AsReadOnly());
    }

    // ------------------------------------------------------------------ setup

    private void Setup()
    {
        var ids = SyntheticLeague.TeamIds;
        string homeId = _options.HomeTeamId ?? ids[_rng.Int(0, ids.Count)];
        var awayChoices = ids.Where(i => !i.Equals(homeId, StringComparison.OrdinalIgnoreCase)).ToList();
        string awayId = _options.AwayTeamId ?? awayChoices[_rng.Int(0, awayChoices.Count)];

        if (homeId.Equals(awayId, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Home and away teams must be different.");

        var home = SyntheticLeague.BuildTeam(homeId, _options.LeagueSeed);
        var away = SyntheticLeague.BuildTeam(awayId, _options.LeagueSeed);

        _info = new MatchInfo
        {
            MatchId = $"TV-{home.Id}-{away.Id}-{_options.Seed}",
            Competition = SyntheticLeague.CompetitionName,
            Venue = home.Stadium,
            KickOff = _options.KickOff ?? new DateTimeOffset(2026, 10, 24, 15, 0, 0, TimeSpan.Zero),
            Home = home,
            Away = away,
            Seed = _options.Seed,
            Referee = _rng.Pick(Referees),
            Weather = _rng.Pick(Weathers)
        };

        _home = CreateRuntime(home, isHome: true);
        _away = CreateRuntime(away, isHome: false);
    }

    private TeamRuntime CreateRuntime(Team team, bool isHome)
    {
        var runtime = new TeamRuntime { Team = team, IsHome = isHome };
        var template = Formations.Get(team.Formation);

        for (int i = 0; i < team.Squad.Count; i++)
        {
            var player = team.Squad[i];
            _physical[player.Id] = new PhysicalTracker();
            if (i < template.Count)
            {
                runtime.OnPitch.Add(player);
                runtime.Slots[player.Id] = template[i].Base;
            }
            else
            {
                runtime.Bench.Add(player);
            }
        }

        // Home advantage: a touch more aggressive without the ball.
        runtime.PressIntensity = Math.Clamp(team.Style.PressIntensity + (isHome ? 0.05 : 0), 0.1, 0.95);
        runtime.Directness = team.Style.Directness;

        int substitutions = _rng.Int(2, 4);
        var times = new List<double>();
        for (int i = 0; i < substitutions; i++) times.Add(_rng.Range(3450, 4900));
        times.Sort();
        foreach (var time in times) runtime.SubstitutionTimes.Enqueue(time);

        return runtime;
    }

    // ------------------------------------------------------------------ match flow

    private void PlayPeriod(int period, TeamRuntime kickOffTeam)
    {
        _period = period;
        _t = MatchClock.PeriodStart(period);
        double addedMinutes = period == 1 ? _rng.Int(1, 4) : _rng.Int(2, 7);
        _periodEnd = MatchClock.RegulationEnd(period) + addedMinutes * 60;
        _nextTracking = _t + _options.TrackingIntervalSeconds;
        _nextPhaseChange = _t + 600;
        _lastCompletedPass = null;

        Add(E(EventType.PeriodStart));
        KickOff(kickOffTeam);

        int guard = 0;
        while (_t < _periodEnd && guard++ < 50_000)
        {
            MaybeChangePhase();
            MaybeSubstitute();
            Step();
        }

        Add(E(EventType.PeriodEnd) with
        {
            HomeScore = _home.Score,
            AwayScore = _away.Score,
            Qualifiers = new[] { $"Added time: {addedMinutes} min" }
        });
    }

    private void KickOff(TeamRuntime team)
    {
        _att = team;
        _ball = PitchPoint.CentreSpot;
        _carrier = team.OnPitch.FirstOrDefault(p => p.Position == Position.ST)
                   ?? team.OnPitch.First(p => p.Position != Position.GK);
        Add(E(EventType.KickOff) with { TeamId = team.Id, PlayerId = _carrier.Id, Start = _ball });
        Advance(1);
    }

    /// <summary>Every 10 minutes teams adjust their intensity; trailing teams chase, leading teams protect.</summary>
    private void MaybeChangePhase()
    {
        if (_t < _nextPhaseChange) return;
        _nextPhaseChange += 600;

        foreach (var team in new[] { _home, _away })
        {
            var opponent = Opp(team);
            var style = team.Team.Style;
            double chasing = team.Score < opponent.Score && _t > 4200 ? 0.18 : 0;
            double protecting = team.Score > opponent.Score && _t > 4500 ? -0.12 : 0;
            double homeBoost = team.IsHome ? 0.05 : 0;
            team.PressIntensity = Math.Clamp(style.PressIntensity + homeBoost + _rng.Normal(0, 0.12) + chasing + protecting, 0.1, 0.95);
            team.Directness = Math.Clamp(style.Directness + _rng.Normal(0, 0.08) + chasing, 0.1, 0.95);
        }
    }

    private void MaybeSubstitute()
    {
        foreach (var team in new[] { _home, _away })
        {
            if (team.SubstitutionTimes.Count == 0 || _t < team.SubstitutionTimes.Peek()) continue;
            team.SubstitutionTimes.Dequeue();

            var candidates = team.Bench.Where(p => p.Position != Position.GK).ToList();
            if (candidates.Count == 0) continue;
            var incoming = _rng.Pick(candidates);

            var outgoing = team.OnPitch
                .Where(p => p.Position != Position.GK && p.Id != _carrier.Id)
                .Select(p => (Player: p, Key: (SameLine(p.Position, incoming.Position) ? 0 : 100) + p.Attributes.Stamina + _rng.Range(0, 15)))
                .OrderBy(x => x.Key)
                .Select(x => x.Player)
                .FirstOrDefault();
            if (outgoing is null) continue;

            int index = team.OnPitch.FindIndex(p => p.Id == outgoing.Id);
            team.OnPitch[index] = incoming;
            team.Bench.Remove(incoming);
            team.Slots[incoming.Id] = team.Slots[outgoing.Id];
            team.Slots.Remove(outgoing.Id);

            Add(E(EventType.Substitution) with { TeamId = team.Id, PlayerId = outgoing.Id, RelatedPlayerId = incoming.Id });
        }
    }

    private static bool SameLine(Position a, Position b) => Line(a) == Line(b);

    private static int Line(Position p) => p switch
    {
        Position.GK => 0,
        Position.RB or Position.CB or Position.LB => 1,
        Position.DM or Position.CM or Position.AM => 2,
        _ => 3
    };

    // ------------------------------------------------------------------ one decision

    private void Step()
    {
        var def = Opp(_att);
        bool pressured = false;

        // Defences press hardest high up the pitch and around their own box.
        double pressProbability = def.PressIntensity * (_ball.X < 52.5 ? 0.30 : _ball.X > 75 ? 0.34 : 0.20);
        if (_carrier.Position == Position.GK) pressProbability *= 0.4;

        if (_rng.Chance(pressProbability))
        {
            var presser = NearestOutfield(def, _ball.Mirror());
            Add(E(EventType.Pressure) with
            {
                TeamId = def.Id,
                PlayerId = presser.Id,
                RelatedPlayerId = _carrier.Id,
                Start = _ball.Mirror().Round()
            });
            pressured = true;
            Advance(_rng.Range(0.4, 1.2));

            if (_rng.Chance(0.16 + def.PressIntensity * 0.12 + (_ball.X > 75 ? 0.08 : 0)))
            {
                var at = _ball.Mirror();
                WinBall(def, presser, at, EventType.Tackle, at.InOpponentHalf ? "High regain" : "Pressure regain");
                return;
            }
        }

        // The deeper into the box, the more bodies in the way.
        if (_ball.InPenaltyArea && _carrier.Position != Position.GK &&
            _rng.Chance(0.20 + Math.Max(0, _ball.X - 88) * 0.045))
        {
            DefendBox(def);
            return;
        }

        // Calibrated (Python port, 40 matches): ~25 shots, ~3 xG, ~2.7 goals, ~1000 passes at ~83%.
        double distanceToGoal = _ball.DistanceToGoal;
        double shotProbability = distanceToGoal < 11 ? 0.30 : distanceToGoal < 18 ? 0.15 : distanceToGoal < 25 ? 0.10 : distanceToGoal < 30 ? 0.04 : 0.0;
        shotProbability *= 0.75 + _att.Directness * 0.5;
        if (_carrier.Position == Position.GK) shotProbability = 0;

        if (_rng.Chance(shotProbability))
        {
            Shoot(FootChoice(), false);
            return;
        }

        double roll = _rng.Next();
        double dribbleProbability = pressured ? 0.14 : 0.05;
        double carryProbability = _carrier.Position == Position.GK ? 0.02 : 0.20;

        if (roll < dribbleProbability && _carrier.Position != Position.GK) Dribble();
        else if (roll < dribbleProbability + carryProbability) Carry();
        else Pass(pressured, forceCross: false);
    }

    // ------------------------------------------------------------------ actions

    private void Pass(bool pressured, bool forceCross)
    {
        var start = _ball;
        var passer = _carrier;
        bool wideAndAdvanced = start.X > 82 && (start.Y < 16 || start.Y > 52);
        bool cross = forceCross || (wideAndAdvanced && _rng.Chance(0.45));
        bool through = false;

        Player receiver;
        PitchPoint target;

        if (cross)
        {
            receiver = PickBoxTarget(_att, passer);
            target = new PitchPoint(_rng.Range(93, 101), _rng.Range(27, 41));
        }
        else
        {
            var mates = _att.OnPitch
                .Where(p => p.Id != passer.Id && (p.Position != Position.GK || start.X < 40))
                .ToList();
            double preferredLength = 13 + (1 - _att.Team.Style.Possession) * 14 + _att.Directness * 6;

            receiver = _rng.PickWeighted(mates, mate =>
            {
                var position = DynamicPosition(_att, mate, start.X);
                double d = position.DistanceTo(start);
                double weight = Math.Exp(-Math.Pow(d - preferredLength, 2) / (2 * 11 * 11));
                double forward = position.X - start.X;
                if (forward > 0) weight *= 1 + _att.Directness * forward / 18;
                if (forward < -15) weight *= 0.5;
                return weight;
            });

            var receiverPosition = DynamicPosition(_att, receiver, start.X);
            target = new PitchPoint(receiverPosition.X + _rng.Normal(2, 4), receiverPosition.Y + _rng.Normal(0, 4));

            if (target.X > start.X + 15 && target.X > 70 && _rng.Chance(0.10 + _att.Directness * 0.10))
            {
                through = true;
                target = new PitchPoint(target.X + _rng.Range(4, 10), target.Y);
            }

            target = new PitchPoint(Math.Clamp(target.X, 1, 104), Math.Clamp(target.Y, 1, 67));
        }

        double distance = start.DistanceTo(target);
        double difficulty = FootballMath.PassDifficulty(start, target, pressured, through, cross);
        double successProbability = FootballMath.PassSuccessProbability(difficulty, passer.Attributes.Passing);
        if (target.InPenaltyArea) successProbability *= 0.8; // crowded box
        double ballSpeed = FootballMath.BallSpeedKmh(distance, cross, _rng.Normal(0, 6));
        double travelSeconds = distance / (ballSpeed / 3.6) * 1.15;

        var qualifiers = new List<string>();
        if (cross) qualifiers.Add("Cross");
        if (through) qualifiers.Add("Through ball");
        if (distance > 32) qualifiers.Add("Long ball");
        if (pressured) qualifiers.Add("Under pressure");
        if (target.X - start.X >= 10 && target.X > 35) qualifiers.Add("Progressive");

        bool success = _rng.Chance(successProbability);

        Add(E(EventType.Pass) with
        {
            TeamId = _att.Id,
            PlayerId = passer.Id,
            RelatedPlayerId = receiver.Id,
            Start = start.Round(),
            End = target.Round(),
            DistanceM = Math.Round(distance, 1),
            BallSpeedKmh = Math.Round(ballSpeed, 1),
            PassDifficulty = difficulty,
            Outcome = success ? EventOutcome.Success : EventOutcome.Fail,
            Qualifiers = qualifiers
        });
        Advance(travelSeconds);

        if (!success)
        {
            HandleFailedPass(target, through, receiver);
            return;
        }

        _carrier = receiver;
        _ball = target;
        _lastCompletedPass = (_att.Id, passer.Id, _t);

        if (through && _rng.Chance(0.6)) Sprint(receiver, _att, target);

        bool headerChance = cross && _rng.Chance(0.30);
        bool oneOnOne = through && target.InPenaltyArea && _rng.Chance(0.35);
        if (headerChance || oneOnOne)
        {
            Advance(_rng.Range(0.3, 1.0));
            if (headerChance) Shoot(BodyPart.Head, false, "Header from cross");
            else Shoot(FootChoice(), true, "One-on-one");
            return;
        }

        Advance(_rng.Range(0.5, 1.6));
    }

    private void HandleFailedPass(PitchPoint target, bool through, Player intendedReceiver)
    {
        var def = Opp(_att);

        if (through && _rng.Chance(0.25))
        {
            Add(E(EventType.Offside) with { TeamId = _att.Id, PlayerId = intendedReceiver.Id, Start = target.Round() });
            Advance(_rng.Range(10, 20));
            var taker = def.OnPitch.FirstOrDefault(p => p.Position == Position.CB) ?? def.Keeper;
            var spot = target.Mirror();
            Add(E(EventType.FreeKick) with { TeamId = def.Id, PlayerId = taker.Id, Start = spot.Round(), Qualifiers = new[] { "Offside" } });
            ChangePossession(def, taker, spot, "Offside");
            return;
        }

        if (!_rng.Chance(0.3))
        {
            var at = target.Mirror();
            var interceptor = NearestOutfield(def, at);

            if (target.InPenaltyArea && _rng.Chance(0.5))
            {
                Add(E(EventType.Clearance) with { TeamId = def.Id, PlayerId = interceptor.Id, Start = at.Round() });
                Advance(_rng.Range(1, 2));
                ContestClearance(def, at);
                return;
            }

            WinBall(def, interceptor, at, EventType.Interception);
            return;
        }

        // Ball out of play.
        if (target.X > 100)
        {
            Advance(_rng.Range(15, 30));
            var keeper = def.Keeper;
            var spot = new PitchPoint(6, 34);
            Add(E(EventType.GoalKick) with { TeamId = def.Id, PlayerId = keeper.Id, Start = spot });
            ChangePossession(def, keeper, spot, "Goal kick");
            return;
        }

        var throwAt = new PitchPoint(target.X, target.Y < 34 ? 0 : 68).Mirror();
        Advance(_rng.Range(8, 18));
        var thrower = NearestOutfield(def, throwAt);
        Add(E(EventType.ThrowIn) with { TeamId = def.Id, PlayerId = thrower.Id, Start = throwAt.Round() });
        ChangePossession(def, thrower, throwAt, "Throw-in");
    }

    /// <summary>A defender gets a block, tackle or clearance in before the attacker can shoot.</summary>
    private void DefendBox(TeamRuntime def)
    {
        var at = _ball.Mirror();
        var defender = NearestOutfield(def, at);
        Add(E(EventType.Clearance) with
        {
            TeamId = def.Id,
            PlayerId = defender.Id,
            RelatedPlayerId = _carrier.Id,
            Start = at.Round(),
            Qualifiers = new[] { "Box defending" }
        });
        Advance(_rng.Range(1, 2));

        if (_rng.Chance(0.15))
        {
            Corner();
            return;
        }

        ContestClearance(def, at);
    }

    /// <summary>The ball is hacked upfield: usually the defending side keeps it, sometimes the attack wins the second ball.</summary>
    private void ContestClearance(TeamRuntime def, PitchPoint fromInDefenceFrame)
    {
        var landing = new PitchPoint(
            Math.Clamp(fromInDefenceFrame.X + _rng.Range(20, 45), 1, 104),
            Math.Clamp(fromInDefenceFrame.Y + _rng.Normal(0, 12), 1, 67));

        if (_rng.Chance(0.7))
        {
            ChangePossession(def, NearestOutfield(def, landing), landing, "Clearance");
            return;
        }

        var attackFrame = landing.Mirror();
        _carrier = NearestOutfield(_att, attackFrame);
        _ball = attackFrame;
        Advance(_rng.Range(1.0, 2.0));
    }

    private void Carry()
    {
        double distance = Math.Clamp(_rng.Normal(8, 4), 2, 25);
        double angle = _rng.Normal(0, 0.55);
        var start = _ball;
        var end = new PitchPoint(
            Math.Clamp(start.X + distance * Math.Cos(angle), 1, 104),
            Math.Clamp(start.Y + distance * Math.Sin(angle), 1, 67));
        double actual = start.DistanceTo(end);
        double speed = _rng.Range(4.5, 6.5) + (_carrier.Attributes.Pace - 70) / 40.0;

        Add(E(EventType.Carry) with
        {
            TeamId = _att.Id,
            PlayerId = _carrier.Id,
            Start = start.Round(),
            End = end.Round(),
            DistanceM = Math.Round(actual, 1),
            Outcome = EventOutcome.Success
        });

        if (actual > 12 && _rng.Chance(0.25 + (_carrier.Attributes.Pace - 70) / 60.0))
            Sprint(_carrier, _att, end);

        _ball = end;
        Advance(actual / Math.Max(3, speed));
    }

    private void Dribble()
    {
        var def = Opp(_att);
        var defender = NearestOutfield(def, _ball.Mirror());
        double successProbability = Math.Clamp(0.52 + (_carrier.Attributes.Dribbling - defender.Attributes.Defending) / 150.0, 0.2, 0.85);
        var start = _ball;

        if (_rng.Chance(successProbability))
        {
            var end = new PitchPoint(
                Math.Clamp(start.X + _rng.Range(3, 8), 1, 104),
                Math.Clamp(start.Y + _rng.Normal(0, 3), 1, 67));
            Add(E(EventType.Dribble) with
            {
                TeamId = _att.Id,
                PlayerId = _carrier.Id,
                RelatedPlayerId = defender.Id,
                Start = start.Round(),
                End = end.Round(),
                DistanceM = Math.Round(start.DistanceTo(end), 1),
                Outcome = EventOutcome.Success
            });
            _ball = end;
            Advance(_rng.Range(1.5, 3.0));
            return;
        }

        Add(E(EventType.Dribble) with
        {
            TeamId = _att.Id,
            PlayerId = _carrier.Id,
            RelatedPlayerId = defender.Id,
            Start = start.Round(),
            Outcome = EventOutcome.Fail
        });
        Advance(_rng.Range(0.5, 1.2));

        if (_rng.Chance(0.2))
        {
            CommitFoul(def, defender, start);
            return;
        }

        WinBall(def, defender, start.Mirror(), EventType.Tackle);
    }

    private void Shoot(BodyPart bodyPart, bool bigChance, params string[] qualifiers)
    {
        var shooter = _carrier;
        var def = Opp(_att);
        var keeper = def.Keeper;
        var start = _ball;
        bool penalty = qualifiers.Contains("Penalty");

        double xg = penalty ? 0.78 : FootballMath.ExpectedGoals(start, bodyPart, bigChance);
        double speed = bodyPart == BodyPart.Head
            ? _rng.Range(38, 68)
            : Math.Clamp(_rng.Normal(88, 14) + (shooter.Attributes.Finishing - 70) * 0.4, 55, 132);
        double goalProbability = Math.Clamp(
            xg * (0.6 + shooter.Attributes.Finishing / 250.0) * (1.05 - keeper.Attributes.Goalkeeping / 1000.0),
            0.005, 0.95);

        var aim = new PitchPoint(105, _rng.Range(30.6, 37.4));
        EventOutcome outcome;
        if (_rng.Chance(goalProbability))
        {
            outcome = EventOutcome.Goal;
        }
        else
        {
            double roll = _rng.Next();
            outcome = roll < 0.36 ? EventOutcome.Saved : roll < 0.58 ? EventOutcome.Blocked : EventOutcome.OffTarget;
            if (penalty && outcome == EventOutcome.Blocked) outcome = EventOutcome.Saved;
            if (outcome == EventOutcome.OffTarget)
                aim = new PitchPoint(105, _rng.Chance(0.5) ? _rng.Range(24, 30) : _rng.Range(38, 44));
        }

        var shotQualifiers = qualifiers.ToList();
        if ((bigChance || xg >= 0.35) && !shotQualifiers.Contains("Big chance")) shotQualifiers.Add("Big chance");

        string? assisterId = null;
        if (_lastCompletedPass is { } last && last.TeamId == _att.Id && _t - last.Time < 12 && last.PlayerId != shooter.Id && !penalty)
            assisterId = last.PlayerId;

        Add(E(EventType.Shot) with
        {
            TeamId = _att.Id,
            PlayerId = shooter.Id,
            RelatedPlayerId = assisterId,
            Start = start.Round(),
            End = aim.Round(),
            DistanceM = Math.Round(start.DistanceToGoal, 1),
            BallSpeedKmh = Math.Round(speed, 1),
            Xg = xg,
            BodyPart = bodyPart,
            Outcome = outcome,
            Qualifiers = shotQualifiers
        });
        Advance(_rng.Range(0.4, 1.0));

        switch (outcome)
        {
            case EventOutcome.Goal:
                ScoreGoal(shooter, assisterId, xg);
                break;

            case EventOutcome.Saved:
                Add(E(EventType.Save) with
                {
                    TeamId = def.Id,
                    PlayerId = keeper.Id,
                    RelatedPlayerId = shooter.Id,
                    Start = new PitchPoint(2, 34),
                    Xg = xg
                });
                if (!penalty && _rng.Chance(0.32))
                {
                    Corner();
                }
                else
                {
                    Advance(_rng.Range(3, 8));
                    ChangePossession(def, keeper, new PitchPoint(5, 34), "Save");
                }
                break;

            case EventOutcome.Blocked:
                var blocker = NearestOutfield(def, start.Mirror());
                Add(E(EventType.Block) with
                {
                    TeamId = def.Id,
                    PlayerId = blocker.Id,
                    RelatedPlayerId = shooter.Id,
                    Start = start.Mirror().Round()
                });
                if (_rng.Chance(0.4)) Corner();
                else ChangePossession(def, blocker, start.Mirror(), "Blocked shot");
                break;

            default:
                Advance(_rng.Range(12, 25));
                var goalKickSpot = new PitchPoint(6, 34);
                Add(E(EventType.GoalKick) with { TeamId = def.Id, PlayerId = keeper.Id, Start = goalKickSpot });
                ChangePossession(def, keeper, goalKickSpot, "Goal kick");
                break;
        }
    }

    private void ScoreGoal(Player scorer, string? assisterId, double xg)
    {
        var scoring = _att;
        var conceding = Opp(scoring);
        scoring.Score++;

        Add(E(EventType.Goal) with
        {
            TeamId = scoring.Id,
            PlayerId = scorer.Id,
            RelatedPlayerId = assisterId,
            Start = _ball.Round(),
            Xg = xg,
            HomeScore = _home.Score,
            AwayScore = _away.Score
        });

        Advance(_rng.Range(50, 80)); // celebration and restart
        conceding.PressIntensity = Math.Min(0.95, conceding.PressIntensity + 0.1);
        KickOff(conceding);
    }

    private void Corner()
    {
        Advance(_rng.Range(12, 25));
        var taker = BestOutfield(_att, p => p.Attributes.Passing);
        var spot = new PitchPoint(105, _rng.Chance(0.5) ? 0 : 68);
        Add(E(EventType.Corner) with { TeamId = _att.Id, PlayerId = taker.Id, Start = spot });
        _carrier = taker;
        _ball = spot;
        Advance(_rng.Range(2, 5));
        Pass(pressured: false, forceCross: true);
    }

    private void CommitFoul(TeamRuntime def, Player fouler, PitchPoint at)
    {
        bool penalty = at.InPenaltyArea;
        Add(E(EventType.Foul) with
        {
            TeamId = def.Id,
            PlayerId = fouler.Id,
            RelatedPlayerId = _carrier.Id,
            Start = at.Mirror().Round(),
            Qualifiers = penalty ? new[] { "Penalty" } : Array.Empty<string>()
        });
        Advance(_rng.Range(2, 5));

        double cardProbability = penalty ? 0.35 : 0.13 + (at.X > 70 ? 0.05 : 0);
        if (_rng.Chance(cardProbability)) Book(def, fouler);
        Advance(_rng.Range(15, 35));

        if (penalty)
        {
            _carrier = BestOutfield(_att, p => p.Attributes.Finishing);
            _ball = new PitchPoint(94, 34);
            Shoot(BodyPart.RightFoot, true, "Penalty");
            return;
        }

        Add(E(EventType.FreeKick) with { TeamId = _att.Id, PlayerId = _carrier.Id, Start = at.Round() });
        _ball = at;

        if (at.DistanceToGoal < 28 && Math.Abs(at.Y - 34) < 14 && _rng.Chance(0.35))
        {
            _carrier = BestOutfield(_att, p => p.Attributes.Finishing + p.Attributes.Passing);
            Shoot(FootChoice(), false, "Direct free kick");
        }
    }

    private void Book(TeamRuntime team, Player player)
    {
        bool alreadyBooked = team.Booked.Contains(player.Id);
        bool straightRed = _rng.Chance(0.04);

        if (alreadyBooked || straightRed)
        {
            Add(E(EventType.RedCard) with
            {
                TeamId = team.Id,
                PlayerId = player.Id,
                Qualifiers = new[] { alreadyBooked ? "Second yellow" : "Straight red" }
            });
            if (player.Position != Position.GK && team.OnPitch.Count > 7)
            {
                team.OnPitch.RemoveAll(p => p.Id == player.Id);
                team.Slots.Remove(player.Id);
            }
            return;
        }

        team.Booked.Add(player.Id);
        Add(E(EventType.YellowCard) with { TeamId = team.Id, PlayerId = player.Id });
    }

    private void Sprint(Player player, TeamRuntime team, PitchPoint at)
    {
        double topSpeed = Math.Clamp(26.5 + (player.Attributes.Pace - 60) / 35.0 * 8.5 + _rng.Normal(0, 1.2), 24, 37.5);
        var tracker = _physical[player.Id];
        tracker.Sprints++;
        tracker.DistanceM += _rng.Range(15, 35);
        if (topSpeed > tracker.TopSpeedKmh) tracker.TopSpeedKmh = topSpeed;

        Add(E(EventType.Sprint) with
        {
            TeamId = team.Id,
            PlayerId = player.Id,
            Start = at.Round(),
            PlayerSpeedKmh = Math.Round(topSpeed, 1)
        });
    }

    // ------------------------------------------------------------------ possession helpers

    private void WinBall(TeamRuntime winner, Player player, PitchPoint at, EventType how, params string[] qualifiers)
    {
        Add(E(how) with
        {
            TeamId = winner.Id,
            PlayerId = player.Id,
            RelatedPlayerId = _carrier.Id,
            Start = at.Round(),
            Outcome = EventOutcome.Success,
            Qualifiers = qualifiers
        });
        ChangePossession(winner, player, at, how.ToString());
    }

    private void ChangePossession(TeamRuntime team, Player carrier, PitchPoint at, string reason)
    {
        _att = team;
        _carrier = carrier;
        _ball = at.Clamp();
        _lastCompletedPass = null;
        Add(E(EventType.PossessionChange) with
        {
            TeamId = team.Id,
            PlayerId = carrier.Id,
            Start = _ball.Round(),
            Qualifiers = new[] { reason }
        });
        Advance(_rng.Range(0.8, 2.0));
    }

    private Player NearestOutfield(TeamRuntime team, PitchPoint pointInTeamFrame) =>
        team.OnPitch
            .Where(p => p.Position != Position.GK)
            .Select(p => (Player: p, Distance: DynamicPosition(team, p, pointInTeamFrame.X).DistanceTo(pointInTeamFrame) + _rng.Range(0, 6)))
            .OrderBy(x => x.Distance)
            .First()
            .Player;

    private static Player BestOutfield(TeamRuntime team, Func<Player, int> score) =>
        team.OnPitch.Where(p => p.Position != Position.GK).OrderByDescending(score).First();

    private Player PickBoxTarget(TeamRuntime team, Player passer)
    {
        var candidates = team.OnPitch
            .Where(p => p.Id != passer.Id && p.Position is Position.ST or Position.AM or Position.LW or Position.RW or Position.CB or Position.CM)
            .ToList();
        if (candidates.Count == 0)
            candidates = team.OnPitch.Where(p => p.Id != passer.Id && p.Position != Position.GK).ToList();
        return _rng.PickWeighted(candidates, p => p.Attributes.Finishing + (p.Position == Position.ST ? 30 : 0));
    }

    /// <summary>Where a player is likely to be: base slot shifted up or down the pitch with the ball.</summary>
    private static PitchPoint DynamicPosition(TeamRuntime team, Player player, double ballXInTeamFrame)
    {
        var slot = team.Slots.TryGetValue(player.Id, out var s) ? s : new PitchPoint(50, 34);
        double shift = player.Position == Position.GK ? 0.12 : 0.45;
        // Capped at x = 91: attackers rarely receive unmarked inside the six-yard area.
        return new PitchPoint(Math.Clamp(slot.X + (ballXInTeamFrame - 52.5) * shift, 2, 91), slot.Y);
    }

    private BodyPart FootChoice() => _rng.Chance(0.72) ? BodyPart.RightFoot : BodyPart.LeftFoot;

    private TeamRuntime Opp(TeamRuntime team) => ReferenceEquals(team, _home) ? _away : _home;

    // ------------------------------------------------------------------ time, tracking, emission

    private void Advance(double seconds)
    {
        if (seconds <= 0) return;

        double fatigue = _period == 2 ? 0.94 : 1.0;
        foreach (var team in new[] { _home, _away })
        {
            foreach (var player in team.OnPitch)
            {
                double baseSpeed = player.Position == Position.GK ? 1.05 : 1.92;
                double stamina = 0.9 + player.Attributes.Stamina / 800.0;
                _physical[player.Id].DistanceM += baseSpeed * stamina * fatigue * seconds;
            }
        }

        _t += seconds;

        while (_t >= _nextTracking && _nextTracking <= _periodEnd + 60)
        {
            EmitTracking(_nextTracking);
            _nextTracking += _options.TrackingIntervalSeconds;
        }
    }

    private void EmitTracking(double at)
    {
        var rows = _home.OnPitch.Concat(_away.OnPitch)
            .Select(p =>
            {
                var tracker = _physical[p.Id];
                return new PlayerPhysical(p.Id, Math.Round(tracker.DistanceM), Math.Round(tracker.TopSpeedKmh, 1), tracker.Sprints);
            })
            .ToArray();

        Add(E(EventType.TrackingSnapshot, at) with { Tracking = rows });
    }

    private MatchEvent E(EventType type, double? at = null) => new()
    {
        MatchId = _info.MatchId,
        Sequence = ++_seq,
        Type = type,
        Period = _period,
        MatchSecond = Math.Round(at ?? _t, 1)
    };

    private void Add(MatchEvent matchEvent) => _events.Add(matchEvent);
}
