using System.Text.Json;
using TwelfthVoice.Domain;
using TwelfthVoice.Intelligence;
using TwelfthVoice.Simulator;

namespace TwelfthVoice.Cli;

internal static class Commands
{
    public static int Help()
    {
        Console.WriteLine("""
            Twelfth Voice — synthetic match intelligence

            Commands:
              teams                              List the fictional clubs and their starting XIs
              generate [options] [--out FILE]    Simulate a match and write events, insights and recap to JSON
              recap    [options] [--json]        Simulate a match and print the automated recap
              replay   [options]                 Replay a match live, printing insights as they happen

            Match options:
              --seed N          Match seed (default 42). Same seed + teams = identical match
              --home CODE       Home team code, e.g. HAR (default: random)
              --away CODE       Away team code, e.g. IRV (default: random)

            Replay options:
              --speed N         Speed multiplier (default 60 = one match minute per second)
              --mode casual|analyst   Audience mode (default casual)
              --follow ID       Focus on a team code (HAR) or player id (HAR-09)

            Examples:
              twelfth-voice replay --home HAR --away IRV --seed 7 --speed 120
              twelfth-voice replay --home HAR --away IRV --seed 7 --mode analyst --follow HAR-09
              twelfth-voice generate --home KIN --away WMC --seed 11 --out data/kin-wmc.json
            """);
        return 0;
    }

    public static int Teams()
    {
        foreach (var team in SyntheticLeague.BuildAll())
        {
            ConsoleRender.Line($"{team.Id}  {team.Name}  ({team.Formation}, strength {team.Style.Strength})", ConsoleColor.Yellow);
            Console.WriteLine($"     {team.Stadium}, {team.City} · {Narration.StyleDescription(team.Style)}");
            foreach (var p in team.Squad.Take(11))
                Console.WriteLine($"     {p.ShirtNumber,2} {p.Position,-3} {p.Name,-26} {p.Nationality,-12} {p.Id}");
            Console.WriteLine($"     Bench: {string.Join(", ", team.Squad.Skip(11).Select(p => $"{p.ShortName} ({p.Position})"))}");
            Console.WriteLine();
        }
        return 0;
    }

    public static int Generate(CliArgs cli)
    {
        var match = MatchSimulator.Run(cli.ToSimulationOptions());
        var analyzer = new MatchAnalyzer(match.Info);
        analyzer.ProcessAll(match.Events);
        var final = analyzer.Snapshot();
        var recap = RecapBuilder.Build(match.Info, final, analyzer.Insights);

        string path = cli.Get("out") ?? $"{match.Info.MatchId}.json";
        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var document = new
        {
            match = match.Info,
            events = match.Events,
            insights = analyzer.Insights,
            finalSnapshot = final,
            recap
        };
        var options = cli.Has("pretty") ? MatchJson.Indented : MatchJson.Compact;
        File.WriteAllText(path, JsonSerializer.Serialize(document, options));

        ConsoleRender.Line($"{recap.Headline}", ConsoleColor.Yellow);
        Console.WriteLine($"Wrote {match.Events.Count} events and {analyzer.Insights.Count} insights to {Path.GetFullPath(path)}");
        Console.WriteLine();
        foreach (var group in match.Events.GroupBy(e => e.Type).OrderByDescending(g => g.Count()))
            Console.WriteLine($"  {group.Key,-18} {group.Count(),5}");
        return 0;
    }

    public static int Recap(CliArgs cli)
    {
        var match = MatchSimulator.Run(cli.ToSimulationOptions());
        var analyzer = new MatchAnalyzer(match.Info);
        analyzer.ProcessAll(match.Events);
        var recap = RecapBuilder.Build(match.Info, analyzer.Snapshot(), analyzer.Insights);

        if (cli.Has("json"))
        {
            Console.WriteLine(JsonSerializer.Serialize(recap, MatchJson.Indented));
            return 0;
        }

        ConsoleRender.PrintRecap(recap);
        return 0;
    }

    public static async Task<int> ReplayAsync(CliArgs cli, CancellationToken cancellationToken)
    {
        var match = MatchSimulator.Run(cli.ToSimulationOptions());
        var info = match.Info;
        var analyzer = new MatchAnalyzer(info);
        var profile = new ViewerProfile(
            Mode: string.Equals(cli.Get("mode"), "analyst", StringComparison.OrdinalIgnoreCase) ? ViewerMode.Analyst : ViewerMode.Casual,
            Follow: cli.Get("follow")?.ToUpperInvariant());
        double speed = cli.GetDouble("speed", 60);

        ConsoleRender.Line($"{info.Home.Name} v {info.Away.Name} · {info.Venue} · {info.Competition}", ConsoleColor.White);
        Console.WriteLine($"Seed {info.Seed} · Referee {info.Referee} · {info.Weather} · Mode {profile.Mode}{(profile.Follow is null ? "" : $" · Following {profile.Follow}")}");
        Console.WriteLine(new string('─', 72));

        double nextScoreboard = 300;
        await foreach (var e in LiveFeed.PlayAsync(match.Events, speed, cancellationToken))
        {
            var insights = analyzer.Process(e);
            foreach (var insight in insights.Where(profile.Wants))
                ConsoleRender.PrintInsight(insight, profile);

            if (e.Type != EventType.TrackingSnapshot && e.MatchSecond >= nextScoreboard)
            {
                nextScoreboard = (Math.Floor(e.MatchSecond / 300) + 1) * 300;
                ConsoleRender.PrintScoreboard(info, analyzer.Snapshot(), profile);
            }
        }

        Console.WriteLine(new string('─', 72));
        ConsoleRender.PrintRecap(RecapBuilder.Build(info, analyzer.Snapshot(), analyzer.Insights));
        return 0;
    }
}

internal enum ViewerMode
{
    Casual,
    Analyst
}

/// <summary>
/// Early preview of the Personalize stage: the same insight stream filtered and rendered
/// differently for different viewers.
/// </summary>
internal sealed record ViewerProfile(ViewerMode Mode, string? Follow)
{
    public bool Wants(Insight insight)
    {
        bool essential = insight.Kind is InsightKind.Goal or InsightKind.HalfTime or InsightKind.FullTime or InsightKind.Kickoff
                         || insight.Importance >= 0.85;
        if (essential) return true;

        if (Follow is not null)
        {
            bool aboutFollowed = string.Equals(insight.TeamId, Follow, StringComparison.OrdinalIgnoreCase)
                                 || insight.PlayerIds.Any(id => string.Equals(id, Follow, StringComparison.OrdinalIgnoreCase));
            if (!aboutFollowed) return false;
        }

        return Mode == ViewerMode.Analyst || insight.Importance >= 0.45;
    }
}
