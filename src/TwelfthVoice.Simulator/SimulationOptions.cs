using TwelfthVoice.Domain;

namespace TwelfthVoice.Simulator;

public sealed class SimulationOptions
{
    /// <summary>Same seed + same teams = identical match, event for event.</summary>
    public int Seed { get; init; } = 42;

    /// <summary>Team code such as "HAR". Random if not set.</summary>
    public string? HomeTeamId { get; init; }

    /// <summary>Team code such as "IRV". Random if not set.</summary>
    public string? AwayTeamId { get; init; }

    public DateTimeOffset? KickOff { get; init; }

    /// <summary>How often (match seconds) to emit player tracking snapshots.</summary>
    public double TrackingIntervalSeconds { get; init; } = 60;

    public int LeagueSeed { get; init; } = SyntheticLeague.DefaultLeagueSeed;

    /// <summary>
    /// Optional id for the match. Defaults to "TV-{home}-{away}-{seed}". The API sets a unique id
    /// per run so the same fixture can be replayed and stored more than once.
    /// </summary>
    public string? MatchId { get; init; }
}

public sealed record SimulatedMatch(MatchInfo Info, IReadOnlyList<MatchEvent> Events);
