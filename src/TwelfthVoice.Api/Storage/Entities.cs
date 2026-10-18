namespace TwelfthVoice.Api.Storage;

// Relational shape of the data. Key analytical columns are real columns (so they can be
// queried, indexed and fed to Microsoft Fabric / Power BI); the full object is also kept as
// JSON so nothing is lost if the domain model grows.

public sealed class MatchRecord
{
    public string MatchId { get; set; } = "";
    public string HomeTeamId { get; set; } = "";
    public string AwayTeamId { get; set; } = "";
    public int Seed { get; set; }
    public string Competition { get; set; } = "";
    public string Venue { get; set; } = "";
    public DateTimeOffset KickOff { get; set; }
    public string Status { get; set; } = "";
    public int HomeScore { get; set; }
    public int AwayScore { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset? FinishedUtc { get; set; }
    public string InfoJson { get; set; } = "";
}

public sealed class EventRecord
{
    public string MatchId { get; set; } = "";
    public int Sequence { get; set; }
    public string Type { get; set; } = "";
    public int Period { get; set; }
    public double MatchSecond { get; set; }
    public string? TeamId { get; set; }
    public string? PlayerId { get; set; }
    public string? RelatedPlayerId { get; set; }
    public string? Outcome { get; set; }
    public double? StartX { get; set; }
    public double? StartY { get; set; }
    public double? EndX { get; set; }
    public double? EndY { get; set; }
    public double? DistanceM { get; set; }
    public double? BallSpeedKmh { get; set; }
    public double? PlayerSpeedKmh { get; set; }
    public double? Xg { get; set; }
    public double? PassDifficulty { get; set; }
    public string? Qualifiers { get; set; }
    public string PayloadJson { get; set; } = "";
}

public sealed class InsightRecord
{
    public string InsightId { get; set; } = "";
    public string MatchId { get; set; } = "";
    public string Kind { get; set; } = "";
    public int Period { get; set; }
    public double MatchSecond { get; set; }
    public double Importance { get; set; }
    public string? TeamId { get; set; }
    public string Headline { get; set; } = "";
    public string WhyItMatters { get; set; } = "";
    public string? AnalystNote { get; set; }
    public string PayloadJson { get; set; } = "";
}

public sealed class ViewerProfileRecord
{
    public Guid Id { get; set; }
    public string? DisplayName { get; set; }
    public string Mode { get; set; } = "Casual";
    public string? FavoriteTeamId { get; set; }
    public string? FocusPlayerId { get; set; }
    public string Language { get; set; } = "en";
    public DateTimeOffset UpdatedUtc { get; set; }
}
