using Microsoft.Extensions.Logging;
using TwelfthVoice.Intelligence;

namespace TwelfthVoice.Agents;

public sealed record AskRequest(string Question, ViewerMode Mode = ViewerMode.Casual, string? Language = "en");

public sealed record AnalystAnswer
{
    public required string Question { get; init; }
    public required string Answer { get; init; }
    public required string Language { get; init; }
    public required bool Verified { get; init; }
    public required bool FromAi { get; init; }
    public IReadOnlyList<string> ToolsUsed { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Checks { get; init; } = Array.Empty<string>();
}

public sealed record StoryRecap
{
    public required string Headline { get; init; }
    public required string Body { get; init; }
    public required string Language { get; init; }
    public required bool FromAi { get; init; }
    public required bool Verified { get; init; }
    public IReadOnlyList<string> Checks { get; init; } = Array.Empty<string>();
    public required MatchRecap Facts { get; init; }
}

/// <summary>
/// "Ask the match" and the full-time story. The Analyst agent answers fans' questions by calling
/// tools over the live data; the Storyteller writes the recap. Both go through the same
/// number check as live commentary, and through the Localizer for other languages.
/// </summary>
public sealed class MatchAnalyst
{
    private const int MaxQuestionLength = 300;

    private readonly ICrewModel _model;
    private readonly ILogger<MatchAnalyst> _logger;

    public MatchAnalyst(ICrewModel model, ILogger<MatchAnalyst> logger)
    {
        _model = model;
        _logger = logger;
    }

    public bool IsAvailable => _model.IsAvailable;

    public async Task<AnalystAnswer> AskAsync(IMatchData match, AskRequest request, CancellationToken cancellationToken)
    {
        string question = (request.Question ?? "").Trim();
        if (question.Length > MaxQuestionLength) question = question[..MaxQuestionLength];
        string language = CommentaryVariant.NormalizeLanguage(request.Language);

        if (question.Length == 0) return Unanswered(question, language, "Ask a question about the match, for example who has been the best player so far.");
        if (!_model.IsAvailable) return Unanswered(question, language, "The AI analyst isn't connected right now. The live feed and stats still have the full picture.");

        var tools = new MatchTools(match);
        string style = request.Mode == ViewerMode.Analyst
            ? "The fan wants the detailed, numbers-first answer."
            : "The fan is a casual viewer: keep it plain and use one or two numbers at most.";

        string? reply = null;
        FactCheckResult check = FactCheckResult.Ok;
        IReadOnlyList<string>? feedback = null;

        for (int attempt = 1; attempt <= 2; attempt++)
        {
            string prompt = feedback is null
                ? $"{style}\n\nQuestion: {question}"
                : $"{style}\n\nQuestion: {question}\n\nYour previous answer used numbers the data doesn't support ({string.Join(" ", feedback)}). Look the numbers up again with your tools and answer using only what they return.";

            reply = (await _model.RunWithToolsAsync("TwelfthVoiceAnalyst", CrewPrompts.AnalystInstructions, prompt, tools.AsTools(), cancellationToken))?.Trim();
            if (string.IsNullOrWhiteSpace(reply)) break;

            var allowed = NumberGuard.AllowedForms(
                tools.Outputs.SelectMany(NumberGuard.Extract).Concat(NumberGuard.Extract(question)));
            check = NumberGuard.Check(reply, allowed);
            if (check.Passed) break;
            feedback = check.Issues;
        }

        if (string.IsNullOrWhiteSpace(reply))
            return Unanswered(question, language, "The analyst couldn't answer that just now. Try again in a moment.");

        var used = tools.Outputs.Count;
        if (!check.Passed)
        {
            _logger.LogInformation("Analyst answer held back: {Issues}", string.Join(" ", check.Issues));
            return Unanswered(question, language, "I couldn't verify an answer to that from the match data, so I won't guess.") with
            {
                Checks = new[] { "Answer held back: its numbers couldn't be traced to the match data" }
            };
        }

        var checks = new List<string> { used > 0 ? $"Looked up live data {used} time{(used == 1 ? "" : "s")}" : "Answered without looking up data" };
        if (NumberGuard.Extract(reply).Count > 0) checks.Add("Every number traced to match data");

        string answer = reply;
        if (language != "en")
        {
            var translated = await TranslateAsync(new CrewLineText("", reply), language, cancellationToken);
            if (translated is not null)
            {
                answer = translated.Value.Body;
                checks.Add($"Translated to {CrewPrompts.LanguageName(language)}, numbers re-checked");
            }
            else
            {
                language = "en";
            }
        }

        return new AnalystAnswer
        {
            Question = question,
            Answer = answer,
            Language = language,
            Verified = true,
            FromAi = true,
            ToolsUsed = ToolNames(tools),
            Checks = checks
        };
    }

