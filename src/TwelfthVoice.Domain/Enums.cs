namespace TwelfthVoice.Domain;

public enum EventType
{
    MatchStart,
    PeriodStart,
    PeriodEnd,
    MatchEnd,
    KickOff,
    Pass,
    Carry,
    Dribble,
    Shot,
    Goal,
    Save,
    Block,
    Tackle,
    Interception,
    Clearance,
    Pressure,
    Foul,
    YellowCard,
    RedCard,
    FreeKick,
    Corner,
    ThrowIn,
    GoalKick,
    Offside,
    Substitution,
    Sprint,
    PossessionChange,
    TrackingSnapshot
}

public enum EventOutcome
{
    Success,
    Fail,
    Goal,
    Saved,
    OffTarget,
    Blocked
}

public enum Position
{
    GK,
    RB,
    CB,
    LB,
    DM,
    CM,
    AM,
    RW,
    LW,
    ST
}

public enum BodyPart
{
    RightFoot,
    LeftFoot,
    Head
}
