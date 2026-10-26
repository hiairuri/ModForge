using System.Globalization;
using System.Xml.Linq;
using ModForge.Core.Model;
using ModForge.Core.Modules.CardPicking;

namespace ModForge.Core.Modules.Files;

/// <summary>
/// 一条效果的 XML 读写。卡的、共享效果的、状态触发的都是用这一份——
/// 三处存的是同一个 <see cref="EffectDef"/>，各写一遍迟早会各写漏一点。
///
/// 形状（也是"一条效果长什么样"的说明，改字段先看这里）：
///
/// <code>
/// &lt;li&gt;                       &lt;!-- 单独一条效果时这个外层元素叫 &lt;effect&gt; --&gt;
///   &lt;kind&gt;DealDamage&lt;/kind&gt;
///   &lt;target&gt;AnyEnemy&lt;/target&gt;
///   &lt;base&gt;                    &lt;!-- 数值三件套：数值 = 基础值 + 变量 × 变量系数 --&gt;
///     &lt;count&gt;6&lt;/count&gt;
///     &lt;source&gt;HandCount&lt;/source&gt;
///     &lt;coefficient&gt;2&lt;/coefficient&gt;
///   &lt;/base&gt;
///   &lt;repeat&gt;                  &lt;!-- 次数三件套，同样是这三个 --&gt;
///     &lt;count&gt;1&lt;/count&gt;
///     &lt;source&gt;Stars&lt;/source&gt;
///     &lt;coefficient&gt;0.5&lt;/coefficient&gt;
///   &lt;/repeat&gt;
///   &lt;upgrade&gt;
///     &lt;li&gt;&lt;level&gt;1&lt;/level&gt;…&lt;/li&gt;   &lt;!-- 有 level：等级表，绝对取值，缺项沿用上一级 --&gt;
///     &lt;li&gt;…&lt;/li&gt;                     &lt;!-- 无 level：增量，每级加多少；只能有一个 --&gt;
///   &lt;/upgrade&gt;
///   &lt;pile&gt;Hand&lt;/pile&gt;           &lt;!-- 这条效果自己的参数 --&gt;
/// &lt;/li&gt;
/// </code>
///
/// 几条贯穿全表的规矩：
///
///   **三件套就是三个数**：基础值、变量（<c>&lt;source&gt;</c>）、变量系数（<c>&lt;coefficient&gt;</c>）。
///   数值那一块与次数那一块**各自**是一套，所以两块都有一个 <c>&lt;count&gt;</c>——
///   路径不同（<c>base/count</c> 与 <c>repeat/count</c>），含义由所在的那一块决定。
///   没选变量就是固定值，这时系数没有意义、不写。
///
///   **与默认相同的项不写**（见 <see cref="XmlIo.SetIf"/>）：整块都是默认时那一块整块不出现
///   （"造成 6 点伤害"只有 <c>&lt;base&gt;&lt;count&gt;6&lt;/count&gt;&lt;/base&gt;</c>），所以一条效果通常两三行。
///   反过来，删掉哪一行/哪一块就等于把那一项恢复默认。
///
///   **同一个元素名在两种地方含义不同**：三件套写在效果自己身上时"缺项即默认"，
///   写在等级表里时"缺项即沿用上一级"。这是等级表能表达"只改系数"的前提。
///
///   **空值用 <c>NULL</c>，无变量用 <c>None</c>**：两者不是一个概念。
///   <c>NULL</c> 是"没有值"的通用兜底——读不动的值一律当它，在等级表里表示"这一项沿用上一级"；
///   <c>None</c> 是枚举 <see cref="ValueSourceKind.None"/>，明确表示"这个数值没有变量、是固定值"。
///   所以等级表里 <c>&lt;source&gt;None&lt;/source&gt;</c> 是"这一级不要变量了"，
///   而不写 <c>&lt;source&gt;</c>（或写 <c>NULL</c>）是"这一级不动变量"。
///
///   **读不动只降级、不中断**：拼错的元素名、非法的枚举值、重复的块，都会记一条带文件与位置的
///   问题（见 <see cref="ReadLog"/>），同时退回兜底值把文件读完。不能因为一处写错就让整份工程
///   打不开——但也不能一声不吭，那样"我明明写了却没生效"会变成一场猜谜。
/// </summary>
public static class EffectXml
{
    private static readonly EffectDef Default = new();
    private static readonly ValueSource DefaultSource = new();

    /// <summary>一条效果的子元素名（三件套那一层之外的那些）。</summary>
    private static readonly string[] KnownElements =
    {
        "shared", "kind", "trigger", "target", "ofPet", "setId", "base", "repeat", "energyAmount", "starAmount",
        "upgrade", "pick", "targetPile", "placement", "cardId", "upgraded",
        "addMode", "candidates", "poolFilter",
        "condition",
        "powerId", "removal", "selection",
        "orb", "evokeTarget", "orbKinds", "enchantmentId", "cardFilter", "pileScope",
        "costDuration", "useCounter", "counterTarget",
        "counterReset", "transformInto",
    };

    /// <summary>&lt;pick&gt; 接受的全部子元素。</summary>
    private static readonly string[] PickElements =
    {
        "source", "set", "piles", "types", "rarities", "cardIds", "tags", "keywords",
        "energyComparison", "starComparison", "mode", "pickCount",
    };

    /// <summary>升级里一个 <c>&lt;li&gt;</c> 与等级表一项接受的子元素。</summary>
    private static readonly string[] UpgradeBlockNames =
    {
        "level", "base", "repeat", "energyAmount", "starAmount",
    };

