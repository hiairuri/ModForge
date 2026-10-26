using System.Text.Json;
using System.Text.Json.Nodes;
using ModForge.Core;
using ModForge.Core.Generate;
using ModForge.Core.Model;
using ModForge.Core.Modules.CardPicking;
using ModForge.Core.Modules.Descriptions;
using ModForge.Core.Modules.Variables;
using ModForge.Core.Modules.CardSets;
using ModForge.Core.Modules.Effects;
using ModForge.Core.Modules.Targets;

// 效果种类验收：**已搬进 Modules/Effects/Kinds/ 的每一位**，断言它的三个面
// （出码 / 句子 / 界面元数据）。
//
// 为什么要这个工具：效果模块是**逐批**搬迁的，而搬迁期间"生成结果逐字节不变"这个门槛有个
// 盲区——快照夹具没用到那种效果时，`diff` 为空其实什么都没验证（批 1 的六种效果全都没被夹具
// 覆盖，是查出来才补的）。夹具现在覆盖全部 31 种（见 gensnapshot 的 EffMatrix），
// 但夹具只能证明"没变"，不能证明"对"——那需要把期望写下来。两者互补。
//
// 期望值取自**搬迁前的源码**（不是跑一遍新代码抄下来），所以这是一份真正的回归断言。
//
// 用法: kindtest [gameDir]
//
// 需要游戏目录来查**原版名称**（球名"闪电"、状态名"力量"这些）。没有游戏时也能跑，
// 只是那些名字会退回句式表里的兜底词——涉及具体名字的断言会因此失败，所以本工具默认取标准路径。

string gameDir = args.Length > 0
    ? args[0]
    : @"D:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2";

if (Directory.Exists(Path.Combine(gameDir, "data_sts2_windows_x86_64")))
{
    DescriptionModule.Names = new VanillaNames(gameDir);
    Console.WriteLine($"vanilla names: {gameDir}");
}
else
{
    Console.WriteLine("vanilla names: (not found — 球名/状态名会退回兜底词)");
}

int failed = 0;
int passed = 0;

void Check(string what, bool ok, string detail = "")
{
    if (ok)
    {
        passed++;
        return;
    }

    failed++;
    Console.WriteLine($"  [失败] {what}{(detail.Length > 0 ? $"  ({detail})" : "")}");
}

// 造一张只带一条效果的卡，取它生成出来的源码。
string CardSource(EffectDef effect)
{
    var project = new ModProject { Id = "KindProbe", Name = "Kind Probe", Version = "1.0.0" };

    // 生成物会引用 Generated.Powers 命名空间，所以工程要有一个状态。
    project.Powers.Add(new PowerDef
    {
        Id = "MARK",
        Name = new LocalizedText("eng", "Mark"),
        Kind = PowerKind.Buff,
    });

    project.Cards.Add(new CardDef
    {
        Id = "PROBE",
        Name = new LocalizedText("eng", "Probe"),
        Description = new LocalizedText("eng", "Test."),
        Pool = "ironclad",
        Type = CardType.Skill,
        Rarity = CardRarity.Common,
        Target = CardTargetType.Self,
        EnergyCost = 1,
        Effects = { effect },
    });

    return new CodeGenerator(project).GenerateAll()["Cards/Probe.cs"];
}

// ===========================================================================
// 批 1：最简单的六种（无变量槽、无选牌、无校验）
// ===========================================================================
Console.WriteLine("== 批 1：获得能量 / 星辰、消耗此牌、三种充能球 ==");

// ---- 获得能量 ----
{
    string src = CardSource(new EffectDef
    {
        Kind = EffectKind.GainEnergy, Target = EffectTarget.Self, Amount = 2,
    });

    // 数值从**它自己的变量**里读（这条效果现在占变量槽，见 tripletest）——
    // 以前是写死字面量 `GainEnergy(2m, ...)`，那条路会让升级失效。
    Check("获得能量：读自己的变量",
        src.Contains("await PlayerCmd.GainEnergy(DynamicVars[\"Energy\"].BaseValue, Owner);"),
        "出码不对 —— 应当读 DynamicVars[\"Energy\"].BaseValue");
}

// ---- 获得星辰 ----
{
    string src = CardSource(new EffectDef
    {
        Kind = EffectKind.GainStars, Target = EffectTarget.Self, Amount = 2,
    });

    Check("获得星辰：读自己的变量",
        src.Contains("await PlayerCmd.GainStars(DynamicVars[\"Stars\"].BaseValue, Owner);"),
        "出码不对 —— 应当读 DynamicVars[\"Stars\"].BaseValue");
}

// ---- 消耗牌（"消耗这张牌"是它的"此牌"那一档）----
{
    string src = CardSource(new EffectDef
    {
        Kind = EffectKind.ExhaustCards, Target = EffectTarget.Self,
        Pick = new CardPick { Source = PickSource.ThisCard },
    });

    Check("消耗牌 → 此牌：挑出来的就是这一张",
        src.Contains("var pickedCardsExhaust0 = new List<CardModel> { this };"),
        "没走选牌器——'此牌'应当是作用对象的一档");

    Check("消耗牌：把挑到的牌挪进消耗堆",
        src.Contains("await CardPileCmd.Add(exhaustTarget, PileType.Exhaust);"),
        "出码不对——它应当是直接 Add 到消耗堆，不走弃牌");
}

// ---- 消耗牌：张数就是它的数值（不是选牌器自己那一格）----
{
    string src = CardSource(new EffectDef
    {
        Kind = EffectKind.ExhaustCards, Target = EffectTarget.Self,
        Amount = 2,
        Pick = new CardPick
        {
            Source = PickSource.Piles, Piles = { PileKind.DrawPile },
            Mode = PickMode.Choose,
        },
    });

    Check("消耗牌：张数读的是效果自己的变量（变量模块传进来）",
        src.Contains("new CardSelectorPrefs(new LocString(\"cards\", \"PROBE.selectionScreenPrompt\"), (int)Math.Max(1m, DynamicVars[\"ExhaustCount\"].BaseValue))"),
        "出码不对——张数应当来自效果数值（ExhaustCount 变量），而不是选牌器自己算");

    Check("消耗牌：变量在卡上声明了（描述里的占位符才有得可查）",
        src.Contains("new DynamicVar(\"ExhaustCount\", 2m)"),
        "没有声明 ExhaustCount 变量");

    // 升级改张数走的是同一个变量（"升级后多消耗一张"就是它）。
    string upgraded = CardSource(new EffectDef
    {
        Kind = EffectKind.ExhaustCards, Target = EffectTarget.Self, Amount = 2,
        Upgrade = new EffectUpgrade { AmountDelta = 1 },
        Pick = CardPick.InPile(PileKind.DrawPile, PickMode.Choose),
    });

    Check("消耗牌：升级改的是张数那个变量（升级后多消耗一张）",
        upgraded.Contains("DynamicVars[\"ExhaustCount\"].UpgradeValueBy(1m)"),
        "升级没有改张数");
}

// ---- 消耗牌：整手牌（"全部"那一档）----
{
    string src = CardSource(new EffectDef
    {
        Kind = EffectKind.ExhaustCards, Target = EffectTarget.Self,
        
    Pick = CardPick.InPile(PileKind.Hand, PickMode.All),
    });

    Check("消耗牌 → 手牌全部：扫整个牌堆",
        src.Contains("PileType.Hand.GetPile(Owner)") && src.Contains("foreach (var exhaustTarget in"),
        "出码不对——'全部'应当直接遍历该牌堆");
}

// ---- 生成充能球 ----
{
    string src = CardSource(new EffectDef
    {
        Kind = EffectKind.ChannelOrb, Target = EffectTarget.Self, Amount = 1, Orb = OrbKind.Frost,
    });

    Check("生成充能球：按球种生成对应的 Orb 类型",
        src.Contains("OrbCmd.Channel<MegaCrit.Sts2.Core.Models.Orbs.FrostOrb>"),
        "球种类型写错了");
    Check("生成充能球：逐个生成（球位满了会自动激发，与原版一致）",
        src.Contains("for (int n = 0; n < channelCount; n++)"),
        "循环形状不对");
}

// ---- 激发充能球 ----
{
    string src = CardSource(new EffectDef
    {
        Kind = EffectKind.EvokeOrbs, Target = EffectTarget.Self, Amount = 2,
    });

    // 原版形状：**同一个球激发 m 次**，只有最后一次把它移出队列（dequeue）。
    // 逐次都出队的话激发的是 m 个不同的球——那与原版不是一回事。
    Check("激发充能球：同一个球激发 m 次，末次才出队",
        src.Contains("OrbCmd.EvokeNext(choiceContext, Owner, i == evokeCount - 1)")
        && src.Contains("for (int i = 0; i < evokeCount; i++)"),
        "出码不对——应当是 EvokeNext(..., i == evokeCount - 1)");
}

