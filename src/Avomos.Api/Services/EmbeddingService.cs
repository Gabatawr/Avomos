using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Avomos.Api.Services;

public class EmbeddingService
{
    private readonly HttpClient _httpClient;
    private readonly LlmCache _cache;
    private readonly string _model;

    public EmbeddingService(IConfiguration config, LlmCache cache)
    {
        _cache = cache;
        _model = config["OpenRouter:Model"]!;
        _httpClient = new HttpClient();
        _httpClient.BaseAddress = new Uri(config["OpenRouter:Endpoint"]!.TrimEnd('/') + '/');
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {config["OpenRouter:ApiKey"]}");
        _httpClient.DefaultRequestHeaders.Add("HTTP-Referer", config["OpenRouter:SiteUrl"] ?? "");
        _httpClient.DefaultRequestHeaders.Add("X-OpenRouter-Title", config["OpenRouter:SiteTitle"] ?? "");
    }

    public async Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken ct = default)
    {
        var request = new { model = _model, input = text };
        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync("embeddings", content, ct);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(ct);
        var result = JsonSerializer.Deserialize<OpenRouterEmbeddingResponse>(responseJson);

        if (result?.Data is null || result.Data.Count == 0)
            throw new InvalidOperationException("Empty embedding response");

        return result.Data[0].Embedding;
    }

    public async Task<float[]> EmbedCachedAsync(string text, string feature, CancellationToken ct = default)
    {
        var key = _cache.Key(text);
        var modelTag = Regex.Replace(_model, @"[^a-zA-Z0-9._-]", "_");
        var cacheKey = $"{feature}_{modelTag}_{key}";
        var cached = await _cache.GetAsync<float[]>("embedding", cacheKey);
        if (cached is not null) return cached;

        var vec = await GenerateEmbeddingAsync(text, ct);
        await _cache.SetAsync("embedding", cacheKey, vec);
        return vec;
    }

    private class OpenRouterEmbeddingResponse
    {
        [JsonPropertyName("data")]
        public List<EmbeddingData>? Data { get; set; }
    }

    private class EmbeddingData
    {
        [JsonPropertyName("embedding")]
        public float[] Embedding { get; set; } = [];
    }
}
