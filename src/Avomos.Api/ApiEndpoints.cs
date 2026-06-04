using System.Text.Json;
using System.Text.Json.Serialization;
using Avomos.Api.Features.Lyrics;
using Avomos.Api.Features.Chat;
using Avomos.Api.Features.Riders;
using MediatR;

namespace Avomos.Api;

public static class ApiEndpoints
{
    private static readonly string ChatDir = Path.Combine(".cache", "chat");
    private static readonly string IndexPath = Path.Combine(ChatDir, "index.json");

    public static void Map(WebApplication app)
    {
        app.MapGet("/tracks/search", async (
            string query,
            int? limit, int? offset,
            float? titleLyricsWeight, float? stylesWeight,
            IMediator mediator) =>
        {
            var cmd = new SemanticSearchQuery(query)
            {
                Limit = limit ?? 10,
                Offset = offset ?? 0,
                TitleLyricsWeight = titleLyricsWeight ?? 0.7f,
                StylesWeight = stylesWeight ?? 0.3f,
            };
            var result = await mediator.Send(cmd);
            return Results.Ok(result);
        });

        app.MapGet("/tracks/{originId}", async (string originId, IMediator mediator) =>
        {
            var result = await mediator.Send(new GetByOriginIdQuery(originId));
            return result is null
                ? Results.Ok(new { found = false, lyric = (object?)null })
                : Results.Ok(new { found = true, lyric = result });
        });

        app.MapPost("/tracks/upsert", async (UpsertTracksCommand cmd, IMediator mediator) =>
        {
            var result = await mediator.Send(cmd);
            return Results.Ok(result);
        });

        app.MapPost("/tracks/metadata", async (UpdateMetadataCommand cmd, IMediator mediator) =>
        {
            var result = await mediator.Send(cmd);
            return Results.Ok(result);
        });

        app.MapDelete("/tracks/{originId}", async (string originId, IMediator mediator) =>
        {
            var result = await mediator.Send(new DeleteLyricCommand(originId));
            return result.Deleted ? Results.Ok(result) : Results.NotFound(result);
        });

        ChatEndpoint.Map(app);
        RidersApi.Map(app);

        // --- Multi-session API ---

        app.MapGet("/chat/sessions", () =>
        {
            var (sessions, currentId) = LoadIndex();
            return Results.Ok(new { sessions, currentId });
        });

        app.MapGet("/chat/session", () =>
        {
            var (sessions, currentId) = LoadIndex();
            if (currentId == null)
            {
                var id = Guid.NewGuid().ToString();
                var now = DateTime.UtcNow.ToString("O");
                sessions.Insert(0, new SessionInfo(id, "New Chat", now));
                SaveIndex(sessions, id);
                File.WriteAllText(Path.Combine(ChatDir, $"{id}.json"), "{}");
                return Results.Ok(new { sessionId = id, buffer = new List<object>(), messages = new List<object>() });
            }
            var path = Path.Combine(ChatDir, $"{currentId}.json");
            if (!File.Exists(path))
            {
                File.WriteAllText(path, "{}");
                return Results.Ok(new { sessionId = currentId, buffer = new List<object>(), messages = new List<object>() });
            }
            var json = File.ReadAllText(path);
            var data = JsonSerializer.Deserialize<SessionData>(json);
            return Results.Ok(new { sessionId = currentId, buffer = data?.Buffer ?? new List<object>(), messages = data?.Messages ?? new List<object>() });
        });

        app.MapGet("/chat/session/{id}", (string id) =>
        {
            var path = Path.Combine(ChatDir, $"{id}.json");
            if (!File.Exists(path)) return Results.NotFound();
            var json = File.ReadAllText(path);
            var data = JsonSerializer.Deserialize<SessionData>(json);
            return Results.Ok(new { sessionId = id, buffer = data?.Buffer ?? new List<object>(), messages = data?.Messages ?? new List<object>() });
        });

        app.MapPost("/chat/session", async (SaveSessionRequest data) =>
        {
            Directory.CreateDirectory(ChatDir);
            var sessionId = data.SessionId ?? Guid.NewGuid().ToString();
            var path = Path.Combine(ChatDir, $"{sessionId}.json");

            var (sessions, _) = LoadIndex();
            var existing = sessions.FirstOrDefault(s => s.Id == sessionId);
            var updatedAt = DateTime.UtcNow.ToString("O");

            var sessionData = new SessionData { Buffer = data.Buffer, Messages = data.Messages, UpdatedAt = updatedAt };
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(sessionData));

            var name = existing?.Name ?? "New Chat";
            var newSessions = sessions.Where(s => s.Id != sessionId).ToList();
            newSessions.Insert(0, new SessionInfo(sessionId, name, updatedAt));
            SaveIndex(newSessions, sessionId);

            return Results.Ok(new { sessionId, sessions = newSessions, currentId = sessionId });
        });

