using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Avomos.Api.Services;

public class LlmCache
{
    private readonly string _basePath;

    public LlmCache(string basePath = ".cache/llm")
    {
        _basePath = basePath;
    }

    public string CacheDir(string feature) => Path.Combine(_basePath, feature);

    public string Key(string input)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public async Task<T?> GetAsync<T>(string feature, string key) where T : class
    {
        var path = Path.Combine(_basePath, feature, $"{key}.json");
        if (!File.Exists(path)) return null;

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream);
    }

    public async Task SetAsync<T>(string feature, string key, T value)
    {
        var dir = Path.Combine(_basePath, feature);
        Directory.CreateDirectory(dir);

        var path = Path.Combine(dir, $"{key}.json");
        var json = JsonSerializer.Serialize(value);
        await File.WriteAllTextAsync(path, json);
    }
}