// ---- 激发充能球：最左侧那一路 ----
{
    string src = CardSource(new EffectDef
    {
        Kind = EffectKind.EvokeOrbs, Target = EffectTarget.Self, Amount = 1,
        EvokeTarget = OrbEvokeTarget.Leftmost,
    });

    Check("激发充能球 → 最左侧：走 EvokeLast",
        src.Contains("OrbCmd.EvokeLast"),
        "出码不对——最左侧应当是 EvokeLast（队列的最后一个）");
}

// ---- 激发充能球：全部（原版"碎片"的形状）----
{
    string src = CardSource(new EffectDef
    {
        Kind = EffectKind.EvokeOrbs, Target = EffectTarget.Self, Amount = 2,
        EvokeTarget = OrbEvokeTarget.All,
    });

    // "每个球各激发 m 次"：数一遍球数，每轮把一个球激发 m 次（末次出队），
    // 所以下一轮正好轮到下一个球——原版碎片的写法。
    // 名字带编号（一张牌上可能有好几条激发效果，不带就重名、编译不过），
    // 所以这里只比形状，不比编号的值。
    Check("激发充能球 → 全部：每个球各激发 m 次",
        System.Text.RegularExpressions.Regex.IsMatch(src, @"int evokeTotal\d+ = evokeQueue\d+\.Orbs\.Count;")
        && System.Text.RegularExpressions.Regex.IsMatch(src, @"for \(int evokeIndex\d+ = 0;")
        && src.Contains("i == evokeCount - 1"),
        "出码不对——应当数一遍球数、逐个激发");
}

// ---- 激发充能球：随机（游戏没有这个接口，自己挑一个）----
{
    string src = CardSource(new EffectDef
    {
        Kind = EffectKind.EvokeOrbs, Target = EffectTarget.Self, Amount = 1,
        EvokeTarget = OrbEvokeTarget.Random,
    });

    // 随机流叫 CombatOrbGeneration（枚举值才叫 CombatOrbs）——写错名字是编译不过的，
    // 但这条断言让它在 kindtest 就能发现（newfx2 那次真编译才抓到，晚了）。
    Check("激发充能球 → 随机：用战斗的球随机流挑一个",
        System.Text.RegularExpressions.Regex.IsMatch(
            src, @"RunState\.Rng\.CombatOrbGeneration\.NextInt\(evokeOrbs\d+\.Count\)"),
        "出码不对——随机应当走 Rng.CombatOrbGeneration");

    Check("激发充能球 → 随机：挑中的球先挪到队首再激发",
        System.Text.RegularExpressions.Regex.IsMatch(src, @"evokeQueue\d+\.Remove\(evokeOrb\);")
        && System.Text.RegularExpressions.Regex.IsMatch(src, @"evokeQueue\d+\.Insert\(0, evokeOrb\);"),
        "出码不对——EvokeNext 只认队首，别处的球要先挪过去");

    Check("激发充能球 → 随机：单个球挪队首才需要，队首那个是空操作",
        System.Text.RegularExpressions.Regex.IsMatch(src, @"if \(evokeQueue\d+\.Orbs\.IndexOf\(evokeOrb\) > 0\)"),
        "应当先判断它在不在队首（最右侧那种情况下本来就是空操作）");
}

// ---- 激发充能球：只激发某些种类 ----
{
    string src = CardSource(new EffectDef
    {
        Kind = EffectKind.EvokeOrbs, Target = EffectTarget.Self, Amount = 1,
        EvokeTarget = OrbEvokeTarget.All,
        OrbKinds = { OrbKind.Lightning, OrbKind.Frost },
    });

    Check("激发充能球 → 限定种类：按球的模型 id 筛（LIGHTNING_ORB / FROST_ORB）",
        src.Contains("o.Id.Entry == \"LIGHTNING_ORB\" || o.Id.Entry == \"FROST_ORB\""),
        "出码不对——种类应当用游戏自己的球 id 判");

    Check("激发充能球 → 有限定种类时不会直接调 EvokeNext 扫全场",
        !src.Contains("int evokeTotal ="),
        "筛了种类就不能按球数盲扫（符合的球未必在队首）");
}

// ---- 获得充能球栏位 ----
{
    string src = CardSource(new EffectDef
    {
        Kind = EffectKind.AddOrbSlots, Target = EffectTarget.Self, Amount = 1,
    });

    Check("获得充能球栏位：读自己的变量",
        src.Contains("await OrbCmd.AddSlots(Owner, (int)DynamicVars[\"OrbSlots\"].BaseValue);"),
        "出码不对 —— 应当读 DynamicVars[\"OrbSlots\"].BaseValue");
}

// ---------------------------------------------------------------------------
// 句子：验证行为把该说的词接上了（球名、牌堆、"能量/星辰"这些）
// ---------------------------------------------------------------------------
Console.WriteLine();
Console.WriteLine("== 批 1：句子 ==");

string Sentence(EffectDef effect, string language) => DescriptionModule.Describe(null, effect, language);

// 生成充能球要把**球名**嵌进句子——这是它这个行为唯一非平凡的地方（球名来自游戏本地化表）。
Check("生成充能球：句子里带球名（中文）",
    Sentence(new EffectDef { Kind = EffectKind.ChannelOrb, Amount = 1, Orb = OrbKind.Lightning }, "zhs")
        .Contains("闪电"),
    "没有把球名嵌进句子");

Check("生成充能球：句子里带球名（英文）",
    Sentence(new EffectDef { Kind = EffectKind.ChannelOrb, Amount = 1, Orb = OrbKind.Lightning }, "eng")
        .Contains("Lightning", StringComparison.OrdinalIgnoreCase),
    "没有把球名嵌进句子");

Check("消耗牌 → 此牌：句子里读出\"这张牌\"",
    Sentence(new EffectDef { Kind = EffectKind.ExhaustCards, Pick = CardPick.This() }, "zhs")
        .Contains("消耗这张牌"),
    "句子不对——作用对象是\"此牌\"时应当读成\"消耗这张牌\"");

Check("消耗牌 → 手牌全部：句子里读出牌堆与\\\"全部\\\"",
    Sentence(new EffectDef
    {
        Kind = EffectKind.ExhaustCards,
        Pick = CardPick.InPile(PileKind.Hand, PickMode.All),
    }, "zhs").Contains("消耗手牌中所有牌"),
    "句子不对——应当读成\"消耗手牌中所有牌\"");

// 按某一维筛（流程四~八）时**不念牌堆**，所以张数（流程九/十）必须由条件短语那一支自己说出来：
// 只念条件的话，"至多 1 张攻击牌"会变成了"所有攻击牌"——与生成代码（MinSelect=0, MaxSelect=1）对不上。
Check("消耗牌 → 按类型（全部）：句子读成\"消耗所有攻击牌\"",
    Sentence(new EffectDef
    {
        Kind = EffectKind.ExhaustCards,
        Pick = new CardPick
        {
            Types = { CardType.Attack },
            Mode = PickMode.All,
        },
    }, "zhs").Contains("消耗所有攻击牌"),
    "句子不对——带筛选时就念条件，不念牌堆");

Check("消耗牌 → 按类型（至多 1 张）：句子里必须带张数",
    Sentence(new EffectDef
    {
        Kind = EffectKind.ExhaustCards,
        Pick = new CardPick
        {
            Types = { CardType.Attack },
            Mode = PickMode.AtMost,
            Count = new(1, new ValueSource()),
        },
    }, "zhs").Contains("消耗至多 1 张攻击牌"),
    "句子不对——\"至多 N 张\"被念成了\"所有\"，卡面与生成代码就不一致了");

Check("消耗牌 → 按牌 ID（选择 2 张）：句子里读出牌名与张数",
    Sentence(new EffectDef
    {
        Kind = EffectKind.ExhaustCards,
        // 张数就是这条效果自己的数值（Amount），不是选牌器那一格。
        Amount = 2,
        Pick = new CardPick
        {
            CardIds = { "CARD.STRIKE_IRONCLAD" },
            Mode = PickMode.Choose,
        },
    }, "zhs").Contains("选择的 2 张打击"),
    "句子不对——按牌名时应当读出牌名与张数（张数来自效果自己的数值）");

// **多维度并存**：维度之间是"与"，所以定语链与费用从句都要念出来。
Check("消耗牌 → 稀有 + 攻击 + 能量>2：句子把三个维度都念出来",
    Sentence(new EffectDef
    {
        Kind = EffectKind.ExhaustCards,
        EnergyAmount = 2,
        Pick = new CardPick
        {
            Rarities = { CardRarity.Rare },
            Types = { CardType.Attack },
            EnergyComparison = CostComparison.GreaterThan,
            Mode = PickMode.All,
        },
    }, "zhs").Contains("能量消耗大于 2 的稀有攻击牌"),
    "句子不对——多维度并存时定语链与费用从句都要念出来");

