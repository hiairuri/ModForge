using System.Xml;
using System.Xml.Linq;
using ModForge.Core.Model;

namespace ModForge.Core.Modules.Files;

/// <summary>
/// 一种语言的翻译键：<c>Language/&lt;语言&gt;/Localization.xml</c>。
///
/// <code>
/// &lt;LanguageData&gt;
///   &lt;cards&gt;
///     &lt;FORGE_STRIKE.title&gt;锻炉打击&lt;/FORGE_STRIKE.title&gt;
///     &lt;FORGE_STRIKE.description&gt;造成 {Damage:diff()} 点伤害。&lt;/FORGE_STRIKE.description&gt;
///   &lt;/cards&gt;
///   &lt;powers&gt;
///     &lt;FORGE_MIGHT_POWER.title&gt;锻炉之力&lt;/FORGE_MIGHT_POWER.title&gt;
///   &lt;/powers&gt;
/// &lt;/LanguageData&gt;
/// </code>
///
/// **元素名就是游戏的翻译键**（RimWorld 的语言文件也是这个写法）。键有两段：
/// 前面是模型条目的名字（卡牌用生成的类型名 slug，例如 <c>FORGE_STRIKE</c>），后面是
/// <c>title</c> / <c>description</c>。所以这个文件里的每一行都能直接对上游戏里的键，
/// 翻译的人不必回编辑器查。
///
/// 一个语言一个文件，而不是一种内容一个文件：翻译是按语言整批做的，翻到哪种语言就只开
/// 哪个文件夹。表名（<c>cards</c> / <c>powers</c>）与游戏自己的本地化表同名。
/// </summary>
public static class LanguageXml
{
    public const string FileName = "Localization.xml";

    private const string RootName = "LanguageData";

    /// <summary>一种语言的两张表：键 → 文本。</summary>
    public sealed record Tables(
        Dictionary<string, string> Cards,
        Dictionary<string, string> Powers);

    /// <summary>卡牌条目的名字（翻译键的前半段），例如 <c>FORGE_STRIKE</c>。</summary>
    public static string CardEntry(ModProject project, CardDef card) =>
        ModelIds.Slugify(project.CardTypeName(card));

    /// <summary>状态条目的名字，例如 <c>FORGE_MIGHT_POWER</c>。</summary>
    public static string PowerEntry(ModProject project, PowerDef power) =>
        ModelIds.Slugify(project.PowerTypeName(power));

    public static void Write(
        string path,
        IReadOnlyDictionary<string, string> cards,
        IReadOnlyDictionary<string, string> powers)
    {
        var root = new XElement(RootName);

        if (cards.Count > 0)
        {
            var table = new XElement("cards");

            foreach ((string key, string text) in cards.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                RequireValidKey(path, key);
                table.Add(new XElement(key, new XText(text)));
            }

            root.Add(table);
        }

        if (powers.Count > 0)
        {
            var table = new XElement("powers");

            foreach ((string key, string text) in powers.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                RequireValidKey(path, key);
                table.Add(new XElement(key, new XText(text)));
            }

            root.Add(table);
        }

        XmlIo.Write(path, XmlIo.Document(root));
    }

    /// <summary>
    /// 读一份语言表。
    ///
    /// 翻译键**不受已知名单限制**：这里面一个"字段"都没有，键是开放集合（每张卡一个），
    /// 所以不认识的键不是错误，它就是一条翻译。只有表名（<c>cards</c> / <c>powers</c>）之外
    /// 的顶层元素才算可疑，记一条。
    /// </summary>
    public static Tables Read(string path, ReadLog log)
    {
        XDocument document = XmlIo.Read(path);
        XElement root = XmlIo.Root(document, path, RootName);

        foreach (XElement child in root.Elements())
        {
            string name = child.Name.LocalName;

            if (name is not ("cards" or "powers"))
            {
                using (log.Scope(name))
                {
                    log.Error($"语言文件里不认识 <{name}>（只有 <cards> 与 <powers>），已忽略。");
                }
            }
        }

        return new Tables(Table(root, "cards"), Table(root, "powers"));
    }

    /// <summary>
    /// 键能不能当 XML 元素名。
    ///
    /// 键是"生成的类型名"算出来的，所以正常情况下一定合法——除非工程 id 或类型前缀以数字
    /// 开头（那样生成的 C# 类名本身也不合法）。保存前先整体查一遍并把话说清楚，
    /// 好过写到一半抛出来、留下半份工程。
    /// </summary>
    public static bool IsValidKey(string key)
    {
        try
        {
            XmlConvert.VerifyName(key);
            return true;
        }
        catch (XmlException)
        {
            return false;
        }
    }

    private static void RequireValidKey(string path, string key)
    {
        if (!IsValidKey(key))
        {
            throw new ProjectXmlException(
                path,
                $"\"{key}\" 不能用作翻译键（XML 元素名不能以数字或符号开头）。" +
                "多半是工程的 id 或类型前缀以数字开头——把它们改成字母开头即可。");
        }
    }

    private static Dictionary<string, string> Table(XElement root, string name)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        if (root.Element(name) is not XElement table)
        {
            return result;
        }

        foreach (XElement entry in table.Elements())
        {
            result[entry.Name.LocalName] = entry.Value;
        }

        return result;
    }
}
