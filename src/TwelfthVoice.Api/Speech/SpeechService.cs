using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security;
using System.Text;

namespace TwelfthVoice.Api.Speech;

/// <summary>Settings for the "Speech" configuration section (Azure AI Speech).</summary>
public sealed class SpeechOptions
{
    public const string SectionName = "Speech";

    /// <summary>Speech resource key. Keep it in user secrets, never in appsettings.json.</summary>
    public string? Key { get; set; }

    /// <summary>Speech resource region, e.g. eastus2.</summary>
    public string? Region { get; set; }

    public bool Enabled { get; set; } = true;

    /// <summary>Longest text the API will voice in one request.</summary>
    public int MaxCharacters { get; set; } = 400;

    public bool IsConfigured => Enabled && !string.IsNullOrWhiteSpace(Key) && !string.IsNullOrWhiteSpace(Region);
}

/// <summary>A neural voice and the word its commentators shout when the ball goes in.</summary>
/// <param name="LongGoal">The goal word as commentators hold it, e.g. "Gooooooooal". The voice stretches the repeated letters.</param>
public sealed record CommentaryVoice(string Locale, string Name, string GoalWord, string LongGoal);

/// <summary>
/// Turns text into speech with Azure AI Speech neural voices (REST API, MP3 out). Every clip is
/// cached by its SSML, so the same goal call or line is only ever synthesised once.
/// </summary>
public sealed class SpeechService
{
    private const int MaxCachedClips = 400;

    private static readonly Dictionary<string, CommentaryVoice> Voices = new(StringComparer.OrdinalIgnoreCase)
    {
        ["en"] = new("en-GB", "en-GB-RyanNeural", "Goal", "Goooooooooooooaaaal"),
        ["es"] = new("es-ES", "es-ES-AlvaroNeural", "Gol", "Gooooooooooooool"),
        ["fr"] = new("fr-FR", "fr-FR-HenriNeural", "But", "Buuuuuuuuuuuut"),
        ["pt"] = new("pt-BR", "pt-BR-AntonioNeural", "Gol", "Gooooooooooooool"),
        ["ar"] = new("ar-SA", "ar-SA-HamedNeural", "هدف", "هدددددددف"),
        ["hi"] = new("hi-IN", "hi-IN-MadhurNeural", "गोल", "गोओओओओओओल"),
        ["ja"] = new("ja-JP", "ja-JP-KeitaNeural", "ゴール", "ゴーーーーーーーーール"),
        ["de"] = new("de-DE", "de-DE-ConradNeural", "Tor", "Tooooooooooor"),
        ["it"] = new("it-IT", "it-IT-DiegoNeural", "Gol", "Gooooooooooool")
    };

    private readonly SpeechOptions _options;
    private readonly HttpClient _http;
    private readonly ILogger<SpeechService> _logger;
    private readonly ConcurrentDictionary<string, Lazy<Task<byte[]?>>> _cache = new();
    private long _synthesised;
    private long _characters;
    private long _failures;

    public SpeechService(SpeechOptions options, HttpClient http, ILogger<SpeechService> logger)
    {
        _options = options;
        _http = http;
        _logger = logger;
    }

    public bool IsAvailable => _options.IsConfigured;

    public object Usage => new
    {
        configured = IsAvailable,
        region = _options.Region,
        clipsSynthesised = Interlocked.Read(ref _synthesised),
        charactersSynthesised = Interlocked.Read(ref _characters),
        failures = Interlocked.Read(ref _failures),
        cachedClips = _cache.Count
    };

    public static CommentaryVoice VoiceFor(string? language) =>
        Voices.TryGetValue(NormalizeLanguage(language), out var voice) ? voice : Voices["en"];

