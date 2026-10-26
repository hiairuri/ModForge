using System.Text.Json.Serialization;

namespace ModForge.Core.Model;

/// <summary>Vanilla card types.</summary>
public enum CardType { Attack, Skill, Power, Status, Curse, Quest }

/// <summary>Vanilla rarities. Only Common/Uncommon/Rare appear in rewards and shops.</summary>
public enum CardRarity { Basic, Common, Uncommon, Rare, Ancient, Event, Token, Status, Curse, Quest }

/// <summary>
/// Vanilla targeting modes（对应游戏的 <c>TargetType</c>，**名字必须逐字相同**：
/// 出码是 <c>TargetType.{值}</c> 的直接映射）。
///
/// 只列这个编辑器用得上的那些。游戏里另有三个刻意不收：
/// <c>AnyPlayer</c>（单人里等同于 <see cref="Self"/>：不选目标、打给自己）、
/// <c>TargetedNoCreature</c>（原版只给"对着商人用的药剂"这一处 hack）、
/// <c>Osty</c>（只对死灵师的宠物有意义）。收了它们等于给用户三条没有用处的岔路。
///
/// <see cref="AnyAlly"/> 与 <see cref="AllAllies"/> 是**多人合作**那一档：单人游戏里
/// 前者没有合法目标（游戏会直接把这张牌移进结算牌堆、不结算），后者退化成"只有自己"。
/// 校验器对前者会给一条提醒。
///
/// **<c>AnyPlayer</c> 与 <c>Osty</c> 刻意不收**，理由不是"没用"而是"对卡牌不成立"：
/// 游戏的 <c>CardModel.IsValidTarget</c> 写死只有 <c>AnyEnemy</c>/<c>AnyAlly</c> 能带目标，
/// 其余档必须以"无目标"打出——而 <c>AnyPlayer</c> 的语义本要"选一名玩家"，
/// 真传了目标反而非法（<c>PlayCardAction</c> 会把这张牌判为非法目标而取消）。
/// <c>Osty</c> 同理：它有界面（原版会高亮自己那只奥斯提），但原版**一张卡都没用**，
/// 而且我们表达"打宠物"的方式是**目标选择器里的**「目标的奥斯提」那一组（见 <c>EffectTarget.TargetPet</c>），
/// 那是随效果走、比卡牌目标精确得多（一张牌可以既打敌人又强化自己的奥斯提）。
/// </summary>
public enum CardTargetType { Self, AnyEnemy, AllEnemies, RandomEnemy, None, AnyAlly, AllAllies }

/// <summary>
/// 卡牌按**玩家人数**的限制（对应游戏的 <c>CardMultiplayerConstraint</c>，名字逐字相同——
/// 出码就是 <c>CardMultiplayerConstraint.{值}</c> 的直接映射）。
///
/// 游戏在**生成与商店**那一侧过滤它（<c>CardFactory</c> / <c>CardPoolModel.GetUnlockedCards</c> /
/// <c>MerchantInventory</c>）：<see cref="MultiplayerOnly"/> 的牌单人游戏里根本不会出现
/// （原版"我相信你""恶魔之盾""生命赞歌"这类需要队友的牌就是它）。
///
/// 与"卡牌目标"是两件事：这一档管"这张牌会不会出现在单人游戏里"，
/// 而目标管"打出去指向谁"。一张多人牌通常两样都要设（原版那几张就是既
/// <c>MultiplayerOnly</c>、目标又是"任意友方"）。
/// </summary>
public enum CardMultiplayerConstraint { None, MultiplayerOnly, SingleplayerOnly }

/// <summary>Vanilla keywords.</summary>
public enum CardKeyword { Exhaust, Ethereal, Innate, Retain, Sly, Eternal, Unplayable }

/// <summary>
/// 卡牌**标签**（游戏的 <c>CardTag</c>）。
///
/// 与 <see cref="CardKeyword"/> 是两套东西，不能混为一谈（我早先把它们并进了一个枚举，那错了）：
///   标签   —— 无关行为的元数据，问的是"这张牌算不算打击牌"（原版"完美打击"数打击牌、
///             "精准"加成小刀）。游戏里读 <c>c.Tags.Contains(CardTag.X)</c>。
///   关键词 —— 带自动行为的机制标记（消耗、虚无、固有…），游戏里读
///             <c>c.Keywords.Contains(CardKeyword.Y)</c>，而且与遗物/能力牌的改写叠加。
///
/// 分开还因为它们不是同一个集合接口：游戏里 <c>Tags</c> 是 <c>IEnumerable&lt;CardTag&gt;</c>、
/// <c>Keywords</c> 是 <c>IReadOnlySet&lt;CardKeyword&gt;</c>。
/// </summary>
public enum CardTag { Strike, Defend, Shiv, Minion, OstyAttack }

/// <summary>A playable card definition.</summary>
public sealed class CardDef
{
    /// <summary>
    /// Identifier used for the class name and localization keys. Uppercase with
    /// underscores, e.g. FORGE_STRIKE, which makes the model id CARD.FORGE_STRIKE.
    /// Must not collide with existing content.
    /// </summary>
    public string Id { get; set; } = "NEW_CARD";

    /// <summary>卡牌名，按语言分别填写。未填的语言由游戏回退到英语。</summary>
    public LocalizedText Name { get; set; } = new(GameLanguages.Fallback, "New Card");

