using ModForge.Core.Model;

namespace ModForge.Core.Modules.Files;

/// <summary>（收进来的素材数，清掉的素材数，写出的源码数）。</summary>
public sealed record SaveReport(int Imported, int Removed, int SourcesWritten);

/// <summary>
/// 把一份工程读写成一整个文件夹（布局见 <see cref="ProjectLayout"/>）。
///
/// 这里是"保存"与"打开"的**唯一**入口：写的时候一个实体一个文件，读的时候原样拼回
/// 内存里的 <see cref="ModProject"/>。中间的每一个 XxxXml 只管自己那一种文件，
/// 落地顺序与相互引用（谁先读、文本怎么回填）都在这一层定。
///
/// 保存的顺序是有意的：
///   1. 先收素材、清素材——引用会变，后面写出来的东西才是最终样子；
///   2. 再写 About.xml、语言、Create、源码。
/// 中途失败最坏留下的是"素材多收了几张"，而不是"定义引用了不存在的图"。
/// </summary>
public static class ProjectStore
{
    /* ================================================================== 保存 */

    /// <param name="toPng">把封面转成 PNG 的办法（见 <see cref="ProjectAssets.Import"/>）。</param>
    public static SaveReport Save(
        ModProject project,
        ProjectLayout layout,
        Func<string?, string, string?> resolve,
        IReadOnlyDictionary<string, string>? sources = null,
        Func<string, string?>? toPng = null,
        Action<string>? log = null)
    {
        // 先在写盘**之前**查一遍：翻译键是不是能当 XML 元素名。写了一半才发现的话，
        // 留下的是半份工程，比直接拒绝保存更难收拾。
        Preflight(project, layout);

        // 写出去的是**标准形**（与读进来时同一套规整）：同一件事只有一种写法，
        // 磁盘上的文件才不会有"两种都行、行为却不同"的余地。
        ModProject.Normalize(project);

        layout.EnsureCreated();

        ProjectAssets.Report assets = ProjectAssets.Import(project, layout, resolve, toPng, log);
        int removed = ProjectAssets.Clean(project, layout, log);

        AboutXml.Write(layout.AboutFile, project);

        int written = WriteLanguages(project, layout, log);
        WriteDefinitions(project, layout, log);

        int sourcesWritten = sources is null ? 0 : WriteSources(layout, sources, log);

        log?.Invoke($"已保存到 {layout.Root}");

        return new SaveReport(assets.Imported, removed, sourcesWritten + written);
    }

    /// <summary>保存前的整体检查：不通过就直接拒绝，不写任何东西。</summary>
    private static void Preflight(ModProject project, ProjectLayout layout)
    {
        foreach (CardDef card in project.Cards)
        {
            Require(LanguageXml.CardEntry(project, card), layout, card.Id, "卡牌");
        }

        foreach (PowerDef power in project.Powers)
        {
            Require(LanguageXml.PowerEntry(project, power), layout, power.Id, "状态");
        }
    }

    private static void Require(string entry, ProjectLayout layout, string id, string what)
    {
        string key = entry + ".title";

        if (!LanguageXml.IsValidKey(key))
        {
            throw new InvalidOperationException(
                $"{what} \"{id}\" 的名字 \"{key}\" 不能用作翻译键。" +
                "多半是工程的 id 或类型前缀以数字开头——改成字母开头即可。");
        }
    }

    /* ------------------------------------------------------------------ 语言 */

    /// <summary>
    /// 工程实际需要写出的语言：所有内容里填过的语言，并**始终包含英语**。
    ///
    /// 英语必定写出，因为它是游戏的兜底语言；其余语言只在实际填过时才写，未填的语言靠
    /// 游戏自身的回退机制显示英语。
    /// </summary>
    public static IReadOnlyCollection<string> EffectiveLanguages(ModProject project)
    {
        var languages = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            GameLanguages.Fallback,
        };