    /// <summary>
    /// The goal call: one long held goal word, climbing in pitch, a quick second one, then the scorer
    /// and the score. Lasts about five seconds.
    /// </summary>
    /// <param name="simple">A plainer version (no held word) used if the voice rejects the full one.</param>
    public static string GoalCallSsml(string? language, string scorer, string homeTeam, int homeScore, string awayTeam, int awayScore, bool simple = false)
    {
        var voice = VoiceFor(language);
        string goal = Escape(voice.GoalWord);
        string held = Escape(simple ? voice.GoalWord : voice.LongGoal);
        return Wrap(voice, $"""
            <prosody rate="x-slow" pitch="+30%" volume="x-loud">{held}!</prosody>
            <break time="150ms"/>
            <prosody rate="-20%" pitch="+25%" volume="x-loud">{goal}!</prosody>
            <break time="250ms"/>
            <prosody rate="+8%" pitch="+12%" volume="loud">{Escape(scorer)}! {Escape(homeTeam)} {homeScore}, {Escape(awayTeam)} {awayScore}!</prosody>
            """);
    }

    /// <summary>A commentary line, read with a little broadcast energy.</summary>
    public string? LineSsml(string? language, string text)
    {
        string clean = text.Trim();
        if (clean.Length == 0) return null;
        if (clean.Length > _options.MaxCharacters) clean = clean[.._options.MaxCharacters];
        var voice = VoiceFor(language);
        return Wrap(voice, $"""<prosody rate="+6%" pitch="+4%">{Escape(clean)}</prosody>""");
    }

    /// <summary>MP3 audio for the SSML, from the cache when possible. Null when speech isn't available or fails.</summary>
    public Task<byte[]?> SynthesizeAsync(string ssml, CancellationToken cancellationToken)
    {
        if (!IsAvailable) return Task.FromResult<byte[]?>(null);
        if (_cache.Count > MaxCachedClips) _cache.Clear();

        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ssml)));
        var entry = _cache.GetOrAdd(key, _ => new Lazy<Task<byte[]?>>(() => CallAsync(ssml)));
        return WaitAsync(key, entry.Value, cancellationToken);
    }

    private async Task<byte[]?> WaitAsync(string key, Task<byte[]?> task, CancellationToken cancellationToken)
    {
        var audio = await task.WaitAsync(cancellationToken);
        if (audio is null) _cache.TryRemove(key, out _); // don't cache failures
        return audio;
    }

    private async Task<byte[]?> CallAsync(string ssml)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"https://{_options.Region}.tts.speech.microsoft.com/cognitiveservices/v1");
        request.Headers.Add("Ocp-Apim-Subscription-Key", _options.Key);
        request.Headers.Add("X-Microsoft-OutputFormat", "audio-24khz-48kbitrate-mono-mp3");
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("TwelfthVoice", "1.0"));
        request.Content = new StringContent(ssml, Encoding.UTF8);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/ssml+xml");

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var response = await _http.SendAsync(request, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                Interlocked.Increment(ref _failures);
                _logger.LogWarning("Speech synthesis failed: {Status} {Reason}", (int)response.StatusCode, response.ReasonPhrase);
                return null;
            }

            var audio = await response.Content.ReadAsByteArrayAsync(timeout.Token);
            Interlocked.Increment(ref _synthesised);
            Interlocked.Add(ref _characters, ssml.Length);
            return audio;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            Interlocked.Increment(ref _failures);
            _logger.LogWarning(ex, "Speech synthesis failed");
            return null;
        }
    }

    private static string Wrap(CommentaryVoice voice, string body) =>
        $"""<speak version="1.0" xmlns="http://www.w3.org/2001/10/synthesis" xml:lang="{voice.Locale}"><voice name="{voice.Name}">{body}</voice></speak>""";

    private static string Escape(string text) => SecurityElement.Escape(text) ?? "";

    private static string NormalizeLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language)) return "en";
        string code = language.Trim().ToLowerInvariant();
        int dash = code.IndexOf('-');
        return dash > 0 ? code[..dash] : code;
    }
}
