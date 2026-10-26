using System.Reflection;
using System.Text.Json;

namespace ModForge.Core.Modules.Descriptions;

/// <summary>
/// 一种语言的句式表：用词（<c>words</c>）、数值来源名（<c>sources</c>）、角色名（<c>actors</c>）
/// 与句式模板（<c>templates</c>）。
///
/// 模板用 <c>{名字}</c> 占位，替换是纯文本替换——不做语法分析，所以各语言的语序可以完全不同。
/// 这正是需要的：中文把修饰语放在名词前（"等于手牌数的伤害"），英文放在名词后
/// （"damage equal to cards in hand"），同一套模板无法同时满足，只能各写各的。
/// </summary>
public sealed class SentenceSet
{
    public string Language { get; init; } = GameLanguages.Fallback;

    internal Dictionary<string, string> Words { get; init; } = new(StringComparer.Ordinal);

    internal Dictionary<string, string> Sources { get; init; } = new(StringComparer.Ordinal);

    internal Dictionary<string, string> Actors { get; init; } = new(StringComparer.Ordinal);

    internal Dictionary<string, string> Templates { get; init; } = new(StringComparer.Ordinal);

    /// <summary>取一个词。缺失时返回 null，由调用方决定回退。</summary>
    public string? Word(string key) => Words.TryGetValue(key, out string? v) ? v : null;

    public string? Source(string kind) => Sources.TryGetValue(kind, out string? v) ? v : null;

    public string? Actor(string actor) => Actors.TryGetValue(actor, out string? v) ? v : null;

    public bool HasTemplate(string key) => Templates.ContainsKey(key);

    /// <summary>
    /// 渲染时**没找到**的模板名。
    ///
    /// 记下来是因为缺模板不会报错：会退回 "unknown" 那条通用句子，而它内部的占位符又都能
    /// 被填上，于是输出看起来是正常的一句话，只是意思不对（实测过：模板名写错时，
    /// "自动打出这张牌"渲染成了内部枚举名加一个 0）。光看渲染结果发现不了，
    /// 所以这里留个记录给测试断言。
    ///
    /// 只增不减（每个语言一个实例、内容来自文件，所以结果是确定的）。
    /// </summary>
    public IReadOnlyCollection<string> MissingTemplateKeys => _missingTemplates;

    private readonly HashSet<string> _missingTemplates = new(StringComparer.Ordinal);

    /// <summary>
    /// 按模板渲染一句话。模板本身可以带占位符，词条里也可以带（例如 intoPileTop 用 {pile}）。
    /// </summary>
    public string Render(string templateKey, IReadOnlyDictionary<string, string> vars)
    {
        if (!Templates.TryGetValue(templateKey, out string? t))
        {
            _missingTemplates.Add(templateKey);
        }

        string template = t
            ?? (Templates.TryGetValue("unknown", out string? fallback) ? fallback : templateKey);

        return Substitute(template, vars);
    }

    /// <summary>替换 <c>{名字}</c> 占位符；没有对应值的占位符留原样，便于发现缺项。</summary>
    public static string Substitute(string template, IReadOnlyDictionary<string, string> vars)
    {
        if (template.Length == 0 || !template.Contains('{'))
        {
            return template;
        }

        var sb = new System.Text.StringBuilder(template.Length + 32);
        int i = 0;

        while (i < template.Length)
        {
            char c = template[i];

            if (c != '{')
            {
                sb.Append(c);
                i++;
                continue;
            }

            int end = template.IndexOf('}', i + 1);

            if (end < 0)
            {
                sb.Append(template, i, template.Length - i);
                break;
            }

            string name = template[(i + 1)..end];

            if (vars.TryGetValue(name, out string? value))
            {
                sb.Append(value);
            }
            else
            {
                // 未提供的占位符原样保留：漏了哪个一眼就能看出来。
                sb.Append(template, i, end - i + 1);
            }

            i = end + 1;
        }

        return sb.ToString();
    }
}

/// <summary>
/// 按语言加载句式表。
///
/// 优先读磁盘上的 <c>language/sentences/</c> 文件夹，这样增改语言只要放一个文件，不必重新编译；
/// 文件夹不存在时用编译进程序里的同一份文件作兜底，保证换台机器也能跑。
/// </summary>
public static class SentenceLibrary
{
    private static readonly Dictionary<string, SentenceSet> Cache = new(StringComparer.Ordinal);
    private static readonly Lock Gate = new();

    private static string? _folder;

    /// <summary>显式指定句式文件夹；不指定时自动探测。</summary>
    public static void Configure(string? folder)
    {
        lock (Gate)
        {
            _folder = folder;
            Cache.Clear();
        }
    }