Check("消耗牌 → 稀有 + 攻击 + 能量>2 + 星辰>=0（英文）：费用从句放在名词后面",
    Sentence(new EffectDef
    {
        Kind = EffectKind.ExhaustCards,
        EnergyAmount = 2,
        StarAmount = 0,
        Pick = new CardPick
        {
            Rarities = { CardRarity.Rare },
            Types = { CardType.Attack },
            EnergyComparison = CostComparison.GreaterThan,
            StarComparison = CostComparison.AtLeast,
            Mode = PickMode.All,
        },
    }, "eng").Contains("Rare Attack cards with energy cost above 2 and star cost at least 0"),
    "英文句子不对——英文把费用从句放在名词后面");

Check("消耗牌 → 星辰为 X：句子里读出 X（不念阈值）",
    Sentence(new EffectDef
    {
        Kind = EffectKind.ExhaustCards,
        StarAmount = 3,
        Pick = new CardPick
        {
            StarComparison = CostComparison.IsX,
            Mode = PickMode.All,
        },
    }, "zhs").Contains("星辰消耗为 X"),
    "句子不对——X 那一档不该念阈值");

// 张数是**算出来的**（有来源）时：卡面写不出确定的数字，所以句子把来源说出来当**张数**——
// 张数后面带量词"张"（"等于 当前星辰 张牌"，与"抽 等于 X 张牌"同形），
// 不能写一个看着像真的的数字，也不能拿"…的牌"去套（那样量词没了）。
Check("消耗牌 → 张数是算出来的（至多）：把来源念成张数",
    Sentence(new EffectDef
    {
        Kind = EffectKind.ExhaustCards,
        Amount = 0,
        Source = new ValueSource { Actor = ValueSourceActor.Self, Kind = ValueSourceKind.Stars },
        Pick = CardPick.InPile(PileKind.Hand, PickMode.AtMost),
    }, "zhs").Contains("至多消耗 等于 当前星辰 张牌"),
    "句子不对——算出来的张数应当念成来源 + 量词，而不是某个数字");

// 有筛选时更要紧：名词本身就是一条"…的牌"的定语链，张数若还写"等于 X 的{名词}"，
// 会叠成两个"的"、且看不出 X 是张数。
Check("消耗牌 → 张数算出来 + 有筛选：张数带量词、不与名词叠「的」",
    Sentence(new EffectDef
    {
        Kind = EffectKind.ExhaustCards,
        Amount = 0,
        Source = new ValueSource { Actor = ValueSourceActor.Self, Kind = ValueSourceKind.HandCount },
        EnergyAmount = 1,
        Pick = new CardPick
        {
            Types = { CardType.Attack },
            EnergyComparison = CostComparison.GreaterThan,
            Mode = PickMode.Choose,
        },
    }, "zhs").Contains("消耗 等于 手牌数 张能量消耗大于 1 的攻击牌"),
    "句子不对——张数要带量词「张」，且不能与以「…的牌」结尾的定语链叠「的」");

Check("消耗牌 → 张数是算出来的 + 按类型筛：名词跟着维度走",
    Sentence(new EffectDef
    {
        Kind = EffectKind.ExhaustCards,
        Amount = 0,
        Source = new ValueSource { Actor = ValueSourceActor.Self, Kind = ValueSourceKind.Stars },
        Pick = new CardPick
        {
            Types = { CardType.Attack },
            Mode = PickMode.Choose,
        },
    }, "zhs").Contains("消耗 等于 当前星辰 张攻击牌"),
    "句子不对——带筛选时名词该是那条定语链（攻击牌）");

// ---- 句式判据：什么时候还能用"定语栈"（紧凑），什么时候必须改成显式从句 ----
//
// 判据在 PickerWording.UseShortForm 一处（每个取值维度恰好一个值、且这类维度不超过两个），
// 下面这几条按边界把它钉死。**要紧的是：判据的"紧凑"那一侧与旧输出逐字节相同**，
// 所以这些断言同时证明了"常见的短句子没有被这次改动碰到"。
//
// 按条件数牌是这类句子最短的一条路（"等于 X 的牌数"），所以用它当夹具。
EffectDef CountBy(CardFilter filter) => new()
{
    Kind = EffectKind.DealDamage,
    Target = EffectTarget.Target,
    Amount = 0,
    Source = new ValueSource
    {
        Kind = ValueSourceKind.FilteredCardCount,
        CountScope = CountScope.Hand,
        Filter = filter,
    },
};

// 一个维度一个值 → 紧凑（与旧输出一致：类型就是"攻击"，不带标签词）。
Check("句式：一维一值走紧凑（\"攻击牌数\"）",
    Sentence(CountBy(new CardFilter { Types = { CardType.Attack } }), "zhs")
        .Contains("手牌中的攻击牌数"),
    "一维一值应当仍是紧凑的定语栈");

// 两个维度各一个值 → 仍然紧凑（这是最常见的那一档，必须一个字都不变）。
Check("句式：两维各一值仍走紧凑（\"能量消耗大于 2 的稀有攻击牌数\"）",
    Sentence(CountBy(new CardFilter
    {
        Rarities = { CardRarity.Rare },
        Types = { CardType.Attack },
        EnergyComparison = CostComparison.GreaterThan,
        EnergyThreshold = 2,
    }), "zhs").Contains("手牌中的能量消耗大于 2 的稀有攻击牌数"),
    "两维各一个值时定语栈还读得通，不该改成从句");

// 同一维有**多个取值** → 显式从句（定语栈在这里会粘成"攻击技能能力"）。
Check("句式：同一维多值改显式（\"类型为攻击、技能的\"，值之间带顿号）",
    Sentence(CountBy(new CardFilter { Types = { CardType.Attack, CardType.Skill } }), "zhs")
        .Contains("手牌中的类型为攻击、技能的牌数"),
    "同一维多个取值必须改用显式从句，并用顿号分开");

// 三个维度（每维一个值）→ 显式从句：定语栈叠到三段就散了。
Check("句式：三维起改显式（每维一个值也是）",
    Sentence(CountBy(new CardFilter
    {
        Rarities = { CardRarity.Rare },
        Types = { CardType.Attack },
        Keywords = { CardKeyword.Exhaust },
    }), "zhs").Contains("手牌中的稀有度为稀有的且类型为攻击的且带有消耗的牌数"),
    "三维起应当改用显式从句，即便每维只有一个值");

// 你给的那条完整例子：四维、三处多值，费用从句在**最后**（紧凑写法里它在最前面）。
// 牌名那一维**不硬写名字**：这个工具没有工程也没有游戏本地化表，牌名会退回 Pascal 形式
//（"StrikeIronclad"），写死它反而会把"名字怎么解析"混进句式断言里。所以只钉从句的
// 顺序与连接词，把牌名那一格夹在中间。
string fourDims = Sentence(CountBy(new CardFilter
{
    Types = { CardType.Attack, CardType.Skill, CardType.Power },
    Keywords = { CardKeyword.Retain, CardKeyword.Exhaust },
    CardIds = { "CARD.STRIKE_IRONCLAD", "CARD.DEFEND_IRONCLAD" },
    EnergyComparison = CostComparison.AtMost,
    EnergyThreshold = 2,
}), "zhs");

Check("句式：四维 + 多值 + 费用（顺序：类型 → 关键词 → 牌名 → 费用，费用在最后）",
    fourDims.Contains("手牌中的类型为攻击、技能、能力的且带有保留、消耗的且名字中带有")
    && fourDims.Contains("的且能量消耗不大于 2 的牌数"),
    "四维多值的句式不对（从句顺序或连接词错）");

Check("句式：英文同一维多值用 \"A, B or C\"",
    Sentence(CountBy(new CardFilter { Types = { CardType.Attack, CardType.Skill, CardType.Power } }), "eng")
        .Contains("cards with type Attack, Skill or Power in your hand"),
    "英文同一维的多个取值要用 or 连起来");

Check("句式：英文从句跟在名词后面",
    Sentence(CountBy(new CardFilter
    {
        Rarities = { CardRarity.Common, CardRarity.Uncommon },
        Tags = { CardTag.Strike },
    }), "eng").Contains("cards of rarity Common or Uncommon, with the Strike tag in your hand"),
    "英文的显式从句应当跟在名词后面");

// 关键词在**紧凑**写法里仍要那层「」包装（裸词直接接名词会被读成别的意思）。
Check("句式：关键词紧凑写法仍有「」包装（\"带「消耗」的牌数\"）",
    Sentence(CountBy(new CardFilter { Keywords = { CardKeyword.Exhaust } }), "zhs")
        .Contains("手牌中的带「消耗」的牌数"),
    "关键词在紧凑写法里要套上「」，裸词接名词会读错");

