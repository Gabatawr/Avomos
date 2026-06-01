using System.ComponentModel;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Avomos.Api.Features.Chat;

public record ChatMessage(string Role, string Content, string? Simple = null, AdvancedData? Advanced = null, List<string>? Hooks = null, string? Reply = null);
public record ChatRequest(List<string>? TrackIds, string? CreateMode, List<ChatMessage> Messages);
public record AdvancedData(string Lyrics, string Styles, string Title);
public record ChatResponse(string? Simple = null, AdvancedData? Advanced = null, List<string>? Hooks = null, string? Reply = null);

public sealed class AvomosPlugin
{
    [KernelFunction("simple")]
    [Description("Generate a simple mode prompt for Suno AI.")]
    public string Simple(
        [Description("The full generation prompt")] string prompt
    ) => prompt;

    [KernelFunction("advanced")]
    [Description("Generate lyrics, style tags, and title for a Suno AI track.")]
    public string Advanced(
        [Description("The full lyrics text")] string lyrics,
        [Description("Style tags separated by commas")] string styles,
        [Description("The track title")] string title
    ) => JsonSerializer.Serialize(new { lyrics, styles, title });

    [KernelFunction("hooks")]
    [Description("Suggest creative hooks or ideas as short phrases.")]
    public string Hooks(
        [Description("Array of hook phrases, each 1-3 words")] List<string> hooks
    ) => JsonSerializer.Serialize(new { hooks });

    [KernelFunction("reply")]
    [Description("Respond with plain text when user is just chatting.")]
    public string Reply(
        [Description("The text response")] string text
    ) => text;
}

