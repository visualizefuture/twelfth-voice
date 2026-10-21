namespace TwelfthVoice.Domain;

public enum KitPattern
{
    Plain,
    Stripes,
    Hoops,
    Halves,
    Sash,
    Sleeves
}

/// <summary>A strip: shirt colours and pattern, shorts and socks. Colours are #RRGGBB.</summary>
public sealed record Kit
{
    public required string Shirt { get; init; }
    /// <summary>Second shirt colour: stripes, hoops, the other half, the sash or the sleeves.</summary>
    public required string Trim { get; init; }
    public KitPattern Pattern { get; init; } = KitPattern.Plain;
    public required string Shorts { get; init; }
    public required string Socks { get; init; }
    /// <summary>Colour of the shirt number and name.</summary>
    public required string Number { get; init; }
}

public enum CrestShape
{
    Shield,
    Roundel,
    Pennant,
    Badge
}

/// <summary>An original club crest, drawn by the app from these parts.</summary>
public sealed record Crest
{
    public CrestShape Shape { get; init; } = CrestShape.Shield;
    /// <summary>Emblem drawn in the middle: anchor, crown, anvil, stag, oak, lantern.</summary>
    public required string Emblem { get; init; }
    public required string Field { get; init; }
    public required string Accent { get; init; }
    public int Founded { get; init; }
    public string Motto { get; init; } = "";
}

/// <summary>How a player's illustrated portrait is drawn. Generated, never a real person.</summary>
public sealed record PlayerLook
{
    public string SkinTone { get; init; } = "#C68B59";
    /// <summary>short, buzz, curly, afro, long, bun, bald, fringe</summary>
    public string HairStyle { get; init; } = "short";
    public string HairColor { get; init; } = "#2B1B10";
    /// <summary>none, stubble, beard, moustache</summary>
    public string FacialHair { get; init; } = "none";
}

/// <summary>Where a starting player lines up, in the team's attacking frame.</summary>
public sealed record LineupSlot(string PlayerId, Position Position, PitchPoint Base);
