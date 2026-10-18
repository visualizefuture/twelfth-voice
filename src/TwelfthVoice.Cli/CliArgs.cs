using System.Globalization;
using TwelfthVoice.Simulator;

namespace TwelfthVoice.Cli;

/// <summary>Minimal "command --key value" parser, so the CLI has no package dependencies.</summary>
internal sealed class CliArgs
{
    private readonly Dictionary<string, string> _options = new(StringComparer.OrdinalIgnoreCase);

    public string Command { get; private init; } = "help";

    public static CliArgs Parse(string[] args)
    {
        var cli = new CliArgs { Command = args.Length > 0 ? args[0].ToLowerInvariant() : "help" };
        for (int i = 1; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal)) continue;
            string key = args[i][2..];
            bool hasValue = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal);
            cli._options[key] = hasValue ? args[++i] : "true";
        }
        return cli;
    }

    public string? Get(string key) => _options.TryGetValue(key, out var value) ? value : null;

    public bool Has(string key) => _options.ContainsKey(key);

    public int GetInt(string key, int fallback) =>
        int.TryParse(Get(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;

    public double GetDouble(string key, double fallback) =>
        double.TryParse(Get(key), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : fallback;

    public SimulationOptions ToSimulationOptions() => new()
    {
        Seed = GetInt("seed", 42),
        HomeTeamId = Get("home")?.ToUpperInvariant(),
        AwayTeamId = Get("away")?.ToUpperInvariant()
    };
}