    /// <summary>The full-time (or so-far) story, written by the Storyteller from the recap facts.</summary>
    public async Task<StoryRecap> StoryAsync(MatchRecap recap, ViewerMode mode, string? language, CancellationToken cancellationToken)
    {
        string lang = CommentaryVariant.NormalizeLanguage(language);
        var fallback = new StoryRecap
        {
            Headline = recap.Headline,
            Body = string.Join("\n\n", new[] { recap.Summary, string.Join(" · ", recap.Scorers) }.Where(s => s.Length > 0)),
            Language = "en",
            FromAi = false,
            Verified = true,
            Facts = recap
        };
        if (!_model.IsAvailable) return fallback;

        var facts = new
        {
            headline = recap.Headline,
            summary = recap.Summary,
            scorers = recap.Scorers,
            playerOfTheMatch = recap.PlayerOfTheMatch is null ? null : new
            {
                recap.PlayerOfTheMatch.Name,
                recap.PlayerOfTheMatch.Rating,
                recap.PlayerOfTheMatch.Goals,
                recap.PlayerOfTheMatch.Assists,
                xg = Math.Round(recap.PlayerOfTheMatch.Xg, 2),
                recap.PlayerOfTheMatch.PassesCompleted,
                recap.PlayerOfTheMatch.Tackles
            },
            keyMoments = recap.KeyMoments.Select(m => new { minute = Domain.MatchClock.MinuteLabel(m.Period, m.MatchSecond), m.Headline, why = m.WhyItMatters }),
            stats = recap.StatLines
        };

        var numbers = new List<double>();
        numbers.AddRange(NumberGuard.Extract(CrewJson.Serialize(facts)));
        var allowed = NumberGuard.AllowedForms(numbers);

        var draft = CrewJson.ReadLine(await _model.RunAsync(CrewRole.Storyteller,
            CrewJson.Serialize(new { viewer = new { mode = mode.ToString() }, facts }), cancellationToken));
        if (draft is null) return fallback;

        var check = NumberGuard.Check($"{draft.Value.Headline} {draft.Value.Body}", allowed);
        if (!check.Passed)
        {
            _logger.LogInformation("Recap held back: {Issues}", string.Join(" ", check.Issues));
            return fallback with { Checks = new[] { "AI recap held back by the fact-checker; showing the data-written recap" } };
        }

        var story = new StoryRecap
        {
            Headline = draft.Value.Headline.Length > 0 ? draft.Value.Headline : recap.Headline,
            Body = draft.Value.Body,
            Language = "en",
            FromAi = true,
            Verified = true,
            Checks = new[] { "Every number traced to match data" },
            Facts = recap
        };

        if (lang == "en") return story;
        var translated = await TranslateAsync(new CrewLineText(story.Headline, story.Body), lang, cancellationToken);
        return translated is null
            ? story
            : story with
            {
                Headline = translated.Value.Headline,
                Body = translated.Value.Body,
                Language = lang,
                Checks = story.Checks.Append($"Translated to {CrewPrompts.LanguageName(lang)}, numbers re-checked").ToList()
            };
    }

    private readonly record struct CrewLineText(string Headline, string Body);

    private async Task<(string Headline, string Body)?> TranslateAsync(CrewLineText text, string language, CancellationToken cancellationToken)
    {
        string prompt = CrewJson.Serialize(new { targetLanguage = CrewPrompts.LanguageName(language), text = new { headline = text.Headline, body = text.Body } });
        var translated = CrewJson.ReadLine(await _model.RunAsync(CrewRole.Localizer, prompt, cancellationToken));
        if (translated is null) return null;
        var preserved = NumberGuard.CheckPreserved($"{text.Headline} {text.Body}", $"{translated.Value.Headline} {translated.Value.Body}");
        return preserved.Passed ? translated : null;
    }

    private static IReadOnlyList<string> ToolNames(MatchTools tools) =>
        tools.Outputs.Count == 0 ? Array.Empty<string>() : new[] { $"{tools.Outputs.Count} data look-ups" };

    private static AnalystAnswer Unanswered(string question, string language, string message) => new()
    {
        Question = question,
        Answer = message,
        Language = "en",
        Verified = true,
        FromAi = false
    };
}
