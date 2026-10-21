using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using TwelfthVoice.Domain;
using TwelfthVoice.Intelligence;

namespace TwelfthVoice.Agents;

/// <summary>One audience a line is written for. Viewers who share a variant share the line (and the cost).</summary>
public sealed record CommentaryVariant(ViewerMode Mode, string Language, Perspective Perspective)
{
    public static CommentaryVariant For(ViewerProfile profile, Perspective perspective) =>
        new(profile.Mode, NormalizeLanguage(profile.Language), perspective);

    public bool IsEnglish => Language == "en";

    public static string NormalizeLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language)) return "en";
        string code = language.Trim().ToLowerInvariant();
        int dash = code.IndexOf('-');
        return dash > 0 ? code[..dash] : code;
    }
}

/// <summary>The match situation when the moment happened.</summary>
public sealed record MomentContext(MatchInfo Info, int HomeScore, int AwayScore);

/// <summary>A line ready for air, with an audit trail of what the crew did to it.</summary>
public sealed record CrewLine
{
    public required string InsightId { get; init; }
    public required string Headline { get; init; }
    public required string Body { get; init; }
    public required string Language { get; init; }
    public required string Source { get; init; }
    public required bool Verified { get; init; }
    public IReadOnlyList<string> Checks { get; init; } = Array.Empty<string>();

    public PersonalizedInsight ApplyTo(PersonalizedInsight insight) => insight with
    {
        Headline = Headline,
        Body = Body,
        Language = Language,
        Source = Source,
        Verified = Verified,
        Checks = Checks,
        AnalystNote = insight.AnalystNote
    };
}

public sealed record CrewStats(long Voiced, long Approved, long DraftsRejected, long Retries, long FellBackToRules, long Translated, long TranslationsRejected);

/// <summary>
/// The broadcast crew. For each moment worth voicing, and for each audience watching:
///   1. the Director (policy) decides whether the moment deserves AI commentary or just translation;
///   2. the Commentator agent writes a line from the verified facts for that audience;
///   3. the Fact-checker checks it: every number is traced to the data, then the AI fact-checker
///      reviews the claims; a rejected draft goes back to the Commentator once with the reasons;
///   4. the Localizer agent translates, and the numbers are checked again;
///   5. if anything fails, the rule-written line (built by code from the data) goes out instead.
/// Facts come from code, words come from AI, and nothing unverified goes on air.
/// </summary>
public sealed class BroadcastCrew
{
    private const int MaxCacheEntries = 6000;
    private static readonly InsightKind[] AlwaysVoiced = { InsightKind.Goal, InsightKind.HalfTime, InsightKind.FullTime };

    private readonly ICrewModel _model;
    private readonly AgentOptions _options;
    private readonly ILogger<BroadcastCrew> _logger;
    private readonly ConcurrentDictionary<string, Lazy<Task<CrewLine?>>> _cache = new();
    private readonly ConcurrentDictionary<string, CrewLine> _finished = new();

    private long _voiced, _approved, _rejected, _retries, _fallbacks, _translated, _translationsRejected;

    public BroadcastCrew(ICrewModel model, AgentOptions options, ILogger<BroadcastCrew> logger)
    {
        _model = model;
        _options = options;
        _logger = logger;
    }

    public bool IsAvailable => _model.IsAvailable;

    public CrewUsage Usage => _model.Usage;

    public CrewStats Stats => new(
        Interlocked.Read(ref _voiced), Interlocked.Read(ref _approved), Interlocked.Read(ref _rejected),
        Interlocked.Read(ref _retries), Interlocked.Read(ref _fallbacks), Interlocked.Read(ref _translated),
        Interlocked.Read(ref _translationsRejected));

    /// <summary>The Director's call: is this moment worth AI work for this audience?</summary>
    public bool ShouldHandle(Insight insight, CommentaryVariant variant) =>
        _model.IsAvailable && (WantsVoice(insight) || !variant.IsEnglish);

    public bool WantsVoice(Insight insight) =>
        AlwaysVoiced.Contains(insight.Kind) || insight.Importance >= _options.MinImportance;

    /// <summary>A finished line, if the crew has already produced one for this audience.</summary>
    public CrewLine? TryGetCached(string insightId, CommentaryVariant variant) =>
        _finished.TryGetValue(Key(insightId, variant), out var line) ? line : null;

    /// <summary>Applies any finished crew lines to a personalised feed.</summary>
    public IReadOnlyList<PersonalizedInsight> ApplyCached(IEnumerable<PersonalizedInsight> insights, ViewerProfile profile) =>
        insights.Select(i => TryGetCached(i.InsightId, CommentaryVariant.For(profile, i.Perspective)) is { } line ? line.ApplyTo(i) : i).ToList();

    /// <summary>
    /// Produces the line for one audience. Concurrent requests for the same audience share one run.
    /// Returns null when the crew has nothing to add (unavailable, or English rule text is already best).
    /// </summary>
    public Task<CrewLine?> PrepareAsync(Insight insight, MomentContext context, CommentaryVariant variant, CancellationToken cancellationToken = default)
    {
        if (!ShouldHandle(insight, variant)) return Task.FromResult<CrewLine?>(null);

        if (_cache.Count > MaxCacheEntries)
        {
            _cache.Clear();
            _finished.Clear();
        }

        string key = Key(insight.Id, variant);
        var lazy = _cache.GetOrAdd(key, _ => new Lazy<Task<CrewLine?>>(() => RunAsync(key, insight, context, variant, cancellationToken)));
        return lazy.Value;
    }

