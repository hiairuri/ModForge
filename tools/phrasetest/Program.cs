using ModForge.Core.Model;
using ModForge.Core.Modules.CardPicking;
using ModForge.Core.Modules.Descriptions;
using ModForge.Core.Modules.Variables;
using ModForge.Core.Modules.Effects;

// Prints the auto-generated sentence for every effect shape, in every language that has a
// sentence file. This is the regression check for language/sentences/*.json: descriptions
// are pure data now, so a wording mistake shows up here without touching the editor or the game.
//
// Usage: phrasetest            (all languages found in language/sentences/)
//        phrasetest zhs eng    (only these)

// 语言代码是参数；最后一个参数若含路径分隔符则是游戏目录。
string[] requested = args.Where(a => !a.Contains('\\') && !a.Contains('/')).ToArray();

if (requested.Length == 0)
{
    requested = SentenceLibrary.AvailableLanguages().ToArray();
}

if (requested.Length == 0)
{
    Console.Error.WriteLine($"No sentence files found. Expected them in {SentenceLibrary.Folder}.");
    return 1;
}

Console.WriteLine($"sentences: {SentenceLibrary.Folder}");
Console.WriteLine($"languages: {string.Join(", ", requested)}");

// 原版卡名/状态名的中文叫法要读游戏本地化表；找不到游戏就退回模型 id 的整理形式
// （那会让"打击"显示成 StrikeIronclad），所以能连上就连上。
string gameDir = args.FirstOrDefault(a => a.Contains('\\') || a.Contains('/'))
    ?? @"D:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2";

if (Directory.Exists(Path.Combine(gameDir, "data_sts2_windows_x86_64")))
{
    DescriptionModule.Names = new VanillaNames(gameDir);
    Console.WriteLine($"vanilla names: {gameDir}");
}
else
{
    Console.WriteLine("vanilla names: (not found, card/power names fall back to model ids)");
}

var project = new ModProject();

// 一个本项目自定义的状态，用来检查状态名嵌入句子的效果。
project.Powers.Add(new PowerDef
{
    Id = "MIGHT",
    Name = new LocalizedText("eng", "Might").With("zhs", "伟力"),
    Kind = PowerKind.Buff,
});

static EffectDef Fixed(EffectKind kind, EffectTarget target, int amount) => new()
{
    Kind = kind,
    Target = target,
    Amount = amount,
};

static EffectDef Calc(
    EffectKind kind,
    EffectTarget target,
    ValueSourceKind source,
    int plus,
    decimal perUnit = 1m,
    ValueSourceActor actor = ValueSourceActor.Self,
    string? powerId = null) => new()
{
    Kind = kind,
    Target = target,

    // plus 就是这个计算型的基础值。填 0 就是"纯系数乘法"，句子里不会出现" + N"。
    // （有来源就是计算型，没有单独的"数值方式"。）
    Amount = plus,
    Source = new ValueSource { Actor = actor, Kind = source, PerUnit = perUnit, PowerId = powerId },
};

void Row(string label, EffectDef effect)
{
    Console.WriteLine();
    Console.WriteLine($"  {label}");
    foreach (string language in requested)
    {
        Console.WriteLine($"      {language,-4} {DescriptionModule.Describe(project, effect, language)}");
    }
}

