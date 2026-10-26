using System.Text.Json;
using ModForge.Core.Compile;
using ModForge.Core.Generate;
using ModForge.Core.Model;
using ModForge.Core.Modules.CardPicking;
using ModForge.Core.Pck;

namespace ModForge.Core.Export;

public sealed record ExportResult(
    bool Success,
    string? OutputDirectory,
    IReadOnlyList<string> Messages,
    IReadOnlyList<string> ArtifactPaths);

/// <summary>
/// Turns a project into a loadable mod folder: manifest, compiled assembly,
/// localization PCK, and any loose art files.
/// </summary>
public sealed class Exporter
{
    private readonly string _gameDir;

    /// <summary>
    /// Converts PNG art to WebP during export. The game's texture loader only accepts
    /// WebP, so this keeps the common case (a plain PNG) working.
    /// </summary>
    public Gfx.WebpConverter? WebpConverter { get; set; }

    /// <summary>
    /// 卡图引用的解析基准（工作目录）。
    ///
    /// 工程里存的是相对 cardart 的路径，导出时要还原成磁盘路径才能读文件。不设置时按
    /// 原样当作路径用（那时工程里存的应是绝对路径），所以旧工程与测试工具仍然能用。
    /// </summary>
    public Workspace? Workspace { get; set; }

    public Exporter(string gameDir)
    {
        _gameDir = gameDir;
    }

    /// <summary>把卡图引用解析成可读的磁盘路径。</summary>
    private string ResolveArt(string artReference) =>
        Workspace?.ResolveCardArt(artReference) ?? artReference;

    /// <summary>
    /// 把工程导出成一个可加载的 mod 文件夹，落在 <paramref name="outputRoot"/> \ 工程 id 下。
    ///
    /// outputRoot 就是设置里那个"导出路径"，产物直接放它下面、**不再套一层 mods/**。
    /// 这样把导出路径指到游戏的 mods 目录，产物就已经在对的位置，不必再手动搬一层；
    /// 指到别处时也符合"导出路径 = 产物放哪"的直觉。返回的 OutputDirectory 是 mod 文件夹本身。
    /// </summary>
    public ExportResult Export(ModProject project, string outputRoot)
    {
        var messages = new List<string>();
        var artifacts = new List<string>();

        string modDir = Path.Combine(outputRoot, project.Id);
        Directory.CreateDirectory(modDir);

        // 1. Localization and effect data go into the PCK. Only JSON is packed:
        // Godot does not import .json, so a hand-written pack of JSON is safe, while
        // raw images would need Godot-imported .ctex entries we cannot synthesize.
        var packFiles = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        AddLocalization(packFiles, project, EffectiveLanguages(project), messages);
        packFiles[$"{project.Id}/data/project.json"] =
            System.Text.Encoding.UTF8.GetBytes(project.ToJson());

        // 1b. Skins: copy the selected source packs' assets and their slot bindings.
        foreach (SkinDef skin in project.Skins)
        {
            AddSkin(packFiles, skin, project.Id, messages);
        }

        // 1c. Card art: wrap each image in a .ctex so the game's texture loader reads
        // it straight from the pack, rather than the mod having to load loose files.
        AddCardArt(packFiles, project, messages);

        string pckPath = Path.Combine(modDir, project.Id + ".pck");
        PckFile.Write(pckPath, packFiles);
        artifacts.Add(pckPath);
        messages.Add($"Packed {packFiles.Count} localization/data entries.");

        // 2. Generate and compile the assembly.
        var generator = new CodeGenerator(project);
        IReadOnlyDictionary<string, string> sources = generator.GenerateAll();
        CompileResult compile = ModCompiler.Compile(sources, _gameDir, $"ModForge.{project.Id}");
        foreach (CompileDiagnostic diag in compile.Diagnostics.Take(40))
        {
            string where = diag.File is null ? "" : $" ({Path.GetFileName(diag.File)}:{diag.Line})";
            messages.Add($"  [{diag.Severity}] {diag.Id}: {diag.Message}{where}");
        }

        if (!compile.Success || compile.Assembly is null)
        {
            messages.Add("Compilation failed; no mod was produced.");
            return new ExportResult(false, modDir, messages, artifacts);
        }

        string dllPath = Path.Combine(modDir, project.Id + ".dll");
        File.WriteAllBytes(dllPath, compile.Assembly);
        artifacts.Add(dllPath);
        messages.Add($"Compiled {Path.GetFileName(dllPath)} ({compile.Assembly.Length} bytes).");

        // 3. Manifest. The DLL file name must equal the manifest id.
        string manifestJson = BuildManifest(project);
        string manifestPath = Path.Combine(modDir, project.Id + ".json");
        File.WriteAllText(manifestPath, manifestJson);
        artifacts.Add(manifestPath);

        messages.Add($"Mod written to {modDir}");
        messages.Add("Install by copying this folder into the game's mods directory, then launch the game.");

        return new ExportResult(true, modDir, messages, artifacts);
    }

