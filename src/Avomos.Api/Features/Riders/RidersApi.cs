using Avomos.Api.Services;

namespace Avomos.Api.Features.Riders;

public record MatchRidersRequest(List<string> TrackIds, double? Threshold = null);
public record CreateRiderRequest(List<string> TrackIds, double? Threshold = null);

public static class RidersApi
{
    public static void Map(WebApplication app)
    {
        app.MapPost("/riders/match", async (MatchRidersRequest req, RiderService svc) =>
        {
            var result = await svc.MatchRidersAsync(req.TrackIds, threshold: req.Threshold ?? 0.0);
            return Results.Ok(new { result.Riders, result.CanCreate, result.Similarity, result.OutlierTrackId });
        });

        app.MapPost("/riders/create", async (CreateRiderRequest req, RiderService svc, ILogger<Program> logger) =>
        {
            var result = await svc.CreateRiderFromTracksAsync(req.TrackIds, req.Threshold ?? 0.55, logger);
            return Results.Ok(result);
        });

        app.MapDelete("/riders/{id:guid}", async (Guid id, RiderService svc) =>
        {
            var success = await svc.DeleteRiderAsync(id);
            return success ? Results.Ok(new { deleted = true }) : Results.NotFound();
        });
    }
}