foreach (string language in requested)
{
    Console.WriteLine();
    Console.WriteLine($"===== {ModForge.Core.GameLanguages.DisplayName(language, "zh")} =====");

    Console.WriteLine();
    Console.WriteLine("-- 固定值 --");
    Row("造成 6 点伤害（目标）", Fixed(EffectKind.DealDamage, EffectTarget.Target, 6));
    Row("造成 6 点伤害（全体敌人）", Fixed(EffectKind.DealDamage, EffectTarget.AllEnemies, 6));
    Row("获得 5 点格挡", Fixed(EffectKind.GainBlock, EffectTarget.Self, 5));
    Row("抽 2 张牌", Fixed(EffectKind.DrawCards, EffectTarget.Self, 2));
    Row("获得 1 点能量", Fixed(EffectKind.GainEnergy, EffectTarget.Self, 1));
    Row("回复 3 点生命", Fixed(EffectKind.Heal, EffectTarget.Self, 3));
    Row("失去 2 点生命", Fixed(EffectKind.LoseHp, EffectTarget.Self, 2));

    // 新加的两档目标（随机一名友方 / 所有人）——造物轴那一族的句子要念出对象来。
    Row("对随机一名友方造成 4 点伤害", Fixed(EffectKind.DealDamage, EffectTarget.RandomAlly, 4));

    // "其他"那一族（不含自己）：造物轴读"其他友方"、玩家轴读"其他玩家"——
    // 名字里带"其他"的两档就是这一族（友方含自己、其他不含自己）。
    Row("使所有其他友方获得 2 点格挡", Fixed(EffectKind.GainBlock, EffectTarget.AllOtherAllies, 2));
    Row("对随机其他友方造成 3 点伤害", Fixed(EffectKind.DealDamage, EffectTarget.RandomOtherAlly, 3));
    Row("使所有其他玩家获得 1 点能量", Fixed(EffectKind.GainEnergy, EffectTarget.AllOtherAllies, 1));
    Row("使随机其他玩家抽 1 张牌", Fixed(EffectKind.DrawCards, EffectTarget.RandomOtherAlly, 1));
    Row("对所有人造成 3 点伤害", Fixed(EffectKind.DealDamage, EffectTarget.All, 3));

    // 玩家轴：作用于别人时要念出"谁"（不然"给队友抽 2 张牌"会写成"抽 2 张牌"），
    // 而且**按轴读**——"所有友方"在玩家轴上落地的是玩家，所以读作"所有玩家"；
    // "目标"在玩家轴上是选中的那名队友，读作"任意一名友方"。
    Row("使所有玩家抽 2 张牌", Fixed(EffectKind.DrawCards, EffectTarget.AllAllies, 2));
    Row("使随机一名友方获得 1 点能量", Fixed(EffectKind.GainEnergy, EffectTarget.RandomAlly, 1));
    Row("使随机一名玩家获得 1 点能量", Fixed(EffectKind.GainEnergy, EffectTarget.RandomPlayer, 1));
    Row("使任意一名友方获得 2 颗星辰", Fixed(EffectKind.GainStars, EffectTarget.Target, 2));

    // "作用于其奥斯提"（宠物）那一层：自己那一档本来不念对象，配上宠物必须念出
    // "自己的奥斯提"；其余各档在词后接"的奥斯提"（英文是 's / 复数 ' 的区别）。
    // 玩家宠物那一组：它是**独立的目标档**（不是"某一档 + 勾一个取其宠物"），
    // 四档各有各的说法（自己的 / 目标的 / 所有友方的 / 随机一名友方的奥斯提）。
    Row("对自己的奥斯提施加 2 层伟力", new EffectDef
    {
        Kind = EffectKind.ApplyPower, Target = EffectTarget.SelfPet,
        PowerId = "POWER.STRENGTH_POWER", Amount = 2,
    });
    Row("对所有友方的奥斯提施加 1 层伟力", new EffectDef
    {
        Kind = EffectKind.ApplyPower, Target = EffectTarget.AllAlliesPet,
        PowerId = "POWER.STRENGTH_POWER", Amount = 1,
    });
    Row("为目标的奥斯提回复 4 点生命", new EffectDef
    {
        Kind = EffectKind.Heal, Target = EffectTarget.TargetPet, Amount = 4,
    });
    Row("使随机一名友方的奥斯提提高 5 点最大生命", new EffectDef
    {
        Kind = EffectKind.GainMaxHp, Target = EffectTarget.RandomAllyPet, Amount = 5,
    });
    Row("使所有其他玩家的奥斯提提高 2 点最大生命", new EffectDef
    {
        Kind = EffectKind.GainMaxHp, Target = EffectTarget.AllOtherAlliesPet, Amount = 2,
    });
    Row("为随机其他玩家的奥斯提回复 3 点生命", new EffectDef
    {
        Kind = EffectKind.Heal, Target = EffectTarget.RandomOtherAllyPet, Amount = 3,
    });

    // 其余"对奥斯提有效"的造物轴效果（用户报的缺口：它们的句子原来不念对象）。
    Row("使所有友方的奥斯提降低 2 点最大生命", new EffectDef
    {
        Kind = EffectKind.LoseMaxHp, Target = EffectTarget.AllAlliesPet, Amount = 2,
    });
    Row("使随机一名友方的奥斯提失去 3 点生命", new EffectDef
    {
        Kind = EffectKind.LoseHp, Target = EffectTarget.RandomAllyPet, Amount = 3,
    });
    Row("移除所有友方的奥斯提的伟力", new EffectDef
    {
        Kind = EffectKind.RemovePower, Target = EffectTarget.AllAlliesPet,
        PowerId = "POWER.STRENGTH_POWER",
    });
    Row("使目标的奥斯提的力量减少 2 层", new EffectDef
    {
        Kind = EffectKind.RemovePower, Target = EffectTarget.TargetPet,
        PowerId = "POWER.STRENGTH_POWER", Removal = PowerRemoval.Partial, Amount = 2,
    });
    Row("立即对随机一名友方的奥斯提执行灾厄判定", new EffectDef
    {
        Kind = EffectKind.TriggerDoom, Target = EffectTarget.RandomAllyPet,
    });

    // 灾厄判定作用于**自己**时不能念 {who}（空词），所以另有一条不念对象的说法。
    Row("立即执行灾厄判定（自己）", new EffectDef
    {
        Kind = EffectKind.TriggerDoom, Target = EffectTarget.Self,
    });

    Row("使所有玩家获得 1 个充能球栏位", Fixed(EffectKind.AddOrbSlots, EffectTarget.AllAllies, 1));
    Row("使所有玩家召唤 4", Fixed(EffectKind.Summon, EffectTarget.AllAllies, 4));
    Row("使所有玩家铸造 2 点", Fixed(EffectKind.Forge, EffectTarget.AllAllies, 2));
    Row("使所有玩家生成 1 个闪电充能球", new EffectDef
    {
        Kind = EffectKind.ChannelOrb,
        Target = EffectTarget.AllAllies,
        Amount = 1,
        Orb = OrbKind.Lightning,
    });

    Console.WriteLine();
    Console.WriteLine("-- 计算型（无基础值）--");
    Row("造成 等于 手牌数 的伤害",
        Calc(EffectKind.DealDamage, EffectTarget.AllEnemies, ValueSourceKind.HandCount, 0));
    Row("获得 等于 自身力量 的格挡",
        Calc(EffectKind.GainBlock, EffectTarget.Self, ValueSourceKind.PowerStacks, 0, powerId: "POWER.STRENGTH_POWER"));
    Row("获得 等于 目标格挡 的格挡",
        Calc(EffectKind.GainBlock, EffectTarget.Self, ValueSourceKind.Block, 0,
            actor: ValueSourceActor.Target));
    Row("获得 等于 2 × 目标格挡 的格挡",
        Calc(EffectKind.GainBlock, EffectTarget.Self, ValueSourceKind.Block, 0, 2m,
            actor: ValueSourceActor.Target));

    Console.WriteLine();
    Console.WriteLine("-- 计算型（带基础值）：等于 手牌数 + 6 的 --");
    Row("造成 等于 手牌数 + 6 的伤害",
        Calc(EffectKind.DealDamage, EffectTarget.AllEnemies, ValueSourceKind.HandCount, 6));
    Row("回复 等于 目标格挡 + 3 的生命",
        Calc(EffectKind.Heal, EffectTarget.Target, ValueSourceKind.Block, 3,
            actor: ValueSourceActor.Target));

    Console.WriteLine();
    Console.WriteLine("-- 状态 --");
    Row("获得 3 层伟力", new EffectDef
    {
        Kind = EffectKind.ApplyPower,
        Target = EffectTarget.Self,
        Amount = 3,
        PowerId = "POWER.MIGHT",
    });
    Row("对目标施加 2 层伟力", new EffectDef
    {
        Kind = EffectKind.ApplyPower,
        Target = EffectTarget.Target,
        Amount = 2,
        PowerId = "POWER.MIGHT",
    });
    Row("获得 等于 自身力量 的伟力", new EffectDef
    {
        Kind = EffectKind.ApplyPower,
        Target = EffectTarget.Self,
        Amount = 0,
        PowerId = "POWER.MIGHT",
        Source = new ValueSource { Actor = ValueSourceActor.Self, Kind = ValueSourceKind.PowerStacks, PowerId = "POWER.STRENGTH_POWER" },
    });
    Row("移除伟力", new EffectDef
    {
        Kind = EffectKind.RemovePower,
        Target = EffectTarget.Self,
        PowerId = "POWER.MIGHT",
    });

    // 移除状态的两条轴：移除方式（整个 / 只减层数）× 移除哪一类（按 id / 正面 / 负面）。
    // 六格各一句——**没走到的那一格等于没验**，所以这里逐格都给对照行。
    Row("移除伟力 2 层", new EffectDef
    {
        Kind = EffectKind.RemovePower,
        Target = EffectTarget.Self,
        PowerId = "POWER.MIGHT",
        Removal = PowerRemoval.Partial,
        Amount = 2,
    });
    Row("移除伟力 等于 手牌数 + 1 层", new EffectDef
    {
        Kind = EffectKind.RemovePower,
        Target = EffectTarget.Target,
        PowerId = "POWER.MIGHT",
        Removal = PowerRemoval.Partial,
        Source = new ValueSource { Actor = ValueSourceActor.Self, Kind = ValueSourceKind.HandCount, PerUnit = 1 },
        Amount = 1,
    });
    Row("移除所有正面效果", new EffectDef
    {
        Kind = EffectKind.RemovePower,
        Target = EffectTarget.Self,
        Selection = PowerSelection.Buff,
    });
    Row("移除所有负面效果", new EffectDef
    {
        Kind = EffectKind.RemovePower,
        Target = EffectTarget.Self,
        Selection = PowerSelection.Debuff,
    });
    Row("移除所有正面效果各 2 层", new EffectDef
    {
        Kind = EffectKind.RemovePower,
        Target = EffectTarget.Self,
        Selection = PowerSelection.Buff,
        Removal = PowerRemoval.Partial,
        Amount = 2,
    });
    Row("移除所有负面效果各 等于 手牌数 + 1 层", new EffectDef
    {
        Kind = EffectKind.RemovePower,
        Target = EffectTarget.Target,
        Selection = PowerSelection.Debuff,
        Removal = PowerRemoval.Partial,
        Source = new ValueSource { Actor = ValueSourceActor.Self, Kind = ValueSourceKind.HandCount, PerUnit = 1 },
        Amount = 1,
    });

    Console.WriteLine();
    Console.WriteLine("-- 牌堆操作 --");
    // "将此牌置于牌堆"已并入"在牌堆间移动牌"（作用对象 = 此牌）。
    Row("置入手牌", new EffectDef
    {
        Kind = EffectKind.MoveCardsBetweenPiles,
        Target = EffectTarget.Self,
        Pick = CardPick.This(),
        TargetPile = PileKind.Hand,
    });
    Row("置于抽牌堆顶", new EffectDef
    {
        Kind = EffectKind.MoveCardsBetweenPiles,
        Target = EffectTarget.Self,
        Pick = CardPick.This(),
        TargetPile = PileKind.DrawPile,
        Placement = PilePlacement.Top,
    });
    Row("置于弃牌堆底", new EffectDef
    {
        Kind = EffectKind.MoveCardsBetweenPiles,
        Target = EffectTarget.Self,
        Pick = CardPick.This(),
        TargetPile = PileKind.DiscardPile,
        Placement = PilePlacement.Bottom,
    });
    Row("消耗这张牌", new EffectDef
    {
        Kind = EffectKind.ExhaustCards,
        Target = EffectTarget.Self,
        Pick = CardPick.This(),
    });

    // 复制牌：作用对象决定"复制谁"（此牌 / 某个牌堆 / 指定牌名 / 按条件）。
    Row("复制此牌 3 份加入手牌", new EffectDef
    {
        Kind = EffectKind.CopyCardsToPile,
        Target = EffectTarget.Self,
        Pick = CardPick.This(),
        Amount = 3,
        TargetPile = PileKind.Hand,
    });
    Row("复制 1 张手牌中的牌加入抽牌堆顶", new EffectDef
    {
        Kind = EffectKind.CopyCardsToPile,
        Target = EffectTarget.Self,
        Pick = CardPick.InPile(PileKind.Hand, PickMode.Choose, 1),
        Amount = 1,
        TargetPile = PileKind.DrawPile,
        Placement = PilePlacement.Top,
    });
    Row("加入 3 张打击到手牌", new EffectDef
    {
        Kind = EffectKind.AddNamedCardToPile,
        Target = EffectTarget.Self,
        
        Amount = 3,
        CardId = "CARD.STRIKE_IRONCLAD",
    Pick = CardPick.InPile(PileKind.Hand),
    });
    Row("消耗 2 张手牌中的牌", new EffectDef
    {
        Kind = EffectKind.ExhaustCards,
        Target = EffectTarget.Self,
        
        
        Pick = CardPick.InPile(PileKind.Hand, PickMode.Choose, 2),
    });
    // 来源是**临时牌集合**时，句子里说"刚才那个集合"而不是集合 ID——
    // 卡面是给人读的，ID 是给机器对键用的（写"集合 A"读者也不知道 A 是哪几张）。
    Row("消耗 2 张刚才那个集合里的牌", new EffectDef
    {
        Kind = EffectKind.ExhaustCards,
        Target = EffectTarget.Self,
        Amount = 2,
        Pick = new CardPick { Source = PickSource.Set, SetId = "Drawn", Mode = PickMode.Choose },
    });
    Row("丢弃 2 张手牌中的牌", new EffectDef
    {
        Kind = EffectKind.DiscardCardsInPile,
        Target = EffectTarget.Self,
        
        Amount = 2,
    Pick = CardPick.InPile(PileKind.Hand),
    });

    Console.WriteLine();
    Console.WriteLine("-- 充能球 / 召唤 / 附魔 / 伤害增益 --");
    foreach (OrbKind orb in Enum.GetValues<OrbKind>())
    {
        Row($"生成 2 个{orb}球", new EffectDef
        {
            Kind = EffectKind.ChannelOrb,
            Target = EffectTarget.Self,
            Amount = 2,
            Orb = orb,
        });
    }

    Row("激发 2 个充能球", new EffectDef
    {
        Kind = EffectKind.EvokeOrbs,
        Target = EffectTarget.Self,
        Amount = 2,
    });
    Row("使所有玩家激发最右侧的充能球 2 次", new EffectDef
    {
        Kind = EffectKind.EvokeOrbs,
        Target = EffectTarget.AllAllies,
        Amount = 2,
    });
    Row("获得 2 个充能球栏位", new EffectDef
    {
        Kind = EffectKind.AddOrbSlots,
        Target = EffectTarget.Self,
        Amount = 2,
    });
    Row("召唤 6", new EffectDef
    {
        Kind = EffectKind.Summon,
        Target = EffectTarget.Self,
        Amount = 6,
    });

    // 召唤的**计算型**：生命值是算出来的。这一条以前没有对照行，所以"算出来的句子只念基础值"
    // 那个缺陷在 phrasetest 里也看不见——补上它，缺模板/念错才会现形。
    Row("召唤 等于 手牌数 + 1 生命值的召唤物",
        Calc(EffectKind.Summon, EffectTarget.Self, ValueSourceKind.HandCount, 1));

    Row("铸造 3 点", new EffectDef
    {
        Kind = EffectKind.Forge,
        Target = EffectTarget.Self,
        Amount = 3,
    });

    Row("铸造 等于 手牌数 + 1 的点数",
        Calc(EffectKind.Forge, EffectTarget.Self, ValueSourceKind.HandCount, 1));

    // 附魔走选牌器：作用对象 + 作用方式 + 张数决定"给哪些牌附魔"，
    // 而效果数值是**层数**（Enchant 的第二个参数）。三种作用对象各一句。
    Row("附魔手牌中的 2 张牌（各 1 层）", new EffectDef
    {
        Kind = EffectKind.EnchantCardsInPile,
        Target = EffectTarget.Self,
        Pick = CardPick.InPile(PileKind.Hand, PickMode.Random, 2),
        Amount = 1,
        EnchantmentId = "ENCHANTMENT.SHARP",
    });

    Row("给打击牌附魔 2 层", new EffectDef
    {
        Kind = EffectKind.EnchantCardsInPile,
        Target = EffectTarget.Self,
        Pick = new CardPick { Tags = { CardTag.Strike } },
        Amount = 2,
        EnchantmentId = "ENCHANTMENT.SHARP",
    });

    Row("给指定牌附魔 2 层", new EffectDef
    {
        Kind = EffectKind.EnchantCardsInPile,
        Target = EffectTarget.Self,
        Pick = new CardPick { CardIds = { "CARD.STRIKE_IRONCLAD" } },
        Amount = 2,
        EnchantmentId = "ENCHANTMENT.SHARP",
    });

    // 多维度并存 + 两个费用维度：维度之间是"与"，句子要把它们都念出来
    //（中文"能量消耗大于 2 且星辰消耗不小于 0 的稀有攻击牌"，英文从句跟在名词后面）。
    Row("稀有攻击牌（能量>2、星辰>=0）", new EffectDef
    {
        Kind = EffectKind.ExhaustCards,
        Target = EffectTarget.Self,
        Amount = 1,
        EnergyAmount = 2,
        StarAmount = 0,
        Pick = new CardPick
        {
            Rarities = { CardRarity.Rare },
            Types = { CardType.Attack },
            EnergyComparison = CostComparison.GreaterThan,
            StarComparison = CostComparison.AtLeast,
        },
    });

    Row("星辰为 X 的牌", new EffectDef
    {
        Kind = EffectKind.ExhaustCards,
        Target = EffectTarget.Self,
        Amount = 1,
        StarAmount = 3,
        Pick = new CardPick { StarComparison = CostComparison.IsX },
    });

    // 伤害增益：一条效果，措辞按作用对象（牌堆/指定牌名/按条件/此牌）与持续范围变。
    // 英文的单复数也跟着作用对象变，所以四种对象都要各出一句。
    foreach ((string label, CardPick pick) in new (string, CardPick)[]
    {
        ("从牌堆", CardPick.InPile(PileKind.Hand)),
        ("按牌名", CardPick.Named("CARD.STRIKE_IRONCLAD")),
        ("按标签", CardPick.OfTags(CardTag.Strike)),
        ("此牌", CardPick.This()),
    })
    {
        Row($"伤害增加（{label}，本场战斗）", new EffectDef
        {
            Kind = EffectKind.BoostCardDamage,
            Target = EffectTarget.Self,
            Pick = pick,
            Amount = 3,
            CostDuration = CostDuration.ThisCombat,
        });

        Row($"伤害增加（{label}，永久）", new EffectDef
        {
            Kind = EffectKind.BoostCardDamage,
            Target = EffectTarget.Self,
            Pick = pick,
            Amount = 2,
            CostDuration = CostDuration.Permanent,
        });
    }

    // 使"某类牌"伤害增加：按标签、指定牌、不限三种措辞都要看。
    // 指定牌走另一条模板（英文单数动词），所以必须单独覆盖。
    Row("使打击牌伤害增加", new EffectDef
    {
        Kind = EffectKind.BoostCardDamage,
        Target = EffectTarget.Target,
        Amount = 2,
        CardFilter = CardFilterKind.TagStrike,
    });
    Row("使指定牌伤害增加", new EffectDef
    {
        Kind = EffectKind.BoostCardDamage,
        Target = EffectTarget.Target,
        Amount = 3,
        CardFilter = CardFilterKind.SpecificCard,
        CardId = "CARD.STRIKE_IRONCLAD",
    });
    Row("使所有牌伤害增加", new EffectDef
    {
        Kind = EffectKind.BoostCardDamage,
        Target = EffectTarget.Target,
        Amount = 1,
        CardFilter = CardFilterKind.Any,
    });

    Row("立即执行灾厄判定", new EffectDef
    {
        Kind = EffectKind.TriggerDoom,
        Target = EffectTarget.Target,
    });

    Row("自动打出抽牌堆中的牌", new EffectDef
    {
        Kind = EffectKind.AutoPlay,
        Target = EffectTarget.Self,
        Amount = 2,
        Pick = CardPick.InPile(PileKind.DrawPile),
    });

    Console.WriteLine();
    Console.WriteLine("-- 打出一张牌时（筛选 + 计数器）--");

    // 每一种筛选都描述一遍：这一句里的名词与 filter* 那组是两套说法（"一张X" / "所有X"），
    // 少写一条 playedCard* 就会退回英文兜底，只有中英并列看才发现得了。
    foreach (CardFilterKind filter in Enum.GetValues<CardFilterKind>())
    {
        Row($"每打出一张（{filter}）", new EffectDef
        {
            Kind = EffectKind.GainBlock,
            Target = EffectTarget.Self,
            Amount = 1,
            Trigger = EffectTrigger.CardPlayed,
            CardFilter = filter,
            CardId = filter == CardFilterKind.SpecificCard ? "CARD.STRIKE_IRONCLAD" : null,
        });
    }

    Row("每打出一张牌（不限 + 计数器，本回合）", new EffectDef
    {
        Kind = EffectKind.GainEnergy,
        Target = EffectTarget.Self,
        Amount = 1,
        Trigger = EffectTrigger.CardPlayed,
        UseCounter = true,
        CounterTarget = 3,
        CounterReset = CounterReset.ThisTurn,
    });

    Row("每打出 2 张技能牌（本场战斗）", new EffectDef
    {
        Kind = EffectKind.DrawCards,
        Target = EffectTarget.Self,
        Amount = 1,
        Trigger = EffectTrigger.CardPlayed,
        CardFilter = CardFilterKind.TypeSkill,
        UseCounter = true,
        CounterTarget = 2,
        CounterReset = CounterReset.ThisCombat,
    });

    // 阈值 1 等同于不启用（每张都触发），措辞该按不启用写。
    Row("每打出 1 张虚无牌（阈值 1 → 按每张写）", new EffectDef
    {
        Kind = EffectKind.GainBlock,
        Target = EffectTarget.Self,
        Amount = 2,
        Trigger = EffectTrigger.CardPlayed,
        CardFilter = CardFilterKind.KeywordEthereal,
        UseCounter = true,
        CounterTarget = 1,
        CounterReset = CounterReset.ThisTurn,
    });

    Console.WriteLine();
    Console.WriteLine("-- 最大生命 --");

    Row("提高 3 点最大生命", Fixed(EffectKind.GainMaxHp, EffectTarget.Self, 3));
    Row("降低 3 点最大生命", Fixed(EffectKind.LoseMaxHp, EffectTarget.Self, 3));
    Row("提高 等于本回合失去生命次数的最大生命",
        Calc(EffectKind.GainMaxHp, EffectTarget.Self, ValueSourceKind.HpLostThisTurn, 0));

    Console.WriteLine();
    Console.WriteLine("-- 计算型的重复次数 --");

    Row("造成 5 点伤害，重复 等于失去生命次数+1 次", new EffectDef
    {
        Kind = EffectKind.DealDamage,
        Target = EffectTarget.Target,
        Amount = 5,
        CalculatedRepeat = true,
        RepeatBase = 1,
        RepeatSource = new ValueSource { Actor = ValueSourceActor.Self, Kind = ValueSourceKind.HpLostThisCombat },
    });

    Row("固定重复与计算型次数并存（计算型胜出）", new EffectDef
    {
        Kind = EffectKind.DealDamage,
        Target = EffectTarget.Target,
        Amount = 3,
        Repeat = 2,
        CalculatedRepeat = true,
        RepeatBase = 0,
        RepeatSource = new ValueSource { Actor = ValueSourceActor.Self, Kind = ValueSourceKind.HandCount },
    });

    Console.WriteLine();
    Console.WriteLine("-- 回合时机的牌堆范围 --");
    foreach (PileScope scope in Enum.GetValues<PileScope>())
    {
        Row($"回合开始时（{scope}）", new EffectDef
        {
            Kind = EffectKind.DrawCards,
            Target = EffectTarget.Self,
            Amount = 1,
            Trigger = EffectTrigger.StartOfTurn,
            PileScope = scope,
        });
        Row($"回合结束时（{scope}）", new EffectDef
        {
            Kind = EffectKind.GainBlock,
            Target = EffectTarget.Self,
            Amount = 3,
            Trigger = EffectTrigger.EndOfTurn,
            PileScope = scope,
        });
    }

    Console.WriteLine();
    Console.WriteLine("-- 自动打出 --");

    // 打出别的牌：作用对象是"某个牌堆"，有数值。
    Row("自动打出抽牌堆中的 2 张牌", new EffectDef
    {
        Kind = EffectKind.AutoPlay,
        Target = EffectTarget.Self,
        Amount = 2,
        Pick = CardPick.InPile(PileKind.DrawPile),
    });

    // 打出这张牌自己（原版轰击那种）：作用对象是"这张牌自己"，
    // 时机 + 牌堆范围构成条件，句子要与原版一致。
    Row("回合开始时若在消耗堆中将自己打出", new EffectDef
    {
        Kind = EffectKind.AutoPlay,
        Target = EffectTarget.Self,
        Trigger = EffectTrigger.StartOfTurn,
        PileScope = PileScope.ExhaustPile,
        Pick = CardPick.This(),
    });

    Row("回合结束时若在弃牌堆中将自己打出", new EffectDef
    {
        Kind = EffectKind.AutoPlay,
        Target = EffectTarget.Self,
        Trigger = EffectTrigger.EndOfTurn,
        PileScope = PileScope.DiscardPile,
        Pick = CardPick.This(),
    });

    Console.WriteLine();
    Console.WriteLine("-- 费用 --");

    // 降费：三种挑选方式 × 三种持续范围，都要各有一句。英文里"随机 1 张"要用单数，
    // 所以单数单独列一条。
    foreach (PickMode pick in new[] { PickMode.All, PickMode.Random, PickMode.Choose })
    {
        foreach (CostDuration dur in new[] { CostDuration.ThisTurn, CostDuration.ThisCombat, CostDuration.UntilPlayed })
        {
            int count = pick == PickMode.All ? 0 : 2;
            Row($"降费（{pick}/{dur}）", new EffectDef
            {
                Kind = EffectKind.ModifyCardCost,
                Target = EffectTarget.Self,
                Amount = -1,
                Pick = CardPick.InPile(PileKind.Hand, pick, count),
                CostDuration = dur,
            });
        }
    }

    Row("降费（随机 1 张，单数）", new EffectDef
    {
        Kind = EffectKind.ModifyCardCost,
        Target = EffectTarget.Self,
        Amount = -2,
        
        
        Pick = CardPick.InPile(PileKind.DrawPile, PickMode.Random, 1),
        CostDuration = CostDuration.ThisCombat,
    });

    Row("加费（选择 1 张）", new EffectDef
    {
        Kind = EffectKind.ModifyCardCost,
        Target = EffectTarget.Self,
        Amount = 2,
        
        
        Pick = CardPick.InPile(PileKind.DiscardPile, PickMode.Choose, 1),
        CostDuration = CostDuration.UntilPlayed,
    });

    // 三种作用对象都要各说一句：牌堆 / 指定牌名 / 这张牌自己。
    // （"这张牌自己"的措辞与牌堆不同——英文用单数动词、中文不带"自己"，所以必须单独覆盖。）
    foreach (PickScope scope in new[] { PickScope.Pile, PickScope.SpecificCard, PickScope.ThisCard })
    {
        Row($"升级（作用对象={scope}）", new EffectDef
        {
            Kind = EffectKind.UpgradeCardsInPile,
            Target = EffectTarget.Self,
            Pick = scope switch
            {
                PickScope.SpecificCard => CardPick.Named("CARD.STRIKE_IRONCLAD"),
                PickScope.ByFilter => CardPick.OfTags(CardTag.Strike),
                PickScope.ThisCard => CardPick.This(),
                _ => CardPick.InPile(PileKind.Hand),
            },
        });

        Row($"免费（作用对象={scope}）", new EffectDef
        {
            Kind = EffectKind.SetCardsFree,
            Target = EffectTarget.Self,
            Pick = scope switch
            {
                PickScope.SpecificCard => CardPick.Named("CARD.DEFEND_IRONCLAD"),
                PickScope.ByFilter => CardPick.OfTags(CardTag.Strike),
                PickScope.ThisCard => CardPick.This(),
                _ => CardPick.InPile(PileKind.Hand),
            },
            CostDuration = CostDuration.ThisCombat,
        });
    }

    // 免费：两种持续范围。
    foreach (CostDuration dur in new[] { CostDuration.ThisTurn, CostDuration.ThisCombat })
    {
        Row($"免费（本堆/{dur}）", new EffectDef
        {
            Kind = EffectKind.SetCardsFree,
            Target = EffectTarget.Self,
            
            
            CostDuration = dur,
        Pick = CardPick.InPile(PileKind.Hand, PickMode.All),
        });
    }

    Row("免费（选择 2 张）", new EffectDef
    {
        Kind = EffectKind.SetCardsFree,
        Target = EffectTarget.Self,
        
        
        Pick = CardPick.InPile(PileKind.DrawPile, PickMode.Choose, 2),
        CostDuration = CostDuration.ThisCombat,
    });

    Console.WriteLine();
    Console.WriteLine("-- 改造牌堆 --");

    // 升级：三种挑选方式各一句。游戏自己会跳过不可升级的牌，所以措辞里不带这层条件。
    foreach (PickMode pick in new[] { PickMode.All, PickMode.Random, PickMode.Choose })
    {
        Row($"升级（{pick}）", new EffectDef
        {
            Kind = EffectKind.UpgradeCardsInPile,
            Target = EffectTarget.Self,
            Pick = CardPick.InPile(PileKind.Hand, pick, pick == PickMode.All ? 1 : 2),
        });
    }

    // 变化：随机 / 指定牌（各含单复数与升级版）。
    Row("变化成随机牌（全部）", new EffectDef
    {
        Kind = EffectKind.TransformCardsInPile,
        Target = EffectTarget.Self,
        
        
        TransformInto = TransformInto.Random,
    Pick = CardPick.InPile(PileKind.Hand, PickMode.All),
    });

    Row("变化成指定牌（选择 1 张）", new EffectDef
    {
        Kind = EffectKind.TransformCardsInPile,
        Target = EffectTarget.Self,
        
        
        Pick = CardPick.InPile(PileKind.DrawPile, PickMode.Choose, 1),
        TransformInto = TransformInto.SpecificCard,
        CardId = "CARD.MINION_DIVE_BOMB",
        Upgraded = true,
    });

    Row("变化成指定牌（随机 2 张）", new EffectDef
    {
        Kind = EffectKind.TransformCardsInPile,
        Target = EffectTarget.Self,
        
        
        Pick = CardPick.InPile(PileKind.DiscardPile, PickMode.Random, 2),
        TransformInto = TransformInto.SpecificCard,
        CardId = "CARD.STRIKE_IRONCLAD",
    });

    Console.WriteLine();
    Console.WriteLine("-- 重复次数 --");
    Row("造成 4 点伤害，重复 3 次", new EffectDef
    {
        Kind = EffectKind.DealDamage,
        Target = EffectTarget.Target,
        Amount = 4,
        Repeat = 3,
    });
    Row("获得格挡，重复 2 次", new EffectDef
    {
        Kind = EffectKind.GainBlock,
        Target = EffectTarget.Self,
        Amount = 5,
        Repeat = 2,
    });
    Row("生成充能球，重复 3 次", new EffectDef
    {
        Kind = EffectKind.ChannelOrb,
        Target = EffectTarget.Self,
        Amount = 1,
        Orb = OrbKind.Lightning,
        Repeat = 3,
    });
}

