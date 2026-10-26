using ModForge.Core.Model;
using ModForge.Core.Modules.Variables;

namespace ModForge.Core.Generate;

/// <summary>
/// 把状态引用（模型 id，如 POWER.STRENGTH_POWER）解析成 C# 类型引用，优先用本工程定义的状态。
/// </summary>
internal static class PowerRefs
{
    public static string TypeRef(ModProject project, string powerId)
    {
        string entry = powerId.Contains('.')
            ? powerId[(powerId.LastIndexOf('.') + 1)..]
            : powerId;

        PowerDef? local = project.Powers.FirstOrDefault(p =>
            Naming.ToEntry(p.Id) == entry ||
            Naming.ToEntry(p.Id) + "_POWER" == entry ||
            project.PowerTypeName(p).Equals(entry, StringComparison.OrdinalIgnoreCase));

        return local is not null
            ? $"Powers.{project.PowerTypeName(local)}"
            : $"MegaCrit.Sts2.Core.Models.Powers.{Naming.ToPascal(entry)}";
    }
}

/// <summary>
/// 把卡牌引用（模型 id，如 CARD.STRIKE_IRONCLAD）解析成 C# 类型引用，优先用本工程定义的卡。
///
/// "牌 ID"那一维的判定是 <c>c is X</c>（与卡牌自身、生成代码共用同一套类型引用），
/// 所以按牌名筛的三处（选牌器、按条件数牌、打出一张牌时的筛选）都要这个解析。
/// 它需要工程知识（本工程的卡住 <c>{项目}.Generated.Cards</c>，原版的住游戏命名空间），
/// 所以留在 Generate 侧，由调用方注入给不认识工程的模块。
/// </summary>
internal static class CardRefs
{
    public static string TypeRef(ModProject project, string cardId)
    {
        string entry = cardId.Contains('.') ? cardId[(cardId.LastIndexOf('.') + 1)..] : cardId;

        CardDef? local = project.Cards.FirstOrDefault(c =>
            ModelIds.Slugify(project.CardTypeName(c)) == entry ||
            project.CardTypeName(c).Equals(Naming.ToPascal(entry), StringComparison.OrdinalIgnoreCase));

        return local is not null
            ? $"Cards.{project.CardTypeName(local)}"
            : $"MegaCrit.Sts2.Core.Models.Cards.{Naming.ToPascal(entry)}";
    }
}

/// <summary>
/// 把附魔引用（模型 id，如 ENCHANTMENT.SHARP）解析成 C# 类型引用。
///
/// 附魔目前只能用游戏自带的（自定义附魔还需要图标等资源），所以只有原版一条路径。
/// </summary>
internal static class EnchantmentRefs
{
    public static string TypeRef(ModProject project, string enchantmentId)
    {
        string entry = enchantmentId.Contains('.')
            ? enchantmentId[(enchantmentId.LastIndexOf('.') + 1)..]
            : enchantmentId;

        // 游戏类型名与本地化条目的写法一致，只要转成 PascalCase 即可。
        return $"MegaCrit.Sts2.Core.Models.Enchantments.{Naming.ToPascal(entry)}";
    }
}