Check("获得能量：句子说的是能量",
    Sentence(new EffectDef { Kind = EffectKind.GainEnergy, Amount = 1 }, "zhs").Contains("能量"),
    "句子不对");

Check("获得星辰：句子说的是星辰",
    Sentence(new EffectDef { Kind = EffectKind.GainStars, Amount = 1 }, "zhs").Contains("星辰"),
    "句子不对");

Check("获得充能球栏位：句子说的是栏位",
    Sentence(new EffectDef { Kind = EffectKind.AddOrbSlots, Amount = 1 }, "zhs").Contains("栏位"),
    "句子不对");

Check("激发充能球：句子说的是激发，并写明是哪一头（中文）",
    Sentence(new EffectDef { Kind = EffectKind.EvokeOrbs, Amount = 1 }, "zhs") is string evokeZh
    && evokeZh.Contains("激发") && evokeZh.Contains("最右侧"),
    "句子不对——必须写明激发的是哪一头的球");

Check("激发充能球：最左侧那一路也说得出话（英文）",
    Sentence(
        new EffectDef { Kind = EffectKind.EvokeOrbs, Amount = 2, EvokeTarget = OrbEvokeTarget.Leftmost },
        "eng").Contains("leftmost", StringComparison.OrdinalIgnoreCase),
    "句子不对——最左侧应当读成 leftmost");

Check("激发充能球 → 全部：中文读成\"全部的充能球\"（复数）",
    Sentence(
        new EffectDef { Kind = EffectKind.EvokeOrbs, Amount = 2, EvokeTarget = OrbEvokeTarget.All },
        "zhs").Contains("全部的充能球"),
    "句子不对——全部那一档应当用复数说法");

Check("激发充能球 → 全部：英文读成 all of your Orbs（复数，与游戏一致）",
    Sentence(
        new EffectDef
        {
            Kind = EffectKind.EvokeOrbs, Target = EffectTarget.Self, Amount = 2,
            EvokeTarget = OrbEvokeTarget.All,
        },
        "eng").Contains("all of your Orbs", StringComparison.OrdinalIgnoreCase),
    "句子不对——游戏自己的写法是 \"Evoke all of your Orbs\"");

// 作用于**别人**时句子里要念出对象（玩家轴也一样）：不然"给队友抽 2 张牌"会印成"抽 2 张牌"，
// 卡面与代码对不上。物主也要跟着换（英文的 your → their）。
Check("玩家轴：作用于别人时句子念出对象（中文，且按轴读作「所有玩家」）",
    Sentence(new EffectDef { Kind = EffectKind.DrawCards, Target = EffectTarget.AllAllies, Amount = 2 }, "zhs")
        .Contains("所有玩家"),
    "句子不对——作用于别人时必须念出是谁，而且玩家轴上的「所有友方」要读作「所有玩家」");
Check("宠物档：句子里念出「自己的奥斯提」（而不是像作用在自己身上）",
    Sentence(new EffectDef
    {
        Kind = EffectKind.ApplyPower, Target = EffectTarget.SelfPet,
        PowerId = "POWER.STRENGTH_POWER", Amount = 1,
    }, "zhs").Contains("自己的奥斯提"),
    "句子不对——宠物档必须念出来");
Check("宠物档：四档的说法各不相同（自己的 / 目标的 / 所有友方的 / 随机一名友方的）",
    Sentence(new EffectDef { Kind = EffectKind.Heal, Target = EffectTarget.TargetPet, Amount = 4 }, "zhs")
        .Contains("目标的奥斯提")
    && Sentence(new EffectDef { Kind = EffectKind.GainBlock, Target = EffectTarget.AllAlliesPet, Amount = 1 }, "zhs")
        .Contains("所有友方的奥斯提")
    && Sentence(new EffectDef { Kind = EffectKind.GainBlock, Target = EffectTarget.RandomAllyPet, Amount = 1 }, "zhs")
        .Contains("随机一名友方的奥斯提")
    && Sentence(new EffectDef { Kind = EffectKind.GainBlock, Target = EffectTarget.AllOtherAlliesPet, Amount = 1 }, "zhs")
        .Contains("所有其他玩家的奥斯提")
    && Sentence(new EffectDef { Kind = EffectKind.GainBlock, Target = EffectTarget.RandomOtherAllyPet, Amount = 1 }, "zhs")
        .Contains("随机其他玩家的奥斯提"));
Check("宠物档：复数那一档用复数所有格（英文 all allies' Osty，不是 all allies's）",
    Sentence(new EffectDef
    {
        Kind = EffectKind.ApplyPower, Target = EffectTarget.AllAlliesPet,
        PowerId = "POWER.STRENGTH_POWER", Amount = 1,
    }, "eng") is string petEn
    && petEn.Contains("all allies' Osty", StringComparison.OrdinalIgnoreCase),
    "句子不对——英文的复数所有格只加撇号");
Check("玩家轴：作用于别人时物主也跟着换（英文的 your → their）",
    Sentence(new EffectDef
    {
        Kind = EffectKind.EvokeOrbs, Target = EffectTarget.AllAllies, Amount = 2,
        EvokeTarget = OrbEvokeTarget.Rightmost,
    }, "eng") is string evokeTheir
    && evokeTheir.Contains("their rightmost", StringComparison.OrdinalIgnoreCase)
    && !evokeTheir.Contains("your", StringComparison.OrdinalIgnoreCase),
    "句子不对——队友的球不该说成 your");

Check("激发充能球 → 限定种类：句子把种类念出来",
    Sentence(new EffectDef
    {
        Kind = EffectKind.EvokeOrbs, Amount = 1,
        EvokeTarget = OrbEvokeTarget.All,
        OrbKinds = { OrbKind.Dark, OrbKind.Plasma },
    }, "zhs") is string evokeTyped
    && evokeTyped.Contains("黑暗") && evokeTyped.Contains("等离子") && evokeTyped.Contains("、"),
    "句子不对——要念出限定的种类（中文用顿号连起来）");

// 数值是**算出来的**时走另一条模板（"获得 等于手牌数 的能量"）。这一组以前只念基础值，
// 于是"生成 1 个闪电充能球"与实际生成的数量对不上（真实是 1 + 手牌数 个）——所以逐个钉住。
foreach ((EffectKind Kind, string Expect) in new (EffectKind, string)[]
{
    (EffectKind.GainEnergy, "获得 等于 手牌数 的能量"),
    (EffectKind.GainStars, "获得 等于 手牌数 的星辰"),
    (EffectKind.AddOrbSlots, "获得 等于 手牌数 个充能球栏位"),
    (EffectKind.ChannelOrb, "生成 等于 手牌数 个闪电充能球"),
    (EffectKind.EvokeOrbs, "激发你最右侧的充能球 等于 手牌数 次"),
})
{
    string calc = Sentence(new EffectDef
    {
        Kind = Kind,
        Target = EffectTarget.Self,
        Source = new ValueSource { Actor = ValueSourceActor.Self, Kind = ValueSourceKind.HandCount },
    }, "zhs");

    Check($"{Kind}：数值算出来的那句话念出变量来源",
        calc.Contains(Expect),
        $"算出来的句子不对：{calc}");
}

// 英文的球名与 "Orb"/"Orbs" 之间要有一个空格（中文不需要）——少它的后果是 "FrostOrbs"。
Check("激发充能球 → 限定种类（英文）：球名与 Orb 之间有空格",
    Sentence(new EffectDef
    {
        Kind = EffectKind.EvokeOrbs, Amount = 2,
        EvokeTarget = OrbEvokeTarget.All,
        OrbKinds = { OrbKind.Frost },
    }, "eng") is string evokeSpacing && evokeSpacing.Contains("Frost Orbs"),
    "英文把球名与 Orb 连在一起了（FrostOrbs）");

Check("激发充能球 → 多个种类（英文）：连接词与球名都正常",
    Sentence(new EffectDef
    {
        Kind = EffectKind.EvokeOrbs, Amount = 1,
        EvokeTarget = OrbEvokeTarget.All,
        OrbKinds = { OrbKind.Lightning, OrbKind.Frost },
    }, "eng") is string evokeJoin && evokeJoin.Contains("Lightning or Frost Orbs"),
    "句子不对——要念成 \"Lightning or Frost Orbs\"");

