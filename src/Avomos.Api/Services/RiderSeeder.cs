using System.Text.Json;
using Avomos.Api.Models;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace Avomos.Api.Services;

public class RiderSeeder
{
    private readonly EmbeddingService _embeddings;
    private readonly QdrantClient _qdrant;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public RiderSeeder(EmbeddingService embeddings, QdrantClient qdrant)
    {
        _embeddings = embeddings;
        _qdrant = qdrant;
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
        var collections = await _qdrant.ListCollectionsAsync(ct);
        if (collections.Contains(RiderDocument.Collection))
        {
            var info = await _qdrant.GetCollectionInfoAsync(RiderDocument.Collection, ct);
            if (info.PointsCount > 0) return;
            await _qdrant.DeleteCollectionAsync(RiderDocument.Collection, cancellationToken: ct);
        }

        await _qdrant.CreateCollectionAsync(RiderDocument.Collection, RiderDocument.VectorConfig, cancellationToken: ct);

        var defaults = await LoadDefaultsAsync();
        foreach (var rider in defaults)
        {
            var embedText = string.IsNullOrWhiteSpace(rider.DetailedStyle) ? rider.ShortStyle : rider.DetailedStyle;
            var vec = await _embeddings.EmbedCachedAsync(embedText, "rider", ct);
            var pt = RiderDocument.ToPoint(rider, vec);
            await _qdrant.UpsertAsync(RiderDocument.Collection, [pt], cancellationToken: ct);
        }
    }
}
