namespace TwelfthVoice.Domain;

public static class MatchClock
{
    public const double HalfLength = 2700;
    public const double FullLength = 5400;

    public static double PeriodStart(int period) => period <= 1 ? 0 : HalfLength;

    public static double RegulationEnd(int period) => period <= 1 ? HalfLength : FullLength;

    /// <summary>"23:05", or "45+01:30" / "90+03:12" in added time.</summary>
    public static string Format(int period, double matchSecond)
    {
        double end = RegulationEnd(period);
        if (matchSecond <= end) return MinutesSeconds(matchSecond);
        int baseMinute = (int)(end / 60);
        return $"{baseMinute}+{MinutesSeconds(matchSecond - end)}";
    }

    /// <summary>The minute as fans say it: 1..45, 45 in added time; 46..90, 90 in added time.</summary>
    public static int Minute(int period, double matchSecond)
    {
        double end = RegulationEnd(period);
        if (matchSecond >= end) return (int)(end / 60);
        return (int)Math.Floor(matchSecond / 60) + 1;
    }

    /// <summary>Scoreboard-style minute label: "23'", "45+2'", "90+4'".</summary>
    public static string MinuteLabel(int period, double matchSecond)
    {
        double end = RegulationEnd(period);
        if (matchSecond > end)
            return $"{(int)(end / 60)}+{(int)Math.Ceiling((matchSecond - end) / 60)}'";
        return $"{(int)Math.Floor(matchSecond / 60) + 1}'";
    }

    private static string MinutesSeconds(double seconds)
    {
        int total = (int)Math.Floor(Math.Max(0, seconds));
        return $"{total / 60:D2}:{total % 60:D2}";
    }
}