// 查不到球名（没有游戏目录）时退回枚举名——**不能**退回"充能球/Orb"那种词：
// 模板后面还会补一次后缀，于是会印成"生成 1 个充能球充能球"（实测过）。
{
    VanillaNames? keep = DescriptionModule.Names;
    DescriptionModule.Names = null;   // 模拟"拿不到游戏目录"

    string noGame = DescriptionModule.Describe(
        null, new EffectDef { Kind = EffectKind.ChannelOrb, Amount = 1, Orb = OrbKind.Lightning }, "zhs");

    DescriptionModule.Names = keep;

    Check("生成充能球：没有游戏目录时退回枚举名，不重复后缀",
        !noGame.Contains("充能球充能球") && noGame.Contains("Lightning"),
        $"没有游戏目录时的句子不对：{noGame}");
}

// 计算型走另一条模板（"获得 等于手牌数 的能量"），所以两种都要说得出话来。
foreach (EffectKind kind in new[] { EffectKind.GainEnergy, EffectKind.GainStars })
{
    string calc = Sentence(new EffectDef
    {
        Kind = kind,
        Amount = 2,
        Source = new ValueSource { Actor = ValueSourceActor.Self, Kind = ValueSourceKind.HandCount },
    }, "zhs");

    Check($"{kind}：计算型也说得通（带来源）",
        calc.Contains("手牌") && calc.Contains("2"),
        $"计算型的句子不对：{calc}");
}

// ---------------------------------------------------------------------------
// 界面元数据
// ---------------------------------------------------------------------------
Console.WriteLine();
Console.WriteLine("== 批 1：界面元数据 ==");

static bool FieldEquals(EffectSchemaEntry entry, string field, object expected)
{
    object? actual = field switch
    {
        "kind" => entry.kind,
        "labelZh" => entry.labelZh,
        "amount" => entry.amount,
        "needsOrb" => entry.needsOrb,
        "needsEvokeTarget" => entry.needsEvokeTarget,
        "needsOrbFilter" => entry.needsOrbFilter,
        "needsPower" => entry.needsPower,
        "hasPowerRemoval" => entry.hasPowerRemoval,
        "targets" => string.Join(",", entry.targets),
        _ => null,
    };

    return Equals(actual, expected);
}

// 选牌器那十步的投影（pick）读的是**界面真正拿到的那份**（EditorSchema.Build 的 JSON），
// 不是目录里的原始条目：搬运后的效果走 Kinds/ 自己给的条目，投影是在 Build 里补上去的，
// 只看 EffectCatalog.SchemaOf 会漏掉"界面到底收没收到"这件事（消耗牌曾经就是这样漏的）。
JsonNode? EffectEntry(string kind)
{
    JsonNode root = JsonNode.Parse(JsonSerializer.Serialize(EditorSchema.Build()))!;

    foreach (JsonNode? entry in root["effects"]!.AsArray())
    {
        if (entry!["kind"]!.GetValue<string>() == kind)
        {
            return entry;
        }
    }

    return null;
}

static string Join(JsonNode? array) =>
    array is null ? "<null>" : string.Join(",", array.AsArray().Select(n => n!.GetValue<string>()));

// ---- 界面下发的下拉候选 ----
//
// 比较方式的候选里**不能有 `None`**：它是模型侧的哨兵（"这一维没启用"），而"启不启用"
// 在界面上由"筛选维度"那个多值框表达。把它列进下拉，用户就能选到一个"选了等于没启用、
// 可那一行还在"的档位（界面上只剩一个"不筛选"的下拉，自相矛盾）。
Check("比较方式的候选里没有哨兵 None（它不给用户选）",
    !Join(JsonNode.Parse(JsonSerializer.Serialize(EditorSchema.Build()))!["costComparisons"]).Contains("None"),
    "候选里出现了 None —— 那个下拉只该回答\"怎么比\"");

Check("比较方式的候选是其余六档、且顺序不变",
    Join(JsonNode.Parse(JsonSerializer.Serialize(EditorSchema.Build()))!["costComparisons"]) ==
        "AtLeast,AtMost,GreaterThan,LessThan,NotEqual,IsX",
    $"实际：{Join(JsonNode.Parse(JsonSerializer.Serialize(EditorSchema.Build()))!["costComparisons"])}");

Check("获得能量：kind 由目录填对", FieldEquals(EffectCatalog.SchemaOf(EffectKind.GainEnergy), "kind", "GainEnergy"));
Check("获得能量：要填数值", FieldEquals(EffectCatalog.SchemaOf(EffectKind.GainEnergy), "amount", true));
Check("消耗牌：要填数值——它的数值就是消耗几张",
    FieldEquals(EffectCatalog.SchemaOf(EffectKind.ExhaustCards), "amount", true));
Check("消耗牌：标签是「消耗牌」",
    FieldEquals(EffectCatalog.SchemaOf(EffectKind.ExhaustCards), "labelZh", "消耗牌"));
Check("消耗牌：要挑牌（走选牌器十流程）",
    EffectEntry("ExhaustCards")?["pick"] is not null,
    "界面收到的条目里没有 pick —— 十流程表单就出不来");
Check("消耗牌：挑牌策略里**没有卡组**（消耗堆是战斗内的东西），但有临时牌集合",
    Join(EffectEntry("ExhaustCards")?["pick"]?["sources"]) == "ThisCard,Piles,Set",
    $"实际：{Join(EffectEntry("ExhaustCards")?["pick"]?["sources"])}");
Check("消耗牌：七维筛选都可用、牌堆可多选（只屏蔽卡组那一档）",
    Join(EffectEntry("ExhaustCards")?["pick"]?["dimensions"]) ==
        "CardType,CardId,Tag,Keyword,Rarity,EnergyCost,StarCost" &&
    EffectEntry("ExhaustCards")?["pick"]?["singlePile"]?.GetValue<bool>() == false,
    $"实际：{Join(EffectEntry("ExhaustCards")?["pick"]?["dimensions"])}");
Check("消耗牌：流程九 / 十都要（全部·选择·随机·至多 + 张数）",
    EffectEntry("ExhaustCards")?["pick"]?["usesMode"]?.GetValue<bool>() == true);
Check("消耗牌：要提示语（会打开选择界面，导出必须写 selectionScreenPrompt）",
    EffectEntry("ExhaustCards")?["pick"]?["usesPrompt"]?.GetValue<bool>() == true);
Check("升级：卡组那一档在（对照说明只屏蔽了消耗那一处）",
    CardPickPolicies.For(EffectKind.UpgradeCardsInPile).Sources.Contains(PickSource.Deck),
    "升级/变化/附魔这三类要能挑卡组里的牌——卡组那一档的实现必须留着");
Check("自动打出：选牌策略标了\"此牌那一档不要数值\"",
    CardPickPolicies.For(EffectKind.AutoPlay).SelfNoAmount &&
    EffectEntry("ExhaustCards")?["pick"]?["selfNoAmount"]?.GetValue<bool>() == false,
    "自动打出选\"此牌\"时没有张数；其余效果这一格是假（界面据此决定隐不隐数值）");
Check("生成充能球：要选球种",
    FieldEquals(EffectCatalog.SchemaOf(EffectKind.ChannelOrb), "needsOrb", true));
Check("激发充能球：要选激发哪些（四档）",
    FieldEquals(EffectCatalog.SchemaOf(EffectKind.EvokeOrbs), "needsEvokeTarget", true));
Check("激发充能球：可以筛球的种类（多值那一格）",
    FieldEquals(EffectCatalog.SchemaOf(EffectKind.EvokeOrbs), "needsOrbFilter", true));

// ---- 批 2：共用形状家族（"把一个数值作用于目标"）----
//
// ---- 目标：由**一个模块**按作用域给候选 ----
//
// 这一整段原来逐个效果写死（"伤害只能给别人""格挡可以给队友"…），那是因为候选是各效果自己
// 列的——于是同一族效果互相对不上（移除状态少三档、回复生命与造成伤害各少一档）。
// 现在候选由目标模块按**作用域**给，所以这里钉的是**作用域**这条规则本身：
//   造物轴七档 + 玩家宠物四档、玩家轴五档、作用于牌只有一档；同域的效果候选**逐字相同**。
//
// 注意用的 helper 是 Targets(...) 而不是上面的 Join(...)：那个吃的是界面 JSON 里的节点，
// 这里读的是目录里的原始条目（string[]）。
string Targets(EffectKind kind) => string.Join(",", EffectCatalog.SchemaOf(kind).targets);

const string CreatureTargets =
    "Self,Target,AllEnemies,RandomEnemy,AllAllies,RandomAlly,AllOtherAllies,RandomOtherAlly,All,"
    + "SelfPet,TargetPet,AllAlliesPet,RandomAllyPet,AllOtherAlliesPet,RandomOtherAllyPet";
const string PlayerTargets =
    "Self,Target,AllAllies,RandomPlayer,AllOtherAllies,RandomOtherAlly";