// 句式表缺项检查：模板里出现的占位符必须都被代码提供，否则会原样打印出 {name}。
Console.WriteLine();
Console.WriteLine("===== 用词表覆盖 =====");

// 取不到用词时大多有英文兜底，所以漏了翻译不会报错——只会在中文卡面上冒出一句英文。
    // 这里按描述模块声明的清单核对每种语言是否都提供了。
bool missingWords = false;
foreach (string language in requested)
{
    SentenceSet set = SentenceLibrary.For(language);

    // 清单里既有"词"也有"模板"：**两处都查**（新的筛选短语住在 templates 段，
    // 而"全部/选择的 N 张牌"那几条历史键住在 words 段）。
    List<string> missing = DescriptionModule.RequiredWordKeys()
        .Where(k => set.Word(k) is null && !set.HasTemplate(k))
        .ToList();

    if (missing.Count > 0)
    {
        Console.WriteLine($"  MISSING {language}: {string.Join(", ", missing)}");
        missingWords = true;
    }
    else
    {
        Console.WriteLine($"  ok {language}：{DescriptionModule.RequiredWordKeys().Count} 个用词齐全");
    }
}

// 数值来源的词表（sources）也要齐。
//
// 漏一个不会报错：SourceLabel 会退回 SourceCatalog.Label 那份代码里的名字，
// 于是句式表里精心调过的措辞被悄悄替换成另一套说法，两处从此不一致。
// 这里逐项核对（None 不在表里，它表示"不使用计算"，不会出现在句子里）。
Console.WriteLine();
Console.WriteLine("===== 数值来源词表覆盖 =====");

