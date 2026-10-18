using System.Collections.Concurrent;
using TwelfthVoice.Api.Storage;
using TwelfthVoice.Simulator;

namespace TwelfthVoice.Api.Live;

/// <summary>All matches currently held in memory by this API instance.</summary>
public sealed class MatchRegistry
{
    private readonly ConcurrentDictionary<string, MatchSession> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly IMatchBroadcaster _broadcaster;
    private readonly IMatchStore _store;
    private readonly ILoggerFactory _loggerFactory;
    private readonly CancellationToken _stopping;

    public MatchRegistry(IMatchBroadcaster broadcaster, IMatchStore store, ILoggerFactory loggerFactory, IHostApplicationLifetime lifetime)
    {
        _broadcaster = broadcaster;
        _store = store;
        _loggerFactory = loggerFactory;
        _stopping = lifetime.ApplicationStopping;
    }

    public IReadOnlyList<MatchSession> All => _sessions.Values.OrderByDescending(s => s.CreatedUtc).ToList();

    public MatchSession? Get(string matchId) => _sessions.TryGetValue(matchId, out var session) ? session : null;

    /// <exception cref="ArgumentException">Unknown or duplicate team codes.</exception>
    public MatchSession Create(CreateMatchRequest request)
    {
        var ids = SyntheticLeague.TeamIds;
        string home = Normalize(request.HomeTeamId) ?? ids[Random.Shared.Next(ids.Count)];
        var awayChoices = ids.Where(id => id != home).ToList();
        string away = Normalize(request.AwayTeamId) ?? awayChoices[Random.Shared.Next(awayChoices.Count)];

        if (!ids.Contains(home)) throw new ArgumentException($"Unknown home team '{home}'. Known teams: {string.Join(", ", ids)}");
        if (!ids.Contains(away)) throw new ArgumentException($"Unknown away team '{away}'. Known teams: {string.Join(", ", ids)}");
        if (home == away) throw new ArgumentException("Home and away teams must be different.");

        int seed = request.Seed ?? Random.Shared.Next(1, 100_000);
        // Unique per run, so the same fixture can be replayed and stored more than once.
        string runId = Guid.NewGuid().ToString("N")[..6];
        string matchId = $"TV-{home}-{away}-{seed}-{runId}";

        var match = MatchSimulator.Run(new SimulationOptions
        {
            Seed = seed,
            HomeTeamId = home,
            AwayTeamId = away,
            MatchId = matchId
        });

        var session = new MatchSession(
            match,
            request.Speed ?? 60,
            _broadcaster,
            _store,
            _loggerFactory.CreateLogger($"Match.{matchId}"));

        _sessions[matchId] = session;
        _store.SaveMatch(match.Info, MatchStatus.Ready.ToString());

        if (request.AutoStart ?? true) session.Start(_stopping);
        return session;
    }

    private static string? Normalize(string? teamId) =>
        string.IsNullOrWhiteSpace(teamId) ? null : teamId.Trim().ToUpperInvariant();
}