/// <summary>
/// 发出一条效果时需要的表达式。卡牌与状态所需的写法不同，这里把差异集中起来。
///
/// 这个区分很重要：<c>CardModel.Owner</c> 是 <c>Player</c>，而 <c>PowerModel.Owner</c> 是
/// <c>Creature</c>，所以"自己"和"玩家"在两个上下文里是不同的表达式。
/// </summary>
internal sealed record EmitContext(
    string Self,
    string Player,
    string Dealer,
    string Enemies,

    /// <summary>
    /// 友方集合：玩家阵营的**玩家生物**（<c>CombatState.PlayerCreatures</c>）。
    ///
    /// **不含宠物**：宠物也是玩家阵营的生物，但原版的"所有友方"用的是 PlayerCreatures
    /// （见 <c>LegionOfBone</c>），而且"任意友方"的候选也带 <c>IsPlayer</c> 过滤。
    /// 用 <c>Allies</c> 的话"给所有友方加格挡"会顺手加给奥斯提——那不是原版口径；
    /// 想作用到宠物上得显式选"…的奥斯提"那一档。
    /// </summary>
    string PlayerCreatures,
    string CardSource,
    string? CardTarget,

    /// <summary>这张牌本身，用于"把这张牌置入牌堆"之类的效果。</summary>
    string SelfCard,
    string ChoiceContext,

    /// <summary>
    /// 战斗状态（<c>CombatState</c>）的表达式。
    ///
    /// "所有人"与"所有友方（玩家轴）"要从它取：<c>Creatures</c>（敌我双方）与
    /// <c>Players</c>（所有玩家）。这两个集合在 <c>ICombatState</c> 上，见目标模块
    /// <c>Modules/Targets/</c>。
    /// </summary>
    string CombatState,

    /// <summary>
    /// 随机数那一路（<c>Rng.CombatTargets</c>）的表达式。
    ///
    /// 随机目标必须走游戏自己的 RNG：它跟着战斗存档的随机序列走，自己 new 一个
    /// <c>Random</c> 会让同一个种子打出不同的局面（也就没法复现、没法做回放）。
    /// 原版取随机目标用的就是这个（见 <c>BouncingFlask</c>）。
    /// </summary>
    string Rng,

    /// <summary>
    /// 是否声明了动态变量。卡牌通过 CanonicalVars 声明，状态没有，
    /// 所以状态里只能写死数值或就地计算。
    /// </summary>
    bool HasDynamicVars,

    /// <summary>
    /// 这条效果该用哪个**数值槽**（<c>Damage</c> / <c>Damage2</c> / <c>CalculatedDamage2</c>
    /// / <c>StrengthPower2</c> …）。
    ///
    /// 由调用方注入（卡牌生成时指向变量模块的规划）。必须按**整张牌**一起算：
    /// 单条效果自己看不出"同类里我是第几个"。状态里没有变量表，为 null。
    /// </summary>
    Func<EffectDef, VarSlot?>? SlotFor = null,

    /// <summary>
    /// 这条效果该用哪个**重复次数槽**（<c>Repeat</c> / <c>Repeat2</c>…）。
    /// 只有"能升级的重复"才有，其它情况为 null（循环里直接写字面量）。
    /// </summary>
    Func<EffectDef, VarSlot?>? RepeatSlotFor = null,

    /// <summary>
    /// 这条效果**某个费用角色**的阈值槽（<c>EnergyAmount</c> / <c>StarAmount</c>，见
    /// <c>VariableModule.ThresholdSlots</c>）。筛了那一维才有；没筛、或状态里没有变量表时为 null，
    /// 那时阈值只能写字面量（升级改不到它，校验器会就那种组合提示）。
    /// </summary>
    Func<EffectDef, ValueRole, VarSlot?>? ThresholdSlotFor = null,

    /// <summary>
    /// **局内的牌地址**那个局部量的名字（临时牌集合的键的第一段，见
    /// <c>Modules/CardSets</c>）。生成代码在 <c>OnPlay</c> 开头把它开出来，
    /// 所以卡牌里它是变量名；**状态里为 null**（状态没有"一次出牌结算"这个概念，
    /// 集合只对卡牌成立——校验器会拦下状态里用集合）。
    /// </summary>
    string? SetAddress = null,

    /// <summary>
    /// "这条效果在**原始效果列表**里是第几条"（1 基，界面上"效果 N"那个号）。
    /// 临时牌集合的键要它；没注入时为 null（那时不生成集合相关的语句）。
    ///
    /// 收的是**函数**而不是每个效果一个数：同一个 <see cref="EffectDef"/> 可能被展开成
    /// 多处（共享效果被引用两次），编号按**引用点**算，不能按对象算。
    /// </summary>
    Func<EffectDef, int>? EffectNumberFor = null,

    /// <summary>
    /// "这个集合 ID 是**哪一条效果**定义的"（1 基编号，见 <c>CardSetCatalog.DefinitionNumber</c>）。
    ///
    /// 读集合时要拿定义那一条的编号去拼键，所以读写两侧靠它对上号。收函数同理：
    /// 它查的是**这张卡的原始效果列表**，与 <see cref="EffectNumberFor"/> 同一个口径。
    /// </summary>
    Func<string, int>? SetNumberFor = null)
{
    /// <summary>
    /// 召唤时的"来源"参数：卡牌里是这张牌，状态里没有牌可给，退化成状态自身。
    /// </summary>
    public string CardSourceOrSelf => CardSource == "null" ? Self : CardSource;

    public static EmitContext ForCard() => new(
        Self: "Owner.Creature",
        Player: "Owner",
        Dealer: "Owner.Creature",
        Enemies: "Owner.Creature.CombatState!.HittableEnemies",
        PlayerCreatures: "Owner.Creature.CombatState!.PlayerCreatures",
        CardSource: "this",
        CardTarget: "cardPlay.Target",
        SelfCard: "this",
        ChoiceContext: "choiceContext",
        CombatState: "Owner.Creature.CombatState!",
        Rng: "Owner.RunState.Rng.CombatTargets",
        HasDynamicVars: true);

    /// <summary>状态钩子里带 PlayerChoiceContext 的场合。</summary>
    public static EmitContext ForPower() => ForPower("choiceContext");

    /// <summary>状态钩子里不带 PlayerChoiceContext 的场合，需要自行构造（见 HookContext）。</summary>
    public static EmitContext ForPowerWithoutContext() => ForPower("HookContext()");

    private static EmitContext ForPower(string choiceContext) => new(
        Self: "Owner",
        Player: "Owner.Player!",
        Dealer: "Owner",
        Enemies: "Owner.CombatState!.HittableEnemies",
        PlayerCreatures: "Owner.CombatState!.PlayerCreatures",
        CardSource: "null",
        CardTarget: null,

        // 状态没有"这张牌"，相关效果在状态里不适用。
        SelfCard: "null",
        ChoiceContext: choiceContext,
        CombatState: "Owner.CombatState!",
        Rng: "Owner.CombatState!.RunState.Rng.CombatTargets",
        HasDynamicVars: false);
}