bool missingSources = false;
foreach (string language in requested)
{
    SentenceSet set = SentenceLibrary.For(language);

    List<string> missing = Enum.GetValues<ValueSourceKind>()
        .Where(k => k != ValueSourceKind.None && set.Source(k.ToString()) is null)
        .Select(k => k.ToString())
        .ToList();

    if (missing.Count > 0)
    {
        Console.WriteLine($"  MISSING {language}: {string.Join(", ", missing)}");
        missingSources = true;
    }
    else
    {
        int total = Enum.GetValues<ValueSourceKind>().Count(k => k != ValueSourceKind.None);
        Console.WriteLine($"  ok {language}：{total} 个来源都有词");
    }
}

Console.WriteLine();
Console.WriteLine("===== 模板覆盖 =====");

// 每种效果、每种目标、固定/计算两种数值都描述一遍，然后问句式表："有哪个模板没找到？"
//
// 这一项补的是"缺模板不会报错"这个坑：Render 找不到模板会退回 unknown 那条通用句子，
// 占位符又都能填上，于是输出是一句看起来正常、意思却不对的话（实测过：把新效果的模板名
// 写错时，"自动打出这张牌"渲染成了内部枚举名加一个 0）。枚举一遍比人工维护一份
// 模板清单可靠——将来加效果时漏写模板会直接在这里失败。
bool missingTemplates = false;

