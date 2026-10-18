using TwelfthVoice.Domain;

namespace TwelfthVoice.Tests;

public class FootballMathTests
{
    [Fact]
    public void Closer_central_shots_have_higher_xg()
    {
        double sixYards = FootballMath.ExpectedGoals(new PitchPoint(99, 34), BodyPart.RightFoot, false);
        double penaltySpot = FootballMath.ExpectedGoals(new PitchPoint(94, 34), BodyPart.RightFoot, false);
        double longRange = FootballMath.ExpectedGoals(new PitchPoint(80, 34), BodyPart.RightFoot, false);
        double tightAngle = FootballMath.ExpectedGoals(new PitchPoint(100, 6), BodyPart.RightFoot, false);

        Assert.True(sixYards > penaltySpot);
        Assert.True(penaltySpot > longRange);
        Assert.True(penaltySpot > tightAngle);
        Assert.InRange(longRange, 0.01, 0.08);
    }

    [Fact]
    public void Headers_are_harder_to_score_than_shots_with_the_foot()
    {
        var spot = new PitchPoint(96, 34);
        Assert.True(FootballMath.ExpectedGoals(spot, BodyPart.Head, false) < FootballMath.ExpectedGoals(spot, BodyPart.RightFoot, false));
    }

    [Fact]
    public void Pass_difficulty_rises_with_length_pressure_and_target()
    {
        var start = new PitchPoint(50, 34);
        double shortSideways = FootballMath.PassDifficulty(start, new PitchPoint(50, 44), false, false, false);
        double longForward = FootballMath.PassDifficulty(start, new PitchPoint(85, 34), false, false, false);
        double throughUnderPressure = FootballMath.PassDifficulty(start, new PitchPoint(95, 34), true, true, false);

        Assert.True(shortSideways < longForward);
        Assert.True(longForward < throughUnderPressure);
        Assert.InRange(throughUnderPressure, 0, 10);
    }

    [Fact]
    public void Pitch_mirror_is_an_involution()
    {
        var p = new PitchPoint(12.5, 60);
        Assert.Equal(p, p.Mirror().Mirror());
        Assert.Equal(new PitchPoint(92.5, 8), p.Mirror());
    }

    [Theory]
    [InlineData(1, 0, "00:00")]
    [InlineData(1, 1385, "23:05")]
    [InlineData(1, 2790, "45+01:30")]
    [InlineData(2, 4032, "67:12")]
    [InlineData(2, 5592, "90+03:12")]
    public void Clock_formats_like_a_broadcast(int period, double second, string expected)
    {
        Assert.Equal(expected, MatchClock.Format(period, second));
    }

    [Theory]
    [InlineData(1, 0, "1'")]
    [InlineData(1, 2790, "45+2'")]
    [InlineData(2, 4032, "68'")]
    public void Minute_label_matches_scoreboard_convention(int period, double second, string expected)
    {
        Assert.Equal(expected, MatchClock.MinuteLabel(period, second));
    }
}
