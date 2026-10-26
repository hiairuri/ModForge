using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModForge.CardAudit;
using ModForge.Core.Catalog;
using ModForge.Core.Pck;

// 原版卡牌审计：把 578 张原版卡"是什么、做什么、我们能不能表达"统一导出成一份数据，
// 供后续按「牌池 × 稀有度」分批核对。
//
// 分两层取数据，各取所长：
//   是什么  —— CardStatsReader 读 IL（费用/类型/稀有度/目标/数值/升级），已由 statstest 逐字段验证
//   做什么  —— 扫反编译源码里实际调用的命令与覆写的钩子（见 Mechanics.cs）
//
// 产出：
//   data/card-audit.json   完整数据（含每张卡的识别结果），供程序或人工后续使用
//   data/card-audit.md     按牌池→稀有度分组的对照表，就是分批核对用的清单
//
// Usage: cardaudit [gameDir] [decompiledDir]

string gameDir = args.FirstOrDefault(a => !a.StartsWith("--") && Directory.Exists(a))
    ?? @"D:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2";

string decompiledDir = args.Skip(1).FirstOrDefault(a => !a.StartsWith("--") && Directory.Exists(a))
    ?? FindDecompiled()
    ?? throw new InvalidOperationException("找不到反编译目录（decompiled）。用第二个参数指定。");

string outDir = Path.Combine(FindRepoRoot() ?? Directory.GetCurrentDirectory(), "data");
Directory.CreateDirectory(outDir);

Console.WriteLine($"game       : {gameDir}");
Console.WriteLine($"decompiled : {decompiledDir}");
Console.WriteLine($"output     : {outDir}");
Console.WriteLine();

// ---- 1. 身份与数值（IL，已验证）----
CardStatsTable stats = CardStatsReader.Read(gameDir);
Console.WriteLine($"读取出 {stats.ByEntry.Count} 张卡的数值定义（游戏 {stats.GameVersion}）。");

// ---- 2. 本地化描述 ----
string pck = Path.Combine(gameDir, "SlayTheSpire2.pck");
var loc = new PckLocReader(pck);
IReadOnlyDictionary<string, string> zh = loc.LoadTable("zhs", "cards");
IReadOnlyDictionary<string, string> en = loc.LoadTable("eng", "cards");
Console.WriteLine($"描述文本：简中 {zh.Count} 条、英文 {en.Count} 条。");

// ---- 3. 逐张卡扫描机制 ----
string cardSourceDir = Path.Combine(decompiledDir, "MegaCrit.Sts2.Core.Models.Cards");
if (!Directory.Exists(cardSourceDir))
{
    throw new InvalidOperationException($"反编译目录里没有卡牌源码：{cardSourceDir}");
}

// 状态源码。能力牌（以及所有"施加某个状态"的牌）的真实行为写在状态类里，
// 所以卡类扫完还要跟进它施加的那些状态——否则整类能力牌都会被误判成"可直接实现"。
string powerSourceDir = Path.Combine(decompiledDir, "MegaCrit.Sts2.Core.Models.Powers");
if (!Directory.Exists(powerSourceDir))
{
    throw new InvalidOperationException($"反编译目录里没有状态源码：{powerSourceDir}");
}

var audits = new List<CardAudit>();