    /// <summary>
    /// 写一条效果。
    ///
    /// <paramref name="rootName"/> 是外面那层元素的名字：卡的 `<effects>` 里是 `<li>`，
    /// 而状态触发与共享效果里这条效果本身就是内容，叫 `<effect>`。
    /// 里面指名种类的那一格固定叫 `<kind>`——写成 `<effect>` 的话，那两处会变成
    /// `<effect><effect>` 这种自己套自己的写法。
    /// </summary>
    public static XElement ToXml(EffectDef effect, string rootName = "li")
    {
        // 引用共享效果：只写这个 id 就够说明"这条效果是那个共享效果"，但下面那些字段
        // 仍然照写——引用行允许自带升级增量，而其余字段界面也还留着（它们是插入引用时
        // 一起建的，界面上不显示、生成时以共享定义为准）。写全是为了**存进去什么、
        // 读出来还是什么**，不是因为这些字段会生效。
        var element = new XElement(rootName);

        XmlIo.SetIf(element, "shared", effect.SharedEffectId, Default.SharedEffectId);

        element.Add(new XElement("kind", effect.Kind.ToString()));
        element.Add(new XElement("target", effect.Target.ToString()));

        // 把这条效果产生/选出的那几张牌存进临时牌集合（名字进运行时那个键）。
        XmlIo.SetIf(element, "setId", effect.SetId, Default.SetId);

        XmlIo.SetIf(element, "trigger", effect.Trigger, Default.Trigger);
        WriteValueBlocks(element, effect);
        WriteUpgrade(element, effect);
        WriteCondition(element, effect);

        // 这条效果自己的参数：与默认相同就不写。
        XmlIo.SetIf(element, "targetPile", effect.TargetPile, Default.TargetPile);
        XmlIo.SetIf(element, "placement", effect.Placement, Default.Placement);
        XmlIo.SetIf(element, "cardId", effect.CardId, Default.CardId);
        XmlIo.SetIf(element, "upgraded", effect.Upgraded, Default.Upgraded);

        // "加入指定卡牌"的生成方式（只在这一种效果上出现；默认 = 指定牌那一路）。
        XmlIo.SetIf(element, "addMode", effect.AddMode, Default.AddMode);
        XmlIo.SetIf(element, "candidates", effect.Candidates, Default.Candidates);
        if (effect.PoolFilter.HasFilter())
        {
            element.Add(FilterToXml(effect.PoolFilter, "poolFilter"));
        }

        XmlIo.SetIf(element, "powerId", effect.PowerId, Default.PowerId);

        // 移除状态的两个选项：与其它参数一样，与默认相同就不写
        //（默认＝"整个移除"+"按 id 指定"，也就是这条效果从前的行为）。
        XmlIo.SetIf(element, "removal", effect.Removal, Default.Removal);
        XmlIo.SetIf(element, "selection", effect.Selection, Default.Selection);
        XmlIo.SetIf(element, "orb", effect.Orb, Default.Orb);
        XmlIo.SetIf(element, "evokeTarget", effect.EvokeTarget, Default.EvokeTarget);
        if (effect.OrbKinds.Count > 0)
        {
            XmlIo.SetList(element, "orbKinds", effect.OrbKinds.Select(k => k.ToString()));
        }

        XmlIo.SetIf(element, "enchantmentId", effect.EnchantmentId, Default.EnchantmentId);
        XmlIo.SetIf(element, "cardFilter", effect.CardFilter, Default.CardFilter);
        XmlIo.SetIf(element, "pileScope", effect.PileScope, Default.PileScope);
        XmlIo.SetIf(element, "costDuration", effect.CostDuration, Default.CostDuration);
        XmlIo.SetIf(element, "useCounter", effect.UseCounter, Default.UseCounter);
        XmlIo.SetIf(element, "counterTarget", effect.CounterTarget, Default.CounterTarget);
        XmlIo.SetIf(element, "counterReset", effect.CounterReset, Default.CounterReset);
        XmlIo.SetIf(element, "transformInto", effect.TransformInto, Default.TransformInto);

        WritePick(element, effect);

        return element;
    }

    /// <summary>
    /// 写"选哪些牌"（选牌器的筛选与取法）。整块是默认时**不写**——大多数效果不挑牌，
    /// 那几条挑牌的才带这一块。
    ///
    /// <code>
    /// &lt;pick&gt;
    ///   &lt;source&gt;Piles&lt;/source&gt;              &lt;!-- 此牌 / 牌堆 / 卡组 --&gt;
    ///   &lt;piles&gt;&lt;li&gt;Hand&lt;/li&gt;&lt;/piles&gt;        &lt;!-- 在哪些牌堆里找（可多选；空 = 全部战斗牌堆）--&gt;
    ///   &lt;types&gt;&lt;li&gt;Attack&lt;/li&gt;&lt;/types&gt;      &lt;!-- 筛选：类型（可多选）--&gt;
    ///   &lt;rarities&gt;…&lt;/rarities&gt;               &lt;!-- 筛选：稀有度 --&gt;
    ///   &lt;cardIds&gt;…&lt;/cardIds&gt;                 &lt;!-- 筛选：牌 ID --&gt;
    ///   &lt;tags&gt;…&lt;/tags&gt;                       &lt;!-- 筛选：标签 --&gt;
    ///   &lt;keywords&gt;…&lt;/keywords&gt;               &lt;!-- 筛选：关键词 --&gt;
    ///   &lt;energyComparison&gt;GreaterThan&lt;/energyComparison&gt;  &lt;!-- 筛选：能量消耗怎么比 --&gt;
    ///   &lt;starComparison&gt;AtLeast&lt;/starComparison&gt;          &lt;!-- 筛选：星辰消耗怎么比 --&gt;
    ///   &lt;mode&gt;Choose&lt;/mode&gt;                  &lt;!-- 取法：全部 / 选择 / 随机 / 至多 --&gt;
    ///   &lt;pickCount&gt;                           &lt;!-- 张数：只给"张数不等于效果数值"的效果 --&gt;
    ///     &lt;count&gt;2&lt;/count&gt;
    ///     &lt;source&gt;HandCount&lt;/source&gt;
    ///     &lt;coefficient&gt;0.5&lt;/coefficient&gt;
    ///   &lt;/pickCount&gt;
    /// &lt;/pick&gt;
    /// </code>
    ///
    /// **多个筛选维度可以同时生效（与），维度内部的多个取值是（或）。**
    /// 费用维度只写**运算符**——阈值是效果自己的数值，写在效果级的
    /// <c>&lt;energyAmount&gt;</c> / <c>&lt;starAmount&gt;</c> 里（与 &lt;base&gt; 并列，
    /// 见 <see cref="WriteValueBlocks"/>）。一个管"怎么比"、一个管"比什么数"。
    ///
    /// **张数由谁给，看效果自己**（<c>CardPickPolicy.CountFromEffect</c>）：
    /// 消耗牌的 base 值就是"消耗几张"，所以它的张数写在效果自己的 <c>&lt;base&gt;</c> 里，
    /// 这里**不写 &lt;pickCount&gt;**（整块与 base 同名同义就重复了）；其余效果的数值另有含义
    /// （复制几份、附几层、打几次…），挑几张只能写在这里。
    ///
    /// 写法与效果主体一致：<c>&lt;source&gt;</c> 与 <c>&lt;mode&gt;</c> 在非默认时**总是写出来**
    /// （一眼看出这条效果挑的什么），其余按"与默认不同才写"。
    /// </summary>
    private static void WritePick(XElement parent, EffectDef effect)
    {
        CardPick pick = effect.Pick;

        if (IsDefaultPick(pick))
        {
            return;
        }

        var element = new XElement("pick",
            new XElement("source", pick.Source.ToString()),
            new XElement("mode", pick.Mode.ToString()));

        // 来源是临时牌集合时它读哪个集合（<see cref="PickSource.Set"/>）。
        // 与其它可选字段一样：没填就不写。
        if (!string.IsNullOrWhiteSpace(pick.SetId))
        {
            element.Add(new XElement("set", pick.SetId));
        }

        var piles = new XElement("piles");

        foreach (PileKind pile in pick.Piles)
        {
            piles.Add(new XElement("li", pile.ToString()));
        }

        element.Add(piles);

        WriteList(element, "types", pick.Types.Select(t => t.ToString()));
        WriteList(element, "rarities", pick.Rarities.Select(r => r.ToString()));
        WriteList(element, "cardIds", pick.CardIds);
        WriteList(element, "tags", pick.Tags.Select(t => t.ToString()));
        WriteList(element, "keywords", pick.Keywords.Select(k => k.ToString()));
        XmlIo.SetIf(element, "energyComparison", pick.EnergyComparison, CostComparison.None);
        XmlIo.SetIf(element, "starComparison", pick.StarComparison, CostComparison.None);
        WritePickCount(element, pick, effect.Kind);

        parent.Add(element);
    }

