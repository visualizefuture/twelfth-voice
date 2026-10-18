using System.Text.Json;
using System.Text.Json.Serialization;

namespace TwelfthVoice.Domain;

/// <summary>Shared JSON settings: camelCase, enums as strings, nulls omitted.</summary>
public static class MatchJson
{
    public static readonly JsonSerializerOptions Compact = Create(indented: false);
    public static readonly JsonSerializerOptions Indented = Create(indented: true);

    private static JsonSerializerOptions Create(bool indented)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = indented,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