foreach (string language in requested)
{
    SentenceSet set = SentenceLibrary.For(language);

    // 先描述一遍所有形状，让 Render 把缺的模板名记下来。
    foreach (EffectKind kind in Enum.GetValues<EffectKind>())
    {
        foreach (EffectTarget target in Enum.GetValues<EffectTarget>())
        {
            // 固定值（无来源）与计算型（有来源）两种各描述一遍：两者走的是不同模板
            // （damage 与 damageCalc），只扫一种会漏掉另一种的缺模板。
            foreach (bool calculated in new[] { false, true })
            {
                DescriptionModule.Describe(null, new EffectDef
                {
                    Kind = kind,
                    Target = target,
                    Amount = 2,
                    Source = calculated
                        ? new ValueSource { Actor = ValueSourceActor.Self, Kind = ValueSourceKind.HandCount }
                        : new ValueSource(),
                    PowerId = "POWER.STRENGTH_POWER",
                    EnchantmentId = "ENCHANTMENT.SHARP",
                    CardId = "CARD.STRIKE_IRONCLAD",
                    Orb = OrbKind.Lightning,
                    Trigger = EffectTrigger.StartOfTurn,
                    PileScope = PileScope.DrawPile,
                }, language);
            }
        }
    }

    List<string> missing = set.MissingTemplateKeys.OrderBy(k => k, StringComparer.Ordinal).ToList();

    if (missing.Count > 0)
    {
        Console.WriteLine($"  MISSING {language}: {string.Join(", ", missing)}");
        missingTemplates = true;
    }
    else
    {
        Console.WriteLine($"  ok {language}：用到的模板都有");
    }
}

