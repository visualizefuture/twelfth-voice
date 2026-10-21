using System.ClientModel;
using System.ClientModel.Primitives;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OpenAI;
using OpenAI.Chat;

namespace TwelfthVoice.Agents;

/// <summary>The specialised agents in the broadcast crew. Each has its own fixed instructions.</summary>
public enum CrewRole
{
    Commentator,
    FactChecker,
    Localizer,
    Storyteller
}

/// <summary>
/// The seam between the crew's logic and the model. The real implementation runs Microsoft
/// Agent Framework agents on Microsoft Foundry; tests use a scripted fake.
/// Every method returns null instead of throwing when the model is unavailable, over budget,
/// slow or failing, so callers can always fall back to rule-based text.
/// </summary>
public interface ICrewModel
{
    bool IsAvailable { get; }

    Task<string?> RunAsync(CrewRole role, string prompt, CancellationToken cancellationToken);

    /// <summary>Runs a one-off agent that can call the given tools (used by the Analyst for "Ask the match").</summary>
    Task<string?> RunWithToolsAsync(string name, string instructions, string prompt, IList<AITool> tools, CancellationToken cancellationToken);

    CrewUsage Usage { get; }
}

public sealed record CrewUsage(bool Configured, string Deployment, long Calls, long Failures, long SkippedForBudget, int CallsLastHour, int MaxCallsPerHour);

/// <summary>Rolling one-hour cap on model calls, so a bug or a busy demo can't run up the bill.</summary>
public sealed class CallBudget
{
    private readonly Queue<DateTimeOffset> _calls = new();
    private readonly int _maxPerHour;
    private readonly Func<DateTimeOffset> _clock;

    public CallBudget(int maxPerHour, Func<DateTimeOffset>? clock = null)
    {
        _maxPerHour = Math.Max(0, maxPerHour);
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public bool TryAcquire()
    {
        lock (_calls)
        {
            Trim();
            if (_calls.Count >= _maxPerHour) return false;
            _calls.Enqueue(_clock());
            return true;
        }
    }

    public int UsedLastHour
    {
        get
        {
            lock (_calls)
            {
                Trim();
                return _calls.Count;
            }
        }
    }

    private void Trim()
    {
        var cutoff = _clock() - TimeSpan.FromHours(1);
        while (_calls.Count > 0 && _calls.Peek() < cutoff) _calls.Dequeue();
    }
}

/// <summary>Microsoft Agent Framework agents backed by a Microsoft Foundry model deployment.</summary>
public sealed class FoundryCrewModel : ICrewModel, IDisposable
{
    private readonly AgentOptions _options;
    private readonly ILogger<FoundryCrewModel> _logger;
    private readonly ChatClient? _chatClient;
    private readonly Dictionary<CrewRole, AIAgent> _agents = new();
    private readonly CallBudget _budget;
    private readonly SemaphoreSlim _concurrency;
    private long _calls;
    private long _failures;
    private long _skipped;

    public FoundryCrewModel(AgentOptions options, ILogger<FoundryCrewModel> logger)
    {
        _options = options;
        _logger = logger;
        _budget = new CallBudget(options.MaxCallsPerHour);
        _concurrency = new SemaphoreSlim(Math.Max(1, options.MaxConcurrentCalls));

        if (!options.IsConfigured) return;

        try
        {
            var clientOptions = new OpenAIClientOptions { Endpoint = options.BaseUri() };
            _chatClient = string.IsNullOrWhiteSpace(options.ApiKey)
                ? new ChatClient(options.Deployment, new BearerTokenPolicy(new DefaultAzureCredential(), "https://ai.azure.com/.default"), clientOptions)
                : new ChatClient(options.Deployment, new ApiKeyCredential(options.ApiKey), clientOptions);

            foreach (var role in Enum.GetValues<CrewRole>())
            {
                _agents[role] = _chatClient.AsAIAgent(
                    instructions: CrewPrompts.Instructions(role),
                    name: $"TwelfthVoice{role}",
                    description: CrewPrompts.Description(role));
            }

            _logger.LogInformation("Broadcast crew ready: {Deployment} at {Endpoint} ({Auth})",
                options.Deployment, options.BaseUri(), string.IsNullOrWhiteSpace(options.ApiKey) ? "Entra ID" : "API key");
        }
        catch (Exception ex)
        {
            _chatClient = null;
            _agents.Clear();
            _logger.LogWarning(ex, "Could not set up the broadcast crew; commentary stays rule-based.");
        }
    }

    public bool IsAvailable => _chatClient is not null;

    public CrewUsage Usage => new(
        IsAvailable, _options.Deployment, Interlocked.Read(ref _calls), Interlocked.Read(ref _failures),
        Interlocked.Read(ref _skipped), _budget.UsedLastHour, _options.MaxCallsPerHour);

    public Task<string?> RunAsync(CrewRole role, string prompt, CancellationToken cancellationToken) =>
        _agents.TryGetValue(role, out var agent)
            ? InvokeAsync(agent, role.ToString(), prompt, cancellationToken)
            : Task.FromResult<string?>(null);

    public Task<string?> RunWithToolsAsync(string name, string instructions, string prompt, IList<AITool> tools, CancellationToken cancellationToken)
    {
        if (_chatClient is null) return Task.FromResult<string?>(null);
        var agent = _chatClient.AsAIAgent(instructions: instructions, name: name, tools: tools);
        return InvokeAsync(agent, name, prompt, cancellationToken);
    }

    private async Task<string?> InvokeAsync(AIAgent agent, string label, string prompt, CancellationToken cancellationToken)
    {
        if (!_budget.TryAcquire())
        {
            Interlocked.Increment(ref _skipped);
            return null;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(5, _options.TimeoutSeconds)));

        try
        {
            await _concurrency.WaitAsync(timeout.Token);
            try
            {
                Interlocked.Increment(ref _calls);
                var response = await agent.RunAsync(prompt, cancellationToken: timeout.Token);
                return response.Text;
            }
            finally
            {
                _concurrency.Release();
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Interlocked.Increment(ref _failures);
            _logger.LogWarning("{Agent} timed out after {Seconds}s", label, _options.TimeoutSeconds);
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Interlocked.Increment(ref _failures);
            _logger.LogWarning(ex, "{Agent} call failed", label);
            return null;
        }
    }

    public void Dispose() => _concurrency.Dispose();
}

/// <summary>Used when no endpoint is configured: everything stays rule-based.</summary>
public sealed class NullCrewModel : ICrewModel
{
    public bool IsAvailable => false;
    public CrewUsage Usage => new(false, "", 0, 0, 0, 0, 0);
    public Task<string?> RunAsync(CrewRole role, string prompt, CancellationToken cancellationToken) => Task.FromResult<string?>(null);
    public Task<string?> RunWithToolsAsync(string name, string instructions, string prompt, IList<AITool> tools, CancellationToken cancellationToken) => Task.FromResult<string?>(null);
}