foreach (CardStats card in stats.ByEntry.Values.OrderBy(c => c.Entry, StringComparer.Ordinal))
{
    string file = Path.Combine(cardSourceDir, card.ClassName + ".cs");
    Mechanics.ScanResult scan = File.Exists(file)
        ? Mechanics.Scan(File.ReadAllText(file))
        : new Mechanics.ScanResult(
            Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
            Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
            Array.Empty<string>(), true, Array.Empty<string>());

    // 跟进它施加的状态：把状态类覆写、而我们表达不了的钩子并进这份卡。
    var mergedHooks = new List<string>(scan.HookOverrides);
    var appliedPowers = new List<string>();

    foreach (string powerType in scan.AppliedPowers)
    {
        string powerFile = Path.Combine(powerSourceDir, powerType + ".cs");

        if (!File.Exists(powerFile))
        {
            continue;
        }

        appliedPowers.Add(powerType);

        foreach (string hook in Mechanics.PowerUnsupportedHooks(File.ReadAllText(powerFile)))
        {
            string note = $"{hook}（来自状态 {powerType}）";

            if (!mergedHooks.Contains(note, StringComparer.Ordinal))
            {
                mergedHooks.Add(note);
            }
        }
    }

    Mechanics.ScanResult effective = scan with { HookOverrides = mergedHooks };

    audits.Add(new CardAudit
    {
        Entry = card.Entry,
        ClassName = card.ClassName,
        Pool = card.Pool ?? "(none)",
        Rarity = card.Rarity,
        Type = card.CardType,
        Target = card.Target,
        EnergyCost = card.EnergyCost,
        EnergyCostX = card.EnergyCostX,
        Keywords = card.Keywords,
        Vars = card.Vars.Select(v => new AuditVar(v.Kind, v.Key, v.Value)).ToList(),
        UpgradeEnergyDelta = card.Upgrade.EnergyDelta,
        UpgradeVarDeltas = card.Upgrade.VarDeltas.ToDictionary(kv => kv.Key, kv => kv.Value),
        UpgradeAddedKeywords = card.Upgrade.AddedKeywords,
        UpgradeRemovedKeywords = card.Upgrade.RemovedKeywords,
        HasComputedValues = card.HasComputedValues,

        DescriptionZh = zh.TryGetValue(card.Entry + ".description", out string? z) ? z : null,
        DescriptionEn = en.TryGetValue(card.Entry + ".description", out string? e) ? e : null,
        NameZh = zh.TryGetValue(card.Entry + ".title", out string? zt) ? zt : null,
        NameEn = en.TryGetValue(card.Entry + ".title", out string? et) ? et : null,

        Commands = scan.Commands,
        RecognizedEffects = scan.RecognizedEffects,
        UnsupportedOps = scan.Unsupported,
        AmbiguousMappings = Ambiguities(scan),
        Verdict = Verdict(effective, out string reason),
        VerdictReason = reason,
        SelectionHelpers = scan.Helpers,
        UnknownCommands = scan.UnknownCommands,
        HookOverrides = mergedHooks,
        AppliedPowers = appliedPowers,
        ValueSources = scan.ValueSources,
    });
}

// ---- 4. 写 JSON ----
var options = new JsonSerializerOptions
{
    WriteIndented = true,
    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
};

string jsonPath = Path.Combine(outDir, "card-audit.json");
File.WriteAllText(jsonPath, JsonSerializer.Serialize(new
{
    gameVersion = stats.GameVersion,
    commit = stats.Commit,
    cardCount = audits.Count,
    cards = audits,
}, options));

// ---- 5. 写 Markdown 清单（按牌池 → 稀有度）----
string mdPath = Path.Combine(outDir, "card-audit.md");
File.WriteAllText(mdPath, BuildMarkdown(audits, stats.GameVersion), new UTF8Encoding(false));

// ---- 6. 自检：拿几张行为已知的卡校对识别规则 ----
//
// 识别规则是正则，最容易出的错是"以为认出来了、其实认错了"。这几张卡的效果是确定的，
// 一旦规则被改坏，这里会立刻失败——而不是等到分批核对时才发现结论不可信。
Console.WriteLine();
Console.WriteLine("=== 自检（已知卡对照）===");

