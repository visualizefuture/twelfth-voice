namespace TwelfthVoice.Agents;

/// <summary>Settings for the "AzureAI" configuration section.</summary>
public sealed class AgentOptions
{
    public const string SectionName = "AzureAI";

    /// <summary>
    /// Foundry / Azure OpenAI v1 endpoint, e.g. https://my-resource.services.ai.azure.com/openai/v1/.
    /// A full URL ending in /responses or /chat/completions is also accepted and trimmed.
    /// </summary>
    public string? Endpoint { get; set; }

    /// <summary>Model deployment name, e.g. gpt-4.1-mini.</summary>
    public string Deployment { get; set; } = "gpt-4.1-mini";

    /// <summary>
    /// API key. Keep it in user secrets or an environment variable, never in appsettings.json.
    /// Leave empty to sign in with Microsoft Entra ID (az login locally, managed identity in Azure).
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>Master switch. When false, or when no endpoint is set, all commentary stays rule-based.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Only insights at least this important get AI commentary (goals and summaries always do).</summary>
    public double MinImportance { get; set; } = 0.5;

    /// <summary>The AI fact-checker reviews lines for insights at least this important. Number checks run on everything.</summary>
    public double LlmFactCheckMinImportance { get; set; } = 0.6;

    /// <summary>Spending guard: model calls allowed per rolling hour across the whole app.</summary>
    public int MaxCallsPerHour { get; set; } = 600;

    /// <summary>How many model calls may run at once.</summary>
    public int MaxConcurrentCalls { get; set; } = 4;

    public int TimeoutSeconds { get; set; } = 20;

    public bool IsConfigured => Enabled && !string.IsNullOrWhiteSpace(Endpoint);

    /// <summary>Normalises the endpoint to the v1 base URL the OpenAI client expects.</summary>
    public Uri BaseUri()
    {
        string url = (Endpoint ?? "").Trim();
        foreach (var suffix in new[] { "/responses", "/chat/completions" })
        {
            if (url.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) url = url[..^suffix.Length];
        }
        url = url.TrimEnd('/');
        if (!url.EndsWith("/openai/v1", StringComparison.OrdinalIgnoreCase)) url += "/openai/v1";
        return new Uri(url + "/");
    }
}
