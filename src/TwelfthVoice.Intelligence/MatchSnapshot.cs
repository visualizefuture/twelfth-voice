namespace TwelfthVoice.Intelligence;

/// <summary>Rolling-window indicators that describe the current shape of the game.</summary>
public sealed record GameState
{
    /// <summary>-100..100. Positive = home team on top over the last five minutes.</summary>
    public double Momentum { get; init; }
    public string? MomentumTeamId { get; init; }

    /// <summary>0..100. High = calm, controlled passing; low = scrappy, end-to-end chaos.</summary>
    public double ControlIndex { get; init; }
    public string ControlLabel { get; init; } = "";
    public string? ControllingTeamId { get; init; }

    public double PassCompletionLast5 { get; init; }
    public double TurnoversPerMinute { get; init; }
    public double ActionsPerMinute { get; init; }
    public string Tempo { get; init; } = "";

    public double HomePressuresPerMinute { get; init; }
    public double AwayPressuresPerMinute { get; init; }
    public double HomeShareOfPassesLast5 { get; init; }
}

public sealed record MatchSnapshot
{
    public required string MatchId { get; init; }
    public required int Period { get; init; }
    public required double MatchSecond { get; init; }
    public required string Clock { get; init; }
    public required int HomeScore { get; init; }
    public required int AwayScore { get; init; }
    public required TeamStats Home { get; init; }
    public required TeamStats Away { get; init; }
    public required GameState State { get; init; }
    public required IReadOnlyList<PlayerStats> Players { get; init; }
}
