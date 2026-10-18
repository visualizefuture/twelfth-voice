using TwelfthVoice.Domain;

namespace TwelfthVoice.Intelligence;

/// <summary>
/// The Interpret and Explain stages of the pipeline. Feed it events in order; it keeps
/// running team and player statistics, rolling-window indicators (momentum, control vs chaos,
/// pressing, tempo) and returns an <see cref="Insight"/> whenever something worth showing on
/// screen happens, each with a plain-language reason and the evidence behind it.
/// </summary>
public sealed class MatchAnalyzer
{
    /// <summary>Sprint speed (km/h) that triggers an on-screen speed tag.</summary>
    public const double SpeedAlertKmh = 33.0;

    /// <summary>Distance (m) that triggers an on-screen distance tag.</summary>
    public const double DistanceAlertM = 10_000;

    private readonly MatchInfo _info;
    private readonly Dictionary<string, TeamStats> _teams = new();
    private readonly Dictionary<string, PlayerStats> _players = new();
    private readonly List<WindowItem> _window = new();
    private readonly Dictionary<string, double> _cooldowns = new();
    private readonly HashSet<string> _milestones = new();
    private readonly Dictionary<string, MatchEvent> _lastCompletedPassByTeam = new();
    private readonly Dictionary<string, MatchEvent> _lastShotByTeam = new();
    private readonly List<Insight> _insights = new();

    private int _period;
    private double _periodStart;
    private double _matchSecond;
    private double _lastPossessionTick;
    private double _lastTrackingSecond;
    private string? _possessionTeam;
    private int _homeScore;
    private int _awayScore;
    private int _insightSequence;
    private double _nextTrendCheck;
    private int _momentumSide;
    private string _lastControlLabel = "";
    private string _lastTempo = "";
    private double _matchTopSpeed;

    private sealed record WindowItem(double T, string TeamId, string Kind, double Value);

    public MatchAnalyzer(MatchInfo info)
    {
        _info = info;
        foreach (var team in new[] { info.Home, info.Away })
        {
            _teams[team.Id] = new TeamStats { TeamId = team.Id, Name = team.Name };
            for (int i = 0; i < team.Squad.Count; i++)
            {
                var p = team.Squad[i];
                _players[p.Id] = new PlayerStats
                {
                    PlayerId = p.Id,
                    TeamId = team.Id,
                    Name = p.Name,
                    ShortName = p.ShortName,
                    ShirtNumber = p.ShirtNumber,
                    Position = p.Position,
                    OnPitch = i < 11
                };
            }
        }
    }

    public MatchInfo Info => _info;

    public IReadOnlyList<Insight> Insights => _insights;

    public int HomeScore => _homeScore;
    public int AwayScore => _awayScore;

    /// <summary>Processes one event and returns any insights it triggered.</summary>
    public IReadOnlyList<Insight> Process(MatchEvent e)
    {
        var produced = new List<Insight>();

        if (e.Period != _period)
        {
            _period = e.Period;
            _periodStart = MatchClock.PeriodStart(e.Period);
            _window.Clear();
            _possessionTeam = null;
            _lastPossessionTick = e.MatchSecond;
            _lastTrackingSecond = _periodStart;
            _nextTrendCheck = _periodStart + 480;
        }
        else
        {
            AccruePossession(e.MatchSecond);
        }

        _matchSecond = e.MatchSecond;
        Apply(e, produced);
        UpdatePossessionOwner(e);

        if (_matchSecond >= _nextTrendCheck && e.Type != EventType.PeriodEnd && e.Type != EventType.MatchEnd)
        {
            _nextTrendCheck = _matchSecond + 60;
            _window.RemoveAll(w => w.T < _matchSecond - 660);
            DetectTrends(e, produced);
        }

        _insights.AddRange(produced);
        return produced;
    }

    /// <summary>Processes a whole match at once (for recaps and tests).</summary>
    public IReadOnlyList<Insight> ProcessAll(IEnumerable<MatchEvent> events)
    {
        foreach (var e in events) Process(e);
        return _insights;
    }

    /// <summary>A copy of the current statistics and game state, safe to serialise or hand to an agent.</summary>
    public MatchSnapshot Snapshot()
    {
        RecomputeTotals();
        int period = Math.Max(1, _period);
        return new MatchSnapshot
        {
            MatchId = _info.MatchId,
            Period = period,
            MatchSecond = _matchSecond,
            Clock = MatchClock.Format(period, _matchSecond),
            HomeScore = _homeScore,
            AwayScore = _awayScore,
            Home = _teams[_info.Home.Id] with { },
            Away = _teams[_info.Away.Id] with { },
            State = ComputeState(_matchSecond),
            Players = _players.Values.Select(p => p with { }).ToList()
        };
    }

    // ================================================================== event handling

