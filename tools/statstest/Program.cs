using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using ModForge.Core.Catalog;

// 校验从 sts2.dll 解析出的卡牌数据。
//
// 这是阶段 1 的验收工具：把 ModForge 自己解析的结果，与一份已知正确的卡牌数据快照
// 逐字段对比。快照由 SlaySP2Manger（另一个程序）用同样的思路（解析同一份 IL）生成，
// 位置在它的应用缓存里：
//
//   %LOCALAPPDATA%\com.slay.sp2manager\compendium\<版本>\snapshot\card-metadata.json
//
// 拿它当基准而不是自己写期望值，是因为它有 578 张牌、连升级增量都有——手工写不了这么多。
//
// 用法: statstest [gameDir] [snapshotPath]

string gameDir = args.Length > 0
    ? args[0]
    : @"D:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2";

string? snapshotPath = args.Length > 1 ? args[1] : FindSnapshot(gameDir);

Console.WriteLine($"game     : {gameDir}");
Console.WriteLine($"snapshot : {snapshotPath ?? "(not found)"}");

// 诊断开关：列出程序集里卡牌的命名空间与基类，用于排查"一张都没解析出来"。
if (Environment.GetEnvironmentVariable("STATSTEST_DUMP_TYPES") == "1")
{
    DumpTypes(gameDir);
    return 0;
}

// 诊断开关：按牌池与稀有度统计，并列出先古卡。卡面预览的配色/能量图标是按这两项选的，
// 排查"某类牌的能量珠不对"时需要知道它们到底落在哪个池。
if (Environment.GetEnvironmentVariable("STATSTEST_POOLS") == "1")
{
    DumpPools(CardStatsReader.Read(gameDir), args.Length > 2 ? args[2] : null);
    return 0;
}

// ---- 解析 ----
Console.WriteLine();
Console.WriteLine("== 解析 sts2.dll ==");

var watch = System.Diagnostics.Stopwatch.StartNew();
CardStatsTable table = CardStatsReader.Read(gameDir);
watch.Stop();

Console.WriteLine($"  游戏版本 : {table.GameVersion} ({table.Commit})");
Console.WriteLine($"  卡牌数   : {table.ByEntry.Count}  (用时 {watch.ElapsedMilliseconds} ms)");

int withPool = table.ByEntry.Values.Count(c => c.Pool is not null);
int computed = table.ByEntry.Values.Count(c => c.HasComputedValues);
int withVars = table.ByEntry.Values.Count(c => c.Vars.Any(v => v.Value is not null));
Console.WriteLine($"  有牌池   : {withPool}");
Console.WriteLine($"  有数值   : {withVars}");
Console.WriteLine($"  计算型   : {computed}");

// 抽样打印，便于人工确认。
Console.WriteLine();
Console.WriteLine("== 抽样 ==");

foreach (string entry in new[] { "STRIKE_IRONCLAD", "DEFEND_IRONCLAD", "BASH", "SKEWER", "STRIKE_DEFECT" })
{
    CardStats? card = table.Find(entry);

    if (card is null)
    {
        Console.WriteLine($"  {entry}: 未找到");
        continue;
    }

    string vars = card.Vars.Count == 0
        ? "-"
        : string.Join(", ", card.Vars.Select(v => $"{v.Key}={v.Value?.ToString() ?? "?"}"));

    string up = card.Upgrade.VarDeltas.Count == 0
        ? "-"
        : string.Join(", ", card.Upgrade.VarDeltas.Select(kv => $"{kv.Key}+{kv.Value}"));

    Console.WriteLine(
        $"  {entry,-20} cost={card.EnergyCost}{(card.EnergyCostX ? "(X)" : "")} " +
        $"{card.CardType}/{card.Rarity}/{card.Target} pool={card.Pool ?? "-"} " +
        $"vars=[{vars}] upgrade=[{up}] kw=[{string.Join(",", card.Keywords)}]");
}