        app.MapPost("/chat/sessions/create", () =>
        {
            var id = Guid.NewGuid().ToString();
            var now = DateTime.UtcNow.ToString("O");
            var (sessions, _) = LoadIndex();
            sessions.Insert(0, new SessionInfo(id, "New Chat", now));
            SaveIndex(sessions, id);
            File.WriteAllText(Path.Combine(ChatDir, $"{id}.json"), "{}");
            return Results.Ok(new { id });
        });

        app.MapPut("/chat/session/{id}/rename", async (string id, RenameRequest data) =>
        {
            var (sessions, currentId) = LoadIndex();
            var idx = sessions.FindIndex(s => s.Id == id);
            if (idx < 0) return Results.NotFound();
            sessions[idx] = sessions[idx] with { Name = data.Name ?? sessions[idx].Name };
            SaveIndex(sessions, currentId);
            await Task.CompletedTask;
            return Results.Ok(new { sessions, currentId });
        });

        app.MapDelete("/chat/session/{id}", (string id) =>
        {
            var (sessions, currentId) = LoadIndex();
            var newSessions = sessions.Where(s => s.Id != id).ToList();
            var path = Path.Combine(ChatDir, $"{id}.json");
            if (File.Exists(path)) File.Delete(path);
            var newCurrentId = currentId == id ? newSessions.FirstOrDefault()?.Id : currentId;
            SaveIndex(newSessions, newCurrentId);
            return Results.Ok(new { currentId = newCurrentId });
        });

        app.MapPost("/logs", async (LogEntry entry, ILogger<Program> logger) =>
        {
            logger.LogInformation("[Ext] {Message}", entry.Message);
            return Results.Ok();
        });
    }

    // --- Session index helpers ---

    private static (List<SessionInfo> sessions, string? currentId) LoadIndex()
    {
        if (!File.Exists(IndexPath)) return (new List<SessionInfo>(), null);
        var json = File.ReadAllText(IndexPath);
        var data = JsonSerializer.Deserialize<SessionIndex>(json);
        return (data?.Sessions ?? new List<SessionInfo>(), data?.CurrentId);
    }

    private static void SaveIndex(List<SessionInfo> sessions, string? currentId)
    {
        Directory.CreateDirectory(ChatDir);
        File.WriteAllText(IndexPath, JsonSerializer.Serialize(new SessionIndex { Sessions = sessions, CurrentId = currentId }));
    }
}

// --- Records ---

public record LogEntry(string Message);

public record SessionInfo(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("updatedAt")] string UpdatedAt
);

public class SessionIndex
{
    [JsonPropertyName("sessions")]
    public List<SessionInfo> Sessions { get; set; } = new();
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

public record SaveSessionRequest(
    [property: JsonPropertyName("sessionId")] string? SessionId,
    [property: JsonPropertyName("buffer")] List<object>? Buffer,
    [property: JsonPropertyName("messages")] List<object>? Messages
);

public record RenameRequest(
    [property: JsonPropertyName("name")] string? Name
);
