using TwelfthVoice.Domain;

namespace TwelfthVoice.Intelligence;

public sealed record MatchRecap
{
    public required string Headline { get; init; }
    public required string Summary { get; init; }
    public required IReadOnlyList<string> Scorers { get; init; }
    public PlayerStats? PlayerOfTheMatch { get; init; }
    public required IReadOnlyList<Insight> KeyMoments { get; init; }
    public required IReadOnlyList<string> StatLines { get; init; }
}

/// <summary>
/// Builds an automated match recap from final statistics and insights, with no AI required.
/// The Storyteller agent uses this as its factual skeleton and rewrites it per audience and language.
/// </summary>
public static class RecapBuilder
{
    public static MatchRecap Build(MatchInfo info, MatchSnapshot final, IReadOnlyList<Insight> insights)
    {
        var home = final.Home;
        var away = final.Away;
        string homeShort = info.Home.ShortName;
        string awayShort = info.Away.ShortName;
        int hg = final.HomeScore;
        int ag = final.AwayScore;

        string headline;
        if (hg == ag)
        {
            headline = hg == 0
                ? $"{homeShort} and {awayShort} cancel each other out in a goalless draw"
                : $"{homeShort} and {awayShort} share the spoils in a {hg}–{ag} draw";
        }
        else
        {
            bool homeWin = hg > ag;
            string winner = homeWin ? homeShort : awayShort;
            string loser = homeWin ? awayShort : homeShort;
            int margin = Math.Abs(hg - ag);
            string verb = margin == 1 ? "edge" : margin == 2 ? "beat" : "thrash";
            string venue = homeWin ? "" : " away from home";
            headline = $"{winner} {verb} {loser} {Math.Max(hg, ag)}–{Math.Min(hg, ag)}{venue}";
        }

        var playersById = final.Players.ToDictionary(p => p.PlayerId);
        var goals = insights.Where(i => i.Kind == InsightKind.Goal && i.PlayerIds.Count > 0).ToList();
        var scorers = new List<string>();
        foreach (var team in new[] { info.Home, info.Away })
        {
            var teamGoals = goals.Where(g => g.TeamId == team.Id)
                .GroupBy(g => g.PlayerIds[0])
                .Select(g => $"{(playersById.TryGetValue(g.Key, out var p) ? p.ShortName : g.Key)} {string.Join(", ", g.Select(x => MatchClock.MinuteLabel(x.Period, x.MatchSecond)))}")
                .ToList();
            if (teamGoals.Count > 0) scorers.Add($"{team.ShortName}: {string.Join("; ", teamGoals)}");
        }

        var potm = final.Players
            .Where(p => p.Touches > 0 || p.Saves > 0)
            .OrderByDescending(p => p.Rating)
            .ThenByDescending(p => p.Goals)
            .FirstOrDefault();

        var summary = new List<string>
        {
            $"{info.Home.Name} {hg}–{ag} {info.Away.Name} at {info.Venue}.",
            Narration.BalanceOfPlay(home, away, hg, ag, homeShort, awayShort)
        };

        if (potm is not null)
        {
            string feature = potm.Goals > 0 ? $"{potm.Goals} goal{(potm.Goals == 1 ? "" : "s")} from {potm.Xg:0.00} xG"
                : potm.Assists > 0 ? $"{potm.Assists} assist{(potm.Assists == 1 ? "" : "s")} and {potm.KeyPasses} key passes"
                : potm.Saves >= 3 ? $"{potm.Saves} saves"
                : potm.Tackles + potm.Interceptions >= 5 ? $"{potm.Tackles} tackles and {potm.Interceptions} interceptions"
                : $"{potm.PassesCompleted} completed passes at {Narration.Pct(potm.PassAccuracy)}";
            summary.Add($"{potm.Name} was the standout performer (rating {potm.Rating:0.0}), with {feature}.");
        }

        var turningPoint = insights
            .Where(i => i.Kind is InsightKind.MomentumShift or InsightKind.PressingShift or InsightKind.ControlShift or InsightKind.BigChance
                        || (i.Kind == InsightKind.Card && i.Importance >= 0.8))
            .OrderByDescending(i => i.Importance)
            .FirstOrDefault();
        if (turningPoint is not null)
            summary.Add($"A key moment came at {MatchClock.MinuteLabel(turningPoint.Period, turningPoint.MatchSecond)}: {turningPoint.Headline.TrimEnd('.')}. {Narration.FirstSentence(turningPoint.WhyItMatters)}");

        var fastest = final.Players.OrderByDescending(p => p.TopSpeedKmh).FirstOrDefault();
        var hardestWorker = final.Players.OrderByDescending(p => p.DistanceM).FirstOrDefault();

        var statLines = new List<string>
        {
            $"Possession: {home.PossessionPct:0}% – {away.PossessionPct:0}%",
            $"Shots (on target): {home.Shots} ({home.ShotsOnTarget}) – {away.Shots} ({away.ShotsOnTarget})",
            $"Expected goals: {home.Xg:0.00} – {away.Xg:0.00}",
            $"Pass accuracy: {Narration.Pct(home.PassAccuracy)} – {Narration.Pct(away.PassAccuracy)}",
            $"Final-third entries: {home.FinalThirdEntries} – {away.FinalThirdEntries}",
            $"Pressures (high regains): {home.Pressures} ({home.HighRegains}) – {away.Pressures} ({away.HighRegains})",
            $"Corners: {home.Corners} – {away.Corners}",
            $"Fouls / cards: {home.Fouls} / {home.YellowCards + home.RedCards} – {away.Fouls} / {away.YellowCards + away.RedCards}",
            $"Distance covered: {home.DistanceKm:0.0} km – {away.DistanceKm:0.0} km"
        };
        if (fastest is not null && fastest.TopSpeedKmh > 0)
            statLines.Add($"Fastest player: {fastest.Name} ({fastest.TopSpeedKmh:0.0} km/h)");
        if (hardestWorker is not null && hardestWorker.DistanceM > 0)
            statLines.Add($"Most distance: {hardestWorker.Name} ({hardestWorker.DistanceM / 1000:0.0} km)");

        var keyMoments = insights
            .Where(i => i.Kind is not (InsightKind.HalfTime or InsightKind.FullTime or InsightKind.Kickoff))
            .OrderByDescending(i => i.Importance)
            .Take(6)
            .OrderBy(i => i.Period)
            .ThenBy(i => i.MatchSecond)
            .ToList();

        return new MatchRecap
        {
            Headline = headline,
            Summary = string.Join(" ", summary),
            Scorers = scorers,
            PlayerOfTheMatch = potm,
            KeyMoments = keyMoments,
            StatLines = statLines
        };
    }
}