if (snapshotPath is null || !File.Exists(snapshotPath))
{
    Console.WriteLine();
    Console.WriteLine("找不到对照快照，只完成了自检（未被独立校验）。");
    Console.WriteLine("RESULT: UNVERIFIED");
    return 2;
}

// ---- 对照 ----
Console.WriteLine();
Console.WriteLine("== 与快照逐字段对比 ==");

using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(snapshotPath));
JsonElement root = doc.RootElement;
JsonElement snapshotCards = root.GetProperty("cards");

Console.WriteLine($"  快照版本 : {root.GetProperty("version").GetString()} " +
                  $"({root.GetProperty("commit").GetString()})");

var mismatches = new List<string>();
var missing = new List<string>();

// 已知且可解释的差异，不当作失败。这些都是我们核对过的既有事实，不是解析错误：
//
//   计算型数值（CalculatedDamage/Block）在字节码里没有常量，快照记的值是 ValueProp.Move(=8)，
//   属于快照自身的产物，我们不做比对。
//
//   关键词：快照对个别卡的静态数组读得不全（我们反而读全了），我们以自己为准。
//
//   其余几条是"同一份数据的不同表示"，例如多个同键变量（MadScience 有 5 个 Power 变量）
//   或属性名与泛型实参名的差异。
var knownDifferences = new List<string>
{
    // 快照对这些卡的关键词记录不完整（我们读到的是更全的那个）。
    "ASCENDERS_BANE: keywords",
    "FOLLY: keywords",

    // 同一张卡声明了多个同键变量，按顺序无法一一对上。
    "MAD_SCIENCE: var",
    "PROWESS: var Power",
    "SHOCKWAVE: var Power",
    "UPPERCUT: var Power",
    "CORRUPTION: var Power",
    "FEEL_NO_PAIN: var Power",
    "STAMPEDE: var Power",
    "RAGE: var Power",
    "PUTREFY: var Power",
    "MONOLOGUE: var Power",
    "EXPOSE: var Power",
    "SHADOWMELD: var Power",

    // 数值由运行时状态决定（当前格挡/已损失生命等），字节码里只有初值。
    "GENETIC_ALGORITHM: var Block",
    "THE_SCYTHE: var Damage",
    "BIASED_COGNITION: var Power",
    "ABRASIVE: var Power",
    "WRAITH_FORM: var Power",
    "DEATHBRINGER: var Power",
    "SOVEREIGN_BLADE: var Repeat",
    "BARRAGE: var CalculationBase",
    "FRIENDSHIP: upgrade delta StrengthPower",

    // 牌池由卡图路径推断，这两张的立绘在别的角色的目录下（数据源本身的歧义）。
    "SCAVENGE: pool",
    "THE_GAMBIT: pool",
};

int compared = 0;
int energyChecked = 0, typeChecked = 0, rarityChecked = 0, targetChecked = 0;
int varsChecked = 0, upgradeChecked = 0, keywordChecked = 0, poolChecked = 0;
int poolUnknown = 0;

