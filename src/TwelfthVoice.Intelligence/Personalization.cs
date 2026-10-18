namespace TwelfthVoice.Intelligence;

public enum ViewerMode
{
    /// <summary>Fewer, bigger moments in plain language.</summary>
    Casual,
    /// <summary>Everything, with the numbers behind it.</summary>
    Analyst
}

/// <summary>Who is watching and what they care about.</summary>
public sealed record ViewerProfile
{
    public ViewerMode Mode { get; init; } = ViewerMode.Casual;

    /// <summary>Team code such as "HAR". Their moments are shown more readily.</summary>
    public string? FavoriteTeamId { get; init; }

    /// <summary>Player id such as "HAR-09". Turns on player-focus mode.</summary>
    public string? FocusPlayerId { get; init; }

    /// <summary>BCP-47 language tag, e.g. "en", "es", "ar". Applied by the Localizer agent.</summary>
    public string Language { get; init; } = "en";

    public string? DisplayName { get; init; }

    public static ViewerProfile Default { get; } = new();

    public bool IsPlayerFocus => !string.IsNullOrWhiteSpace(FocusPlayerId);
}

/// <summary>Whose side the moment is on, from the viewer's point of view.</summary>
public enum Perspective
{
    Neutral,
    Favorite,
    Opponent
}

/// <summary>An insight shaped for one viewer: what to say, how much detail, and how prominently.</summary>
public sealed record PersonalizedInsight
{
    public required string InsightId { get; init; }
    public required string MatchId { get; init; }
    public required InsightKind Kind { get; init; }
    public required int Period { get; init; }
    public required double MatchSecond { get; init; }
    public required string Clock { get; init; }

    /// <summary>Importance to the match story (same for everyone).</summary>
    public required double Importance { get; init; }

    /// <summary>Importance to this viewer (boosted for their team or focus player).</summary>
    public required double Relevance { get; init; }

    public required string Headline { get; init; }
    public required string Body { get; init; }
    public string? AnalystNote { get; init; }
    public string? TeamId { get; init; }
    public IReadOnlyList<string> PlayerIds { get; init; } = Array.Empty<string>();
    public IReadOnlyDictionary<string, double> Evidence { get; init; } = new Dictionary<string, double>();

    /// <summary>Event sequences the insight came from, so a renderer can draw them (e.g. a pass arrow).</summary>
    public IReadOnlyList<int> SourceSequences { get; init; } = Array.Empty<int>();

    public required OverlayHint Overlay { get; init; }
    public Perspective Perspective { get; init; }
    public bool AboutFocusPlayer { get; init; }
    public string Language { get; init; } = "en";
}

/// <summary>
/// The Personalize stage: the same insight stream filtered and phrased differently per viewer.
/// </summary>
public static class InsightPersonalizer
{
    public static bool Wants(ViewerProfile profile, Insight insight)
    {
        bool essential = insight.Kind is InsightKind.Goal or InsightKind.HalfTime or InsightKind.FullTime or InsightKind.Kickoff
                         || insight.Importance >= 0.85;
        if (essential) return true;

        if (profile.IsPlayerFocus)
            return AboutPlayer(profile, insight) || insight.Importance >= 0.6;

        double threshold = profile.Mode == ViewerMode.Analyst ? 0.0 : 0.45;
        if (AboutFavorite(profile, insight)) threshold -= 0.15;
        return insight.Importance >= threshold;
    }

    public static double Relevance(ViewerProfile profile, Insight insight)
    {
        double relevance = insight.Importance;
        if (AboutPlayer(profile, insight)) relevance += 0.3;
        if (AboutFavorite(profile, insight)) relevance += 0.1;
        return Math.Round(Math.Clamp(relevance, 0, 1), 2);
    }

    public static PersonalizedInsight Personalize(ViewerProfile profile, Insight insight)
    {
        bool aboutPlayer = AboutPlayer(profile, insight);
        bool analyst = profile.Mode == ViewerMode.Analyst;

        var overlay = aboutPlayer
            ? insight.Overlay with { Priority = Math.Min(100, insight.Overlay.Priority + 10) }
            : insight.Overlay;

        return new PersonalizedInsight
        {
            InsightId = insight.Id,
            MatchId = insight.MatchId,
            Kind = insight.Kind,
            Period = insight.Period,
            MatchSecond = insight.MatchSecond,
            Clock = insight.Clock,
            Importance = insight.Importance,
            Relevance = Relevance(profile, insight),
            Headline = insight.Headline,
            Body = analyst ? insight.WhyItMatters : Narration.FirstSentence(insight.WhyItMatters),
            AnalystNote = analyst ? insight.AnalystNote : null,
            TeamId = insight.TeamId,
            PlayerIds = insight.PlayerIds,
            Evidence = insight.Evidence,
            SourceSequences = insight.SourceSequences,
            Overlay = overlay,
            Perspective = PerspectiveOf(profile, insight),
            AboutFocusPlayer = aboutPlayer,
            Language = profile.Language
        };
    }

    public static IReadOnlyList<PersonalizedInsight> ForViewer(ViewerProfile profile, IEnumerable<Insight> insights) =>
        insights.Where(i => Wants(profile, i)).Select(i => Personalize(profile, i)).ToList();

    private static bool AboutPlayer(ViewerProfile profile, Insight insight) =>
        profile.IsPlayerFocus &&
        insight.PlayerIds.Any(id => string.Equals(id, profile.FocusPlayerId, StringComparison.OrdinalIgnoreCase));

    private static bool AboutFavorite(ViewerProfile profile, Insight insight) =>
        profile.FavoriteTeamId is not null &&
        string.Equals(insight.TeamId, profile.FavoriteTeamId, StringComparison.OrdinalIgnoreCase);

    private static Perspective PerspectiveOf(ViewerProfile profile, Insight insight)
    {
        if (profile.FavoriteTeamId is null || insight.TeamId is null) return Perspective.Neutral;
        return AboutFavorite(profile, insight) ? Perspective.Favorite : Perspective.Opponent;
    }
}
