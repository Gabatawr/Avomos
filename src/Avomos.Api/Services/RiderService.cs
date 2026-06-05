using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avomos.Api.Models;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Avomos.Api.Services;

public class RiderService
{
    private readonly EmbeddingService _embeddings;
    private readonly IHttpClientFactory _httpFactory;
    private readonly Kernel _kernel;

    public RiderService(EmbeddingService embeddings, IHttpClientFactory httpFactory, Kernel kernel)
    {
        _embeddings = embeddings;
        _httpFactory = httpFactory;
        _kernel = kernel;
    }

    public async Task<MatchResult> MatchRidersAsync(List<string> trackIds, int limit = 6, double threshold = 0.0)
    {
        if (trackIds.Count < 3)
            return new MatchResult([], false, null);

        var styles = await FetchStylesAsync(trackIds);
        if (styles.Count < 3)
            return new MatchResult([], false, null);

        var combined = string.Join(" ", styles);
        var queryVec = await _embeddings.EmbedCachedAsync(combined, "rider-match");

        var riders = await SearchRidersRawAsync(queryVec, null, limit, threshold);

        var (canCreate, similarity) = await CheckCoherenceAsync(queryVec, styles.Count, threshold);

        return new MatchResult(riders, canCreate, similarity);
    }

    private async Task<(bool CanCreate, double? Similarity)> CheckCoherenceAsync(float[] queryVec, int trackCount, double threshold)
    {
        var client = _httpFactory.CreateClient("qdrant");
        var searchBody = new
        {
            vector = new { name = "styles_vec", vector = queryVec },
            limit = trackCount,
            with_payload = false,
            with_vector = false,
            score_threshold = threshold
        };
        var resp = await client.PostAsJsonAsync($"/collections/{LyricDocument.Collection}/points/search", searchBody);
        if (!resp.IsSuccessStatusCode)
            return (false, null);

        var wrapper = await resp.Content.ReadFromJsonAsync<SearchResult>();
        var points = wrapper?.Result ?? [];

        if (points.Count < 3)
            return (false, null);

        var avgScore = points.Average(p =>
        {
            if (p.TryGetValue("score", out var sEl) && sEl.ValueKind == JsonValueKind.Number)
                return sEl.GetDouble();
            return 0.0;
        });

        return (avgScore >= threshold, Math.Round(avgScore, 3));
    }