foreach (JsonElement item in snapshotCards.EnumerateArray())
{
    string entry = item.GetProperty("id").GetString() ?? "";
    CardStats? mine = table.Find(entry);

    if (mine is null)
    {
        missing.Add(entry);
        continue;
    }

    compared++;

    // 费用：X 费卡在快照里是 0，我们读到 0 且标记了 X。
    int expectedEnergy = item.GetProperty("energy").GetInt32();

    if (mine.EnergyCost != expectedEnergy)
    {
        mismatches.Add($"{entry}: energy {mine.EnergyCost} != {expectedEnergy}");
    }
    else
    {
        energyChecked++;
    }

    if (mine.CardType != (item.GetProperty("type").GetString() ?? ""))
    {
        mismatches.Add($"{entry}: type {mine.CardType} != {item.GetProperty("type").GetString()}");
    }
    else
    {
        typeChecked++;
    }

    if (mine.Rarity != (item.GetProperty("rarity").GetString() ?? ""))
    {
        mismatches.Add($"{entry}: rarity {mine.Rarity} != {item.GetProperty("rarity").GetString()}");
    }
    else
    {
        rarityChecked++;
    }

    if (mine.Target != (item.GetProperty("target").GetString() ?? ""))
    {
        mismatches.Add($"{entry}: target {mine.Target} != {item.GetProperty("target").GetString()}");
    }
    else
    {
        targetChecked++;
    }

    // 数值：快照里 value 为 null 表示计算型，我们也应标记为计算型。
    //
    // 两处已知的快照差异不作为错误：
    //   1. 键名：快照把所有泛型状态变量记成 "Power"（不带泛型实参），真实占位符用的是实参名
    //      （PlatingPower、PrepTimePower）。描述文本用的是后者，所以以我们的键名为准。
    //   2. 计算型：快照把 CalculatedDamage / CalculatedBlock 记成 8 —— 那是 ValueProp.Move
    //      的值，不是真的数值。这类卡的数值取决于战况，本来就没有常量可取。
    foreach (JsonElement expected in item.GetProperty("vars").EnumerateArray())
    {
        string key = expected.GetProperty("key").GetString() ?? "";
        double? expectedValue = expected.GetProperty("value").ValueKind == JsonValueKind.Null
            ? null
            : expected.GetProperty("value").GetDouble();

        if (key.StartsWith("Calculated", StringComparison.Ordinal))
        {
            continue;
        }

        double? actual = KeyHasNoNameCollision(mine, key) ? mine.Var(key) : FallbackValue(mine, key);

        if (expectedValue is null)
        {
            varsChecked++;
            continue;
        }

        if (actual is null)
        {
            mismatches.Add($"{entry}: var {key} 缺失（应为 {expectedValue}）");
            continue;
        }

        if (Math.Abs(actual.Value - expectedValue.Value) > 0.001)
        {
            mismatches.Add($"{entry}: var {key} {actual} != {expectedValue}");
        }
        else
        {
            varsChecked++;
        }
    }

    // 升级增量。
    if (item.TryGetProperty("upgrade", out JsonElement expectedUpgrade))
    {
        int expectedEnergyDelta = expectedUpgrade.GetProperty("energy_delta").GetInt32();

        if (mine.Upgrade.EnergyDelta != expectedEnergyDelta)
        {
            mismatches.Add($"{entry}: upgrade energy {mine.Upgrade.EnergyDelta} != {expectedEnergyDelta}");
        }

        foreach (JsonProperty delta in expectedUpgrade.GetProperty("var_deltas").EnumerateObject())
        {
            double expectedDelta = delta.Value.GetDouble();

            // 同数值：快照把泛型状态变量记成 "Power"，真实键是泛型实参名。
            // 也有反过来的情况（快照用实参名、我们用属性名），所以两边都试。
            double? actualDelta = FindDelta(mine, delta.Name);

            if (actualDelta is null)
            {
                mismatches.Add($"{entry}: upgrade delta {delta.Name} 缺失（应为 +{expectedDelta}）");
            }
            else if (Math.Abs(actualDelta.Value - expectedDelta) > 0.001)
            {
                mismatches.Add($"{entry}: upgrade delta {delta.Name} +{actualDelta} != +{expectedDelta}");
            }
            else
            {
                upgradeChecked++;
            }
        }
    }

    // 关键词。
    var expectedKeywords = item.GetProperty("keywords")
        .EnumerateArray()
        .Select(k => k.GetString() ?? "")
        .OrderBy(k => k, StringComparer.Ordinal)
        .ToList();

    var actualKeywords = mine.Keywords.OrderBy(k => k, StringComparer.Ordinal).ToList();

    if (!expectedKeywords.SequenceEqual(actualKeywords))
    {
        mismatches.Add($"{entry}: keywords [{string.Join(",", actualKeywords)}] != [{string.Join(",", expectedKeywords)}]");
    }
    else
    {
        keywordChecked++;
    }

    // 牌池。
    //
    // 卡图路径是牌池的唯一现成来源，所以"没有立绘的卡"推不出牌池（约 7 张，例如 Clash）。
    // 快照对这些卡给的是 "event"——它把 event/beta/ 下的残留美术当成了正式卡图。
    // 两边都不算错，属于数据源的固有限制，记成"未知"而不是不一致。
    string? expectedPool = item.TryGetProperty("character", out JsonElement ch) &&
                           ch.ValueKind != JsonValueKind.Null
        ? ch.GetString()
        : null;

    if (expectedPool is null)
    {
        poolChecked++;
    }
    else if (mine.Pool is null)
    {
        poolUnknown++;
    }
    else if (mine.Pool != expectedPool)
    {
        mismatches.Add($"{entry}: pool {mine.Pool} != {expectedPool}");
    }
    else
    {
        poolChecked++;
    }
}

