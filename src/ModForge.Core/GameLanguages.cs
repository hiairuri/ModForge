namespace ModForge.Core;

/// <summary>
/// 游戏支持的语言，以及它们的显示名。
///
/// 代码来自游戏本体的 localization 目录名，是导出时必须使用的准确值：写错目录名游戏就
/// 读不到那份文本。显示名同时给中英文，因为编辑器界面语言本身也可切换。
/// </summary>
public static class GameLanguages
{
    public sealed record Language(string Code, string NameEn, string NameZh)
    {
        /// <summary>按给定界面语言返回显示名。</summary>
        public string Display(string uiLanguage) =>
            uiLanguage.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? NameZh : NameEn;
    }

    /// <summary>游戏内建的全部语言，顺序按代码排列，便于查找。</summary>
    public static IReadOnlyList<Language> All { get; } = new[]
    {
        new Language("deu", "German", "德语"),
        new Language("eng", "English", "英语"),
        new Language("esp", "Spanish (Spain)", "西班牙语（西班牙）"),
        new Language("fra", "French", "法语"),
        new Language("ita", "Italian", "意大利语"),
        new Language("jpn", "Japanese", "日语"),
        new Language("kor", "Korean", "韩语"),
        new Language("pol", "Polish", "波兰语"),
        new Language("ptb", "Portuguese (Brazil)", "葡萄牙语（巴西）"),
        new Language("rus", "Russian", "俄语"),
        new Language("spa", "Spanish (Latin America)", "西班牙语（拉美）"),
        new Language("tha", "Thai", "泰语"),
        new Language("tur", "Turkish", "土耳其语"),
        new Language("zhs", "Simplified Chinese", "简体中文"),
    };

    /// <summary>
    /// 没有文本时回退到的语言。游戏自己的本地化表也以英语为兜底，所以只要英语填了，
    /// 其它语言即使留空也能显示出来。
    /// </summary>
    public const string Fallback = "eng";

    public static bool IsKnown(string code) =>
        All.Any(l => l.Code.Equals(code, StringComparison.OrdinalIgnoreCase));

    /// <summary>把语言代码规范化成游戏使用的写法，无法识别时返回 null。</summary>
    public static string? Normalize(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        Language? hit = All.FirstOrDefault(
            l => l.Code.Equals(code, StringComparison.OrdinalIgnoreCase));

        // 允许常见简写：zh / zh-CN 指向简体中文，en / en-US 指向英语。
        if (hit is null && code.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
        {
            hit = All.First(l => l.Code == "zhs");
        }

        if (hit is null && code.StartsWith("en", StringComparison.OrdinalIgnoreCase))
        {
            hit = All.First(l => l.Code == "eng");
        }

        return hit?.Code;
    }

    public static string DisplayName(string code, string uiLanguage)
    {
        Language? hit = All.FirstOrDefault(
            l => l.Code.Equals(code, StringComparison.OrdinalIgnoreCase));

        return hit?.Display(uiLanguage) ?? code;
    }
}