    /// <summary>
    /// 流程十：挑几张（三件套，与数值那两块同名同义）。
    ///
    /// 三条不写的条件，各有各的理由：
    ///   张数是效果自己的数值（消耗牌）——它写在 <c>&lt;base&gt;</c> 里，这里再写一份就是两个真相源；
    ///   流程九 = 全部 —— 没有"挑几张"可言（整个牌堆都动）；
    ///   与默认相同（1 张、没有变量）—— 那本来就是默认。
    /// </summary>
    private static void WritePickCount(XElement parent, CardPick pick, EffectKind kind)
    {
        if (CardPickPolicies.For(kind).CountFromEffect || pick.Mode == PickMode.All)
        {
            return;
        }

        if (pick.Count.Base == 1m && !pick.Count.IsCalculated)
        {
            return;
        }

        var count = new XElement("pickCount");

        XmlIo.SetIf(count, "count", pick.Count.Base, 1m);

        if (pick.Count.Variable.IsUsed)
        {
            WriteSource(count, pick.Count.Variable);
            XmlIo.SetIf(count, "coefficient", pick.Count.Coefficient, 1m);
        }

        parent.Add(count);
    }

    /// <summary>写一串取值；**空的列表不写**（一个元素都没有的 &lt;types /&gt; 是纯噪音）。</summary>
    private static void WriteList(XElement parent, string name, IEnumerable<string> values)
    {
        List<string> items = values.ToList();

        if (items.Count > 0)
        {
            XmlIo.SetList(parent, name, items);
        }
    }

    /// <summary>
    /// 这块选牌配置是不是"什么都没动"。
    ///
    /// 默认值必须与 <see cref="CardPick"/> 的默认值一字不差——两处只要有一处对不上，
    /// "不写 &lt;pick&gt;"读回来就会变成另一套配置。牌堆列表的默认是**空**
    /// （= 全部战斗牌堆），早期版本按"手牌"当默认，那会让"默认的 1 张手牌"读回来变成
    /// "从所有牌堆里挑 1 张"。
    /// </summary>
    private static bool IsDefaultPick(CardPick pick) =>
        pick.Source == PickSource.Piles &&
        pick.Piles.Count == 0 &&
        pick.Mode == PickMode.Choose &&
        pick.Count.Base == 1m && !pick.Count.IsCalculated &&
        !pick.HasFilter();

    /// <summary>
    /// 读一条效果。
    ///
    /// 外层元素的名字不挑：卡的 `<effects>` 里是 `<li>`，状态触发与共享效果里是 `<effect>`。
    /// 认错了也只是"读不到内容"，而那属于文件结构问题，调用方已经先看过一层。
    /// </summary>
    public static EffectDef FromXml(XElement element, ReadLog log)
    {
        XmlIo.RejectUnknown(log, element, KnownElements);

        if (XmlIo.Str(element, "kind") is null)
        {
            // 没有 <kind> 就不知道这条效果要做什么——其余字段（牌堆、状态、选牌…）全都得按种类
            // 解释，没有它连该读哪些都是未知的。这是"缺一个字段"里唯一无法兜底的一种。
            log.Fatal("缺 <kind>：不知道这是什么效果，这一条已跳过。");
            return new EffectDef();
        }

        var effect = new EffectDef
        {
            SharedEffectId = XmlIo.Str(element, "shared"),
            Kind = XmlIo.EnumValue(log, element, "kind", Default.Kind),
            Trigger = XmlIo.EnumValue(log, element, "trigger", Default.Trigger),
            Target = XmlIo.EnumValue(log, element, "target", Default.Target),
            SetId = XmlIo.Str(element, "setId"),
            TargetPile = XmlIo.EnumValue(log, element, "targetPile", Default.TargetPile),
            Placement = XmlIo.EnumValue(log, element, "placement", Default.Placement),
            CardId = XmlIo.Str(element, "cardId"),
            Upgraded = XmlIo.Bool(log, element, "upgraded", Default.Upgraded),
            AddMode = XmlIo.EnumValue(log, element, "addMode", Default.AddMode),
            Candidates = XmlIo.Int(log, element, "candidates", Default.Candidates),
            PoolFilter = ReadFilter(log, element.Element("poolFilter")),
            PowerId = XmlIo.Str(element, "powerId"),
            Removal = XmlIo.EnumValue(log, element, "removal", Default.Removal),
            Selection = XmlIo.EnumValue(log, element, "selection", Default.Selection),
            Orb = XmlIo.EnumValue(log, element, "orb", Default.Orb),
            EvokeTarget = XmlIo.EnumValue(log, element, "evokeTarget", Default.EvokeTarget),
            EnchantmentId = XmlIo.Str(element, "enchantmentId"),
            CardFilter = XmlIo.EnumValue(log, element, "cardFilter", Default.CardFilter),
            PileScope = XmlIo.EnumValue(log, element, "pileScope", Default.PileScope),
            CostDuration = XmlIo.EnumValue(log, element, "costDuration", Default.CostDuration),
            UseCounter = XmlIo.Bool(log, element, "useCounter", Default.UseCounter),
            CounterTarget = XmlIo.Int(log, element, "counterTarget", Default.CounterTarget),
            CounterReset = XmlIo.EnumValue(log, element, "counterReset", Default.CounterReset),
            TransformInto = XmlIo.EnumValue(log, element, "transformInto", Default.TransformInto),
        };

        foreach (string kind in XmlIo.List(element, "orbKinds"))
        {
            using (log.Scope("orbKinds"))
            {
                if (System.Enum.TryParse(kind, ignoreCase: true, out OrbKind parsed) &&
                    System.Enum.IsDefined(parsed))
                {
                    effect.OrbKinds.Add(parsed);
                }
                else
                {
                    log.Error(
                        $"\"{kind}\" 不是合法的球种，已忽略。合法值：" +
                        string.Join(" / ", System.Enum.GetNames<OrbKind>()));
                }
            }
        }

        ReadPick(element, effect, log);
        ReadValueBlocks(element, effect, log);
        ReadUpgrade(element, effect, log);
        ReadCondition(element, effect, log);
        WarnCostThresholds(effect, log);

        return effect;
    }