Console.WriteLine();
Console.WriteLine("===== 未替换的占位符 =====");
bool leaky = false;
foreach (string language in requested)
{
    var probe = new List<(string, EffectDef)>
    {
        ("damage", Fixed(EffectKind.DealDamage, EffectTarget.Target, 1)),
        ("damageCalc", Calc(EffectKind.DealDamage, EffectTarget.Target, ValueSourceKind.HandCount, 1)),
        ("block", Fixed(EffectKind.GainBlock, EffectTarget.Self, 1)),
        ("power", new EffectDef { Kind = EffectKind.ApplyPower, Target = EffectTarget.Self, Amount = 1, PowerId = "POWER.MIGHT" }),
        ("move", new EffectDef
        {
            Kind = EffectKind.MoveCardsBetweenPiles, Target = EffectTarget.Self,
            Pick = CardPick.This(), TargetPile = PileKind.DrawPile,
        }),
        ("named", new EffectDef { Kind = EffectKind.AddNamedCardToPile, Target = EffectTarget.Self, Amount = 1, CardId = "CARD.STRIKE_IRONCLAD" }),
        ("pile", new EffectDef { Kind = EffectKind.ExhaustCards, Target = EffectTarget.Self, }),

        // 第二批效果：同样要检查占位符有没有漏传。
        ("orb", new EffectDef { Kind = EffectKind.ChannelOrb, Target = EffectTarget.Self, Amount = 1, Orb = OrbKind.Frost }),
        ("evoke", new EffectDef { Kind = EffectKind.EvokeOrbs, Target = EffectTarget.Self, Amount = 1 }),
        ("slots", new EffectDef { Kind = EffectKind.AddOrbSlots, Target = EffectTarget.Self, Amount = 1 }),
        ("summon", new EffectDef { Kind = EffectKind.Summon, Target = EffectTarget.Self, Amount = 1 }),
        ("enchantAny", new EffectDef { Kind = EffectKind.EnchantCardsInPile, Target = EffectTarget.Self, Amount = 1, EnchantmentId = "ENCHANTMENT.SHARP" }),
        ("enchantTag", new EffectDef
        {
            Kind = EffectKind.EnchantCardsInPile, Target = EffectTarget.Self, Amount = 1,
            Pick = CardPick.OfTags(CardTag.Strike),
            EnchantmentId = "ENCHANTMENT.SHARP",
        }),
        ("enchantAll", new EffectDef { Kind = EffectKind.EnchantCardsInPile, Target = EffectTarget.Self, Amount = 0, EnchantmentId = "ENCHANTMENT.SHARP" }),
        ("sourcePower", new EffectDef
        {
            Kind = EffectKind.GainBlock, Target = EffectTarget.Self,
            Source = new ValueSource { Actor = ValueSourceActor.Self, Kind = ValueSourceKind.PowerStacks, PowerId = "POWER.STRENGTH_POWER" },
        }),
        ("repeatSourcePower", new EffectDef
        {
            Kind = EffectKind.GainBlock, Target = EffectTarget.Self, Amount = 1,
            CalculatedRepeat = true, RepeatBase = 1,
            RepeatSource = new ValueSource { Actor = ValueSourceActor.Self, Kind = ValueSourceKind.PowerStacks, PowerId = "POWER.STRENGTH_POWER" },
        }),
        ("boost", new EffectDef { Kind = EffectKind.BoostCardDamage, Target = EffectTarget.Self, Amount = 1, }),
        ("boostPerm", new EffectDef { Kind = EffectKind.BoostCardDamage, Target = EffectTarget.Self, Amount = 1, Pick = CardPick.This(), CostDuration = CostDuration.Permanent }),
        ("repeat", new EffectDef { Kind = EffectKind.DealDamage, Target = EffectTarget.Target, Amount = 1, Repeat = 2 }),

        // "打出一张牌时"两句：带筛选的与带计数器的，占位符 {cards} / {count} 都不能漏传。
        ("playedFiltered", new EffectDef
        {
            Kind = EffectKind.GainBlock, Target = EffectTarget.Self, Amount = 1,
            Trigger = EffectTrigger.CardPlayed, CardFilter = CardFilterKind.TypeSkill,
        Pick = CardPick.This(),
        }),
        ("playedCounted", new EffectDef
        {
            Kind = EffectKind.GainBlock, Target = EffectTarget.Self, Amount = 1,
            Trigger = EffectTrigger.CardPlayed, CardFilter = CardFilterKind.TypeAttack,
            UseCounter = true, CounterTarget = 3, CounterReset = CounterReset.ThisTurn,
        }),
    };

    foreach ((string label, EffectDef effect) in probe)
    {
        string text = DescriptionModule.Describe(DummyProject(), effect, language);

        if (text.Contains('{'))
        {
            Console.WriteLine($"  LEAK {language} {label}: {text}");
            leaky = true;
        }
    }
}