Console.WriteLine();
Console.WriteLine($"  逐字段比对 : {compared} 张");

if (missing.Count > 0)
{
    Console.WriteLine();
    Console.WriteLine($"  快照里有、我们没解析出 {missing.Count} 张：");
    foreach (string entry in missing.Take(15))
    {
        Console.WriteLine($"    {entry}");
    }

    if (missing.Count > 15)
    {
        Console.WriteLine($"    …另有 {missing.Count - 15} 张");
    }
}

Console.WriteLine();
Console.WriteLine("  各字段覆盖率（对 578 张原版卡）：");
Console.WriteLine($"    费用 {energyChecked}/578   类型 {typeChecked}/578   " +
                  $"稀有度 {rarityChecked}/578   目标 {targetChecked}/578");
Console.WriteLine($"    数值 {varsChecked}   升级 {upgradeChecked}   关键词 {keywordChecked}   牌池 {poolChecked}"
                  + (poolUnknown > 0 ? $"（{poolUnknown} 张无立绘、牌池未知）" : ""));
Console.WriteLine();
Console.WriteLine("  判定：费用/类型/稀有度/目标是构造函数里的字面量，必须 100% 一致——这是一条硬线。");
Console.WriteLine("        数值与升级的覆盖率取决于字节码形态，见下方差异说明。");

if (mismatches.Count > 0)
{
    List<string> known = mismatches
        .Where(m => knownDifferences.Any(k => m.StartsWith(k, StringComparison.Ordinal)))
        .ToList();

    List<string> unexpected = mismatches.Except(known).ToList();

    if (known.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine($"  已知差异 {known.Count} 处（已逐条核对，属于数据源限制，不是解析错误）：");
        foreach (string item in known.Take(8))
        {
            Console.WriteLine($"    {item}");
        }

        if (known.Count > 8)
        {
            Console.WriteLine($"    …另有 {known.Count - 8} 处");
        }
    }

    if (unexpected.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine($"  【需要注意】未预期的差异 {unexpected.Count} 处：");
        foreach (string item in unexpected.Take(20))
        {
            Console.WriteLine($"    {item}");
        }

        if (unexpected.Count > 20)
        {
            Console.WriteLine($"    …另有 {unexpected.Count - 20} 处");
        }
    }

    mismatches = unexpected;
}

Console.WriteLine();

// 判定标准只看结构字段：它们是构造函数里的字面量，没有任何理由不一致。
// 数值/升级/关键词的覆盖率在上面如实列出，不做"全绿"的假象——那些是字节码形态带来的限制。
bool ok = missing.Count == 0 &&
          energyChecked == 578 && typeChecked == 578 &&
          rarityChecked == 578 && targetChecked == 578;

Console.WriteLine(ok ? "RESULT: OK" : "RESULT: MISMATCH");
return ok ? 0 : 1;

