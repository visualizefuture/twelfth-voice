using TwelfthVoice.Domain;

namespace TwelfthVoice.Intelligence;

public enum InsightKind
{
    Kickoff,
    Goal,
    BigChance,
    SpeedAlert,
    DistanceMilestone,
    PassOfNote,
    PlayerMilestone,
    MomentumShift,
    PressingShift,
    ControlShift,
    TempoShift,
    Card,
    Substitution,
    HalfTime,
    FullTime
}

/// <summary>
/// How and where an insight should be drawn on top of the match feed. Kept generic so any
/// renderer (the Angular overlay, a broadcast graphics engine) can map it to its own templates.
/// </summary>
/// <param name="Template">Graphic template, e.g. "goal-banner", "player-tag", "stat-card".</param>
/// <param name="Placement">Screen region, e.g. "full-width", "lower-third", "top-bar", "near-player".</param>
/// <param name="DisplaySeconds">How long to keep it on screen.</param>
/// <param name="Priority">0-100. Higher replaces lower when two compete for the same region.</param>
public sealed record OverlayHint(string Template, string Placement, double DisplaySeconds, int Priority);

/// <summary>
/// An explained moment: what happened, why it matters and the evidence behind it.
/// Produced by rules first; AI agents can later rewrite the language for each audience,
/// but the evidence numbers always come from here.
/// </summary>
public sealed record Insight
{
    public required string Id { get; init; }
    public required string MatchId { get; init; }
    public required InsightKind Kind { get; init; }
    public required int Period { get; init; }
    public required double MatchSecond { get; init; }
    public string Clock => MatchClock.Format(Period, MatchSecond);

    /// <summary>0..1, how much this moment matters to the story of the match.</summary>
    public required double Importance { get; init; }

    /// <summary>Short, casual-fan friendly line.</summary>
    public required string Headline { get; init; }

    /// <summary>The explanation: why this moment matters.</summary>
    public required string WhyItMatters { get; init; }

    /// <summary>Numbers-first version for the analyst mode.</summary>
    public string? AnalystNote { get; init; }

    public string? TeamId { get; init; }
    public IReadOnlyList<string> PlayerIds { get; init; } = Array.Empty<string>();
    public IReadOnlyDictionary<string, double> Evidence { get; init; } = new Dictionary<string, double>();

    /// <summary>Event sequence numbers this insight was derived from.</summary>
    public IReadOnlyList<int> SourceSequences { get; init; } = Array.Empty<int>();

    public required OverlayHint Overlay { get; init; }
}