Check("造物轴：七档 + 其他友方两档 + 玩家宠物四档（自己 / 目标 / 所有敌人 / 随机敌人 / "
    + "所有友方 / 随机一名友方 / 所有其他友方 / 随机其他友方 / 所有人，外加宠物六档）",
    string.Join(",", TargetCatalog.Names(TargetScope.Creature)) == CreatureTargets,
    $"实际：{string.Join(",", TargetCatalog.Names(TargetScope.Creature))}");
Check("玩家轴：六档（敌人那三档给不出；含自己/不含自己各两条随机与全体，外加专有的"
    + "「随机一名玩家」；宠物那一组也不给——收 Player 的命令接不住宠物）",
    string.Join(",", TargetCatalog.Names(TargetScope.Player)) == PlayerTargets,
    $"实际：{string.Join(",", TargetCatalog.Names(TargetScope.Player))}");
Check("作用于牌：只有「自己」一档（谁受影响由选牌器决定）",
    string.Join(",", TargetCatalog.Names(TargetScope.Cards)) == "Self");

// ---- 每轴的**名字**：同一档在两轴上的读法不同，所以标签也必须分轴 ----
//
// "所有友方"在玩家轴上落到的是 CombatState.Players（玩家），所以读作"所有玩家"；
// "目标"在玩家轴上是选中的那名队友，读作"任意一名友方"。这条规则只写在
// TargetCatalog.LabelOf 一处，界面照它画下拉。
string Label(TargetScope scope, EffectTarget target) => TargetCatalog.LabelOf(scope, target);

Check("玩家轴：「所有友方」那一档读作「所有玩家」（那一轴落到的是玩家集合）",
    Label(TargetScope.Player, EffectTarget.AllAllies) == "所有玩家",
    $"实际：{Label(TargetScope.Player, EffectTarget.AllAllies)}");
Check("玩家轴：「目标」那一档读作「任意其他玩家」（卡牌目标是任意友方，游戏给它的候选"
    + "就是「除自己以外的玩家」）",
    Label(TargetScope.Player, EffectTarget.Target) == "任意其他玩家",
    $"实际：{Label(TargetScope.Player, EffectTarget.Target)}");
Check("玩家轴：「随机一名友方」那一档读作「随机其他玩家」（那一轴排除自己）",
    Label(TargetScope.Player, EffectTarget.RandomAlly) == "随机其他玩家",
    $"实际：{Label(TargetScope.Player, EffectTarget.RandomAlly)}");
Check("造物轴：同一档仍读作「所有友方」「目标」（那一轴落到的是生物）",
    Label(TargetScope.Creature, EffectTarget.AllAllies) == "所有友方"
    && Label(TargetScope.Creature, EffectTarget.Target) == "目标");
// "友方"与"其他友方"的分别是**含不含自己**（用户定的口径，也是游戏自己的分法：
// 原版"所有友方"含自己，"任意友方"的候选排除自己）。所以名字里带"其他"的那两档不含自己。
Check("「含自己」那一族：随机一名玩家 / 随机一名友方（造物轴）",
    Label(TargetScope.Player, EffectTarget.RandomPlayer) == "随机一名玩家"
    && Label(TargetScope.Creature, EffectTarget.RandomAlly) == "随机一名友方");
Check("「不含自己」那一族：其他友方按轴读（造物轴「其他友方」、玩家轴「其他玩家」）",
    Label(TargetScope.Creature, EffectTarget.AllOtherAllies) == "所有其他友方"
    && Label(TargetScope.Creature, EffectTarget.RandomOtherAlly) == "随机其他友方"
    && Label(TargetScope.Player, EffectTarget.AllOtherAllies) == "所有其他玩家"
    && Label(TargetScope.Player, EffectTarget.RandomOtherAlly) == "随机其他玩家");
Check("玩家轴上没有「随机一名友方」那一档（它与「随机一名玩家」同集，列两档只会让人挑不动）",
    !TargetCatalog.Options(TargetScope.Player).Contains(EffectTarget.RandomAlly)
    && TargetCatalog.Options(TargetScope.Player).Contains(EffectTarget.RandomOtherAlly));

// 标签数组与候选数组一一对应（界面对齐着用，错位就会张冠李戴）。
foreach (TargetScope scope in Enum.GetValues<TargetScope>())
{
    Check($"{scope} 轴：标签与候选一一对应",
        TargetCatalog.Labels(scope).Length == TargetCatalog.Names(scope).Length);
}

// ---- "此刻能不能用"那一层：要"打出时选定的目标"的档，得这张牌真给得出一个合用的对象 ----
//
// 这就是"效果按调用场合屏蔽非法目标"的落点：作用域说得出哪几档，卡牌目标说此刻哪几档成立。
// 界面**不显示**用不上的档（列出来才叫噪音），当前值恰好是它时给一句红字说明。
//
// 判据是**每档一份名单**（`PlayedTargetSources`：档名 → 接受的卡牌目标名），
// 界面读它、校验器也从它推判据——同一条规则两种用法。下面拿全枚举 × 全档逐个比对两者的域，
// 分叉的后果是"界面说能用、校验器说不行"。
foreach (TargetScope scope in Enum.GetValues<TargetScope>())
{
    foreach (EffectTarget band in TargetCatalog.Options(scope))
    {
        foreach (CardTargetType ct in Enum.GetValues<CardTargetType>())
        {
            bool fromMap = TargetCatalog.PlayedTargetSources(scope)
                .TryGetValue(band.ToString(), out string[]? need) && need.Contains(ct.ToString());

            // 不需要出牌目标的档：名单里没有它，判据恒为真。
            bool expected = !TargetCatalog.UsesPlayedTarget(band) || fromMap;

            Check($"{scope}/{band} × {ct}：界面读的名单与校验器的判据一致",
                TargetCatalog.AcceptsPlayedTarget(scope, band, ct) == expected);
        }
    }
}

Check("造物轴的「目标」要一个**生物**：任意敌人与任意友方都行，其余不行",
    TargetCatalog.AcceptsPlayedTarget(TargetScope.Creature, EffectTarget.Target, CardTargetType.AnyEnemy)
    && TargetCatalog.AcceptsPlayedTarget(TargetScope.Creature, EffectTarget.Target, CardTargetType.AnyAlly)
    && !TargetCatalog.AcceptsPlayedTarget(TargetScope.Creature, EffectTarget.Target, CardTargetType.AllEnemies)
    && !TargetCatalog.AcceptsPlayedTarget(TargetScope.Creature, EffectTarget.Target, CardTargetType.Self));
Check("「目标的奥斯提」要一名**玩家**：只有任意友方给得出（敌人没有宠物）",
    TargetCatalog.AcceptsPlayedTarget(TargetScope.Creature, EffectTarget.TargetPet, CardTargetType.AnyAlly)
    && !TargetCatalog.AcceptsPlayedTarget(TargetScope.Creature, EffectTarget.TargetPet, CardTargetType.AnyEnemy));
Check("玩家轴的「目标」也只认「任意友方」（那个才给玩家）",
    TargetCatalog.AcceptsPlayedTarget(TargetScope.Player, EffectTarget.Target, CardTargetType.AnyAlly)
    && !TargetCatalog.AcceptsPlayedTarget(TargetScope.Player, EffectTarget.Target, CardTargetType.AnyEnemy));
Check("作用于牌那一轴根本没有「要出牌目标」的那两档（候选里就没有它们）",
    !TargetCatalog.Options(TargetScope.Cards).Any(TargetCatalog.UsesPlayedTarget)
    && TargetCatalog.PlayedTargetSources(TargetScope.Cards).Count == 0);

// ---- 玩家宠物那一组：谁能用、哪几档 ----
//
// **判据是作用域**（造物轴 ⇒ 能用）：造物轴那九条命令收的都是 Creature，而宠物就是 Creature
// ——"理论上对奥斯提有效"与"属于造物轴"是同一件事。以前是每条效果各声明一遍，
// 结果漏了移除状态、两个最大生命、失去生命、灾厄判定（用户报的正是这个）；
// 现在由作用域推出，漏不了。
//
// 注意它**不是**"某一档 + 勾一个取其宠物"：宠物是**独立的一组目标档**
// （自己的 / 目标的 / 所有友方的 / 随机一名友方的奥斯提），所以"哪几档"这件事由
// `IsPetBand` 说，而不是由"原来的档 + 布尔"推。
Check("宠物档：造物轴九条**全部**都能用（伤害 / 格挡 / 回复 / 失去生命 / 两个最大生命 / "
    + "施加·移除状态 / 灾厄判定）",
    new[]
    {
        EffectKind.DealDamage, EffectKind.GainBlock, EffectKind.Heal, EffectKind.LoseHp,
        EffectKind.GainMaxHp, EffectKind.LoseMaxHp, EffectKind.ApplyPower,
        EffectKind.RemovePower, EffectKind.TriggerDoom,
    }.All(k => TargetCatalog.SupportsBand(EffectCatalog.ScopeOf(k), EffectTarget.SelfPet)),
    "它们都是造物轴，命令都收 Creature");