    /// <summary>
    /// 描述模板，按语言分别填写。数值用游戏自己的 SmartFormat 语法引用，例如
    /// "Deal {Damage:diff()} damage."；引用某个状态的数值时用它生成的类型名，例如
    /// {ForgeMightPower:diff()}。
    ///
    /// 不同语言的描述可以引用不同的数值，所以每种语言都会单独检查。
    /// </summary>
    public LocalizedText Description { get; set; } = new(GameLanguages.Fallback, "Deal {Damage:diff()} damage.");

    /// <summary>Name of the vanilla card pool this card joins, e.g. "ironclad".</summary>
    public string Pool { get; set; } = "ironclad";

    public CardType Type { get; set; } = CardType.Attack;

    public CardRarity Rarity { get; set; } = CardRarity.Common;

    public CardTargetType Target { get; set; } = CardTargetType.AnyEnemy;

    /// <summary>
    /// 按玩家人数的限制（界面上叫「多人游戏卡牌」）。默认 <c>None</c>＝两种模式都能出现。
    /// 见 <see cref="CardMultiplayerConstraint"/>。
    /// </summary>
    public CardMultiplayerConstraint Multiplayer { get; set; } = CardMultiplayerConstraint.None;

    public int EnergyCost { get; set; } = 1;

    /// <summary>-1 means the card has no star cost, which is the vanilla default.</summary>
    public int StarCost { get; set; } = -1;

    /// <summary>
    /// 费用是 X：打出时消耗全部能量，效果按消耗量结算。
    ///
    /// 注意 X 费卡打出时能量已经被扣掉了，所以效果里不能用"当前能量"取值（读到的是 0），
    /// 要用"X 值"这个来源（对应游戏的 ResolveEnergyXValue）。
    /// </summary>
    public bool EnergyCostX { get; set; }

    /// <summary>费用是 X 的星辰版（打出时消耗全部星辰）。</summary>
    public bool StarCostX { get; set; }

    /// <summary>
    /// 这张牌**额外打出几次**（0 = 正常打一次，1 = 打两次…）。
    ///
    /// 对应 <c>CardModel.BaseReplayCount</c>——它是**卡牌自己的属性**，与费用、关键词并列，
    /// 不是某条效果的事：它改的是"这张牌被打出时执行几遍"。
    ///
    /// 与另一条同名的效果区分：效果版的 <see cref="EffectKind.AddReplay"/> 是"给**别的**牌
    /// 加重放"，这里的是"这张牌自己天生多重放"。
    ///
    /// 生成时发成一个 **[SavedProperty] 自有属性**（setter 把它写进 BaseReplayCount）：
    /// 游戏只存「id + 升级等级 + 该类自己声明的 SavedProperty + 附魔」，而 BaseReplayCount
    /// 是个普通字段、不进存档——不自己记一份的话读档就丢了。原版 SpoilsMap 是同样的写法。
    /// 卡面的"重放 N 次"悬停说明由游戏自己根据这个字段加（见 CardModel 的 ExtraCardText）。
    /// </summary>
    public int ReplayCount { get; set; }

    public List<CardKeyword> Keywords { get; set; } = new();

    /// <summary>Cost change applied on upgrade, e.g. -1 to make it cheaper.</summary>
    public int UpgradeCostDelta { get; set; }

    /// <summary>
    /// 升级时**星辰费用**的变化量（增量语义，通常为负）。
    ///
    /// 对应游戏的 <c>CardModel.UpgradeStarCostBy(addend)</c>，它的文档写明"meant to be called
    /// in OnUpgrade"。原版**没有一张卡用过它**（星费卡升级改的都是伤害），所以这条路没有
    /// 原版对照；但接口本身是游戏提供的，且 <see cref="StarCostX"/> 时调用它会抛异常
    /// （游戏源码里写死），校验器会拦下那种组合。
    /// </summary>
    public int UpgradeStarDelta { get; set; }

    /// <summary>
    /// 升级上限。默认 1（升一次），设为 2 或更多就是**多次升级**——卡名会显示 +2、+3，
    /// 每级的数值由各效果的 <see cref="EffectDef.Levels"/> 给出。设为 0 表示不可升级
    /// （诅咒牌就是这种）。
    ///
    /// 游戏侧是 <c>CardModel.MaxUpgradeLevel</c>，可覆写，我们的生成类照它发出去。
    /// 原版 578 张里**一张多级升级的牌都没有**（`MaxUpgradeLevel` 只有诅咒牌用到，
    /// 且都设 0），所以这条路没有原版对照可验证。
    /// </summary>
    public int MaxUpgradeLevel { get; set; } = 1;

    /// <summary>
    /// Everything the card does, in order. This is the single source of truth for the
    /// card's numbers: damage, block and power amounts are all read back from here rather
    /// than stored separately, so there is nothing to keep in sync.
    ///
    /// An entry may be an inline effect or a reference to a shared one.
    /// </summary>
    public List<EffectDef> Effects { get; set; } = new();

    /// <summary>Optional art file placed next to the mod, loaded at runtime.</summary>
    public string? ArtFile { get; set; }

    /// <summary>
    /// True when the card changes on upgrade. Each effect owns its own upgrade delta (or
    /// its own per-level table), so that is the single place an upgrade is expressed.
    /// </summary>
    [JsonIgnore]
    public bool Upgrades => MaxUpgradeLevel > 0
        && (UpgradeCostDelta != 0 || Effects.Any(e => e.Upgrade.IsSet || e.HasLevels));
}