    /// <summary>
    /// Packs custom card art as .ctex textures and records the route for each card.
    ///
    /// A .ctex is used because the engine's texture loader unpacks it directly, so no
    /// separate import step is needed and the artwork ships inside the pack. Each file
    /// is stored under this mod's own prefix to keep it clear of the game's assets, and
    /// the generated card overrides its portrait path to point at it, which is the same
    /// approach used by released card mods.
    /// </summary>
    private void AddCardArt(
        Dictionary<string, byte[]> packFiles,
        ModProject project,
        List<string> messages)
    {
        int added = 0;

        foreach (CardDef card in project.Cards)
        {
            if (string.IsNullOrWhiteSpace(card.ArtFile))
            {
                continue;
            }

            // 工程里存的是相对 cardart 的引用，这里还原成磁盘路径。
            string artPath = ResolveArt(card.ArtFile);

            if (!File.Exists(artPath))
            {
                messages.Add($"[warn] Art file for '{card.Id}' not found: {card.ArtFile}");
                continue;
            }

            string source = artPath;

            // The game only ships a WebP unpacker, so convert other formats rather than
            // packing art that would fail to load.
            if (!IsWebpFile(source))
            {
                if (WebpConverter is { IsAvailable: true })
                {
                    try
                    {
                        source = WebpConverter.Convert(source);
                        messages.Add(
                            $"Card art '{card.Id}': converted {Path.GetFileName(card.ArtFile)} to WebP.");
                    }
                    catch (Exception ex)
                    {
                        messages.Add($"[warn] Card art '{card.Id}' conversion failed: {ex.Message}");
                        continue;
                    }
                }
                else
                {
                    messages.Add(
                        $"[warn] Card art '{card.Id}' skipped: the game requires WebP, and no Godot " +
                        "installation was found to convert the image. Supply a .webp file or point the " +
                        "editor at Godot.");
                    continue;
                }
            }

            byte[]? ctex = Gfx.CtexWriter.FromFile(source, out string? reason);
            if (ctex is null)
            {
                messages.Add($"[warn] Art for '{card.Id}' was skipped: {reason}");
                continue;
            }

            string entry = ModelIds.Slugify(project.CardTypeName(card)).ToLowerInvariant();
            string route = $"{project.Id}/images/card_art/{entry}.ctex";
            packFiles[route] = ctex;

            messages.Add($"Card art '{card.Id}': {ctex.Length} bytes packed as {route}");
            added++;
        }

        if (added > 0)
        {
            messages.Add($"Packed custom art for {added} card(s).");
        }
    }

