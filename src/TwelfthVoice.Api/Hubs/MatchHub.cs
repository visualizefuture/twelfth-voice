using Microsoft.AspNetCore.SignalR;
using TwelfthVoice.Agents;
using TwelfthVoice.Api.Live;
using TwelfthVoice.Intelligence;

namespace TwelfthVoice.Api.Hubs;

/// <summary>
/// Real-time channel at <c>/hubs/match</c>.
///
/// Client → server:
///   JoinMatch(matchId, profile)  → JoinResult (catch-up: summary, info, state, recent insights),
///                                  or null if the match isn't running here (e.g. after a restart)
///   UpdateProfile(profile)       change mode / club / player / language mid-match
///   SubscribeEvents(bool)        opt in or out of the raw event feed
///   LeaveMatch()
///
/// Server → client: "insight", "state", "status", "event" (see <see cref="SignalRBroadcaster"/>).
/// </summary>
public sealed class MatchHub : Hub
{
    private readonly MatchRegistry _registry;
    private readonly ViewerConnections _viewers;
    private readonly BroadcastCrew _crew;
    private readonly CrewDispatcher _dispatcher;

    /// <summary>When a viewer joins or changes their view, the crew also reworks this many recent moments for them.</summary>
    private const int RecentToRework = 8;

    public MatchHub(MatchRegistry registry, ViewerConnections viewers, BroadcastCrew crew, CrewDispatcher dispatcher)
    {
        _registry = registry;
        _viewers = viewers;
        _crew = crew;
        _dispatcher = dispatcher;
    }

    public async Task<JoinResult?> JoinMatch(string matchId, ViewerProfile? profile)
    {
        await LeaveCurrentAsync();

        // Matches live in memory, so a restart (or Azure scaling to zero) ends them. That's an
        // expected situation for a reconnecting browser, not a server error.
        var session = _registry.Get(matchId);
        if (session is null) return null;

        var effective = profile ?? ViewerProfile.Default;
        var connection = new ViewerConnection(Context.ConnectionId, session.MatchId, effective);
        _viewers.Set(connection);
        session.Viewers = _viewers.CountFor(session.MatchId);
        await Groups.AddToGroupAsync(Context.ConnectionId, HubGroups.Match(session.MatchId));

        var joined = session.Join(effective);
        ReworkRecent(session, connection);
        return joined with { RecentInsights = _crew.ApplyCached(joined.RecentInsights, effective) };
    }

    public LiveState UpdateProfile(ViewerProfile profile)
    {
        var connection = _viewers.Get(Context.ConnectionId) ?? throw new HubException("Join a match first.");
        var updated = connection with { Profile = profile };
        _viewers.Set(updated);
        var session = _registry.Get(connection.MatchId) ?? throw new HubException("The match is no longer available.");
        ReworkRecent(session, updated);
        return session.State(profile.FocusPlayerId);
    }

    /// <summary>Asks the crew to voice or translate the latest moments for this viewer's new view; results arrive as "insightUpdate".</summary>
    private void ReworkRecent(MatchSession session, ViewerConnection connection)
    {
        var recent = session.Insights()
            .Where(i => InsightPersonalizer.Wants(connection.Profile, i))
            .TakeLast(RecentToRework)
            .ToList();
        _dispatcher.Dispatch(session, recent, new[] { connection });
    }

    public async Task SubscribeEvents(bool subscribe)
    {
        var connection = _viewers.Get(Context.ConnectionId) ?? throw new HubException("Join a match first.");
        string group = HubGroups.Events(connection.MatchId);
        if (subscribe) await Groups.AddToGroupAsync(Context.ConnectionId, group);
        else await Groups.RemoveFromGroupAsync(Context.ConnectionId, group);
    }

    public Task LeaveMatch() => LeaveCurrentAsync();

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await LeaveCurrentAsync();
        await base.OnDisconnectedAsync(exception);
    }

    private async Task LeaveCurrentAsync()
    {
        var previous = _viewers.Remove(Context.ConnectionId);
        if (previous is null) return;

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, HubGroups.Match(previous.MatchId));
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, HubGroups.Events(previous.MatchId));
        if (_registry.Get(previous.MatchId) is { } session)
            session.Viewers = _viewers.CountFor(previous.MatchId);
    }
}
