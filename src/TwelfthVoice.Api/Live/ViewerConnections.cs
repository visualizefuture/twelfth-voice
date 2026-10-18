using System.Collections.Concurrent;
using TwelfthVoice.Intelligence;

namespace TwelfthVoice.Api.Live;

public sealed record ViewerConnection(string ConnectionId, string MatchId, ViewerProfile Profile);

/// <summary>Which viewer (SignalR connection) is watching which match, with which profile.</summary>
public sealed class ViewerConnections
{
    private readonly ConcurrentDictionary<string, ViewerConnection> _byConnection = new();

    public void Set(ViewerConnection connection) => _byConnection[connection.ConnectionId] = connection;

    public ViewerConnection? Get(string connectionId) =>
        _byConnection.TryGetValue(connectionId, out var connection) ? connection : null;

    public ViewerConnection? Remove(string connectionId) =>
        _byConnection.TryRemove(connectionId, out var connection) ? connection : null;

    public IReadOnlyList<ViewerConnection> ForMatch(string matchId) =>
        _byConnection.Values.Where(c => string.Equals(c.MatchId, matchId, StringComparison.OrdinalIgnoreCase)).ToList();

    public int CountFor(string matchId) =>
        _byConnection.Values.Count(c => string.Equals(c.MatchId, matchId, StringComparison.OrdinalIgnoreCase));
}

public static class HubGroups
{
    /// <summary>Everyone watching a match: status changes.</summary>
    public static string Match(string matchId) => $"match:{matchId}";

    /// <summary>Opt-in raw event feed, e.g. for an overlay renderer or analytics tool.</summary>
    public static string Events(string matchId) => $"events:{matchId}";
}
