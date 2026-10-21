using System.Collections.Concurrent;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using TwelfthVoice.Agents;
using TwelfthVoice.Domain;
using TwelfthVoice.Intelligence;
using TwelfthVoice.Simulator;

namespace TwelfthVoice.Tests;

public class NumberGuardTests
{
    [Fact]
    public void Extracts_numbers_in_any_script_and_decimal_style()
    {
        Assert.Equal(new[] { 0.31, 104 }, NumberGuard.Extract("A 0.31 xG chance, struck at 104 km/h"));
        Assert.Equal(new[] { 0.31 }, NumberGuard.Extract("una ocasión de 0,31 xG"));
        Assert.Equal(new[] { 23.0 }, NumberGuard.Extract("في الدقيقة ٢٣"));
        Assert.Equal(new[] { 2.0, 1.0 }, NumberGuard.Extract("Harbourside 2–1"));
        Assert.Empty(NumberGuard.Extract("No numbers here."));
    }

    [Fact]
    public void Accepts_rounded_percent_and_kilometre_forms_of_facts()
    {
        var allowed = NumberGuard.AllowedForms(new[] { 0.3127, 10432.0, 24.6 });
        Assert.True(NumberGuard.Check("A 0.31 xG chance, about 31%.", allowed).Passed);
        Assert.True(NumberGuard.Check("He has run 10.4 km.", allowed).Passed);
        Assert.True(NumberGuard.Check("A 25 m pass.", allowed).Passed);
    }

    [Fact]
    public void Rejects_invented_numbers()
    {
        var allowed = NumberGuard.AllowedForms(new[] { 0.31, 104.0 });
        var result = NumberGuard.Check("That was his 7th goal at 118 km/h.", allowed);
        Assert.False(result.Passed);
        Assert.Equal(2, result.Issues.Count);
    }

    [Fact]
    public void Translation_must_keep_the_same_numbers()
    {
        Assert.True(NumberGuard.CheckPreserved("A 0.31 xG chance at 104 km/h", "Una ocasión de 0,31 xG a 104 km/h").Passed);
        Assert.False(NumberGuard.CheckPreserved("A 0.31 xG chance at 104 km/h", "Una ocasión de 0,13 xG a 104 km/h").Passed);
        Assert.False(NumberGuard.CheckPreserved("Score 2-1", "Marcador 2-1, 3 tiros").Passed);
    }
}

public class AgentOptionsTests
{
    [Theory]
    [InlineData("https://tv.services.ai.azure.com/openai/v1/responses", "https://tv.services.ai.azure.com/openai/v1/")]
    [InlineData("https://tv.services.ai.azure.com/openai/v1/chat/completions", "https://tv.services.ai.azure.com/openai/v1/")]
    [InlineData("https://tv.services.ai.azure.com/openai/v1", "https://tv.services.ai.azure.com/openai/v1/")]
    [InlineData("https://tv.openai.azure.com/", "https://tv.openai.azure.com/openai/v1/")]
    public void Endpoint_is_normalised_to_the_v1_base(string configured, string expected)
    {
        Assert.Equal(expected, new AgentOptions { Endpoint = configured }.BaseUri().ToString());
    }

    [Fact]
    public void Call_budget_caps_calls_per_hour()
    {
        var now = DateTimeOffset.UtcNow;
        var budget = new CallBudget(2, () => now);
        Assert.True(budget.TryAcquire());
        Assert.True(budget.TryAcquire());
        Assert.False(budget.TryAcquire());
        now = now.AddMinutes(61);
        Assert.True(budget.TryAcquire());
    }
}

public class BroadcastCrewTests
{
    /// <summary>A scripted model: each role answers from a queue, so tests can play out good and bad drafts.</summary>
    private sealed class ScriptedModel : ICrewModel
    {
        private readonly ConcurrentDictionary<CrewRole, ConcurrentQueue<string?>> _scripts = new();
        public ConcurrentQueue<(CrewRole Role, string Prompt)> Calls { get; } = new();

        public ScriptedModel Says(CrewRole role, params string?[] replies)
        {
            var queue = _scripts.GetOrAdd(role, _ => new ConcurrentQueue<string?>());
            foreach (var reply in replies) queue.Enqueue(reply);
            return this;
        }

        public bool IsAvailable => true;
        public CrewUsage Usage => new(true, "test", Calls.Count, 0, 0, 0, 100);

        public Task<string?> RunAsync(CrewRole role, string prompt, CancellationToken cancellationToken)
        {
            Calls.Enqueue((role, prompt));
            return Task.FromResult(_scripts.TryGetValue(role, out var q) && q.TryDequeue(out var reply) ? reply : null);
        }

