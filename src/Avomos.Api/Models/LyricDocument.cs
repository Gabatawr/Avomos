using Qdrant.Client.Grpc;

namespace Avomos.Api.Models;

public static class LyricDocument
{
    public const int VectorSize = 2048;
    public const string Collection = "lyrics";
    public static string QdrantHost => Environment.GetEnvironmentVariable("QDRANT_HOST") ?? "localhost";

    public const string TitleLyricsVec = "title_lyrics_vec";
    public const string StylesVec = "styles_vec";

    public static class PayloadKeys
    {
        public const string Title = "title";
        public const string Lyrics = "lyrics";
        public const string Styles = "styles";
        public const string OriginId = "origin_id";
        public const string CreatedAt = "created_at";
        public const string Url = "url";
        public const string Plays = "plays";
        public const string Model = "model";
        public const string IsPublic = "is_public";
        public const string ImageUrl = "image_url";
    }

    public static VectorParamsMap VectorConfig => new()
    {
        Map =
        {
            [TitleLyricsVec] = new VectorParams { Size = VectorSize, Distance = Distance.Cosine },
            [StylesVec] = new VectorParams { Size = VectorSize, Distance = Distance.Cosine }
        }
    };

    public static PointStruct ToPoint(Lyric lyric, float[] contentVec, float[] metaVec) => new()
    {
        Id = lyric.Id,
        Vectors = new Vectors
        {
            Vectors_ = new NamedVectors
            {
                Vectors =
                {
                    [TitleLyricsVec] = new Vector { Dense = new DenseVector { Data = { contentVec } } },
                    [StylesVec] = new Vector { Dense = new DenseVector { Data = { metaVec } } }
                }
            }
        },
        Payload =
        {
            [PayloadKeys.Title] = lyric.Title,
            [PayloadKeys.Lyrics] = lyric.Lyrics,
            [PayloadKeys.Styles] = lyric.Styles,
            [PayloadKeys.OriginId] = lyric.OriginId,
            [PayloadKeys.CreatedAt] = lyric.CreatedAt.ToString("O"),
            [PayloadKeys.Url] = lyric.Url,
            [PayloadKeys.Plays] = (double)lyric.Plays,
            [PayloadKeys.Model] = lyric.Model,
            [PayloadKeys.IsPublic] = lyric.IsPublic,
            [PayloadKeys.ImageUrl] = lyric.ImageUrl
        }
    };
}
