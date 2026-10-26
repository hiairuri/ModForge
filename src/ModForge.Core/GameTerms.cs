using ModForge.Core.Pck;

namespace ModForge.Core;

/// <summary>
/// 界面里出现的游戏术语（卡牌类型、稀有度、关键词等）的本地化名称。
///
/// 名称直接取自游戏自己的本地化表，而不是在代码里写一份翻译：用词与游戏内一致，
/// 而且游戏更新或换语言时不用改代码。拿不到游戏目录时回退到英文枚举名。
/// </summary>
public sealed class GameTerms
{
    /// <summary>界面用到的表。键形如 "cards"，对应 localization/&lt;lang&gt;/cards.json。</summary>
    private static readonly string[] NeededTables =
    {
        "card_keywords", "gameplay_ui", "card_library", "extensions", "cards", "powers", "intents",
        "characters", "enchantments", "orbs",
    };

    /// <summary>
    /// 每个枚举值对应的查表键。格式为 "表名:键"，与游戏本地化表一致。
    /// </summary>
    private static readonly Dictionary<string, string> LookupKeys = new(StringComparer.Ordinal)
    {
        // 卡牌牌池。角色池没有独立译名，用的就是角色名；特殊池在 OwnTerms 里补充。
        ["CardPool.ironclad"] = "characters:IRONCLAD.title",
        ["CardPool.silent"] = "characters:SILENT.title",
        ["CardPool.defect"] = "characters:DEFECT.title",
        ["CardPool.regent"] = "characters:REGENT.title",
        ["CardPool.necrobinder"] = "characters:NECROBINDER.title",

        // 卡牌类型
        ["CardType.Attack"] = "extensions:EXTENSION.card.humanizedCardTypes.attack",
        ["CardType.Skill"] = "extensions:EXTENSION.card.humanizedCardTypes.skill",
        ["CardType.Power"] = "extensions:EXTENSION.card.humanizedCardTypes.power",
        ["CardType.Status"] = "extensions:EXTENSION.card.humanizedCardTypes.status",
        ["CardType.Curse"] = "extensions:EXTENSION.card.humanizedCardTypes.curse",
        ["CardType.Quest"] = "extensions:EXTENSION.card.humanizedCardTypes.quest",
        ["CardType.None"] = "extensions:EXTENSION.card.humanizedCardTypes.none",

        // 稀有度
        ["CardRarity.Basic"] = "gameplay_ui:CARD_RARITY.BASIC",
        ["CardRarity.Common"] = "gameplay_ui:CARD_RARITY.COMMON",
        ["CardRarity.Uncommon"] = "gameplay_ui:CARD_RARITY.UNCOMMON",
        ["CardRarity.Rare"] = "gameplay_ui:CARD_RARITY.RARE",
        ["CardRarity.Ancient"] = "gameplay_ui:CARD_RARITY.ANCIENT",
        ["CardRarity.Event"] = "gameplay_ui:CARD_RARITY.EVENT",
        ["CardRarity.Token"] = "gameplay_ui:CARD_RARITY.TOKEN",
        ["CardRarity.Status"] = "gameplay_ui:CARD_RARITY.STATUS",
        ["CardRarity.Curse"] = "gameplay_ui:CARD_RARITY.CURSE",
        ["CardRarity.Quest"] = "gameplay_ui:CARD_RARITY.QUEST",
        ["CardRarity.None"] = "gameplay_ui:CARD_RARITY.NONE",

        // 关键词（名称取 .title）
        ["CardKeyword.Exhaust"] = "card_keywords:EXHAUST.title",
        ["CardKeyword.Ethereal"] = "card_keywords:ETHEREAL.title",
        ["CardKeyword.Innate"] = "card_keywords:INNATE.title",
        ["CardKeyword.Retain"] = "card_keywords:RETAIN.title",
        ["CardKeyword.Sly"] = "card_keywords:SLY.title",
        ["CardKeyword.Eternal"] = "card_keywords:ETERNAL.title",
        ["CardKeyword.Unplayable"] = "card_keywords:UNPLAYABLE.title",

        // 附魔（名称取 .title，如"锋利""腐化"）。界面里的下拉直接按这个查，用词与游戏一致。
        ["Enchantment.SHARP"] = "enchantments:SHARP.title",
        ["Enchantment.ADROIT"] = "enchantments:ADROIT.title",
        ["Enchantment.CLONE"] = "enchantments:CLONE.title",
        ["Enchantment.CORRUPTED"] = "enchantments:CORRUPTED.title",
        ["Enchantment.GLAM"] = "enchantments:GLAM.title",
        ["Enchantment.GOOPY"] = "enchantments:GOOPY.title",
        ["Enchantment.IMBUED"] = "enchantments:IMBUED.title",
        ["Enchantment.INKY"] = "enchantments:INKY.title",
        ["Enchantment.INSTINCT"] = "enchantments:INSTINCT.title",
        ["Enchantment.MOMENTUM"] = "enchantments:MOMENTUM.title",
        ["Enchantment.NIMBLE"] = "enchantments:NIMBLE.title",
        ["Enchantment.PERFECT_FIT"] = "enchantments:PERFECT_FIT.title",
        ["Enchantment.ROYALLY_APPROVED"] = "enchantments:ROYALLY_APPROVED.title",
        ["Enchantment.SLITHER"] = "enchantments:SLITHER.title",
        ["Enchantment.SLUMBERING_ESSENCE"] = "enchantments:SLUMBERING_ESSENCE.title",
        ["Enchantment.SOULS_POWER"] = "enchantments:SOULS_POWER.title",
        ["Enchantment.SOWN"] = "enchantments:SOWN.title",
        ["Enchantment.SPIRAL"] = "enchantments:SPIRAL.title",
        ["Enchantment.STEADY"] = "enchantments:STEADY.title",
        ["Enchantment.SWIFT"] = "enchantments:SWIFT.title",
        ["Enchantment.TEZCATARAS_EMBER"] = "enchantments:TEZCATARAS_EMBER.title",
        ["Enchantment.VIGOROUS"] = "enchantments:VIGOROUS.title",

        // 充能球（"闪电""冰霜"；卡面写作"闪电充能球"，所以后缀由句式表补）。
        ["Orb.Lightning"] = "orbs:LIGHTNING_ORB.title",
        ["Orb.Frost"] = "orbs:FROST_ORB.title",
        ["Orb.Dark"] = "orbs:DARK_ORB.title",
        ["Orb.Plasma"] = "orbs:PLASMA_ORB.title",
        ["Orb.Glass"] = "orbs:GLASS_ORB.title",
    };

