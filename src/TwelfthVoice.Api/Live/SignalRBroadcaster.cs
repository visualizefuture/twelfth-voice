using Microsoft.AspNetCore.SignalR;
using TwelfthVoice.Api.Hubs;
using TwelfthVoice.Domain;
using TwelfthVoice.Intelligence;

namespace TwelfthVoice.Api.Live;

/// <summary>
/// Pushes live output over SignalR. Insights and scoreboard state are personalised per viewer;
/// raw events go to the opt-in events group.
///
/// Client messages:
///   "insight" → <see cref="PersonalizedInsight"/> (rule-written, instant)
///   "insightUpdate" → <see cref="PersonalizedInsight"/> (same id, rewritten by the broadcast crew and fact-checked)
///   "state"   → <see cref="LiveState"/>
///   "status"  → <see cref="MatchSummary"/>
///   "event"   → <see cref="MatchEvent"/> (events group only)
/// </summary>
public sealed class SignalRBroadcaster : IMatchBroadcaster
{
    private readonly IHubContext<MatchHub> _hub;
    private readonly ViewerConnections _viewers;
    private readonly CrewDispatcher _crew;

    public SignalRBroadcaster(IHubContext<MatchHub> hub, ViewerConnections viewers, CrewDispatcher crew)
    {
        _hub = hub;
        _viewers = viewers;
        _crew = crew;
    }

    public async Task PublishAsync(MatchSession session, MatchEvent matchEvent, IReadOnlyList<Insight> insights, bool stateDue, CancellationToken cancellationToken)
    {
        if (matchEvent.Type != EventType.TrackingSnapshot)
            await _hub.Clients.Group(HubGroups.Events(session.MatchId)).SendAsync("event", matchEvent, cancellationToken);

        var viewers = _viewers.ForMatch(session.MatchId);
        if (viewers.Count == 0) return;

        MatchSummary? summary = null;
        MatchSnapshot? snapshot = null;
        if (stateDue)
        {
            summary = session.Summary();
            snapshot = session.Snapshot();
        }

        foreach (var viewer in viewers)
        {
            var client = _hub.Clients.Client(viewer.ConnectionId);

            foreach (var insight in insights)
            {
                if (InsightPersonalizer.Wants(viewer.Profile, insight))
                    await client.SendAsync("insight", InsightPersonalizer.Personalize(viewer.Profile, insight), cancellationToken);
            }

            if (summary is not null && snapshot is not null)
                await client.SendAsync("state", LiveState.From(summary, snapshot, viewer.Profile.FocusPlayerId), cancellationToken);
        }

        _crew.Dispatch(session, insights, viewers);
    }

    public Task StatusChangedAsync(MatchSession session, CancellationToken cancellationToken) =>
        _hub.Clients.Group(HubGroups.Match(session.MatchId)).SendAsync("status", session.Summary(), cancellationToken);
}
