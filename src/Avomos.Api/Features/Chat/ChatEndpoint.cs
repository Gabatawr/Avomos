using System.ComponentModel;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Avomos.Api.Services;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Avomos.Api.Features.Chat;

public record ChatMessage(string Role, string Content, string? Simple = null, AdvancedData? Advanced = null, List<string>? Hooks = null, string? Reply = null);
public record ChatRequest(List<string>? TrackIds, string? CreateMode, List<ChatMessage> Messages, double? RidersThreshold = null);
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
    private static readonly string _systemPrompt;

    static ChatEndpoint()
    {
        var promptsDir = Path.Combine(AppContext.BaseDirectory, "Prompts");
        _systemPrompt = File.Exists(Path.Combine(promptsDir, "SystemPrompt.txt"))
            ? File.ReadAllText(Path.Combine(promptsDir, "SystemPrompt.txt"))
            : "You are a helpful assistant for Suno AI music metadata.";
    }

    public static void Map(WebApplication app)
    {
        app.MapPost("/chat", async (ChatRequest req, Kernel kernel, ILogger<Program> logger, IHttpClientFactory httpFactory, RiderService riderSvc) =>
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

            var systemMsg = _systemPrompt;

            // Match riders dynamically from Qdrant based on buffer (max 3 in prompt)
            if (req.TrackIds?.Count >= 3)
            {
                var matchResult = await riderSvc.MatchRidersAsync(req.TrackIds, threshold: req.RidersThreshold ?? 0.0);
                var matchedRiders = matchResult.Riders.Take(3).ToList();
                systemMsg += "\n\n=== RIDERS ===";
                if (matchedRiders.Count > 0)
                {
                    foreach (var rider in matchedRiders)
                    {
                        systemMsg += $"\n\n--- {rider.Name} ({rider.Type}) ---";
                        systemMsg += $"\nModel: {rider.Model} | Tempo: {rider.Tempo}";
                        if (!string.IsNullOrWhiteSpace(rider.Weirdness))
                            systemMsg += $" | Weirdness: {rider.Weirdness}";
                        if (!string.IsNullOrWhiteSpace(rider.StyleInfluence))
                            systemMsg += $" | Style Influence: {rider.StyleInfluence}";
                        systemMsg += $"\nShort Style: {rider.ShortStyle}";
                        systemMsg += $"\nDetailed Style: {rider.DetailedStyle}";
                        systemMsg += $"\nExclude: {rider.Exclude}";
                    }
                }
                else
                {
                    systemMsg += "\n\nNo riders matched the current track selection. Riders are reusable style templates — the user can create new ones based on their tracks.";
                }
            }

            systemMsg += "\n\n=== TRACKS ===";

            if (context.Count > 0)
            {
                systemMsg += "\n\n" + string.Join("\n---\n", context);
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
                    var obj = new Dictionary<string, object?>();
                    if (m.Reply is not null) obj["reply"] = m.Reply;
                    if (m.Simple is not null) obj["simple"] = m.Simple;
                    if (m.Advanced is not null) obj["advanced"] = m.Advanced;
                    if (m.Hooks?.Count > 0) obj["hooks"] = m.Hooks;
                    history.AddAssistantMessage(obj.Count > 0
                        ? JsonSerializer.Serialize(obj)
                        : (m.Content ?? "..."));
                }
            }

            var reply = await chat.GetChatMessageContentAsync(history);
            var raw = (reply.Content ?? "").Trim();
            logger.LogInformation("[Chat] reply: {Len} chars", raw.Length);

            var response = new ChatResponse();
            var rawForParse = raw;

            var jsonBlock = Regex.Match(raw, @"```(?:json)?\s*(\{[\s\S]*?\})\s*```", RegexOptions.Multiline);
            if (jsonBlock.Success)
                rawForParse = jsonBlock.Groups[1].Value;

            try
            {
                var parsed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(rawForParse);
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
                logger.LogWarning(ex, "Failed to parse chat response, raw={Raw}", raw);
            }

            if (response.Reply is null && response.Simple is null && response.Advanced is null && response.Hooks is null)
                response = response with { Reply = raw.Trim() };

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