    /// <summary>句式文件夹的实际位置（探测结果或显式设置的值），供界面显示。</summary>
    public static string? Folder
    {
        get
        {
            lock (Gate)
            {
                return _folder ??= Probe();
            }
        }
    }

    /// <summary>磁盘上已有的语言代码，供界面提示可用的句式文件。</summary>
    public static IReadOnlyList<string> AvailableLanguages()
    {
        string? folder = Folder;

        if (folder is null || !Directory.Exists(folder))
        {
            return Array.Empty<string>();
        }

        return Directory.EnumerateFiles(folder, "*.json", SearchOption.TopDirectoryOnly)
            .Select(f => Path.GetFileNameWithoutExtension(f))
            .Where(GameLanguages.IsKnown)
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// 取某种语言的句式表。缺该语言时回退英语，再缺就回退内置英语——所以调用方总能拿到一份可用的。
    /// </summary>
    public static SentenceSet For(string language)
    {
        string code = GameLanguages.Normalize(language) ?? GameLanguages.Fallback;

        lock (Gate)
        {
            if (Cache.TryGetValue(code, out SentenceSet? cached))
            {
                return cached;
            }

            SentenceSet? loaded = LoadFromDisk(code) ?? LoadEmbedded(code);

            if (loaded is null && code != GameLanguages.Fallback)
            {
                loaded = For(GameLanguages.Fallback);
            }

            loaded ??= Empty();

            Cache[code] = loaded;
            return loaded;
        }
    }

    private static SentenceSet Empty() => new() { Language = GameLanguages.Fallback };

    private static SentenceSet? LoadFromDisk(string code)
    {
        string? folder = Folder;

        if (folder is null)
        {
            return null;
        }

        string path = Path.Combine(folder, $"{code}.json");
        return File.Exists(path) ? Parse(File.ReadAllText(path)) : null;
    }

    /// <summary>
    /// 用编译进程序的那份。路径与仓库里的 language/sentences 文件夹是同一份文件，避免两处内容分叉。
    /// </summary>
    private static SentenceSet? LoadEmbedded(string code)
    {
        Assembly assembly = typeof(SentenceLibrary).Assembly;
        string wanted = $"sentences.{code}.json";

        string? name = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(wanted, StringComparison.Ordinal));

        if (name is null)
        {
            return null;
        }

        using Stream? stream = assembly.GetManifestResourceStream(name);
        if (stream is null)
        {
            return null;
        }

        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    private static readonly JsonSerializerOptions Options = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
    };

    private sealed record FileDto(
        string? Language,
        string? Name,
        Dictionary<string, string>? Actors,
        Dictionary<string, string>? Sources,
        Dictionary<string, string>? Words,
        Dictionary<string, string>? Templates);

    private static SentenceSet Parse(string json)
    {
        try
        {
            FileDto? dto = JsonSerializer.Deserialize<FileDto>(json, Options);

            if (dto is null)
            {
                return Empty();
            }

            return new SentenceSet
            {
                Language = GameLanguages.Normalize(dto.Language) ?? GameLanguages.Fallback,
                Actors = dto.Actors ?? new Dictionary<string, string>(),
                Sources = dto.Sources ?? new Dictionary<string, string>(),
                Words = dto.Words ?? new Dictionary<string, string>(),
                Templates = dto.Templates ?? new Dictionary<string, string>(),
            };
        }
        catch (JsonException)
        {
            // 文件写坏了也不该让编辑器起不来，退回空表，上层还有英语兜底。
            return Empty();
        }
    }

    /// <summary>
    /// 找 <c>language/sentences</c> 文件夹。先看工作目录（从仓库根目录启动时就是它，
    /// 最直观），再顺着程序目录往上找几层（从构建输出目录或发布目录运行时都能命中）。
    ///
    /// 只认带 <c>language/</c> 前缀的新位置。旧的 <c>sentences/</c> 不再探测：
    /// 探测两处会让"当前用的是哪份文件"变得含糊，而迁移只发生一次、代价很低。
    /// </summary>
    private static string? Probe()
    {
        string cwd = Path.Combine(Directory.GetCurrentDirectory(), "language", "sentences");

        if (Directory.Exists(cwd))
        {
            return cwd;
        }

        string? probe = AppContext.BaseDirectory;

        for (int i = 0; i < 8 && probe is not null; i++)
        {
            string candidate = Path.Combine(probe, "language", "sentences");

            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            probe = Path.GetDirectoryName(probe);
        }

        // 没找到就返回程序目录下的期望位置，界面能据此提示用户把文件夹放哪。
        return Path.Combine(AppContext.BaseDirectory, "language", "sentences");
    }
}