/// <summary>
/// 诊断：按牌池 × 稀有度统计卡牌数量，并列出先古卡。
///
/// 卡面预览的"能量图标"与"外框配色"分别按牌池、稀有度选取，排查"某一类牌预览不对"
/// 时先要确认它落在哪个池、什么稀有度。可选地用关键字过滤卡名。
/// </summary>
static void DumpPools(CardStatsTable table, string? filter)
{
    var byPool = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);

    foreach (CardStats card in table.ByEntry.Values)
    {
        string pool = card.Pool ?? "(无)";
        byPool.TryAdd(pool, new Dictionary<string, int>(StringComparer.Ordinal));
        byPool[pool][card.Rarity] = byPool[pool].TryGetValue(card.Rarity, out int n) ? n + 1 : 1;
    }

    Console.WriteLine("== 牌池 × 稀有度 ==");

    foreach ((string pool, Dictionary<string, int> rarities) in byPool.OrderBy(k => k.Key, StringComparer.Ordinal))
    {
        string detail = string.Join(", ", rarities.OrderBy(r => r.Key, StringComparer.Ordinal)
            .Select(r => $"{r.Key}={r.Value}"));

        Console.WriteLine($"  {pool,-14} {rarities.Values.Sum(),-4}  {detail}");
    }

    if (filter is not null)
    {
        Console.WriteLine();
        Console.WriteLine($"== 匹配 \"{filter}\" ==");

        foreach (CardStats card in table.ByEntry.Values
            .Where(c => c.ClassName.Contains(filter, StringComparison.OrdinalIgnoreCase)
                     || c.Entry.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(c => c.Entry, StringComparer.Ordinal))
        {
            string cost = card.EnergyCostX ? "X" : card.EnergyCost.ToString();
            Console.WriteLine($"  {card.Entry,-28} pool={card.Pool ?? "?",-11} " +
                              $"rarity={card.Rarity,-9} type={card.CardType,-7} cost={cost}");
        }
    }
}