    /// <summary>
    /// ModForge 自己的概念，游戏里没有对应术语，所以在这里给出中英名称。
    ///
    /// 与上面分开，是因为这些词不可能从游戏本地化表里查到；混在一起会让人误以为
    /// 漏读了表。
    /// </summary>
    private static readonly Dictionary<string, (string En, string Zh)> OwnTerms = new(StringComparer.Ordinal)
    {
        // 特殊牌池：游戏没有统一的池名，按游戏内的说法给出。
        ["CardPool.colorless"] = ("Colorless", "无色"),
        ["CardPool.curse"] = ("Curse", "诅咒"),
        ["CardPool.status"] = ("Status", "状态"),
        ["CardPool.event"] = ("Event", "事件"),
        ["CardPool.quest"] = ("Quest", "任务"),
        ["CardPool.token"] = ("Token", "衍生"),

        // 卡牌目标
        ["CardTargetType.Self"] = ("Self", "自身"),
        ["CardTargetType.AnyEnemy"] = ("Any enemy", "任意敌人"),
        ["CardTargetType.AllEnemies"] = ("All enemies", "所有敌人"),
        ["CardTargetType.RandomEnemy"] = ("Random enemy", "随机敌人"),
        ["CardTargetType.None"] = ("None", "无目标"),
        // 多人合作那一档：单人里"任意友方"没有合法目标（游戏会直接把这张牌移进结算牌堆、
        // 不结算），"所有友方"退化成只有自己。校验器对前者会给一条提醒。
        ["CardTargetType.AnyAlly"] = ("Any ally (co-op)", "任意友方（多人合作）"),
        ["CardTargetType.AllAllies"] = ("All allies", "所有友方"),

        // 按玩家人数的限制（界面上叫「多人游戏卡牌」）。游戏在卡池与商店那一侧过滤它。
        ["CardMultiplayerConstraint.None"] = ("Both modes", "两种模式都有"),
        ["CardMultiplayerConstraint.MultiplayerOnly"] = ("Multiplayer only", "只在多人合作"),
        ["CardMultiplayerConstraint.SingleplayerOnly"] = ("Single-player only", "只在单人游戏"),

        // 卡牌标签（选牌流程七）。游戏里查不到这几个词的译名——CardTag 是**无行为的元数据**
        // （见 CardTag.cs 的注释："non-model areas … should probably never reference individual ones"），
        // 它不出现在卡面上，所以本地化表里没有它。名字按用户的叫法给。
        ["CardTag.Strike"] = ("Strike", "打击"),
        ["CardTag.Defend"] = ("Defend", "防御"),
        ["CardTag.Shiv"] = ("Shiv", "小刀"),
        ["CardTag.Minion"] = ("Minion", "仆从"),
        ["CardTag.OstyAttack"] = ("Osty attack", "奥斯提攻击"),

        // 效果的作用对象。与上面的"卡牌目标"是两套值：卡牌只需说明它指向谁，
        // 而效果还要能指"所有友方"等，所以单独一组，不能混用同一个键前缀。
        ["EffectTarget.Self"] = ("Self", "自身"),
        ["EffectTarget.Target"] = ("The target", "目标"),
        ["EffectTarget.AllEnemies"] = ("All enemies", "所有敌人"),
        ["EffectTarget.RandomEnemy"] = ("A random enemy", "随机一名敌人"),
        ["EffectTarget.AllAllies"] = ("All allies", "所有友方"),
        ["EffectTarget.RandomAlly"] = ("A random ally", "随机一名友方"),
        ["EffectTarget.AllOtherAllies"] = ("All other allies", "所有其他友方"),
        ["EffectTarget.RandomOtherAlly"] = ("A random other ally", "随机其他友方"),
        ["EffectTarget.RandomPlayer"] = ("A random player", "随机一名玩家"),
        ["EffectTarget.All"] = ("Everyone", "所有人"),

        // 激发哪些充能球（"最右侧"这类说的是**画面**上的左右，见 OrbEvokeTarget 的注释：
        // 队列的首尾与画面的左右是反的）。
        ["OrbEvokeTarget.Rightmost"] = ("Rightmost", "最右侧"),
        ["OrbEvokeTarget.Leftmost"] = ("Leftmost", "最左侧"),
        ["OrbEvokeTarget.All"] = ("All", "全部"),
        ["OrbEvokeTarget.Random"] = ("Random", "随机"),
        ["OrbKind.Lightning"] = ("Lightning", "闪电"),
        ["OrbKind.Frost"] = ("Frost", "冰霜"),
        ["OrbKind.Dark"] = ("Dark", "黑暗"),
        ["OrbKind.Plasma"] = ("Plasma", "等离子"),
        ["OrbKind.Glass"] = ("Glass", "玻璃"),

        // 状态性质
        ["PowerKind.Buff"] = ("Buff", "增益"),
        ["PowerKind.Debuff"] = ("Debuff", "减益"),

        // 状态叠加
        ["PowerStack.Counter"] = ("Counter (visible, stacks)", "计数（显示层数，可叠加）"),
        ["PowerStack.Single"] = ("Single (hidden, always 1)", "单层（隐藏，恒为 1）"),

        // 伤害影响
        ["DamageModifier.None"] = ("None", "不影响"),
        ["DamageModifier.Additive"] = ("Add damage", "增加伤害"),
        ["DamageModifier.Multiplicative"] = ("Multiply damage taken", "放大受到的伤害"),

        // 衰减
        ["DecayMode.Never"] = ("Never", "不衰减"),
        ["DecayMode.OnePerTurnEnd"] = ("Lose 1 per turn end", "每回合末失去 1 层"),
        ["DecayMode.AllAtTurnEnd"] = ("Expires at turn end", "回合末全部消失"),

        // 状态触发时机
        ["PowerTrigger.TurnStart"] = ("Start of turn", "回合开始时"),
        ["PowerTrigger.TurnEnd"] = ("End of turn", "回合结束时"),
        ["PowerTrigger.CardPlayed"] = ("When a card is played", "打出卡牌时"),
        ["PowerTrigger.DamageTaken"] = ("When damage is taken", "受到伤害时"),

        // 效果触发时机
        ["EffectTrigger.OnPlay"] = ("When played", "打出时"),
        ["EffectTrigger.AfterPlayed"] = ("After played", "打出后"),
        ["EffectTrigger.OnDraw"] = ("When drawn", "抽到时"),
        ["EffectTrigger.OnDiscard"] = ("When discarded", "被弃掉时"),
        ["EffectTrigger.OnExhaust"] = ("When exhausted", "被消耗时"),
        ["EffectTrigger.EndOfTurn"] = ("End of turn", "回合结束时"),
        ["EffectTrigger.StartOfTurn"] = ("Start of turn", "回合开始时"),
        ["EffectTrigger.CardPlayed"] = ("When a card is played", "打出一张牌时"),

        // 计数器归零范围（"打出一张牌时"专用）
        ["CounterReset.ThisTurn"] = ("Each turn", "本回合"),
        ["CounterReset.ThisCombat"] = ("Each combat", "本场战斗"),

        // 回合触发时这张牌必须待在哪个牌堆。Any 是"任意战斗牌堆"，也就是默认语义。
        ["PileScope.Any"] = ("Any pile", "任意牌堆"),
        ["PileScope.Hand"] = ("If in hand", "若在手牌中"),
        ["PileScope.DrawPile"] = ("If in the draw pile", "若在抽牌堆中"),
        ["PileScope.DiscardPile"] = ("If in the discard pile", "若在弃牌堆中"),
        ["PileScope.ExhaustPile"] = ("If in the exhaust pile", "若在消耗堆中"),

        // 数值来源取自谁（非玩家维度时可选）
        ["ValueSourceActor.Self"] = ("Self", "自身"),
        ["ValueSourceActor.Target"] = ("The target", "目标"),

        // 放入位置
        ["PilePlacement.Top"] = ("Top", "顶部"),
        ["PilePlacement.Bottom"] = ("Bottom", "底部"),
        ["PilePlacement.Random"] = ("Random", "随机位置"),
    };