    private void Apply(MatchEvent e, List<Insight> produced)
    {
        switch (e.Type)
        {
            case EventType.MatchStart:
                produced.Add(KickoffInsight(e));
                break;

            case EventType.Pass:
                OnPass(e, produced);
                break;

            case EventType.Carry:
                P(e.PlayerId).Touches++;
                Window(e, "action");
                break;

            case EventType.Dribble:
            {
                var p = P(e.PlayerId);
                p.Touches++;
                p.DribblesAttempted++;
                Window(e, "action");
                if (e.Outcome == EventOutcome.Success)
                {
                    p.DribblesCompleted++;
                    Window(e, "dribble_ok");
                }
                break;
            }

            case EventType.Shot:
                OnShot(e, produced);
                break;

            case EventType.Goal:
                OnGoal(e, produced);
                break;

            case EventType.Save:
                P(e.PlayerId).Saves++;
                T(e.TeamId).Saves++;
                break;

            case EventType.Block:
                P(e.PlayerId).Blocks++;
                T(e.TeamId).Blocks++;
                break;

            case EventType.Clearance:
                P(e.PlayerId).Clearances++;
                T(e.TeamId).Clearances++;
                break;

            case EventType.Tackle:
            case EventType.Interception:
            {
                var p = P(e.PlayerId);
                var t = T(e.TeamId);
                if (e.Type == EventType.Tackle) { p.Tackles++; t.Tackles++; }
                else { p.Interceptions++; t.Interceptions++; }
                Window(e, "regain");
                if (e.Start is { } at && at.InOpponentHalf)
                {
                    t.HighRegains++;
                    Window(e, "regain_high");
                }
                break;
            }

            case EventType.Pressure:
                P(e.PlayerId).Pressures++;
                T(e.TeamId).Pressures++;
                Window(e, "pressure");
                break;

            case EventType.Foul:
                P(e.PlayerId).FoulsCommitted++;
                T(e.TeamId).Fouls++;
                if (e.RelatedPlayerId is not null) P(e.RelatedPlayerId).FoulsWon++;
                Window(e, "foul");
                break;

            case EventType.YellowCard:
                OnYellow(e, produced);
                break;

            case EventType.RedCard:
                OnRed(e, produced);
                break;

            case EventType.Corner:
                T(e.TeamId).Corners++;
                Window(e, "corner");
                break;

            case EventType.Offside:
                T(e.TeamId).Offsides++;
                break;

            case EventType.Substitution:
                OnSubstitution(e, produced);
                break;

            case EventType.Sprint:
                OnSprint(e, produced);
                break;

            case EventType.TrackingSnapshot:
                OnTracking(e, produced);
                break;

            case EventType.PossessionChange:
                Window(e, "turnover");
                break;

            case EventType.PeriodEnd:
                if (e.HomeScore is { } hs) _homeScore = hs;
                if (e.AwayScore is { } aws) _awayScore = aws;
                produced.Add(PeriodSummary(e));
                break;
        }
    }

    private void OnPass(MatchEvent e, List<Insight> produced)
    {
        var t = T(e.TeamId);
        var p = P(e.PlayerId);
        t.PassesAttempted++;
        p.PassesAttempted++;
        p.Touches++;
        if (e.Has("Cross")) t.Crosses++;

        bool completed = e.Outcome == EventOutcome.Success;
        Window(e, completed ? "pass_ok" : "pass_fail");
        Window(e, "action");
        if (!completed) return;

        t.PassesCompleted++;
        p.PassesCompleted++;
        double difficulty = e.PassDifficulty ?? 0;
        p.TotalPassDifficulty += difficulty;
        double distance = e.DistanceM ?? 0;
        if (distance > p.LongestPassM) p.LongestPassM = distance;

        if (e.Has("Progressive"))
        {
            t.ProgressivePasses++;
            p.ProgressivePasses++;
        }

        if (e.Start is { } s && e.End is { } end)
        {
            if (!s.InFinalThird && end.InFinalThird)
            {
                t.FinalThirdEntries++;
                Window(e, "f3");
            }
            if (!s.InPenaltyArea && end.InPenaltyArea)
            {
                t.BoxEntries++;
                Window(e, "box");
            }
        }

        _lastCompletedPassByTeam[t.TeamId] = e;

        if ((p.PassesCompleted is 50 or 75 or 100) && _milestones.Add($"passes{p.PassesCompleted}:{p.PlayerId}"))
        {
            produced.Add(Make(InsightKind.PlayerMilestone, e, 0.3,
                $"{p.Name}: {p.PassesCompleted} completed passes",
                $"{p.ShortName} is the hub of {TeamShort(p.TeamId)}'s play, completing {Narration.Pct(p.PassAccuracy)} of passes with an average difficulty of {p.AveragePassDifficulty:0.0}/10.",
                new OverlayHint("player-tag-stat", "near-player", 5, 30),
                analyst: $"{p.PassesCompleted}/{p.PassesAttempted} passes · {p.ProgressivePasses} progressive · longest {p.LongestPassM:0} m",
                teamId: p.TeamId,
                players: new[] { p.PlayerId },
                evidence: new() { ["passes_completed"] = p.PassesCompleted, ["pass_accuracy"] = p.PassAccuracy }));
        }

        if (difficulty >= 7.5 && Cool("pass-of-note", 120))
        {
            var receiver = e.RelatedPlayerId is null ? null : P(e.RelatedPlayerId);
            var reasons = new List<string>();
            if (e.Has("Under pressure")) reasons.Add("played under pressure");
            if (e.Has("Through ball")) reasons.Add("threaded through the defensive line");
            if (e.Has("Cross")) reasons.Add("whipped in as a cross");
            if (e.End is { InPenaltyArea: true }) reasons.Add("landing in the penalty area");
            if (distance > 32) reasons.Add($"travelling {distance:0} m");
            string reasonText = reasons.Count == 0 ? "a long, forward ball" : string.Join(", ", reasons);
            double typicalSuccess = FootballMath.PassSuccessProbability(difficulty, 70);

            produced.Add(Make(InsightKind.PassOfNote, e, 0.45 + (difficulty - 7.5) * 0.08,
                receiver is null
                    ? $"Quality pass from {p.Name}"
                    : $"{p.ShortName} finds {receiver.ShortName} with a {FootballMath.DescribeDifficulty(difficulty)} pass",
                $"Difficulty {difficulty:0.0}/10: {reasonText}. An average passer completes a ball like that only about {Narration.Pct(typicalSuccess)} of the time.",
                new OverlayHint("pass-graphic", "pitch-overlay", 6, 55),
                analyst: $"Distance {distance:0.0} m · Ball speed {e.BallSpeedKmh ?? 0:0} km/h · Difficulty {difficulty:0.0} · {p.ShortName} {p.PassesCompleted}/{p.PassesAttempted} ({Narration.Pct(p.PassAccuracy)})",
                teamId: t.TeamId,
                players: receiver is null ? new[] { p.PlayerId } : new[] { p.PlayerId, receiver.PlayerId },
                evidence: new()
                {
                    ["difficulty"] = difficulty,
                    ["distance_m"] = distance,
                    ["ball_speed_kmh"] = e.BallSpeedKmh ?? 0,
                    ["typical_completion"] = Math.Round(typicalSuccess, 2)
                }));
        }
    }