    /// <summary>
    /// 读一条效果；**这一条根本读不出来**时返回 null（并在 <paramref name="log"/> 里留下记录）。
    ///
    /// 与 <see cref="FromXml"/> 分工不同：那个只负责"读出来的东西尽量对"，这个负责"读不出来时
    /// 别让调用方把一条空效果收进卡里"——一条空效果会生成一段什么都不做的代码，
    /// 比明确少一条更难查。
    /// </summary>
    public static EffectDef? TryFromXml(XElement element, ReadLog log)
    {
        int before = log.Issues.Count;
        EffectDef effect = FromXml(element, log);

        return log.Issues.Skip(before).Any(i => i.Fatal) ? null : effect;
    }

    /* ------------------------------------------------------------------ 数值块（四个角色） */

    /// <summary>
    /// 写四个数值角色的块：数值 <c>&lt;base&gt;</c>、次数 <c>&lt;repeat&gt;</c>、
    /// 能量阈值 <c>&lt;energyAmount&gt;</c>、星辰阈值 <c>&lt;starAmount&gt;</c>
    /// （见 <see cref="ValueRole"/>）。
    ///
    /// 规则逐角色共用一套：**整块都是默认时那一块不写**；写了就至少写出 <c>&lt;count&gt;</c>
    /// （一眼看出这一块给了什么数），来源与系数按"与默认不同才写"。
    /// </summary>
    private static void WriteValueBlocks(XElement parent, EffectDef effect)
    {
        foreach (ValueRole role in EffectUpgrade.AllRoles)
        {
            WriteValueBlock(parent, effect, role);
        }
    }

    private static void WriteValueBlock(XElement parent, EffectDef effect, ValueRole role)
    {
        int amount = effect.AmountOf(role);
        int fallback = role == ValueRole.Repeat ? Default.Repeat : 0;
        ValueSource source = effect.SourceOf(role);

        bool calculated = source.IsUsed;
        bool hasAmount = amount != fallback;

        // 次数那一块多一条规矩：**计算型时把 <count> 也写出来**（哪怕它恰好等于默认的 1）——
        // 不写的话读回来是"固定 1 次"，而原意是"1 + 次数变量"。
        bool alwaysWriteCount = role == ValueRole.Repeat && calculated;

        if (!hasAmount && !calculated && !alwaysWriteCount)
        {
            return;
        }

        var element = new XElement(BlockName(role));

        if (alwaysWriteCount)
        {
            XmlIo.Set(element, "count", amount);
        }
        else
        {
            XmlIo.SetIf(element, "count", amount, fallback);
        }

        if (calculated)
        {
            WriteSource(element, source);
            XmlIo.SetIf(element, "coefficient", source.PerUnit, 1m);
        }

        parent.Add(element);
    }

    /// <summary>
    /// 读"选哪些牌"。整块不在时用默认值（从全部战斗牌堆里挑 1 张、不筛）。
    /// </summary>
    private static void ReadPick(XElement element, EffectDef effect, ReadLog log)
    {
        if (element.Element("pick") is not XElement pick)
        {
            return;
        }

        using (log.Scope("pick"))
        {
            XmlIo.RejectUnknown(log, pick, PickElements);

            CardPick config = effect.Pick;

            config.Source = XmlIo.EnumValue(log, pick, "source", config.Source);
            config.SetId = XmlIo.Str(pick, "set");
            config.Mode = XmlIo.EnumValue(log, pick, "mode", config.Mode);
            config.EnergyComparison = XmlIo.EnumValue(log, pick, "energyComparison", config.EnergyComparison);
            config.StarComparison = XmlIo.EnumValue(log, pick, "starComparison", config.StarComparison);

            // 流程二：牌堆（可多选）。**写了这一块就按它来**（哪怕写空 —— 空表示"全部战斗牌堆"）；
            // 不写则沿用模型默认（也是空 = 全部战斗牌堆）。
            if (pick.Element("piles") is XElement piles)
            {
                config.Piles.Clear();

                foreach (string name in XmlIo.List(pick, "piles"))
                {
                    if (TryEnum(pick, log, "piles", name, out PileKind pile))
                    {
                        config.Piles.Add(pile);
                    }
                }
            }

            Fill(pick, log, "types", config.Types);
            Fill(pick, log, "rarities", config.Rarities);
            Fill(pick, log, "tags", config.Tags);
            Fill(pick, log, "keywords", config.Keywords);

            config.CardIds.Clear();

            foreach (string id in XmlIo.List(pick, "cardIds"))
            {
                config.CardIds.Add(id);
            }

            // 流程十：张数的三件套。缺项即默认（基础值 1、没有变量）。
            if (pick.Element("pickCount") is XElement count)
            {
                // "张数就是效果数值"的效果（消耗牌）：<pickCount> 在这里没有位置，
                // 那份张数写在效果自己的 <base> 里。**报出来而不是静默忽略**——
                // 静默的话文件里那一段看着像生效了，实际什么也没做。
                if (CardPickPolicies.For(effect.Kind).CountFromEffect)
                {
                    log.Error(
                        "这条效果的张数由它自己的 <base> 给（<base> 的数值就是消耗几张），" +
                        "<pickCount> 已忽略——请把张数写在 <base> 里。");
                }
                else
                {
                    using (log.Scope("pickCount"))
                    {
                        XmlIo.RejectUnknown(log, count, BaseElements);

                        int value = XmlIo.Int(log, count, "count", 1);
                        ValueSource source = ReadSource(log, count);

                        // 系数是乘在变量上的（与效果主体那边同一读法）：没有 <source> 时它没有意义，
                        // 所以"单独写了 coefficient"会被 ReadCoefficient 记一条错误并忽略。
                        if (ReadCoefficient(log, count, allowAlone: false) is decimal coefficient)
                        {
                            source.PerUnit = coefficient;
                        }

                        config.Count = new Modules.Variables.NumericValue(value, source);
                    }
                }
            }
        }
    }