Check("宠物档：玩家轴一条都不能用（那些命令收 Player，给它一只宠物编译不过）",
    new[]
    {
        EffectKind.GainEnergy, EffectKind.GainStars, EffectKind.DrawCards, EffectKind.Forge,
        EffectKind.Summon, EffectKind.ChannelOrb, EffectKind.EvokeOrbs, EffectKind.AddOrbSlots,
    }.All(k => !EffectCatalog.SchemaOf(k).targets.Contains(nameof(EffectTarget.SelfPet))));
Check("宠物档：作用于牌那一轴与宠物无关",
    !EffectCatalog.SchemaOf(EffectKind.ExhaustCards).targets.Contains(nameof(EffectTarget.SelfPet)));

Check("宠物档：六档（自己的 / 目标的 / 所有友方的 / 随机一名友方的 / 所有其他玩家的 / 随机其他玩家的奥斯提）",
    TargetCatalog.Options(TargetScope.Creature).Where(TargetCatalog.IsPetBand)
        .Select(t => t.ToString())
        .SequenceEqual(new[]
        {
            nameof(EffectTarget.SelfPet), nameof(EffectTarget.TargetPet),
            nameof(EffectTarget.AllAlliesPet), nameof(EffectTarget.RandomAllyPet),
            nameof(EffectTarget.AllOtherAlliesPet), nameof(EffectTarget.RandomOtherAllyPet),
        }));
Check("宠物档：`目标` 与 `目标的奥斯提` 是要出牌目标的那两档（其余档不需要）",
    TargetCatalog.UsesPlayedTarget(EffectTarget.Target)
    && TargetCatalog.UsesPlayedTarget(EffectTarget.TargetPet)
    && !TargetCatalog.UsesPlayedTarget(EffectTarget.SelfPet)
    && !TargetCatalog.UsesPlayedTarget(EffectTarget.Self));
Check("友方＝玩家生物，不是 Allies（宠物不算友方；想作用到宠物上得选宠物档）",
    Label(TargetScope.Creature, EffectTarget.AllAllies) == "所有友方"
    && Label(TargetScope.Creature, EffectTarget.AllAlliesPet) == "所有友方的奥斯提");

// 作用于牌那一轴没有"支援"这类档：它的候选只有「自己」（谁受影响由选牌器决定）。
Check("作用于牌那一轴只有「自己」一档（没有友方/敌人那些）",
    TargetCatalog.Options(TargetScope.Cards).Count == 1);

// 造物轴那一族（数值 / 状态 / 灾厄判定）：候选逐字相同。
foreach (EffectKind kind in new[]
{
    EffectKind.DealDamage, EffectKind.GainBlock, EffectKind.Heal, EffectKind.LoseHp,
    EffectKind.GainMaxHp, EffectKind.LoseMaxHp, EffectKind.ApplyPower, EffectKind.RemovePower,
    EffectKind.TriggerDoom,
})
{
    Check($"{kind}：造物轴十一档齐全（与其它造物轴效果逐字相同）",
        Targets(kind) == CreatureTargets, $"实际：{Targets(kind)}");
}

// 玩家轴那一族（能量 / 星辰 / 球 / 抽牌 / 铸造 / 召唤）：命令收的是 Player。
foreach (EffectKind kind in new[]
{
    EffectKind.GainEnergy, EffectKind.GainStars, EffectKind.AddOrbSlots, EffectKind.ChannelOrb,
    EffectKind.EvokeOrbs, EffectKind.DrawCards, EffectKind.Forge, EffectKind.Summon,
})
{
    Check($"{kind}：玩家轴五档（与其它玩家轴效果逐字相同）",
        Targets(kind) == PlayerTargets, $"实际：{Targets(kind)}");
}

Check("消耗牌：作用于牌，所以只有「自己」",
    Targets(EffectKind.ExhaustCards) == "Self", $"实际：{Targets(EffectKind.ExhaustCards)}");

// 作用域本身也是这套判据的一部分："这条效果属于哪一轴"只有一处答案。
Check("作用域：伤害在造物轴、抽牌在玩家轴、消耗牌在牌那一轴",
    EffectCatalog.ScopeOf(EffectKind.DealDamage) == TargetScope.Creature
    && EffectCatalog.ScopeOf(EffectKind.DrawCards) == TargetScope.Player
    && EffectCatalog.ScopeOf(EffectKind.ExhaustCards) == TargetScope.Cards);

// ---- 临时牌集合（Modules/CardSets）：谁能存、谁能取、ID 怎么算 ----
//
// 这几条是"键能不能对上"的判据本身（键 = 地址.定义它的效果编号.集合ID）。
// 读写两侧靠 DefinitionNumber 对上号，所以它必须按**原始效果列表**数，与界面上「效果 N」一致。
Check("集合：抽牌能存（抽到的那几张正是最典型的用处），但不能取（「抽集合里的牌」不成立）",
    CardSetCatalog.Stores(EffectKind.DrawCards) && !CardSetCatalog.Reads(EffectKind.DrawCards));
Check("集合：加入指定卡牌能存（生成的每一张都留着），但不能取",
    CardSetCatalog.Stores(EffectKind.AddNamedCardToPile)
    && !CardSetCatalog.Reads(EffectKind.AddNamedCardToPile));
Check("集合：会挑牌的那几条既能存也能取",
    new[]
    {
        EffectKind.ExhaustCards, EffectKind.CopyCardsToPile, EffectKind.DiscardCardsInPile,
        EffectKind.EnchantCardsInPile, EffectKind.ModifyCardCost, EffectKind.SetCardsFree,
        EffectKind.UpgradeCardsInPile, EffectKind.TransformCardsInPile, EffectKind.MoveCardsBetweenPiles,
        EffectKind.AddReplay, EffectKind.BoostCardDamage,
    }.All(k => CardSetCatalog.Stores(k) && CardSetCatalog.Reads(k)));
Check("集合：不产牌的效果既不能存也不能取（伤害 / 能量那类没东西可存）",
    !CardSetCatalog.Stores(EffectKind.DealDamage) && !CardSetCatalog.Reads(EffectKind.DealDamage)
    && !CardSetCatalog.Stores(EffectKind.GainEnergy) && !CardSetCatalog.Reads(EffectKind.GainEnergy));
Check("集合：自动打出既不能取也不能存（它的出码直接从牌堆取顶牌，不看候选列表——给了就是静默不生效）",
    !CardSetCatalog.Stores(EffectKind.AutoPlay) && !CardSetCatalog.Reads(EffectKind.AutoPlay));

Check("集合：ID 只接受字母数字下划线（键用点分段，带点的 ID 会让按前缀清空误伤别的地址）",
    CardSetCatalog.IsValidId("Drawn") && CardSetCatalog.IsValidId("A_1")
    && !CardSetCatalog.IsValidId("A.B") && !CardSetCatalog.IsValidId("A B")
    && !CardSetCatalog.IsValidId("") && !CardSetCatalog.IsValidId(null));

// 键的第二段：定义它的那条效果的编号（1 基，按原始列表数）。
{
    var effects = new List<EffectDef>
    {
        new() { Kind = EffectKind.GainBlock, Target = EffectTarget.Self, Amount = 1 },
        new() { Kind = EffectKind.DrawCards, Target = EffectTarget.Self, Amount = 1, SetId = "Drawn" },
        new()
        {
            Kind = EffectKind.ModifyCardCost, Target = EffectTarget.Self, Amount = -1,
            Pick = new CardPick { Source = PickSource.Set, SetId = "Drawn", Mode = PickMode.All },
        },
    };

    Check("集合：编号按原始列表数（第 2 条定义就读 2）——与界面上「效果 2」一致",
        CardSetCatalog.DefinitionNumber(effects, "Drawn") == 2
        && CardSetCatalog.DefinitionNumber(effects, "Nope") == 0);

    IReadOnlyList<(string Id, int Number)> defined = CardSetCatalog.Definitions(effects);

    Check("集合：列出已定义的集合（ID + 编号），界面照它做下拉建议",
        defined.Count == 1 && defined[0].Id == "Drawn" && defined[0].Number == 2);
}

// 卡牌目标那两档新值：只有"任意敌人 / 任意友方"会给出打出时选定的目标；
// 玩家轴的"目标"那一档还要求它给的是**玩家**。
Check("卡牌目标：任意友方 / 所有友方在候选里",
    Enum.GetNames<CardTargetType>().Contains("AnyAlly")
    && Enum.GetNames<CardTargetType>().Contains("AllAllies"));
