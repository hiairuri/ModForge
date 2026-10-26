using ModForge.Core.Model;

namespace ModForge.Core.Modules.Effects;

/// <summary>
/// 效果种类 → 行为的**唯一**查表处（替代原来生成器 / 描述 / 界面里那三处 switch）。
///
/// 搬迁是**逐批**进行的（见 README 的效果模块顺序表），所以没搬的种类在这里没有条目——
/// 调用方用 <see cref="Find"/> 拿到 null 时退回原处的旧代码。一批搬完就删掉那一批的旧分支，
/// 最后这里覆盖全部 30 种、三处 switch 随之消失。
///
/// 类本身公开（验收工具要按种类断言元数据），但 <see cref="Find"/> 只给本程序集用——
/// 对外只需知道"某个种类该怎么在界面上呈现"。
/// </summary>
public static class EffectCatalog
{
    private static readonly Dictionary<EffectKind, IEffectBehavior> Behaviors = new()
    {
        // 批 1：最简单的六种（无变量槽、无选牌、无校验）。
        [EffectKind.GainEnergy] = new GainEnergyBehavior(),
        [EffectKind.GainStars] = new GainStarsBehavior(),
        [EffectKind.AddOrbSlots] = new AddOrbSlotsBehavior(),
        [EffectKind.ExhaustCards] = new ExhaustCardsBehavior(),
        [EffectKind.ChannelOrb] = new ChannelOrbBehavior(),
        [EffectKind.EvokeOrbs] = new EvokeOrbsBehavior(),

        // 批 2：共用形状家族（"把一个数值作用于目标"）——伤害 / 格挡 / 回复 / 失去生命 /
        // 两个最大生命。它们的出码只差那句命令与属性，目标循环留在共用层。
        [EffectKind.DealDamage] = new DealDamageBehavior(),
        [EffectKind.GainBlock] = new GainBlockBehavior(),
        [EffectKind.Heal] = new HealBehavior(),
        [EffectKind.LoseHp] = new LoseHpBehavior(),
        [EffectKind.GainMaxHp] = new GainMaxHpBehavior(),
        [EffectKind.LoseMaxHp] = new LoseMaxHpBehavior(),

        // 批 3：四种各成一类，不共用形状——抽牌（牌堆命令）、铸造（储君的君王之剑）、
        // 召唤（先播施法动画再召唤）、立即执行灾厄判定（判定规则挂在灾厄状态自己身上）。
        [EffectKind.DrawCards] = new DrawCardsBehavior(),
        [EffectKind.Forge] = new ForgeBehavior(),
        [EffectKind.Summon] = new SummonBehavior(),
        [EffectKind.TriggerDoom] = new TriggerDoomBehavior(),

        // 批 4：状态这一族。施加只有一个动作（PowerCmd.Apply）；移除有**四条路径**
        // （整层/减层 × 指定那一个/所有正面·负面），见 RemovePowerBehavior。
        [EffectKind.ApplyPower] = new ApplyPowerBehavior(),
        [EffectKind.RemovePower] = new RemovePowerBehavior(),

        // 批 5：牌堆生成与再分配。加入指定卡牌有两种生成方式（指定牌 / 从卡池 N 选一，
        // 见 AddCardMode）；丢弃与自动打出都是"挑出来的牌逐个挪走"，
        // 但自动打出的重复要作用在"打出"上、不重新取牌堆（见 AutoPlayBehavior）。
        [EffectKind.AddNamedCardToPile] = new AddNamedCardToPileBehavior(),
        [EffectKind.DiscardCardsInPile] = new DiscardCardsBehavior(),
        [EffectKind.AutoPlay] = new AutoPlayBehavior(),

        // 批 6：选牌类六种——都是"选牌器挑一批牌 → 逐个做一件事"，出码只有最后那句不同。
        // 升级 / 变化 / 搬运各带一个原版特例（FromHandForUpgrade 的预览、指定牌与随机的
        // 两个入口、"此牌"那一句独立的话）。
        [EffectKind.CopyCardsToPile] = new CopyCardsToPileBehavior(),
        [EffectKind.ModifyCardCost] = new ModifyCardCostBehavior(),
        [EffectKind.SetCardsFree] = new SetCardsFreeBehavior(),
        [EffectKind.UpgradeCardsInPile] = new UpgradeCardsInPileBehavior(),
        [EffectKind.MoveCardsBetweenPiles] = new MoveCardsBetweenPilesBehavior(),
        [EffectKind.TransformCardsInPile] = new TransformCardsInPileBehavior(),

        // 批 7：改造类收尾（最难的三种最后搬）。附魔的"层数 vs 张数"与 CanEnchant 的
        // 黑名单、增伤与重放的"战斗中改字段 / 永久写存档"双路径，见各自文件。
        [EffectKind.EnchantCardsInPile] = new EnchantCardsInPileBehavior(),
        [EffectKind.BoostCardDamage] = new BoostCardDamageBehavior(),
        [EffectKind.AddReplay] = new AddReplayBehavior(),
    };

    /// <summary>这条效果的行为；还没搬过来的种类返回 null（调用方退回旧路径）。</summary>
    internal static IEffectBehavior? Find(EffectKind kind) =>
        Behaviors.TryGetValue(kind, out IEffectBehavior? behavior) ? behavior : null;

    /// <summary>这个种类是否已经搬进 <c>Kinds/</c>。</summary>
    public static bool IsMigrated(EffectKind kind) => Behaviors.ContainsKey(kind);

    /// <summary>
    /// 这条效果的作用域（造物 / 玩家 / 牌）——决定它能选哪几档目标、以及目标落到什么上。
    ///
    /// 还没搬进 <c>Kinds/</c> 的种类按"作用于牌"算：它们全是作用于牌的
    /// （谁受影响由选牌器决定，与生物无关）。出码那一侧（<c>EffectEmitter</c>）与校验器
    /// 都问这里，所以"这条效果属于哪个作用域"只有一处答案。
    /// </summary>
    public static TargetScope ScopeOf(EffectKind kind) =>
        Find(kind)?.Schema.scope ?? TargetScope.Cards;

    /// <summary>
    /// 界面元数据。两件事在这里统一做，行为文件里就不必操心：
    ///
    ///   <c>kind</c> 由这里填 —— 从字典的键来，所以行为不可能把自己标错。
    ///   三个数组字段的 null 归一成空数组 —— 界面与旧行为都拿到数组，
    ///   序列化出来是 <c>[]</c> 而不是 <c>null</c>（与搬迁前逐字节一致）。
    ///
    /// 只在 <see cref="IsMigrated"/> 为真时调；没搬的种类用 <c>EditorSchema</c> 的旧写法。
    /// </summary>
    public static EffectSchemaEntry SchemaOf(EffectKind kind)
    {
        EffectSchemaEntry schema = Behaviors[kind].Schema;

        return schema with
        {
            kind = kind.ToString(),
            durations = schema.durations ?? Array.Empty<string>(),
        };
    }
}
