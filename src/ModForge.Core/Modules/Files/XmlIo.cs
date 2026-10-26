using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace ModForge.Core.Modules.Files;

/// <summary>
/// 整个文件读不出来时抛这个（XML 语法坏了、根元素不对、文件不存在）。
///
/// **单个字段的问题不走异常**——那种情况由 <see cref="ReadLog"/> 记下来并继续读，
/// 因为"一个字段写错"不该让整份工程打不开。异常只留给"这份文件根本没有内容可读"。
/// </summary>
public sealed class ProjectXmlException : Exception
{
    public ProjectXmlException(string path, string message, Exception? inner = null)
        : base($"{System.IO.Path.GetFileName(path)}: {message}", inner)
    {
        Path = path;
    }

    /// <summary>出问题的文件。</summary>
    public string Path { get; }
}

/// <summary>
/// 工程 XML 的读写底座。
///
/// 读的一侧**不信任文件内容**：工程文件夹是拿来分享、从别处拷来的，里面的 XML 可能是别人写的。
/// 所以一律按"不可信输入"解析——关掉 DTD（DOCTYPE 与 ENTITY 直接报错）、不给外部实体
/// 解析器，这样 XML 里的外部实体既不会去读本地文件，也不会有指数展开那类问题。
///
/// 写的一侧固定成缩进两个空格、LF 换行、UTF-8 不带 BOM：同样的工程写两次是逐字节一样的，
/// 所以"保存一下看看 git 有什么变化"是可信的。
/// </summary>
public static class XmlIo
{
    private static readonly XmlReaderSettings ReaderSettings = new()
    {
        // 不可信输入：不处理 DTD（DOCTYPE / ENTITY 会被这里拒掉），也不解析外部实体。
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
        CloseInput = true,
    };

    private static readonly XmlWriterSettings WriterSettings = new()
    {
        Indent = true,
        IndentChars = "  ",
        NewLineChars = "\n",
        Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        CloseOutput = true,
    };

    /// <summary>安全地读一个 XML 文档。</summary>
    public static XDocument Read(string path)
    {
        if (!File.Exists(path))
        {
            throw new ProjectXmlException(path, "文件不存在。");
        }

        try
        {
            using FileStream stream = File.OpenRead(path);

            // 显式用上面那份设置建 reader，而不是 XDocument.Load(path)：后者的默认设置
            // 随 .NET 版本走，这里要的是"无论哪个版本都不解析 DTD"。
            using XmlReader reader = XmlReader.Create(stream, ReaderSettings);
            return XDocument.Load(reader);
        }
        catch (XmlException ex)
        {
            throw new ProjectXmlException(path, $"XML 解析失败：{ex.Message}", ex);
        }
        catch (IOException ex)
        {
            throw new ProjectXmlException(path, $"读取失败：{ex.Message}", ex);
        }
    }

    /// <summary>安全地解析一段 XML 文本（测试与"检查这段内容是不是合法 XML"用）。</summary>
    public static XDocument Parse(string xml, string origin = "(text)")
    {
        try
        {
            using var text = new StringReader(xml);
            using XmlReader reader = XmlReader.Create(text, ReaderSettings);
            return XDocument.Load(reader);
        }
        catch (XmlException ex)
        {
            throw new ProjectXmlException(origin, $"XML 解析失败：{ex.Message}", ex);
        }
    }

