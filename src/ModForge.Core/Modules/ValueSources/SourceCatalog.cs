using ModForge.Core.Model;
using ModForge.Core.Modules.Counting;

namespace ModForge.Core.Modules.ValueSources;

/// <summary>
/// 变量来源模块的唯一名单：**每个来源一行**，回答"它叫什么、从哪儿读、要不要再选什么"。
///
/// 为什么要有这份名单：以前"怎么取值"是一段 if/else 加三个 switch（玩家资源 / 状态层数 /
/// 生物属性），而"要不要取自谁""要不要选状态""界面归到哪一组"三件事各自散在别处
/// （`ValueSource.IsPlayerScoped` 里三十个枚举项、schema 里两句手写条件、界面的一个 if 链）。
/// 它们必须互相对得上，而漏改一处不会报错——只会让某个来源读到另一侧的数（编译过、
/// 行为错），或者界面上少一个选项。现在这些判断全部由 <see cref="SourceSpec.Reader"/> 推出来。
///
/// **本模块与计数模块的分工**：读当前的数（资源、X 值、金币、回合数、生物属性、状态层数）
/// 在这里；数出来的（战斗历史、牌堆张数、按条件数牌、自建跟踪，共三十种）在
/// <c>Modules/Counting/</c>，本模块只把 <see cref="SourceReader.Counter"/> 那一档转过去。
/// 两边的名单加起来必须覆盖枚举的每一个成员——`tools/srctest` 逐个断言，
/// **加了新来源却忘了声明读法会当场变红**，不会再静默落到兜底分支上。
/// </summary>
public static class SourceCatalog
{
    /// <summary>
    /// 本模块直接给出的来源，**按枚举顺序**排列（照枚举加成员时不用来回找位置；
    /// 计数类不在这里，序号自然是断开的）。
    ///
    /// 表达式写的是**字面代码**（不是"算法"），因为这几条就是游戏暴露的属性名，
    /// 多一层映射只会多一处可能写错的地方。
    /// </summary>
    public static IReadOnlyList<SourceSpec> All { get; } = new[]
    {
        // ---- 不取值 ----
        new SourceSpec(ValueSourceKind.None, "无", "None", SourceReader.None),

        // ---- 状态层数：要再选一个状态，类型解析由调用方注入（见 Context.PowerTypeFor） ----
        new SourceSpec(ValueSourceKind.PowerStacks, "状态层数", "power stacks", SourceReader.PowerStacks),

        // ---- 生物属性：取自谁由 Actor 决定（自身 / 目标） ----
        new SourceSpec(ValueSourceKind.CurrentHp, "当前生命", "CurrentHp", SourceReader.CreatureStat,
            "{who}!.CurrentHp"),

        new SourceSpec(ValueSourceKind.MaxHp, "最大生命", "MaxHp", SourceReader.CreatureStat,
            "{who}!.MaxHp"),

        new SourceSpec(ValueSourceKind.MissingHp, "已损失生命", "MissingHp", SourceReader.CreatureStat,
            "({who}!.MaxHp - {who}!.CurrentHp)"),

        new SourceSpec(ValueSourceKind.Block, "格挡", "Block", SourceReader.CreatureStat,
            "{who}!.Block"),

        // ---- 玩家资源与 X 值 ----
        new SourceSpec(ValueSourceKind.Energy, "当前能量", "Energy", SourceReader.PlayerScoped,
            "{player}.PlayerCombatState!.Energy"),

        new SourceSpec(ValueSourceKind.Stars, "当前星辰", "Stars", SourceReader.PlayerScoped,
            "{player}.PlayerCombatState!.Stars"),

        new SourceSpec(ValueSourceKind.Gold, "金币", "Gold", SourceReader.PlayerScoped,
            "{player}.Gold"),

        // X 值要的是"实际消耗了多少"，所以走游戏自己的解析方法（它会算上"化学 X"这类遗物的
        // 修正）；与"当前能量"是两个数——X 费卡打出时能量已经扣光了。
        new SourceSpec(ValueSourceKind.EnergyX, "X 值（能量）", "X value (energy)", SourceReader.PlayerScoped,
            "{card}.ResolveEnergyXValue()", NeedsCard: true),

        new SourceSpec(ValueSourceKind.StarsX, "X 值（星辰）", "X value (stars)", SourceReader.PlayerScoped,
            "{card}.ResolveStarXValue()", NeedsCard: true),

        new SourceSpec(ValueSourceKind.TurnNumber, "回合数", "turn number", SourceReader.PlayerScoped,
            "{player}.PlayerCombatState!.TurnNumber"),

        // ---- 战斗历史计数（20 种）、牌堆张数（5 种）、自建跟踪（4 种）、按条件数牌 ----
        // 这三十种的读法在计数模块里（Modules/Counting/），本模块只把请求转过去。
    };

