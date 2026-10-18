using Microsoft.AspNetCore.SignalR;
using TwelfthVoice.Api.Live;
using TwelfthVoice.Intelligence;

namespace TwelfthVoice.Api.Hubs;

/// <summary>
/// Real-time channel at <c>/hubs/match</c>.
///
/// Client → server:
///   JoinMatch(matchId, profile)  → JoinResult (catch-up: summary, info, state, recent insights)
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

    public MatchHub(MatchRegistry registry, ViewerConnections viewers)
    {
        _registry = registry;
        _viewers = viewers;
    }

    public async Task<JoinResult> JoinMatch(string matchId, ViewerProfile? profile)
    {
        var session = _registry.Get(matchId) ?? throw new HubException($"Match '{matchId}' was not found.");
        await LeaveCurrentAsync();

        var effective = profile ?? ViewerProfile.Default;
        _viewers.Set(new ViewerConnection(Context.ConnectionId, session.MatchId, effective));
        session.Viewers = _viewers.CountFor(session.MatchId);
        await Groups.AddToGroupAsync(Context.ConnectionId, HubGroups.Match(session.MatchId));

        return session.Join(effective);
    }

    public LiveState UpdateProfile(ViewerProfile profile)
    {
        var connection = _viewers.Get(Context.ConnectionId) ?? throw new HubException("Join a match first.");
        _viewers.Set(connection with { Profile = profile });
        var session = _registry.Get(connection.MatchId) ?? throw new HubException("The match is no longer available.");
        return session.State(profile.FocusPlayerId);
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
