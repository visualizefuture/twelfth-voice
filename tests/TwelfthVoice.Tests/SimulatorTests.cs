using System.Text.Json;
using TwelfthVoice.Domain;
using TwelfthVoice.Simulator;

namespace TwelfthVoice.Tests;

public class SimulatorTests
{
    private static SimulatedMatch Sim(int seed = 7) =>
        MatchSimulator.Run(new SimulationOptions { Seed = seed, HomeTeamId = "HAR", AwayTeamId = "IRV" });

    [Fact]
    public void Same_seed_produces_identical_match()
    {
        var a = Sim(11);
        var b = Sim(11);

        Assert.Equal(a.Events.Count, b.Events.Count);
        Assert.Equal(
            JsonSerializer.Serialize(a.Events, MatchJson.Compact),
            JsonSerializer.Serialize(b.Events, MatchJson.Compact));
    }

    [Fact]
    public void Different_seeds_produce_different_matches()
    {
        var a = Sim(1);
        var b = Sim(2);
        Assert.NotEqual(
            JsonSerializer.Serialize(a.Events, MatchJson.Compact),
            JsonSerializer.Serialize(b.Events, MatchJson.Compact));
    }

    [Fact]
    public void Events_are_ordered_and_time_never_goes_backwards_within_a_period()
    {
        var match = Sim();
        for (int i = 1; i < match.Events.Count; i++)
        {
            var previous = match.Events[i - 1];
            var current = match.Events[i];
            Assert.True(current.Sequence > previous.Sequence);
            if (current.Period == previous.Period)
                Assert.True(current.MatchSecond >= previous.MatchSecond,
                    $"Time went backwards at sequence {current.Sequence}: {previous.MatchSecond} -> {current.MatchSecond}");
        }
    }

    [Fact]
    public void Match_has_the_expected_structure()
    {
        var match = Sim();
        var events = match.Events;

        Assert.Equal(EventType.MatchStart, events[0].Type);
        Assert.Equal(EventType.MatchEnd, events[^1].Type);
        Assert.Equal(2, events.Count(e => e.Type == EventType.PeriodStart));
        Assert.Equal(2, events.Count(e => e.Type == EventType.PeriodEnd));

        var end = events[^1];
        Assert.Equal(events.Count(e => e.Type == EventType.Goal && e.TeamId == "HAR"), end.HomeScore);
        Assert.Equal(events.Count(e => e.Type == EventType.Goal && e.TeamId == "IRV"), end.AwayScore);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(21)]
    [InlineData(99)]
    public void Volumes_are_football_realistic(int seed)
    {
        var events = Sim(seed).Events;

        int passes = events.Count(e => e.Type == EventType.Pass);
        int completed = events.Count(e => e.Type == EventType.Pass && e.Outcome == EventOutcome.Success);
        int shots = events.Count(e => e.Type == EventType.Shot);
        int goals = events.Count(e => e.Type == EventType.Goal);
        double lastSecond = events.Max(e => e.MatchSecond);

        Assert.InRange(passes, 450, 1500);
        Assert.InRange((double)completed / passes, 0.65, 0.95);
        Assert.InRange(shots, 6, 50);
        Assert.InRange(goals, 0, 12);
        Assert.InRange(lastSecond, 5400, 5400 + 8 * 60);
    }

    [Fact]
    public void Every_player_reference_points_to_a_real_squad_member()
    {
        var match = Sim();
        var ids = match.Info.AllPlayers.Select(p => p.Id).ToHashSet();

        foreach (var e in match.Events)
        {
            if (e.PlayerId is not null) Assert.True(ids.Contains(e.PlayerId), $"Unknown player {e.PlayerId}");
            if (e.RelatedPlayerId is not null) Assert.True(ids.Contains(e.RelatedPlayerId), $"Unknown player {e.RelatedPlayerId}");
        }
    }

    [Fact]
    public void Coordinates_stay_on_the_pitch()
    {
        foreach (var e in Sim().Events)
        {
            foreach (var point in new[] { e.Start, e.End })
            {
                if (point is not { } p) continue;
                Assert.InRange(p.X, 0, PitchPoint.Length);
                Assert.InRange(p.Y, 0, PitchPoint.Width);
            }
        }
    }

    [Fact]
    public void Squads_are_stable_across_matches()
    {
        var first = SyntheticLeague.BuildTeam("HAR");
        var second = SyntheticLeague.BuildTeam("HAR");
        Assert.Equal(first.Squad.Select(p => p.Name), second.Squad.Select(p => p.Name));
        Assert.Equal(16, first.Squad.Count);
        Assert.Equal(first.Squad.Count, first.Squad.Select(p => p.Id).Distinct().Count());
    }

    [Fact]
    public void Events_round_trip_through_json()
    {
        var events = Sim().Events;
        string json = JsonSerializer.Serialize(events, MatchJson.Compact);
        var back = JsonSerializer.Deserialize<List<MatchEvent>>(json, MatchJson.Compact);

        Assert.NotNull(back);
        Assert.Equal(events.Count, back!.Count);
        Assert.Equal(JsonSerializer.Serialize(events, MatchJson.Compact), JsonSerializer.Serialize(back, MatchJson.Compact));
    }
}
