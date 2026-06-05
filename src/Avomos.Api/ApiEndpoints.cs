using Avomos.Api.Features.Chat;
using Avomos.Api.Features.Lyrics;
using Avomos.Api.Features.Riders;
using Avomos.Api.Services;
using MediatR;

namespace Avomos.Api;

public static class ApiEndpoints
{
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

        app.MapGet("/chat/sessions", (ChatSessionService svc) =>
        {
            var (sessions, currentId) = svc.LoadIndex();
            return Results.Ok(new { sessions, currentId });
        });

        app.MapGet("/chat/session", (ChatSessionService svc) =>
        {
            var (sessions, currentId) = svc.LoadIndex();
            if (currentId == null)
            {
                var info = svc.CreateInfo("New Chat");
                svc.SaveIndex([info], info.Id);
                svc.CreateEmptySession();
                return Results.Ok(new { sessionId = info.Id, buffer = new List<object>(), messages = new List<object>() });
            }
            var data = svc.LoadSession(currentId);
            return Results.Ok(new { sessionId = currentId, buffer = data?.Buffer ?? new List<object>(), messages = data?.Messages ?? new List<object>() });
        });

        app.MapGet("/chat/session/{id}", (string id, ChatSessionService svc) =>
        {
            var data = svc.LoadSession(id);
            if (data is null) return Results.NotFound();
            return Results.Ok(new { sessionId = id, buffer = data.Buffer ?? new List<object>(), messages = data.Messages ?? new List<object>() });
        });

        app.MapPost("/chat/session", async (SaveSessionRequest req, ChatSessionService svc) =>
        {
            var sessionId = req.SessionId ?? Guid.NewGuid().ToString();

            var (sessions, _) = svc.LoadIndex();
            var existing = sessions.FirstOrDefault(s => s.Id == sessionId);
            var updatedAt = DateTime.UtcNow.ToString("O");

            var sessionData = new SessionData { Buffer = req.Buffer, Messages = req.Messages, UpdatedAt = updatedAt };
            await svc.SaveSessionAsync(sessionId, sessionData);

            var name = existing?.Name ?? "New Chat";
            var newSessions = sessions.Where(s => s.Id != sessionId).ToList();
            newSessions.Insert(0, new SessionInfo(sessionId, name, updatedAt));
            svc.SaveIndex(newSessions, sessionId);

            return Results.Ok(new { sessionId, sessions = newSessions, currentId = sessionId });
        });

        app.MapPost("/chat/sessions/create", (ChatSessionService svc) =>
        {
            var info = svc.CreateInfo("New Chat");
            var (sessions, _) = svc.LoadIndex();
            sessions.Insert(0, info);
            svc.SaveIndex(sessions, info.Id);
            svc.CreateEmptySession();
            return Results.Ok(new { id = info.Id });
        });

        app.MapPut("/chat/session/{id}/rename", (string id, RenameRequest req, ChatSessionService svc) =>
        {
            var (sessions, currentId) = svc.LoadIndex();
            var idx = sessions.FindIndex(s => s.Id == id);
            if (idx < 0) return Results.NotFound();
            sessions[idx] = sessions[idx] with { Name = req.Name ?? sessions[idx].Name };
            svc.SaveIndex(sessions, currentId);
            return Results.Ok(new { sessions, currentId });
        });

        app.MapDelete("/chat/session/{id}", (string id, ChatSessionService svc) =>
        {
            var (sessions, currentId) = svc.LoadIndex();
            var newSessions = sessions.Where(s => s.Id != id).ToList();
            svc.DeleteSession(id);
            var newCurrentId = currentId == id ? newSessions.FirstOrDefault()?.Id : currentId;
            svc.SaveIndex(newSessions, newCurrentId);
            return Results.Ok(new { currentId = newCurrentId });
        });

        app.MapPost("/logs", async (LogEntry entry, ILogger<Program> logger) =>
        {
            logger.LogInformation("[Ext] {Message}", entry.Message);
            await Task.CompletedTask;
            return Results.Ok();
        });
    }
}

public record LogEntry(string Message);

public record SaveSessionRequest(
    string? SessionId,
    List<object>? Buffer,
    List<object>? Messages
);

public record RenameRequest(
    string? Name
);