    /// <summary>
    /// 费用阈值的三条"填了不生效"的检查（读写两侧共用同一份判据——两处各写一遍必然分叉）。
    ///
    /// 在写完 / 读完一条效果之后调一次；**只看这两块与运算符的组合**，不看别的字段。
    /// </summary>
    private static void WarnCostThresholds(EffectDef effect, ReadLog log)
    {
        bool picker = CardPickPolicies.UsesPicker(effect.Kind);

        foreach (ValueRole role in CostRoles.All)
        {
            bool hasThreshold =
                effect.AmountOf(role) != 0 || effect.SourceOf(role).IsUsed;

            string block = role == ValueRole.Energy ? "energyAmount" : "starAmount";
            string comparison = role == ValueRole.Energy ? "energyComparison" : "starComparison";
            CostComparison op = effect.Pick.ComparisonOf(role);

            if (!picker && (hasThreshold || op != CostComparison.None))
            {
                log.Error(
                    $"<{block}> / <{comparison}> 只对「调用选牌控件」的效果有意义，" +
                    $"这条效果（{effect.Kind}）不挑牌，这几项已忽略。");
                continue;
            }

            if (hasThreshold && op == CostComparison.None)
            {
                log.Error(
                    $"写了 <{block}> 的阈值，但 <{comparison}> 是「不筛选」（或没写），" +
                    "这个阈值不会参与筛选——要么给它一个运算符，要么把阈值删掉。");
            }

            if (op == CostComparison.IsX && hasThreshold)
            {
                log.Error(
                    $"<{comparison}> 是 X（那一档不用阈值），所以 <{block}> 里的阈值已忽略。" +
                    "要按阈值比就换一个运算符。");
            }
        }
    }

    /// <summary>
    /// 读一串枚举值（多选的维度）：不认识的名字记一条、跳过。
    /// </summary>
    private static void Fill<T>(XElement pick, ReadLog log, string name, List<T> into)
        where T : struct, Enum
    {
        into.Clear();

        foreach (string text in XmlIo.List(pick, name))
        {
            if (TryEnum(pick, log, name, text, out T value))
            {
                into.Add(value);
            }
        }
    }

    /// <summary>把一个枚举名解析成值；不认识时记一条（位置指到哪一维）。</summary>
    private static bool TryEnum<T>(XElement pick, ReadLog log, string list, string text, out T value)
        where T : struct, Enum
    {
        if (System.Enum.TryParse(text, ignoreCase: true, out value) && System.Enum.IsDefined(value))
        {
            return true;
        }

        using (log.Scope(list))
        {
            log.Error($"\"{text}\" 不是合法的 {typeof(T).Name}，已忽略。合法值：" +
                      string.Join(" / ", System.Enum.GetNames<T>()));
        }

        value = default;
        return false;
    }

    /// <summary>
    /// 一个数值角色在 XML 里的块名。
    ///
    /// 数值叫 <c>&lt;base&gt;</c>、次数叫 <c>&lt;repeat&gt;</c>（这两个名字先有）、
    /// 两个费用阈值各叫 <c>&lt;energyAmount&gt;</c> / <c>&lt;starAmount&gt;</c>
    /// ——与效果模型上那两个字段同名，读文件时对得起来。
    /// </summary>
    private static string BlockName(ValueRole role) => role switch
    {
        ValueRole.Repeat => "repeat",
        ValueRole.Energy => "energyAmount",
        ValueRole.Star => "starAmount",
        _ => "base",
    };

    private static void ReadValueBlocks(XElement parent, EffectDef effect, ReadLog log)
    {
        // 四个角色各读自己那一块（见 ValueRole）：块名、默认值、以及"要不要读来源"的规矩
        // 由角色决定，读法本身只写一遍。
        foreach (ValueRole role in EffectUpgrade.AllRoles)
        {
            ReadValueBlock(parent, effect, role, log);
        }
    }

    private static void ReadValueBlock(XElement parent, EffectDef effect, ValueRole role, ReadLog log)
    {
        string name = BlockName(role);

        if (parent.Element(name) is not XElement element)
        {
            return;
        }

        using (log.Scope(name))
        {
            XmlIo.RejectUnknown(log, element, BaseElements);

            int fallback = role == ValueRole.Repeat ? Default.Repeat : 0;
            effect.SetAmount(role, XmlIo.Int(log, element, "count", fallback));
            ValueSource source = ReadSource(log, element);

            // 次数那一块多一条规矩：没写 <source>（或写 NULL）就是**固定次数**，
            // 这时不能把空来源盖上去（不然"有没有变量"这个判据会被静默改掉）。
            // 其余三个角色没有这种"空来源有语义"的读法，照读即可。
            if (role != ValueRole.Repeat || source.IsUsed)
            {
                effect.SetSource(role, source);
            }

            if (ReadCoefficient(log, element, allowAlone: false) is decimal coefficient)
            {
                effect.SourceOf(role).PerUnit = coefficient;
            }
        }
    }

    /* ------------------------------------------------------------------ 条件 */

    /// <summary><c>&lt;condition&gt;</c> 接受的全部子元素（两侧块与四个升级增量）。</summary>
    private static readonly string[] ConditionElements =
    {
        "comparison", "left", "right",
        "leftUpgrade", "leftPerUnitUpgrade", "rightUpgrade", "rightPerUnitUpgrade",
    };

    /// <summary>
    /// 写启用条件（<see cref="EffectCondition"/>）。未启用时**整个元素不写**——
    /// 与其它参数一样，"与默认相同就不写"，没条件的工程文件一个字节都不变。
    ///
    /// 形状：
    /// <c>&lt;condition&gt;&lt;comparison&gt;AtLeast&lt;/comparison&gt;
    /// &lt;left&gt;&lt;count&gt;0&lt;/count&gt;&lt;source&gt;…&lt;/source&gt;&lt;/left&gt;
    /// &lt;right&gt;&lt;count&gt;3&lt;/count&gt;&lt;/right&gt;
    /// &lt;leftUpgrade&gt;1&lt;/leftUpgrade&gt;…&lt;/condition&gt;</c>
    /// 两侧块内部与数值块同一套（count / source / coefficient），读的人只需要认一次。
    /// </summary>
    private static void WriteCondition(XElement parent, EffectDef effect)
    {
        if (effect.Condition is not { } condition)
        {
            return;
        }

        var element = new XElement("condition",
            new XElement("comparison", condition.Comparison.ToString()));

        WriteConditionSide(element, "left", condition.LeftAmount, condition.LeftSource, fallback: 0);
        WriteConditionSide(element, "right", condition.RightAmount, condition.RightSource, fallback: 1);

        void WriteUpgradeNumber(string name, int value)
        {
            if (value != 0)
            {
                element.Add(new XElement(name, value.ToString(CultureInfo.InvariantCulture)));
            }
        }

        void WriteUpgradeDecimal(string name, decimal value)
        {
            if (value != 0m)
            {
                element.Add(new XElement(name, value.ToString(CultureInfo.InvariantCulture)));
            }
        }

        WriteUpgradeNumber("leftUpgrade", condition.LeftUpgrade);
        WriteUpgradeDecimal("leftPerUnitUpgrade", condition.LeftPerUnitUpgrade);
        WriteUpgradeNumber("rightUpgrade", condition.RightUpgrade);
        WriteUpgradeDecimal("rightPerUnitUpgrade", condition.RightPerUnitUpgrade);

        parent.Add(element);
    }