    private async Task<CrewLine?> RunAsync(string key, Insight insight, MomentContext context, CommentaryVariant variant, CancellationToken cancellationToken)
    {
        try
        {
            var line = WantsVoice(insight)
                ? await VoiceAsync(insight, context, variant, cancellationToken)
                : RuleLine(insight, variant, Array.Empty<string>());

            if (!variant.IsEnglish) line = await TranslateAsync(line, variant.Language, cancellationToken);

            if (line.Source == "rules" && variant.IsEnglish) return null; // nothing better than what viewers already have
            _finished[key] = line;
            return line;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Crew failed on insight {InsightId}", insight.Id);
            _cache.TryRemove(key, out _);
            return null;
        }
    }

    // ------------------------------------------------------------------ commentator + fact-checker

    private async Task<CrewLine> VoiceAsync(Insight insight, MomentContext context, CommentaryVariant variant, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _voiced);
        var facts = MomentFacts.Build(insight, context, variant);
        var allowed = NumberGuard.AllowedForms(facts.Numbers);
        bool useAiChecker = insight.Importance >= _options.LlmFactCheckMinImportance || AlwaysVoiced.Contains(insight.Kind);

        IReadOnlyList<string>? feedback = null;
        for (int attempt = 1; attempt <= 2; attempt++)
        {
            if (attempt == 2) Interlocked.Increment(ref _retries);

            string prompt = CrewJson.Serialize(new { facts = facts.Payload, previousDraftRejectedBecause = feedback });
            var draft = CrewJson.ReadLine(await _model.RunAsync(CrewRole.Commentator, prompt, cancellationToken));
            if (draft is null) break; // model unavailable or unreadable: use the rule line

            var checks = new List<string>();
            var numbers = NumberGuard.Check($"{draft.Value.Headline} {draft.Value.Body}", allowed);
            if (!numbers.Passed)
            {
                Interlocked.Increment(ref _rejected);
                feedback = numbers.Issues;
                continue;
            }
            checks.Add("Every number traced to match data");

            if (useAiChecker)
            {
                string reviewPrompt = CrewJson.Serialize(new { facts = facts.Payload, draft = new { headline = draft.Value.Headline, body = draft.Value.Body } });
                var verdict = CrewJson.ReadVerdict(await _model.RunAsync(CrewRole.FactChecker, reviewPrompt, cancellationToken));
                if (verdict is { Passed: false })
                {
                    Interlocked.Increment(ref _rejected);
                    feedback = verdict.Issues;
                    continue;
                }
                if (verdict is { Passed: true }) checks.Add("Approved by the AI fact-checker");
            }

            if (attempt == 2) checks.Add("Rewritten after the fact-checker's notes");
            Interlocked.Increment(ref _approved);
            return new CrewLine
            {
                InsightId = insight.Id,
                Headline = draft.Value.Headline.Length > 0 ? draft.Value.Headline : insight.Headline,
                Body = draft.Value.Body,
                Language = "en",
                Source = "ai",
                Verified = true,
                Checks = checks
            };
        }

        Interlocked.Increment(ref _fallbacks);
        var note = feedback is null
            ? Array.Empty<string>()
            : new[] { "AI draft held back by the fact-checker; showing the data-written line" };
        return RuleLine(insight, variant, note);
    }

    // ------------------------------------------------------------------ localizer

    private async Task<CrewLine> TranslateAsync(CrewLine line, string language, CancellationToken cancellationToken)
    {
        string prompt = CrewJson.Serialize(new
        {
            targetLanguage = CrewPrompts.LanguageName(language),
            text = new { headline = line.Headline, body = line.Body }
        });

        var translated = CrewJson.ReadLine(await _model.RunAsync(CrewRole.Localizer, prompt, cancellationToken));
        if (translated is null) return line;

        var preserved = NumberGuard.CheckPreserved($"{line.Headline} {line.Body}", $"{translated.Value.Headline} {translated.Value.Body}");
        if (!preserved.Passed)
        {
            Interlocked.Increment(ref _translationsRejected);
            return line with { Checks = line.Checks.Append($"Translation to {CrewPrompts.LanguageName(language)} held back: numbers changed").ToList() };
        }

        Interlocked.Increment(ref _translated);
        return line with
        {
            Headline = translated.Value.Headline.Length > 0 ? translated.Value.Headline : line.Headline,
            Body = translated.Value.Body.Length > 0 ? translated.Value.Body : line.Body,
            Language = language,
            Checks = line.Checks.Append($"Translated to {CrewPrompts.LanguageName(language)}, numbers re-checked").ToList()
        };
    }

    private static CrewLine RuleLine(Insight insight, CommentaryVariant variant, IReadOnlyList<string> checks) => new()
    {
        InsightId = insight.Id,
        Headline = insight.Headline,
        Body = variant.Mode == ViewerMode.Analyst ? insight.WhyItMatters : Narration.FirstSentence(insight.WhyItMatters),
        Language = "en",
        Source = "rules",
        Verified = true,
        Checks = checks
    };

    private static string Key(string insightId, CommentaryVariant variant) =>
        $"{insightId}|{variant.Mode}|{variant.Language}|{variant.Perspective}";
}
