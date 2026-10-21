using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using TwelfthVoice.Agents;
using TwelfthVoice.Api.Live;
using TwelfthVoice.Domain;
using TwelfthVoice.Simulator;

namespace TwelfthVoice.Api.Mcp;

/// <summary>
/// Twelfth Voice as an MCP server (at /mcp): any MCP client, such as GitHub Copilot agent mode,
/// Claude or another agent, can list and start matches and read the same live data the
/// broadcast crew's Analyst uses. Every tool except start_match is read-only.
/// </summary>
[McpServerToolType]
public sealed class MatchMcpTools
{
    private readonly MatchRegistry _registry;
    private readonly BroadcastCrew _crew;

    public MatchMcpTools(MatchRegistry registry, BroadcastCrew crew)
    {
        _registry = registry;
        _crew = crew;
    }

    [McpServerTool(Name = "list_teams"), Description("The fictional clubs in the league: code, name, nickname, formation and playing style.")]
    public string ListTeams() => Json(SyntheticLeague.BuildAll().Select(t => new
    {
        code = t.Id,
        t.Name,
        t.Nickname,
        t.Formation,
        t.Stadium,
        captain = t.Squad.FirstOrDefault(p => p.IsCaptain)?.Name,
        style = new { t.Style.Possession, t.Style.Directness, pressIntensity = t.Style.PressIntensity, t.Style.Strength }
    }));

    [McpServerTool(Name = "list_matches"), Description("Matches running on this server, newest first, with score, clock and status.")]
    public string ListMatches() => Json(_registry.All.Select(s => s.Summary()));

    [McpServerTool(Name = "start_match"), Description("Simulate a new match and start playing it live. Returns its match id.")]
    public string StartMatch(
        [Description("Home club code, e.g. HAR. Random if omitted.")] string? home = null,
        [Description("Away club code, e.g. IRV. Random if omitted.")] string? away = null,
        [Description("Seed: the same seed and clubs always give the same match. Random if omitted.")] int? seed = null,
        [Description("Match seconds per real second; 60 plays a minute per second.")] double? speed = null)
    {
        try
        {
            var session = _registry.Create(new CreateMatchRequest { HomeTeamId = home, AwayTeamId = away, Seed = seed, Speed = speed ?? 60 });
            return Json(session.Summary());
        }
        catch (ArgumentException ex)
        {
            return Json(new { error = ex.Message });
        }
    }

    [McpServerTool(Name = "get_match_state"), Description("Score, clock and the current game state: momentum, control vs chaos, tempo and who is dictating play.")]
    public string GetMatchState([Description("Match id; defaults to the most recent match")] string? matchId = null) =>
        WithMatch(matchId, tools => tools.GetMatchState());

    [McpServerTool(Name = "get_team_stats"), Description("Full statistics for one team so far.")]
    public string GetTeamStats(
        [Description("Team name, short name or code, e.g. Harbourside or HAR")] string team,
        [Description("Match id; defaults to the most recent match")] string? matchId = null) =>
        WithMatch(matchId, tools => tools.GetTeamStats(team));

    [McpServerTool(Name = "get_player_stats"), Description("One player's match so far: passes, shots, xG, goals, tackles, distance, top speed and rating.")]
    public string GetPlayerStats(
        [Description("Player name, surname or id such as HAR-09")] string player,
        [Description("Match id; defaults to the most recent match")] string? matchId = null) =>
        WithMatch(matchId, tools => tools.GetPlayerStats(player));

    [McpServerTool(Name = "get_top_players"), Description("The top five players for a metric: rating, goals, xg, shots, passes, key_passes, progressive_passes, tackles, interceptions, pressures, distance or top_speed.")]
    public string GetTopPlayers(
        [Description("The metric to rank by")] string metric,
        [Description("Optional team name or code")] string? team = null,
        [Description("Match id; defaults to the most recent match")] string? matchId = null) =>
        WithMatch(matchId, tools => tools.GetTopPlayers(metric, team));

    [McpServerTool(Name = "get_key_moments"), Description("Key moments and why each one mattered, optionally between two minutes.")]
    public string GetKeyMoments(
        [Description("Optional first minute")] int? fromMinute = null,
        [Description("Optional last minute")] int? toMinute = null,
        [Description("Match id; defaults to the most recent match")] string? matchId = null) =>
        WithMatch(matchId, tools => tools.GetKeyMoments(fromMinute, toMinute));

    [McpServerTool(Name = "get_momentum"), Description("How momentum and control changed over the match, sampled every two minutes.")]
    public string GetMomentum(
        [Description("Optional first minute")] int? fromMinute = null,
        [Description("Optional last minute")] int? toMinute = null,
        [Description("Match id; defaults to the most recent match")] string? matchId = null) =>
        WithMatch(matchId, tools => tools.GetMomentum(fromMinute, toMinute));

    [McpServerTool(Name = "get_recap"), Description("The automated recap: headline, scorers, summary, key moments and the main statistics.")]
    public string GetRecap([Description("Match id; defaults to the most recent match")] string? matchId = null)
    {
        var session = Find(matchId);
        return session is null ? NotFound(matchId) : Json(session.Recap());
    }

    [McpServerTool(Name = "get_crew_room"), Description("What the AI broadcast crew did: each draft, the fact-checker's verdicts and reasons, and what went on air.")]
    public string GetCrewRoom(
        [Description("How many recent moments, up to 80")] int take = 15,
        [Description("Match id; defaults to the most recent match")] string? matchId = null)
    {
        var session = Find(matchId);
        return session is null ? NotFound(matchId) : Json(_crew.RecentActivity(session.MatchId, take));
    }

    private string WithMatch(string? matchId, Func<MatchTools, string> read)
    {
        var session = Find(matchId);
        return session is null ? NotFound(matchId) : read(new MatchTools(session));
    }

    private MatchSession? Find(string? matchId) =>
        string.IsNullOrWhiteSpace(matchId) ? _registry.All.FirstOrDefault() : _registry.Get(matchId);

    private static string NotFound(string? matchId) => Json(new
    {
        error = string.IsNullOrWhiteSpace(matchId)
            ? "No match is running. Call start_match first."
            : $"Match '{matchId}' isn't running on this server. Call list_matches to see what is."
    });

    private static string Json<T>(T value) => JsonSerializer.Serialize(value, MatchJson.Compact);
}