    private static readonly Dictionary<ValueSourceKind, SourceSpec> ByKind =
        All.ToDictionary(s => s.Kind);

    /// <summary>本模块声明的那个来源；计数类在这里没有行（它们在计数模块里）。</summary>
    public static SourceSpec? Find(ValueSourceKind kind) =>
        ByKind.TryGetValue(kind, out SourceSpec? spec) ? spec : null;

    /// <summary>
    /// 这个来源**从哪儿读**——包括转给计数模块的那些。
    ///
    /// 认不出来时返回 <see cref="SourceReader.None"/>。真出现那种情况说明枚举加了成员而
    /// 名单没跟上（`srctest` 会拦下），这里的返回值只保证"不静默读到别处的数"。
    /// </summary>
    public static SourceReader Read(ValueSourceKind kind) =>
        Find(kind)?.Reader ?? (CounterCatalog.IsCounter(kind) ? SourceReader.Counter : SourceReader.None);

    /// <summary>
    /// 这个来源属于玩家维度（资源、牌堆、战斗历史…），与具体生物无关。
    ///
    /// 界面据此隐藏"取自谁"；描述据此不再念"自身/目标"。计数读了玩家的牌堆与历史，
    /// 所以它们同样是玩家维度——这条以前是三十个枚举项的手写清单，现在是这一行。
    /// </summary>
    public static bool IsPlayerScoped(ValueSourceKind kind) =>
        Read(kind) is SourceReader.PlayerScoped or SourceReader.Counter;

    /// <summary>这个来源要不要再选"是哪个状态"（界面据此在它后面加一格状态选择框）。</summary>
    public static bool NeedsPower(ValueSourceKind kind) => Read(kind) == SourceReader.PowerStacks;

    /// <summary>
    /// 这个来源的名字。计数类的名字由计数模块给出（它才是"这个计数叫什么"的出处），
    /// 其余取本模块那一行——**每个来源的名字都只有一个出处**，界面与描述都读这里。
    /// </summary>
    public static string Label(ValueSourceKind kind, string uiLanguage = "zh")
    {
        bool zh = uiLanguage.StartsWith("zh", StringComparison.OrdinalIgnoreCase);

        if (CounterCatalog.Find(kind) is Counter counter)
        {
            return zh ? counter.LabelZh : counter.LabelEn;
        }

        if (Find(kind) is not SourceSpec spec)
        {
            // 名单缺这一条。名字退回枚举名（与旧版兜底一致），不假装有一条。
            return kind.ToString();
        }

        return zh ? spec.LabelZh : spec.LabelEn;
    }

    /// <summary>
    /// 一份来源的**简短说明**（"自身 力量"、"2 × 手牌数"），编辑器与自动描述用。
    ///
    /// 与描述模块的 <c>ValueWording.SourceLabel</c> 不是一回事：那个把来源摆进整句里
    /// （语序各语言不同），这个只是一段独立的短语。
    /// </summary>
    public static string Describe(ValueSource source, string uiLanguage = "zh")
    {
        bool zh = uiLanguage.StartsWith("zh", StringComparison.OrdinalIgnoreCase);

        string actor = source.Actor switch
        {
            ValueSourceActor.Self => zh ? "自身" : "your",
            ValueSourceActor.Target => zh ? "目标" : "the target's",
            ValueSourceActor.AllEnemies => zh ? "所有敌人" : "all enemies'",
            ValueSourceActor.AllAllies => zh ? "所有友方" : "all allies'",
            _ => zh ? "自身" : "your",
        };

        string kind = Label(source.Kind, uiLanguage);

        if (IsPlayerScoped(source.Kind))
        {
            // 牌堆与资源只与玩家有关，不必再写"自身/目标"。
            return source.PerUnit == 1m ? kind : $"{source.PerUnit} × {kind}";
        }

        return zh
            ? (source.PerUnit == 1m ? $"{actor}{kind}" : $"{source.PerUnit} × {actor}{kind}")
            : (source.PerUnit == 1m ? $"{actor} {kind}" : $"{source.PerUnit} × {actor} {kind}");
    }
}
