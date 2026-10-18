using System.Runtime.CompilerServices;
using TwelfthVoice.Domain;

namespace TwelfthVoice.Simulator;

/// <summary>
/// Replays a simulated match "as it happens": events are released with the same gaps as in
/// match time, divided by <c>speed</c> (speed 1 = real time, 60 = one match minute per second).
/// </summary>
public static class LiveFeed
{
    public static async IAsyncEnumerable<MatchEvent> PlayAsync(
        IReadOnlyList<MatchEvent> events,
        double speed,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (speed <= 0) speed = 1;
        MatchEvent? previous = null;

        foreach (var matchEvent in events)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (previous is not null)
            {
                double realSeconds = matchEvent.Period == previous.Period
                    ? (matchEvent.MatchSecond - previous.MatchSecond) / speed
                    : 2.0; // short pause for half-time
                if (realSeconds > 0)
                    await Task.Delay(TimeSpan.FromSeconds(realSeconds), cancellationToken);
            }

            yield return matchEvent;
            previous = matchEvent;
        }
    }
}
