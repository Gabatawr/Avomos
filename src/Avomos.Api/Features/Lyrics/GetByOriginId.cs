using Avomos.Api.Models;
using MediatR;
using Qdrant.Client;
using static Qdrant.Client.Grpc.Conditions;

namespace Avomos.Api.Features.Lyrics;

public record GetByOriginIdQuery(string OriginId) : IRequest<LyricDetail?>;

public record LyricDetail(
    Guid Id, string OriginId, string Title, string Lyrics, string Url,
    DateTime CreatedAt, string Styles,
    int Plays, string Model, bool IsPublic, string ImageUrl);

public class GetByOriginIdHandler(QdrantClient qdrant) : IRequestHandler<GetByOriginIdQuery, LyricDetail?>
{
    public async Task<LyricDetail?> Handle(GetByOriginIdQuery query, CancellationToken ct)
    {
        var scroll = await qdrant.ScrollAsync(LyricDocument.Collection,
            filter: MatchKeyword(LyricDocument.PayloadKeys.OriginId, query.OriginId),
            limit: 1,
            cancellationToken: ct);

        var point = scroll.Result.FirstOrDefault();
        if (point is null) return null;

        return new LyricDetail(
            Guid.Parse(point.Id.Uuid),
            PayloadString(point, LyricDocument.PayloadKeys.OriginId),
            PayloadString(point, LyricDocument.PayloadKeys.Title),
            PayloadString(point, LyricDocument.PayloadKeys.Lyrics),
            PayloadString(point, LyricDocument.PayloadKeys.Url),
            PayloadDateTime(point, LyricDocument.PayloadKeys.CreatedAt),
            PayloadString(point, LyricDocument.PayloadKeys.Styles),
            PayloadInt(point, LyricDocument.PayloadKeys.Plays),
            PayloadString(point, LyricDocument.PayloadKeys.Model),
            PayloadBool(point, LyricDocument.PayloadKeys.IsPublic),
            PayloadString(point, LyricDocument.PayloadKeys.ImageUrl)
        );
    }

    private static string PayloadString(Qdrant.Client.Grpc.RetrievedPoint point, string key) =>
        point.Payload.TryGetValue(key, out var v) ? v?.StringValue ?? "" : "";

    private static DateTime PayloadDateTime(Qdrant.Client.Grpc.RetrievedPoint point, string key) =>
        point.Payload.TryGetValue(key, out var v) && DateTime.TryParse(v?.StringValue, out var dt) ? dt : DateTime.MinValue;

    private static int PayloadInt(Qdrant.Client.Grpc.RetrievedPoint point, string key)
    {
        if (!point.Payload.TryGetValue(key, out var v)) return 0;
        if (v?.HasIntegerValue is true) return (int)v.IntegerValue;
        if (v?.HasDoubleValue is true) return (int)v.DoubleValue;
        return 0;
    }

    private static bool PayloadBool(Qdrant.Client.Grpc.RetrievedPoint point, string key)
    {
        if (!point.Payload.TryGetValue(key, out var v)) return false;
        if (v?.HasBoolValue is true) return v.BoolValue;
        return false;
    }
}