// 两套说法各管一处，必须都在：
//
//   效果行摘要（不传变量名）—— 写字面数字，读起来是说明文；
//   卡牌描述自动填充（传变量名）—— 写**占位符**，数值跟着升级走，也才有"提升了多少"的绿字。
//
// 原版的描述全是后者（{Damage:diff()} / {CalculatedDamage:diff()} / {ExtraDamage:diff()}）。
// 这一块断言两种形态：自动填充里出现变量占位符、且变量名与 EffectResolver 的规划一致
// （名字对不上的话游戏内格式化失败——而那种错只在带描述导出时现形）。
Console.WriteLine();
Console.WriteLine("===== 描述占位符（自动填充 vs 效果行摘要）=====");

{
    var cardProject = new ModProject { Id = "PhraseProbe", Name = "句式探针" };

    // 两条同类固定值伤害 + 计算型格挡 + 可升级的重复：
    // 覆盖 Damage/Damage2、CalculationBase/ExtraDamage、Repeat 四类变量。
    cardProject.Cards.Add(new CardDef
    {
        Id = "PHRASE_PROBE",
        Name = new LocalizedText("eng", "Phrase Probe"),
        Pool = "ironclad",
        Type = CardType.Attack,
        Rarity = CardRarity.Common,
        Target = CardTargetType.AnyEnemy,
        EnergyCost = 1,
        Effects =
        {
            new EffectDef
            {
                Kind = EffectKind.DealDamage, Target = EffectTarget.Target,
                Amount = 4, Upgrade = new EffectUpgrade { AmountDelta = 2 },
            },
            new EffectDef
            {
                Kind = EffectKind.DealDamage, Target = EffectTarget.Target,
                Amount = 9, Upgrade = new EffectUpgrade { AmountDelta = 3 },
            },
            new EffectDef
            {
                Kind = EffectKind.GainBlock, Target = EffectTarget.Self,
                Amount = 5,
                Source = new ValueSource { Actor = ValueSourceActor.Self, Kind = ValueSourceKind.HandCount, PerUnit = 2 },
                Upgrade = new EffectUpgrade { AmountDelta = 1, PerUnitDelta = 1 },
            },
            new EffectDef
            {
                Kind = EffectKind.DealDamage, Target = EffectTarget.Target,
                Amount = 3, Repeat = 2,
                Upgrade = new EffectUpgrade { RepeatDelta = 1 },
            },
        },
    });

    CardDef card = cardProject.Cards[0];
    bool nameFail = false;

    foreach (string language in requested)
    {
        string suggested = DescriptionPlaceholders.Suggest(cardProject, card, language);

        // 每一条效果都该引用**它自己**那个变量。
        //
        // 注意格挡的系数变量叫 **CalculationExtra**（不是 ExtraDamage）——这是游戏自己埋的
        // 命名陷阱（CalculatedDamageVar 覆写 GetExtraVar 取 ExtraDamage，CalculatedBlockVar
        // 走基类取 CalculationExtra）。名字写错时游戏内格式化失败，所以这里钉死。
        var expected = new[]
        {
            "{Damage:diff()}",          // 第一条固定值伤害
            "{Damage2:diff()}",         // 第二条固定值伤害（同类第二个）
            "{CalculationBase:diff()}", // 计算型格挡的基础值
            "{CalculationExtra:diff()}",// 计算型格挡的每单位系数
            "{Repeat:diff()}",          // 可升级的重复次数
        };

        var missing = expected.Where(t => !suggested.Contains(t, StringComparison.Ordinal)).ToList();

        if (missing.Count > 0)
        {
            Console.WriteLine($"  [失败] {language} 自动描述缺占位符：{string.Join(", ", missing)}");
            Console.WriteLine($"         实际：{suggested.Replace("\n", " / ")}");
            nameFail = true;
            continue;
        }

        // 变量名必须与规划一致：描述里引用的每一个占位符都要在 Placeholders 里有对应项，
        // 否则游戏内格式化失败（而且只在真的带描述导出时才会发现）。
        var available = new HashSet<string>(
            DescriptionPlaceholders.For(cardProject, card, language).Select(p => p.Placeholder),
            StringComparer.OrdinalIgnoreCase);

        var stray = DescriptionPlaceholders.Extract(suggested)
            .Where(t => !available.Contains(t))
            .ToList();

        if (stray.Count > 0)
        {
            Console.WriteLine($"  [失败] {language} 描述引用了卡上没有的变量：{string.Join(", ", stray)}");
            nameFail = true;
            continue;
        }

        Console.WriteLine($"  ok {language}：自动描述引用 {expected.Length} 个变量，且都在卡上声明了");
    }

    // 效果行摘要保持字面数字（那是说明文，写成占位符反而难读）。
    string summary = DescriptionModule.Describe(cardProject, card.Effects[0], "zhs");

    if (summary.Contains('{'))
    {
        Console.WriteLine($"  [失败] 效果行摘要里出现了占位符：{summary}");
        nameFail = true;
    }
    else
    {
        Console.WriteLine($"  ok 效果行摘要保持字面数字：{summary}");
    }

    if (nameFail)
    {
        Console.WriteLine();
        Console.WriteLine("RESULT: FAILED (description placeholder names are wrong)");
        return 1;
    }
}

