using TwelfthVoice.Agents;
using TwelfthVoice.Api.Live;
using TwelfthVoice.Api.Storage;
using TwelfthVoice.Domain;
using TwelfthVoice.Intelligence;
using TwelfthVoice.Simulator;

namespace TwelfthVoice.Api.Endpoints;

public static class ApiEndpoints
{
    public static IEndpointRouteBuilder MapTwelfthVoiceApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        // ---------------------------------------------------------------- league
        api.MapGet("/teams", () => SyntheticLeague.BuildAll())
            .WithName("ListTeams")
            .WithSummary("Fictional clubs with styles and squads.");

        // ---------------------------------------------------------------- matches
        var matches = api.MapGroup("/matches");

        matches.MapPost("", (CreateMatchRequest request, MatchRegistry registry) =>
            {
                try
                {
                    var session = registry.Create(request);
                    return Results.Created($"/api/matches/{session.MatchId}", session.Summary());
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new { error = ex.Message });
                }
            })
            .WithName("CreateMatch")
            .WithSummary("Simulate a match and (by default) start streaming it live.");

        matches.MapGet("", async (MatchRegistry registry, IMatchStore store, CancellationToken ct) => Results.Ok(new
            {
                live = registry.All.Select(s => s.Summary()),
                stored = await store.RecentMatchesAsync(20, ct)
            }))
            .WithName("ListMatches")
            .WithSummary("Matches running in this instance, plus recent matches from storage.");

        matches.MapGet("/{matchId}", (string matchId, MatchRegistry registry) =>
                WithSession(registry, matchId, s => Results.Ok(new { summary = s.Summary(), info = s.Info })))
            .WithName("GetMatch");

        matches.MapPost("/{matchId}/start", (string matchId, MatchRegistry registry, IHostApplicationLifetime lifetime) =>
                WithSession(registry, matchId, s =>
                {
                    s.Start(lifetime.ApplicationStopping);
                    return Results.Ok(s.Summary());
                }))
            .WithName("StartMatch");

        matches.MapPost("/{matchId}/pause", (string matchId, MatchRegistry registry) =>
                WithSession(registry, matchId, s =>
                {
                    s.Pause();
                    return Results.Ok(s.Summary());
                }))
            .WithName("PauseMatch");

        matches.MapPost("/{matchId}/resume", (string matchId, MatchRegistry registry) =>
                WithSession(registry, matchId, s =>
                {
                    s.Resume();
                    return Results.Ok(s.Summary());
                }))
            .WithName("ResumeMatch");

        matches.MapPut("/{matchId}/speed", (string matchId, SpeedRequest body, MatchRegistry registry) =>
                WithSession(registry, matchId, s =>
                {
                    s.SetSpeed(body.Speed);
                    return Results.Ok(s.Summary());
                }))
            .WithName("SetMatchSpeed")
            .WithSummary("Match seconds per real second (0.5 – 10000).");

        matches.MapGet("/{matchId}/state", (string matchId, string? focusPlayerId, MatchRegistry registry) =>
                WithSession(registry, matchId, s => Results.Ok(s.State(focusPlayerId))))
            .WithName("GetMatchState")
            .WithSummary("Scoreboard and game-state strip (momentum, control, tempo).");

        matches.MapGet("/{matchId}/snapshot", (string matchId, MatchRegistry registry) =>
                WithSession(registry, matchId, s => Results.Ok(s.Snapshot())))
            .WithName("GetMatchSnapshot")
            .WithSummary("Full team and player statistics so far.");

        matches.MapGet("/{matchId}/insights",
                (string matchId, string? mode, string? favoriteTeamId, string? focusPlayerId, string? language, int? take, MatchRegistry registry, BroadcastCrew crew) =>
                    WithSession(registry, matchId, s =>
                    {
                        var profile = new ViewerProfile
                        {
                            Mode = string.Equals(mode, "analyst", StringComparison.OrdinalIgnoreCase) ? ViewerMode.Analyst : ViewerMode.Casual,
                            FavoriteTeamId = favoriteTeamId?.ToUpperInvariant(),
                            FocusPlayerId = focusPlayerId?.ToUpperInvariant(),
                            Language = string.IsNullOrWhiteSpace(language) ? "en" : language
                        };
                        var insights = crew.ApplyCached(InsightPersonalizer.ForViewer(profile, s.Insights()), profile);
                        int count = Math.Clamp(take ?? 50, 1, 500);
                        return Results.Ok(insights.Skip(Math.Max(0, insights.Count - count)));
                    }))
            .WithName("GetInsights")
            .WithSummary("Insights so far, personalised for the given viewer settings.");

        matches.MapGet("/{matchId}/events", (string matchId, int? after, string? type, int? take, MatchRegistry registry) =>
                WithSession(registry, matchId, s =>
                {
                    int count = Math.Clamp(take ?? 200, 1, 2000);
                    IEnumerable<MatchEvent> events = s.PublishedEvents(after ?? 0, int.MaxValue);
                    if (!string.IsNullOrWhiteSpace(type) && Enum.TryParse<EventType>(type, ignoreCase: true, out var eventType))
                        events = events.Where(e => e.Type == eventType);
                    return Results.Ok(events.Take(count));
                }))
            .WithName("GetEvents")
            .WithSummary("Events released so far (use 'after' to page by sequence).");

        matches.MapGet("/{matchId}/timeline", (string matchId, MatchRegistry registry) =>
                WithSession(registry, matchId, s => Results.Ok(s.Timeline())))
            .WithName("GetTimeline")
            .WithSummary("Momentum, control, xG and score sampled every ~30 match seconds.");

        matches.MapGet("/{matchId}/recap", (string matchId, MatchRegistry registry) =>
                WithSession(registry, matchId, s => Results.Ok(s.Recap())))
            .WithName("GetRecap")
            .WithSummary("Automated recap (final once the match has finished).");

        matches.MapGet("/{matchId}/story", async (string matchId, string? mode, string? language, MatchRegistry registry, MatchAnalyst analyst, CancellationToken ct) =>
            {
                if (registry.Get(matchId) is not { } session) return Results.NotFound(new { error = $"Match '{matchId}' was not found." });
                var viewerMode = string.Equals(mode, "analyst", StringComparison.OrdinalIgnoreCase) ? ViewerMode.Analyst : ViewerMode.Casual;
                return Results.Ok(await analyst.StoryAsync(session.Recap(), viewerMode, language, ct));
            })
            .WithName("GetStory")
            .WithSummary("The match story written by the Storyteller agent from the verified recap, fact-checked and translated.");

        matches.MapGet("/{matchId}/crew", (string matchId, int? take, MatchRegistry registry, BroadcastCrew crew) =>
                WithSession(registry, matchId, s => Results.Ok(crew.RecentActivity(s.MatchId, take ?? 40))))
            .WithName("GetCrewRoom")
            .WithSummary("The crew room: each moment the crew worked on, every draft, the fact-checker's verdicts and what went on air.");

        matches.MapPost("/{matchId}/ask", async (string matchId, AskRequest request, MatchRegistry registry, MatchAnalyst analyst, CancellationToken ct) =>
            {
                if (registry.Get(matchId) is not { } session) return Results.NotFound(new { error = $"Match '{matchId}' was not found." });
                if (string.IsNullOrWhiteSpace(request.Question)) return Results.BadRequest(new { error = "Ask a question about the match." });
                return Results.Ok(await analyst.AskAsync(session, request, ct));
            })
            .WithName("AskTheMatch")
            .WithSummary("Ask the Analyst agent a question; it looks up the live data with tools and its numbers are checked.");

        // ---------------------------------------------------------------- viewer profiles
        var profiles = api.MapGroup("/profiles");

        profiles.MapPost("", async (ViewerProfile profile, IMatchStore store, CancellationToken ct) =>
            {
                var stored = await store.SaveProfileAsync(null, profile, ct);
                return Results.Created($"/api/profiles/{stored.Id}", stored);
            })
            .WithName("CreateProfile");

        profiles.MapPut("/{id:guid}", async (Guid id, ViewerProfile profile, IMatchStore store, CancellationToken ct) =>
                Results.Ok(await store.SaveProfileAsync(id, profile, ct)))
            .WithName("SaveProfile");

        profiles.MapGet("/{id:guid}", async (Guid id, IMatchStore store, CancellationToken ct) =>
                await store.GetProfileAsync(id, ct) is { } stored ? Results.Ok(stored) : Results.NotFound())
            .WithName("GetProfile");

        // ---------------------------------------------------------------- status
        api.MapGet("/status", (MatchRegistry registry, IMatchStore store, StorageState storage, BroadcastCrew crew, Speech.SpeechService speech) => Results.Ok(new
            {
                service = "Twelfth Voice",
                storage = store.Mode,
                crew = new { usage = crew.Usage, stats = crew.Stats },
                speech = speech.Usage,
                storageError = storage.LastError,
                liveMatches = registry.All.Count(s => s.Status is MatchStatus.Live or MatchStatus.Paused)
            }))
            .WithName("GetStatus");

        return app;
    }

    private static IResult WithSession(MatchRegistry registry, string matchId, Func<MatchSession, IResult> action) =>
        registry.Get(matchId) is { } session
            ? action(session)
            : Results.NotFound(new { error = $"Match '{matchId}' was not found." });
}