        public Task<string?> RunWithToolsAsync(string name, string instructions, string prompt, IList<AITool> tools, CancellationToken cancellationToken) =>
            Task.FromResult<string?>(null);
    }

    private static readonly MatchInfo Info = MatchSimulator.Run(new SimulationOptions { Seed = 7, HomeTeamId = "HAR", AwayTeamId = "IRV" }).Info;

    private static Insight Goal() => new()
    {
        Id = "TEST-I0001",
        MatchId = Info.MatchId,
        Kind = InsightKind.Goal,
        Period = 1,
        MatchSecond = 1385,
        Importance = 1.0,
        Headline = "GOAL! Test Striker — Harbourside 1–0 Ironvale",
        WhyItMatters = "Harbourside open the scoring. A 0.31 xG chance, scored roughly 31% of the time.",
        AnalystNote = "xG 0.31 · Shot 104 km/h",
        TeamId = "HAR",
        PlayerIds = new[] { "HAR-09" },
        Evidence = new Dictionary<string, double> { ["xg"] = 0.31, ["shot_speed_kmh"] = 104, ["home_score"] = 1, ["away_score"] = 0 },
        Overlay = new OverlayHint("goal-banner", "full-width", 10, 100)
    };

    private static BroadcastCrew Crew(ICrewModel model) =>
        new(model, new AgentOptions { Endpoint = "https://x/openai/v1/" }, NullLogger<BroadcastCrew>.Instance);

    private static readonly MomentContext Context = new(Info, 1, 0);
    private static readonly CommentaryVariant English = new(ViewerMode.Casual, "en", Perspective.Neutral);

    [Fact]
    public async Task Approved_draft_goes_on_air_with_its_audit_trail()
    {
        var model = new ScriptedModel()
            .Says(CrewRole.Commentator, """{"headline":"Harbourside strike first!","body":"A 0.31 xG chance, finished at 104 km/h."}""")
            .Says(CrewRole.FactChecker, """{"verdict":"pass","issues":[]}""");

        var line = await Crew(model).PrepareAsync(Goal(), Context, English);

        Assert.NotNull(line);
        Assert.Equal("ai", line!.Source);
        Assert.True(line.Verified);
        Assert.Equal("Harbourside strike first!", line.Headline);
        Assert.Contains("Every number traced to match data", line.Checks);
        Assert.Contains("Approved by the AI fact-checker", line.Checks);
    }

    [Fact]
    public async Task Invented_number_is_sent_back_and_the_rewrite_is_used()
    {
        var model = new ScriptedModel()
            .Says(CrewRole.Commentator,
                """{"headline":"His 12th of the season!","body":"Another goal."}""",
                """{"headline":"Harbourside lead","body":"A 0.31 xG chance taken."}""")
            .Says(CrewRole.FactChecker, """{"verdict":"pass"}""");

        var line = await Crew(model).PrepareAsync(Goal(), Context, English);

        Assert.Equal("ai", line!.Source);
        Assert.Equal("Harbourside lead", line.Headline);
        Assert.Contains("Rewritten after the fact-checker's notes", line.Checks);
        var retryPrompt = model.Calls.Where(c => c.Role == CrewRole.Commentator).Skip(1).First().Prompt;
        Assert.Contains("12", retryPrompt); // the rejection reason was handed back
    }

    [Fact]
    public async Task Twice_rejected_draft_never_airs()
    {
        var model = new ScriptedModel()
            .Says(CrewRole.Commentator,
                """{"headline":"A 99 km/h rocket","body":"Unbelievable."}""",
                """{"headline":"Goal","body":"From 40 yards out."}""");

        var crew = Crew(model);
        var line = await crew.PrepareAsync(Goal(), Context, English);

        // English rule text is already on screen, so there's nothing to replace.
        Assert.Null(line);
        Assert.Equal(1, crew.Stats.FellBackToRules);
        Assert.Equal(2, crew.Stats.DraftsRejected);
    }

    [Fact]
    public async Task Fact_checker_veto_is_respected()
    {
        var model = new ScriptedModel()
            .Says(CrewRole.Commentator,
                """{"headline":"Ironvale score","body":"A 0.31 xG chance."}""",
                """{"headline":"Harbourside score","body":"A 0.31 xG chance."}""")
            .Says(CrewRole.FactChecker,
                """{"verdict":"fail","issues":["Harbourside scored, not Ironvale."]}""",
                """{"verdict":"pass"}""");

        var line = await Crew(model).PrepareAsync(Goal(), Context, English);

        Assert.Equal("Harbourside score", line!.Headline);
        var retryPrompt = model.Calls.Where(c => c.Role == CrewRole.Commentator).Skip(1).First().Prompt;
        Assert.Contains("Harbourside scored, not Ironvale.", retryPrompt);
    }

