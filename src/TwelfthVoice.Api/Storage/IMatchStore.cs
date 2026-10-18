using TwelfthVoice.Domain;
using TwelfthVoice.Intelligence;

namespace TwelfthVoice.Api.Storage;

/// <summary>
/// Persistence for matches, events, insights and viewer profiles. Writes are fire-and-forget
/// (queued and batched in the background) so the live stream never waits on the database.
/// </summary>
public interface IMatchStore
{
    /// <summary>"Sql", "Memory", or "Unavailable" — shown on the health endpoint.</summary>
    string Mode { get; }

    void SaveMatch(MatchInfo info, string status);
    void AppendEvent(MatchEvent matchEvent);
    void AppendInsight(Insight insight);
    void UpdateMatchStatus(string matchId, string status, int homeScore, int awayScore, bool finished);

    Task<IReadOnlyList<StoredMatch>> RecentMatchesAsync(int take, CancellationToken cancellationToken);
    Task<StoredProfile?> GetProfileAsync(Guid id, CancellationToken cancellationToken);
    Task<StoredProfile> SaveProfileAsync(Guid? id, ViewerProfile profile, CancellationToken cancellationToken);
}

public sealed record StoredMatch(
    string MatchId,
    string HomeTeamId,
    string AwayTeamId,
    int Seed,
    string Status,
    int HomeScore,
    int AwayScore,
    DateTimeOffset CreatedUtc,
    DateTimeOffset? FinishedUtc);

public sealed record StoredProfile(Guid Id, ViewerProfile Profile, DateTimeOffset UpdatedUtc);