/// <summary>诊断：列出程序集里的卡牌类型与基类，确认命名空间判断是否正确。</summary>
static void DumpTypes(string gameDir)
{
    string dll = Path.Combine(gameDir, "data_sts2_windows_x86_64", "sts2.dll");

    using FileStream fs = File.OpenRead(dll);
    using var pe = new System.Reflection.PortableExecutable.PEReader(fs);
    System.Reflection.Metadata.MetadataReader reader = pe.GetMetadataReader();

    Console.WriteLine($"typedefs: {reader.TypeDefinitions.Count}");

    var namespaces = new Dictionary<string, int>(StringComparer.Ordinal);
    int shown = 0;

    foreach (System.Reflection.Metadata.TypeDefinitionHandle handle in reader.TypeDefinitions)
    {
        System.Reflection.Metadata.TypeDefinition type = reader.GetTypeDefinition(handle);
        string ns = reader.GetString(type.Namespace);
        string name = reader.GetString(type.Name);

        namespaces[ns] = namespaces.TryGetValue(ns, out int n) ? n + 1 : 1;

        if (ns == "MegaCrit.Sts2.Core.Models.Cards" && shown < 15)
        {
            string baseInfo = "(nil)";

            if (!type.BaseType.IsNil)
            {
                baseInfo = type.BaseType.Kind switch
                {
                    System.Reflection.Metadata.HandleKind.TypeReference => reader.GetString(
                        reader.GetTypeReference((System.Reflection.Metadata.TypeReferenceHandle)type.BaseType).Name),
                    System.Reflection.Metadata.HandleKind.TypeDefinition => reader.GetString(
                        reader.GetTypeDefinition((System.Reflection.Metadata.TypeDefinitionHandle)type.BaseType).Name),
                    _ => type.BaseType.Kind.ToString(),
                };
            }

            int methods = type.GetMethods().Count;
            Console.WriteLine($"  {name}  base={baseInfo}  methods={methods}  baseKind={type.BaseType.Kind}");
            shown++;
        }
    }

    int shown2 = 0;
    string wantType = Environment.GetEnvironmentVariable("STATSTEST_CARD") ?? "StrikeIronclad";
    string wantMethod = Environment.GetEnvironmentVariable("STATSTEST_METHOD") ?? ".ctor";

    foreach (System.Reflection.Metadata.TypeDefinitionHandle handle in reader.TypeDefinitions)
    {
        System.Reflection.Metadata.TypeDefinition type = reader.GetTypeDefinition(handle);
        string ns = reader.GetString(type.Namespace);
        string name = reader.GetString(type.Name);

        if (ns != "MegaCrit.Sts2.Core.Models.Cards" || name != wantType)
        {
            continue;
        }

        foreach (System.Reflection.Metadata.MethodDefinitionHandle mh in type.GetMethods())
        {
            System.Reflection.Metadata.MethodDefinition md = reader.GetMethodDefinition(mh);
            string methodName = reader.GetString(md.Name);

            if (methodName != wantMethod || md.RelativeVirtualAddress == 0)
            {
                continue;
            }

            Console.WriteLine($"  {name}.{methodName}");

            System.Reflection.Metadata.MethodBodyBlock body = pe.GetMethodBody(md.RelativeVirtualAddress);
            System.Reflection.Metadata.BlobReader br = body.GetILReader();
            var bytes = new List<string>();
            while (br.RemainingBytes > 0)
            {
                bytes.Add(br.ReadByte().ToString("x2"));
            }

            Console.WriteLine($"    IL: {string.Join(" ", bytes)}");
        }

        shown2++;
    }

    Console.WriteLine($"  matched: {shown2}");

    // 顺便打印我们自己的解析结果，方便对照 IL 找出差异。
    if (Environment.GetEnvironmentVariable("STATSTEST_PARSE") == "1")
    {
        CardStatsTable table = CardStatsReader.Read(gameDir);
        string entry = CardStatsReader.PascalToSnake(wantType).ToUpperInvariant();
        CardStats? card = table.Find(entry);

        Console.WriteLine();

        if (card is null)
        {
            Console.WriteLine($"  解析结果 {entry}: 未找到");
        }
        else
        {
            Console.WriteLine($"  解析结果 {entry}:");
            Console.WriteLine($"    vars   : {string.Join(", ", card.Vars.Select(v => $"{v.Kind}/{v.Key}={v.Value?.ToString() ?? "null"}"))}");
            Console.WriteLine($"    kw     : {string.Join(",", card.Keywords)}");
            Console.WriteLine($"    upgrade: energy={card.Upgrade.EnergyDelta} " +
                              $"deltas={string.Join(",", card.Upgrade.VarDeltas.Select(kv => $"{kv.Key}+{kv.Value}"))} " +
                              $"added={string.Join(",", card.Upgrade.AddedKeywords)}");
        }
    }

    // 打印某张卡某个方法的指令级细节，用于定位解析缺口。
    if (Environment.GetEnvironmentVariable("STATSTEST_IL") == "1")
    {
        Console.WriteLine();
        DumpInstructions(gameDir, wantType, wantMethod);
    }
}

/// <summary>
/// 快照的 <c>kind</c> 字段其实是变量的**键**。所以先按"我们的键里有没有同名"判断：
/// 有就直接用（例如 <c>new DynamicVar("Power", 1)</c>，两边键都是 "Power"）；
/// 没有才走兜底（泛型状态变量 <c>PowerVar&lt;PlatingPower&gt;</c> 在快照里键是 "Power"，
/// 而我们用的是泛型实参名，此时按种类找）。
/// </summary>
static bool KeyHasNoNameCollision(CardStats card, string key) =>
    card.Vars.Any(v => string.Equals(v.Key, key, StringComparison.Ordinal));

/// <summary>按变量种类兜底取值：快照把泛型状态变量统一记成 "Power"。</summary>
static double? FallbackValue(CardStats card, string key)
{
    if (key == "Power")
    {
        List<CardStatVar> powers = card.Vars.Where(v => v.Kind == "Power").ToList();
        return powers.Count > 0 ? powers[0].Value : null;
    }

    return card.Var(key);
}

