using System.Collections.Concurrent;
using Microsoft.Extensions.Logging.Abstractions;
using TwelfthVoice.Api.Live;
using TwelfthVoice.Api.Storage;
using TwelfthVoice.Domain;
using TwelfthVoice.Intelligence;
using TwelfthVoice.Simulator;

namespace TwelfthVoice.Tests;

public class MatchSessionTests
{
    private sealed class RecordingBroadcaster : IMatchBroadcaster
    {
        public ConcurrentQueue<MatchEvent> Events { get; } = new();
        public ConcurrentQueue<Insight> Insights { get; } = new();
        public int StateCount;
        public int StatusCount;

        public Task PublishAsync(MatchSession session, MatchEvent matchEvent, IReadOnlyList<Insight> insights, bool stateDue, CancellationToken cancellationToken)
        {
            Events.Enqueue(matchEvent);
            foreach (var insight in insights) Insights.Enqueue(insight);
            if (stateDue) Interlocked.Increment(ref StateCount);
            return Task.CompletedTask;
        }

        public Task StatusChangedAsync(MatchSession session, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref StatusCount);
            return Task.CompletedTask;
        }
    }

    private static (MatchSession Session, RecordingBroadcaster Broadcaster) Create(double speed)
    {
        var match = MatchSimulator.Run(new SimulationOptions { Seed = 7, HomeTeamId = "HAR", AwayTeamId = "IRV", MatchId = "TEST-MATCH" });
        var broadcaster = new RecordingBroadcaster();
        var session = new MatchSession(match, speed, broadcaster, new InMemoryMatchStore(), NullLogger.Instance);
        return (session, broadcaster);
    }

    [Fact]
    public async Task Instant_session_plays_every_event_in_order_and_finishes()
    {
        var (session, broadcaster) = Create(MatchSession.InstantSpeed);

        Assert.True(session.Start(CancellationToken.None));
        await session.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(MatchStatus.Finished, session.Status);
        Assert.Equal(session.AllEvents.Count, broadcaster.Events.Count);
        Assert.Equal(session.AllEvents.Select(e => e.Sequence), broadcaster.Events.Select(e => e.Sequence));
        Assert.Contains(broadcaster.Insights, i => i.Kind == InsightKind.FullTime);
        Assert.True(broadcaster.StateCount > 100, "Scoreboard state should be pushed regularly.");

        var timeline = session.Timeline();
        Assert.True(timeline.Count > 100, "Timeline should be sampled every ~30 match seconds.");
        Assert.Equal(session.AllEvents[^1].HomeScore, timeline[^1].HomeScore);

        var summary = session.Summary();
        Assert.Equal(session.AllEvents[^1].HomeScore, summary.HomeScore);
        Assert.Equal(session.AllEvents[^1].AwayScore, summary.AwayScore);
    }

    [Fact]
    public async Task Pause_stops_the_clock_and_resume_continues()
    {
        var (session, broadcaster) = Create(speed: 600);

        session.Start(CancellationToken.None);
        await Task.Delay(400);
        Assert.True(session.Pause());
        await Task.Delay(300); // let any in-flight event land
        int countWhenPaused = broadcaster.Events.Count;

        await Task.Delay(600);
        Assert.Equal(countWhenPaused, broadcaster.Events.Count);
        Assert.Equal(MatchStatus.Paused, session.Status);

        Assert.True(session.Resume());
        session.SetSpeed(MatchSession.InstantSpeed);
        await session.Completion.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(MatchStatus.Finished, session.Status);
        Assert.Equal(session.AllEvents.Count, broadcaster.Events.Count);
    }

    [Fact]
    public void Join_returns_everything_needed_to_render()
    {
        var (session, _) = Create(MatchSession.InstantSpeed);
        var joined = session.Join(new ViewerProfile { FocusPlayerId = "HAR-09" });

        Assert.Equal("TEST-MATCH", joined.Match.MatchId);
        Assert.Equal(MatchStatus.Ready, joined.Match.Status);
        Assert.Equal("HAR", joined.Info.Home.Id);
        Assert.NotNull(joined.State.FocusPlayer);
    }

    [Fact]
    public void Speed_is_clamped_to_a_safe_range()
    {
        var (session, _) = Create(speed: 60);
        session.SetSpeed(0);
        Assert.Equal(MatchSession.MinSpeed, session.Speed);
        session.SetSpeed(1_000_000);
        Assert.Equal(MatchSession.InstantSpeed, session.Speed);
    }
}

public class McpToolsTests
{
    private sealed class Lifetime : Microsoft.Extensions.Hosting.IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => CancellationToken.None;
        public CancellationToken ApplicationStopped => CancellationToken.None;
        public void StopApplication() { }
    }

    private sealed class SilentBroadcaster : IMatchBroadcaster
    {
        public Task PublishAsync(MatchSession session, MatchEvent matchEvent, IReadOnlyList<Insight> insights, bool stateDue, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StatusChangedAsync(MatchSession session, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private static TwelfthVoice.Api.Mcp.MatchMcpTools Tools(out MatchRegistry registry)
    {
        registry = new MatchRegistry(new SilentBroadcaster(), new InMemoryMatchStore(), NullLoggerFactory.Instance, new Lifetime());
        var crew = new TwelfthVoice.Agents.BroadcastCrew(new TwelfthVoice.Agents.NullCrewModel(), new TwelfthVoice.Agents.AgentOptions(),
            NullLogger<TwelfthVoice.Agents.BroadcastCrew>.Instance);
        return new TwelfthVoice.Api.Mcp.MatchMcpTools(registry, crew);
    }

    [Fact]
    public async Task Tools_start_a_match_and_read_it_back()
    {
        var tools = Tools(out var registry);

        Assert.Contains("No match is running", tools.GetMatchState());
        Assert.Contains("Harbourside Rovers", tools.ListTeams());

        string started = tools.StartMatch("HAR", "IRV", 7, MatchSession.InstantSpeed);
        Assert.Contains("TV-HAR-IRV-7", started);
        await registry.All[0].Completion.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Contains("\"score\"", tools.GetMatchState());
        Assert.Contains("Harbourside", tools.GetTeamStats("HAR"));
        Assert.Contains("\"top\"", tools.GetTopPlayers("rating"));
        Assert.Contains("headline", tools.GetRecap());
        Assert.Equal("[]", tools.GetCrewRoom());
        Assert.Contains("running on this server", tools.GetMatchState("TV-NOPE"));
        Assert.Contains("error", tools.StartMatch("XXX", "IRV"));
    }
}
