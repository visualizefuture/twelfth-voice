using TwelfthVoice.Domain;
using TwelfthVoice.Intelligence;
using TwelfthVoice.Simulator;

namespace TwelfthVoice.Tests;

public class IntelligenceTests
{
    private static (SimulatedMatch Match, MatchAnalyzer Analyzer) Analyse(int seed = 7)
    {
        var match = MatchSimulator.Run(new SimulationOptions { Seed = seed, HomeTeamId = "HAR", AwayTeamId = "IRV" });
        var analyzer = new MatchAnalyzer(match.Info);
        analyzer.ProcessAll(match.Events);
        return (match, analyzer);
    }

    [Fact]
    public void Team_stats_match_the_raw_events()
    {
        var (match, analyzer) = Analyse();
        var snapshot = analyzer.Snapshot();

        Assert.Equal(match.Events.Count(e => e.Type == EventType.Pass && e.TeamId == "HAR"), snapshot.Home.PassesAttempted);
        Assert.Equal(match.Events.Count(e => e.Type == EventType.Shot && e.TeamId == "IRV"), snapshot.Away.Shots);
        Assert.Equal(match.Events[^1].HomeScore, snapshot.HomeScore);
        Assert.Equal(match.Events[^1].AwayScore, snapshot.AwayScore);
    }

    [Fact]
    public void Possession_adds_up_to_one_hundred()
    {
        var (_, analyzer) = Analyse(5);
        var snapshot = analyzer.Snapshot();
        Assert.Equal(100, snapshot.Home.PossessionPct + snapshot.Away.PossessionPct, 1);
        Assert.InRange(snapshot.Home.PossessionPct, 20, 80);
    }

    [Fact]
    public void Every_goal_produces_an_explained_insight()
    {
        for (int seed = 1; seed <= 10; seed++)
        {
            var (match, analyzer) = Analyse(seed);
            int goals = match.Events.Count(e => e.Type == EventType.Goal);
            var goalInsights = analyzer.Insights.Where(i => i.Kind == InsightKind.Goal).ToList();

            Assert.Equal(goals, goalInsights.Count);
            Assert.All(goalInsights, i =>
            {
                Assert.False(string.IsNullOrWhiteSpace(i.WhyItMatters));
                Assert.True(i.Evidence.ContainsKey("xg"));
                Assert.Equal("goal-banner", i.Overlay.Template);
            });
        }
    }

    [Fact]
    public void Half_time_and_full_time_summaries_are_produced()
    {
        var (_, analyzer) = Analyse();
        Assert.Single(analyzer.Insights, i => i.Kind == InsightKind.HalfTime);
        Assert.Single(analyzer.Insights, i => i.Kind == InsightKind.FullTime);
    }

    [Fact]
    public void Trend_insights_appear_over_a_full_match()
    {
        var trendKinds = new[] { InsightKind.MomentumShift, InsightKind.ControlShift, InsightKind.PressingShift, InsightKind.TempoShift };
        int total = 0;
        for (int seed = 1; seed <= 5; seed++)
        {
            var (_, analyzer) = Analyse(seed);
            total += analyzer.Insights.Count(i => trendKinds.Contains(i.Kind));
        }
        Assert.True(total > 0, "Expected at least one momentum / control / pressing / tempo insight across five matches.");
    }

    [Fact]
    public void Insights_have_unique_ids_and_valid_importance()
    {
        var (_, analyzer) = Analyse();
        var insights = analyzer.Insights;
        Assert.Equal(insights.Count, insights.Select(i => i.Id).Distinct().Count());
        Assert.All(insights, i => Assert.InRange(i.Importance, 0, 1));
    }

    [Fact]
    public void Recap_is_consistent_with_the_score()
    {
        var (match, analyzer) = Analyse(13);
        var final = analyzer.Snapshot();
        var recap = RecapBuilder.Build(match.Info, final, analyzer.Insights);

        Assert.False(string.IsNullOrWhiteSpace(recap.Headline));
        Assert.Contains($"{final.HomeScore}–{final.AwayScore}", recap.Summary);
        Assert.NotEmpty(recap.StatLines);
        Assert.True(recap.KeyMoments.Count <= 6);
    }
}