    private readonly string? _gameDir;
    private readonly Dictionary<string, Dictionary<string, string>> _byLanguage = new(StringComparer.Ordinal);

    public GameTerms(string? gameDir)
    {
        _gameDir = gameDir;
    }

    /// <summary>
    /// 全部可查的键。服务端据此一次性下发，不必在两处各维护一份列表。
    /// </summary>
    public static IReadOnlyCollection<string> AllKeys { get; } =
        LookupKeys.Keys.Concat(OwnTerms.Keys).Distinct(StringComparer.Ordinal).ToList();

    /// <summary>界面是否拿到了游戏术语表（拿不到时用英文枚举名，界面仍可用）。</summary>
    public bool HasTerms(string language) => Load(language).Count > 0;

    /// <summary>把 "CardType.Attack" 这样的键换成该语言的名称。</summary>
    public string? Lookup(string enumKey, string language)
    {
        string? code = GameLanguages.Normalize(language);
        if (code is null)
        {
            return null;
        }

        bool zh = code.StartsWith("zh", StringComparison.OrdinalIgnoreCase);

        // ModForge 自己的概念先查（游戏表里没有这些词）。
        if (OwnTerms.TryGetValue(enumKey, out (string En, string Zh) own))
        {
            return zh ? own.Zh : own.En;
        }

        if (!LookupKeys.TryGetValue(enumKey, out string? target))
        {
            return null;
        }

        int colon = target.IndexOf(':');
        string table = target[..colon];
        string key = target[(colon + 1)..];

        Dictionary<string, string> tables = Load(code);
        return tables.TryGetValue($"{table}\0{key}", out string? value) ? value : null;
    }

    /// <summary>按需读入某语言用到的几张表，读过一次就缓存。</summary>
    private Dictionary<string, string> Load(string language)
    {
        if (_byLanguage.TryGetValue(language, out Dictionary<string, string>? cached))
        {
            return cached;
        }

        var merged = new Dictionary<string, string>(StringComparer.Ordinal);

        if (_gameDir is not null)
        {
            try
            {
                string pck = Path.Combine(_gameDir, "SlayTheSpire2.pck");
                if (File.Exists(pck))
                {
                    var reader = new PckLocReader(pck);

                    foreach (string table in NeededTables)
                    {
                        foreach ((string k, string v) in reader.LoadTable(language, table))
                        {
                            // 带上表名，避免不同表里的同名键互相覆盖。
                            merged[$"{table}\0{k}"] = v;
                        }
                    }
                }
            }
            catch (Exception)
            {
                // 读不到时留空，调用方会回退到英文枚举名。
            }
        }

        _byLanguage[language] = merged;
        return merged;
    }
}