    public async Task<CreateRiderOutput> CreateRiderFromTracksAsync(List<string> trackIds, double threshold = 0.55, ILogger? logger = null)
    {
        if (trackIds.Count < 3)
            return new CreateRiderOutput("", "error", "Need at least 3 tracks");

        var trackPayloads = await FetchTrackPayloadsAsync(trackIds);
        if (trackPayloads.Count < 3)
            return new CreateRiderOutput("", "error", "Not enough tracks with data");

        var styles = trackPayloads
            .Select(t => t.GetValueOrDefault("styles") as string)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Cast<string>()
            .ToList();

        List<MatchedRider> defaultRiders = [];
        List<MatchedRider> customRiders = [];
        float[]? queryVec = null;

        if (styles.Count >= 3)
        {
            var combined = string.Join(" ", styles);
            queryVec = await _embeddings.EmbedCachedAsync(combined, "rider-match");

            defaultRiders = await SearchRidersRawAsync(queryVec, "default", 3, 0.0);
            customRiders = await SearchRidersRawAsync(queryVec, "custom", 3, threshold);

            // If no custom riders matched, use 6 defaults instead
            if (customRiders.Count == 0)
                defaultRiders = await SearchRidersRawAsync(queryVec, "default", 6, 0.0);
        }

        var trackSummary = string.Join("\n---\n", trackPayloads.Select(t =>
            $"title: {t.GetValueOrDefault("title") ?? ""}\nstyle: {t.GetValueOrDefault("styles") ?? ""}\nmodel: {t.GetValueOrDefault("model") ?? ""}"
        ));

        var defaultSummary = string.Join("\n\n", defaultRiders.Select(r =>
            $"DEFAULT RIDER (structure example — never modify):\nID: {r.RiderId}\nName: {r.Name}\nShort Style: {r.ShortStyle}\nDetailed Style: {r.DetailedStyle}\nModel: {r.Model}\nTempo: {r.Tempo}\nExclude: {r.Exclude}\nLyrics Template:\n{r.LyricsTemplate}"
        ));

        var customSummary = string.Join("\n\n", customRiders.Select(r =>
            $"CUSTOM RIDER (existing — replace only if the new style is nearly identical):\nID: {r.RiderId}\nName: {r.Name}\nShort Style: {r.ShortStyle}\nDetailed Style: {r.DetailedStyle}\nExclude: {r.Exclude}\nLyrics Template:\n{r.LyricsTemplate}"
        ));

        var chat = _kernel.GetRequiredService<IChatCompletionService>();
        var history = new ChatHistory();

        var prompt = $@"You are a Suno AI music style analyst. Create a new style rider based on the tracks below.

YOUR TASK:
- Analyze the tracks' style, structure, and aesthetic
- Create ONE new rider that captures their essence
- DEFAULT riders are <b>never</b> to be modified — use them only as structure examples
- CUSTOM riders may be replaced, but ONLY if your new rider is extremely similar (high confidence, minimal changes) — otherwise create a new one
- Base the rider primarily on the actual tracks in TRACKS, not on existing riders

OUTPUT FORMAT — respond with valid JSON only:
{{
  ""action"": ""create"" or ""replace"",
  ""existing_rider_id"": ""..."" or null,
  ""rider"": {{
    ""name"": ""Rider name"",
    ""model"": ""recommended model"",
    ""tempo"": ""tempo description"",
    ""weirdness"": ""0.X - 0.Y"",
    ""style_influence"": ""0.X"",
    ""short_style"": ""under 100 chars"",
    ""detailed_style"": ""up to 200 chars"",
    ""exclude"": ""comma-separated with 'no' prefix (e.g. no synths, no autotune, no reverb wash)"",
    ""lyrics_template"": ""full lyrics template starting with [Intro]""
  }}
}}

TRACKS:
{trackSummary}

{defaultSummary}

{(customRiders.Count > 0 ? $"EXISTING CUSTOM RIDERS (replace only at very high similarity):\n{customSummary}" : "")}";

        history.AddSystemMessage(prompt);

        var reply = await chat.GetChatMessageContentAsync(history);
        var raw = (reply.Content ?? "").Trim();
        logger?.LogInformation("[CreateRider] LLM reply: {Len} chars", raw.Length);

        var rawForParse = raw;
        var jsonBlock = System.Text.RegularExpressions.Regex.Match(raw, @"```(?:json)?\s*(\{[\s\S]*?\})\s*```", System.Text.RegularExpressions.RegexOptions.Multiline);
        if (jsonBlock.Success)
            rawForParse = jsonBlock.Groups[1].Value;

        RiderGenerationOutput? output;
        try
        {
            output = JsonSerializer.Deserialize<RiderGenerationOutput>(rawForParse);
        }
        catch
        {
            return new CreateRiderOutput("", "error", "Failed to parse LLM response");
        }

        if (output?.Rider == null)
            return new CreateRiderOutput("", "error", "No rider in LLM response");

        var r = output.Rider;
        var riderId = output.Action == "replace" && !string.IsNullOrWhiteSpace(output.ExistingRiderId)
            ? output.ExistingRiderId
            : Guid.NewGuid().ToString();

        var rider = new RiderData(
            RiderId: riderId,
            Type: "custom",
            Name: r.Name ?? "Custom Rider",
            SortOrder: 0,
            Model: r.Model ?? "v5.5",
            Tempo: r.Tempo ?? "",
            Weirdness: r.Weirdness ?? "",
            StyleInfluence: r.StyleInfluence ?? "",
            ShortStyle: r.ShortStyle ?? "",
            DetailedStyle: r.DetailedStyle ?? "",
            Exclude: r.Exclude ?? "",
            LyricsTemplate: r.LyricsTemplate ?? ""
        );

        var embedVec = await _embeddings.EmbedCachedAsync(rider.ShortStyle, "rider");
        var pt = RiderDocument.ToPoint(rider, embedVec);

        var qdrantClient = new Qdrant.Client.QdrantClient(LyricDocument.QdrantHost);
        await qdrantClient.UpsertAsync(RiderDocument.Collection, [pt]);

        var status = output.Action == "replace" ? "replaced" : "created";
        return new CreateRiderOutput(riderId, status, $"Rider '{r.Name}' {status} successfully");
    }

