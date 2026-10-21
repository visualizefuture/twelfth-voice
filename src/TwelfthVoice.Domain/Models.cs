using System.Text.Json.Serialization;

namespace TwelfthVoice.Domain;

/// <summary>Synthetic player ratings on a 0-100 scale.</summary>
public sealed record PlayerAttributes
{
    public int Passing { get; init; } = 70;
    public int Finishing { get; init; } = 60;
    public int Dribbling { get; init; } = 65;
    public int Defending { get; init; } = 60;
    public int Pace { get; init; } = 70;
    public int Stamina { get; init; } = 70;
    public int Goalkeeping { get; init; } = 20;
}

public sealed record Player
{
    /// <summary>Stable id, e.g. "HAR-09".</summary>
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string ShortName { get; init; }
    public required int ShirtNumber { get; init; }
    public required Position Position { get; init; }
    public required string Nationality { get; init; }
    public PlayerAttributes Attributes { get; init; } = new();
    public bool IsCaptain { get; init; }
    public PlayerLook Look { get; init; } = new();
}

/// <summary>How a team likes to play. All values 0..1 except Strength (0..100).</summary>
public sealed record TeamStyle
{
    /// <summary>Preference for short, patient passing (high) vs going long (low).</summary>
    public double Possession { get; init; } = 0.5;
    /// <summary>Willingness to play forward quickly and shoot.</summary>
    public double Directness { get; init; } = 0.5;
    /// <summary>How aggressively the team presses without the ball.</summary>
    public double PressIntensity { get; init; } = 0.5;
    public int Strength { get; init; } = 75;
}

public sealed record Team
{
    /// <summary>Three-letter code, e.g. "HAR".</summary>
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string ShortName { get; init; }
    public required string City { get; init; }
    public required string Stadium { get; init; }
    public required string PrimaryColor { get; init; }
    public required string SecondaryColor { get; init; }
    public required string Formation { get; init; }
    public required TeamStyle Style { get; init; }
    /// <summary>The first eleven players are the starting XI; the rest are substitutes.</summary>
    public required IReadOnlyList<Player> Squad { get; init; }

    public string Nickname { get; init; } = "";
    public Kit? HomeKit { get; init; }
    public Kit? AwayKit { get; init; }
    public Kit? GoalkeeperKit { get; init; }
    public Crest? Crest { get; init; }

    /// <summary>The kit worn in this match (home kit, or away kit when the colours clash).</summary>
    public Kit? MatchKit { get; init; }

    /// <summary>Starting XI positions in the formation, attacking frame.</summary>
    public IReadOnlyList<LineupSlot> Lineup { get; init; } = Array.Empty<LineupSlot>();
}

public sealed record MatchInfo
{
    public required string MatchId { get; init; }
    public required string Competition { get; init; }
    public required string Venue { get; init; }
    public required DateTimeOffset KickOff { get; init; }
    public required Team Home { get; init; }
    public required Team Away { get; init; }
    public required int Seed { get; init; }
    public string Referee { get; init; } = "";
    public string Weather { get; init; } = "";

    public Team TeamById(string teamId) =>
        string.Equals(teamId, Home.Id, StringComparison.OrdinalIgnoreCase) ? Home :
        string.Equals(teamId, Away.Id, StringComparison.OrdinalIgnoreCase) ? Away :
        throw new KeyNotFoundException($"Team '{teamId}' is not part of match {MatchId}.");

    public Team Opponent(string teamId) =>
        string.Equals(teamId, Home.Id, StringComparison.OrdinalIgnoreCase) ? Away : Home;

    public bool IsHome(string teamId) => string.Equals(teamId, Home.Id, StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public IEnumerable<Player> AllPlayers => Home.Squad.Concat(Away.Squad);

    public Player? FindPlayer(string? playerId) =>
        playerId is null ? null : AllPlayers.FirstOrDefault(p => p.Id == playerId);
}

/// <summary>Cumulative physical data for one player at a point in time.</summary>
public sealed record PlayerPhysical(string PlayerId, double DistanceM, double TopSpeedKmh, int Sprints);
