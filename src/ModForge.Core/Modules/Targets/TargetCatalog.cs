using ModForge.Core.Model;

namespace ModForge.Core.Modules.Targets;

/// <summary>
/// 作用对象这条路的三件事的**唯一出处**：一个作用域给哪几档目标、每一档在界面上叫什么、
/// 以及某一档此刻**能不能用**。
///
/// 为什么要它：这些原来散在各处——每个效果自己在 <c>Schema</c> 里写一遍候选列表、
/// 界面在"状态"页用另一份全局列表兜底、出码处再按 <c>switch</c> 展开一遍。三处各写一套的
/// 结果实测就摆在那里：移除状态少一档、回复生命与造成伤害的候选还不一样，而"状态"页里
/// 每一条效果都摆出全部五档（哪怕那条效果根本用不上目标）。
///
/// **分成"能表达"与"此刻能不能用"两层**，这是这套设计的要点：
///
///   <see cref="Options"/>              —— 这个作用域**表达得出**哪几档（由游戏接口收什么参数定）。
///   <see cref="PlayedTargetSources"/>  —— 再叠上**此刻**的约束：要"打出时选定的目标"的那两档，
///                                         得这张牌真的给得出一个合用的对象。用不了时界面
///                                         **不显示**那一档，而当前值恰好是它时给一句红字说明。
///   <see cref="SupportsPet"/>          —— 宠物那一组只对造物轴成立（宠物是 Creature；
///                                         收 Player 的命令接不住它）。
///
/// 于是"各效果屏蔽非法目标"不需要每个效果自己列名单：**作用域就是那份名单**，
/// 动态的那部分（卡牌目标、宠物）由这里按事实判。少一处名单，就少一处会与别处对不上的名单。
/// </summary>
public static class TargetCatalog
{
    /// <summary>
    /// 这个作用域**表达得出**哪几档目标（顺序 = 界面上下拉的顺序，从最常用排到少见）。
    ///
    ///   AllAllies       造物＝<c>PlayerCreatures</c>（**不含宠物**） 玩家＝<c>CombatState.Players</c>
    ///   AllOtherAllies  同上，**去掉自己**（"其他友方"）           同上，**去掉自己**（"其他玩家"）
    ///   RandomAlly      从 AllAllies 里随机一个（含自己）          同左（＝"随机一名玩家"）
    ///   RandomOtherAlly 从 AllOtherAllies 里随机一个（不含自己）   同左
    ///
    /// **"友方"与"其他友方"的分别就是"含不含自己"**——这也是游戏自己的分法：
    /// 原版"所有友方"的牌遍历时含自己，"任意友方"（<c>TargetType.AnyAlly</c>）的候选排除自己。
    ///   玩家轴：命令收的是 <c>Player</c>——敌人身上没有 Player，"所有敌人"这三档给不出来；
    ///           反过来它是唯一给得出"随机一名玩家"的轴（造物轴上那等于先随机一个人再取生物，
    ///           与"随机一名友方"重复）。**宠物那一组也不给**：宠物是 Creature，
    ///           收 Player 的命令（能量 / 星辰 / 抽牌 / 球 / 铸造 / 召唤）接不住它。
    ///   作用于牌：谁受影响由选牌器决定，"自己"这一档说的是"这张牌的执行者"。
    ///
    /// **同一档在两轴上的集合不同**，见 <see cref="LabelOf"/> 与 <see cref="TargetEmitter"/>：
    ///   Self       造物＝<c>Owner.Creature</c>                玩家＝出牌的那个玩家
    ///   AllAllies       造物＝<c>PlayerCreatures</c>（**不含宠物**） 玩家＝<c>CombatState.Players</c>
    ///   AllOtherAllies  同上，**去掉自己**（"其他友方"）           同上，**去掉自己**（"其他玩家"）
    ///   RandomAlly      从 AllAllies 里随机一个（含自己）          同左（＝"随机一名玩家"）
    ///   RandomOtherAlly 从 AllOtherAllies 里随机一个（不含自己）   同左
    ///
    /// **"友方"与"其他友方"的分别就是"含不含自己"**——这也是游戏自己的分法：
    /// 原版"所有友方"的牌遍历时含自己，"任意友方"（<c>TargetType.AnyAlly</c>）的候选排除自己。
    /// </summary>
    public static IReadOnlyList<EffectTarget> Options(TargetScope scope) => scope switch
    {
        TargetScope.Creature => new[]
        {
            EffectTarget.Self,
            EffectTarget.Target,
            EffectTarget.AllEnemies,
            EffectTarget.RandomEnemy,
            EffectTarget.AllAllies,
            EffectTarget.RandomAlly,
            EffectTarget.AllOtherAllies,
            EffectTarget.RandomOtherAlly,
            EffectTarget.All,
            EffectTarget.SelfPet,
            EffectTarget.TargetPet,
            EffectTarget.AllAlliesPet,
            EffectTarget.RandomAllyPet,
            EffectTarget.AllOtherAlliesPet,
            EffectTarget.RandomOtherAllyPet,
        },

        TargetScope.Player => new[]
        {
            EffectTarget.Self,
            EffectTarget.Target,
            EffectTarget.AllAllies,
            EffectTarget.RandomPlayer,
            EffectTarget.AllOtherAllies,
            EffectTarget.RandomOtherAlly,
        },

        _ => new[] { EffectTarget.Self },
    };

