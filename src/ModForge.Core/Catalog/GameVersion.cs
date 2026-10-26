using System.Text.Json;

namespace ModForge.Core.Catalog;

/// <summary>
/// 游戏版本。取自游戏根目录的 <c>release_info.json</c>，内容形如
/// <c>{"commit":"59260271","version":"v0.107.1",...}</c>。
///
/// 版本用来给缓存命名：游戏一升级，缓存文件名就变了，于是数据自动重算，
/// 而不是拿着一份旧数据去解释新版本。
/// </summary>
public sealed record GameVersion(string Version, string? Commit)
{
    /// <summary>缓存文件名里的版本标识，例如 v0.107.1-59260271。</summary>
    public string CacheKey => Commit is { Length: > 0 } ? $"{Version}-{Commit}" : Version;

    public static GameVersion Read(string gameDir)
    {
        string path = Path.Combine(gameDir, "release_info.json");

        if (!File.Exists(path))
        {
            return new GameVersion("unknown", null);
        }

        try
        {
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));

            string version = doc.RootElement.TryGetProperty("version", out JsonElement v)
                ? v.GetString() ?? "unknown"
                : "unknown";

            string? commit = doc.RootElement.TryGetProperty("commit", out JsonElement c)
                ? c.GetString()
                : null;

            return new GameVersion(version, commit);
        }
        catch (JsonException)
        {
            return new GameVersion("unknown", null);
        }
    }
}