    /// <summary>条件一侧的块：与数值块同一套形状（count / source / coefficient），缺项即默认。</summary>
    private static void WriteConditionSide(XElement parent, string name, int amount, ValueSource source, int fallback)
    {
        var element = new XElement(name);

        XmlIo.SetIf(element, "count", amount, fallback);

        if (source.IsUsed)
        {
            WriteSource(element, source);
            XmlIo.SetIf(element, "coefficient", source.PerUnit, 1m);
        }

        parent.Add(element);
    }

    /// <summary>读启用条件。整块不在时什么都不做（无条件）。</summary>
    private static void ReadCondition(XElement element, EffectDef effect, ReadLog log)
    {
        if (element.Element("condition") is not XElement cond)
        {
            return;
        }

        using (log.Scope("condition"))
        {
            XmlIo.RejectUnknown(log, cond, ConditionElements);

            var condition = new EffectCondition
            {
                Comparison = XmlIo.EnumValue(log, cond, "comparison", ConditionComparison.AtLeast),
                LeftUpgrade = XmlIo.Int(log, cond, "leftUpgrade", 0),
                RightUpgrade = XmlIo.Int(log, cond, "rightUpgrade", 0),
                RightAmount = 1,
            };

            condition.LeftPerUnitUpgrade = XmlIo.Decimal(log, cond, "leftPerUnitUpgrade", 0m);
            condition.RightPerUnitUpgrade = XmlIo.Decimal(log, cond, "rightPerUnitUpgrade", 0m);

            ReadConditionSide(cond, "left", log, condition, fallback: 0);
            ReadConditionSide(cond, "right", log, condition, fallback: 1);

            effect.Condition = condition;
        }
    }

    /// <summary>读条件一侧的块（缺块 = 这一侧用默认值）。</summary>
    private static void ReadConditionSide(
        XElement cond,
        string name,
        ReadLog log,
        EffectCondition condition,
        int fallback)
    {
        if (cond.Element(name) is not XElement side)
        {
            return;
        }

        using (log.Scope(name))
        {
            XmlIo.RejectUnknown(log, side, BaseElements);

            int amount = XmlIo.Int(log, side, "count", fallback);
            ValueSource source = ReadSource(log, side);

            if (ReadCoefficient(log, side, allowAlone: false) is decimal coefficient)
            {
                source.PerUnit = coefficient;
            }

            if (name == "left")
            {
                condition.LeftAmount = amount;
                condition.LeftSource = source;
            }
            else
            {
                condition.RightAmount = amount;
                condition.RightSource = source;
            }
        }
    }

    /* ------------------------------------------------------------------ 升级 */

    private static void WriteUpgrade(XElement parent, EffectDef effect)
    {
        bool hasLevels = effect.Levels is { Count: > 0 };
        bool hasDeltas = effect.Upgrade.IsSet;

        if (!hasLevels && !hasDeltas)
        {
            return;
        }

        var upgrade = new XElement("upgrade");

        if (hasLevels)
        {
            for (int level = 0; level < effect.Levels!.Count; level++)
            {
                if (effect.Levels[level] is not EffectLevel step)
                {
                    // "这一级沿用上一级"的占位：把后面的级顶到正确的下标上，必须留。
                    continue;
                }

                upgrade.Add(LevelToXml(level + 1, step));
            }
        }

        if (hasDeltas)
        {
            var element = new XElement("li");

            foreach (ValueRole role in EffectUpgrade.AllRoles)
            {
                WriteDelta(
                    element,
                    BlockName(role),
                    effect.Upgrade.AmountDeltaOf(role),
                    effect.Upgrade.PerUnitDeltaOf(role),
                    effect.Upgrade.SourceOf(role));
            }

            upgrade.Add(element);
        }

        parent.Add(upgrade);
    }

    private static XElement LevelToXml(int level, EffectLevel step)
    {
        var element = new XElement("li", new XElement("level", level.ToString(CultureInfo.InvariantCulture)));

        // 四个角色各写自己那一块（只写这一级真填了的项，其余留空 = 沿用上一级）。
        foreach (ValueRole role in EffectUpgrade.AllRoles)
        {
            WriteLevelPart(element, BlockName(role), step, role);
        }

        return element;
    }

    /// <summary>等级表里一个角色那一块（<c>count</c> / <c>coefficient</c> / <c>source</c>）。</summary>
    private static void WriteLevelPart(XElement parent, string name, EffectLevel step, ValueRole role)
    {
        if (!step.RoleIsSet(role))
        {
            return;
        }

        var block = new XElement(name);

        XmlIo.SetIf(block, "count", step.AmountOf(role));

        if (step.SourceOf(role) is ValueSource source)
        {
            WriteSource(block, source);
        }

        XmlIo.SetIf(block, "coefficient", step.PerUnitOf(role));

        if (block.HasElements)
        {
            parent.Add(block);
        }
    }

    /// <summary>
    /// 增量块：两个数是"每级加多少"，另外可选一个"升级后换用的变量"
    /// （<paramref name="upgradedSource"/>，只在没有等级表时生效）。
    /// </summary>
    private static void WriteDelta(
        XElement parent,
        string name,
        decimal amountDelta,
        decimal perUnitDelta,
        ValueSource? upgradedSource)
    {
        if (amountDelta == 0m && perUnitDelta == 0m && upgradedSource is null)
        {
            return;
        }

        var element = new XElement(name);

        XmlIo.SetIf(element, "count", amountDelta, 0m);
        XmlIo.SetIf(element, "coefficient", perUnitDelta, 0m);

        if (upgradedSource is not null)
        {
            WriteSource(element, upgradedSource);
        }

        parent.Add(element);
    }