    /// <summary>True when the file starts with a RIFF/WEBP header.</summary>
    private static bool IsWebpFile(string path)
    {
        try
        {
            using FileStream stream = File.OpenRead(path);
            Span<byte> head = stackalloc byte[12];
            if (stream.Read(head) < head.Length)
            {
                return false;
            }

            return head[0] == 'R' && head[1] == 'I' && head[2] == 'F' && head[3] == 'F' &&
                   head[8] == 'W' && head[9] == 'E' && head[10] == 'B' && head[11] == 'P';
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <summary>The in-pack route a card's custom art is stored at.</summary>
    public static string CardArtRoute(ModProject project, CardDef card) =>
        $"{project.Id}/images/card_art/{ModelIds.Slugify(project.CardTypeName(card)).ToLowerInvariant()}.ctex";

    /// <summary>
    /// Copies a source skin package's assets into the generated mod and writes the
    /// slot-binding table the runtime reads.
    ///
    /// The pack's already-imported Godot resources are copied verbatim rather than
    /// rebuilt, because Godot import artifacts (.ctex/.spatlas/.spskel) cannot be
    /// synthesized outside the editor but are exactly what the game loads.
    /// </summary>
    private void AddSkin(
        Dictionary<string, byte[]> packFiles,
        SkinDef skin,
        string exportedRoutesPrefix,
        List<string> messages)
    {
        // 工程里存的是相对 Skins/ 的文件名，这里还原成磁盘路径。
        string pack = Workspace?.ResolveSkinPack(skin.SourcePack) ?? skin.SourcePack;

        if (!File.Exists(pack))
        {
            messages.Add($"[warn] Skin source pack not found: {skin.SourcePack}");
            return;
        }

        Dictionary<string, byte[]> source = PckFile.Read(pack);
        int copied = 0;

        foreach ((string route, byte[] data) in source)
        {
            if (!PckFile.IsSafePackPath(route))
            {
                continue;
            }

            // Every asset travels under its original route so the skin's own assets
            // resolve exactly as they did in the source mod.
            packFiles[route] = data;
            copied++;
        }

        // Slot bindings, read by the generated runtime.
        var bindings = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach ((string slot, string animation) in skin.SlotBindings)
        {
            if (!string.IsNullOrWhiteSpace(animation))
            {
                bindings[slot] = animation;
            }
        }

        var table = new
        {
            target = skin.Target,
            name = skin.Name,
            bindings,
        };

        packFiles[$"{exportedRoutesPrefix}/skins/{Slug(skin.Target)}.json"] =
            System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
                table,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

        messages.Add($"Skin '{skin.Name}' ({skin.Target}): copied {copied} asset(s), " +
                     $"{bindings.Count} slot binding(s).");
    }

    private static string Slug(string text) => new string(
        text.Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '_').ToArray()).Trim('_');

    /// <summary>
    /// 项目实际需要写出的语言：所有内容里填过的语言，并始终包含英语。
    ///
    /// 英语必定写出，因为它是游戏的兜底语言；其余语言只在实际填过时才写，未填的语言靠
    /// 游戏自身的回退机制显示英语。
    ///
    /// 与工程文件夹里写出的那几份 <c>Language/</c> 是同一套判据——两处各写一份的话，
    /// "工程里有 zhs、导出的包里没有"这种偏差迟早出现。
    /// </summary>
    public static IReadOnlyCollection<string> EffectiveLanguages(ModProject project) =>
        Modules.Files.ProjectStore.EffectiveLanguages(project);

    /// <summary>
    /// 写出每种已填写语言的本地化表。
    ///
    /// 只写真正填过的语言：游戏对缺失的语言会回退到英语，多写一份空的反而会覆盖掉兜底，
    /// 让玩家看到空白文本。所以英语是必须填的，其它语言按需补充。
    /// </summary>
    private static void AddLocalization(
        Dictionary<string, byte[]> packFiles,
        ModProject project,
        IReadOnlyCollection<string> languages,
        List<string> messages)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        // 英语先算出来，其一作为其余语言缺失条目的兜底（与游戏行为一致），
        // 其二用来提示哪些语言其实还是英文。
        Dictionary<string, string> englishCards = BuildCards(project, GameLanguages.Fallback);
        Dictionary<string, string> englishPowers = BuildPowers(project, GameLanguages.Fallback);

        int written = 0;

        foreach (string language in languages.Distinct().OrderBy(l => l, StringComparer.Ordinal))
        {
            Dictionary<string, string> cards = BuildCards(project, language);
            Dictionary<string, string> powers = BuildPowers(project, language);

            // 英语必须写出：它是游戏的兜底语言，缺了它未翻译的语言会整片空白。
            // 其它语言才需要判断是否真的翻译过，避免生成一份与英语逐字相同的文件。
            bool isFallback = language.Equals(GameLanguages.Fallback, StringComparison.OrdinalIgnoreCase);
            bool keepCards = cards.Count > 0 &&
                             (isFallback || !MatchesExactly(cards, englishCards));
            bool keepPowers = powers.Count > 0 &&
                              (isFallback || !MatchesExactly(powers, englishPowers));

            if (keepCards)
            {
                packFiles[$"{project.Id}/localization/{language}/cards.json"] =
                    JsonSerializer.SerializeToUtf8Bytes(cards, options);
            }

            if (keepPowers)
            {
                packFiles[$"{project.Id}/localization/{language}/powers.json"] =
                    JsonSerializer.SerializeToUtf8Bytes(powers, options);
            }

            int writtenCards = keepCards ? cards.Count / 2 : 0;
            int writtenPowers = keepPowers ? powers.Count / 2 : 0;

            if (writtenCards > 0 || writtenPowers > 0)
            {
                written++;
                messages.Add($"本地化 {language}: {writtenCards} 张卡牌, {writtenPowers} 个状态。");
            }
            else if (cards.Count > 0 || powers.Count > 0)
            {
                messages.Add($"本地化 {language}: 与英语完全相同，已跳过。");
            }
        }

        if (!languages.Contains(GameLanguages.Fallback, StringComparer.OrdinalIgnoreCase))
        {
            messages.Add("[warn] 未填写英语文本：游戏在其他语言缺失时会回退到英语，建议至少填好英语。");
        }

        messages.Add($"本地化合计写出 {written} 种语言。");
    }