    /// <summary>写出一个 XML 文档（目录不存在就建）。</summary>
    public static void Write(string path, XDocument document)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);

        using FileStream stream = File.Create(path);
        using XmlWriter writer = XmlWriter.Create(stream, WriterSettings);
        document.Save(writer);
    }

    /// <summary>新建一份带标准声明的文档。</summary>
    public static XDocument Document(XElement root) =>
        new(new XDeclaration("1.0", "utf-8", null), root);

    /// <summary>根元素，名字不对时给出说得清的错。</summary>
    public static XElement Root(XDocument document, string path, string expected)
    {
        XElement? root = document.Root;

        if (root is null)
        {
            throw new ProjectXmlException(path, "文件是空的。");
        }

        if (!root.Name.LocalName.Equals(expected, StringComparison.Ordinal))
        {
            throw new ProjectXmlException(path, $"根元素应为 <{expected}>，实际是 <{root.Name.LocalName}>。");
        }

        return root;
    }

    /* ------------------------------------------------------------------ 读 */
    //
    // 读的一侧全部**宽容**：元素没有就是默认值（正常写法），值写错了就用默认值并在
    // ReadLog 里记一条（不中断读取）。区分"没有"与"写错"很重要——前者是作者没写，
    // 后者是作者写坏了要说一声，但两者都该让工程正常打开。

    /// <summary>空值字面量。读不动的值一律视作它，也就是"没有值"。</summary>
    public const string NullLiteral = "NULL";

    /// <summary>这段文本是不是空值字面量（大小写不敏感）。</summary>
    public static bool IsNull(string? text) =>
        text is not null && text.Equals(NullLiteral, StringComparison.OrdinalIgnoreCase);

    /// <summary>取一个子元素的文本；没有、只有空白、或是 <c>NULL</c> 时返回 null。</summary>
    public static string? Str(XElement parent, string name)
    {
        string? value = parent.Element(name)?.Value.Trim();

        return string.IsNullOrEmpty(value) || IsNull(value) ? null : value;
    }

    /// <summary>取一个整数子元素；没有、<c>NULL</c>、或读不动时用 <paramref name="fallback"/>。</summary>
    public static int Int(ReadLog log, XElement parent, string name, int fallback = 0) =>
        Whole(log, parent, name) ?? fallback;

    /// <summary>
    /// 取一个整数子元素；没有或 <c>NULL</c> 时返回 null（与"填了 0"区分开）；
    /// 读不动时记一条并返回 null。
    /// </summary>
    public static int? IntOrNull(ReadLog log, XElement parent, string name) => Whole(log, parent, name);

    /// <summary>取一个小数子元素；没有、<c>NULL</c>、或读不动时用 <paramref name="fallback"/>。</summary>
    public static decimal Decimal(ReadLog log, XElement parent, string name, decimal fallback = 0m) =>
        DecimalOrNull(log, parent, name) ?? fallback;

    /// <summary>取一个可空的小数子元素；没有或 <c>NULL</c> 时返回 null；读不动时记一条并返回 null。</summary>
    public static decimal? DecimalOrNull(ReadLog log, XElement parent, string name)
    {
        string? text = Str(parent, name);

        if (text is null)
        {
            return null;
        }

        if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal value))
        {
            return value;
        }

        using (log.Scope(name))
        {
            log.Error($"\"{text}\" 不是一个数，已按\"没填\"处理。");
        }

        return null;
    }

    /// <summary>取一个布尔子元素。<c>true</c> / <c>1</c> / <c>yes</c> 都算真。</summary>
    public static bool Bool(ReadLog log, XElement parent, string name, bool fallback = false)
    {
        string? text = Str(parent, name);

        if (text is null)
        {
            return fallback;
        }

        if (text.Equals("true", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("1", StringComparison.Ordinal) ||
            text.Equals("yes", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (text.Equals("false", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("0", StringComparison.Ordinal) ||
            text.Equals("no", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        using (log.Scope(name))
        {
            log.Error($"\"{text}\" 不是 true / false，已按 {fallback.ToString().ToLowerInvariant()} 处理。");
        }

        return fallback;
    }

    /// <summary>
    /// 取一个枚举子元素。
    ///
    /// 枚举名是手写时最容易打错的东西，所以**写错一定要报出来**——但只是报出来：
    /// 那一项退回默认值，文件其余部分照常读。悄悄退回默认值而不吭声是最坏的一种，
    /// 因为卡的行为与文件里写的不是一回事，而没有任何提示。
    /// </summary>
    public static T EnumValue<T>(ReadLog log, XElement parent, string name, T fallback)
        where T : struct, Enum
    {
        if (TryEnum(log, parent, name, out T value))
        {
            return value;
        }

        return fallback;
    }

    /// <summary>
    /// 取一个枚举子元素，返回"这一项到底写没写"。
    ///
    /// <c>false</c> 有两种情况（都是"没指定"）：元素不在、值是 <c>NULL</c>。
    /// 需要区分"没指定"与"明确指定成某个值（含 None）"的地方用这个——
    /// 等级表里 <c>&lt;source&gt;None&lt;/source&gt;</c>（这一级不要变量）与不写 <c>&lt;source&gt;</c>
    /// （这一级不动变量）正是那两回事。
    /// </summary>
    public static bool TryEnum<T>(ReadLog log, XElement parent, string name, out T value)
        where T : struct, Enum
    {
        value = default;

        string? text = Str(parent, name);

        if (text is null)
        {
            return false;
        }

        if (System.Enum.TryParse(text, ignoreCase: true, out value) && System.Enum.IsDefined(value))
        {
            return true;
        }

        using (log.Scope(name))
        {
            log.Error(
                $"\"{text}\" 不是合法的 {typeof(T).Name}，已忽略。合法值：" +
                string.Join(" / ", System.Enum.GetNames<T>()));
        }

        value = default;
        return false;
    }

    /// <summary>
    /// 不认识子元素就记一条（无视它，但要说出来）。
    ///
    /// 手写文件里"多出来的东西"十有八九是拼错，而不是新功能。无视它是对的——不能因为
    /// 多了一个词就让工程打不开；但**必须报出来**，否则"我明明写了却没生效"会变成一场
    /// 猜谜：作者盯着自己写的那行，而读取器早把它跳过了。
    /// </summary>
    public static void RejectUnknown(ReadLog log, XElement parent, IReadOnlyCollection<string> known)
    {
        foreach (XElement child in parent.Elements())
        {
            if (!known.Contains(child.Name.LocalName))
            {
                using (log.Scope(child.Name.LocalName))
                {
                    log.Error(
                        $"这里不认识 <{child.Name.LocalName}>，已忽略。这里接受：" +
                        string.Join(" / ", known));
                }
            }
        }
    }

    /// <summary>整数读取的共用部分：解析失败时记一条并当作"没填"。</summary>
    private static int? Whole(ReadLog log, XElement parent, string name)
    {
        string? text = Str(parent, name);

        if (text is null)
        {
            return null;
        }

        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
        {
            return value;
        }

        using (log.Scope(name))
        {
            log.Error($"\"{text}\" 不是一个整数，已按\"没填\"处理。");
        }

        return null;
    }

    /// <summary>
    /// 取一串 <c>&lt;li&gt;</c> 条目，例如
    /// <c>&lt;keywords&gt;&lt;li&gt;Exhaust&lt;/li&gt;&lt;/keywords&gt;</c>。
    /// </summary>
    public static List<string> List(XElement parent, string name)
    {
        var result = new List<string>();
        XElement? holder = parent.Element(name);

        if (holder is null)
        {
            return result;
        }

        foreach (XElement item in holder.Elements("li"))
        {
            string value = item.Value.Trim();

            if (value.Length > 0)
            {
                result.Add(value);
            }
        }

        return result;
    }

    /* ------------------------------------------------------------------ 写 */

    /// <summary>
    /// 写一个子元素。
    ///
    /// **与默认值相同的项一律不写**：写出来的文件才是"只有这张卡真正设置过的东西"，
    /// 一页看得完、改动一眼看得出。反过来，手工把某一行删掉就等于恢复默认值，
    /// 与读的时候"缺项即默认"正好对上。
    ///
    /// 但注意：默认值不是这里硬编码的常量，而是调用方拿**模型自己的默认实例**比出来的
    /// （见各 XxxXml 的写法），所以模型改了默认值，写出来的东西自动跟着变。
    /// </summary>
    public static void Set(XElement parent, string name, object? value) =>
        parent.Add(new XElement(name, Format(value)));

    /// <summary>写一个整数子元素。</summary>
    public static void Set(XElement parent, string name, int value) =>
        parent.Add(new XElement(name, value.ToString(CultureInfo.InvariantCulture)));

    /// <summary>写一个小数子元素。</summary>
    public static void Set(XElement parent, string name, decimal value) =>
        parent.Add(new XElement(name, value.ToString(CultureInfo.InvariantCulture)));

    /// <summary>写一个布尔子元素。</summary>
    public static void Set(XElement parent, string name, bool value) =>
        parent.Add(new XElement(name, value ? "true" : "false"));

    /// <summary>写一个枚举子元素（写名字，不写数字：文件是给人读的）。</summary>
    public static void Set<T>(XElement parent, string name, T value) where T : struct, Enum =>
        parent.Add(new XElement(name, value.ToString()));

    /// <summary>写一串 <c>&lt;li&gt;</c> 条目。</summary>
    public static void SetList(XElement parent, string name, IEnumerable<string> values)
    {
        var holder = new XElement(name);

        foreach (string value in values)
        {
            holder.Add(new XElement("li", value));
        }

        parent.Add(holder);
    }

    /// <summary>写一个多行文本子元素：内容原样保存，缩进不会串进文本里。</summary>
    public static void SetText(XElement parent, string name, string value) =>
        parent.Add(new XElement(name, new XText(value)));

    /// <summary>按"值是否与默认相同"决定写不写一个字符串子元素。</summary>
    public static void SetIf(XElement parent, string name, string? value, string? fallback = null)
    {
        if (!string.Equals(value ?? string.Empty, fallback ?? string.Empty, StringComparison.Ordinal))
        {
            SetText(parent, name, value ?? string.Empty);
        }
    }

    /// <summary>可空的整数：有值才写（"没填"与"填了 0"是两回事，等级表里尤其如此）。</summary>
    public static void SetIf(XElement parent, string name, int? value)
    {
        if (value is int number)
        {
            Set(parent, name, number);
        }
    }

    /// <summary>可空的小数：有值才写。</summary>
    public static void SetIf(XElement parent, string name, decimal? value)
    {
        if (value is decimal number)
        {
            Set(parent, name, number);
        }
    }

    /// <summary>按"值是否与默认相同"决定写不写一个整数子元素。</summary>
    public static void SetIf(XElement parent, string name, int value, int fallback)
    {
        if (value != fallback)
        {
            Set(parent, name, value);
        }
    }

    /// <summary>按"值是否与默认相同"决定写不写一个小数子元素。</summary>
    public static void SetIf(XElement parent, string name, decimal value, decimal fallback)
    {
        if (value != fallback)
        {
            Set(parent, name, value);
        }
    }

    /// <summary>按"值是否与默认相同"决定写不写一个布尔子元素。</summary>
    public static void SetIf(XElement parent, string name, bool value, bool fallback)
    {
        if (value != fallback)
        {
            Set(parent, name, value);
        }
    }

    /// <summary>按"值是否与默认相同"决定写不写一个枚举子元素。</summary>
    public static void SetIf<T>(XElement parent, string name, T value, T fallback) where T : struct, Enum
    {
        if (!value.Equals(fallback))
        {
            Set(parent, name, value);
        }
    }

    private static string Format(object? value) => value switch
    {
        null => string.Empty,
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };
}