    private void OnShot(MatchEvent e, List<Insight> produced)
    {
        var t = T(e.TeamId);
        var p = P(e.PlayerId);
        double xg = e.Xg ?? 0;
        double speed = e.BallSpeedKmh ?? 0;

        t.Shots++;
        p.Shots++;
        p.Touches++;
        t.Xg = Math.Round(t.Xg + xg, 3);
        p.Xg = Math.Round(p.Xg + xg, 3);
        if (xg >= 0.35) t.BigChances++;
        if (speed > p.FastestShotKmh) p.FastestShotKmh = speed;

        bool onTarget = e.Outcome is EventOutcome.Goal or EventOutcome.Saved;
        if (onTarget)
        {
            t.ShotsOnTarget++;
            p.ShotsOnTarget++;
        }

        Window(e, "shot", xg);
        Window(e, "action");
        _lastShotByTeam[t.TeamId] = e;

        if (e.RelatedPlayerId is not null && _players.TryGetValue(e.RelatedPlayerId, out var creator))
        {
            creator.KeyPasses++;
            creator.XgAssisted = Math.Round(creator.XgAssisted + xg, 3);
        }

        if (e.Outcome == EventOutcome.Goal) return; // the Goal event tells this story

        var opponent = T(_info.Opponent(t.TeamId).Id);
        bool penalty = e.Has("Penalty");

        if (xg >= 0.3)
        {
            string keeper = OpposingKeeperShortName(t.TeamId);
            string headline = penalty
                ? e.Outcome == EventOutcome.Saved ? $"Penalty saved! {keeper} denies {p.ShortName}" : $"Penalty missed by {p.ShortName}"
                : e.Outcome switch
                {
                    EventOutcome.Saved => $"Big chance! {keeper} denies {p.ShortName}",
                    EventOutcome.Blocked => $"Big chance blocked — {p.ShortName} is denied",
                    _ => $"Big chance! {p.ShortName} fires wide"
                };

            produced.Add(Make(InsightKind.BigChance, e, 0.55 + xg * 0.4, headline,
                $"That chance was worth {xg:0.00} xG — players score from there {Narration.Odds(xg)} times. {TeamShort(t.TeamId)} have now created {t.Xg:0.00} xG to {opponent.Xg:0.00}.",
                new OverlayHint("chance-card", "lower-third", 7, 70),
                analyst: $"xG {xg:0.00} · {e.DistanceM ?? 0:0.0} m out · {e.BodyPart} · {speed:0} km/h · Team xG {t.Xg:0.00}–{opponent.Xg:0.00}",
                teamId: t.TeamId,
                players: new[] { p.PlayerId },
                evidence: new() { ["xg"] = xg, ["shot_speed_kmh"] = speed, ["team_xg"] = t.Xg, ["opponent_xg"] = opponent.Xg }));
        }
        else if (speed >= 115 && Cool("shot-speed", 300))
        {
            produced.Add(Make(InsightKind.SpeedAlert, e, 0.35,
                $"{p.ShortName} unleashes a {speed:0} km/h strike",
                $"One of the hardest-hit shots you'll see — but from {e.DistanceM ?? 0:0} m it was only a {xg:0.00} xG chance.",
                new OverlayHint("shot-speed", "near-player", 5, 45),
                analyst: $"Shot speed {speed:0.0} km/h · xG {xg:0.00} · Outcome {e.Outcome}",
                teamId: t.TeamId,
                players: new[] { p.PlayerId },
                evidence: new() { ["shot_speed_kmh"] = speed, ["xg"] = xg }));
        }
    }