    /// <summary>
    /// 判断一张文本表是否与英语逐条相同，用于跳过没有实际翻译的语言。
    ///
    /// 注意英语本身永远不参与这个判断（它与自己必然相同），否则兜底语言会被跳过。
    /// </summary>
    private static bool MatchesExactly(
        Dictionary<string, string> candidate,
        Dictionary<string, string> english)
    {
        if (candidate.Count == 0)
        {
            return true;
        }

        foreach ((string key, string value) in candidate)
        {
            if (!english.TryGetValue(key, out string? englishValue) || englishValue != value)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>某语言的卡牌文本表，缺失条目用英语兜底，行为与游戏一致。</summary>
    private static Dictionary<string, string> BuildCards(ModProject project, string language)
    {
        var cards = new Dictionary<string, string>(StringComparer.Ordinal);
        bool zh = language.StartsWith("zh", StringComparison.OrdinalIgnoreCase);

        foreach (CardDef card in project.Cards)
        {
            string entry = Modules.Files.LanguageXml.CardEntry(project, card);
            cards[$"{entry}.title"] = card.Name.GetOrFallback(language);
            cards[$"{entry}.description"] = card.Description.GetOrFallback(language);

            // "让玩家挑牌"的效果要在选牌界面上显示一句提示。游戏要求提示语来自**这张牌自己的**
            // cards 表条目（见 CardModel.SelectionScreenPrompt：它读 `cards` 表里的
            // `<条目>.selectionScreenPrompt`），取不到就抛异常。
            //
            // 没有这个条目时不是"少一句话"，而是打出那张牌直接崩——所以只在确实用到选择类效果
            // 时才写，写就必须写全。
            int chooseCount = CountChooseEffects(card);
            if (chooseCount > 0)
            {
                cards[$"{entry}.selectionScreenPrompt"] = SelectionPrompt(zh, card, chooseCount);
            }
        }

        return cards;
    }

    /// <summary>
    /// 这条效果会不会**真的打开选择界面**（用来决定要不要写提示语、以及提示语按哪条效果写）。
    ///
    /// 三个条件都必要：
    ///   <see cref="CardPickPolicies.UsesPicker"/> —— 这个种类压根不挑牌（获得能量…）就谈不上；
    ///   <c>UsesPrompt</c> —— 挑牌但不开选择界面的也有（自动打出只取牌堆顺序）；
    ///   流程九 = 选择 —— "全部 / 随机 / 至多"里只有"选择"会弹界面要一句提示。
    ///
    /// 判据一律取自**策略**（名单只有 <see cref="CardPickPolicies"/> 一处），所以不会出现
    /// "某条效果选了'选择'却没提示语"那种游戏内直接抛异常的情况。早先这里只看 `UsesPrompt`，
    /// 而不挑牌的种类都落在策略的默认分支上（`UsesPrompt: true`），于是不挑牌的卡也会被写进
    /// 一条永远读不到的提示语——更要紧的是提示语按**第一条匹配的效果**取词，那张卡上真正会
    /// 弹界面的效果就被顶掉了（实测：一条"获得能量"排在"消耗牌（选择 1 张）"前面时，
    /// 提示语退化成通用的"选择若干张牌"）。
    /// </summary>
    private static bool OpensChooser(EffectDef effect) =>
        CardPickPolicies.UsesPicker(effect.Kind) &&
        CardPickPolicies.For(effect.Kind).UsesPrompt &&
        effect.Pick.Mode == Modules.CardPicking.PickMode.Choose;

    private static int CountChooseEffects(CardDef card) => card.Effects.Count(OpensChooser);

    /// <summary>
    /// 挑牌界面的提示语。
    ///
    /// 措辞按效果的实际作用写（附魔 / 复制 / 搬运 / 增伤 / 重放 / 降费…），而不是照抄游戏的
    /// "选择 N 张牌来升级"那套——那些说的是别的操作，会误导玩家。张数写成 {Amount}，与游戏的
    /// 提示语一样由选择界面填进去（CardSelectorPrefs 的构造函数会设好 Amount）。
    /// </summary>
    private static string SelectionPrompt(bool zh, CardDef card, int count)
    {
        EffectDef choose = card.Effects.First(OpensChooser);

        // 只用作占位符：提示语里的张数由选择界面自己填，所以这里不看具体值。
        _ = count;

        // 除了"降费 / 加费"要看数值正负，其余都按操作说。
        if (choose.Kind != EffectKind.ModifyCardCost)
        {
            (string zh_, string en_) = choose.Kind switch
            {
                EffectKind.SetCardsFree => (
                    "选择[blue]{Amount}[/blue]张牌，它们本场战斗的费用变为 0。",
                    "Choose {Amount:plural:a card|{Amount} cards} to make free to play."),

                // 消耗牌的张数就是它自己的数值（<base>），所以这一句只说"选几张来消耗"：
                // 张数由选择界面按 CardSelectorPrefs 里那个数填，算出来的也行。
                EffectKind.ExhaustCards => (
                    "选择[blue]{Amount}[/blue]张牌来[gold]消耗[/gold]。",
                    "Choose {Amount:plural:a card|{Amount} cards} to [gold]Exhaust[/gold]."),

                EffectKind.UpgradeCardsInPile => (
                    "选择[blue]{Amount}[/blue]张牌来[gold]升级[/gold]。",
                    "Choose {Amount:plural:a card|{Amount} cards} to [gold]Upgrade[/gold]."),

                EffectKind.TransformCardsInPile => (
                    "选择[blue]{Amount}[/blue]张牌来[gold]变化[/gold]。",
                    "Choose {Amount:plural:a card|{Amount} cards} to [gold]Transform[/gold]."),

                EffectKind.EnchantCardsInPile => (
                    "选择[blue]{Amount}[/blue]张牌来[gold]附魔[/gold]。",
                    "Choose {Amount:plural:a card|{Amount} cards} to [gold]Enchant[/gold]."),

                EffectKind.CopyCardsToPile => (
                    "选择[blue]{Amount}[/blue]张牌来[gold]复制[/gold]。",
                    "Choose {Amount:plural:a card|{Amount} cards} to [gold]Copy[/gold]."),

                EffectKind.MoveCardsBetweenPiles => (
                    "选择[blue]{Amount}[/blue]张牌来[gold]移动[/gold]。",
                    "Choose {Amount:plural:a card|{Amount} cards} to [gold]Move[/gold]."),

                EffectKind.BoostCardDamage => (
                    "选择[blue]{Amount}[/blue]张牌，[gold]提高[/gold]它们的伤害。",
                    "Choose {Amount:plural:a card|{Amount} cards} to [gold]increase the damage[/gold] of."),

                EffectKind.AddReplay => (
                    "选择[blue]{Amount}[/blue]张牌，[gold]增加[/gold]它们的重放次数。",
                    "Choose {Amount:plural:a card|{Amount} cards} to add [gold]Replay[/gold] to."),

                // 挑牌却不在这份名单里的，说明策略新加了一条而这里没跟上——留个明确的占位
                // 也好过悄悄退回"降费"那句误导玩家。
                _ => (
                    "选择[blue]{Amount}[/blue]张牌。",
                    "Choose {Amount:plural:a card|{Amount} cards}."),
            };

            return zh ? zh_ : en_;
        }

        bool cheaper = choose.Amount < 0;

        return (cheaper, zh) switch
        {
            (true, true) => "选择[blue]{Amount}[/blue]张牌，降低它们的费用。",
            (true, false) => "Choose {Amount:plural:a card|{Amount} cards} to reduce the cost of.",
            (false, true) => "选择[blue]{Amount}[/blue]张牌，提高它们的费用。",
            (false, false) => "Choose {Amount:plural:a card|{Amount} cards} to raise the cost of.",
        };
    }

    /// <summary>某语言的 buff / debuff 文本表。</summary>
    private static Dictionary<string, string> BuildPowers(ModProject project, string language)
    {
        var powers = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (PowerDef power in project.Powers)
        {
            string entry = Modules.Files.LanguageXml.PowerEntry(project, power);
            powers[$"{entry}.title"] = power.Name.GetOrFallback(language);
            powers[$"{entry}.description"] = power.Description.GetOrFallback(language);
        }

        return powers;
    }

    private static string BuildManifest(ModProject project)
    {
        var manifest = new Dictionary<string, object>
        {
            ["id"] = project.Id,
            ["name"] = project.Name,
            ["author"] = project.Author,
            ["description"] = project.Description,
            ["version"] = project.Version,
            ["has_pck"] = true,
            ["has_dll"] = true,
            ["dependencies"] = Array.Empty<string>(),
            ["affects_gameplay"] = project.AffectsGameplay,
            ["min_game_version"] = project.MinGameVersion,
        };

        return JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
    }
}
