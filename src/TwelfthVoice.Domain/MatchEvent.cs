namespace TwelfthVoice.Domain;

/// <summary>
/// One synthetic match event. The shape is deliberately flat so it maps cleanly to JSON,
/// a SQL table and an overlay renderer.
/// Coordinates (<see cref="Start"/>, <see cref="End"/>) are in the attacking frame of
/// <see cref="TeamId"/> (see <see cref="PitchPoint"/>).
/// </summary>
public sealed record MatchEvent
{
    public required string MatchId { get; init; }

    /// <summary>Strictly increasing order of events within the match.</summary>
    public required int Sequence { get; init; }

    public required EventType Type { get; init; }

    /// <summary>1 = first half, 2 = second half.</summary>
    public required int Period { get; init; }

    /// <summary>Seconds of match time. The second half starts at 2700 regardless of first-half added time.</summary>
    public required double MatchSecond { get; init; }

    /// <summary>Broadcast-style clock, e.g. "67:12" or "90+02:41".</summary>
    public string Clock => MatchClock.Format(Period, MatchSecond);

    /// <summary>Team performing the action.</summary>
    public string? TeamId { get; init; }

    /// <summary>Player performing the action.</summary>
    public string? PlayerId { get; init; }

    /// <summary>
    /// Second player involved: pass receiver, player tackled or fouled, shot assister,
    /// substitute coming on, shooter for a save.
    /// </summary>
    public string? RelatedPlayerId { get; init; }

    public PitchPoint? Start { get; init; }
    public PitchPoint? End { get; init; }
    public EventOutcome? Outcome { get; init; }

    public double? DistanceM { get; init; }
    public double? BallSpeedKmh { get; init; }
    public double? PlayerSpeedKmh { get; init; }

    /// <summary>Expected goals for shots (0..1).</summary>
    public double? Xg { get; init; }

    /// <summary>Pass difficulty rating, 0 (trivial) to 10 (exceptional).</summary>
    public double? PassDifficulty { get; init; }

    public BodyPart? BodyPart { get; init; }

    /// <summary>Score after the event (set on goals, period ends and match end).</summary>
    public int? HomeScore { get; init; }
    public int? AwayScore { get; init; }

    /// <summary>Free-form tags such as "Through ball", "Under pressure", "Big chance", "Penalty".</summary>
    public IReadOnlyList<string> Qualifiers { get; init; } = Array.Empty<string>();

    /// <summary>Only on <see cref="EventType.TrackingSnapshot"/> events.</summary>
    public IReadOnlyList<PlayerPhysical>? Tracking { get; init; }

    public bool Has(string qualifier) =>
        Qualifiers.Any(q => string.Equals(q, qualifier, StringComparison.OrdinalIgnoreCase));
}