    /// <summary>
    /// 这一档是不是**玩家宠物**那一组的（从玩家派生：<c>player.Osty</c>）。
    ///
    /// 界面据此把它们单独归到一个「玩家宠物」组里（用户要的"专门一个新组件"）；
    /// 出码据此走那几条"先读玩家、再取宠物"的形状。
    /// </summary>
    public static bool IsPetBand(EffectTarget target) => target is
        EffectTarget.SelfPet or EffectTarget.TargetPet
        or EffectTarget.AllAlliesPet or EffectTarget.RandomAllyPet
        or EffectTarget.AllOtherAlliesPet or EffectTarget.RandomOtherAllyPet;

    /// <summary>
    /// 这一档在**这个作用域**里界面上叫什么。
    ///
    /// 同名的档在两轴上落到的集合不同，所以名字也必须分轴（"所有友方"在玩家轴上就是"所有玩家"，
    /// 而"目标"在玩家轴上是"任意一名友方"——它指的是选中的那名玩家）。写成一处，
    /// 是为了不让界面自己再维护一份"某轴该显示什么"的名单，那正是会与这里对不上的东西。
    /// </summary>
    public static string LabelOf(TargetScope scope, EffectTarget target) => (scope, target) switch
    {
        // "其他友方"那一族：造物轴读"其他友方"，玩家轴读"其他玩家"（那一轴落到的是玩家）。
        (TargetScope.Player, EffectTarget.AllOtherAllies) => "所有其他玩家",
        (TargetScope.Player, EffectTarget.RandomOtherAlly) => "随机其他玩家",
        (_, EffectTarget.AllOtherAllies) => "所有其他友方",
        (_, EffectTarget.RandomOtherAlly) => "随机其他友方",

        // 玩家宠物那一组：名字自己就说明了它落在宠物上，所以不分轴（其实只有造物轴有）。
        (_, EffectTarget.SelfPet) => "自己的奥斯提",
        (_, EffectTarget.TargetPet) => "目标的奥斯提",
        (_, EffectTarget.AllAlliesPet) => "所有友方的奥斯提",
        (_, EffectTarget.RandomAllyPet) => "随机一名友方的奥斯提",
        (_, EffectTarget.AllOtherAlliesPet) => "所有其他玩家的奥斯提",
        (_, EffectTarget.RandomOtherAllyPet) => "随机其他玩家的奥斯提",

        // 玩家轴一律用**玩家**的说法（"友方"在这一轴上读起来像"某只友方生物"，而它落到的是
        // 人）："目标"这一档是**另一名玩家**（卡牌目标是"任意友方"，游戏给它的候选就是
        // "除自己以外的玩家"），"随机一名友方"也是**其他玩家**（那一轴排除自己）——
        // 含自己那一档另有一条（"随机一名玩家"）。
        (TargetScope.Player, EffectTarget.Target) => "任意其他玩家",
        (TargetScope.Player, EffectTarget.AllAllies) => "所有玩家",
        (TargetScope.Player, EffectTarget.RandomPlayer) => "随机一名玩家",
        (TargetScope.Player, EffectTarget.RandomAlly) => "随机其他玩家",

        (_, EffectTarget.Self) => "自身",
        (_, EffectTarget.Target) => "目标",
        (_, EffectTarget.AllEnemies) => "所有敌人",
        (_, EffectTarget.RandomEnemy) => "随机一名敌人",
        (_, EffectTarget.AllAllies) => "所有友方",
        (_, EffectTarget.RandomAlly) => "随机一名友方",
        (_, EffectTarget.RandomPlayer) => "随机一名玩家",
        (_, EffectTarget.All) => "所有人",

        // 作用于牌那一轴：这一档的含义是"这张牌的执行者"。
        _ => "自身",
    };