        foreach (CardDef card in project.Cards)
        {
            foreach (string language in card.Name.Languages)
            {
                languages.Add(language);
            }

            foreach (string language in card.Description.Languages)
            {
                languages.Add(language);
            }
        }

        foreach (PowerDef power in project.Powers)
        {
            foreach (string language in power.Name.Languages)
            {
                languages.Add(language);
            }

            foreach (string language in power.Description.Languages)
            {
                languages.Add(language);
            }
        }

        return languages;
    }

    private static int WriteLanguages(ModProject project, ProjectLayout layout, Action<string>? log)
    {
        Directory.CreateDirectory(layout.LanguageDirectory);

        var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int written = 0;

        foreach (string language in EffectiveLanguages(project))
        {
            string? code = GameLanguages.Normalize(language);

            if (code is null)
            {
                log?.Invoke($"[warn] 跳过无法识别的语言代码 \"{language}\"");
                continue;
            }

            wanted.Add(code);

            Dictionary<string, string> cards = CardTable(project, code);
            Dictionary<string, string> powers = PowerTable(project, code);

            // 英语即使是空的也要留一个文件：它是兜底语言，"没有这个语言"与"这个语言是空的"
            // 对游戏来说是两回事，前者会去读别的表。
            if (cards.Count > 0 || powers.Count > 0 ||
                code.Equals(GameLanguages.Fallback, StringComparison.OrdinalIgnoreCase))
            {
                LanguageXml.Write(layout.LocalizationFile(code), cards, powers);
                written++;
            }
        }

        RemoveStaleLanguages(layout, wanted, log);

        return written;
    }

    /// <summary>
    /// 删掉工程里已经没有内容的语言。
    ///
    /// 必须删：否则在界面上把某种语言的文本清空之后，那份文件还留在磁盘上，下次打开又把它
    /// 读回来——用户会觉得"翻译删不掉"。
    ///
    /// 只动我们自己写的那个 <c>Localization.xml</c>：文件夹里若还有别的东西（用户自己放的
    /// 参考文件之类）就整份留着，只有真的空了才连文件夹一起删。
    /// </summary>
    private static void RemoveStaleLanguages(
        ProjectLayout layout,
        ICollection<string> wanted,
        Action<string>? log)
    {
        if (!Directory.Exists(layout.LanguageDirectory))
        {
            return;
        }

        foreach (string directory in Directory.EnumerateDirectories(layout.LanguageDirectory))
        {
            string code = Path.GetFileName(directory);

            if (wanted.Contains(code))
            {
                continue;
            }

            string file = Path.Combine(directory, LanguageXml.FileName);

            if (File.Exists(file))
            {
                try
                {
                    File.Delete(file);
                    log?.Invoke($"已删掉不再有内容的语言：{code}");
                }
                catch (IOException ex)
                {
                    log?.Invoke($"删除 {code} 失败：{ex.Message}");
                    continue;
                }
            }

            if (!Directory.EnumerateFileSystemEntries(directory).Any())
            {
                Directory.Delete(directory);
            }
        }
    }

    /// <summary>某语言的卡牌翻译表：键 → 文本（键形如 <c>FORGE_STRIKE.title</c>）。</summary>
    private static Dictionary<string, string> CardTable(ModProject project, string language)
    {
        var table = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (CardDef card in project.Cards)
        {
            string entry = LanguageXml.CardEntry(project, card);

            if (card.Name.Has(language))
            {
                table[$"{entry}.title"] = card.Name[language];
            }

            if (card.Description.Has(language))
            {
                table[$"{entry}.description"] = card.Description[language];
            }
        }

        return table;
    }

    /// <summary>某语言的状态翻译表。</summary>
    private static Dictionary<string, string> PowerTable(ModProject project, string language)
    {
        var table = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (PowerDef power in project.Powers)
        {
            string entry = LanguageXml.PowerEntry(project, power);

            if (power.Name.Has(language))
            {
                table[$"{entry}.title"] = power.Name[language];
            }

            if (power.Description.Has(language))
            {
                table[$"{entry}.description"] = power.Description[language];
            }
        }

        return table;
    }

    /* ------------------------------------------------------------------ 定义 */

    private static void WriteDefinitions(ModProject project, ProjectLayout layout, Action<string>? log)
    {
        var cards = new Dictionary<string, CardDef>(StringComparer.OrdinalIgnoreCase);

        foreach (CardDef card in project.Cards)
        {
            string name = ProjectLayout.FileName(card.Id);

            if (!cards.TryAdd(name, card))
            {
                log?.Invoke($"[warn] 两张卡都写向 {name}.xml，后面的那张没写出去（id 撞了？）");
                continue;
            }

            CardXml.Write(Path.Combine(layout.CreateCardsDirectory, name + ".xml"), card);
        }

        RemoveStale(layout.CreateCardsDirectory, cards.Keys, log, "卡牌", FileKey);

        var effects = new Dictionary<string, SharedEffectDef>(StringComparer.OrdinalIgnoreCase);

        foreach (SharedEffectDef shared in project.SharedEffects)
        {
            string name = ProjectLayout.FileName(shared.Id);

            if (!effects.TryAdd(name, shared))
            {
                log?.Invoke($"[warn] 两个共享效果都写向 {name}.xml，后面的那个没写出去（id 撞了？）");
                continue;
            }

            SharedEffectXml.Write(Path.Combine(layout.CreateEffectsDirectory, name + ".xml"), shared);
        }

        RemoveStale(layout.CreateEffectsDirectory, effects.Keys, log, "共享效果", FileKey);

        var powers = new Dictionary<string, PowerDef>(StringComparer.OrdinalIgnoreCase);

        foreach (PowerDef power in project.Powers)
        {
            string name = ProjectLayout.FileName(power.Id);

            if (!powers.TryAdd(name, power))
            {
                log?.Invoke($"[warn] 两个状态都写向 {name}.xml，后面的那个没写出去（id 撞了？）");
                continue;
            }

            PowerXml.Write(Path.Combine(layout.CreatePowersDirectory, name + ".xml"), power);
        }

        RemoveStale(layout.CreatePowersDirectory, powers.Keys, log, "状态", FileKey);

        var skins = new Dictionary<string, SkinDef>(StringComparer.OrdinalIgnoreCase);

        foreach (SkinDef skin in project.Skins)
        {
            string name = ProjectLayout.FileName(skin.Target);

            if (!skins.TryAdd(name, skin))
            {
                log?.Invoke($"[warn] 两个皮肤都换同一个角色（{skin.Target}），后面的那个没写出去");
                continue;
            }

            SkinXml.Write(Path.Combine(layout.CreateSkinsDirectory, name + ".xml"), skin);
        }

        RemoveStale(layout.CreateSkinsDirectory, skins.Keys, log, "皮肤", FileKey);
    }

    /* ------------------------------------------------------------------ 源码 */

    /// <summary>
    /// 把生成出来的源码写进 Source/，供查看与对照。内容变了才写，避免反复落盘。
    /// 不再生成的旧文件会删掉——那个文件夹整体是产物，留着上一版只会让人看错。
    /// </summary>
    public static int WriteSources(
        ProjectLayout layout,
        IReadOnlyDictionary<string, string> files,
        Action<string>? log = null)
    {
        Directory.CreateDirectory(layout.SourceDirectory);
        int written = 0;

        var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach ((string relative, string content) in files)
        {
            string target = Path.Combine(
                layout.SourceDirectory,
                relative.Replace('/', Path.DirectorySeparatorChar));

            // 只接受目录内的路径，避免生成器给出的名字里带 .. 逃出去。
            if (!ProjectLayout.IsInside(layout.SourceDirectory, target))
            {
                continue;
            }

            wanted.Add(SourceKey(relative));

            if (File.Exists(target) && File.ReadAllText(target) == content)
            {
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(target, content);
            written++;
        }

        // 源码可以是子目录里的（Cards/EmberStrike.cs），所以键要带上相对路径；
        // 而 Create/ 里的定义是平铺的，键就是文件名。
        RemoveStale(layout.SourceDirectory, wanted, log, "源码", SourceKey, recursive: true);

        return written;
    }

    /// <summary>源码文件的键：相对 Source/ 的路径（不带扩展名，分隔符统一成 /）。</summary>
    private static string SourceKey(string relative) =>
        (Path.ChangeExtension(relative, null) ?? relative).Replace('\\', '/');

    /// <summary>定义的键：文件名去掉扩展名。</summary>
    private static string FileKey(string relative) =>
        Path.GetFileNameWithoutExtension(relative);

    /// <summary>
    /// 删掉文件夹里不再生成的文件。
    ///
    /// 必须删：否则在编辑器里删掉一张卡之后，它的文件还留在 Create/ 里，下次打开又"复活"了
    /// ——用户会觉得删不掉。每一份都写一行日志，删了什么看得见。
    /// </summary>
    private static void RemoveStale(
        string directory,
        ICollection<string> wanted,
        Action<string>? log,
        string what,
        Func<string, string> keyOf,
        bool recursive = false)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (string file in Directory.EnumerateFiles(
                     directory,
                     recursive ? "*" : "*.xml",
                     recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly))
        {
            string relative = Path.GetRelativePath(directory, file).Replace('\\', '/');
            string key = keyOf(relative);

            if (wanted.Contains(key))
            {
                continue;
            }

            try
            {
                File.Delete(file);
                log?.Invoke($"已删掉不再存在的{what}：{Path.GetFileName(file)}");
            }
            catch (IOException ex)
            {
                log?.Invoke($"删除 {Path.GetFileName(file)} 失败：{ex.Message}");
            }
        }
    }

    /* ================================================================== 打开 */

    /// <summary>打开的结果：读出来的工程，加上读取过程中记下的所有问题。</summary>
    public sealed record LoadResult(ModProject Project, IReadOnlyList<ReadIssue> Issues)
    {
        /// <summary>读的时候有没有遇到"这一处彻底读不出来"的问题。</summary>
        public bool HasFatal => Issues.Any(i => i.Fatal);
    }

    /// <summary>
    /// 读一份工程文件夹。
    ///
    /// 读的顺序：先是 About.xml（id 与类型前缀在它里面，后面算翻译键要用），
    /// 再是语言表（名字与描述在语言文件里），最后才是 Create/ 里的定义——
    /// 定义本身不含文本，读完再把语言表里的文本回填进去。
    ///
    /// **坏一处不影响其它**：单个字段写错退回默认值、一条效果读不动只丢那一条、
    /// 一个文件整个读不出来只丢那一个实体。每一处都记进返回的
    /// <see cref="LoadResult.Issues"/>，由界面弹出来——不能因为局部损坏就让整份工程打不开，
    /// 也不能一声不吭地降级。
    /// </summary>
    public static LoadResult Load(ProjectLayout layout, Action<string>? log = null)
    {
        var issues = new List<ReadIssue>();
        var project = new ModProject();

        AboutXml.AboutData about;

        using (ReadLog read = Open(layout.AboutFile, issues))
        {
            about = ReadFile(layout.AboutFile, read, path => AboutXml.Read(path, read)) ?? AboutXml.Default;
        }

        AboutXml.Apply(about, project);

        // id 空着时（About.xml 缺 <packageId>）用文件夹名顶上：工程总得有个身份，
        // 而文件夹名是它最自然的身份。
        if (string.IsNullOrWhiteSpace(project.Id))
        {
            project.Id = Path.GetFileName(layout.Root.TrimEnd(Path.DirectorySeparatorChar));
        }

        // 封面：**文件名固定 Preview.png**。About.xml 里那一项只是记录，实际以文件为准；
        // 文件不在就当没有封面（这正是"NULL 防御"——缺了不该让别的地方出问题）。
        project.PreviewFile = File.Exists(layout.PreviewFile)
            ? Path.GetFileName(layout.PreviewFile)
            : null;

        Dictionary<string, Dictionary<string, string>> languages = ReadLanguages(layout, issues, log);
        var claimed = new HashSet<string>(StringComparer.Ordinal);

        ReadCards(project, layout, languages, claimed, issues, log);
        ReadPowers(project, layout, languages, claimed, issues, log);
        ReadSharedEffects(project, layout, issues, log);
        ReadSkins(project, layout, issues, log);

        ReportUnmatched(languages, claimed, log);

        foreach (ReadIssue issue in issues)
        {
            log?.Invoke($"[读取] {issue.Describe()}");
        }

        return new LoadResult(project, issues);
    }

    /// <summary>开一个读取记录器：它读到的每一处问题都会并进那份总表。</summary>
    private static ReadLog Open(string file, List<ReadIssue> issues) =>
        new(file, read => issues.AddRange(read));

    private static Dictionary<string, Dictionary<string, string>> ReadLanguages(
        ProjectLayout layout,
        List<ReadIssue> issues,
        Action<string>? log)
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        if (!Directory.Exists(layout.LanguageDirectory))
        {
            return result;
        }

        foreach (string directory in Directory.EnumerateDirectories(layout.LanguageDirectory))
        {
            string? code = GameLanguages.Normalize(Path.GetFileName(directory));

            if (code is null)
            {
                log?.Invoke($"[warn] 语言文件夹 \"{Path.GetFileName(directory)}\" 不是游戏认识的语言，已跳过");
                continue;
            }

            string file = Path.Combine(directory, LanguageXml.FileName);

            if (!File.Exists(file))
            {
                continue;
            }

            using ReadLog read = Open(file, issues);
            LanguageXml.Tables tables = LanguageXml.Read(file, read);
            var merged = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach ((string key, string text) in tables.Cards)
            {
                merged[key] = text;
            }

            foreach ((string key, string text) in tables.Powers)
            {
                merged[key] = text;
            }

            result[code] = merged;
        }

        return result;
    }

    private static void ReadCards(
        ModProject project,
        ProjectLayout layout,
        Dictionary<string, Dictionary<string, string>> languages,
        HashSet<string> claimed,
        List<ReadIssue> issues,
        Action<string>? log)
    {
        foreach (string file in Files(layout.CreateCardsDirectory))
        {
            using ReadLog read = Open(file, issues);

            if (ReadFile(file, read, path => CardXml.Read(path, read)) is not CardDef card)
            {
                continue;
            }

            WarnOnNameMismatch(file, card.Id, log);

            string entry = LanguageXml.CardEntry(project, card);
            card.Name = Text(languages, claimed, entry + ".title");
            card.Description = Text(languages, claimed, entry + ".description");

            project.Cards.Add(card);
        }
    }

    private static void ReadPowers(
        ModProject project,
        ProjectLayout layout,
        Dictionary<string, Dictionary<string, string>> languages,
        HashSet<string> claimed,
        List<ReadIssue> issues,
        Action<string>? log)
    {
        foreach (string file in Files(layout.CreatePowersDirectory))
        {
            using ReadLog read = Open(file, issues);

            if (ReadFile(file, read, path => PowerXml.Read(path, read)) is not PowerDef power)
            {
                continue;
            }

            WarnOnNameMismatch(file, power.Id, log);

            string entry = LanguageXml.PowerEntry(project, power);
            power.Name = Text(languages, claimed, entry + ".title");
            power.Description = Text(languages, claimed, entry + ".description");

            project.Powers.Add(power);
        }
    }

    private static void ReadSharedEffects(
        ModProject project,
        ProjectLayout layout,
        List<ReadIssue> issues,
        Action<string>? log)
    {
        foreach (string file in Files(layout.CreateEffectsDirectory))
        {
            using ReadLog read = Open(file, issues);

            if (ReadFile(file, read, path => SharedEffectXml.Read(path, read)) is not SharedEffectDef shared)
            {
                continue;
            }

            WarnOnNameMismatch(file, shared.Id, log);
            project.SharedEffects.Add(shared);
        }
    }

    /// <summary>
    /// 读一个文件：**文件整个坏了**（XML 语法错、根元素不对、读不到）时记一条致命问题并返回 null，
    /// 其余情况原样返回读出来的实体。
    ///
    /// 这是"一个文件坏了不影响别的文件"的落点：从目录里读一批文件时，坏的那一份只是少一个实体，
    /// 而不是让整份工程打不开。
    /// </summary>
    private static T? ReadFile<T>(string file, ReadLog read, Func<string, T?> parse) where T : class
    {
        try
        {
            return parse(file);
        }
        catch (ProjectXmlException ex)
        {
            read.Fatal(ex.Message);
            return null;
        }
        catch (IOException ex)
        {
            read.Fatal($"读不了这个文件：{ex.Message}");
            return null;
        }
    }

    private static void ReadSkins(
        ModProject project,
        ProjectLayout layout,
        List<ReadIssue> issues,
        Action<string>? log)
    {
        foreach (string file in Files(layout.CreateSkinsDirectory))
        {
            using ReadLog read = Open(file, issues);

            if (ReadFile(file, read, path => SkinXml.Read(path, read)) is not SkinDef skin)
            {
                continue;
            }

            WarnOnNameMismatch(file, skin.Target, log);
            project.Skins.Add(skin);
        }
    }

    /// <summary>某个翻译键在所有语言下的文本。没有任何语言填过时返回**空**文本。</summary>
    private static LocalizedText Text(
        Dictionary<string, Dictionary<string, string>> languages,
        HashSet<string> claimed,
        string key)
    {
        var text = new LocalizedText();

        foreach ((string language, Dictionary<string, string> table) in languages)
        {
            if (table.TryGetValue(key, out string? value))
            {
                text[language] = value;
                claimed.Add($"{language}\u0000{key}");
            }
        }

        return text;
    }

    /// <summary>
    /// 语言文件里有、但工程里没有对应条目的键。
    ///
    /// 多半是改名留下的：卡的 id 改了，翻译键跟着变，旧的那几行就没人认领了。不报的话
    /// 表现是"翻译莫名其妙没了"，而这行日志能直接指出是哪几个键。
    /// </summary>
    private static void ReportUnmatched(
        Dictionary<string, Dictionary<string, string>> languages,
        HashSet<string> claimed,
        Action<string>? log)
    {
        var orphans = new List<string>();

        foreach ((string language, Dictionary<string, string> table) in languages)
        {
            foreach (string key in table.Keys)
            {
                if (!claimed.Contains($"{language}\u0000{key}"))
                {
                    orphans.Add($"{language}: {key}");
                }
            }
        }

        if (orphans.Count > 0)
        {
            log?.Invoke(
                $"[warn] 有 {orphans.Count} 条翻译没有对应的卡牌/状态（改过 id 的话就会这样）：" +
                string.Join("、", orphans.Take(10)) + (orphans.Count > 10 ? " …" : ""));
        }
    }

    private static IEnumerable<string> Files(string directory) =>
        Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, "*.xml").OrderBy(f => f, StringComparer.Ordinal)
            : Enumerable.Empty<string>();

    /// <summary>文件名与里面的 id 不一致时提醒一句（多半是手工改过名）。以文件里的 id 为准。</summary>
    private static void WarnOnNameMismatch(string file, string id, Action<string>? log)
    {
        string name = Path.GetFileNameWithoutExtension(file);

        if (!name.Equals(id, StringComparison.OrdinalIgnoreCase))
        {
            log?.Invoke($"[warn] {name}.xml 里的 id 是 {id}，以文件里的为准");
        }
    }
}