    private void OnGoal(MatchEvent e, List<Insight> produced)
    {
        var t = T(e.TeamId);
        var scorer = P(e.PlayerId);
        t.Goals++;
        scorer.Goals++;
        if (e.HomeScore is { } hs) _homeScore = hs;
        if (e.AwayScore is { } aws) _awayScore = aws;
        _possessionTeam = null;

        PlayerStats? assister = null;
        if (e.RelatedPlayerId is not null && _players.TryGetValue(e.RelatedPlayerId, out var a))
        {
            assister = a;
            a.Assists++;
        }

        bool home = _info.IsHome(t.TeamId);
        int teamGoals = home ? _homeScore : _awayScore;
        int opponentGoals = home ? _awayScore : _homeScore;
        string teamShort = TeamShort(t.TeamId);
        string opponentShort = _info.Opponent(t.TeamId).ShortName;

        string state = teamGoals == opponentGoals ? $"{teamShort} draw level"
            : teamGoals == 1 && opponentGoals == 0 ? $"{teamShort} open the scoring"
            : teamGoals == opponentGoals + 1 ? $"{teamShort} go ahead"
            : teamGoals > opponentGoals ? $"{teamShort} extend their lead"
            : $"{teamShort} pull one back";

        double xg = e.Xg ?? 0;
        _lastShotByTeam.TryGetValue(t.TeamId, out var shot);
        double speed = shot?.BallSpeedKmh ?? 0;

        var why = new List<string> { $"{state}." };

        if (shot is not null && shot.Has("Penalty"))
            why.Add($"Penalties are converted about {Narration.Pct(0.78)} of the time — {scorer.ShortName} made no mistake.");
        else if (xg < 0.1)
            why.Add($"A low-percentage finish: chances like that ({xg:0.00} xG) go in {Narration.Odds(xg)}.");
        else if (xg >= 0.5)
            why.Add($"A high-quality chance ({xg:0.00} xG) — the kind teams expect to convert.");
        else
            why.Add($"A {xg:0.00} xG chance, scored roughly {Narration.Pct(xg)} of the time.");

        if (assister is not null && _lastCompletedPassByTeam.TryGetValue(t.TeamId, out var pass) && pass.PlayerId == assister.PlayerId)
        {
            double d = pass.PassDifficulty ?? 0;
            why.Add($"{assister.ShortName}'s {FootballMath.DescribeDifficulty(d)} pass ({pass.DistanceM ?? 0:0} m, difficulty {d:0.0}/10) set it up.");
        }

        double momentum = MomentumFor(t.TeamId, _matchSecond - 20);
        if (momentum <= -35) why.Add($"It comes against the run of play — {opponentShort} had been on top.");
        else if (momentum >= 35) why.Add($"The pressure had been building: {teamShort} dominated the previous five minutes.");

        if (scorer.Goals == 2) why.Add($"That's {scorer.ShortName}'s second of the game.");
        if (scorer.Goals == 3) why.Add($"Hat-trick for {scorer.ShortName}!");

        var players = assister is null ? new[] { scorer.PlayerId } : new[] { scorer.PlayerId, assister.PlayerId };
        var sources = shot is null ? new[] { e.Sequence } : new[] { shot.Sequence, e.Sequence };

        produced.Add(Make(InsightKind.Goal, e, 1.0,
            $"GOAL! {scorer.Name} — {Narration.ScoreLine(_info, _homeScore, _awayScore)}",
            string.Join(" ", why),
            new OverlayHint("goal-banner", "full-width", 10, 100),
            analyst: $"xG {xg:0.00} · Shot {speed:0} km/h · {shot?.BodyPart?.ToString() ?? "-"} · Match xG {T(_info.Home.Id).Xg:0.00}–{T(_info.Away.Id).Xg:0.00} · Momentum {momentum:+0;-0;0}",
            teamId: t.TeamId,
            players: players,
            evidence: new()
            {
                ["xg"] = xg,
                ["shot_speed_kmh"] = speed,
                ["momentum_for_team"] = momentum,
                ["home_score"] = _homeScore,
                ["away_score"] = _awayScore
            },
            sources: sources));
    }

    private void OnYellow(MatchEvent e, List<Insight> produced)
    {
        var p = P(e.PlayerId);
        var t = T(e.TeamId);
        p.YellowCards++;
        t.YellowCards++;
        string role = p.Position is Position.CB or Position.DM or Position.RB or Position.LB
            ? " For a defender that changes how aggressively they can engage."
            : "";
        produced.Add(Make(InsightKind.Card, e, 0.4,
            $"Yellow card: {p.Name} ({TeamShort(t.TeamId)})",
            $"{p.ShortName} must be careful now — another booking means a red.{role} {TeamShort(t.TeamId)} have {t.YellowCards} booking{(t.YellowCards == 1 ? "" : "s")} and {t.Fouls} fouls.",
            new OverlayHint("card-alert", "lower-third", 6, 60),
            teamId: t.TeamId,
            players: new[] { p.PlayerId },
            evidence: new() { ["team_yellows"] = t.YellowCards, ["team_fouls"] = t.Fouls }));
    }

    private void OnRed(MatchEvent e, List<Insight> produced)
    {
        var p = P(e.PlayerId);
        var t = T(e.TeamId);
        p.RedCards++;
        t.RedCards++;
        p.OnPitch = false;
        double minutesLeft = Math.Max(0, MatchClock.FullLength - e.MatchSecond) / 60;
        string how = e.Has("Second yellow") ? "a second yellow" : "a straight red";
        produced.Add(Make(InsightKind.Card, e, 0.9,
            $"RED CARD! {p.Name} is sent off — {TeamShort(t.TeamId)} down to ten",
            $"{p.ShortName} goes for {how}. With about {minutesLeft:0} minutes left, {TeamShort(t.TeamId)} will likely sit deeper and cede the ball; expect {_info.Opponent(t.TeamId).ShortName} to see more possession and more chances.",
            new OverlayHint("card-alert", "lower-third", 8, 85),
            teamId: t.TeamId,
            players: new[] { p.PlayerId },
            evidence: new() { ["minutes_remaining"] = Math.Round(minutesLeft) }));
    }

    private void OnSubstitution(MatchEvent e, List<Insight> produced)
    {
        var off = P(e.PlayerId);
        off.OnPitch = false;
        PlayerStats? on = null;
        if (e.RelatedPlayerId is not null)
        {
            on = P(e.RelatedPlayerId);
            on.OnPitch = true;
            on.EnteredAt = e.MatchSecond;
        }

        produced.Add(Make(InsightKind.Substitution, e, 0.25,
            on is null ? $"{off.Name} comes off" : $"Substitution {TeamShort(off.TeamId)}: {on.Name} replaces {off.Name}",
            $"{off.ShortName} leaves after {off.PassesCompleted}/{off.PassesAttempted} passes and {off.DistanceM / 1000:0.0} km covered (rating {off.Rating:0.0}).",
            new OverlayHint("sub-board", "lower-third", 6, 40),
            teamId: off.TeamId,
            players: on is null ? new[] { off.PlayerId } : new[] { off.PlayerId, on.PlayerId },
            evidence: new() { ["rating_off"] = off.Rating, ["distance_km_off"] = Math.Round(off.DistanceM / 1000, 2) }));
    }

