using TwelfthVoice.Domain;
using TwelfthVoice.Intelligence;

namespace TwelfthVoice.Api.Live;

/// <summary>Delivers live output to viewers. SignalR in production, a fake in tests.</summary>
public interface IMatchBroadcaster
{
    /// <summary>Called once per event, in order, with any insights it produced.</summary>
    Task PublishAsync(MatchSession session, MatchEvent matchEvent, IReadOnlyList<Insight> insights, bool stateDue, CancellationToken cancellationToken);

    /// <summary>Called when a match starts, pauses, resumes, changes speed or finishes.</summary>
    Task StatusChangedAsync(MatchSession session, CancellationToken cancellationToken);
}
