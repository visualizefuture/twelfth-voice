using System.ComponentModel;
using Microsoft.Extensions.AI;
using TwelfthVoice.Domain;
using TwelfthVoice.Intelligence;

namespace TwelfthVoice.Agents;

public sealed record MomentumSample(int Period, double MatchSecond, string Clock, double Momentum, double ControlIndex, int HomeScore, int AwayScore);

/// <summary>Read-only live data the Analyst agent can look up. Implemented by a live match session.</summary>
public interface IMatchData
{
    MatchInfo Info { get; }
    MatchSnapshot Snapshot();
    IReadOnlyList<Insight> Insights();
    IReadOnlyList<MomentumSample> Momentum();
}

/// <summary>
/// The Analyst's tools: small, read-only views over the live match. Every tool result is
/// recorded so the answer's numbers can be traced back to what the tools returned.
/// </summary>
public sealed class MatchTools
{
    private readonly IMatchData _data;
    private readonly List<string> _outputs = new();

    public MatchTools(IMatchData data) => _data = data;

    /// <summary>Everything the tools returned during this question, for the number check.</summary>
    public IReadOnlyList<string> Outputs
    {
        get { lock (_outputs) return _outputs.ToList(); }
    }

    public IList<AITool> AsTools() => new List<AITool>
    {
        AIFunctionFactory.Create(GetMatchState, "get_match_state", "Score, clock, and the current game state: momentum, control vs chaos, tempo, who is dictating play."),
        AIFunctionFactory.Create(GetTeamStats, "get_team_stats", "Full statistics for one team so far."),
        AIFunctionFactory.Create(GetPlayerStats, "get_player_stats", "Statistics for one player so far: passes, shots, xG, goals, tackles, distance, top speed, rating."),
        AIFunctionFactory.Create(GetTopPlayers, "get_top_players", "The top five players for a metric, optionally for one team."),
        AIFunctionFactory.Create(GetKeyMoments, "get_key_moments", "Key moments with the explanation of why each mattered, optionally between two minutes."),
        AIFunctionFactory.Create(GetMomentum, "get_momentum", "How momentum and control changed over time, sampled every two minutes, optionally between two minutes.")
    };

    public string GetMatchState()
    {
        var s = _data.Snapshot();
        var info = _data.Info;
        string? controlling = s.State.ControllingTeamId is null ? null : info.TeamById(s.State.ControllingTeamId).Name;
        string? onTop = s.State.MomentumTeamId is null ? null : info.TeamById(s.State.MomentumTeamId).Name;
        return Record(new
        {
            home = info.Home.Name,
            away = info.Away.Name,
            score = $"{s.HomeScore}-{s.AwayScore}",
            clock = s.Clock,
            minute = MatchClock.MinuteLabel(s.Period, s.MatchSecond),
            half = s.Period == 1 ? "first" : "second",
            momentum = new { value = s.State.Momentum, scale = "-100 (away on top) to +100 (home on top)", teamOnTop = onTop },
            control = new { index = s.State.ControlIndex, label = s.State.ControlLabel, teamDictating = controlling },
            tempo = s.State.Tempo,
            passCompletionLastFiveMinutes = s.State.PassCompletionLast5,
            turnoversPerMinute = s.State.TurnoversPerMinute
        });
    }

    public string GetTeamStats([Description("Team name, short name or three-letter code, e.g. Harbourside or HAR")] string team)
    {
        var stats = FindTeam(team);
        if (stats is null) return Record(new { error = $"No team matches '{team}'.", teams = new[] { _data.Info.Home.Name, _data.Info.Away.Name } });
        var s = stats;
        return Record(new
        {
            team = s.Name,
            goals = s.Goals,
            shots = s.Shots,
            shotsOnTarget = s.ShotsOnTarget,
            xg = Math.Round(s.Xg, 2),
            bigChances = s.BigChances,
            possessionPct = s.PossessionPct,
            passesCompleted = s.PassesCompleted,
            passesAttempted = s.PassesAttempted,
            passAccuracy = s.PassAccuracy,
            progressivePasses = s.ProgressivePasses,
            finalThirdEntries = s.FinalThirdEntries,
            boxEntries = s.BoxEntries,
            pressures = s.Pressures,
            highRegains = s.HighRegains,
            tackles = s.Tackles,
            interceptions = s.Interceptions,
            corners = s.Corners,
            fouls = s.Fouls,
            yellowCards = s.YellowCards,
            redCards = s.RedCards,
            distanceKm = s.DistanceKm
        });
    }

    public string GetPlayerStats([Description("Player name, surname or id such as HAR-09")] string player)
    {
        var snapshot = _data.Snapshot();
        var matches = snapshot.Players.Where(p => Matches(p, player)).ToList();
        if (matches.Count == 0) return Record(new { error = $"No player matches '{player}'." });
        if (matches.Count > 1 && !matches.Any(p => p.PlayerId.Equals(player, StringComparison.OrdinalIgnoreCase)))
            return Record(new { error = "More than one player matches.", candidates = matches.Select(p => new { p.Name, team = TeamName(p.TeamId), p.Position }) });

        var x = matches.FirstOrDefault(p => p.PlayerId.Equals(player, StringComparison.OrdinalIgnoreCase)) ?? matches[0];
        return Record(new
        {
            name = x.Name,
            team = TeamName(x.TeamId),
            position = x.Position.ToString(),
            shirt = x.ShirtNumber,
            onPitch = x.OnPitch,
            rating = x.Rating,
            touches = x.Touches,
            passesCompleted = x.PassesCompleted,
            passesAttempted = x.PassesAttempted,
            passAccuracy = x.PassAccuracy,
            averagePassDifficulty = x.AveragePassDifficulty,
            progressivePasses = x.ProgressivePasses,
            keyPasses = x.KeyPasses,
            shots = x.Shots,
            shotsOnTarget = x.ShotsOnTarget,
            goals = x.Goals,
            assists = x.Assists,
            xg = Math.Round(x.Xg, 2),
            fastestShotKmh = x.FastestShotKmh,
            dribblesCompleted = x.DribblesCompleted,
            tackles = x.Tackles,
            interceptions = x.Interceptions,
            pressures = x.Pressures,
            distanceKm = Math.Round(x.DistanceM / 1000, 2),
            topSpeedKmh = x.TopSpeedKmh,
            sprints = x.Sprints
        });
    }

