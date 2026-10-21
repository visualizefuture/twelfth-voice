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

    private sealed record ClubLook(string Nickname, Kit Home, Kit Away, Kit Goalkeeper, Crest Crest);

    private static readonly ClubSeed[] Clubs =
    {
        new("HAR", "Harbourside Rovers", "Harbourside", "Port Avery", "The Quayside", "#1B2A49", "#7FC8F8", "4-3-3", 0.78, 0.35, 0.72, 81),
        new("KIN", "Kingsbridge Athletic", "Kingsbridge", "Kingsbridge", "Crownfield Park", "#C8102E", "#FFFFFF", "4-2-3-1", 0.55, 0.55, 0.60, 79),
        new("IRV", "Ironvale United", "Ironvale", "Ironvale", "The Foundry Ground", "#111111", "#E0B03A", "4-2-3-1", 0.40, 0.72, 0.50, 76),
        new("WMC", "Westmoor City", "Westmoor", "Westmoor", "Moorgate Stadium", "#5DA9E9", "#F5F5F5", "4-3-3", 0.66, 0.48, 0.66, 78),
        new("ASH", "Ashcombe Town", "Ashcombe", "Ashcombe", "Elm Lane", "#2E7D32", "#FFFFFF", "4-2-3-1", 0.45, 0.62, 0.42, 73),
        new("NGA", "Northgate Albion", "Northgate", "Northgate", "Lantern Road", "#7A1F3D", "#8EC5E8", "4-3-3", 0.58, 0.50, 0.55, 75),
    };

    /// <summary>Original kits and crests. Designed for this app; not based on any real club.</summary>
    private static readonly Dictionary<string, ClubLook> Looks = new()
    {
        ["HAR"] = new("The Tides",
            new Kit { Shirt = "#1B2A49", Trim = "#7FC8F8", Pattern = KitPattern.Hoops, Shorts = "#FFFFFF", Socks = "#1B2A49", Number = "#FFFFFF" },
            new Kit { Shirt = "#F4F6F8", Trim = "#1B2A49", Pattern = KitPattern.Sash, Shorts = "#1B2A49", Socks = "#F4F6F8", Number = "#1B2A49" },
            new Kit { Shirt = "#B6E22E", Trim = "#1B2A49", Shorts = "#1B2A49", Socks = "#B6E22E", Number = "#1B2A49" },
            new Crest { Shape = CrestShape.Roundel, Emblem = "anchor", Field = "#1B2A49", Accent = "#7FC8F8", Founded = 1887, Motto = "By wind and tide" }),
        ["KIN"] = new("The Crowns",
            new Kit { Shirt = "#C8102E", Trim = "#FFFFFF", Pattern = KitPattern.Sleeves, Shorts = "#FFFFFF", Socks = "#C8102E", Number = "#FFFFFF" },
            new Kit { Shirt = "#13294B", Trim = "#C8102E", Shorts = "#13294B", Socks = "#13294B", Number = "#FFFFFF" },
            new Kit { Shirt = "#F28C28", Trim = "#222222", Shorts = "#222222", Socks = "#F28C28", Number = "#222222" },
            new Crest { Shape = CrestShape.Shield, Emblem = "crown", Field = "#C8102E", Accent = "#F2C14E", Founded = 1879, Motto = "Over the bridge" }),
        ["IRV"] = new("The Anvils",
            new Kit { Shirt = "#111111", Trim = "#E0B03A", Pattern = KitPattern.Stripes, Shorts = "#111111", Socks = "#E0B03A", Number = "#E0B03A" },
            new Kit { Shirt = "#F2F2F2", Trim = "#111111", Pattern = KitPattern.Plain, Shorts = "#F2F2F2", Socks = "#111111", Number = "#111111" },
            new Kit { Shirt = "#2DB3A6", Trim = "#111111", Shorts = "#111111", Socks = "#2DB3A6", Number = "#111111" },
            new Crest { Shape = CrestShape.Badge, Emblem = "anvil", Field = "#111111", Accent = "#E0B03A", Founded = 1892, Motto = "Forged together" }),
        ["WMC"] = new("The Stags",
            new Kit { Shirt = "#5DA9E9", Trim = "#13294B", Pattern = KitPattern.Plain, Shorts = "#FFFFFF", Socks = "#5DA9E9", Number = "#13294B" },
            new Kit { Shirt = "#6B1E3B", Trim = "#5DA9E9", Pattern = KitPattern.Halves, Shorts = "#6B1E3B", Socks = "#6B1E3B", Number = "#FFFFFF" },
            new Kit { Shirt = "#9B5DE5", Trim = "#FFFFFF", Shorts = "#9B5DE5", Socks = "#9B5DE5", Number = "#FFFFFF" },
            new Crest { Shape = CrestShape.Roundel, Emblem = "stag", Field = "#5DA9E9", Accent = "#13294B", Founded = 1894, Motto = "Up on the moor" }),
        ["ASH"] = new("The Oaks",
            new Kit { Shirt = "#2E7D32", Trim = "#FFFFFF", Pattern = KitPattern.Halves, Shorts = "#FFFFFF", Socks = "#2E7D32", Number = "#FFFFFF" },
            new Kit { Shirt = "#F2C14E", Trim = "#2E7D32", Pattern = KitPattern.Plain, Shorts = "#2E7D32", Socks = "#F2C14E", Number = "#2E7D32" },
            new Kit { Shirt = "#EF476F", Trim = "#222222", Shorts = "#222222", Socks = "#EF476F", Number = "#222222" },
            new Crest { Shape = CrestShape.Shield, Emblem = "oak", Field = "#2E7D32", Accent = "#F2C14E", Founded = 1901, Motto = "Deep roots" }),
        ["NGA"] = new("The Lanterns",
            new Kit { Shirt = "#7A1F3D", Trim = "#8EC5E8", Pattern = KitPattern.Sleeves, Shorts = "#FFFFFF", Socks = "#7A1F3D", Number = "#FFFFFF" },
            new Kit { Shirt = "#F4F6F8", Trim = "#7A1F3D", Pattern = KitPattern.Stripes, Shorts = "#7A1F3D", Socks = "#F4F6F8", Number = "#7A1F3D" },
            new Kit { Shirt = "#FFD60A", Trim = "#222222", Shorts = "#222222", Socks = "#FFD60A", Number = "#222222" },
            new Crest { Shape = CrestShape.Pennant, Emblem = "lantern", Field = "#7A1F3D", Accent = "#8EC5E8", Founded = 1885, Motto = "Light the way home" }),
    };

    public static IReadOnlyList<string> TeamIds { get; } = Clubs.Select(c => c.Id).ToArray();

    public static Team BuildTeam(string teamId, int leagueSeed = DefaultLeagueSeed)
    {
        var club = Clubs.FirstOrDefault(c => c.Id.Equals(teamId, StringComparison.OrdinalIgnoreCase))
                   ?? throw new ArgumentException($"Unknown team '{teamId}'. Known teams: {string.Join(", ", TeamIds)}");

        var rng = new Rng(leagueSeed ^ StableHash.Of(club.Id));
        var look = Looks[club.Id];
        var squad = SquadGenerator.Generate(club.Id, club.Formation, club.Strength, rng);
        squad = SquadGenerator.Dress(squad, leagueSeed);

        var template = Formations.Get(club.Formation);
        var lineup = squad.Take(template.Count)
            .Select((p, i) => new LineupSlot(p.Id, template[i].Position, template[i].Base))
            .ToArray();

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
            Squad = squad,
            Nickname = look.Nickname,
            HomeKit = look.Home,
            AwayKit = look.Away,
            GoalkeeperKit = look.Goalkeeper,
            Crest = look.Crest,
            MatchKit = look.Home,
            Lineup = lineup
        };
    }

    public static IReadOnlyList<Team> BuildAll(int leagueSeed = DefaultLeagueSeed) =>
        TeamIds.Select(id => BuildTeam(id, leagueSeed)).ToArray();

    /// <summary>
    /// The away side changes into its away kit when the two home shirts would be hard to tell apart.
    /// </summary>
    public static Team ForAwayFixture(Team away, Team home)
    {
        if (away.HomeKit is null || away.AwayKit is null || home.HomeKit is null) return away;
        bool clash = ColourDistance(away.HomeKit.Shirt, home.HomeKit.Shirt) < 150;
        return clash ? away with { MatchKit = away.AwayKit } : away;
    }

    /// <summary>"Redmean" colour distance: a cheap approximation of how different two colours look.</summary>
    private static double ColourDistance(string a, string b)
    {
        static (int R, int G, int B) Parse(string hex)
        {
            hex = hex.TrimStart('#');
            return (Convert.ToInt32(hex[..2], 16), Convert.ToInt32(hex[2..4], 16), Convert.ToInt32(hex[4..6], 16));
        }
        var x = Parse(a);
        var y = Parse(b);
        double r = (x.R + y.R) / 2.0;
        int dr = x.R - y.R, dg = x.G - y.G, db = x.B - y.B;
        return Math.Sqrt((2 + r / 256) * dr * dr + 4 * dg * dg + (2 + (255 - r) / 256) * db * db);
    }
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

    private static readonly string[] SkinTones = { "#F3D2B8", "#E8B896", "#D49A6A", "#B97A4F", "#8D5A3B", "#6B4027", "#4A2B1A" };
    private static readonly string[] HairStyles = { "short", "buzz", "curly", "afro", "long", "bun", "bald", "fringe" };
    private static readonly string[] HairColours = { "#1C1410", "#2B1B10", "#4A2E1C", "#6B4A2B", "#A0723F", "#D9B26A", "#8A2E14", "#5B5B5B" };
    private static readonly string[] FacialHair = { "none", "none", "none", "stubble", "stubble", "beard", "moustache" };

    /// <summary>
    /// Gives every player an illustrated look and names a captain. Uses its own seeded random
    /// source so adding looks never changes the generated names or ratings.
    /// </summary>
    public static IReadOnlyList<Player> Dress(IReadOnlyList<Player> squad, int leagueSeed)
    {
        var starters = squad.Take(11).Where(p => p.Position != Position.GK).ToList();
        string? captainId = starters
            .OrderByDescending(p => p.Attributes.Passing + p.Attributes.Defending + (p.Position is Position.CB or Position.DM or Position.CM ? 8 : 0))
            .Select(p => p.Id)
            .FirstOrDefault();

        return squad.Select(p =>
        {
            var rng = new Rng(leagueSeed ^ StableHash.Of(p.Id + ":look"));
            return p with
            {
                IsCaptain = p.Id == captainId,
                Look = new PlayerLook
                {
                    SkinTone = rng.Pick(SkinTones),
                    HairStyle = rng.Pick(HairStyles),
                    HairColor = rng.Pick(HairColours),
                    FacialHair = rng.Pick(FacialHair)
                }
            };
        }).ToArray();
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
