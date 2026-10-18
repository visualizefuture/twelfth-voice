using TwelfthVoice.Domain;

namespace TwelfthVoice.Simulator;

/// <summary>Formation templates: position, shirt number and base location (attacking frame) for each starter.</summary>
internal static class Formations
{
    public sealed record Slot(Position Position, int Shirt, PitchPoint Base);

    private static readonly Dictionary<string, Slot[]> Templates = new()
    {
        ["4-3-3"] = new[]
        {
            new Slot(Position.GK, 1, new PitchPoint(6, 34)),
            new Slot(Position.RB, 2, new PitchPoint(32, 8)),
            new Slot(Position.CB, 5, new PitchPoint(24, 26)),
            new Slot(Position.CB, 4, new PitchPoint(24, 42)),
            new Slot(Position.LB, 3, new PitchPoint(32, 60)),
            new Slot(Position.DM, 6, new PitchPoint(42, 34)),
            new Slot(Position.CM, 8, new PitchPoint(54, 22)),
            new Slot(Position.CM, 10, new PitchPoint(54, 46)),
            new Slot(Position.RW, 7, new PitchPoint(76, 10)),
            new Slot(Position.ST, 9, new PitchPoint(84, 34)),
            new Slot(Position.LW, 11, new PitchPoint(76, 58)),
        },
        ["4-2-3-1"] = new[]
        {
            new Slot(Position.GK, 1, new PitchPoint(6, 34)),
            new Slot(Position.RB, 2, new PitchPoint(32, 8)),
            new Slot(Position.CB, 5, new PitchPoint(24, 26)),
            new Slot(Position.CB, 4, new PitchPoint(24, 42)),
            new Slot(Position.LB, 3, new PitchPoint(32, 60)),
            new Slot(Position.DM, 6, new PitchPoint(42, 26)),
            new Slot(Position.DM, 8, new PitchPoint(42, 42)),
            new Slot(Position.AM, 10, new PitchPoint(64, 34)),
            new Slot(Position.RW, 7, new PitchPoint(74, 12)),
            new Slot(Position.ST, 9, new PitchPoint(86, 34)),
            new Slot(Position.LW, 11, new PitchPoint(74, 56)),
        },
    };

    public static readonly (Position Position, int Shirt)[] Bench =
    {
        (Position.GK, 13),
        (Position.CB, 15),
        (Position.CM, 16),
        (Position.RW, 17),
        (Position.ST, 19),
    };

    public static IReadOnlyList<Slot> Get(string formation) =>
        Templates.TryGetValue(formation, out var slots) ? slots : Templates["4-3-3"];
}
