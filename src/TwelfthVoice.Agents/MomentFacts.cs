using TwelfthVoice.Domain;
using TwelfthVoice.Intelligence;

namespace TwelfthVoice.Agents;

/// <summary>
/// The verified facts about one moment, as handed to the Commentator and Fact-checker, plus
/// every number the line is allowed to contain.
/// </summary>
public sealed record MomentFacts(object Payload, IReadOnlyList<double> Numbers)
{
    public static MomentFacts Build(Insight insight, MomentContext context, CommentaryVariant variant)
    {
        var info = context.Info;
        var players = insight.PlayerIds
            .Select(info.FindPlayer)
            .Where(p => p is not null)
            .Select(p => p!)
            .ToList();

        string? teamName = insight.TeamId is null ? null : info.TeamById(insight.TeamId).Name;
        string? favourite = variant.Perspective switch
        {
            Perspective.Favorite => teamName,
            Perspective.Opponent when insight.TeamId is not null => info.Opponent(insight.TeamId).Name,
            _ => null
        };
        string minute = MatchClock.MinuteLabel(insight.Period, insight.MatchSecond);

        var payload = new
        {
            moment = new
            {
                kind = insight.Kind.ToString(),
                minute,
                period = insight.Period == 1 ? "first half" : "second half",
                team = teamName,
                players = players.Select(p => new
                {
                    name = p.Name,
                    shortName = p.ShortName,
                    shirt = p.ShirtNumber,
                    position = p.Position.ToString(),
                    team = info.TeamById(p.Id[..3]).Name
                }),
                whatTheDataSays = insight.Headline,
                whyItMatters = insight.WhyItMatters,
                numbers = insight.AnalystNote,
                evidence = insight.Evidence
            },
            match = new
            {
                home = info.Home.Name,
                away = info.Away.Name,
                scoreNow = $"{info.Home.ShortName} {context.HomeScore}-{context.AwayScore} {info.Away.ShortName}",
                venue = info.Venue
            },
            viewer = new
            {
                mode = variant.Mode.ToString(),
                perspective = variant.Perspective.ToString(),
                supports = favourite
            }
        };

        var numbers = new List<double> { context.HomeScore, context.AwayScore, insight.Period };
        numbers.AddRange(insight.Evidence.Values);
        numbers.AddRange(players.Select(p => (double)p.ShirtNumber));
        numbers.AddRange(NumberGuard.Extract(minute));
        numbers.AddRange(NumberGuard.Extract(insight.Clock));
        numbers.AddRange(NumberGuard.Extract(insight.Headline));
        numbers.AddRange(NumberGuard.Extract(insight.WhyItMatters));
        numbers.AddRange(NumberGuard.Extract(insight.AnalystNote));

        return new MomentFacts(payload, numbers);
    }
}
