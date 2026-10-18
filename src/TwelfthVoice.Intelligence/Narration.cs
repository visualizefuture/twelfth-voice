using TwelfthVoice.Domain;

namespace TwelfthVoice.Intelligence;

/// <summary>
/// Small, deterministic phrase builders. These give Twelfth Voice a fully offline narrative
/// layer: AI agents rewrite and localise on top, but the demo never depends on a model call.
/// </summary>
public static class Narration
{
    public static string Pct(double fraction) => $"{fraction * 100:0}%";

    /// <summary>"1 in 8" style odds for a probability.</summary>
    public static string Odds(double probability)
    {
        if (probability <= 0) return "almost never";
        int n = (int)Math.Round(1 / probability);
        return n <= 1 ? "almost every time" : $"about 1 in {n}";
    }

    public static string FirstSentence(string text)
    {
        int i = text.IndexOf(". ", StringComparison.Ordinal);
        return i < 0 ? text : text[..(i + 1)];
    }

    public static string StyleDescription(TeamStyle style)
    {
        var parts = new List<string>();
        if (style.Possession >= 0.65) parts.Add("patient, possession-based");
        else if (style.Possession <= 0.45) parts.Add("happy to go long");
        if (style.Directness >= 0.62) parts.Add("direct and quick to shoot");
        if (style.PressIntensity >= 0.65) parts.Add("pressing high");
        else if (style.PressIntensity <= 0.45) parts.Add("sitting in a compact block");
        return parts.Count == 0 ? "balanced" : string.Join(", ", parts);
    }

    /// <summary>
    /// The story the numbers tell: who created more, who had the ball, and whether the
    /// scoreline is fair.
    /// </summary>
    public static string BalanceOfPlay(TeamStats home, TeamStats away, int homeGoals, int awayGoals, string homeShort, string awayShort)
    {
        var sentences = new List<string>();

        double xgGap = home.Xg - away.Xg;
        string? xgLeader = Math.Abs(xgGap) < 0.3 ? null : xgGap > 0 ? homeShort : awayShort;
        if (xgLeader is null)
            sentences.Add($"Little separates them on chance quality ({home.Xg:0.00} v {away.Xg:0.00} xG).");
        else
            sentences.Add($"{xgLeader} have created the better chances ({Math.Max(home.Xg, away.Xg):0.00} v {Math.Min(home.Xg, away.Xg):0.00} xG).");

        string possessionLeader = home.PossessionPct >= away.PossessionPct ? homeShort : awayShort;
        double possession = Math.Max(home.PossessionPct, away.PossessionPct);
        if (possession >= 55)
        {
            string tail = xgLeader is not null && xgLeader != possessionLeader
                ? $", but {xgLeader} have done more with less of it"
                : "";
            sentences.Add($"{possessionLeader} have had {possession:0}% of the ball{tail}.");
        }
        else
        {
            sentences.Add("Possession has been shared almost evenly.");
        }

        if (homeGoals != awayGoals && xgLeader is not null)
        {
            string scoreLeader = homeGoals > awayGoals ? homeShort : awayShort;
            if (scoreLeader != xgLeader)
                sentences.Add($"{scoreLeader} lead despite creating less — clinical finishing, or a bit of luck. {xgLeader} can feel hard done by.");
        }
        else if (homeGoals == awayGoals && xgLeader is not null && Math.Abs(xgGap) >= 0.8)
        {
            sentences.Add($"On chances alone, {xgLeader} deserve to be ahead.");
        }

        return string.Join(" ", sentences);
    }

    public static string ScoreLine(MatchInfo info, int homeGoals, int awayGoals) =>
        $"{info.Home.ShortName} {homeGoals}–{awayGoals} {info.Away.ShortName}";
}
