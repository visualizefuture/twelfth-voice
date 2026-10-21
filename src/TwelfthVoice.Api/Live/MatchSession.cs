using TwelfthVoice.Agents;
using TwelfthVoice.Api.Storage;
using TwelfthVoice.Domain;
using TwelfthVoice.Intelligence;
using TwelfthVoice.Simulator;

namespace TwelfthVoice.Api.Live;

/// <summary>
/// One match being played live. Releases pre-simulated events on the match clock (scaled by
/// <see cref="Speed"/>), runs them through the <see cref="MatchAnalyzer"/>, then hands the
/// results to the broadcaster and the store. Supports pause, resume and speed changes mid-match.
/// </summary>
public sealed class MatchSession : IMatchData
{
    /// <summary>At or above this speed, events are released without any delay (tests, fast-forward).</summary>
    public const double InstantSpeed = 10_000;
    public const double MinSpeed = 0.5;

    private const double StateIntervalMatchSeconds = 30;
    private const int RecentInsightsOnJoin = 25;

    private readonly object _gate = new();
    private readonly MatchAnalyzer _analyzer;
    private readonly IMatchBroadcaster _broadcaster;
    private readonly IMatchStore _store;
    private readonly ILogger _logger;
    private readonly List<MatchEvent> _published = new();
    private readonly List<TimelinePoint> _timeline = new();

    private int _next;
    private double _speed;
    private MatchStatus _status = MatchStatus.Ready;
    private CancellationTokenSource? _cancellation;
    private Task _runner = Task.CompletedTask;

    public MatchSession(SimulatedMatch match, double speed, IMatchBroadcaster broadcaster, IMatchStore store, ILogger logger)
    {
        Info = match.Info;
        AllEvents = match.Events;
        _analyzer = new MatchAnalyzer(match.Info);
        _speed = ClampSpeed(speed);
        _broadcaster = broadcaster;
        _store = store;
        _logger = logger;
    }

    public MatchInfo Info { get; }
    public IReadOnlyList<MatchEvent> AllEvents { get; }
    public string MatchId => Info.MatchId;
    public DateTimeOffset CreatedUtc { get; } = DateTimeOffset.UtcNow;

    /// <summary>Completes when the match finishes or is stopped.</summary>
    public Task Completion => _runner;

    /// <summary>Number of viewers, maintained by the hub.</summary>
    public int Viewers { get; set; }

    public MatchStatus Status
    {
        get { lock (_gate) return _status; }
    }

    public double Speed
    {
        get { lock (_gate) return _speed; }
    }

    // ------------------------------------------------------------------ control

    public bool Start(CancellationToken applicationStopping)
    {
        lock (_gate)
        {
            if (_status != MatchStatus.Ready) return false;
            _status = MatchStatus.Live;
            _cancellation = CancellationTokenSource.CreateLinkedTokenSource(applicationStopping);
            var token = _cancellation.Token;
            _runner = Task.Run(() => RunAsync(token), CancellationToken.None);
        }
        NotifyStatus();
        return true;
    }

    public bool Pause()
    {
        lock (_gate)
        {
            if (_status != MatchStatus.Live) return false;
            _status = MatchStatus.Paused;
        }
        NotifyStatus();
        return true;
    }

    public bool Resume()
    {
        lock (_gate)
        {
            if (_status != MatchStatus.Paused) return false;
            _status = MatchStatus.Live;
        }
        NotifyStatus();
        return true;
    }

    public void SetSpeed(double speed)
    {
        lock (_gate) _speed = ClampSpeed(speed);
        NotifyStatus();
    }

    public void Stop()
    {
        lock (_gate) _cancellation?.Cancel();
    }

    // ------------------------------------------------------------------ queries (thread-safe)

    public MatchSnapshot Snapshot()
    {
        lock (_gate) return _analyzer.Snapshot();
    }

    public IReadOnlyList<Insight> Insights()
    {
        lock (_gate) return _analyzer.Insights.ToList();
    }

    public IReadOnlyList<MatchEvent> PublishedEvents(int afterSequence, int take)
    {
        lock (_gate)
            return _published.Where(e => e.Sequence > afterSequence).Take(take).ToList();
    }

    public IReadOnlyList<TimelinePoint> Timeline()
    {
        lock (_gate) return _timeline.ToList();
    }

    public IReadOnlyList<MomentumSample> Momentum() =>
        Timeline().Select(p => new MomentumSample(p.Period, p.MatchSecond, p.Clock, p.Momentum, p.ControlIndex, p.HomeScore, p.AwayScore)).ToList();

    public MatchRecap Recap()
    {
        lock (_gate) return RecapBuilder.Build(Info, _analyzer.Snapshot(), _analyzer.Insights);
    }

