using System.Text.Json;
using System.Text.Json.Serialization;

namespace Avomos.Api.Services;

public class ChatSessionService
{
    private static readonly string ChatDir = Path.Combine(".cache", "chat");
    private static readonly string IndexPath = Path.Combine(ChatDir, "index.json");

    public (List<SessionInfo> sessions, string? currentId) LoadIndex()
    {
        if (!File.Exists(IndexPath)) return ([], null);
        var json = File.ReadAllText(IndexPath);
        var data = JsonSerializer.Deserialize<SessionIndex>(json);
        return (data?.Sessions ?? [], data?.CurrentId);
    }

    public void SaveIndex(List<SessionInfo> sessions, string? currentId)
    {
        Directory.CreateDirectory(ChatDir);
        File.WriteAllText(IndexPath, JsonSerializer.Serialize(new SessionIndex
        {
            Sessions = sessions,
            CurrentId = currentId
        }));
    }

    public SessionData? LoadSession(string id)
    {
        var path = Path.Combine(ChatDir, $"{id}.json");
        if (!File.Exists(path)) return null;
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<SessionData>(json);
    }

    public async Task SaveSessionAsync(string id, SessionData data)
    {
        Directory.CreateDirectory(ChatDir);
        var path = Path.Combine(ChatDir, $"{id}.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(data));
    }

    public string CreateEmptySession()
    {
        var id = Guid.NewGuid().ToString();
        var now = DateTime.UtcNow.ToString("O");
        File.WriteAllText(Path.Combine(ChatDir, $"{id}.json"), "{}");
        return id;
    }

    public void DeleteSession(string id)
    {
        var path = Path.Combine(ChatDir, $"{id}.json");
        if (File.Exists(path)) File.Delete(path);
    }

    public SessionInfo CreateInfo(string name) =>
        new(Guid.NewGuid().ToString(), name, DateTime.UtcNow.ToString("O"));
}

public record SessionInfo(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("updatedAt")] string UpdatedAt
);

public class SessionIndex
{
    [JsonPropertyName("sessions")]
    public List<SessionInfo> Sessions { get; set; } = [];
    [JsonPropertyName("currentId")]
    public string? CurrentId { get; set; }
}

public class SessionData
{
    [JsonPropertyName("buffer")]
    public List<object>? Buffer { get; set; }
    [JsonPropertyName("messages")]
    public List<object>? Messages { get; set; }
    [JsonPropertyName("updatedAt")]
    public string? UpdatedAt { get; set; }
}
