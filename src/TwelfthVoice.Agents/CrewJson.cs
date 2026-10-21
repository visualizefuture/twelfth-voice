using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TwelfthVoice.Agents;

/// <summary>JSON in and out of the agents, tolerant of the ways models wrap their replies.</summary>
internal static class CrewJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
        WriteIndented = false,
        // Keep accents, non-Latin scripts and punctuation readable for the model instead of \uXXXX escapes.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    /// <summary>Reads {"headline","body"} from a reply, ignoring code fences or chatter around the JSON.</summary>
    public static (string Headline, string Body)? ReadLine(string? reply)
    {
        var root = ReadObject(reply);
        if (root is null) return null;
        string headline = GetString(root.Value, "headline");
        string body = GetString(root.Value, "body");
        if (string.IsNullOrWhiteSpace(headline) && string.IsNullOrWhiteSpace(body)) return null;
        return (headline.Trim(), body.Trim());
    }

    /// <summary>Reads {"verdict","issues"} from the fact-checker. Null means the reply couldn't be read.</summary>
    public static FactCheckResult? ReadVerdict(string? reply)
    {
        var root = ReadObject(reply);
        if (root is null) return null;
        string verdict = GetString(root.Value, "verdict");
        if (string.IsNullOrWhiteSpace(verdict)) return null;

        var issues = new List<string>();
        if (root.Value.TryGetProperty("issues", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in list.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
                    issues.Add(item.GetString()!);
            }
        }

        bool passed = verdict.Trim().Equals("pass", StringComparison.OrdinalIgnoreCase);
        if (!passed && issues.Count == 0) issues.Add("The fact-checker rejected the line.");
        return new FactCheckResult(passed, passed ? Array.Empty<string>() : issues);
    }

    private static JsonElement? ReadObject(string? reply)
    {
        if (string.IsNullOrWhiteSpace(reply)) return null;
        int start = reply.IndexOf('{');
        int end = reply.LastIndexOf('}');
        if (start < 0 || end <= start) return null;

        try
        {
            using var document = JsonDocument.Parse(reply[start..(end + 1)]);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string GetString(JsonElement element, string name)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind == JsonValueKind.String)
                return property.Value.GetString() ?? "";
        }
        return "";
    }
}