    private void OnSprint(MatchEvent e, List<Insight> produced)
    {
        var p = P(e.PlayerId);
        double speed = e.PlayerSpeedKmh ?? 0;
        if (speed > p.TopSpeedKmh) p.TopSpeedKmh = speed;
        bool matchBest = speed > _matchTopSpeed;
        double previousBest = _matchTopSpeed;
        if (matchBest) _matchTopSpeed = speed;

        if (speed < SpeedAlertKmh || !Cool($"speed:{p.PlayerId}", 900)) return;

        string context = matchBest
            ? "the fastest sprint of the match so far"
            : $"just off the match best of {previousBest:0.0} km/h";
        produced.Add(Make(InsightKind.SpeedAlert, e, 0.35,
            $"{p.Name} · {speed:0.0} km/h",
            $"That's {context}. Raw pace like this lets {p.ShortName} get in behind and turn a half-chance into a run on goal.",
            new OverlayHint("player-tag-speed", "near-player", 5, 50),
            analyst: $"Top speed {speed:0.0} km/h · threshold {SpeedAlertKmh:0} km/h · sprints so far {p.Sprints}",
            teamId: p.TeamId,
            players: new[] { p.PlayerId },
            evidence: new() { ["speed_kmh"] = speed, ["match_top_speed_kmh"] = _matchTopSpeed }));
    }

    private void OnTracking(MatchEvent e, List<Insight> produced)
    {
        if (e.Tracking is null) return;
        double minutesSinceLast = Math.Max(0, e.MatchSecond - _lastTrackingSecond) / 60;
        _lastTrackingSecond = e.MatchSecond;

        foreach (var row in e.Tracking)
        {
            if (!_players.TryGetValue(row.PlayerId, out var p)) continue;
            p.DistanceM = row.DistanceM;
            p.Sprints = row.Sprints;
            if (row.TopSpeedKmh > p.TopSpeedKmh) p.TopSpeedKmh = row.TopSpeedKmh;
            p.MinutesPlayed = Math.Round(p.MinutesPlayed + minutesSinceLast, 1);

            if (p.DistanceM >= DistanceAlertM && _milestones.Add($"dist10k:{p.PlayerId}"))
            {
                int reached = _players.Values.Count(x => x.DistanceM >= DistanceAlertM);
                string rank = reached == 1 ? "the first player to get there" : $"player number {reached} to reach it";
                produced.Add(Make(InsightKind.DistanceMilestone, e, 0.3,
                    $"{p.Name} · 10 km covered",
                    $"{p.ShortName} is {rank} — a measure of {TeamShort(p.TeamId)}'s work rate off the ball.",
                    new OverlayHint("player-tag-distance", "near-player", 5, 35),
                    analyst: $"Distance {p.DistanceM / 1000:0.00} km · sprints {p.Sprints} · top speed {p.TopSpeedKmh:0.0} km/h",
                    teamId: p.TeamId,
                    players: new[] { p.PlayerId },
                    evidence: new() { ["distance_m"] = p.DistanceM, ["sprints"] = p.Sprints }));
            }
        }
    }

    // ================================================================== trends: the "why" over time

    private void DetectTrends(MatchEvent e, List<Insight> produced)
    {
        var state = ComputeState(_matchSecond);
        string homeId = _info.Home.Id;
        string awayId = _info.Away.Id;

        // 1. Momentum swings
        int side = state.Momentum >= 45 ? 1 : state.Momentum <= -45 ? -1 : 0;
        if (side != 0 && side != _momentumSide && Cool("momentum", 480))
        {
            _momentumSide = side;
            produced.Add(MomentumInsight(e, side > 0 ? homeId : awayId, state));
        }
        else if (Math.Abs(state.Momentum) < 15)
        {
            _momentumSide = 0;
        }

        // 2. Control vs chaos
        if (_lastControlLabel.Length > 0 && state.ControlLabel != _lastControlLabel && Cool("control", 600))
            produced.Add(ControlInsight(e, state));
        _lastControlLabel = state.ControlLabel;

        // 3. Pressing changes (needs two full windows)
        if (_matchSecond - _periodStart >= 600)
        {
            foreach (var teamId in new[] { homeId, awayId })
            {
                double current = Rate(teamId, "pressure", _matchSecond - 300, _matchSecond);
                double previous = Rate(teamId, "pressure", _matchSecond - 600, _matchSecond - 300);
                if (current - previous >= 0.7 && current >= 1.6 && Cool($"press:{teamId}", 600))
                    produced.Add(PressingInsight(e, teamId, current, previous));
            }
        }

        // 4. Tempo
        if (_lastTempo.Length > 0 && state.Tempo != _lastTempo && Cool("tempo", 900))
        {
            produced.Add(Make(InsightKind.TempoShift, e, 0.3,
                $"Tempo: {state.Tempo.ToLowerInvariant()}",
                $"{state.ActionsPerMinute:0.0} on-ball actions a minute over the last five minutes (was \"{_lastTempo.ToLowerInvariant()}\"). " +
                (state.Tempo == "High tempo" ? "Quick play stretches tired legs and opens space." : state.Tempo == "Slow build-up" ? "Patient circulation: one side is happy to wait for an opening." : "A measured rhythm, neither side forcing it."),
                new OverlayHint("stat-card", "lower-third", 5, 30),
                evidence: new() { ["actions_per_minute"] = state.ActionsPerMinute }));
        }
        _lastTempo = state.Tempo;
    }

