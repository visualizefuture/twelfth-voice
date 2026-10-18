using TwelfthVoice.Domain;

namespace TwelfthVoice.Simulator;

/// <summary>
/// A fictional league. Clubs, colours, stadiums and players are all invented, so demos and
/// videos never use real trademarks or player likenesses.
/// Squads are generated from a fixed league seed, so the same players appear in every match.
/// </summary>
public static class SyntheticLeague
{
    public const string CompetitionName = "Twelfth Voice Synthetic League";
    public const int DefaultLeagueSeed = 2026;

    private sealed record ClubSeed(
        string Id, string Name, string ShortName, string City, string Stadium,
        string Primary, string Secondary, string Formation,
        double Possession, double Directness, double Press, int Strength);

    private static readonly ClubSeed[] Clubs =
    {
        new("HAR", "Harbourside Rovers", "Harbourside", "Port Avery", "The Quayside", "#1B2A49", "#7FC8F8", "4-3-3", 0.78, 0.35, 0.72, 81),
        new("KIN", "Kingsbridge Athletic", "Kingsbridge", "Kingsbridge", "Crownfield Park", "#C8102E", "#FFFFFF", "4-2-3-1", 0.55, 0.55, 0.60, 79),
        new("IRV", "Ironvale United", "Ironvale", "Ironvale", "The Foundry Ground", "#111111", "#E0B03A", "4-2-3-1", 0.40, 0.72, 0.50, 76),
        new("WMC", "Westmoor City", "Westmoor", "Westmoor", "Moorgate Stadium", "#5DA9E9", "#F5F5F5", "4-3-3", 0.66, 0.48, 0.66, 78),
        new("ASH", "Ashcombe Town", "Ashcombe", "Ashcombe", "Elm Lane", "#2E7D32", "#FFFFFF", "4-2-3-1", 0.45, 0.62, 0.42, 73),
        new("NGA", "Northgate Albion", "Northgate", "Northgate", "Lantern Road", "#7A1F3D", "#8EC5E8", "4-3-3", 0.58, 0.50, 0.55, 75),
    };

    public static IReadOnlyList<string> TeamIds { get; } = Clubs.Select(c => c.Id).ToArray();

    public static Team BuildTeam(string teamId, int leagueSeed = DefaultLeagueSeed)
    {
        var club = Clubs.FirstOrDefault(c => c.Id.Equals(teamId, StringComparison.OrdinalIgnoreCase))
                   ?? throw new ArgumentException($"Unknown team '{teamId}'. Known teams: {string.Join(", ", TeamIds)}");

        var rng = new Rng(leagueSeed ^ StableHash.Of(club.Id));

        return new Team
        {
            Id = club.Id,
            Name = club.Name,
            ShortName = club.ShortName,
            City = club.City,
            Stadium = club.Stadium,
            PrimaryColor = club.Primary,
            SecondaryColor = club.Secondary,
            Formation = club.Formation,
            Style = new TeamStyle
            {
                Possession = club.Possession,
                Directness = club.Directness,
                PressIntensity = club.Press,
                Strength = club.Strength
            },
            Squad = SquadGenerator.Generate(club.Id, club.Formation, club.Strength, rng)
        };
    }

    public static IReadOnlyList<Team> BuildAll(int leagueSeed = DefaultLeagueSeed) =>
        TeamIds.Select(id => BuildTeam(id, leagueSeed)).ToArray();
}

internal static class SquadGenerator
{
    public static IReadOnlyList<Player> Generate(string teamId, string formation, int strength, Rng rng)
    {
        var players = new List<Player>();
        var usedLastNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var slot in Formations.Get(formation))
            players.Add(Create(teamId, slot.Position, slot.Shirt, strength, rng, usedLastNames));

        foreach (var (position, shirt) in Formations.Bench)
            players.Add(Create(teamId, position, shirt, strength - 3, rng, usedLastNames));

        return players;
    }

    private static Player Create(string teamId, Position position, int shirt, int strength, Rng rng, HashSet<string> usedLastNames)
    {
        NamePools.Nation nation;
        string first, last;
        int attempts = 0;
        do
        {
            nation = rng.Pick(NamePools.Nations);
            first = rng.Pick(nation.First);
            last = rng.Pick(nation.Last);
        }
        while (!usedLastNames.Add(last) && ++attempts < 50);

        return new Player
        {
            Id = $"{teamId}-{shirt:D2}",
            Name = $"{first} {last}",
            ShortName = last,
            ShirtNumber = shirt,
            Position = position,
            Nationality = nation.Name,
            Attributes = Attributes(position, strength, rng)
        };
    }

    private static PlayerAttributes Attributes(Position position, int strength, Rng rng)
    {
        double b = rng.Normal(strength, 4);
        int A(double v) => (int)Math.Round(Math.Clamp(v + rng.Normal(0, 3), 25, 97));

        return position switch
        {
            Position.GK => new PlayerAttributes
            {
                Goalkeeping = A(b + 6), Passing = A(b - 12), Finishing = A(25), Dribbling = A(40),
                Defending = A(50), Pace = A(55), Stamina = A(b - 5)
            },
            Position.CB => new PlayerAttributes
            {
                Defending = A(b + 8), Passing = A(b - 5), Pace = A(b - 6), Finishing = A(b - 30),
                Dribbling = A(b - 15), Stamina = A(b), Goalkeeping = A(20)
            },
            Position.RB or Position.LB => new PlayerAttributes
            {
                Defending = A(b + 2), Passing = A(b), Pace = A(b + 6), Finishing = A(b - 25),
                Dribbling = A(b - 4), Stamina = A(b + 6), Goalkeeping = A(20)
            },
            Position.DM => new PlayerAttributes
            {
                Defending = A(b + 5), Passing = A(b + 4), Pace = A(b - 4), Finishing = A(b - 18),
                Dribbling = A(b - 4), Stamina = A(b + 6), Goalkeeping = A(20)
            },
            Position.CM => new PlayerAttributes
            {
                Defending = A(b - 4), Passing = A(b + 6), Pace = A(b - 2), Finishing = A(b - 10),
                Dribbling = A(b + 2), Stamina = A(b + 8), Goalkeeping = A(20)
            },
            Position.AM => new PlayerAttributes
            {
                Defending = A(b - 15), Passing = A(b + 6), Pace = A(b), Finishing = A(b - 2),
                Dribbling = A(b + 6), Stamina = A(b), Goalkeeping = A(20)
            },
            Position.RW or Position.LW => new PlayerAttributes
            {
                Defending = A(b - 20), Passing = A(b), Pace = A(b + 10), Finishing = A(b - 3),
                Dribbling = A(b + 8), Stamina = A(b + 2), Goalkeeping = A(20)
            },
            _ => new PlayerAttributes // ST
            {
                Defending = A(b - 25), Passing = A(b - 5), Pace = A(b + 3), Finishing = A(b + 10),
                Dribbling = A(b), Stamina = A(b - 2), Goalkeeping = A(20)
            },
        };
    }
}
