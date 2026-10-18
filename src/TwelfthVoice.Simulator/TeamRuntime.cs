using TwelfthVoice.Domain;

namespace TwelfthVoice.Simulator;

/// <summary>Mutable in-match state for one team.</summary>
internal sealed class TeamRuntime
{
    public required Team Team { get; init; }
    public required bool IsHome { get; init; }

    public List<Player> OnPitch { get; } = new();
    public List<Player> Bench { get; } = new();

    /// <summary>Base position (attacking frame) for every player currently on the pitch.</summary>
    public Dictionary<string, PitchPoint> Slots { get; } = new();

    public HashSet<string> Booked { get; } = new();
    public Queue<double> SubstitutionTimes { get; } = new();

    public int Score { get; set; }
    public double PressIntensity { get; set; }
    public double Directness { get; set; }

    public string Id => Team.Id;

    public Player Keeper => OnPitch.First(p => p.Position == Position.GK);
}

internal sealed class PhysicalTracker
{
    public double DistanceM { get; set; }
    public double TopSpeedKmh { get; set; }
    public int Sprints { get; set; }
}
