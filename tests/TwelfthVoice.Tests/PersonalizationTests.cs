using TwelfthVoice.Intelligence;

namespace TwelfthVoice.Tests;

public class PersonalizationTests
{
    private static Insight Make(InsightKind kind, double importance, string? teamId = null, params string[] players) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        MatchId = "TEST",
        Kind = kind,
        Period = 1,
        MatchSecond = 600,
        Importance = importance,
        Headline = "Headline",
        WhyItMatters = "First sentence. Second sentence with numbers.",
        AnalystNote = "xG 0.31",
        TeamId = teamId,
        PlayerIds = players,
        Overlay = new OverlayHint("stat-card", "lower-third", 5, 30)
    };

    [Fact]
    public void Everyone_sees_goals()
    {
        var goal = Make(InsightKind.Goal, 1.0, "HAR", "HAR-09");
        Assert.True(InsightPersonalizer.Wants(new ViewerProfile(), goal));
        Assert.True(InsightPersonalizer.Wants(new ViewerProfile { FocusPlayerId = "IRV-10" }, goal));
    }

    [Fact]
    public void Casual_fans_skip_minor_moments_that_analysts_see()
    {
        var minor = Make(InsightKind.TempoShift, 0.3);
        Assert.False(InsightPersonalizer.Wants(new ViewerProfile { Mode = ViewerMode.Casual }, minor));
        Assert.True(InsightPersonalizer.Wants(new ViewerProfile { Mode = ViewerMode.Analyst }, minor));
    }

    [Fact]
    public void Favourite_team_moments_get_a_lower_bar()
    {
        var pressing = Make(InsightKind.PressingShift, 0.35, "HAR");
        Assert.False(InsightPersonalizer.Wants(new ViewerProfile(), pressing));
        Assert.True(InsightPersonalizer.Wants(new ViewerProfile { FavoriteTeamId = "HAR" }, pressing));
    }

    [Fact]
    public void Player_focus_shows_small_moments_for_that_player_only()
    {
        var sprint = Make(InsightKind.SpeedAlert, 0.35, "HAR", "HAR-09");
        var otherSprint = Make(InsightKind.SpeedAlert, 0.35, "HAR", "HAR-07");
        var profile = new ViewerProfile { FocusPlayerId = "HAR-09" };

        Assert.True(InsightPersonalizer.Wants(profile, sprint));
        Assert.False(InsightPersonalizer.Wants(profile, otherSprint));

        var personalized = InsightPersonalizer.Personalize(profile, sprint);
        Assert.True(personalized.AboutFocusPlayer);
        Assert.True(personalized.Relevance > sprint.Importance);
        Assert.True(personalized.Overlay.Priority > sprint.Overlay.Priority);
    }

    [Fact]
    public void Casual_body_is_short_and_analyst_body_is_full()
    {
        var insight = Make(InsightKind.BigChance, 0.7, "HAR", "HAR-09");

        var casual = InsightPersonalizer.Personalize(new ViewerProfile(), insight);
        var analyst = InsightPersonalizer.Personalize(new ViewerProfile { Mode = ViewerMode.Analyst }, insight);

        Assert.Equal("First sentence.", casual.Body);
        Assert.Null(casual.AnalystNote);
        Assert.Equal(insight.WhyItMatters, analyst.Body);
        Assert.Equal("xG 0.31", analyst.AnalystNote);
    }

    [Fact]
    public void Perspective_reflects_the_favourite_team()
    {
        var insight = Make(InsightKind.BigChance, 0.7, "IRV");
        Assert.Equal(Perspective.Opponent, InsightPersonalizer.Personalize(new ViewerProfile { FavoriteTeamId = "HAR" }, insight).Perspective);
        Assert.Equal(Perspective.Favorite, InsightPersonalizer.Personalize(new ViewerProfile { FavoriteTeamId = "IRV" }, insight).Perspective);
        Assert.Equal(Perspective.Neutral, InsightPersonalizer.Personalize(new ViewerProfile(), insight).Perspective);
    }
}
