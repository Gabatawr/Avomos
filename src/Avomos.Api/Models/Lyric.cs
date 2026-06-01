namespace Avomos.Api.Models;

public class Lyric
{
    public Guid Id { get; init; }
    public string OriginId { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Lyrics { get; set; } = string.Empty;
    public string Styles { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
    public string Url { get; init; } = string.Empty;
    public int Plays { get; set; }
    public string Model { get; init; } = string.Empty;
    public bool IsPublic { get; set; } = true;
    public string ImageUrl { get; init; } = string.Empty;
}
