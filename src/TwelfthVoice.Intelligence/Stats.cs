using TwelfthVoice.Domain;

namespace TwelfthVoice.Intelligence;

public sealed record TeamStats
{
    public required string TeamId { get; init; }
    public required string Name { get; init; }

    public int Goals { get; set; }
    public int Shots { get; set; }
    public int ShotsOnTarget { get; set; }
    public double Xg { get; set; }
    public int BigChances { get; set; }

    public int PassesAttempted { get; set; }
    public int PassesCompleted { get; set; }
    public int ProgressivePasses { get; set; }
    public int FinalThirdEntries { get; set; }
    public int BoxEntries { get; set; }
    public int Crosses { get; set; }

    public int Tackles { get; set; }
    public int Interceptions { get; set; }
    public int Pressures { get; set; }
    public int HighRegains { get; set; }
    public int Clearances { get; set; }
    public int Blocks { get; set; }
    public int Saves { get; set; }

    public int Fouls { get; set; }
    public int Corners { get; set; }
    public int Offsides { get; set; }
    public int YellowCards { get; set; }
    public int RedCards { get; set; }

    public double PossessionSeconds { get; set; }
    public double PossessionPct { get; set; }
    public double DistanceKm { get; set; }
    public int Sprints { get; set; }

    public double PassAccuracy => PassesAttempted == 0 ? 0 : Math.Round((double)PassesCompleted / PassesAttempted, 3);
}

public sealed record PlayerStats
{
    public required string PlayerId { get; init; }
    public required string TeamId { get; init; }
    public required string Name { get; init; }
    public required string ShortName { get; init; }
    public required int ShirtNumber { get; init; }
    public required Position Position { get; init; }

    public bool OnPitch { get; set; }
    public double MinutesPlayed { get; set; }
    public double EnteredAt { get; set; }

    public int Touches { get; set; }
    public int PassesAttempted { get; set; }
    public int PassesCompleted { get; set; }
    public int ProgressivePasses { get; set; }
    public int KeyPasses { get; set; }
    public double TotalPassDifficulty { get; set; }
    public double LongestPassM { get; set; }

    public int Shots { get; set; }
    public int ShotsOnTarget { get; set; }
    public int Goals { get; set; }
    public int Assists { get; set; }
    public double Xg { get; set; }
    public double XgAssisted { get; set; }
    public double FastestShotKmh { get; set; }

    public int DribblesAttempted { get; set; }
    public int DribblesCompleted { get; set; }
    public int Tackles { get; set; }
    public int Interceptions { get; set; }
    public int Pressures { get; set; }
    public int Clearances { get; set; }
    public int Blocks { get; set; }
    public int Saves { get; set; }
    public int FoulsCommitted { get; set; }
    public int FoulsWon { get; set; }
    public int YellowCards { get; set; }
    public int RedCards { get; set; }

    public double DistanceM { get; set; }
    public double TopSpeedKmh { get; set; }
    public int Sprints { get; set; }

    public double PassAccuracy => PassesAttempted == 0 ? 0 : Math.Round((double)PassesCompleted / PassesAttempted, 3);

    public double AveragePassDifficulty => PassesCompleted == 0 ? 0 : Math.Round(TotalPassDifficulty / PassesCompleted, 2);

    /// <summary>A transparent 3-10 match rating built only from the counted stats above.</summary>
    public double Rating
    {
        get
        {
            if (Touches == 0 && Saves == 0 && Tackles == 0) return 6.0;
            double volume = Math.Min(PassesAttempted, 40) / 40.0;
            double r = 6.0
                       + Goals * 1.0
                       + Assists * 0.6
                       + Xg * 0.5
                       + KeyPasses * 0.25
                       + ProgressivePasses * 0.04
                       + (PassAccuracy - 0.8) * 3 * volume
                       + DribblesCompleted * 0.1
                       + Tackles * 0.15
                       + Interceptions * 0.12
                       + Blocks * 0.1
                       + Saves * 0.3
                       - YellowCards * 0.3
                       - RedCards * 1.5
                       - (DribblesAttempted - DribblesCompleted) * 0.05;
            return Math.Round(Math.Clamp(r, 3, 10), 1);
        }
    }
}
