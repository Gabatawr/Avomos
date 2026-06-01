using Avomos.Api.Models;
using Avomos.Api.Services;
using MediatR;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace Avomos.Api.Features.Lyrics;

public record SemanticSearchQuery(
    string Query,
    int Limit = 10,
    int Offset = 0,
    float TitleLyricsWeight = 0.7f,
    float StylesWeight = 0.3f) : IRequest<SemanticSearchResult>;

public record SearchHit(
    Guid Id,
    string OriginId,
    string Title,
    string Url,
    DateTime CreatedAt,
    string Lyrics,
    double Score,
    int Plays,
    string Model,
    bool IsPublic,
    string Styles,
    string ImageUrl);

public record SemanticSearchResult(List<SearchHit> Hits, int Total);

public class SemanticSearchHandler(
    EmbeddingService embeddings,
    QdrantClient qdrant) : IRequestHandler<SemanticSearchQuery, SemanticSearchResult>
{
    public async Task<SemanticSearchResult> Handle(SemanticSearchQuery query, CancellationToken ct)
    {
        var vector = await embeddings.GenerateEmbeddingAsync(query.Query, ct);

        var titleLyricsHits = await qdrant.SearchAsync(LyricDocument.Collection, vector,
            vectorName: LyricDocument.TitleLyricsVec,
            limit: (ulong)(query.Limit * 2),
            cancellationToken: ct);

        List<ScoredPoint> combined;
        if (query.StylesWeight >= 0.99f)
        {
            combined = [.. await qdrant.SearchAsync(LyricDocument.Collection, vector,
                vectorName: LyricDocument.StylesVec,
                limit: (ulong)(query.Limit * 2),
                cancellationToken: ct)];
        }
        else if (query.StylesWeight > 0.01f)
        {
            var stylesHits = await qdrant.SearchAsync(LyricDocument.Collection, vector,
                vectorName: LyricDocument.StylesVec,
                limit: (ulong)(query.Limit * 2),
                cancellationToken: ct);

            combined = HybridMerge(titleLyricsHits, stylesHits, query.TitleLyricsWeight, query.StylesWeight)
                .Take(query.Limit * 2).ToList();
        }
        else
        {
            combined = [.. titleLyricsHits];
        }

        var total = combined.Count;
        var paged = combined.Skip(query.Offset).Take(query.Limit).ToList();

        var hits = paged.Select(r =>
        {
            var lyrics = PayloadString(r, LyricDocument.PayloadKeys.Lyrics);
            return new SearchHit(
                Guid.Parse(r.Id.Uuid),
                PayloadString(r, LyricDocument.PayloadKeys.OriginId),
                PayloadString(r, LyricDocument.PayloadKeys.Title),
                PayloadString(r, LyricDocument.PayloadKeys.Url),
                PayloadDateTime(r, LyricDocument.PayloadKeys.CreatedAt),
                lyrics,
                r.Score,
                PayloadInt(r, LyricDocument.PayloadKeys.Plays),
                PayloadString(r, LyricDocument.PayloadKeys.Model),
                PayloadBool(r, LyricDocument.PayloadKeys.IsPublic),
                PayloadString(r, LyricDocument.PayloadKeys.Styles),
                PayloadString(r, LyricDocument.PayloadKeys.ImageUrl));
        }).ToList();

        return new SemanticSearchResult(hits, total);
    }

    private static List<ScoredPoint> HybridMerge(
        IReadOnlyList<ScoredPoint> content, IReadOnlyList<ScoredPoint> meta,
        float wContent, float wMeta)
    {
        var merged = new Dictionary<Guid, (double score, ScoredPoint point)>();
        var sum = wContent + wMeta;
        wContent /= sum;
        wMeta /= sum;

        foreach (var p in content)
        {
            var id = Guid.Parse(p.Id.Uuid);
            merged[id] = (p.Score * wContent, p);
        }

        foreach (var p in meta)
        {
            var id = Guid.Parse(p.Id.Uuid);
            if (merged.TryGetValue(id, out var existing))
                merged[id] = (existing.score + p.Score * wMeta, p);
            else
                merged[id] = (p.Score * wMeta, p);
        }

        return [.. merged.Values.OrderByDescending(v => v.score).Select(v => v.point)];
    }

    private static string PayloadString(ScoredPoint point, string key) =>
        point.Payload.TryGetValue(key, out var v) ? v?.StringValue ?? "" : "";

    private static DateTime PayloadDateTime(ScoredPoint point, string key) =>
        point.Payload.TryGetValue(key, out var v) && DateTime.TryParse(v?.StringValue, out var dt) ? dt : DateTime.MinValue;

    private static int PayloadInt(ScoredPoint point, string key)
    {
        if (!point.Payload.TryGetValue(key, out var v)) return 0;
        if (v?.HasIntegerValue is true) return (int)v.IntegerValue;
        if (v?.HasDoubleValue is true) return (int)v.DoubleValue;
        return 0;
    }

    private static bool PayloadBool(ScoredPoint point, string key)
    {
        if (!point.Payload.TryGetValue(key, out var v)) return false;
        if (v?.HasBoolValue is true) return v.BoolValue;
        return false;
    }
}
