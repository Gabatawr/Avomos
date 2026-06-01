using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avomos.Api.Models;
using Avomos.Api.Services;
using MediatR;
using Qdrant.Client;

namespace Avomos.Api.Features.Lyrics;

public record TrackImport(
    string OriginId,
    string Title,
    string? Lyrics,
    string? Model,
    string? Styles,
    int? Plays,
    bool? IsPublic,
    string? CreatedAt,
    string? ImageUrl);

public record UpsertTracksCommand(List<TrackImport> Tracks) : IRequest<UpsertTracksResult>;

public record TrackImportResult(string OriginId, string? Title, string? Action, string? Error);

public record UpsertTracksResult(int Total, int Imported, int Updated, int Failed, List<TrackImportResult> Items);

public class UpsertTracksHandler(
    IHttpClientFactory httpFactory,
    EmbeddingService embeddings,
    QdrantClient qdrant,
    ILogger<UpsertTracksHandler> logger)
    : IRequestHandler<UpsertTracksCommand, UpsertTracksResult>
{
    public async Task<UpsertTracksResult> Handle(UpsertTracksCommand command, CancellationToken ct)
    {
        var imported = 0;
        var updated = 0;
        var failed = 0;
        var items = new List<TrackImportResult>();

        foreach (var track in command.Tracks)
        {
            try
            {
                var (action, title) = await UpsertOne(track, ct);
                items.Add(new TrackImportResult(track.OriginId, title, action, null));
                if (action == "imported") imported++;
                else if (action == "updated") updated++;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Upsert failed for {OriginId}", track.OriginId);
                items.Add(new TrackImportResult(track.OriginId, null, null, ex.Message));
                failed++;
            }
        }

        return new UpsertTracksResult(command.Tracks.Count, imported, updated, failed, items);
    }

    private async Task<(string action, string? title)> UpsertOne(TrackImport track, CancellationToken ct)
    {
        var existingId = await FindByOriginId(track.OriginId, ct);

        Guid pointId;
        if (existingId.HasValue)
        {
            pointId = existingId.Value;
        }
        else
        {
            pointId = PointIdFromOrigin(track.OriginId);
        }

        var title = track.Title;
        var lyrics = track.Lyrics ?? "";
        var model = track.Model ?? "";
        var styles = track.Styles ?? "";
        var plays = track.Plays ?? 0;
        var isPublic = track.IsPublic ?? true;
        var createdAt = ParseCreatedAt(track.CreatedAt);
        var imageUrl = track.ImageUrl ?? "";

        var titleLyricsVec = await embeddings.EmbedCachedAsync($"{title}\n{lyrics}", "title_lyrics", ct);

        var stylesVec = string.IsNullOrWhiteSpace(styles)
            ? titleLyricsVec
            : await embeddings.EmbedCachedAsync(styles, "styles", ct);

        var lyric = new Lyric
        {
            Id = pointId,
            OriginId = track.OriginId,
            Title = title,
            Lyrics = lyrics,
            Styles = styles,
            CreatedAt = createdAt,
            Url = string.IsNullOrWhiteSpace(track.OriginId) ? "" : $"https://suno.com/song/{track.OriginId}",
            Plays = plays,
            Model = model,
            IsPublic = isPublic,
            ImageUrl = imageUrl
        };

        var point = LyricDocument.ToPoint(lyric, titleLyricsVec, stylesVec);
        await qdrant.UpsertAsync(LyricDocument.Collection, [point], cancellationToken: ct);

        var action = existingId.HasValue ? "updated" : "imported";
        return (action, title);
    }

    private static Guid PointIdFromOrigin(string originId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(originId));
        return new Guid(hash[..16]);
    }

    private static DateTime ParseCreatedAt(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return DateTime.MinValue;
        if (DateTime.TryParse(value, out var dt)) return dt;
        return DateTime.MinValue;
    }

    private async Task<Guid?> FindByOriginId(string originId, CancellationToken ct)
    {
        var client = httpFactory.CreateClient("qdrant");
        var filter = new { must = new[] { new { key = "origin_id", match = new { value = originId } } } };
        var body = new { filter, limit = 1, with_payload = false, with_vector = false };
        var resp = await client.PostAsJsonAsync(
            $"/collections/{LyricDocument.Collection}/points/scroll", body, ct);
        resp.EnsureSuccessStatusCode();

        var result = await resp.Content.ReadFromJsonAsync<ScrollResponse>(ct);
        var point = result?.Result?.Points?.FirstOrDefault();
        return point?.Id;
    }

    private class ScrollResponse
    {
        [JsonPropertyName("result")]
        public ScrollResult? Result { get; set; }
    }

    private class ScrollResult
    {
        [JsonPropertyName("points")]
        public List<PointRef>? Points { get; set; }
    }

    private class PointRef
    {
        [JsonPropertyName("id")]
        public Guid Id { get; set; }
    }
}
