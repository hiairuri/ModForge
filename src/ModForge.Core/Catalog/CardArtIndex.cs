using ModForge.Core.Pck;

namespace ModForge.Core.Catalog;

/// <summary>
/// 卡图清单：卡图名 → （牌池，原名）。
///
/// 卡图按 <c>images/packed/card_portraits/&lt;牌池&gt;/&lt;名字&gt;.png.import</c> 存放，所以
/// 目录结构本身就是"这张图属于哪个角色"的答案，也是把一张卡和它的立绘对上的唯一现成依据。
/// </summary>
internal static class CardArtIndex
{
    /// <summary>建立索引；读不到游戏资源时返回空表（调用方据此退化为没有立绘）。</summary>
    public static Dictionary<string, (string Pool, string Stem)> Build(string gameDir)
    {
        var index = new Dictionary<string, (string Pool, string Stem)>(StringComparer.Ordinal);

        try
        {
            string pck = Path.Combine(gameDir, "SlayTheSpire2.pck");

            if (!File.Exists(pck))
            {
                return index;
            }

            foreach (PckFile.EntryInfo entry in PckFile.ReadDirectory(pck))
            {
                string path = entry.Path;

                if (!path.StartsWith("images/packed/card_portraits/", StringComparison.Ordinal) ||
                    !path.EndsWith(".png.import", StringComparison.Ordinal))
                {
                    continue;
                }

                string[] parts = path.Split('/');

                // 顶层还有 beta.png 这类没有角色目录的，跳过。
                if (parts.Length < 5 || parts[3].Length == 0)
                {
                    continue;
                }

                // beta 变体单独一个子目录，不是正式卡图。
                if (parts.Length >= 6 && parts[4] == "beta")
                {
                    continue;
                }

                string pool = parts[3];
                string stem = Path.GetFileName(parts[^1]).Replace(".png.import", "");

                // 同一个名字出现在两个牌池时（只有两例），保留先出现的，避免互相覆盖。
                index.TryAdd(stem, (pool, stem));
            }
        }
        catch (Exception)
        {
            // 卡图只是显示用，读不到不影响数值读取。
        }

        return index;
    }
}
