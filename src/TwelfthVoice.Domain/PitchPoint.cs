using System.Text.Json.Serialization;

namespace TwelfthVoice.Domain;

/// <summary>
/// A location on a 105 m x 68 m pitch, expressed in the <b>attacking frame</b> of the team
/// performing the action: x = 0 is that team's own goal line, x = 105 the opponent's goal line,
/// and y runs 0..68 across the pitch (y = 0 is the right touchline when facing the opponent's goal).
/// </summary>
public readonly record struct PitchPoint(double X, double Y)
{
    public const double Length = 105.0;
    public const double Width = 68.0;
    public const double GoalHalfWidth = 3.66;
    public const double PenaltyAreaDepth = 16.5;
    public const double PenaltyAreaHalfWidth = 20.16;

    public static PitchPoint CentreSpot => new(Length / 2, Width / 2);
    public static PitchPoint OpponentGoalCentre => new(Length, Width / 2);

    public double DistanceTo(PitchPoint other) =>
        Math.Sqrt((other.X - X) * (other.X - X) + (other.Y - Y) * (other.Y - Y));

    /// <summary>The same physical location, seen from the opponent's attacking frame.</summary>
    public PitchPoint Mirror() => new(Length - X, Width - Y);

    public PitchPoint Clamp() => new(Math.Clamp(X, 0, Length), Math.Clamp(Y, 0, Width));

    public PitchPoint Round() => new(Math.Round(X, 1), Math.Round(Y, 1));

    [JsonIgnore]
    public double DistanceToGoal => DistanceTo(OpponentGoalCentre);

    [JsonIgnore]
    public bool InFinalThird => X >= Length * 2.0 / 3.0;

    [JsonIgnore]
    public bool InOpponentHalf => X > Length / 2;

    [JsonIgnore]
    public bool InPenaltyArea =>
        X >= Length - PenaltyAreaDepth &&
        Y >= Width / 2 - PenaltyAreaHalfWidth &&
        Y <= Width / 2 + PenaltyAreaHalfWidth;

    public override string ToString() => $"({X:F1}, {Y:F1})";
}