var selfChecks = new (string Entry, string[] MustHave, string[] MustNotHave)[]
{
    ("STRIKE_IRONCLAD", new[] { "DealDamage" }, new[] { "LoseHp", "GainBlock" }),
    ("BASH", new[] { "DealDamage", "ApplyPower" }, new[] { "LoseHp" }),

    // 放血：失去生命 + 获得能量。这里最容易错认成"造成伤害"，反之亦然。
    ("BLOODLETTING", new[] { "LoseHp", "GainEnergy" }, new[] { "DealDamage" }),

    // 计算型伤害（来源分别是自身格挡 / 消耗堆张数）。
    ("BODY_SLAM", new[] { "DealDamage" }, new[] { "LoseHp" }),
    ("ASHEN_STRIKE", new[] { "DealDamage" }, new[] { "LoseHp" }),

    // 子弹时间：免费，不是降费；开悟反之。
    ("BULLET_TIME", new[] { "SetCardsFree" }, new[] { "ModifyCardCost" }),
    ("ENLIGHTENMENT", new[] { "ModifyCardCost" }, new[] { "SetCardsFree" }),

    // 轰击：打出的是这张牌自己。合并后自动打出只有一种效果，所以这里只认 AutoPlay
    // （"打谁"由作用对象区分，不再体现为两个不同的效果名）。
    ("BOMBARDMENT", new[] { "AutoPlay", "DealDamage" }, Array.Empty<string>()),

    // 爪击：自身伤害增加。
    ("CLAW", new[] { "BoostCardDamage", "DealDamage" }, Array.Empty<string>()),

    ("POMMEL_STRIKE", new[] { "DealDamage", "DrawCards" }, new[] { "LoseHp" }),

    // 战斗内升级 / 变化（我们新加的两条能力）。
    ("APOTHEOSIS", new[] { "UpgradeCardsInPile" }, new[] { "DealDamage" }),
    ("CHARGE", new[] { "TransformCardsInPile" }, Array.Empty<string>()),
    ("SEANCE", new[] { "TransformCardsInPile" }, Array.Empty<string>()),

    // 铸造（储君）：铸剑者是纯铸造——效果只有一个 Forge，最适合当这条规则的样本。
    ("THE_SMITH", new[] { "Forge" }, new[] { "LoseHp", "GainBlock" }),

    // 搬自己与搬别的牌由同一条效果表达（作用对象不同）——这两张都是它。
    ("DREDGE", new[] { "MoveCardsBetweenPiles" }, Array.Empty<string>()),
    ("HEADBUTT", new[] { "MoveCardsBetweenPiles" }, Array.Empty<string>()),
};

var byEntry = audits.ToDictionary(a => a.Entry, StringComparer.Ordinal);
var selfCheckFailures = new List<string>();

foreach ((string entry, string[] mustHave, string[] mustNotHave) in selfChecks)
{
    if (!byEntry.TryGetValue(entry, out CardAudit? card))
    {
        selfCheckFailures.Add($"{entry}: 审计里没有这张卡（期望它存在）");
        continue;
    }

    foreach (string want in mustHave.Where(w => !card.RecognizedEffects.Contains(w)))
    {
        selfCheckFailures.Add($"{entry}: 缺少应有的效果 {want}（实际 {string.Join(",", card.RecognizedEffects)}）");
    }

    foreach (string unwanted in mustNotHave.Where(w => card.RecognizedEffects.Contains(w)))
    {
        selfCheckFailures.Add($"{entry}: 出现了不该有的效果 {unwanted}（实际 {string.Join(",", card.RecognizedEffects)}）");
    }
}

if (selfCheckFailures.Count > 0)
{
    Console.WriteLine();
    foreach (string f in selfCheckFailures)
    {
        Console.WriteLine("  [失败] " + f);
    }

    Console.WriteLine();
    Console.WriteLine("RESULT: FAILED (识别规则与已知卡不符)");
    return 1;
}

Console.WriteLine($"  {selfChecks.Length} 张已知卡全部识别正确。");

// ---- 7. 控制台摘要 ----
Console.WriteLine();
Console.WriteLine("=== 三档结论 ===");

foreach ((string verdict, int count) in audits
    .GroupBy(a => a.Verdict)
    .Select(g => (Verdict: g.Key, Count: g.Count()))
    .OrderByDescending(x => x.Count))
{
    Console.WriteLine($"  {count,4}  {verdict}");
}

Console.WriteLine();
Console.WriteLine("=== 总览 ===");

int withEffects = audits.Count(a => a.RecognizedEffects.Count > 0);
int unsupported = audits.Count(a => a.UnsupportedOps.Count > 0);
int hookOnly = audits.Count(a => a.HookOverrides.Count > 0);
int nothing = audits.Count(a => a.RecognizedEffects.Count == 0 && a.UnsupportedOps.Count == 0);

Console.WriteLine($"  有玩法效果的卡      : {withEffects}");
Console.WriteLine($"  用到我们没有的操作  : {unsupported}");
Console.WriteLine($"  依赖额外钩子        : {hookOnly}");
Console.WriteLine($"  只认不出玩法        : {nothing}");

Console.WriteLine();
Console.WriteLine("=== 我们缺的操作（按出现次数）===");

