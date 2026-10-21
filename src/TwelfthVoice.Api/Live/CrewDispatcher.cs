using Microsoft.AspNetCore.SignalR;
using TwelfthVoice.Agents;
using TwelfthVoice.Api.Hubs;
using TwelfthVoice.Intelligence;

namespace TwelfthVoice.Api.Live;

/// <summary>
/// Hands moments to the broadcast crew in the background and pushes the finished lines to the
/// viewers they were written for, as "insightUpdate" messages that replace the rule-written
/// text already on screen. Never blocks the live match loop.
/// </summary>
public sealed class CrewDispatcher
{
    private readonly BroadcastCrew _crew;
    private readonly IHubContext<MatchHub> _hub;
    private readonly ILogger<CrewDispatcher> _logger;

    public CrewDispatcher(BroadcastCrew crew, IHubContext<MatchHub> hub, ILogger<CrewDispatcher> logger)
    {
        _crew = crew;
        _hub = hub;
        _logger = logger;
    }

    /// <summary>Queues crew work for each audience watching these moments.</summary>
    public void Dispatch(MatchSession session, IReadOnlyList<Insight> insights, IReadOnlyList<ViewerConnection> viewers)
    {
        if (!_crew.IsAvailable || insights.Count == 0 || viewers.Count == 0) return;

        var summary = session.Summary();
        var context = new MomentContext(session.Info, summary.HomeScore, summary.AwayScore);

        foreach (var insight in insights)
        {
            var audiences = viewers
                .Where(v => InsightPersonalizer.Wants(v.Profile, insight))
                .Select(v => (Viewer: v, Personalized: InsightPersonalizer.Personalize(v.Profile, insight)))
                .GroupBy(x => CommentaryVariant.For(x.Viewer.Profile, x.Personalized.Perspective));

            foreach (var audience in audiences)
            {
                if (!_crew.ShouldHandle(insight, audience.Key)) continue;
                var members = audience.ToList();
                _ = DeliverAsync(insight, context, audience.Key, members);
            }
        }
    }

    private async Task DeliverAsync(Insight insight, MomentContext context, CommentaryVariant variant, List<(ViewerConnection Viewer, PersonalizedInsight Personalized)> members)
    {
        try
        {
            var line = await _crew.PrepareAsync(insight, context, variant);
            if (line is null) return;

            foreach (var (viewer, personalized) in members)
                await _hub.Clients.Client(viewer.ConnectionId).SendAsync("insightUpdate", line.ApplyTo(personalized));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not deliver crew line for {InsightId}", insight.Id);
        }
    }
}