Check("卡牌目标：只有任意敌人与任意友方会选出目标",
    TargetCatalog.ProvidesPlayedTarget(CardTargetType.AnyEnemy)
    && TargetCatalog.ProvidesPlayedTarget(CardTargetType.AnyAlly)
    && !TargetCatalog.ProvidesPlayedTarget(CardTargetType.AllEnemies)
    && !TargetCatalog.ProvidesPlayedTarget(CardTargetType.RandomEnemy)
    && !TargetCatalog.ProvidesPlayedTarget(CardTargetType.AllAllies)
    && !TargetCatalog.ProvidesPlayedTarget(CardTargetType.Self)
    && !TargetCatalog.ProvidesPlayedTarget(CardTargetType.None));
Check("卡牌目标：只有任意友方给的是玩家（玩家轴的「目标」要它）",
    TargetCatalog.GivesPlayerTarget(CardTargetType.AnyAlly)
    && !TargetCatalog.GivesPlayerTarget(CardTargetType.AnyEnemy));

Check("伤害：要填数值", FieldEquals(EffectCatalog.SchemaOf(EffectKind.DealDamage), "amount", true));
Check("失去生命：标签是「失去生命」（不是「伤害」——措辞上要分得开）",
    FieldEquals(EffectCatalog.SchemaOf(EffectKind.LoseHp), "labelZh", "失去生命"));

// ---- 批 3：四种各成一类 ----

// 灾厄判定**没有数值**：它判的是目标身上已有的灾厄层数，卡片上不该出现"数值"那一组。
Check("灾厄判定：不要数值（判的是已有的灾厄层数）",
    !FieldEquals(EffectCatalog.SchemaOf(EffectKind.TriggerDoom), "amount", true));
Check("抽牌 / 铸造 / 召唤：都要数值",
    FieldEquals(EffectCatalog.SchemaOf(EffectKind.DrawCards), "amount", true)
    && FieldEquals(EffectCatalog.SchemaOf(EffectKind.Forge), "amount", true)
    && FieldEquals(EffectCatalog.SchemaOf(EffectKind.Summon), "amount", true));
Check("召唤：标签是「召唤」（不是「召唤物」——与卡面句子同一个词）",
    FieldEquals(EffectCatalog.SchemaOf(EffectKind.Summon), "labelZh", "召唤"));

// ---- 批 4：状态这一族 ----
Check("施加状态：要选是哪个状态",
    FieldEquals(EffectCatalog.SchemaOf(EffectKind.ApplyPower), "needsPower", true));
Check("施加状态：没有「移除方式」那两格",
    !FieldEquals(EffectCatalog.SchemaOf(EffectKind.ApplyPower), "hasPowerRemoval", true));

// 移除状态多出两个下拉；数值那一组**也要可用**——「部分移除」的数值就是「减几层」。
// （整层移除时界面按 Removal 把那几格隐去，所以这里 amount 为真不代表界面上一直显示。）
Check("移除状态：多出「移除方式 / 移除哪一类」两格",
    FieldEquals(EffectCatalog.SchemaOf(EffectKind.RemovePower), "hasPowerRemoval", true));
Check("移除状态：要选是哪个状态（选正面/负面时界面会隐去那一格）",
    FieldEquals(EffectCatalog.SchemaOf(EffectKind.RemovePower), "needsPower", true));
Check("移除状态：数值那一组可用（部分移除要用它）",
    FieldEquals(EffectCatalog.SchemaOf(EffectKind.RemovePower), "amount", true));

// 两个新下拉的候选由后端按枚举下发（界面不自己维护枚举名清单）。
JsonNode schemaRoot = JsonNode.Parse(JsonSerializer.Serialize(EditorSchema.Build()))!;

Check("移除方式的候选是「整个移除 / 只减层数」两档，顺序不变",
    Join(schemaRoot["powerRemovals"]) == "All,Partial",
    $"实际：{Join(schemaRoot["powerRemovals"])}");
Check("移除哪一类的候选是「按 id / 正面 / 负面」三档，顺序不变",
    Join(schemaRoot["powerSelections"]) == "PowerId,Buff,Debuff",
    $"实际：{Join(schemaRoot["powerSelections"])}");
Check("界面 schema 的目标兜底表就是造物轴那套（七档 + 宠物四档）",
    Join(schemaRoot["targets"]) == CreatureTargets,
    $"实际：{Join(schemaRoot["targets"])}");
Check("界面 schema 列出了宠物那六档（界面照它把这几档归到一个 optgroup）",
    Join(schemaRoot["petTargets"]) == "SelfPet,TargetPet,AllAlliesPet,RandomAllyPet,AllOtherAlliesPet,RandomOtherAllyPet",
    $"实际：{Join(schemaRoot["petTargets"])}");

// durations 归一成空数组（不是 null）——**每条已搬的都查**：漏归一的那种会让序列化出 null，
// 而界面按数组用（`[]` 与 `null` 在那边是两回事）。
foreach (EffectKind kind in Enum.GetValues<EffectKind>().Where(EffectCatalog.IsMigrated))
{
    Check($"{kind}：durations 归一成空数组（不是 null）",
        EffectCatalog.SchemaOf(kind).durations is not null,
        "durations 是 null —— 序列化会变成 null 而不是 []，与界面期望不符");
}

// ---------------------------------------------------------------------------
// 目录自检：搬进来的种类必须与枚举对得上，别把自己标错
// ---------------------------------------------------------------------------
Console.WriteLine();
Console.WriteLine("== 目录自检 ==");

foreach (EffectKind kind in new[]
{
    EffectKind.GainEnergy, EffectKind.GainStars, EffectKind.AddOrbSlots,
    EffectKind.ExhaustCards, EffectKind.ChannelOrb, EffectKind.EvokeOrbs,
    EffectKind.DealDamage, EffectKind.GainBlock, EffectKind.Heal,
    EffectKind.LoseHp, EffectKind.GainMaxHp, EffectKind.LoseMaxHp,
    EffectKind.DrawCards, EffectKind.Forge, EffectKind.Summon, EffectKind.TriggerDoom,
    EffectKind.ApplyPower, EffectKind.RemovePower,
    EffectKind.AddNamedCardToPile, EffectKind.DiscardCardsInPile, EffectKind.AutoPlay,
    EffectKind.CopyCardsToPile, EffectKind.ModifyCardCost, EffectKind.SetCardsFree,
    EffectKind.UpgradeCardsInPile, EffectKind.MoveCardsBetweenPiles, EffectKind.TransformCardsInPile,
    EffectKind.EnchantCardsInPile, EffectKind.BoostCardDamage, EffectKind.AddReplay,
})
{
    Check($"{kind}：已进目录", EffectCatalog.IsMigrated(kind));
    Check($"{kind}：SchemaOf 的 kind 与查表键一致",
        EffectCatalog.SchemaOf(kind).kind == kind.ToString());
}

// 加入指定卡牌：schema 要把"生成方式"声明出来（界面据此画那个下拉）。
Check("AddNamedCardToPile：声明了 hasAddMode",
    EffectCatalog.SchemaOf(EffectKind.AddNamedCardToPile).hasAddMode);
Check("AddNamedCardToPile：不挑牌（它不是选牌器，集合是它的写侧）",
    EffectCatalog.SchemaOf(EffectKind.AddNamedCardToPile).pick is null);

// 30 种全部搬完：目录必须覆盖整个枚举，一处都不能少。
Check("全部 30 种都已进目录",
    Enum.GetValues<EffectKind>().All(EffectCatalog.IsMigrated));

// 界面真正拿到的那份 effects **不得有重复**：同一种类出现两次，
// 下拉里就会有两个同名选项（实测踩过：搬迁时旧条目漏删、新条目又补了一条）。
{
    JsonNode root = JsonNode.Parse(JsonSerializer.Serialize(EditorSchema.Build()))!;
    string[] kinds = root["effects"]!.AsArray().Select(e => e!["kind"]!.GetValue<string>()).ToArray();

    Check("界面 effects 列表没有重复种类", kinds.Length == kinds.Distinct().Count(),
        $"实际 {kinds.Length} 条、去重后 {kinds.Distinct().Count()} 种");
    Check("界面 effects 列表覆盖全部 30 种", kinds.Distinct().Count() == 30,
        $"实际覆盖 {kinds.Distinct().Count()} 种");

    // 启用条件的六种比较方式（界面画"比较方式"下拉用的那张表）。
    Check("界面下发条件的六种比较方式",
        (root["conditionComparisons"]?.AsArray().Count ?? 0) == 6,
        $"实际 {(root["conditionComparisons"]?.AsArray().Count ?? 0)} 种");
}

Console.WriteLine();
Console.WriteLine($"通过 {passed} 项，失败 {failed} 项。");
Console.WriteLine(failed == 0 ? "RESULT: OK" : "RESULT: FAILED");
return failed == 0 ? 0 : 1;