    private Insight MomentumInsight(MatchEvent e, string teamId, GameState state)
    {
        string oppId = _info.Opponent(teamId).Id;
        double from = Math.Max(_periodStart, _matchSecond - 300);
        int f3 = Count(teamId, "f3", from), oppF3 = Count(oppId, "f3", from);
        int box = Count(teamId, "box", from), oppBox = Count(oppId, "box", from);
        int shots = Count(teamId, "shot", from);
        double xg = _window.Where(w => w.TeamId == teamId && w.Kind == "shot" && w.T >= from).Sum(w => w.Value);
        int corners = Count(teamId, "corner", from);

        bool home = _info.IsHome(teamId);
        int goalsFor = home ? _homeScore : _awayScore;
        int goalsAgainst = home ? _awayScore : _homeScore;
        string stakes = goalsFor < goalsAgainst ? "An equaliser could be coming."
            : goalsFor == goalsAgainst ? "This is the spell where a winner can arrive."
            : "They're pushing to put the game to bed.";

        string shortName = TeamShort(teamId);
        return Make(InsightKind.MomentumShift, e, 0.6,
            $"Momentum: {shortName} on top",
            $"In the last five minutes {shortName} have made {f3} final-third entries to {oppF3} and reached the box {box} times to {oppBox}, with {shots} shot{(shots == 1 ? "" : "s")} ({xg:0.00} xG) and {corners} corner{(corners == 1 ? "" : "s")}. {stakes}",
            new OverlayHint("momentum-bar", "top-bar", 10, 60),
            analyst: $"Momentum index {state.Momentum:+0;-0;0} (home +) · final-third entries {f3}–{oppF3} · box entries {box}–{oppBox} · xG last 5' {xg:0.00}",
            teamId: teamId,
            evidence: new()
            {
                ["momentum"] = state.Momentum,
                ["final_third_entries"] = f3,
                ["opponent_final_third_entries"] = oppF3,
                ["box_entries"] = box,
                ["shots"] = shots,
                ["xg"] = Math.Round(xg, 2)
            });
    }

    private Insight ControlInsight(MatchEvent e, GameState state)
    {
        var previous = Control(_matchSecond - 600, _matchSecond - 300);
        string? owner = state.ControllingTeamId is null ? null : TeamShort(state.ControllingTeamId);

        (string headline, string why) = state.ControlLabel switch
        {
            "Chaotic" => ("Chaos: the game has broken up",
                $"Pass completion has dropped to {Narration.Pct(state.PassCompletionLast5)} (from {Narration.Pct(previous.Completion)}) and the ball is changing hands {state.TurnoversPerMinute:0.0} times a minute (was {previous.TurnoversPerMinute:0.0}). Chaos favours quick, direct teams — watch for transitions."),
            "Controlled" => (owner is null ? "The game settles down" : $"Control: {owner} take charge",
                $"{owner ?? "Both sides"} have calmed things down: {Narration.Pct(state.PassCompletionLast5)} pass completion and only {state.TurnoversPerMinute:0.0} turnovers a minute." +
                (owner is null ? "" : $" {owner} have {Narration.Pct(_info.IsHome(state.ControllingTeamId!) ? state.HomeShareOfPassesLast5 : 1 - state.HomeShareOfPassesLast5)} of the completed passes in this spell.")),
            _ => ("The contest tightens",
                $"Neither side is in control: {Narration.Pct(state.PassCompletionLast5)} pass completion with {state.TurnoversPerMinute:0.0} turnovers a minute. Small moments will decide this phase.")
        };

        return Make(InsightKind.ControlShift, e, 0.5, headline, why,
            new OverlayHint("control-meter", "top-right", 8, 50),
            analyst: $"Control index {state.ControlIndex:0} ({state.ControlLabel}) · was {previous.Index:0} · completion {Narration.Pct(state.PassCompletionLast5)} · turnovers/min {state.TurnoversPerMinute:0.0}",
            teamId: state.ControllingTeamId,
            evidence: new()
            {
                ["control_index"] = state.ControlIndex,
                ["previous_control_index"] = previous.Index,
                ["pass_completion"] = state.PassCompletionLast5,
                ["turnovers_per_minute"] = state.TurnoversPerMinute
            });
    }

    private Insight PressingInsight(MatchEvent e, string teamId, double current, double previous)
    {
        double from = Math.Max(_periodStart, _matchSecond - 300);
        int highRegains = Count(teamId, "regain_high", from);
        bool home = _info.IsHome(teamId);
        int goalsFor = home ? _homeScore : _awayScore;
        int goalsAgainst = home ? _awayScore : _homeScore;
        string context = goalsFor < goalsAgainst ? " Classic chasing-the-game behaviour."
            : goalsFor > goalsAgainst ? " Unusual for a team in front — they want a second."
            : "";
        string shortName = TeamShort(teamId);

        return Make(InsightKind.PressingShift, e, 0.55,
            $"{shortName} turn up the pressure",
            $"{shortName} are pressing {current:0.0} times a minute, up from {previous:0.0}, and have won the ball back in the opponent's half {highRegains} time{(highRegains == 1 ? "" : "s")} in the last five minutes.{context}",
            new OverlayHint("pressure-graphic", "lower-third", 7, 55),
            analyst: $"Pressures/min {previous:0.0} → {current:0.0} · high regains (5') {highRegains}",
            teamId: teamId,
            evidence: new() { ["pressures_per_minute"] = current, ["previous_pressures_per_minute"] = previous, ["high_regains"] = highRegains });
    }