    [Fact]
    public async Task Translation_is_checked_and_labelled()
    {
        var model = new ScriptedModel()
            .Says(CrewRole.Commentator, """{"headline":"Harbourside strike first!","body":"A 0.31 xG chance."}""")
            .Says(CrewRole.FactChecker, """{"verdict":"pass"}""")
            .Says(CrewRole.Localizer, """{"headline":"¡Harbourside golpea primero!","body":"Una ocasión de 0,31 xG."}""");

        var line = await Crew(model).PrepareAsync(Goal(), Context, new CommentaryVariant(ViewerMode.Casual, "es", Perspective.Neutral));

        Assert.Equal("es", line!.Language);
        Assert.StartsWith("¡Harbourside", line.Headline);
        Assert.Contains(line.Checks, c => c.StartsWith("Translated to Spanish"));
    }

    [Fact]
    public async Task Translation_that_changes_a_number_is_held_back()
    {
        var model = new ScriptedModel()
            .Says(CrewRole.Commentator, """{"headline":"Harbourside strike first!","body":"A 0.31 xG chance."}""")
            .Says(CrewRole.FactChecker, """{"verdict":"pass"}""")
            .Says(CrewRole.Localizer, """{"headline":"¡Gol!","body":"Una ocasión de 0,13 xG."}""");

        var line = await Crew(model).PrepareAsync(Goal(), Context, new CommentaryVariant(ViewerMode.Casual, "es", Perspective.Neutral));

        Assert.Equal("en", line!.Language);
        Assert.Contains(line.Checks, c => c.Contains("held back"));
    }

    [Fact]
    public async Task Viewers_with_the_same_view_share_one_line()
    {
        var model = new ScriptedModel()
            .Says(CrewRole.Commentator, """{"headline":"Harbourside strike first!","body":"A 0.31 xG chance."}""")
            .Says(CrewRole.FactChecker, """{"verdict":"pass"}""");
        var crew = Crew(model);

        var a = crew.PrepareAsync(Goal(), Context, English);
        var b = crew.PrepareAsync(Goal(), Context, English);
        await Task.WhenAll(a, b);

        Assert.Same(a.Result, b.Result);
        Assert.Equal(1, model.Calls.Count(c => c.Role == CrewRole.Commentator));
        Assert.NotNull(crew.TryGetCached("TEST-I0001", English));
    }

    [Fact]
    public async Task Without_a_model_nothing_changes()
    {
        var crew = new BroadcastCrew(new NullCrewModel(), new AgentOptions(), NullLogger<BroadcastCrew>.Instance);
        Assert.Null(await crew.PrepareAsync(Goal(), Context, English));
        Assert.False(crew.ShouldHandle(Goal(), English));
    }
}

public class MatchToolsTests
{
    private sealed class FinishedMatch : IMatchData
    {
        private readonly MatchAnalyzer _analyzer;

        public FinishedMatch()
        {
            var match = MatchSimulator.Run(new SimulationOptions { Seed = 7, HomeTeamId = "HAR", AwayTeamId = "IRV" });
            Info = match.Info;
            _analyzer = new MatchAnalyzer(match.Info);
            _analyzer.ProcessAll(match.Events);
        }

        public MatchInfo Info { get; }
        public MatchSnapshot Snapshot() => _analyzer.Snapshot();
        public IReadOnlyList<Insight> Insights() => _analyzer.Insights;
        public IReadOnlyList<MomentumSample> Momentum() => new[] { new MomentumSample(1, 600, "10:00", 40, 70, 0, 0) };
    }

    [Fact]
    public void Tools_answer_from_live_data_and_record_what_they_returned()
    {
        var tools = new MatchTools(new FinishedMatch());

        Assert.Contains("Harbourside Rovers", tools.GetTeamStats("HAR"));
        Assert.Contains("\"top\"", tools.GetTopPlayers("rating"));
        Assert.Contains("moments", tools.GetKeyMoments(60, 90));
        Assert.Contains("error", tools.GetPlayerStats("Nobody McNoname"));
        Assert.Contains("\"momentum\":40", tools.GetMomentum());
        Assert.Equal(5, tools.Outputs.Count);
        Assert.Equal(6, tools.AsTools().Count);
    }
}