    private static void ReadUpgrade(XElement parent, EffectDef effect, ReadLog log)
    {
        if (parent.Element("upgrade") is not XElement upgrade)
        {
            return;
        }

        using (log.Scope("upgrade"))
        {
            var levels = new List<EffectLevel?>();
            bool hasDelta = false;

            foreach (XElement item in upgrade.Elements())
            {
                using (log.Scope("li"))
                {
                    if (!item.Name.LocalName.Equals("li", StringComparison.Ordinal))
                    {
                        log.Error($"只应有 <li>，实际是 <{item.Name.LocalName}>，这一项已跳过。");
                        continue;
                    }

                    XmlIo.RejectUnknown(log, item, UpgradeBlockNames);

                    if (item.Element("level") is XElement levelElement)
                    {
                        ReadLevel(item, levelElement, levels, log);
                        continue;
                    }

                    if (hasDelta)
                    {
                        log.Error(
                            "这是第二个没有 <level> 的 <li>。没有 <level> 的是「每级加多少」的增量，" +
                            "只能有一份，所以这一份已忽略——要写某几级的绝对值，给它加上 <level>。");
                        continue;
                    }

                    ReadDelta(item, effect, log);
                    hasDelta = true;
                }
            }

            if (levels.Count > 0)
            {
                effect.Levels = levels;
            }
        }
    }

    private static void ReadLevel(XElement item, XElement levelElement, List<EffectLevel?> levels, ReadLog log)
    {
        using (log.Scope("level"))
        {
            if (!int.TryParse(levelElement.Value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out int level) || level < 1)
            {
                log.Error($"<level> 应为 1 起算的整数（第几级），实际是 \"{levelElement.Value.Trim()}\"，这一项已跳过。");
                return;
            }

            if (level <= levels.Count && levels[level - 1] is not null)
            {
                log.Error($"{level} 级写了两遍，后面的那一份已忽略。");
                return;
            }

            // 中间缺的级补成 null —— 那正是"这一级沿用上一级"的占位，
            // 少了它后面的级会整体往前挪。
            while (levels.Count < level)
            {
                levels.Add(null);
            }

            var step = new EffectLevel();

            foreach (ValueRole role in EffectUpgrade.AllRoles)
            {
                ReadLevelPart(item, step, role, log);
            }

            levels[level - 1] = step;
        }
    }

    /// <summary>
    /// 读等级表里一个角色那一块（<c>&lt;base&gt;</c> / <c>&lt;repeat&gt;</c> /
    /// <c>&lt;energyAmount&gt;</c> / <c>&lt;starAmount&gt;</c>）。
    ///
    /// 缺项留 null = 沿用上一级（见 <see cref="EffectLevel"/>）。
    /// </summary>
    private static void ReadLevelPart(XElement item, EffectLevel step, ValueRole role, ReadLog log)
    {
        string name = BlockName(role);

        if (item.Element(name) is not XElement block)
        {
            return;
        }

        using (log.Scope(name))
        {
            XmlIo.RejectUnknown(log, block, BaseElements);

            step.SetAmount(role, XmlIo.IntOrNull(log, block, "count"));
            step.SetPerUnit(role, ReadCoefficient(log, block, allowAlone: true));
            ReadLevelSource(block, source => step.SetSource(role, source), log);
        }
    }

    /// <summary>
    /// 等级表里的来源：**写了才算换**。
    ///
    /// 这里要用"元素在不在"来判断，不能用"变量有没有选"——<c>&lt;source&gt;None&lt;/source&gt;</c>
    /// 是"这一级不要变量了"（换成固定值），与"这一级不动变量"（不写）完全不同。
    /// <c>&lt;source&gt;NULL&lt;/source&gt;</c> 是后者，即"沿用上一级"。
    /// </summary>
    private static void ReadLevelSource(XElement block, Action<ValueSource> assign, ReadLog log)
    {
        if (block.Element("source") is null)
        {
            return;
        }

        if (XmlIo.Str(block, "source") is null)
        {
            // NULL：明确的"空"，也就是沿用上一级，什么都不做。
            return;
        }

        assign(ReadSource(log, block));
    }

    /// <summary>
    /// 读增量块（没有 <c>&lt;level&gt;</c> 的那一项）。
    ///
    /// 两个数（<c>&lt;count&gt;</c> / <c>&lt;coefficient&gt;</c>）是"每级加多少"；
    /// 而 <c>&lt;source&gt;</c> 是**升级后换用的变量**——那一项与增量不是一回事，
    /// 它只在这一条效果没有等级表时生效（有等级表时整条效果由表说了算）。
    /// </summary>
    private static void ReadDelta(XElement item, EffectDef effect, ReadLog log)
    {
        var upgrade = new EffectUpgrade();

        foreach (ValueRole role in EffectUpgrade.AllRoles)
        {
            string name = BlockName(role);

            if (item.Element(name) is not XElement block)
            {
                continue;
            }

            using (log.Scope(name))
            {
                XmlIo.RejectUnknown(log, block, BaseElements);

                upgrade.SetAmountDelta(role, XmlIo.Int(log, block, "count", 0));
                upgrade.SetPerUnitDelta(role, XmlIo.Decimal(log, block, "coefficient", 0m));
                upgrade.SetSource(role, ReadUpgradeSource(log, block));
            }
        }

        effect.Upgrade = upgrade;
    }

    /// <summary>
    /// 增量块里的 <c>&lt;source&gt;</c>：升级后换用的变量。没有（或写成 <c>NULL</c>）就是"不换"。
    /// </summary>
    private static ValueSource? ReadUpgradeSource(ReadLog log, XElement block)
    {
        if (block.Element("source") is null || XmlIo.Str(block, "source") is null)
        {
            return null;
        }

        ValueSource source = ReadSource(log, block);

        // 写了 <source> 但值是 None：那是"升级后不要变量了"，也就是换成固定值。
        // 固定值效果没有算式可言，所以这条路说不通，报一声并当作不换。
        if (!source.IsUsed)
        {
            log.Error(
                "增量里的 <source> 是 None（没有变量）：升级后换用的变量必须是一个真的来源，" +
                "已按「不换」处理。");

            return null;
        }

        return source;
    }

    /* ------------------------------------------------------------------ 来源 */

    /// <summary>三件套里那三个数（外加一个来源自己的参数）。</summary>
    private static readonly string[] BaseElements =
    {
        "count", "source", "coefficient", "power", "actor", "filter", "scope",
    };