    private Insight KickoffInsight(MatchEvent e)
    {
        var home = _info.Home;
        var away = _info.Away;
        return Make(InsightKind.Kickoff, e, 0.2,
            $"Kick-off: {home.Name} v {away.Name}",
            $"{home.ShortName} ({home.Formation}, {Narration.StyleDescription(home.Style)}) host {away.ShortName} ({away.Formation}, {Narration.StyleDescription(away.Style)}) at {_info.Venue}.",
            new OverlayHint("match-intro", "full-width", 8, 80),
            analyst: $"Referee {_info.Referee} · {_info.Weather}");
    }

    private Insight PeriodSummary(MatchEvent e)
    {
        RecomputeTotals();
        var h = T(_info.Home.Id);
        var a = T(_info.Away.Id);
        bool fullTime = e.Period >= 2;
        string scoreLine = Narration.ScoreLine(_info, _homeScore, _awayScore);

        return Make(fullTime ? InsightKind.FullTime : InsightKind.HalfTime, e, fullTime ? 0.95 : 0.8,
            fullTime ? $"Full-time: {scoreLine}" : $"Half-time: {scoreLine}",
            Narration.BalanceOfPlay(h, a, _homeScore, _awayScore, _info.Home.ShortName, _info.Away.ShortName),
            new OverlayHint("summary-panel", "centre", 15, 90),
            analyst: $"Shots {h.Shots}–{a.Shots} · On target {h.ShotsOnTarget}–{a.ShotsOnTarget} · xG {h.Xg:0.00}–{a.Xg:0.00} · Possession {h.PossessionPct:0}–{a.PossessionPct:0}% · Pass acc. {Narration.Pct(h.PassAccuracy)}–{Narration.Pct(a.PassAccuracy)} · Pressures {h.Pressures}–{a.Pressures}",
            evidence: new()
            {
                ["home_xg"] = h.Xg,
                ["away_xg"] = a.Xg,
                ["home_possession"] = h.PossessionPct,
                ["home_shots"] = h.Shots,
                ["away_shots"] = a.Shots
            });
    }

    // ================================================================== rolling-window maths

    private GameState ComputeState(double now)
    {
        string homeId = _info.Home.Id;
        string awayId = _info.Away.Id;
        double from = Math.Max(_periodStart, now - 300);
        double minutes = Math.Max(1, (now - from) / 60);
        var recent = _window.Where(w => w.T >= from && w.T <= now).ToList();

        double Threat(string teamId) => recent.Where(w => w.TeamId == teamId).Sum(w => w.Kind switch
        {
            "f3" => 0.6,
            "box" => 1.5,
            "shot" => 2 + 10 * w.Value,
            "corner" => 1.0,
            "regain_high" => 0.8,
            "dribble_ok" => 0.3,
            _ => 0.0
        });

        double homeThreat = Threat(homeId);
        double awayThreat = Threat(awayId);
        double momentum = Math.Round((homeThreat - awayThreat) / (homeThreat + awayThreat + 4) * 100, 1);

        var control = Control(from, now + 0.001);
        int homePasses = recent.Count(w => w.Kind == "pass_ok" && w.TeamId == homeId);
        int awayPasses = recent.Count(w => w.Kind == "pass_ok" && w.TeamId == awayId);
        double homeShare = homePasses + awayPasses == 0 ? 0.5 : (double)homePasses / (homePasses + awayPasses);
        string? controller = homeShare >= 0.58 ? homeId : homeShare <= 0.42 ? awayId : null;

        double actions = recent.Count(w => w.Kind == "action") / minutes;
        // Simulated matches average ~14 on-ball actions (passes, carries, dribbles, shots) a minute.
        string tempo = actions >= 16.5 ? "High tempo" : actions < 12 ? "Slow build-up" : "Measured";

        return new GameState
        {
            Momentum = momentum,
            MomentumTeamId = momentum >= 20 ? homeId : momentum <= -20 ? awayId : null,
            ControlIndex = control.Index,
            ControlLabel = ControlLabel(control.Index),
            ControllingTeamId = controller,
            PassCompletionLast5 = control.Completion,
            TurnoversPerMinute = control.TurnoversPerMinute,
            ActionsPerMinute = Math.Round(actions, 1),
            Tempo = tempo,
            HomePressuresPerMinute = Math.Round(recent.Count(w => w.Kind == "pressure" && w.TeamId == homeId) / minutes, 2),
            AwayPressuresPerMinute = Math.Round(recent.Count(w => w.Kind == "pressure" && w.TeamId == awayId) / minutes, 2),
            HomeShareOfPassesLast5 = Math.Round(homeShare, 3)
        };
    }