public class CrewRoomTests
{
    private sealed class Script : ICrewModel
    {
        private readonly Queue<string?> _commentator;
        private readonly Queue<string?> _checker;
        public Script(IEnumerable<string?> commentator, IEnumerable<string?> checker)
        {
            _commentator = new Queue<string?>(commentator);
            _checker = new Queue<string?>(checker);
        }
        public bool IsAvailable => true;
        public CrewUsage Usage => new(true, "test", 0, 0, 0, 0, 100);
        public Task<string?> RunAsync(CrewRole role, string prompt, CancellationToken cancellationToken) =>
            Task.FromResult(role switch
            {
                CrewRole.Commentator => _commentator.Count > 0 ? _commentator.Dequeue() : null,
                CrewRole.FactChecker => _checker.Count > 0 ? _checker.Dequeue() : null,
                _ => null
            });
        public Task<string?> RunWithToolsAsync(string name, string instructions, string prompt, IList<Microsoft.Extensions.AI.AITool> tools, CancellationToken cancellationToken) =>
            Task.FromResult<string?>(null);
    }

    [Fact]
    public async Task Crew_room_shows_the_rejected_draft_the_reason_and_the_rewrite()
    {
        // The first seed whose match has a goal.
        var (match, goal) = Enumerable.Range(1, 50)
            .Select(seed =>
            {
                var m = MatchSimulator.Run(new SimulationOptions { Seed = seed, HomeTeamId = "HAR", AwayTeamId = "IRV" });
                var a = new MatchAnalyzer(m.Info);
                a.ProcessAll(m.Events);
                return (m, a.Insights.FirstOrDefault(i => i.Kind == InsightKind.Goal));
            })
            .First(x => x.Item2 is not null);

        var crew = new BroadcastCrew(
            new Script(
                new[] { """{"headline":"A 99th-minute stunner","body":"Unreal."}""", """{"headline":"Goal!","body":"What a finish."}""" },
                new[] { """{"verdict":"pass"}""" }),
            new AgentOptions { Endpoint = "https://x/openai/v1/" },
            Microsoft.Extensions.Logging.Abstractions.NullLogger<BroadcastCrew>.Instance);

        await crew.PrepareAsync(goal!, new MomentContext(match.Info, 1, 0), new CommentaryVariant(ViewerMode.Casual, "en", Perspective.Neutral));

        var room = crew.RecentActivity(match.Info.MatchId);
        var entry = Assert.Single(room);
        Assert.Equal("crew", entry.Outcome);
        Assert.Equal(2, entry.Drafts.Count);
        Assert.False(entry.Drafts[0].Approved);
        Assert.Equal("Number check", entry.Drafts[0].CheckedBy);
        Assert.Contains(entry.Drafts[0].Reasons, r => r.Contains("99"));
        Assert.True(entry.Drafts[1].Approved);
        Assert.Equal("Goal!", entry.FinalHeadline);
        Assert.Equal("Fan, English, neutral", entry.Audience);
    }
}

public class SpeechTests
{
    [Fact]
    public void Goal_call_is_valid_ssml_in_the_viewers_language()
    {
        string ssml = TwelfthVoice.Api.Speech.SpeechService.GoalCallSsml("es-MX", "Álvaro O'Neil <9>", "Harbourside", 1, "Ironvale", 0);
        var doc = System.Xml.Linq.XDocument.Parse(ssml); // throws if escaping is wrong
        Assert.Equal("es-ES", doc.Root!.Attribute(System.Xml.Linq.XNamespace.Xml + "lang")!.Value);
        Assert.Contains("es-ES-AlvaroNeural", ssml);
        Assert.Contains("Gol!", ssml);
        Assert.Contains("Harbourside 1, Ironvale 0", ssml);
        Assert.Contains("&lt;9&gt;", ssml);
    }

    [Fact]
    public void Unknown_languages_fall_back_to_english()
    {
        Assert.Equal("en-GB-RyanNeural", TwelfthVoice.Api.Speech.SpeechService.VoiceFor("xx").Name);
        Assert.Equal("Tor", TwelfthVoice.Api.Speech.SpeechService.VoiceFor("de").GoalWord);
    }

    [Fact]
    public async Task Without_a_key_nothing_is_synthesised()
    {
        var service = new TwelfthVoice.Api.Speech.SpeechService(new TwelfthVoice.Api.Speech.SpeechOptions(), new HttpClient(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<TwelfthVoice.Api.Speech.SpeechService>.Instance);
        Assert.False(service.IsAvailable);
        Assert.Null(await service.SynthesizeAsync("<speak/>", CancellationToken.None));
    }
}