// ---- 启用条件的前缀句 ----
// 两个语言各渲染一条整句："若 手牌数 不小于 3，对目标造成 6 点伤害。"。
// 前缀取词走**公开入口**（DescriptionModule.Describe 的中央拼装），连"前缀在时机之后、
// 动作之前"的顺序一起验——比静态核对键在不在更接近真实出问题的方式。
{
    Console.WriteLine();
    Console.WriteLine("===== 启用条件前缀 =====");
    bool condFail = false;

    foreach (string language in requested)
    {
        SentenceSet set = SentenceLibrary.For(language);
        string opWord = set.Word("condOp_AtLeast") ?? "(null)";

        if (opWord == "(null)")
        {
            Console.WriteLine($"  MISSING {language}: words.condOp_AtLeast 取不到（会退回枚举名）");
            condFail = true;
            continue;
        }

        var effect = new EffectDef
        {
            Kind = EffectKind.DealDamage,
            Target = EffectTarget.Target,
            Amount = 6,
            Condition = new EffectCondition
            {
                Comparison = ConditionComparison.AtLeast,
                LeftSource = new ValueSource { Actor = ValueSourceActor.Self, Kind = ValueSourceKind.HandCount },
                RightAmount = 3,
            },
        };

        string sentence = DescriptionModule.Describe(DummyProject(), effect, language);
        bool ok = language == "zhs"
            ? sentence == "若 手牌数 不小于 3，对目标造成 6 点伤害。"
            : sentence == "If cards in hand is at least 3, Deal 6 damage to the target.";

        Console.WriteLine($"  {(ok ? "ok" : "失败")} {language}：{sentence}");

        if (!ok)
        {
            condFail = true;
        }
    }

    if (condFail)
    {
        Console.WriteLine();
        Console.WriteLine("RESULT: FAILED (condition prefix wording is wrong)");
        return 1;
    }
}

Console.WriteLine(leaky
    ? "RESULT: LEAKED PLACEHOLDERS"
    : missingTemplates ? "RESULT: MISSING TEMPLATES"
    : missingWords ? "RESULT: MISSING WORDS"
    : missingSources ? "RESULT: MISSING SOURCES" : "RESULT: OK");
return leaky || missingWords || missingTemplates || missingSources ? 1 : 0;

static ModProject DummyProject() => new();
