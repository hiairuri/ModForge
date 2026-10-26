using ModForge.Core.Model;

namespace ModForge.Core.Modules.Files;

/// <summary>
/// 一份工程文件夹长什么样——**只有目录与文件名**，不碰内容。
///
/// 布局（工程文件夹以工程名命名，整体自足、可拷贝、可分享）：
///
///   &lt;工程名&gt;/
///     About/         工程信息
///       About.xml       RimWorld 形状的 mod 元数据（名字、作者、id、版本…）
///       Preview.png     可选，mod 封面（文件名记在 About.xml 的 &lt;preview&gt; 里）
///     Language/      本地化，一种语言一个文件夹
///       &lt;语言&gt;/
///         Localization.xml   翻译键（&lt;LanguageData&gt;&lt;cards&gt;&lt;KEY.title&gt;…）
///     Textures/      卡框、效果、遗物等贴图（目前是状态图标）
///     Skins/         皮肤：源皮肤包（.pck）收在这里
///     Cards/         卡面
///     Source/        生成的 C# 源码，供查看与对照
///     Mod/           导出的可加载 mod（.pck / .dll / .json）
///     Create/        定义，一个实体一个文件
///       Cards/&lt;卡 id&gt;.xml
///       Effects/&lt;共享效果 id&gt;.xml
///       Powers/&lt;状态 id&gt;.xml
///
/// 定义与文本分家（Create 放结构与数值、Language 放玩家看得到的话），与 RimWorld 一致：
/// 一条内容的文字要改一个语言就改一个文件，不必碰定义。
///
/// 这个类只管"在哪"，"写什么"在 <see cref="ProjectStore"/> 及其下的各 XxxXml。
/// </summary>
public sealed class ProjectLayout
{
    public ProjectLayout(string root)
    {
        Root = Path.GetFullPath(root);
    }

    /// <summary>工程文件夹。</summary>
    public string Root { get; }

    /* ------------------------------------------------------------------ 目录 */

    public string AboutDirectory => Path.Combine(Root, "About");

    public string LanguageDirectory => Path.Combine(Root, "Language");

    public string TexturesDirectory => Path.Combine(Root, "Textures");

    public string SkinsDirectory => Path.Combine(Root, "Skins");

    /// <summary>卡面（原 cardart）。</summary>
    public string CardsDirectory => Path.Combine(Root, "Cards");

    /// <summary>生成的 C# 源码。名字用 Source 而不是 src：编辑器自身的源码树叫 src/。</summary>
    public string SourceDirectory => Path.Combine(Root, "Source");

    /// <summary>导出的可加载 mod 落在 <c>Mod/&lt;mod id&gt;/</c>。</summary>
    public string ModDirectory => Path.Combine(Root, "Mod");

    public string CreateDirectory => Path.Combine(Root, "Create");

    public string CreateCardsDirectory => Path.Combine(CreateDirectory, "Cards");

    public string CreateEffectsDirectory => Path.Combine(CreateDirectory, "Effects");

    public string CreatePowersDirectory => Path.Combine(CreateDirectory, "Powers");

    public string CreateSkinsDirectory => Path.Combine(CreateDirectory, "Skins");

    /* ------------------------------------------------------------------ 文件 */

    public string AboutFile => Path.Combine(AboutDirectory, "About.xml");

    /// <summary>
    /// 封面文件：**名字写死** <c>Preview.png</c>。
    ///
    /// 写死是有意的：封面就是"About 文件夹下那张图"，多一个可配置的名字就多一处能对不上
    /// 的地方（改了名之后谁也不知道该找哪个）。文件不在就是没有封面——调用方按 null 处理，
    /// 不该因为缺一张图让别的地方出问题。
    /// </summary>
    public string PreviewFile => Path.Combine(AboutDirectory, PreviewFileName);

    /// <summary>封面文件名。</summary>
    public const string PreviewFileName = "Preview.png";

    /// <summary>某种语言的翻译文件。</summary>
    public string LocalizationFile(string language) =>
        Path.Combine(LanguageDirectory, language, "Localization.xml");

    public string CardFile(string id) => XmlIn(CreateCardsDirectory, id);

    public string EffectFile(string id) => XmlIn(CreateEffectsDirectory, id);

    public string PowerFile(string id) => XmlIn(CreatePowersDirectory, id);

