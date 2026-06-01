using Avomos.Api.Models;
using MediatR;
using Qdrant.Client;
using static Qdrant.Client.Grpc.Conditions;

namespace Avomos.Api.Features.Lyrics;

public record DeleteLyricCommand(string OriginId) : IRequest<DeleteLyricResult>;

public record DeleteLyricResult(bool Deleted, string? Error);

public class DeleteLyricHandler(QdrantClient qdrant)
    : IRequestHandler<DeleteLyricCommand, DeleteLyricResult>
{
    public async Task<DeleteLyricResult> Handle(DeleteLyricCommand command, CancellationToken ct)
    {
        var scroll = await qdrant.ScrollAsync(LyricDocument.Collection,
            filter: MatchKeyword(LyricDocument.PayloadKeys.OriginId, command.OriginId),
            limit: 1,
            cancellationToken: ct);

        var existingPoint = scroll.Result.FirstOrDefault();
        if (existingPoint is null)
            return new DeleteLyricResult(false, "Not found");

        await qdrant.DeleteAsync(LyricDocument.Collection, [existingPoint.Id], cancellationToken: ct);

        return new DeleteLyricResult(true, null);
    }
}
