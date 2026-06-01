using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avomos.Api.Models;
using Avomos.Api.Services;
using MediatR;

namespace Avomos.Api.Features.Lyrics;

public record TrackMetadata(
    string OriginId, string? Title, int? Plays, bool? IsPublic, string? Lyrics, string? ImageUrl);

public record UpdateMetadataCommand(List<TrackMetadata> Tracks) : IRequest<UpdateMetadataResult>;

public record UpdateMetadataResult(int Updated, int NotFound);

public class UpdateMetadataHandler(
    IHttpClientFactory httpFactory,
    EmbeddingService embeddings,
    ILogger<UpdateMetadataHandler> logger)
    : IRequestHandler<UpdateMetadataCommand, UpdateMetadataResult>
{
    public async Task<UpdateMetadataResult> Handle(UpdateMetadataCommand command, CancellationToken ct)
    {
        var updated = 0;
        var notFound = 0;

        foreach (var track in command.Tracks)
        {
            var exists = await PointExists(track.OriginId, ct);
            if (!exists)
            {
                notFound++;
                continue;
            }

            var payload = new Dictionary<string, object>();

            if (track.Plays.HasValue)
                payload["plays"] = track.Plays.Value;

            if (track.IsPublic.HasValue)
                payload["is_public"] = track.IsPublic.Value;

            if (!string.IsNullOrWhiteSpace(track.Title))
                payload["title"] = track.Title;

            if (track.Lyrics is not null)
                payload["lyrics"] = track.Lyrics;

            if (track.ImageUrl is not null)
                payload["image_url"] = track.ImageUrl;

            logger.LogInformation("Updating {OriginId}: plays={Plays} title={Title} lyrics={Lyrics} image={Image}",
                track.OriginId, track.Plays, track.Title ?? "", track.Lyrics is not null, track.ImageUrl is not null);

            await SetPayload(track.OriginId, payload, ct);

            if (track.Title is not null || track.Lyrics is not null)
                await UpdateTitleLyricsVec(track.OriginId, track.Title, track.Lyrics, ct);

            updated++;
        }

        return new UpdateMetadataResult(updated, notFound);
    }

    private HttpClient Qdrant() => httpFactory.CreateClient("qdrant");

    private async Task<bool> PointExists(string originId, CancellationToken ct)
    {
        var filter = new { must = new[] { new { key = "origin_id", match = new { value = originId } } } };
        var body = new { filter, limit = 1, with_payload = false, with_vector = false };
        var resp = await Qdrant().PostAsJsonAsync($"/collections/{LyricDocument.Collection}/points/scroll", body, ct);
        var result = await resp.Content.ReadFromJsonAsync<ScrollResponse>(ct);
        return result?.Result?.Points?.Count > 0;
    }

    private async Task SetPayload(string originId, Dictionary<string, object> payload, CancellationToken ct)
    {
        var filter = new { must = new[] { new { key = "origin_id", match = new { value = originId } } } };
        var body = new { filter, payload };
        var resp = await Qdrant().PostAsJsonAsync(
            $"/collections/{LyricDocument.Collection}/points/payload?wait=true", body, ct);
        resp.EnsureSuccessStatusCode();
    }

    private async Task UpdateTitleLyricsVec(string originId, string? newTitle, string? newLyrics, CancellationToken ct)
    {
        var point = await FetchPoint(originId, ct);
        if (point is null) return;

        var title = newTitle ?? GetPayloadString(point.Payload, LyricDocument.PayloadKeys.Title);
        var lyrics = newLyrics ?? GetPayloadString(point.Payload, LyricDocument.PayloadKeys.Lyrics);
        var embedText = string.IsNullOrWhiteSpace(lyrics) ? title : $"{title}\n{lyrics}";
        var vec = await embeddings.EmbedCachedAsync(embedText, "title_lyrics", ct);

        var updateBody = new
        {
            points = new[]
            {
                new
                {
                    id = point.Id,
                    vector = new Dictionary<string, float[]>
                    {
                        [LyricDocument.TitleLyricsVec] = vec
                    }
                }
            }
        };

        var resp = await Qdrant().PutAsJsonAsync(
            $"/collections/{LyricDocument.Collection}/points/vectors", updateBody, ct);
        resp.EnsureSuccessStatusCode();
    }

    private async Task<FetchPointResult?> FetchPoint(string originId, CancellationToken ct)
    {
        var filter = new { must = new[] { new { key = "origin_id", match = new { value = originId } } } };
        var body = new { filter, limit = 1, with_payload = true, with_vector = false };
        var resp = await Qdrant().PostAsJsonAsync(
            $"/collections/{LyricDocument.Collection}/points/scroll", body, ct);
        resp.EnsureSuccessStatusCode();
        var result = await resp.Content.ReadFromJsonAsync<ScrollResponse>(ct);
        return result?.Result?.Points?.FirstOrDefault();
    }

    private static string GetPayloadString(Dictionary<string, JsonElement>? payload, string key)
    {
        if (payload is null) return "";
        if (!payload.TryGetValue(key, out var value)) return "";
        return value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    }

    private class ScrollResponse
    {
        [JsonPropertyName("result")]
        public ScrollResult? Result { get; set; }
    }

    private class ScrollResult
    {
        [JsonPropertyName("points")]
        public List<FetchPointResult>? Points { get; set; }
    }

    private class FetchPointResult
    {
        [JsonPropertyName("id")]
        public Guid Id { get; set; }

        [JsonPropertyName("payload")]
        public Dictionary<string, JsonElement>? Payload { get; set; }
    }
}
