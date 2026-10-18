using TwelfthVoice.Domain;
using TwelfthVoice.Intelligence;

namespace TwelfthVoice.Cli;

internal static class ConsoleRender
{
    public static void Line(string text, ConsoleColor color)
    {
        var previous = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.WriteLine(text);
        Console.ForegroundColor = previous;
    }

    public static void PrintInsight(Insight insight, ViewerProfile profile)
    {
        var color = insight.Kind switch
        {
            InsightKind.Goal => ConsoleColor.Yellow,
            InsightKind.Card => ConsoleColor.Red,
            InsightKind.BigChance => ConsoleColor.Magenta,
            InsightKind.MomentumShift or InsightKind.PressingShift or InsightKind.ControlShift or InsightKind.TempoShift => ConsoleColor.Cyan,
            InsightKind.SpeedAlert or InsightKind.DistanceMilestone => ConsoleColor.Green,
            InsightKind.HalfTime or InsightKind.FullTime or InsightKind.Kickoff => ConsoleColor.White,
            _ => ConsoleColor.Gray
        };

        Line($"[{insight.Clock}] {insight.Headline}", color);

        string why = profile.Mode == ViewerMode.Casual ? Narration.FirstSentence(insight.WhyItMatters) : insight.WhyItMatters;
        Console.WriteLine($"          {why}");

        if (profile.Mode == ViewerMode.Analyst && insight.AnalystNote is not null)
            Line($"          {insight.AnalystNote}", ConsoleColor.DarkGray);
    }

    public static void PrintScoreboard(MatchInfo info, MatchSnapshot snapshot, ViewerProfile profile)
    {
        var state = snapshot.State;
        Line($"  ┌ {snapshot.Clock}  {info.Home.ShortName} {snapshot.HomeScore}–{snapshot.AwayScore} {info.Away.ShortName}   {MomentumBar(info, state.Momentum)}   {state.ControlLabel} · {state.Tempo}", ConsoleColor.DarkCyan);

        if (profile.Mode == ViewerMode.Analyst)
        {
            var h = snapshot.Home;
            var a = snapshot.Away;
            Line($"  └ xG {h.Xg:0.00}–{a.Xg:0.00} · shots {h.Shots}–{a.Shots} · poss {h.PossessionPct:0}–{a.PossessionPct:0}% · pass {Narration.Pct(h.PassAccuracy)}–{Narration.Pct(a.PassAccuracy)} · control {state.ControlIndex:0}", ConsoleColor.DarkGray);
        }

        if (profile.FocusPlayerId is not null)
        {
            var player = snapshot.Players.FirstOrDefault(p => string.Equals(p.PlayerId, profile.FocusPlayerId, StringComparison.OrdinalIgnoreCase));
            if (player is not null)
            {
                Line($"  └ {player.Name}: {player.Touches} touches · {player.PassesCompleted}/{player.PassesAttempted} passes · {player.Shots} shots ({player.Xg:0.00} xG) · {player.DistanceM / 1000:0.0} km · top {player.TopSpeedKmh:0.0} km/h · rating {player.Rating:0.0}", ConsoleColor.Green);
            }
        }
    }

    public static void PrintRecap(MatchRecap recap)
    {
        Line(recap.Headline, ConsoleColor.Yellow);
        foreach (var line in recap.Scorers) Console.WriteLine($"  ⚽ {line}");
        Console.WriteLine();
        Console.WriteLine(recap.Summary);
        Console.WriteLine();
        foreach (var line in recap.StatLines) Console.WriteLine($"  {line}");
        Console.WriteLine();
        Line("Key moments", ConsoleColor.White);
        foreach (var moment in recap.KeyMoments)
            Console.WriteLine($"  {MatchClock.MinuteLabel(moment.Period, moment.MatchSecond),-6} {moment.Headline}");
    }

    private static string MomentumBar(MatchInfo info, double momentum)
    {
        const int width = 17;
        int marker = (int)Math.Round((momentum + 100) / 200 * (width - 1));
        var bar = new char[width];
        for (int i = 0; i < width; i++) bar[i] = '─';
        bar[Math.Clamp(marker, 0, width - 1)] = '●';
        return $"{info.Away.Id} {new string(bar)} {info.Home.Id}";
    }
}