foreach ((string op, int count) in audits
    .SelectMany(a => a.UnsupportedOps)
    .GroupBy(o => StripNote(o))
    .Select(g => (Op: g.Key, Count: g.Count()))
    .OrderByDescending(x => x.Count)
    .ThenBy(x => x.Op, StringComparer.Ordinal))
{
    Console.WriteLine($"  {count,4}  {op}");
}

Console.WriteLine();
Console.WriteLine("=== 未识别的命令（规则没覆盖到的，要人工看）===");

List<string> unknown = audits.SelectMany(a => a.UnknownCommands).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList();

if (unknown.Count == 0)
{
    Console.WriteLine("  （没有）");
}
else
{
    foreach (string cmd in unknown)
    {
        int n = audits.Count(a => a.UnknownCommands.Contains(cmd));
        Console.WriteLine($"  {n,4}  {cmd}");
    }
}

Console.WriteLine();
Console.WriteLine($"数据 : {jsonPath}");
Console.WriteLine($"清单 : {mdPath}");
Console.WriteLine();
Console.WriteLine("RESULT: OK");
return 0;

static string StripNote(string op)
{
    int i = op.IndexOf('（');
    return i > 0 ? op[..i] : op;
}

/// <summary>
/// 三档结论，就是这次审计要回答的问题：
///   无法实现 —— 用到了我们完全没有的操作，得先加效果
///   实现方式不同 —— 能做，但原版走的是我们表达不了的路径（多半是额外钩子）
///   可能实现错误 —— 能做，但我们的映射有歧义，可能选错语义
///   可直接实现 —— 现有效果就能照搬
/// </summary>
static string Verdict(Mechanics.ScanResult scan, out string reason)
{
    if (scan.Unsupported.Count > 0)
    {
        reason = "用到我们没有的操作：" + string.Join("、", scan.Unsupported.Select(StripNote));
        return "无法实现";
    }

    // 歧义优先于钩子：歧义是"可能做错了"，比"换种做法"更需要人看。
    if (Ambiguities(scan).Count > 0)
    {
        reason = "映射有歧义，可能选错语义：" + string.Join("、", Ambiguities(scan));
        return "可能实现错误";
    }

    // 只把**真的钩子**当成阻塞项。Calculated / Repeat / Forge / ExhaustKeyword 是我们
    // 自己打的标记，用来提示"这张卡有值得注意的地方"，但它们不是我们表达不了的东西：
    // 计算型数值与重复次数我们本来就支持（Forge 与"消耗"关键词则另有说明）。
    List<string> realHooks = scan.HookOverrides.Where(IsBlockingHook).ToList();

    if (realHooks.Count > 0)
    {
        reason = "依赖额外钩子：" + string.Join("、", realHooks);
        return "实现方式不同";
    }

    if (scan.RecognizedEffects.Count == 0)
    {
        reason = "没有任何玩法效果（废牌 / 纯关键词牌）";
        return "无需实现";
    }

    reason = "现有效果可直接表达";
    return "可直接实现";
}

/// <summary>
/// 这个"钩子标记"是不是真的会挡住我们。
///
/// 里面混了两类东西：真的覆写了我们表达不了的钩子（AfterCardEnteredCombat 等），以及我自己
/// 打的提示标记（Calculated/Repeat 说明这张卡用了计算型数值或重复次数——那两样我们都支持）。
/// 不区分会把一大批本来能直接实现的卡误判成"实现方式不同"（实测：全身撞击、飞剑回旋镖）。
/// </summary>
static bool IsBlockingHook(string hook) =>
    !IsOurOwnHook(hook)
    && hook is not ("Calculated" or "Repeat" or "Forge" or "ExhaustKeyword")

    // "反应任意牌"这类钩子现在由我们的"打出一张牌时"时机覆盖，记进报告只为核对，
    // 不再是阻塞项（见 Mechanics.HooksOurCardPlayedTriggerCovers 里的两点差异说明）。
    && !hook.Contains("≈我们的", StringComparison.Ordinal);

/// <summary>
/// 我们自己发得出的时机对应的钩子（见 Mechanics.HooksWeEmit）。这些不算阻塞项——
/// 我们的效果就是靠它们落地的（"被消耗时""抽到时""自动打出这张牌"…）。
/// </summary>
static bool IsOurOwnHook(string hook) => hook is
    "AfterCardExhausted" or "AfterCardDrawn" or
    "AfterAutoPrePlayPhaseEnteredEarly" or "AfterAutoPostPlayPhaseEntered" or
    "OnTurnEndInHand";

