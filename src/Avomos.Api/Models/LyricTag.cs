namespace Avomos.Api.Models;

public class LyricTag
{
    public string Name { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public float Confidence { get; init; }
}