    /// <summary>皮肤按它换的那个角色命名（一个角色同时只穿一套皮肤）。</summary>
    public string SkinFile(string target) => XmlIn(CreateSkinsDirectory, target);

    private static string XmlIn(string directory, string id) =>
        Path.Combine(directory, FileName(id) + ".xml");

    /* ------------------------------------------------------------------ 建立 */

    /// <summary>
    /// 建立八个文件夹（幂等）。保存前与打开后都调用，所以一份工程无论从哪来，
    /// 结构都是完整的——缺哪个文件夹就是空的，而不是"不存在"。
    /// </summary>
    public void EnsureCreated()
    {
        foreach (string directory in new[]
        {
            Root,
            AboutDirectory,
            LanguageDirectory,
            TexturesDirectory,
            SkinsDirectory,
            CardsDirectory,
            SourceDirectory,
            ModDirectory,
            CreateDirectory,
            CreateCardsDirectory,
            CreateEffectsDirectory,
            CreatePowersDirectory,
            CreateSkinsDirectory,
        })
        {
            Directory.CreateDirectory(directory);
        }
    }

    /// <summary>这个目录看起来是不是一份工程文件夹（认 About/About.xml）。</summary>
    public static bool LooksLikeProject(string directory) =>
        File.Exists(Path.Combine(directory, AboutDirectoryName, "About.xml"));

    /// <summary>这一个布局指着的目录是不是已经有 About/About.xml。</summary>
    public bool LayoutRootExists() => LooksLikeProject(Root);

    public const string AboutDirectoryName = "About";

    /* ------------------------------------------------------------------ 文件名 */

    /// <summary>
    /// 把一个 id 变成能当文件名的形式。
    ///
    /// id 是用户填的，可能带斜杠、冒号、<c>..</c>——直接用会写到文件夹外面去。
    /// 非法字符一律换成下划线，并去掉首尾的点与空格（Windows 上以点结尾的名字建不出来）。
    /// </summary>
    public static string FileName(string id)
    {
        string raw = (id ?? string.Empty).Trim();
        var cleaned = new System.Text.StringBuilder(raw.Length);

        char[] invalid = Path.GetInvalidFileNameChars();

        foreach (char c in raw)
        {
            cleaned.Append(invalid.Contains(c) || c == '/' || c == '\\' ? '_' : c);
        }

        string result = cleaned.ToString().Trim().Trim('.');

        return result.Length == 0 ? "unnamed" : result;
    }

    /// <summary>
    /// 目标路径是不是在 <paramref name="directory"/> 里面（防 id 里带 <c>..</c> 逃出去）。
    /// </summary>
    public static bool IsInside(string directory, string path)
    {
        string root = Path.GetFullPath(directory) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 把工程名变成能当文件夹名的形式。
    ///
    /// 空名、纯符号名都会退到 id（id 本身已经过校验），所以调用方总能得到一个可用的名字。
    /// </summary>
    public static string FolderNameFor(ModProject project)
    {
        string raw = string.IsNullOrWhiteSpace(project.Name) ? project.Id : project.Name;

        var cleaned = new System.Text.StringBuilder();
        char[] invalid = Path.GetInvalidFileNameChars();

        foreach (char c in raw.Trim())
        {
            cleaned.Append(invalid.Contains(c) ? '_' : c);
        }

        string result = cleaned.ToString().Trim().TrimEnd('.');

        return string.IsNullOrWhiteSpace(result) ? project.Id : result;
    }

    /// <summary>
    /// 在 <paramref name="parentDirectory"/> 下给工程算一个不冲突的文件夹路径。
    ///
    /// 重名时加 -2、-3……但 <paramref name="overwrite"/> 为 true 时直接用原名。
    /// </summary>
    public static string UniqueProjectFolder(string parentDirectory, string folderName, bool overwrite)
    {
        string candidate = Path.Combine(parentDirectory, folderName);

        if (overwrite || !Directory.Exists(candidate))
        {
            return candidate;
        }

        for (int n = 2; n < 1000; n++)
        {
            string next = Path.Combine(parentDirectory, $"{folderName}-{n}");

            if (!Directory.Exists(next))
            {
                return next;
            }
        }

        return Path.Combine(parentDirectory, $"{folderName}-{Guid.NewGuid():N}");
    }
}
