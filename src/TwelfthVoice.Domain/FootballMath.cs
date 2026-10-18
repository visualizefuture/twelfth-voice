namespace TwelfthVoice.Domain;

/// <summary>
/// Transparent football models shared by the simulator (to decide outcomes) and the
/// intelligence layer (to rate and explain them). Kept deliberately simple and readable
/// so every number shown on screen can be explained.
/// </summary>
public static class FootballMath
{
    /// <summary>Angle (radians) between the two goalposts as seen from the shot location.</summary>
    public static double ShotAngle(PitchPoint p)
    {
        double lowPost = PitchPoint.Width / 2 - PitchPoint.GoalHalfWidth;
        double highPost = PitchPoint.Width / 2 + PitchPoint.GoalHalfWidth;
        double dx = Math.Max(0.1, PitchPoint.Length - p.X);
        double a1 = Math.Atan2(lowPost - p.Y, dx);
        double a2 = Math.Atan2(highPost - p.Y, dx);
        return Math.Abs(a2 - a1);
    }

    /// <summary>
    /// Logistic expected-goals model on distance and goal-mouth angle.
    /// Roughly: 6 m central ~0.37, penalty spot (open play) ~0.14, 25 m ~0.02.
    /// </summary>
    public static double ExpectedGoals(PitchPoint shot, BodyPart bodyPart, bool bigChance)
    {
        double distance = shot.DistanceToGoal;
        double angle = ShotAngle(shot);
        double z = -1.6 - 0.11 * distance + 1.6 * angle;
        if (bodyPart == BodyPart.Head) z -= 0.7;
        if (bigChance) z += 0.6;
        double xg = 1.0 / (1.0 + Math.Exp(-z));
        return Math.Round(Math.Clamp(xg, 0.01, 0.95), 3);
    }

    /// <summary>
    /// Pass difficulty, 0..10. Long, forward, pressured passes into the box (through balls
    /// and crosses especially) are harder.
    /// </summary>
    public static double PassDifficulty(PitchPoint start, PitchPoint end, bool underPressure, bool throughBall, bool cross)
    {
        double distance = start.DistanceTo(end);
        double d = 1.0;
        d += Math.Min(distance, 60) / 60 * 4.0;
        double progression = end.X - start.X;
        if (progression > 0) d += Math.Min(progression, 40) / 40 * 2.0;
        if (underPressure) d += 1.5;
        if (end.InPenaltyArea) d += 1.0;
        if (throughBall) d += 1.0;
        if (cross) d += 1.0;
        return Math.Round(Math.Clamp(d, 0, 10), 1);
    }

    /// <summary>Chance a pass of the given difficulty is completed by a passer with the given rating.</summary>
    public static double PassSuccessProbability(double difficulty, int passing) =>
        Math.Clamp(0.985 - 0.045 * difficulty + (passing - 70) * 0.003, 0.25, 0.99);

    /// <summary>Typical ball speed for a pass of a given length (km/h).</summary>
    public static double BallSpeedKmh(double distance, bool cross, double noise) =>
        Math.Clamp(28 + distance * 1.05 + (cross ? 8 : 0) + noise, 18, 115);

    public static string DescribeDifficulty(double difficulty) => difficulty switch
    {
        >= 8.5 => "exceptional",
        >= 7.0 => "very hard",
        >= 5.5 => "hard",
        >= 3.5 => "moderate",
        _ => "routine"
    };
}