/// <summary>
/// 我们的映射里"一条命令对应多个效果"的地方。这些点必须人工确认语义，
/// 因为选错会让卡的实际行为不同（例如 Damage 与 LoseHp 对格挡/增伤的处理完全不同）。
/// </summary>
static List<string> Ambiguities(Mechanics.ScanResult scan)
{
    var result = new List<string>();

    // 注：Damage 与 LoseHp 的区分已经在 Mechanics.Disambiguate 里按"读哪个数值变量"自动
    // 判定了，所以这里不再把"两者同时出现"当成歧义——原版"突破"这类卡确实既失去生命
    // 又造成伤害，两个效果都是真的。
    // 消耗 / 放置 / 自动打出都不在其列：合并后各只有一种效果，
    // "动哪些牌"由作用对象决定，不再是两种效果之分。

    // 注：移动牌的三种情形（此牌 / 某个牌堆 / 指定牌名）现在都是同一条效果
    // （MoveCardsBetweenPiles，作用对象决定），所以不再是"歧义"，不必报。

    return result;
}

/// <summary>按牌池 → 稀有度分组的对照表。</summary>
static string BuildMarkdown(List<CardAudit> audits, string gameVersion)
{
    var sb = new StringBuilder();

    sb.AppendLine($"# 原版卡牌审计（游戏 {gameVersion}）");
    sb.AppendLine();
    sb.AppendLine("按「牌池 → 稀有度」分组，供分批核对。");
    sb.AppendLine();
    sb.AppendLine("每张卡带一个结论，就是这次要回答的问题：");
    sb.AppendLine();
    sb.AppendLine("- **可直接实现** —— 现有效果就能照搬");
    sb.AppendLine("- **实现方式不同** —— 能做，但原版走的是我们表达不了的路径（多半是额外钩子）");
    sb.AppendLine("- **可能实现错误** —— 能做，但我们的映射有歧义，可能选错语义");
    sb.AppendLine("- **无法实现** —— 用到了我们完全没有的操作，得先加效果");
    sb.AppendLine("- **无需实现** —— 废牌 / 纯关键词牌");
    sb.AppendLine();
    sb.AppendLine("每张卡列出的信息：**我们识别的效果** / **无法实现**（我们缺的操作）/ ");
    sb.AppendLine("**额外钩子**（依赖特殊时机）/ **数值来源** / **描述**（原版中文，用来核对识别结果）。");
    sb.AppendLine();

    // ---- 分批总表：先看哪些批次值得优先核对 ----
    sb.AppendLine("## 批次总表");
    sb.AppendLine();
    sb.AppendLine("按此顺序分批核对：无法实现与可能实现错误的卡优先。");
    sb.AppendLine();
    sb.AppendLine("| 牌池 | 稀有度 | 张数 | 可直接实现 | 实现方式不同 | 可能实现错误 | 无法实现 | 无需实现 |");
    sb.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|");

    foreach (IGrouping<string, CardAudit> pool in audits
        .GroupBy(a => a.Pool)
        .OrderBy(g => PoolOrder(g.Key))
        .ThenBy(g => g.Key, StringComparer.Ordinal))
    {
        foreach (IGrouping<string, CardAudit> rarity in pool
            .GroupBy(a => a.Rarity)
            .OrderBy(g => RarityOrder(g.Key))
            .ThenBy(g => g.Key, StringComparer.Ordinal))
        {
            int n = rarity.Count();

            sb.AppendLine($"| {pool.Key} | {rarity.Key} | {n} "
                + $"| {Count(rarity, "可直接实现")} "
                + $"| {Count(rarity, "实现方式不同")} "
                + $"| {Count(rarity, "可能实现错误")} "
                + $"| {Count(rarity, "无法实现")} "
                + $"| {Count(rarity, "无需实现")} |");
        }
    }

    sb.AppendLine();

    // ---- 缺口清单：我们缺的操作，以及它们卡住了哪些卡 ----
    sb.AppendLine("## 我们缺的操作");
    sb.AppendLine();
    sb.AppendLine("按卡住的卡数排序。这些是要新增的效果，或者明确放弃的能力。");
    sb.AppendLine();
    sb.AppendLine("| 缺的操作 | 卡住的卡数 | 涉及哪些卡 |");
    sb.AppendLine("|---|---:|---|");

    foreach ((string op, List<CardAudit> cards) in audits
        .SelectMany(a => a.UnsupportedOps.Select(o => (Op: StripNote(o), Card: a)))
        .GroupBy(x => x.Op)
        .Select(g => (Op: g.Key, Cards: g.Select(x => x.Card).OrderBy(c => c.Entry, StringComparer.Ordinal).ToList()))
        .OrderByDescending(x => x.Cards.Count)
        .ThenBy(x => x.Op, StringComparer.Ordinal))
    {
        string list = string.Join("、", cards.Select(c => c.NameZh ?? c.Entry));
        sb.AppendLine($"| `{op}` | {cards.Count} | {list} |");
    }

    sb.AppendLine();
    sb.AppendLine("## 可能实现错误的映射");
    sb.AppendLine();
    sb.AppendLine("这些卡我们能做，但映射有歧义——核对时要逐张确认用的是哪种语义。");
    sb.AppendLine();

    foreach ((string amb, List<CardAudit> cards) in audits
        .SelectMany(a => a.AmbiguousMappings.Select(m => (Mapping: m, Card: a)))
        .GroupBy(x => x.Mapping)
        .Select(g => (Mapping: g.Key, Cards: g.Select(x => x.Card).OrderBy(c => c.Entry, StringComparer.Ordinal).ToList()))
        .OrderByDescending(x => x.Cards.Count)
        .ThenBy(x => x.Mapping, StringComparer.Ordinal))
    {
        sb.AppendLine($"### {amb}（{cards.Count} 张）");
        sb.AppendLine();
        sb.AppendLine(string.Join("、", cards.Select(c => c.NameZh ?? c.Entry)));
        sb.AppendLine();
    }

    sb.AppendLine("---");
    sb.AppendLine();
    sb.AppendLine("## 逐张明细");
    sb.AppendLine();

    foreach (IGrouping<string, CardAudit> pool in audits
        .GroupBy(a => a.Pool)
        .OrderBy(g => PoolOrder(g.Key))
        .ThenBy(g => g.Key, StringComparer.Ordinal))
    {
        int poolCount = pool.Count();
        sb.AppendLine($"## {pool.Key}（{poolCount} 张）");
        sb.AppendLine();

        foreach (IGrouping<string, CardAudit> rarity in pool
            .GroupBy(a => a.Rarity)
            .OrderBy(g => RarityOrder(g.Key))
            .ThenBy(g => g.Key, StringComparer.Ordinal))
        {
            sb.AppendLine($"### {rarity.Key}（{rarity.Count()} 张）");
            sb.AppendLine();

            foreach (CardAudit card in rarity.OrderBy(c => c.NameZh ?? c.Entry, StringComparer.Ordinal))
            {
                string name = card.NameZh ?? card.NameEn ?? card.Entry;
                string cost = card.EnergyCostX ? "X" : card.EnergyCost.ToString();
                string keywords = card.Keywords.Count > 0 ? " · " + string.Join("/", card.Keywords) : "";

                sb.AppendLine($"#### {name} `{card.Entry}` — {card.Verdict}");
                sb.AppendLine();
                sb.AppendLine($"- 费用 {cost} · {card.Type} · 目标 {card.Target}{keywords}");

                if (card.RecognizedEffects.Count > 0)
                {
                    sb.AppendLine($"- 我们识别的效果：{string.Join("、", card.RecognizedEffects)}");
                }
                else
                {
                    sb.AppendLine("- 我们识别的效果：（无）");
                }

                if (card.AmbiguousMappings.Count > 0)
                {
                    sb.AppendLine($"- ⚠️ **可能实现错误**：{string.Join("、", card.AmbiguousMappings)}");
                }

                if (card.UnsupportedOps.Count > 0)
                {
                    sb.AppendLine($"- **无法实现**：{string.Join("、", card.UnsupportedOps)}");
                }

                if (card.UnknownCommands.Count > 0)
                {
                    sb.AppendLine($"- **未识别命令**：{string.Join("、", card.UnknownCommands)}");
                }

                if (card.AppliedPowers.Count > 0)
                {
                    sb.AppendLine($"- 施加的状态：{string.Join("、", card.AppliedPowers)}");
                }

                if (card.HookOverrides.Count > 0)
                {
                    sb.AppendLine($"- 额外钩子：{string.Join("、", card.HookOverrides)}");
                }

                if (card.ValueSources.Count > 0)
                {
                    sb.AppendLine($"- 数值来源：{string.Join("、", card.ValueSources)}");
                }

                if (card.DescriptionZh is { Length: > 0 })
                {
                    sb.AppendLine($"- 描述：{card.DescriptionZh.Replace("\n", " / ")}");
                }

                sb.AppendLine();
            }
        }
    }

    return sb.ToString();
}