public static class ChatEndpoint
{
    public static void Map(WebApplication app)
    {
        app.MapPost("/chat", async (ChatRequest req, Kernel kernel, ILogger<Program> logger, IHttpClientFactory httpFactory) =>
        {
            var chat = kernel.GetRequiredService<IChatCompletionService>();
            var history = new ChatHistory();
            var context = new List<string>();

            if (req.TrackIds?.Count > 0)
            {
                foreach (var id in req.TrackIds)
                {
                    var track = await FetchTrack(httpFactory, id);
                    if (track != null)
                    {
                        var parts = new List<string>();
                        if (track.TryGetValue("title", out var t) && t is string ts && !string.IsNullOrWhiteSpace(ts))
                            parts.Add($"title: {ts}");
                        if (track.TryGetValue("model", out var m) && m is string ms && !string.IsNullOrWhiteSpace(ms))
                            parts.Add($"model: {ms}");
                        if (track.TryGetValue("styles", out var s) && s is string ss && !string.IsNullOrWhiteSpace(ss))
                            parts.Add($"style: {ss}");
                        if (track.TryGetValue("plays", out var p))
                            parts.Add($"plays: {p}");
                        if (track.TryGetValue("is_public", out var pub))
                            parts.Add($"public: {pub}");
                        if (track.TryGetValue("content", out var c) && c is string ct && !string.IsNullOrWhiteSpace(ct))
                            parts.Add($"content: {ct}");
                        if (parts.Count > 0)
                            context.Add(string.Join(" | ", parts));
                    }
                }
            }

            var systemMsg = @"You are a helpful assistant for Suno AI music metadata. You must respond with valid JSON only, no markdown, no code fences.

RESPONSE FORMAT — choose exactly ONE of these structures:

1. For casual chat or questions:
{ ""reply"": ""your text response"" }

2. For Simple mode generation prompt:
{ ""simple"": ""Idea: ... Styles: ..."" }

3. For Advanced mode track creation:
{ ""advanced"": { ""lyrics"": ""..."" , ""styles"": ""..."" , ""title"": ""..."" } }

4. For hooks/ideas (short phrases, each 1-3 words):
{ ""hooks"": [""phrase1"", ""phrase2"", ""phrase3""] }

Mode rules:
- Simple mode on Suno → use simple
- Advanced mode on Suno → use advanced
- If user asks for ideas/suggestions → use hooks
- General chat or questions → use reply

When generating lyrics, analyze style tags of tracks in the buffer and use a similar aesthetic. Do NOT mention other tracks from the buffer in your response.";

            if (context.Count > 0)
            {
                systemMsg += "\n\nCurrently selected tracks:\n";
                systemMsg += string.Join("\n---\n", context);
                systemMsg += "\n\nIf the user asks about a track that was previously discussed but is no longer in the list above, explain that you no longer have access to that track.";
            }
            else
            {
                systemMsg += "\n\nNo tracks are currently selected. The user may ask general questions or search for tracks to add.";
            }
            if (!string.IsNullOrWhiteSpace(req.CreateMode))
                systemMsg += $"\nUser is currently on {req.CreateMode} mode on Suno create page.";

            history.AddSystemMessage(systemMsg);

            foreach (var m in req.Messages)
            {
                if (m.Role == "user")
                {
                    history.AddUserMessage(m.Content ?? "");
                }
                else if (m.Role == "assistant")
                {
                    var summary = new List<string>();
                    if (m.Reply is not null) summary.Add(m.Reply);
                    if (m.Simple is not null) summary.Add($"simple prompt: {m.Simple}");
                    if (m.Advanced is not null) summary.Add($"advanced — lyrics, styles: {m.Advanced.Styles}, title: {m.Advanced.Title}");
                    if (m.Hooks?.Count > 0) summary.Add($"hooks: {string.Join(", ", m.Hooks)}");
                    history.AddAssistantMessage(summary.Count > 0 ? string.Join("\n", summary) : (m.Content ?? "..."));
                }
            }

            var reply = await chat.GetChatMessageContentAsync(history);
            var raw = (reply.Content ?? "").Trim();
            logger.LogInformation("[Chat] reply: {Len} chars", raw.Length);

            var response = new ChatResponse();
            try
            {
                var parsed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(raw);
                if (parsed == null) return Results.Ok(response);

                if (parsed.TryGetValue("reply", out var replyEl))
                    response = response with { Reply = replyEl.GetString() };

                if (parsed.TryGetValue("simple", out var simpleEl))
                    response = response with { Simple = simpleEl.GetString() };

                if (parsed.TryGetValue("advanced", out var advEl) && advEl.ValueKind == JsonValueKind.Object)
                {
                    var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var adv = JsonSerializer.Deserialize<AdvancedData>(advEl.GetRawText(), opts);
                    if (adv != null)
                        response = response with { Advanced = adv };
                }

                if (parsed.TryGetValue("hooks", out var hooksEl) && hooksEl.ValueKind == JsonValueKind.Array)
                {
                    var hooks = JsonSerializer.Deserialize<List<string>>(hooksEl.GetRawText());
                    response = response with { Hooks = hooks };
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to parse chat response");
            }

            return Results.Ok(response);
        });
    }

    private static async Task<Dictionary<string, object?>?> FetchTrack(IHttpClientFactory httpFactory, string originId)
    {
        try
        {
            var client = httpFactory.CreateClient("qdrant");
            var filter = new { must = new[] { new { key = "origin_id", match = new { value = originId } } } };
            var body = new { filter, limit = 1, with_payload = true, with_vector = false };
            var resp = await client.PostAsJsonAsync("/collections/lyrics/points/scroll", body);
            var wrapper = await resp.Content.ReadFromJsonAsync<ScrollWrapper>();
            var pt = wrapper?.Result?.Points?.FirstOrDefault();
            var rawPayload = pt != null && pt.TryGetValue("payload", out var pje) ? pje.GetRawText() : null;
            if (rawPayload == null) return null;
            var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(rawPayload);
            if (dict == null) return null;
            var result = new Dictionary<string, object?>();
            foreach (var (k, v) in dict)
                result[k] = v.ValueKind switch
                {
                    JsonValueKind.String => v.GetString(),
                    JsonValueKind.Number => v.TryGetInt64(out var i) ? (object)i : v.GetDouble(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    _ => v.ToString()
                };
            return result;
        }
        catch
        {
            return null;
        }
    }

    private class ScrollWrapper
    {
        [JsonPropertyName("result")]
        public ScrollInner? Result { get; set; }
    }

    private class ScrollInner
    {
        [JsonPropertyName("points")]
        public List<Dictionary<string, JsonElement>>? Points { get; set; }
    }
}