    /// <summary>
    /// Control vs chaos for a time window. Blends pass completion (50%), how rarely the ball
    /// changes hands (35%) and how few fouls and duels break the game up (15%).
    /// </summary>
    private (double Index, double Completion, double TurnoversPerMinute) Control(double from, double to)
    {
        from = Math.Max(_periodStart, from);
        var items = _window.Where(w => w.T >= from && w.T < to).ToList();
        double minutes = Math.Max(1, (to - from) / 60);

        int ok = items.Count(w => w.Kind == "pass_ok");
        int failed = items.Count(w => w.Kind == "pass_fail");
        int turnovers = items.Count(w => w.Kind == "turnover");
        int fouls = items.Count(w => w.Kind == "foul");
        int regains = items.Count(w => w.Kind == "regain");

        double completion = ok + failed == 0 ? 0.8 : (double)ok / (ok + failed);
        double turnoversPerMinute = turnovers / minutes;
        double breakupsPerMinute = fouls / minutes + regains / minutes * 0.5;

        double completionScore = Math.Clamp((completion - 0.6) / 0.35, 0, 1);
        // Calibrated against simulated matches, which average ~3.5 changes of possession a minute.
        double turnoverScore = 1 - Math.Clamp((turnoversPerMinute - 2.2) / 3.0, 0, 1);
        double breakupScore = 1 - Math.Clamp(breakupsPerMinute / 2.0, 0, 1);
        double index = 100 * (0.5 * completionScore + 0.35 * turnoverScore + 0.15 * breakupScore);

        return (Math.Round(index, 1), Math.Round(completion, 3), Math.Round(turnoversPerMinute, 2));
    }

    private static string ControlLabel(double index) =>
        index >= 66 ? "Controlled" : index >= 45 ? "Contested" : "Chaotic";

    private double MomentumFor(string teamId, double at)
    {
        double momentum = ComputeState(at).Momentum;
        return _info.IsHome(teamId) ? momentum : -momentum;
    }

    private double Rate(string teamId, string kind, double from, double to)
    {
        from = Math.Max(_periodStart, from);
        double minutes = Math.Max(1, (to - from) / 60);
        return Math.Round(_window.Count(w => w.TeamId == teamId && w.Kind == kind && w.T >= from && w.T < to) / minutes, 2);
    }

    private int Count(string teamId, string kind, double from) =>
        _window.Count(w => w.TeamId == teamId && w.Kind == kind && w.T >= from);

    // ================================================================== bookkeeping

    private void AccruePossession(double now)
    {
        if (_possessionTeam is not null)
        {
            double dt = Math.Min(now - _lastPossessionTick, 20);
            if (dt > 0) _teams[_possessionTeam].PossessionSeconds += dt;
        }
        _lastPossessionTick = now;
    }

    private void UpdatePossessionOwner(MatchEvent e)
    {
        switch (e.Type)
        {
            case EventType.Pass:
            case EventType.Carry:
            case EventType.Dribble:
            case EventType.Shot:
            case EventType.KickOff:
            case EventType.ThrowIn:
            case EventType.GoalKick:
            case EventType.Corner:
            case EventType.FreeKick:
            case EventType.PossessionChange:
                if (e.TeamId is not null) _possessionTeam = e.TeamId;
                break;
            case EventType.Goal:
            case EventType.PeriodEnd:
            case EventType.MatchEnd:
                _possessionTeam = null;
                break;
        }
    }

    private void RecomputeTotals()
    {
        var home = _teams[_info.Home.Id];
        var away = _teams[_info.Away.Id];
        double total = home.PossessionSeconds + away.PossessionSeconds;
        if (total > 0)
        {
            home.PossessionPct = Math.Round(100 * home.PossessionSeconds / total, 1);
            away.PossessionPct = Math.Round(100 - home.PossessionPct, 1);
        }

        foreach (var team in new[] { home, away })
        {
            var squad = _players.Values.Where(p => p.TeamId == team.TeamId).ToList();
            team.DistanceKm = Math.Round(squad.Sum(p => p.DistanceM) / 1000, 1);
            team.Sprints = squad.Sum(p => p.Sprints);
        }
    }

    private void Window(MatchEvent e, string kind, double value = 0) =>
        _window.Add(new WindowItem(e.MatchSecond, e.TeamId ?? "", kind, value));

    private bool Cool(string key, double seconds)
    {
        if (_cooldowns.TryGetValue(key, out var until) && _matchSecond < until) return false;
        _cooldowns[key] = _matchSecond + seconds;
        return true;
    }

    private PlayerStats P(string? playerId) =>
        playerId is not null && _players.TryGetValue(playerId, out var p)
            ? p
            : throw new InvalidOperationException($"Unknown player '{playerId}'.");

    private TeamStats T(string? teamId) =>
        teamId is not null && _teams.TryGetValue(teamId, out var t)
            ? t
            : throw new InvalidOperationException($"Unknown team '{teamId}'.");

    private string TeamShort(string teamId) => _info.TeamById(teamId).ShortName;

    private string OpposingKeeperShortName(string attackingTeamId)
    {
        string defendingId = _info.Opponent(attackingTeamId).Id;
        return _players.Values.FirstOrDefault(p => p.TeamId == defendingId && p.Position == Position.GK && p.OnPitch)?.ShortName
               ?? "the keeper";
    }

    private Insight Make(
        InsightKind kind,
        MatchEvent e,
        double importance,
        string headline,
        string why,
        OverlayHint overlay,
        string? analyst = null,
        string? teamId = null,
        IEnumerable<string>? players = null,
        Dictionary<string, double>? evidence = null,
        IEnumerable<int>? sources = null) => new()
    {
        Id = $"{_info.MatchId}-I{++_insightSequence:D4}",
        MatchId = _info.MatchId,
        Kind = kind,
        Period = Math.Max(1, e.Period),
        MatchSecond = e.MatchSecond,
        Importance = Math.Round(Math.Clamp(importance, 0, 1), 2),
        Headline = headline,
        WhyItMatters = why,
        AnalystNote = analyst,
        TeamId = teamId,
        PlayerIds = players?.ToArray() ?? Array.Empty<string>(),
        Evidence = evidence ?? new Dictionary<string, double>(),
        SourceSequences = (sources ?? new[] { e.Sequence }).ToArray(),
        Overlay = overlay
    };
}
