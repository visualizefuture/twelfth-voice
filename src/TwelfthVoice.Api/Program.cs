using System.Text.Json;
using System.Text.Json.Serialization;
using TwelfthVoice.Agents;
using TwelfthVoice.Api.Endpoints;
using TwelfthVoice.Api.Hubs;
using TwelfthVoice.Api.Live;
using TwelfthVoice.Api.Mcp;
using TwelfthVoice.Api.Storage;

var builder = WebApplication.CreateBuilder(args);

// Same JSON shape everywhere: camelCase, enums as strings, nulls omitted.
static void ConfigureJson(JsonSerializerOptions options)
{
    options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    options.Converters.Add(new JsonStringEnumConverter());
}

builder.Services.ConfigureHttpJsonOptions(o => ConfigureJson(o.SerializerOptions));
builder.Services.AddSignalR().AddJsonProtocol(o => ConfigureJson(o.PayloadSerializerOptions));
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();

string[] allowedOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? new[] { "http://localhost:4200" };
builder.Services.AddCors(options => options.AddPolicy("frontend", policy => policy
    .WithOrigins(allowedOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));

builder.Services.AddTwelfthVoiceStorage(builder.Configuration);
// The broadcast crew (Microsoft Agent Framework on Microsoft Foundry). Without an endpoint it stays off
// and every line remains rule-written, so the app always runs.
var agentOptions = builder.Configuration.GetSection(AgentOptions.SectionName).Get<AgentOptions>() ?? new AgentOptions();
builder.Services.AddSingleton(agentOptions);
if (agentOptions.IsConfigured)
    builder.Services.AddSingleton<ICrewModel, FoundryCrewModel>();
else
    builder.Services.AddSingleton<ICrewModel, NullCrewModel>();
builder.Services.AddSingleton<BroadcastCrew>();
builder.Services.AddSingleton<MatchAnalyst>();
builder.Services.AddSingleton<CrewDispatcher>();

builder.Services.AddSingleton<ViewerConnections>();
builder.Services.AddSingleton<IMatchBroadcaster, SignalRBroadcaster>();
builder.Services.AddSingleton<MatchRegistry>();

// MCP server: the live match data as tools for any MCP client (GitHub Copilot, Claude, other agents).
bool mcpEnabled = builder.Configuration.GetValue("Mcp:Enabled", true);
if (mcpEnabled)
{
    builder.Services.AddMcpServer()
        .WithHttpTransport(options => options.Stateless = true)
        .WithTools<MatchMcpTools>();
}

var app = builder.Build();

app.UseCors("frontend");
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapOpenApi();
app.MapHealthChecks("/health");
app.MapTwelfthVoiceApi();
app.MapHub<MatchHub>("/hubs/match");
if (mcpEnabled) app.MapMcp("/mcp");

app.Run();

/// <summary>Exposed so integration tests can host the API with WebApplicationFactory.</summary>
public partial class Program
{
}