    private static void WriteSource(XElement block, ValueSource source)
    {
        block.Add(new XElement("source", source.Kind.ToString()));

        // 来源自己的参数：只写与默认不同的，且只在真的用到时写
        // （"取自目标"这种默认取自身，写出来是噪音；换了才写）。
        XmlIo.SetIf(block, "actor", source.Actor, DefaultSource.Actor);
        XmlIo.SetIf(block, "power", source.PowerId, DefaultSource.PowerId);

        // 数哪些牌（只写真的用到的维度）。形状与选牌块里那几维**完全一样**：两边共用
        // 同一个谓词生成处，文件里也就该长一样。
        if (source.Filter.HasFilter())
        {
            block.Add(FilterToXml(source.Filter));
        }

        XmlIo.SetIf(block, "scope", source.CountScope, DefaultSource.CountScope);
    }

    /// <summary>
    /// 一份多维筛选的 XML：五个取值列表 + 两个费用运算符与阈值。
    ///
    /// 选牌块（<c>&lt;pick&gt;</c>）与来源块（"按条件数牌"）共用这一份形状——
    /// 两处筛的是同一种东西，写出来的形状就该相同，读的人也只需要认一次。
    /// 差别只有阈值：选牌那侧住效果上（<c>&lt;energyAmount&gt;</c> 那两块），
    /// 这里是自己块里的 <c>&lt;energyThreshold&gt;</c>。
    /// </summary>
    private static XElement FilterToXml(CardFilter filter, string name = "filter")
    {
        var element = new XElement(name);

        WriteList(element, "types", filter.Types.Select(t => t.ToString()));
        WriteList(element, "rarities", filter.Rarities.Select(r => r.ToString()));
        WriteList(element, "cardIds", filter.CardIds);
        WriteList(element, "tags", filter.Tags.Select(t => t.ToString()));
        WriteList(element, "keywords", filter.Keywords.Select(k => k.ToString()));

        XmlIo.SetIf(element, "energyComparison", filter.EnergyComparison, CostComparison.None);
        XmlIo.SetIf(element, "energyThreshold", filter.EnergyThreshold, 0);
        XmlIo.SetIf(element, "starComparison", filter.StarComparison, CostComparison.None);
        XmlIo.SetIf(element, "starThreshold", filter.StarThreshold, 0);

        return element;
    }

    /// <summary>
    /// 读一份多维筛选。<paramref name="element"/> 为 null（没写这一块）时给一份空筛选
    /// （= 不筛任何维度）。
    ///
    /// <c>&lt;filter&gt;</c> 早先是**单个条件名**（<c>&lt;filter&gt;TagStrike&lt;/filter&gt;</c>）。
    /// 那种写法现在读不进来，会明确报一条——静默读成"不筛"会让效果的数值悄悄变掉，
    /// 比当场报错糟得多（与枚举改名后不兼容旧文件是同一条规矩）。
    /// </summary>
    private static CardFilter ReadFilter(ReadLog log, XElement? element)
    {
        if (element is null)
        {
            return new CardFilter();
        }

        if (!element.HasElements && !string.IsNullOrWhiteSpace(element.Value))
        {
            log.Error(
                "<filter> 现在是**多维筛选块**（里面写 <types> / <rarities> / <cardIds> / <tags> / " +
                "<keywords> 与 <energyComparison> / <energyThreshold> 那四项），不再是单个条件名。" +
                "这份文件里写的是旧写法，读不进来——请在界面上重新选条件。");

            return new CardFilter();
        }

        using (log.Scope("filter"))
        {
            var filter = new CardFilter();

            Fill(element, log, "types", filter.Types);
            Fill(element, log, "rarities", filter.Rarities);
            Fill(element, log, "tags", filter.Tags);
            Fill(element, log, "keywords", filter.Keywords);

            filter.CardIds.Clear();
            filter.CardIds.AddRange(XmlIo.List(element, "cardIds").Where(id => !string.IsNullOrWhiteSpace(id)));

            filter.EnergyComparison = XmlIo.EnumValue(log, element, "energyComparison", CostComparison.None);
            filter.StarComparison = XmlIo.EnumValue(log, element, "starComparison", CostComparison.None);
            filter.EnergyThreshold = XmlIo.Int(log, element, "energyThreshold");
            filter.StarThreshold = XmlIo.Int(log, element, "starThreshold");

            return filter;
        }
    }

    /// <summary>
    /// 读来源。<c>&lt;source&gt;</c> 没写、或写成 <c>NULL</c> 时返回"没有变量"的空来源。
    ///
    /// 注意每个名字都是**块里的子元素**：所以 Kind 也从块上读，而不是从 <c>&lt;source&gt;</c>
    /// 自己上读——那样会去找 <c>&lt;source&gt;&lt;source&gt;</c>，永远读回 None。
    /// </summary>
    private static ValueSource ReadSource(ReadLog log, XElement block)
    {
        if (block.Element("source") is null || XmlIo.Str(block, "source") is null)
        {
            // 没写，或明确写了 NULL（"空"）：这个数值没有变量。
            // 这时其余几项（actor / power / filter）无从谈起，也不去读。
            return new ValueSource();
        }

        var source = new ValueSource
        {
            Kind = XmlIo.EnumValue(log, block, "source", ValueSourceKind.None),
            Actor = XmlIo.EnumValue(log, block, "actor", DefaultSource.Actor),
            PowerId = XmlIo.Str(block, "power"),
            Filter = ReadFilter(log, block.Element("filter")),
            CountScope = XmlIo.EnumValue(log, block, "scope", DefaultSource.CountScope),
        };

        // 没选变量时不该有别的参数：那是"填了不生效"的写法，说出来比留着强。
        if (!source.IsUsed &&
            (source.Actor != DefaultSource.Actor || source.PowerId is not null ||
             source.Filter.HasFilter() || source.CountScope != DefaultSource.CountScope))
        {
            log.Error(
                "<source> 是 None（没有变量），却又写了 actor / power / filter / scope——那几项已忽略。" +
                "要固定值就只留 <count>，要变量就选一个真正的来源。");
        }

        return source;
    }

    /// <summary>
    /// 读 <c>&lt;coefficient&gt;</c>（变量系数）。没写时返回 null（"不动"，而不是 1）。
    ///
    /// <paramref name="allowAlone"/> 说的是"只写系数、不写变量"算不算合法：
    /// 等级表里算（那表示"这一级只把系数改掉，变量沿用上一级"），
    /// 效果自己身上不算（没有变量，系数就没东西可乘——那是"填了不生效"）。
    /// </summary>
    private static decimal? ReadCoefficient(ReadLog log, XElement block, bool allowAlone)
    {
        decimal? value = XmlIo.DecimalOrNull(log, block, "coefficient");

        if (value is not null && !allowAlone && block.Element("source") is null)
        {
            log.Error(
                "<coefficient> 要配一个 <source> 才有意义（系数是乘在变量上的），这一项已忽略。" +
                "固定值只写 <count> 即可。");
            return null;
        }

        return value;
    }
}
