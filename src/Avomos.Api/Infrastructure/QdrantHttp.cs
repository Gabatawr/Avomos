using System.Text.Json;
using System.Text.Json.Serialization;

namespace Avomos.Api.Infrastructure;

public class QdrantSearchResult
{
    [JsonPropertyName("result")]
    public List<Dictionary<string, JsonElement>>? Result { get; set; }
}

public class QdrantScrollResponse
{
    [JsonPropertyName("result")]
    public QdrantScrollResult? Result { get; set; }
}

public class QdrantScrollResult
{
    [JsonPropertyName("points")]
    public List<QdrantScrollPoint>? Points { get; set; }
}

public class QdrantScrollPoint
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("payload")]
    public Dictionary<string, JsonElement>? Payload { get; set; }
}

public static class QdrantPayload
{
    public static string String(Dictionary<string, JsonElement>? payload, string key)
    {
        if (payload is null) return "";
        if (!payload.TryGetValue(key, out var value)) return "";
        return value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    }

    public static int Int(Dictionary<string, JsonElement>? payload, string key)
    {
        if (payload is null) return 0;
        if (!payload.TryGetValue(key, out var value)) return 0;
        if (value.ValueKind == JsonValueKind.Number) return value.TryGetInt64(out var i) ? (int)i : (int)value.GetDouble();
        return 0;
    }

    public static bool Bool(Dictionary<string, JsonElement>? payload, string key)
    {
        if (payload is null) return false;
        if (!payload.TryGetValue(key, out var value)) return false;
        return value.ValueKind == JsonValueKind.True;
    }
}