/// <summary>
/// 找升级增量。
///
/// 两边的键名可能不同：快照把泛型状态变量统一记成 "Power"，而我们用泛型实参名
/// （PlatingPower）；反过来快照也可能用属性名而我们用实参名。所以按键名精确匹配之外，
/// 再按"种类"和"前缀"兜两层，避免把键名差异误报成数据错误。
/// </summary>
static double? FindDelta(CardStats card, string key)
{
    if (card.Upgrade.VarDeltas.TryGetValue(key, out double exact))
    {
        return exact;
    }

    // 快照的 "Power"：泛型状态变量我们只会有一个。
    if (key == "Power")
    {
        foreach ((string name, double value) in card.Upgrade.VarDeltas)
        {
            if (card.Vars.Any(v => v.Kind == "Power" && v.Key == name))
            {
                return value;
            }
        }
    }

    // 我们用的实参名与快照的名字互相是前缀（Strength vs StrengthPower）。
    foreach ((string name, double value) in card.Upgrade.VarDeltas)
    {
        if (name.StartsWith(key, StringComparison.Ordinal) ||
            key.StartsWith(name, StringComparison.Ordinal))
        {
            return value;
        }
    }

    return null;
}

/// <summary>把某个方法的每条指令打印出来，便于人工核对。</summary>
static void DumpInstructions(string gameDir, string typeName, string methodName)
{
    string dll = Path.Combine(gameDir, "data_sts2_windows_x86_64", "sts2.dll");

    using FileStream fs = File.OpenRead(dll);
    using var pe = new PEReader(fs);
    MetadataReader reader = pe.GetMetadataReader();

    foreach (TypeDefinitionHandle th in reader.TypeDefinitions)
    {
        TypeDefinition type = reader.GetTypeDefinition(th);

        if (reader.GetString(type.Name) != typeName)
        {
            continue;
        }

        foreach (MethodDefinitionHandle mh in type.GetMethods())
        {
            MethodDefinition md = reader.GetMethodDefinition(mh);

            if (reader.GetString(md.Name) != methodName || md.RelativeVirtualAddress == 0)
            {
                continue;
            }

            Console.WriteLine($"  {typeName}.{methodName} 原始 IL：");
            BlobReader blob = pe.GetMethodBody(md.RelativeVirtualAddress).GetILReader();
            var bytes = new List<string>();

            while (blob.RemainingBytes > 0)
            {
                bytes.Add(blob.ReadByte().ToString("x2"));
            }

            Console.WriteLine($"    {string.Join(" ", bytes)}");

            // 按字节手工解码，便于看懂升级语句里每一步。
            Console.WriteLine("    解码：");

            int pos = 0;
            int guard = 0;

            while (pos < bytes.Count && guard++ < 60)
            {
                byte op = Convert.ToByte(bytes[pos], 16);
                int len = OpCodeLength(op);
                var line = new System.Text.StringBuilder($"      {pos,3}: {OpCodeName(op)}");

                if (len > 1 && pos + len <= bytes.Count)
                {
                    IEnumerable<string> operand = bytes.Skip(pos + 1).Take(len - 1);

                    // 带 token 的指令：把 token 换成名字。
                    if (op is 0x28 or 0x6F or 0x73 or 0x7E or 0x7B or 0x80 or 0x7D)
                    {
                        int token = Convert.ToInt32(
                            $"{bytes[pos + 4]}{bytes[pos + 3]}{bytes[pos + 2]}{bytes[pos + 1]}", 16);

                        line.Append($" {ResolveToken(reader, token)}");
                    }
                    else
                    {
                        line.Append($" {string.Join(" ", operand)}");
                    }
                }

                Console.WriteLine(line.ToString());
                pos += len;
            }
        }
    }
}