    /// <summary>这个作用域的目标候选（<c>Schema</c> 里那个字符串数组，供界面按值取）。</summary>
    public static string[] Names(TargetScope scope) =>
        Options(scope).Select(t => t.ToString()).ToArray();

    /// <summary>与 <see cref="Names"/> 一一对应的**界面标签**（界面直接用，不再自己查词表）。</summary>
    public static string[] Labels(TargetScope scope) =>
        Options(scope).Select(t => LabelOf(scope, t)).ToArray();

    /// <summary>
    /// 新建这类效果时先落在哪一档（界面用它给新效果一个合理的初始值）。
    ///
    ///   造物轴 → <see cref="EffectTarget.Target"/>：原版绝大多数攻击与减益牌都是"打选定的目标"。
    ///   玩家轴 → <see cref="EffectTarget.Self"/>：给自己能量 / 抽牌 / 加栏位是绝对主流；
    ///            那一档里的"目标"意思是"给队友"，是特例，不该当默认。
    ///   作用于牌 → <see cref="EffectTarget.Self"/>（唯一一档）。
    /// </summary>
    public static EffectTarget DefaultFor(TargetScope scope) =>
        scope == TargetScope.Creature ? EffectTarget.Target : EffectTarget.Self;

    /// <summary>
    /// 这个**卡牌目标**会不会让玩家选一个目标——也就是打出时 <c>cardPlay.Target</c> 有没有值。
    ///
    /// 只有 <c>AnyEnemy</c> 与 <c>AnyAlly</c> 会。这一条不是我们的选择，是游戏写死的：
    /// <c>CardModel.IsValidTarget</c> 对**其余所有档**都要求"没有目标"（<c>target != null</c>
    /// 时只有 AnyEnemy / AnyAlly 返回 true），而 <c>PlayCardAction</c> 也只为这两个补默认目标。
    /// 所以刻意没把游戏的 <c>AnyPlayer</c> 收进来：它本意要"选一名玩家"，真传了目标反而非法。
    /// </summary>
    public static bool ProvidesPlayedTarget(CardTargetType cardTarget) =>
        cardTarget is CardTargetType.AnyEnemy or CardTargetType.AnyAlly;

    /// <summary>
    /// 选出的那个目标**是不是一名玩家**。
    ///
    /// 只有"任意友方"能满足（游戏侧 <c>CardCmd</c> 给它的候选是"除自己以外的玩家"）。
    /// "任意敌人"给出的是敌人——敌人身上没有 Player，玩家轴的效果要
    /// <c>cardPlay.Target.Player!</c>、宠物档要 <c>…Target.Player?.Osty</c> 都会空引用。
    /// </summary>
    public static bool GivesPlayerTarget(CardTargetType cardTarget) =>
        cardTarget == CardTargetType.AnyAlly;

