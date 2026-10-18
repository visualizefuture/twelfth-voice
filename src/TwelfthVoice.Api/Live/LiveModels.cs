using TwelfthVoice.Domain;
using TwelfthVoice.Intelligence;

namespace TwelfthVoice.Api.Live;

public enum MatchStatus
{
    Ready,
    Live,
    Paused,
    Finished
}

public sealed record CreateMatchRequest
{
    /// <summary>Team code, e.g. "HAR". Random if omitted.</summary>
    public string? HomeTeamId { get; init; }

    /// <summary>Team code, e.g. "IRV". Random if omitted.</summary>
    public string? AwayTeamId { get; init; }

    /// <summary>Same seed and teams = same match. Random if omitted.</summary>
    public int? Seed { get; init; }

    /// <summary>Match seconds per real second. 60 = one match minute per second.</summary>
    public double? Speed { get; init; }

    /// <summary>Start streaming immediately (default true).</summary>
    public bool? AutoStart { get; init; }
}

public sealed record SpeedRequest(double Speed);

public sealed record MatchSummary
{
    public required string MatchId { get; init; }
    public required MatchStatus Status { get; init; }
    public required string HomeTeamId { get; init; }
    public required string HomeTeam { get; init; }
    public required string AwayTeamId { get; init; }
    public required string AwayTeam { get; init; }
    public required int HomeScore { get; init; }
    public required int AwayScore { get; init; }
    public required int Period { get; init; }
    public required string Clock { get; init; }
    public required double Speed { get; init; }
    public required int Seed { get; init; }
    public required string Venue { get; init; }
    public required DateTimeOffset CreatedUtc { get; init; }
    public int Viewers { get; init; }
}

/// <summary>The scoreboard and game-state strip a viewer sees, pushed periodically over SignalR.</summary>
public sealed record LiveState
{
    public required string MatchId { get; init; }
    public required MatchStatus Status { get; init; }
    public required int Period { get; init; }
    public required double MatchSecond { get; init; }
    public required string Clock { get; init; }
    public required int HomeScore { get; init; }
    public required int AwayScore { get; init; }
    public required TeamLine Home { get; init; }
    public required TeamLine Away { get; init; }
    public required GameState Game { get; init; }

    /// <summary>Set only for viewers in player-focus mode.</summary>
    public PlayerStats? FocusPlayer { get; init; }

    public static LiveState From(MatchSummary summary, MatchSnapshot snapshot, string? focusPlayerId) => new()
    {
        MatchId = summary.MatchId,
        Status = summary.Status,
        Period = snapshot.Period,
        MatchSecond = snapshot.MatchSecond,
        Clock = snapshot.Clock,
        HomeScore = snapshot.HomeScore,
        AwayScore = snapshot.AwayScore,
        Home = TeamLine.From(snapshot.Home),
        Away = TeamLine.From(snapshot.Away),
        Game = snapshot.State,
        FocusPlayer = focusPlayerId is null
            ? null
            : snapshot.Players.FirstOrDefault(p => string.Equals(p.PlayerId, focusPlayerId, StringComparison.OrdinalIgnoreCase))
    };
}

/// <summary>The headline numbers for one team, small enough to push every few seconds.</summary>
public sealed record TeamLine
{
    public required string TeamId { get; init; }
    public required string Name { get; init; }
    public double PossessionPct { get; init; }
    public int Shots { get; init; }
    public int ShotsOnTarget { get; init; }
    public double Xg { get; init; }
    public double PassAccuracy { get; init; }
    public int Corners { get; init; }
    public int Fouls { get; init; }
    public int YellowCards { get; init; }
    public int RedCards { get; init; }
    public int Pressures { get; init; }
    public double DistanceKm { get; init; }

    public static TeamLine From(TeamStats t) => new()
    {
        TeamId = t.TeamId,
        Name = t.Name,
        PossessionPct = t.PossessionPct,
        Shots = t.Shots,
        ShotsOnTarget = t.ShotsOnTarget,
        Xg = Math.Round(t.Xg, 2),
        PassAccuracy = t.PassAccuracy,
        Corners = t.Corners,
        Fouls = t.Fouls,
        YellowCards = t.YellowCards,
        RedCards = t.RedCards,
        Pressures = t.Pressures,
        DistanceKm = t.DistanceKm
    };
}

/// <summary>What a viewer receives when they join: everything needed to render immediately.</summary>
public sealed record JoinResult
{
    public required MatchSummary Match { get; init; }
    public required MatchInfo Info { get; init; }
    public required LiveState State { get; init; }
    public required IReadOnlyList<PersonalizedInsight> RecentInsights { get; init; }
    public required IReadOnlyList<TimelinePoint> Timeline { get; init; }
}

/// <summary>One sample of the match story, taken every ~30 match seconds: drives the momentum chart.</summary>
public sealed record TimelinePoint(
    int Period,
    double MatchSecond,
    string Clock,
    double Momentum,
    double ControlIndex,
    int HomeScore,
    int AwayScore,
    double HomeXg,
    double AwayXg);