/// <summary>某个分组里结论为某档的卡数，用于批次总表。</summary>
static int Count(IEnumerable<CardAudit> cards, string verdict) =>
    cards.Count(c => c.Verdict == verdict);

static int PoolOrder(string pool) => pool switch
{
    "ironclad" => 0,
    "silent" => 1,
    "defect" => 2,
    "necrobinder" => 3,
    "regent" => 4,
    "colorless" => 5,
    "curse" => 6,
    "status" => 7,
    "event" => 8,
    "quest" => 9,
    "token" => 10,
    _ => 11,
};

static int RarityOrder(string rarity) => rarity switch
{
    "basic" => 0,
    "common" => 1,
    "uncommon" => 2,
    "rare" => 3,
    "ancient" => 4,
    "event" => 5,
    "token" => 6,
    "status" => 7,
    "curse" => 8,
    "quest" => 9,
    _ => 10,
};

static string? FindRepoRoot()
{
    string? probe = Directory.GetCurrentDirectory();

    for (int i = 0; i < 8 && probe is not null; i++)
    {
        if (File.Exists(Path.Combine(probe, "ModForge.slnx")))
        {
            return probe;
        }

        probe = Path.GetDirectoryName(probe);
    }

    return null;
}

static string? FindDecompiled()
{
    string? root = FindRepoRoot();
    return root is null ? null : Path.Combine(root, "decompiled");
}

