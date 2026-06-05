using Qdrant.Client.Grpc;

namespace Avomos.Api.Models;

public record RiderData(
    string RiderId,
    string Type,      // "default" | "custom"
    string Name,
    int SortOrder,
    string Model,
    string Tempo,
    string Weirdness,
    string StyleInfluence,
    string ShortStyle,
    string DetailedStyle,
    string Exclude,
    string LyricsTemplate
);

public static class RiderDocument
{
    public const int VectorSize = 2048;
    public const string Collection = "riders";
    public const string VectorName = "style_vec";

    public static VectorParamsMap VectorConfig => new()
    {
        Map =
        {
            [VectorName] = new VectorParams { Size = VectorSize, Distance = Distance.Cosine }
        }
    };

    public static PointStruct ToPoint(RiderData rider, float[] vec) => new()
    {
        Id = new PointId { Uuid = rider.RiderId },
        Vectors = new Vectors
        {
            Vectors_ = new NamedVectors
            {
                Vectors = { [VectorName] = new Vector { Dense = new DenseVector { Data = { vec } } } }
            }
        },
        Payload =
        {
            ["rider_id"] = rider.RiderId,
            ["type"] = rider.Type,
            ["name"] = rider.Name,
            ["sort_order"] = (double)rider.SortOrder,
            ["model"] = rider.Model,
            ["tempo"] = rider.Tempo,
            ["weirdness"] = rider.Weirdness,
            ["style_influence"] = rider.StyleInfluence,
            ["short_style"] = rider.ShortStyle,
            ["detailed_style"] = rider.DetailedStyle,
            ["exclude"] = rider.Exclude,
            ["lyrics_template"] = rider.LyricsTemplate
        }
    };

    public static string BuildEmbedText(RiderData r) =>
        $"{r.ShortStyle} | {r.DetailedStyle} | Tempo: {r.Tempo} | Style influence: {r.StyleInfluence}";

    public static RiderData FromPayload(Dictionary<string, object?> payload) => new(
        RiderId: (string)(payload.GetValueOrDefault("rider_id") ?? ""),
        Type: (string)(payload.GetValueOrDefault("type") ?? "custom"),
        Name: (string)(payload.GetValueOrDefault("name") ?? ""),
        SortOrder: (int)(payload.GetValueOrDefault("sort_order") is double d ? d : 0),
        Model: (string)(payload.GetValueOrDefault("model") ?? ""),
        Tempo: (string)(payload.GetValueOrDefault("tempo") ?? ""),
        Weirdness: (string)(payload.GetValueOrDefault("weirdness") ?? ""),
        StyleInfluence: (string)(payload.GetValueOrDefault("style_influence") ?? ""),
        ShortStyle: (string)(payload.GetValueOrDefault("short_style") ?? ""),
        DetailedStyle: (string)(payload.GetValueOrDefault("detailed_style") ?? ""),
        Exclude: (string)(payload.GetValueOrDefault("exclude") ?? ""),
        LyricsTemplate: (string)(payload.GetValueOrDefault("lyrics_template") ?? "")
    );
}