    public async Task<bool> DeleteRiderAsync(Guid id)
    {
        try
        {
            var qdrantClient = new Qdrant.Client.QdrantClient(LyricDocument.QdrantHost);
            await qdrantClient.DeleteAsync(RiderDocument.Collection, id);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task<List<MatchedRider>> SearchRidersRawAsync(float[] queryVec, string? typeFilter, int limit, double threshold)
    {
        var client = _httpFactory.CreateClient("qdrant");

        object searchBody;
        if (typeFilter != null)
        {
            searchBody = new
            {
                vector = new { name = "style_vec", vector = queryVec },
                filter = new { must = new[] { new { key = "type", match = new { value = typeFilter } } } },
                limit,
                with_payload = true,
                with_vector = false,
                score_threshold = threshold
            };
        }
        else
        {
            searchBody = new
            {
                vector = new { name = "style_vec", vector = queryVec },
                limit,
                with_payload = true,
                with_vector = false,
                score_threshold = threshold
            };
        }

        var resp = await client.PostAsJsonAsync($"/collections/{RiderDocument.Collection}/points/search", searchBody);
        if (!resp.IsSuccessStatusCode) return [];

        var wrapper = await resp.Content.ReadFromJsonAsync<SearchResult>();
        var points = wrapper?.Result ?? [];

        var riders = new List<MatchedRider>();
        foreach (var p in points)
        {
            if (!p.TryGetValue("payload", out var pje)) continue;
            var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(pje.GetRawText());
            if (dict == null) continue;

            var score = p.TryGetValue("score", out var sEl) && sEl.ValueKind == JsonValueKind.Number
                ? Math.Round(sEl.GetDouble(), 3)
                : 0.0;

            var rider = JsonPayloadToRider(dict);
            if (rider != null)
                riders.Add(rider with { Score = score });
        }

        return riders.Take(limit).ToList();
    }

    private async Task<List<string>> FetchStylesAsync(List<string> trackIds)
    {
        var styles = new List<string>();
        foreach (var id in trackIds)
        {
            var track = await FetchSinglePayloadAsync(id);
            if (track != null && track.TryGetValue("styles", out var s) && s is string ss && !string.IsNullOrWhiteSpace(ss))
                styles.Add(ss);
        }
        return styles;
    }

    private async Task<List<Dictionary<string, object?>>> FetchTrackPayloadsAsync(List<string> trackIds)
    {
        var result = new List<Dictionary<string, object?>>();
        foreach (var id in trackIds)
        {
            var track = await FetchSinglePayloadAsync(id);
            if (track != null) result.Add(track);
        }
        return result;
    }

    private async Task<Dictionary<string, object?>?> FetchSinglePayloadAsync(string originId)
    {
        try
        {
            var client = _httpFactory.CreateClient("qdrant");
            var filter = new { must = new[] { new { key = "origin_id", match = new { value = originId } } } };
            var body = new { filter, limit = 1, with_payload = true, with_vector = false };
            var resp = await client.PostAsJsonAsync("/collections/lyrics/points/scroll", body);
            var wrapper = await resp.Content.ReadFromJsonAsync<ScrollResult>();
            var pt = wrapper?.Result?.Points?.FirstOrDefault();
            if (pt == null || !pt.TryGetValue("payload", out var pje)) return null;
            var rawPayload = pje.GetRawText();
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
        catch { return null; }
    }

    private static MatchedRider JsonPayloadToRider(Dictionary<string, JsonElement> p)
    {
        try
        {
            return new MatchedRider(
                RiderId: p.GetValueOrDefault("rider_id", default(JsonElement)).GetString() ?? "",
                Type: p.GetValueOrDefault("type", default(JsonElement)).GetString() ?? "custom",
                Name: p.GetValueOrDefault("name", default(JsonElement)).GetString() ?? "",
                Score: 0.0,
                Model: p.GetValueOrDefault("model", default(JsonElement)).GetString() ?? "",
                Tempo: p.GetValueOrDefault("tempo", default(JsonElement)).GetString() ?? "",
                Weirdness: p.GetValueOrDefault("weirdness", default(JsonElement)).GetString() ?? "",
                StyleInfluence: p.GetValueOrDefault("style_influence", default(JsonElement)).GetString() ?? "",
                ShortStyle: p.GetValueOrDefault("short_style", default(JsonElement)).GetString() ?? "",
                DetailedStyle: p.GetValueOrDefault("detailed_style", default(JsonElement)).GetString() ?? "",
                Exclude: p.GetValueOrDefault("exclude", default(JsonElement)).GetString() ?? "",
                LyricsTemplate: p.GetValueOrDefault("lyrics_template", default(JsonElement)).GetString() ?? ""
            );
        }
        catch { return null; }
    }

    private class SearchResult
    {
        [JsonPropertyName("result")]
        public List<Dictionary<string, JsonElement>>? Result { get; set; }
    }

    private class ScrollResult
    {
        [JsonPropertyName("result")]
        public ScrollInner? Result { get; set; }
    }

    private class ScrollInner
    {
        [JsonPropertyName("points")]
        public List<Dictionary<string, JsonElement>>? Points { get; set; }
    }

    private class RiderGenerationOutput
    {
        [JsonPropertyName("action")] public string? Action { get; set; }
        [JsonPropertyName("existing_rider_id")] public string? ExistingRiderId { get; set; }
        [JsonPropertyName("rider")] public RiderGenerationData? Rider { get; set; }
    }

    private class RiderGenerationData
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("model")] public string? Model { get; set; }
        [JsonPropertyName("tempo")] public string? Tempo { get; set; }
        [JsonPropertyName("weirdness")] public string? Weirdness { get; set; }
        [JsonPropertyName("style_influence")] public string? StyleInfluence { get; set; }
        [JsonPropertyName("short_style")] public string? ShortStyle { get; set; }
        [JsonPropertyName("detailed_style")] public string? DetailedStyle { get; set; }
        [JsonPropertyName("exclude")] public string? Exclude { get; set; }
        [JsonPropertyName("lyrics_template")] public string? LyricsTemplate { get; set; }
    }
}

public record MatchedRider(
    string RiderId, string Type, string Name, double Score,
    string Model, string Tempo, string Weirdness, string StyleInfluence,
    string ShortStyle, string DetailedStyle, string Exclude, string LyricsTemplate
);

public record MatchResult(List<MatchedRider> Riders, bool CanCreate, double? Similarity);

public record CreateRiderOutput(string RiderId, string Status, string? Message);
