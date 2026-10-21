using TwelfthVoice.Agents;
using TwelfthVoice.Api.Live;
using TwelfthVoice.Api.Speech;
using TwelfthVoice.Intelligence;

namespace TwelfthVoice.Api.Endpoints;

/// <summary>
/// Audio for the app: the goal call and spoken commentary. The browser never sees the Speech key,
/// and it can only ask for lines the match actually produced (by insight id), not arbitrary text.
/// </summary>
public static class SpeechEndpoints
{
    public static IEndpointRouteBuilder MapSpeechApi(this IEndpointRouteBuilder app)
    {
        var speech = app.MapGroup("/api/matches/{matchId}/speech");

        speech.MapGet("/{insightId}/call", async (string matchId, string insightId, string? language,
                MatchRegistry registry, SpeechService voice, CancellationToken ct) =>
            {
                if (!voice.IsAvailable) return Unavailable();
                if (registry.Get(matchId) is not { } session) return Results.NotFound();
                var goal = session.Insights().FirstOrDefault(i => i.Id == insightId && i.Kind == InsightKind.Goal);
                if (goal is null) return Results.NotFound(new { error = "No goal with that id in this match." });

                var info = session.Info;
                string scorer = goal.PlayerIds.Count > 0 ? info.FindPlayer(goal.PlayerIds[0])?.Name ?? "" : "";
                int home = (int)goal.Evidence.GetValueOrDefault("home_score");
                int away = (int)goal.Evidence.GetValueOrDefault("away_score");

                string ssml = SpeechService.GoalCallSsml(language, scorer, info.Home.ShortName, home, info.Away.ShortName, away);
                var audio = await voice.SynthesizeAsync(ssml, ct)
                    // If the voice rejects the held word, a plainer call still goes out in the same voice.
                    ?? await voice.SynthesizeAsync(SpeechService.GoalCallSsml(language, scorer, info.Home.ShortName, home, info.Away.ShortName, away, simple: true), ct);
                return Audio(audio);
            })
            .WithName("GetGoalCall")
            .WithSummary("The goal call for a goal in this match, as MP3, in the viewer's language.");

        speech.MapGet("/{insightId}", async (string matchId, string insightId, string? mode, string? language,
                string? favoriteTeamId, string? focusPlayerId, MatchRegistry registry, BroadcastCrew crew,
                SpeechService voice, CancellationToken ct) =>
            {
                if (!voice.IsAvailable) return Unavailable();
                if (registry.Get(matchId) is not { } session) return Results.NotFound();
                var insight = session.Insights().FirstOrDefault(i => i.Id == insightId);
                if (insight is null) return Results.NotFound(new { error = "No moment with that id in this match." });

                var profile = new ViewerProfile
                {
                    Mode = string.Equals(mode, "analyst", StringComparison.OrdinalIgnoreCase) ? ViewerMode.Analyst : ViewerMode.Casual,
                    FavoriteTeamId = favoriteTeamId?.ToUpperInvariant(),
                    FocusPlayerId = focusPlayerId?.ToUpperInvariant(),
                    Language = string.IsNullOrWhiteSpace(language) ? "en" : language
                };

                // Speak exactly what this viewer sees: the crew's fact-checked line if there is one.
                var line = crew.ApplyCached(new[] { InsightPersonalizer.Personalize(profile, insight) }, profile)[0];
                string text = line.Headline.EndsWith('.') || line.Headline.EndsWith('!') ? $"{line.Headline} {line.Body}" : $"{line.Headline}. {line.Body}";
                var ssml = voice.LineSsml(line.Language, text);
                return ssml is null ? Results.NoContent() : Audio(await voice.SynthesizeAsync(ssml, ct));
            })
            .WithName("GetSpokenLine")
            .WithSummary("A moment read aloud as MP3, exactly as this viewer sees it (crew line and language included).");

        return app;
    }

    private static IResult Audio(byte[]? audio) =>
        audio is null
            ? Results.Problem("The voice couldn't be generated just now.", statusCode: StatusCodes.Status502BadGateway)
            : Results.File(audio, "audio/mpeg");

    private static IResult Unavailable() =>
        Results.Problem("Azure AI Speech isn't configured on this server.", statusCode: StatusCodes.Status503ServiceUnavailable);
}
