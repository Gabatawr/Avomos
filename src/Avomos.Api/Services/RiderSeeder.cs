using System.Text.Json;
using Avomos.Api.Infrastructure;
using Avomos.Api.Models;
using Microsoft.Extensions.Logging;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace Avomos.Api.Services;

public class RiderSeeder
{
    private readonly EmbeddingService _embeddings;
    private readonly QdrantClient _qdrant;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<RiderSeeder> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public RiderSeeder(EmbeddingService embeddings, QdrantClient qdrant, IHttpClientFactory httpFactory, ILogger<RiderSeeder> logger)
    {
        _embeddings = embeddings;
        _qdrant = qdrant;
        _httpFactory = httpFactory;
        _logger = logger;
    }

    public static async Task<RiderData[]> LoadDefaultsAsync()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Data", "default-riders.json");
        if (!File.Exists(path))
            return [];
        var json = await File.ReadAllTextAsync(path);
        return JsonSerializer.Deserialize<RiderData[]>(json, JsonOpts) ?? [];
    }

    public async Task SeedIfEmptyAsync(CancellationToken ct = default)
    {
        var fileDefaults = await LoadDefaultsAsync();
        if (fileDefaults.Length == 0) return;

        var collections = await _qdrant.ListCollectionsAsync(ct);
        if (!collections.Contains(RiderDocument.Collection))
        {
            _logger.LogInformation("Riders collection doesn't exist, creating and seeding {Count} defaults", fileDefaults.Length);
            await _qdrant.CreateCollectionAsync(RiderDocument.Collection, RiderDocument.VectorConfig, cancellationToken: ct);
        }

        var existing = await ScrollDefaultsAsync(fileDefaults.Length + 10, ct);
        var fileByOrder = fileDefaults.ToDictionary(r => r.SortOrder);
        var existingByOrder = new Dictionary<int, (Guid PointId, RiderData Rider)>();
        foreach (var (id, rider) in existing)
        {
            if (!existingByOrder.ContainsKey(rider.SortOrder))
                existingByOrder[rider.SortOrder] = (id, rider);
        }

        var toDelete = new List<Guid>();
        var toUpsert = new List<RiderData>();

        foreach (var (order, (ptId, qRider)) in existingByOrder)
        {
            if (!fileByOrder.TryGetValue(order, out var fRider) || fRider != qRider)
                toDelete.Add(ptId);
        }

        foreach (var (order, fRider) in fileByOrder)
        {
            if (!existingByOrder.TryGetValue(order, out var pair) || pair.Rider != fRider)
                toUpsert.Add(fRider);
        }

        if (toDelete.Count == 0 && toUpsert.Count == 0)
            return;

        _logger.LogInformation("Default riders changed — deleting {Del} old, upserting {Up} new", toDelete.Count, toUpsert.Count);

        foreach (var id in toDelete)
            await _qdrant.DeleteAsync(RiderDocument.Collection, id, cancellationToken: ct);

        foreach (var rider in toUpsert)
        {
            var vec = await _embeddings.EmbedCachedAsync(RiderDocument.BuildEmbedText(rider), "rider", ct);
            var pt = RiderDocument.ToPoint(rider, vec);
            await _qdrant.UpsertAsync(RiderDocument.Collection, [pt], cancellationToken: ct);
        }

        _logger.LogInformation("Default riders sync complete: {Count} riders in Qdrant", fileDefaults.Length);
    }

    private async Task<List<(Guid PointId, RiderData Rider)>> ScrollDefaultsAsync(int limit, CancellationToken ct)
    {
        var client = _httpFactory.CreateClient("qdrant");
        var body = new
        {
            filter = new { must = new[] { new { key = "type", match = new { value = "default" } } } },
            limit,
            with_payload = true,
            with_vector = false
        };

        var resp = await client.PostAsJsonAsync($"/collections/{RiderDocument.Collection}/points/scroll", body, ct);
        if (!resp.IsSuccessStatusCode) return [];

        var wrapper = await resp.Content.ReadFromJsonAsync<QdrantScrollResponse>(cancellationToken: ct);
        var points = wrapper?.Result?.Points ?? [];

        return points
            .Where(p => p.Payload != null)
            .Select(p => (
                p.Id,
                RiderDocument.FromPayload(
                    p.Payload!.ToDictionary(kv => kv.Key, kv => FromJsonElement(kv.Value)))
            ))
            .ToList();
    }

    private static object? FromJsonElement(JsonElement el)
    {
        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Number => el.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }
}