    public string GetTopPlayers(
        [Description("One of: rating, goals, xg, shots, passes, key_passes, progressive_passes, tackles, interceptions, pressures, distance, top_speed")] string metric,
        [Description("Optional team name or code to limit the list")] string? team = null)
    {
        Func<PlayerStats, double>? selector = metric.Trim().ToLowerInvariant().Replace(' ', '_') switch
        {
            "rating" => p => p.Rating,
            "goals" => p => p.Goals,
            "xg" => p => p.Xg,
            "shots" => p => p.Shots,
            "passes" => p => p.PassesCompleted,
            "key_passes" => p => p.KeyPasses,
            "progressive_passes" => p => p.ProgressivePasses,
            "tackles" => p => p.Tackles,
            "interceptions" => p => p.Interceptions,
            "pressures" => p => p.Pressures,
            "distance" => p => p.DistanceM / 1000,
            "top_speed" => p => p.TopSpeedKmh,
            _ => null
        };
        if (selector is null) return Record(new { error = $"Unknown metric '{metric}'." });

        var teamStats = team is null ? null : FindTeam(team);
        var top = _data.Snapshot().Players
            .Where(p => p.Touches > 0 || p.DistanceM > 0)
            .Where(p => teamStats is null || p.TeamId == teamStats.TeamId)
            .OrderByDescending(selector)
            .Take(5)
            .Select(p => new { name = p.Name, team = TeamName(p.TeamId), position = p.Position.ToString(), value = Math.Round(selector(p), 2) });

        return Record(new { metric, top });
    }

    public string GetKeyMoments(
        [Description("Optional first minute, e.g. 60")] int? fromMinute = null,
        [Description("Optional last minute, e.g. 75")] int? toMinute = null)
    {
        var moments = _data.Insights()
            .Where(i => i.Importance >= 0.4)
            .Where(i => InWindow(Minute(i.Period, i.MatchSecond), fromMinute, toMinute))
            .OrderByDescending(i => i.Importance)
            .Take(12)
            .OrderBy(i => i.Period).ThenBy(i => i.MatchSecond)
            .Select(i => new
            {
                minute = MatchClock.MinuteLabel(i.Period, i.MatchSecond),
                kind = i.Kind.ToString(),
                headline = i.Headline,
                why = i.WhyItMatters,
                numbers = i.AnalystNote
            });

        return Record(new { moments });
    }

    public string GetMomentum(
        [Description("Optional first minute")] int? fromMinute = null,
        [Description("Optional last minute")] int? toMinute = null)
    {
        var samples = _data.Momentum()
            .Where(m => InWindow(Minute(m.Period, m.MatchSecond), fromMinute, toMinute))
            .GroupBy(m => (m.Period, Bucket: (int)(m.MatchSecond / 120)))
            .Select(g => g.Last())
            .Select(m => new
            {
                minute = MatchClock.MinuteLabel(m.Period, m.MatchSecond),
                momentum = Math.Round(m.Momentum),
                control = Math.Round(m.ControlIndex),
                score = $"{m.HomeScore}-{m.AwayScore}"
            });

        return Record(new
        {
            home = _data.Info.Home.Name,
            away = _data.Info.Away.Name,
            scale = "momentum: -100 (away on top) to +100 (home on top); control: 0 chaotic to 100 controlled",
            samples
        });
    }

    // ------------------------------------------------------------------ helpers

    private TeamStats? FindTeam(string query)
    {
        var s = _data.Snapshot();
        var info = _data.Info;
        foreach (var (team, stats) in new[] { (info.Home, s.Home), (info.Away, s.Away) })
        {
            if (Contains(team.Id, query) || Contains(team.Name, query) || Contains(team.ShortName, query) || Contains(query, team.ShortName))
                return stats;
        }
        return null;
    }

    private static bool Matches(PlayerStats p, string query) =>
        p.PlayerId.Equals(query.Trim(), StringComparison.OrdinalIgnoreCase)
        || Contains(p.Name, query)
        || Contains(query, p.ShortName);

    private static bool Contains(string haystack, string needle) =>
        !string.IsNullOrWhiteSpace(needle) && haystack.Contains(needle.Trim(), StringComparison.OrdinalIgnoreCase);

    private string TeamName(string teamId) => _data.Info.TeamById(teamId).Name;

    private static int Minute(int period, double matchSecond) => MatchClock.Minute(period, matchSecond);

    private static bool InWindow(int minute, int? from, int? to) =>
        (from is null || minute >= from) && (to is null || minute <= to);

    private string Record(object value)
    {
        string json = CrewJson.Serialize(value);
        lock (_outputs) _outputs.Add(json);
        return json;
    }
}