    /// <summary>这一档要不要用**打出时选定的目标**（"目标"与"目标的奥斯提"两档要）。</summary>
    public static bool UsesPlayedTarget(EffectTarget target) =>
        target is EffectTarget.Target or EffectTarget.TargetPet;

    /// <summary>
    /// 一个作用域里**哪几档需要卡牌目标给出对象**，以及各自接受哪几种卡牌目标。
    ///
    /// 这就是发给界面的 `playedTargetSources`（界面拿它对一下就知道某一档此刻能不能用），
    /// 同时也是 <see cref="AcceptsPlayedTarget"/> 的数据来源——**同一条规则，两种用法**，
    /// 所以不会出现"界面说能用、校验器说不行"（`kindtest` 拿全枚举逐个比对两者的域）。
    ///
    ///   造物轴的「目标」要**一个生物**：任意敌人或任意友方都行；
    ///   造物轴的「目标的奥斯提」要**一名玩家**：只有任意友方给得出（敌人没有宠物）；
    ///   玩家轴的「目标」也要**一名玩家**：同上；
    ///   作用于牌那一轴没有需要出牌目标的档（它的候选里根本没有那两档）。
    /// </summary>
    public static IReadOnlyDictionary<string, string[]> PlayedTargetSources(TargetScope scope)
    {
        var anyCreature = new[] { nameof(CardTargetType.AnyEnemy), nameof(CardTargetType.AnyAlly) };
        var anyPlayer = new[] { nameof(CardTargetType.AnyAlly) };

        return scope switch
        {
            TargetScope.Creature => new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                [nameof(EffectTarget.Target)] = anyCreature,
                [nameof(EffectTarget.TargetPet)] = anyPlayer,
            },

            TargetScope.Player => new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                [nameof(EffectTarget.Target)] = anyPlayer,
            },

            _ => new Dictionary<string, string[]>(StringComparer.Ordinal),
        };
    }

    /// <summary>
    /// 这个作用域里这一档，配上这张牌的目标能不能用。
    ///
    /// 不需要出牌目标的档总是能用（界面照旧列出来），需要的那两档按
    /// <see cref="PlayedTargetSources"/> 判。校验器用的就是这一条；界面读的是同一份名单。
    /// </summary>
    public static bool AcceptsPlayedTarget(TargetScope scope, EffectTarget target, CardTargetType cardTarget)
    {
        if (!UsesPlayedTarget(target))
        {
            return true;
        }

        return PlayedTargetSources(scope).TryGetValue(target.ToString(), out string[]? sources)
            && sources.Contains(cardTarget.ToString());
    }

    /// <summary>
    /// 这一档要的对象是"**一名玩家**"（而不是"任意一个生物"）。
    ///
    /// 只有三种情况：「目标的奥斯提」（要卡牌选中的那名玩家的宠物）、以及玩家轴的「目标」
    /// （它是"任意一名友方"，指选中的那名玩家）。校验器的提示按它说清该把卡牌目标改成什么。
    /// </summary>
    public static bool GivesPlayerTargetOnly(TargetScope scope, EffectTarget target) =>
        target == EffectTarget.TargetPet
        || (scope == TargetScope.Player && target == EffectTarget.Target);

    /// <summary>
    /// 这一档能不能出现在这个作用域里。
    ///
    /// 宠物那一组只对**造物轴**成立：宠物是 <c>Creature</c>，造物轴的命令收它，
    /// 玩家轴的命令收 <c>Player</c> 接不住（"给宠物加能量"编译不过），
    /// 作用于牌那一轴与宠物无关。校验器按这一条拦手改的 JSON；
    /// 候选列表本身就是按它生成的（所以界面不必判）。
    /// </summary>
    public static bool SupportsBand(TargetScope scope, EffectTarget target) =>
        Options(scope).Contains(target);
}