/// <summary>审计结果里的一张卡。</summary>
internal sealed record CardAudit
{
    public required string Entry { get; init; }
    public required string ClassName { get; init; }
    public required string Pool { get; init; }
    public required string Rarity { get; init; }
    public required string Type { get; init; }
    public required string Target { get; init; }
    public required int EnergyCost { get; init; }
    public bool EnergyCostX { get; init; }
    public IReadOnlyList<string> Keywords { get; init; } = Array.Empty<string>();
    public IReadOnlyList<AuditVar> Vars { get; init; } = Array.Empty<AuditVar>();
    public int UpgradeEnergyDelta { get; init; }
    public IReadOnlyDictionary<string, double> UpgradeVarDeltas { get; init; } = new Dictionary<string, double>();
    public IReadOnlyList<string> UpgradeAddedKeywords { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> UpgradeRemovedKeywords { get; init; } = Array.Empty<string>();
    public bool HasComputedValues { get; init; }

    public string? NameZh { get; init; }
    public string? NameEn { get; init; }
    public string? DescriptionZh { get; init; }
    public string? DescriptionEn { get; init; }

    public IReadOnlyList<string> Commands { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> RecognizedEffects { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> UnsupportedOps { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> AmbiguousMappings { get; init; } = Array.Empty<string>();

    /// <summary>三档结论：无法实现 / 实现方式不同 / 可能实现错误 / 可直接实现 / 无需实现。</summary>
    public string Verdict { get; init; } = "";
    public string VerdictReason { get; init; } = "";
    public IReadOnlyList<string> SelectionHelpers { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> UnknownCommands { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> HookOverrides { get; init; } = Array.Empty<string>();

    /// <summary>这张卡施加的状态类名（已跟进读取它们的钩子，见 <see cref="HookOverrides"/>）。</summary>
    public IReadOnlyList<string> AppliedPowers { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> ValueSources { get; init; } = Array.Empty<string>();
}

internal sealed record AuditVar(string Kind, string Key, double? Value);