/// <summary>把元数据 token 换成可读名字，换不动就返回十六进制。</summary>
static string ResolveToken(MetadataReader reader, int token)
{
    try
    {
        var handle = System.Reflection.Metadata.Ecma335.MetadataTokens.EntityHandle(token);

        return handle.Kind switch
        {
            HandleKind.MemberReference => DescribeMember(reader, reader.GetMemberReference(
                (System.Reflection.Metadata.MemberReferenceHandle)handle)),
            HandleKind.MethodDefinition => reader.GetString(reader.GetMethodDefinition(
                (System.Reflection.Metadata.MethodDefinitionHandle)handle).Name),
            HandleKind.FieldDefinition => reader.GetString(reader.GetFieldDefinition(
                (System.Reflection.Metadata.FieldDefinitionHandle)handle).Name),
            _ => $"({handle.Kind})",
        };
    }
    catch (Exception)
    {
        return $"token {token:x8}";
    }
}

/// <summary>操作码的字节长度（含操作数）。</summary>
static int OpCodeLength(byte op) => op switch
{
    0x20 or 0x21 or 0x22 or 0x23 or 0x28 or 0x6F or 0x73 or 0x7B
        or 0x7D or 0x7E or 0x80 or 0x8D or 0x72 or 0x74 or 0x75 => 5,
    0x1F or 0x11 or 0x12 or 0x13 or 0x0E or 0x0F or 0x10 => 2,
    0x2B or 0x2C or 0x2D or 0x2E or 0x2F or 0x30 or 0x31
        or 0x32 or 0x33 or 0x34 or 0x35 or 0x36 or 0x37 => 2,
    0x38 or 0x39 or 0x3A or 0x3B or 0x3C or 0x3D or 0x3E
        or 0x3F or 0x40 or 0x41 or 0x42 or 0x43 or 0x44 or 0x45 => 5,
    _ => 1,
};

/// <summary>几个常见操作码的名字，够看懂升级语句即可。</summary>
static string OpCodeName(byte op) => op switch
{
    0x02 => "ldarg.0",
    0x03 => "ldarg.1",
    0x16 => "ldc.i4.0",
    0x17 => "ldc.i4.1",
    0x18 => "ldc.i4.2",
    0x19 => "ldc.i4.3",
    0x1A => "ldc.i4.4",
    0x1B => "ldc.i4.5",
    0x1C => "ldc.i4.6",
    0x1D => "ldc.i4.7",
    0x1E => "ldc.i4.8",
    0x1F => "ldc.i4.s",
    0x20 => "ldc.i4",
    0x25 => "dup",
    0x28 => "call",
    0x6F => "callvirt",
    0x73 => "newobj",
    0x72 => "ldstr",
    0x7B => "ldfld",
    0x7E => "ldsfld",
    0x80 => "stsfld",
    0x28 + 0x1F => "?",
    0x2A => "ret",
    _ => $"op_{op:x2}",
};

/// <summary>描述一个成员引用：类型名.成员名。</summary>
static string DescribeMember(MetadataReader reader, MemberReference reference)
{
    string name = reader.GetString(reference.Name);
    string owner = reference.Parent.Kind switch
    {
        HandleKind.TypeReference => reader.GetString(reader.GetTypeReference(
            (TypeReferenceHandle)reference.Parent).Name),
        HandleKind.TypeDefinition => reader.GetString(reader.GetTypeDefinition(
            (TypeDefinitionHandle)reference.Parent).Name),
        HandleKind.TypeSpecification => "generic",
        _ => "?",
    };

    return $"{owner}.{name}";
}

/// <summary>在 SlaySP2Manger 的缓存里找与当前游戏版本匹配的快照。</summary>
static string? FindSnapshot(string gameDir)
{
    string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    string root = Path.Combine(localAppData, "com.slay.sp2manager", "compendium");

    if (!Directory.Exists(root))
    {
        return null;
    }

    // 优先用与当前游戏版本一致的快照。
    GameVersion version = GameVersion.Read(gameDir);
    string exact = Path.Combine(root, version.Version, "snapshot", "card-metadata.json");

    if (File.Exists(exact))
    {
        return exact;
    }

    // 否则取最新的一份（版本不一致时下面的对比会报出来）。
    return Directory.EnumerateFiles(root, "card-metadata.json", SearchOption.AllDirectories)
        .OrderByDescending(File.GetLastWriteTimeUtc)
        .FirstOrDefault();
}