    public MatchSummary Summary()
    {
        lock (_gate)
        {
            var last = _published.Count > 0 ? _published[^1] : null;
            return new MatchSummary
            {
                MatchId = MatchId,
                Status = _status,
                HomeTeamId = Info.Home.Id,
                HomeTeam = Info.Home.Name,
                AwayTeamId = Info.Away.Id,
                AwayTeam = Info.Away.Name,
                HomeScore = _analyzer.HomeScore,
                AwayScore = _analyzer.AwayScore,
                Period = last?.Period ?? 1,
                Clock = last is null ? "00:00" : last.Clock,
                Speed = _speed,
                Seed = Info.Seed,
                Venue = Info.Venue,
                CreatedUtc = CreatedUtc,
                Viewers = Viewers
            };
        }
    }

    public LiveState State(string? focusPlayerId) => LiveState.From(Summary(), Snapshot(), focusPlayerId);

    /// <summary>Everything a new viewer needs to render straight away.</summary>
    public JoinResult Join(ViewerProfile profile)
    {
        var summary = Summary();
        var recent = InsightPersonalizer.ForViewer(profile, Insights());
        return new JoinResult
        {
            Match = summary,
            Info = Info,
            State = LiveState.From(summary, Snapshot(), profile.FocusPlayerId),
            RecentInsights = recent.Skip(Math.Max(0, recent.Count - RecentInsightsOnJoin)).ToList(),
            Timeline = Timeline()
        };
    }

    // ------------------------------------------------------------------ the live loop

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        MatchEvent? previous = null;
        double nextStateAt = 0;

        try
        {
            while (true)
            {
                MatchEvent current;
                lock (_gate)
                {
                    if (_next >= AllEvents.Count) break;
                    current = AllEvents[_next];
                }

                if (previous is not null) await WaitForMatchTimeAsync(previous, current, cancellationToken);
                await WaitWhilePausedAsync(cancellationToken);

                bool stateDue = current.MatchSecond >= nextStateAt
                                || current.Type is EventType.Goal or EventType.PeriodStart or EventType.PeriodEnd
                                    or EventType.MatchEnd or EventType.RedCard;
                if (stateDue) nextStateAt = current.MatchSecond + StateIntervalMatchSeconds;

                IReadOnlyList<Insight> insights;
                lock (_gate)
                {
                    insights = _analyzer.Process(current);
                    _published.Add(current);
                    _next++;
                    if (stateDue) _timeline.Add(ToTimelinePoint(_analyzer.Snapshot()));
                }

                _store.AppendEvent(current);
                foreach (var insight in insights) _store.AppendInsight(insight);
                if (current.Type is EventType.Goal or EventType.PeriodEnd)
                    _store.UpdateMatchStatus(MatchId, MatchStatus.Live.ToString(), _analyzer.HomeScore, _analyzer.AwayScore, finished: false);

                try
                {
                    await _broadcaster.PublishAsync(this, current, insights, stateDue, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Broadcast failed for {MatchId} event {Sequence}", MatchId, current.Sequence);
                }

                previous = current;
            }

            lock (_gate) _status = MatchStatus.Finished;
            _store.UpdateMatchStatus(MatchId, MatchStatus.Finished.ToString(), _analyzer.HomeScore, _analyzer.AwayScore, finished: true);
            NotifyStatus();
            _logger.LogInformation("Match {MatchId} finished {Home}-{Away}", MatchId, _analyzer.HomeScore, _analyzer.AwayScore);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Match {MatchId} stopped", MatchId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Match {MatchId} failed", MatchId);
        }
    }

    /// <summary>Sleeps for the match time between two events, in short slices so pause and speed changes apply quickly.</summary>
    private async Task WaitForMatchTimeAsync(MatchEvent previous, MatchEvent current, CancellationToken cancellationToken)
    {
        if (Speed >= InstantSpeed) return;

        if (current.Period != previous.Period)
        {
            await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken); // half-time pause
            return;
        }

        double remaining = current.MatchSecond - previous.MatchSecond;
        while (remaining > 0.001)
        {
            await WaitWhilePausedAsync(cancellationToken);
            double speed = Speed;
            if (speed >= InstantSpeed) return;
            double realSeconds = Math.Min(0.25, remaining / speed);
            await Task.Delay(TimeSpan.FromSeconds(realSeconds), cancellationToken);
            remaining -= realSeconds * speed;
        }
    }

    private async Task WaitWhilePausedAsync(CancellationToken cancellationToken)
    {
        while (Status == MatchStatus.Paused)
            await Task.Delay(200, cancellationToken);
    }

    private void NotifyStatus()
    {
        _ = NotifyStatusAsync();
    }

    private async Task NotifyStatusAsync()
    {
        try
        {
            await _broadcaster.StatusChangedAsync(this, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Status broadcast failed for {MatchId}", MatchId);
        }
    }

    private static TimelinePoint ToTimelinePoint(MatchSnapshot s) => new(
        s.Period,
        s.MatchSecond,
        s.Clock,
        s.State.Momentum,
        s.State.ControlIndex,
        s.HomeScore,
        s.AwayScore,
        Math.Round(s.Home.Xg, 2),
        Math.Round(s.Away.Xg, 2));

    private static double ClampSpeed(double speed) =>
        double.IsNaN(speed) ? 60 : Math.Clamp(speed, MinSpeed, InstantSpeed);
}
