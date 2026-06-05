using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Avomos.Api.Infrastructure;
using Avomos.Api.Models;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Avomos.Api.Services;

public class RiderService
{
    private readonly EmbeddingService _embeddings;
    private readonly IHttpClientFactory _httpFactory;
    private readonly Kernel _kernel;
    private static readonly string _createRiderPrompt;

    static RiderService()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Prompts", "CreateRider.md");
        _createRiderPrompt = File.Exists(path)
            ? File.ReadAllText(path)
            : "You are a Suno AI music style analyst. Create a new style rider.";
    }

    public RiderService(EmbeddingService embeddings, IHttpClientFactory httpFactory, Kernel kernel)
    {
        _embeddings = embeddings;
        _httpFactory = httpFactory;
        _kernel = kernel;
    }

    public async Task<MatchResult> MatchRidersAsync(List<string> trackIds, int limit = 6, double threshold = 0.0)
    {
        if (trackIds.Count < 3)
            return new MatchResult([], false, null, null);

        var tracks = await FetchTrackPayloadsAsync(trackIds);
        if (tracks.Count < 3)
            return new MatchResult([], false, null, null);

        var styles = tracks
            .Select(t => t.GetValueOrDefault("styles") as string)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Cast<string>()
            .OrderBy(s => s)
            .ToList();

        var combined = string.Join(" ", styles);
        var queryVec = await _embeddings.EmbedCachedAsync(combined, "rider-match");

        var riders = await SearchRidersRawAsync(queryVec, null, limit, threshold);

        var (canCreate, similarity, outlierOriginId) = await CheckCoherenceAsync(tracks, threshold);

        return new MatchResult(riders, canCreate, similarity, outlierOriginId);
    }

    private async Task<(bool CanCreate, double? Similarity, string? OutlierOriginId)> CheckCoherenceAsync(List<Dictionary<string, object?>> tracks, double threshold)
    {
        var vecs = new List<(float[] Vec, string OriginId)>();
        foreach (var t in tracks)
        {
            var style = t.GetValueOrDefault("styles") as string;
            if (string.IsNullOrWhiteSpace(style)) continue;
            var originId = t.GetValueOrDefault("origin_id") as string ?? "";
            var vec = await _embeddings.EmbedCachedAsync(style, "styles", CancellationToken.None);
            vecs.Add((vec, originId));
        }

        if (vecs.Count < 3)
            return (false, null, null);

        var centroid = new float[vecs[0].Vec.Length];
        foreach (var (v, _) in vecs)
            for (var i = 0; i < centroid.Length; i++)
                centroid[i] += v[i];
        for (var i = 0; i < centroid.Length; i++)
            centroid[i] /= vecs.Count;

        var minSim = 1.0;
        var outlierOriginId = (string?)null;
        foreach (var (v, originId) in vecs)
        {
            var sim = CosineSimilarity(v, centroid);
            if (sim < minSim)
            {
                minSim = sim;
                outlierOriginId = originId;
            }
        }

        return (minSim >= threshold, Math.Round(minSim, 3), outlierOriginId);
    }

    private static double CosineSimilarity(float[] a, float[] b)
    {
        double dot = 0, na = 0, nb = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            na += a[i] * a[i];
            nb += b[i] * b[i];
        }
        var denom = Math.Sqrt(na) * Math.Sqrt(nb);
        return denom > 0 ? dot / denom : 0;
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
            .OrderBy(s => s)
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

        var prompt = _createRiderPrompt + $"\n\nTRACKS:\n{trackSummary}\n\n{defaultSummary}\n\n{(customRiders.Count > 0 ? $"EXISTING CUSTOM RIDERS (replace only at very high similarity):\n{customSummary}" : "")}";

        var chat = _kernel.GetRequiredService<IChatCompletionService>();
        var history = new ChatHistory();
        history.AddSystemMessage(prompt);

        var reply = await chat.GetChatMessageContentAsync(history);
        var raw = (reply.Content ?? "").Trim();
        logger?.LogInformation("[CreateRider] LLM reply: {Len} chars", raw.Length);

        var rawForParse = raw;
        var jsonBlock = Regex.Match(raw, @"```(?:json)?\s*(\{[\s\S]*?\})\s*```", RegexOptions.Multiline);
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

        var embedText = string.IsNullOrWhiteSpace(rider.DetailedStyle) ? rider.ShortStyle : rider.DetailedStyle;
        var embedVec = await _embeddings.EmbedCachedAsync(embedText, "rider");
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
                vector = new { name = RiderDocument.VectorName, vector = queryVec },
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
                vector = new { name = RiderDocument.VectorName, vector = queryVec },
                limit,
                with_payload = true,
                with_vector = false,
                score_threshold = threshold
            };
        }

        var resp = await client.PostAsJsonAsync($"/collections/{RiderDocument.Collection}/points/search", searchBody);
        if (!resp.IsSuccessStatusCode) return [];

        var wrapper = await resp.Content.ReadFromJsonAsync<QdrantSearchResult>();
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
            var wrapper = await resp.Content.ReadFromJsonAsync<QdrantScrollResponse>();
            var pt = wrapper?.Result?.Points?.FirstOrDefault();
            if (pt == null) return null;
            return PayloadToDict(pt.Payload);
        }
        catch { return null; }
    }

    private static Dictionary<string, object?> PayloadToDict(Dictionary<string, JsonElement>? payload)
    {
        var result = new Dictionary<string, object?>();
        if (payload is null) return result;
        foreach (var (k, v) in payload)
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

    private static MatchedRider? JsonPayloadToRider(Dictionary<string, JsonElement> p)
    {
        try
        {
            return new MatchedRider(
                RiderId: QdrantPayload.String(p, "rider_id"),
                Type: QdrantPayload.String(p, "type") is { Length: >0 } t ? t : "custom",
                Name: QdrantPayload.String(p, "name"),
                Score: 0.0,
                Model: QdrantPayload.String(p, "model"),
                Tempo: QdrantPayload.String(p, "tempo"),
                Weirdness: QdrantPayload.String(p, "weirdness"),
                StyleInfluence: QdrantPayload.String(p, "style_influence"),
                ShortStyle: QdrantPayload.String(p, "short_style"),
                DetailedStyle: QdrantPayload.String(p, "detailed_style"),
                Exclude: QdrantPayload.String(p, "exclude"),
                LyricsTemplate: QdrantPayload.String(p, "lyrics_template")
            );
        }
        catch { return null; }
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

public record MatchResult(List<MatchedRider> Riders, bool CanCreate, double? Similarity, string? OutlierTrackId);

public record CreateRiderOutput(string RiderId, string Status, string? Message);
