using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;
using ModForge.Core;
using ModForge.Core.Export;
using ModForge.Core.Generate;
using ModForge.Core.Model;
using ModForge.Core.Modules.CardPicking;
using ModForge.Core.Modules.Descriptions;
using ModForge.Core.Modules.Variables;
using ModForge.Core.Modules.Effects;
using ModForge.Core.Validation;

// 验证第二批效果：充能球、召唤、附魔、伤害增益、重复次数。
//
// 关键不是"生成了文本"，而是生成出来的代码能被 Roslyn 针对游戏程序集编译通过——
// API 名字或签名写错的话，编译这一步就会失败。
//
// 用法: newfx2 <gameDir> [outDir]

string gameDir = args.Length > 0
    ? args[0]
    : @"D:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2";
string outDir = args.Length > 1 ? args[1] : @"D:\ModForge\out\newfx2";

var project = new ModProject
{
    Id = "NewFx2Demo",
    Name = "新效果演示 2",
    Author = "ModForge",
    Version = "1.0.0",
    SelfCheckOnStartup = true,
};

// 状态：按卡牌筛选的伤害增益 —— "所有打击牌额外造成 4 点伤害"。
project.Powers.Add(new PowerDef
{
    Id = "SHARPENED_STRIKES",
    Name = new LocalizedText("eng", "Sharpened Strikes").With("zhs", "磨砺打击"),
    Description = new LocalizedText("eng", "Strikes deal {Amount} additional damage.")
        .With("zhs", "打击牌额外造成 {Amount} 点伤害。"),
    Kind = PowerKind.Buff,
    Stack = PowerStack.Counter,
    DamageModifier = DamageModifier.Additive,
    CardFilter = CardFilterKind.TagStrike,
});

// 按指定卡牌筛的伤害增益。
project.Powers.Add(new PowerDef
{
    Id = "BLADE_MASTERY",
    Name = new LocalizedText("eng", "Blade Mastery").With("zhs", "刀法精通"),
    Description = new LocalizedText("eng", "Shivs deal {Amount} additional damage.")
        .With("zhs", "小刀额外造成 {Amount} 点伤害。"),
    Kind = PowerKind.Buff,
    Stack = PowerStack.Counter,
    DamageModifier = DamageModifier.Additive,
    CardFilter = CardFilterKind.TagShiv,
});

// --- 卡 1：生成充能球（故障机器人）---
project.Cards.Add(new CardDef
{
    Id = "CHANNEL_STORM",
    Name = new LocalizedText("eng", "Channel Storm").With("zhs", "引导风暴"),
    Description = new LocalizedText("eng", "Channel 2 Lightning.")
        .With("zhs", "生成 2 个闪电充能球。"),
    Pool = "defect",
    Type = CardType.Skill,
    Rarity = CardRarity.Common,
    Target = CardTargetType.Self,
    EnergyCost = 1,
    Effects =
    {
        new EffectDef
        {
            Kind = EffectKind.ChannelOrb,
            Target = EffectTarget.Self,
            Amount = 2,
            Orb = OrbKind.Lightning,
            Upgrade = new EffectUpgrade { AmountDelta = 1 },
        },
    },
});

// --- 卡 2：激发充能球 + 增加球位 ---
project.Cards.Add(new CardDef
{
    Id = "ORB_BURST",
    Name = new LocalizedText("eng", "Orb Burst").With("zhs", "充能爆发"),
    Description = new LocalizedText("eng", "Gain 2 orb slots, then evoke 2.")
        .With("zhs", "获得 2 个充能球栏位，然后激发 2 个充能球。"),
    Pool = "defect",
    Type = CardType.Skill,
    Rarity = CardRarity.Uncommon,
    Target = CardTargetType.Self,
    EnergyCost = 1,
    Effects =
    {
        new EffectDef { Kind = EffectKind.AddOrbSlots, Target = EffectTarget.Self, Amount = 2 },
        new EffectDef { Kind = EffectKind.EvokeOrbs, Target = EffectTarget.Self, Amount = 2 },
    },
});

// --- 卡 2b：激发球的四种形态（这张卡的价值在于**被编译**）---
//
// 四种形态里，"随机"与"限定种类"两条用到了 <c>OrbQueue.Remove/Insert</c>、
// <c>Rng.CombatOrbs</c>、<c>orb.Id.Entry</c> 与三元表达式——这些只有真编译一次才知道写对没有
// （gensnapshot 只生成不编译）。
project.Cards.Add(new CardDef
{
    Id = "ORB_SWEEP",
    Name = new LocalizedText("eng", "Orb Sweep").With("zhs", "球潮"),
    Description = new LocalizedText("eng", "Evoke all of your Lightning or Frost orbs twice.")
        .With("zhs", "激发你全部的闪电、冰霜充能球 2 次。"),
    Pool = "defect",
    Type = CardType.Skill,
    Rarity = CardRarity.Rare,
    Target = CardTargetType.Self,
    EnergyCost = 2,
    Effects =
    {
        new EffectDef
        {
            Kind = EffectKind.EvokeOrbs, Target = EffectTarget.Self, Amount = 2,
            EvokeTarget = OrbEvokeTarget.All,
            OrbKinds = { OrbKind.Lightning, OrbKind.Frost },
        },
        new EffectDef
        {
            Kind = EffectKind.EvokeOrbs, Target = EffectTarget.Self, Amount = 1,
            EvokeTarget = OrbEvokeTarget.Random,
        },
        new EffectDef
        {
            Kind = EffectKind.EvokeOrbs, Target = EffectTarget.Self, Amount = 1,
            EvokeTarget = OrbEvokeTarget.Leftmost,
            OrbKinds = { OrbKind.Dark },
        },
    },
});

// --- 卡 3：召唤（亡灵契约师）---
project.Cards.Add(new CardDef
{
    Id = "RAISE_GUARD",
    Name = new LocalizedText("eng", "Raise Guard").With("zhs", "召唤护卫"),
    Description = new LocalizedText("eng", "Summon 6.").With("zhs", "召唤 6。"),
    Pool = "necrobinder",
    Type = CardType.Skill,
    Rarity = CardRarity.Common,
    Target = CardTargetType.Self,
    EnergyCost = 1,
    Effects =
    {
        new EffectDef
        {
            Kind = EffectKind.Summon,
            Target = EffectTarget.Self,
            Amount = 6,
            Upgrade = new EffectUpgrade { AmountDelta = 2 },
        },
    },
});

// --- 卡 4：附魔手牌中的牌 ---
//
// "附魔几张"由选牌器决定（这里：手牌里挑 2 张），效果数值是**层数**（各附 1 层）。
// 两者是不同的东西，生成上要抓住三件事：
//   1. 挑牌用游戏自己的 CanEnchant 过滤（各附魔适用范围不同，自己判迟早不一致），
//      但**已经带同种附魔的牌要留**——要做的正是给它加层数；
//   2. 已经有同种附魔 → 直接 `Amount +=`，不走 CardCmd.Enchant（那会抛异常）；
//   3. Enchant 的第二个参数是层数，用动态变量承载（可升级、可计算）。
project.Cards.Add(new CardDef
{
    Id = "SHARPEN_EDGE",
    Name = new LocalizedText("eng", "Sharpen Edge").With("zhs", "开刃"),
    Description = new LocalizedText("eng", "Enchant 2 cards in your hand with 1 Sharp.")
        .With("zhs", "为手牌中随机 2 张牌附魔 1 层锋利。"),
    Pool = "ironclad",
    Type = CardType.Skill,
    Rarity = CardRarity.Uncommon,
    Target = CardTargetType.Self,
    EnergyCost = 1,
    Effects =
    {
        new EffectDef
        {
            Kind = EffectKind.EnchantCardsInPile,
            Target = EffectTarget.Self,
            Pick = CardPick.InPile(PileKind.Hand, PickMode.Random, 2),
            Amount = 1,
            Upgrade = new EffectUpgrade { AmountDelta = 1 },
            EnchantmentId = "ENCHANTMENT.SHARP",
        },
    },
});

// --- 卡 5：本场战斗伤害增加 ---
project.Cards.Add(new CardDef
{
    Id = "TEMPER",
    Name = new LocalizedText("eng", "Temper").With("zhs", "淬火"),
    Description = new LocalizedText("eng", "Deal 6 damage. This card deals 3 more damage this combat.")
        .With("zhs", "造成 6 点伤害。在本场战斗中，这张牌的伤害增加 3 点。"),
    Pool = "ironclad",
    Type = CardType.Attack,
    Rarity = CardRarity.Common,
    Target = CardTargetType.AnyEnemy,
    EnergyCost = 1,
    Effects =
    {
        new EffectDef { Kind = EffectKind.DealDamage, Target = EffectTarget.Target, Amount = 6 },
        new EffectDef
        {
            Kind = EffectKind.BoostCardDamage,
            Target = EffectTarget.Self,
            Pick = CardPick.This(),
            Amount = 3,
            CostDuration = CostDuration.ThisCombat,
        },
    },
});

// --- 卡 6：永久伤害增加（自增牌）---
project.Cards.Add(new CardDef
{
    Id = "ETERNAL_SHARPENING",
    Name = new LocalizedText("eng", "Eternal Sharpening").With("zhs", "永恒磨砺"),
    Description = new LocalizedText("eng", "Deal 8 damage. Permanently increase this card's damage by 2.")
        .With("zhs", "造成 8 点伤害。这张牌的伤害值增加 2 点。"),
    Pool = "ironclad",
    Type = CardType.Attack,
    Rarity = CardRarity.Uncommon,
    Target = CardTargetType.AnyEnemy,
    EnergyCost = 1,
    Effects =
    {
        new EffectDef { Kind = EffectKind.DealDamage, Target = EffectTarget.Target, Amount = 8 },
        new EffectDef
        {
            // 合并后的写法："作用于这张牌自己" + 永久。
            Kind = EffectKind.BoostCardDamage,
            Target = EffectTarget.Self,
            Pick = CardPick.This(),
            Amount = 2,
            CostDuration = CostDuration.Permanent,
        },
    },
});

// --- 卡 7：重复次数 + 一张施加筛选型增益的牌 ---
project.Cards.Add(new CardDef
{
    Id = "TRIPLE_TAP",
    Name = new LocalizedText("eng", "Triple Tap").With("zhs", "三连击"),
    Description = new LocalizedText("eng", "Deal 4 damage 3 times.")
        .With("zhs", "造成 4 点伤害，重复 3 次。"),
    Pool = "silent",
    Type = CardType.Attack,
    Rarity = CardRarity.Common,
    Target = CardTargetType.AnyEnemy,
    EnergyCost = 1,
    Effects =
    {
        new EffectDef
        {
            Kind = EffectKind.DealDamage,
            Target = EffectTarget.Target,
            Amount = 4,
            Repeat = 3,
        },
    },
});

// --- 卡 8：施加"打击牌伤害增益"状态 ---
project.Cards.Add(new CardDef
{
    Id = "WHETSTONE",
    Name = new LocalizedText("eng", "Whetstone").With("zhs", "磨刀石"),
    Description = new LocalizedText("eng", "Strikes deal 4 additional damage.")
        .With("zhs", "打击牌额外造成 4 点伤害。"),
    Pool = "ironclad",
    Type = CardType.Power,
    Rarity = CardRarity.Rare,
    Target = CardTargetType.Self,
    EnergyCost = 1,
    Effects =
    {
        new EffectDef
        {
            Kind = EffectKind.ApplyPower,
            Target = EffectTarget.Self,
            Amount = 4,
            PowerId = "POWER.SHARPENED_STRIKES",
        },
    },
});

// --- 卡 9：X 费用 + X 值（能量） ---
//
// 这是用户特别关心的组合：X 费卡打出时能量已被扣光，所以效果里不能用"当前能量"，
// 必须用"X 值（能量）"（对应游戏的 ResolveEnergyXValue）。
project.Cards.Add(new CardDef
{
    Id = "X_BARRAGE",
    Name = new LocalizedText("eng", "X Barrage").With("zhs", "X 连击"),
    Description = new LocalizedText("eng", "Deal damage equal to the energy spent, 2 times.")
        .With("zhs", "造成 等于 X 值（能量）的伤害，重复 2 次。"),
    Pool = "defect",
    Type = CardType.Attack,
    Rarity = CardRarity.Rare,
    Target = CardTargetType.AnyEnemy,
    EnergyCostX = true,
    Effects =
    {
        new EffectDef
        {
            Kind = EffectKind.DealDamage,
            Target = EffectTarget.Target,
            Amount = 0,
            Source = new ValueSource { Actor = ValueSourceActor.Self, Kind = ValueSourceKind.EnergyX },
            Repeat = 2,
            Upgrade = new EffectUpgrade { RepeatDelta = 1 },
        },
    },
});

// 牌堆搬运：抽牌 / 弃牌 / 消耗。
//
// 重点是**弃牌**：它必须走 CardCmd.Discard，不能直接 CardPileCmd.Add 到弃牌堆——
// 后者只是换个牌堆，不会触发弃牌钩子，"被弃掉时"的效果全都不会生效。
// 这条路径只有真的编译过才能确认 API 用对，所以专门放一张牌覆盖它。
project.Cards.Add(new CardDef
{
    Id = "TACTICAL_DISCARD",
    Name = new LocalizedText("eng", "Tactical Discard").With("zhs", "战术弃牌"),
    Description = new LocalizedText("eng", "Draw 2 cards, then discard 1 card from your hand.")
        .With("zhs", "抽 2 张牌，然后丢弃手牌中的 1 张牌。"),
    Pool = "silent",
    Type = CardType.Skill,
    Rarity = CardRarity.Uncommon,
    Target = CardTargetType.Self,
    EnergyCost = 1,
    Effects =
    {
        new EffectDef { Kind = EffectKind.DrawCards, Target = EffectTarget.Self, Amount = 2 },
        new EffectDef
        {
            Kind = EffectKind.DiscardCardsInPile,
            Target = EffectTarget.Self,
            Amount = 1,
            
        Pick = CardPick.InPile(PileKind.Hand),
        },
    },
});

// 消耗走另一条分支（CardPileCmd.Add(…, PileType.Exhaust)），一并编译到。
project.Cards.Add(new CardDef
{
    Id = "PYRE_OFFERING",
    Name = new LocalizedText("eng", "Pyre Offering").With("zhs", "焚身供品"),
    Description = new LocalizedText("eng", "Exhaust the top card of your draw pile.")
        .With("zhs", "消耗抽牌堆顶的 1 张牌。"),
    Pool = "ironclad",
    Type = CardType.Skill,
    Rarity = CardRarity.Uncommon,
    Target = CardTargetType.Self,
    EnergyCost = 0,
    Effects =
    {
        new EffectDef
        {
            Kind = EffectKind.ExhaustCards,
            Target = EffectTarget.Self,

            // 流程九选"全部"：整堆都消耗，张数用不上（Amount 只是这条效果的数值，
            // 生成代码里不会读它）。
            Amount = 1,
            Pick = CardPick.InPile(PileKind.DrawPile, PickMode.All),
        },
    },
});

// --- 卡 10：移除状态 + 计算型施加状态（带升级）+ 计算型群体伤害 ---
//
// 这张是照着用户实际做坏的那张牌构造的，它同时踩了三个坑：
//   1. 计算型施加状态被升级，但生成代码写的是 DynamicVars["<状态名>"]——那个变量根本
//      没声明过（计算型不走 PowerVar），游戏里一升级就 KeyNotFoundException。
//   2. 计算型伤害的增量变量名字写成了 CalculationExtra，而 CalculatedDamageVar 实际读的是
//      ExtraDamage，同样在运行时炸。
//   3. 就值计算（施加状态量）完全丢掉了"基础值 + 变量 × 变量系数"里的基础值，
//      只返回了来源值。
// 所以这张牌必须出现在回归里，而且下面还要静态扫一遍生成代码里的 DynamicVars 引用。
project.Cards.Add(new CardDef
{
    Id = "ABYSSAL_GAZE",
    Name = new LocalizedText("eng", "Abyssal Gaze").With("zhs", "深渊凝视"),
    Description = new LocalizedText("eng", "Remove Artifact. Apply Doom equal to your missing HP + 6, 3 times. Deal damage equal to your Doom to ALL enemies.")
        .With("zhs", "移除人工制品。\n对目标施加 等于 自身已损失生命 + 6 的灾厄，重复 3 次。\n对所有敌人造成 等于 自身厄运 的伤害。"),
    Pool = "necrobinder",
    Type = CardType.Attack,
    Rarity = CardRarity.Basic,
    Target = CardTargetType.AnyEnemy,
    EnergyCost = 3,
    UpgradeCostDelta = -1,
    Effects =
    {
        new EffectDef
        {
            Kind = EffectKind.RemovePower,
            Target = EffectTarget.Target,
            PowerId = "POWER.ARTIFACT_POWER",
        },
        new EffectDef
        {
            Kind = EffectKind.ApplyPower,
            Target = EffectTarget.Target,
            Amount = 6,
            Source = new ValueSource { Actor = ValueSourceActor.Self, Kind = ValueSourceKind.MissingHp },
            PowerId = "POWER.DOOM_POWER",
            Repeat = 3,
            Upgrade = new EffectUpgrade { AmountDelta = 12 },
        },
        new EffectDef
        {
            Kind = EffectKind.DealDamage,
            Target = EffectTarget.AllEnemies,
            Source = new ValueSource { Actor = ValueSourceActor.Self, Kind = ValueSourceKind.PowerStacks, PowerId = "POWER.DOOM_POWER" },
        },
    },
});

// --- 卡 10d：**同一个状态、不同时机**各加一次力量 ---
//
// 与上一张（同一时机两次）是不同的场景，因为"不同时机"意味着代码落在**不同的钩子**里：
//   打出时    → OnPlay            （牌正在结算，一定在）
//   回合开始时 → AfterSideTurnStart（牌必须还在战斗牌堆里，否则钩子不会派发过来）
//   回合结束时 → AfterSideTurnEnd
//   打出一张牌时 → AfterCardPlayed
//
// 所以两件事要分别成立：
//   1. 施加状态本身不限次数（两条各走各的 PowerCmd.Apply，各算各的量）——与上一张同理；
//   2. 用了钩子的那几条要求这张牌**还在战斗牌堆里**，靠 ShouldReceiveCombatHooks 标记，
//      而这个标记只要有钩子类时机就会发出（见 hasCombatHooks）。
//
// 这里故意把"打出时"与"回合开始时"混在一条牌上：前者不需要钩子、后者需要，
// 正好检验那个标记有没有因为"这条牌已经有 OnPlay 了"而被漏掉。
project.Cards.Add(new CardDef
{
    Id = "MIGHT_BY_STAGES",
    Name = new LocalizedText("eng", "Might by Stages").With("zhs", "逐步壮硕"),
    Description = new LocalizedText("eng", "Gain 2 Strength. At the start of each turn, gain 1 Strength.")
        .With("zhs", "获得 2 点力量。回合开始时，获得 1 点力量。"),
    Pool = "ironclad",
    Type = CardType.Power,
    Rarity = CardRarity.Rare,
    Target = CardTargetType.Self,
    EnergyCost = 2,
    Effects =
    {
        // 打出时：固定 2 层
        new EffectDef
        {
            Kind = EffectKind.ApplyPower, Target = EffectTarget.Self,
            PowerId = "POWER.STRENGTH_POWER",
            Amount = 2,
            Upgrade = new EffectUpgrade { AmountDelta = 1 },
        },

        // 回合开始时：固定 1 层（同一个状态，但时机不同）
        new EffectDef
        {
            Kind = EffectKind.ApplyPower, Target = EffectTarget.Self,
            PowerId = "POWER.STRENGTH_POWER",
            Amount = 1,
            Trigger = EffectTrigger.StartOfTurn,
        },
    },
});

// --- 卡 10c：同一个状态施加两次（力量两条，都是计算型）---
//
// 这是"一张牌两条效果 A、B 都加力量"那种设计。实测结论：**施加状态不受"一种数值一条"的限制**，
// 两条都能生效——因为施加状态走的是 PowerCmd.Apply，每次调用各带各的量，
// 与 DynamicVars 里那个"一个状态一个 PowerVar"是两回事：
//
//   运行时：PowerCmd.Apply<StrengthPower>(..., CalcAmount0(...)) / CalcAmount1(...)
//           两条各算各的量、各施加一次，都生效。
//   PowerVar 只影响**卡面描述的占位符**与预览值：同一个状态只留一个 PowerVar
//           （见下面 powerVars 的去重），所以描述里写 {StrengthPower:diff()} 只会显示其中一个数。
//
// 也就是说：**行为是对的，卡面文字只会显示一个数**。要做"加两次力量"完全可以，
// 但描述不要指望能把两个数都显示出来——这正是原版也不做的事（同一张牌同一个状态加两次很罕见）。
project.Cards.Add(new CardDef
{
    Id = "TWIN_MIGHT",
    Name = new LocalizedText("eng", "Twin Might").With("zhs", "双重伟力"),
    Description = new LocalizedText("eng", "Gain Strength equal to 2 times your Strength plus 2. Gain Strength equal to 4 times your Dexterity plus 4.")
        .With("zhs", "获得 等于 2 × 自身力量 + 2 的力量。获得 等于 4 × 自身敏捷 + 4 的力量。"),
    Pool = "ironclad",
    Type = CardType.Skill,
    Rarity = CardRarity.Rare,
    Target = CardTargetType.Self,
    EnergyCost = 1,
    Effects =
    {
        // A：2 × 力量 + 2（基础值 2 启用）
        new EffectDef
        {
            Kind = EffectKind.ApplyPower, Target = EffectTarget.Self,
            PowerId = "POWER.STRENGTH_POWER",
            Amount = 2,
            Source = new ValueSource { Actor = ValueSourceActor.Self, Kind = ValueSourceKind.PowerStacks, PowerId = "POWER.STRENGTH_POWER", PerUnit = 2 },
            Upgrade = new EffectUpgrade { AmountDelta = 1, PerUnitDelta = 1 },
        },

        // B：4 × 敏捷 + 4（基础值 4 启用）——同一个状态，第二条
        new EffectDef
        {
            Kind = EffectKind.ApplyPower, Target = EffectTarget.Self,
            PowerId = "POWER.STRENGTH_POWER",
            Amount = 4,
            Source = new ValueSource { Actor = ValueSourceActor.Self, Kind = ValueSourceKind.PowerStacks, PowerId = "POWER.DEXTERITY_POWER", PerUnit = 4 },
            Upgrade = new EffectUpgrade { AmountDelta = 3, PerUnitDelta = 2 },
        },
    },
});

// --- 卡 10b：两个**固定值**伤害效果（同一张牌上）---
//
// 这张卡是给"一张牌能不能有多个固定值伤害"这个问题做实测的。结论：**可以**——
// 每条效果各占一个变量（Damage / Damage2），各是各的数值、各升各的。
//
// 它原先是个**错误**：生成器把所有同种效果合并到一个变量，"4 点两次 + 9 点"会变成
// "9 点两次 + 9 点"（变量取较大值，小的被顶上去），升级两行也叠在同一个变量上。
// 后来改成按效果分配槽位，这类牌就能正常做出来了。
//
// 多段**同值**攻击仍然是"一条效果 + 重复次数"（原版"千刀万剐"就是 1 点伤害重复 3 次），
// 那是更简洁的写法；这里这张是"多段不同值"，只能靠多个槽。
project.Cards.Add(new CardDef
{
    Id = "TWIN_STRIKE",
    Name = new LocalizedText("eng", "Twin Strike").With("zhs", "双重打击"),
    Description = new LocalizedText("eng", "Deal 4 damage twice, then 9 damage.")
        .With("zhs", "造成 4 点伤害两次，再造成 9 点伤害。"),
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
            Amount = 4, Repeat = 2,
            Upgrade = new EffectUpgrade { AmountDelta = 2, RepeatDelta = 1 },
        },
        new EffectDef
        {
            Kind = EffectKind.DealDamage, Target = EffectTarget.Target,
            Amount = 9,
            Upgrade = new EffectUpgrade { AmountDelta = 3 },
        },
    },
});

// --- 卡 11：计算型伤害**且**升级 ---
//
// 计算型伤害的增量变量是 ExtraDamage（CalculatedDamageVar 覆写了 GetExtraVar），
// 不是 CalculationExtra。写错的话编译照样通过，但升级的一瞬间游戏会
// KeyNotFoundException。所以必须有一张"计算型伤害 + 升级增量"的牌覆盖这条路径。
project.Cards.Add(new CardDef
{
    Id = "SWELLING_WRATH",
    Name = new LocalizedText("eng", "Swelling Wrath").With("zhs", "膨胀怒火"),
    Description = new LocalizedText("eng", "Deal 4 damage plus 2 damage for each card in your hand.")
        .With("zhs", "造成 4 点伤害，每有一张手牌额外造成 2 点。"),
    Pool = "ironclad",
    Type = CardType.Attack,
    Rarity = CardRarity.Uncommon,
    Target = CardTargetType.AnyEnemy,
    EnergyCost = 1,
    Effects =
    {
        new EffectDef
        {
            Kind = EffectKind.DealDamage,
            Target = EffectTarget.Target,
            Amount = 4,
            Source = new ValueSource { Actor = ValueSourceActor.Self, Kind = ValueSourceKind.HandCount, PerUnit = 2 },
            Upgrade = new EffectUpgrade { AmountDelta = 2, PerUnitDelta = 1 },
        },
    },
});

// --- 卡 12：计算型格挡 + 升级（增量变量是 CalculationExtra，与上面那张相反）---
project.Cards.Add(new CardDef
{
    Id = "SCALED_BULWARK",
    Name = new LocalizedText("eng", "Scaled Bulwark").With("zhs", "鳞甲壁垒"),
    Description = new LocalizedText("eng", "Gain 5 Block plus 1 Block for each card in your hand.")
        .With("zhs", "获得 5 点格挡，每有一张手牌额外获得 1 点。"),
    Pool = "ironclad",
    Type = CardType.Skill,
    Rarity = CardRarity.Uncommon,
    Target = CardTargetType.Self,
    EnergyCost = 1,
    Effects =
    {
        new EffectDef
        {
            Kind = EffectKind.GainBlock,
            Target = EffectTarget.Self,
            Amount = 5,
            Source = new ValueSource { Actor = ValueSourceActor.Self, Kind = ValueSourceKind.HandCount, PerUnit = 1 },
            Upgrade = new EffectUpgrade { AmountDelta = 3, PerUnitDelta = 1 },
        },
    },
});

// --- 卡 13：计算型伤害 **和** 计算型格挡同时在 ---
//
// 这两个计算变量各有各的增量变量（ExtraDamage / CalculationExtra），基础值则按**全局**顺序
// 各占一个（伤害拿 CalculationBase、格挡拿 CalculationBase2）。
//
// 这张牌原先带着一个静默缺陷：槽位类是按"同类里第几个"选的，两者各自都是同类第一个，
// 于是格挡也拿了原版类型——而原版类型写死了读 CalculationBase，实际读到的是**伤害**的基础值。
// 编译正常、卡面预览还"看起来对"（预览读自己那个变量），只在结算时现形。
// 现在判据换成"这个槽有没有占到原版名字"，第二个及以后的槽一律走运行时子类。
project.Cards.Add(new CardDef
{
    Id = "TEMPERED_STANCE",
    Name = new LocalizedText("eng", "Tempered Stance").With("zhs", "淬火架势"),
    Description = new LocalizedText("eng", "Deal 3 damage and gain 3 Block, plus 1 each for every card in your hand.")
        .With("zhs", "造成 3 点伤害并获得 3 点格挡，每有一张手牌各额外 1 点。"),
    Pool = "ironclad",
    Type = CardType.Attack,
    Rarity = CardRarity.Rare,
    Target = CardTargetType.AnyEnemy,
    EnergyCost = 1,
    Effects =
    {
        new EffectDef
        {
            Kind = EffectKind.DealDamage,
            Target = EffectTarget.Target,
            Amount = 3,
            Source = new ValueSource { Actor = ValueSourceActor.Self, Kind = ValueSourceKind.HandCount },
            Upgrade = new EffectUpgrade { AmountDelta = 2, PerUnitDelta = 1 },
        },
        new EffectDef
        {
            Kind = EffectKind.GainBlock,
            Target = EffectTarget.Self,
            Amount = 3,
            Source = new ValueSource { Actor = ValueSourceActor.Self, Kind = ValueSourceKind.HandCount },
            Upgrade = new EffectUpgrade { AmountDelta = 2, PerUnitDelta = 1 },
        },
    },
});

// --- 卡 13b：两个**计算型**伤害，且**第二个也带基础值**---
//
// 与卡 21 互补：那张的两条都不带基础值（纯系数），这张的第二条带基础值，
// 于是 CalculationBase2 里躺着一个真数、升级也真的去改它。
// 这条路径原先整段蒸发（算式被第一条顶掉），现在由运行时子类承载。
project.Cards.Add(new CardDef
{
    Id = "CASCADING_RECKONING",
    Name = new LocalizedText("eng", "Cascading Reckoning").With("zhs", "渐次清算"),
    Description = new LocalizedText("eng", "Deal 3 damage plus 2 damage per card in your hand, then deal 4 damage plus 1 damage per missing HP.")
        .With("zhs", "造成 3 点伤害，每有一张手牌额外 2 点；再造成 4 点伤害，每点已损失生命额外 1 点。"),
    Pool = "ironclad",
    Type = CardType.Attack,
    Rarity = CardRarity.Rare,
    Target = CardTargetType.AnyEnemy,
    EnergyCost = 2,
    Effects =
    {
        // 第一条：2 × 手牌数 + 3
        new EffectDef
        {
            Kind = EffectKind.DealDamage, Target = EffectTarget.Target,
            Amount = 3,
            Source = new ValueSource { Actor = ValueSourceActor.Self, Kind = ValueSourceKind.HandCount, PerUnit = 2 },
            Upgrade = new EffectUpgrade { AmountDelta = 1, PerUnitDelta = 1 },
        },

        // 第二条：1 × 目标已损失生命 + 4（基础值进 CalculationBase2）
        new EffectDef
        {
            Kind = EffectKind.DealDamage, Target = EffectTarget.Target,
            Amount = 4,
            Source = new ValueSource { Actor = ValueSourceActor.Target, Kind = ValueSourceKind.MissingHp, PerUnit = 1 },
            Upgrade = new EffectUpgrade { AmountDelta = 2, PerUnitDelta = 0.5m },
        },
    },
});

// --- 卡 14：使某种牌的伤害增加（按标签 / 指定牌 / 不限）---
//
// 用的是原版爪击（Claw）/ 撕咬（Maul）的做法：直接改战斗牌堆里那些牌的伤害值，
// 不合成状态。所以这里验证的是"遍历战斗中的牌 + 按标签/指定牌筛选 + 加值"能编译。
project.Cards.Add(new CardDef
{
    Id = "SHARPEN_DISCIPLE",
    Name = new LocalizedText("eng", "Sharpen Disciple").With("zhs", "磨砺门徒"),
    Description = new LocalizedText("eng", "Your Strikes deal {Increase:diff()} additional damage this combat. Gain 1 Shiv.")
        .With("zhs", "使打击牌在本场战斗中的伤害增加 {Increase:diff()} 点。获得 1 张小刀。"),
    Pool = "silent",
    Type = CardType.Power,
    Rarity = CardRarity.Uncommon,
    Target = CardTargetType.Self,
    EnergyCost = 1,
    Effects =
    {
        new EffectDef
        {
            Kind = EffectKind.BoostCardDamage,
            Target = EffectTarget.Self,
            Pick = new CardPick(),
            Amount = 3,
            CardFilter = CardFilterKind.TagStrike,
            CostDuration = CostDuration.ThisCombat,
            Upgrade = new EffectUpgrade { AmountDelta = 1 },
        },
    },
});

// 指定具体某一张牌 + 不限范围的两种筛选，一并编译到。
project.Cards.Add(new CardDef
{
    Id = "BLADE_DEVOTION",
    Name = new LocalizedText("eng", "Blade Devotion").With("zhs", "刀刃虔诚"),
    Description = new LocalizedText("eng", "Your Shivs deal 2 additional damage. All your attacks deal 1 additional damage.")
        .With("zhs", "使小刀伤害增加 2 点。使你的所有攻击伤害增加 1 点。"),
    Pool = "silent",
    Type = CardType.Power,
    Rarity = CardRarity.Rare,
    Target = CardTargetType.Self,
    EnergyCost = 2,
    Effects =
    {
        new EffectDef
        {
            Kind = EffectKind.BoostCardDamage,
            Target = EffectTarget.Self,
            Pick = new CardPick(),
            Amount = 2,
            CardFilter = CardFilterKind.TagShiv,
            CostDuration = CostDuration.ThisCombat,
        },
        new EffectDef
        {
            Kind = EffectKind.BoostCardDamage,
            Target = EffectTarget.Self,
            Pick = new CardPick(),
            Amount = 1,
            CardFilter = CardFilterKind.Any,
            CostDuration = CostDuration.ThisCombat,
        },
    },
});

// 指定某一张牌：验证"按卡牌类型筛选"的那条分支（原版爪击就是只作用于自己）。
project.Cards.Add(new CardDef
{
    Id = "CLAW_DOCTRINE",
    Name = new LocalizedText("eng", "Claw Doctrine").With("zhs", "爪击教义"),
    Description = new LocalizedText("eng", "Deal 3 damage. Increase the damage of ALL Claw cards by {Increase:diff()} this combat.")
        .With("zhs", "造成 3 点伤害。本场战斗中所有爪击卡牌的伤害增加 {Increase:diff()} 点。"),
    Pool = "ironclad",
    Type = CardType.Attack,
    Rarity = CardRarity.Common,
    Target = CardTargetType.AnyEnemy,
    EnergyCost = 1,
    Effects =
    {
        new EffectDef
        {
            Kind = EffectKind.DealDamage,
            Target = EffectTarget.Target,
            Amount = 3,
        },
        new EffectDef
        {
            Kind = EffectKind.BoostCardDamage,
            Target = EffectTarget.Self,
            Pick = CardPick.Named(),
            Amount = 2,
            CardId = "CARD.CLAW",
            CostDuration = CostDuration.ThisCombat,
        },
    },
});

// --- 卡 15：立即执行灾厄判定 ---
project.Cards.Add(new CardDef
{
    Id = "FINAL_JUDGEMENT",
    Name = new LocalizedText("eng", "Final Judgement").With("zhs", "终末审判"),
    Description = new LocalizedText("eng", "Trigger Doom on the target.")
        .With("zhs", "立即对目标执行灾厄判定。"),
    Pool = "necrobinder",
    Type = CardType.Skill,
    Rarity = CardRarity.Uncommon,
    Target = CardTargetType.AnyEnemy,
    EnergyCost = 0,
    Effects =
    {
        new EffectDef
        {
            Kind = EffectKind.TriggerDoom,
            Target = EffectTarget.Target,
        },
    },
});

// --- 卡 16：自动打出牌堆中的牌 ---
project.Cards.Add(new CardDef
{
    Id = "RECITE_FROM_MEMORY",
    Name = new LocalizedText("eng", "Recite From Memory").With("zhs", "背诵"),
    Description = new LocalizedText("eng", "Auto-play the top card of your draw pile.")
        .With("zhs", "自动打出抽牌堆顶的 1 张牌。"),
    Pool = "ironclad",
    Type = CardType.Skill,
    Rarity = CardRarity.Rare,
    Target = CardTargetType.Self,
    EnergyCost = 1,
    Effects =
    {
        new EffectDef
        {
            // 自动打出 → 某个牌堆：从抽牌堆打 1 张。
            Kind = EffectKind.AutoPlay,
            Target = EffectTarget.Self,
            Amount = 1,
            Pick = CardPick.InPile(PileKind.DrawPile),
        },
    },
});

// --- 卡 16a：复制牌并加入牌堆（选牌器）---
//
// 复制谁由作用对象决定（此牌 / 某个牌堆 / 指定牌名 / 按条件）。
// 生成上要注意：副本是**新生成的牌**，所以走 AddGeneratedCardToCombat（它会写战斗历史），
// 而不是普通的 Add；amount 是**每张**复制几份，所以两层循环。
project.Cards.Add(new CardDef
{
    Id = "TWINNED_ECHO",
    Name = new LocalizedText("eng", "Twinned Echo").With("zhs", "双生回响"),
    Description = new LocalizedText("eng", "Add 2 copies of this card to your hand.")
        .With("zhs", "复制此牌 2 份并加入手牌。"),
    Pool = "ironclad",
    Type = CardType.Skill,
    Rarity = CardRarity.Rare,
    Target = CardTargetType.Self,
    EnergyCost = 1,
    Effects =
    {
        new EffectDef
        {
            Kind = EffectKind.CopyCardsToPile,
            Target = EffectTarget.Self,
            Pick = CardPick.This(),
            Amount = 2,
            TargetPile = PileKind.Hand,
        },
    },
});

// 复制"某个牌堆里挑出来的牌"：作用对象 = 牌堆，带作用方式与张数。
project.Cards.Add(new CardDef
{
    Id = "MIRRORED_DRAW",
    Name = new LocalizedText("eng", "Mirrored Draw").With("zhs", "镜像抽取"),
    Description = new LocalizedText("eng", "Choose a card in your hand and add 1 copy of it to your draw pile.")
        .With("zhs", "选择 1 张手牌，复制 1 份加入抽牌堆顶。"),
    Pool = "ironclad",
    Type = CardType.Skill,
    Rarity = CardRarity.Uncommon,
    Target = CardTargetType.Self,
    EnergyCost = 1,
    Effects =
    {
        new EffectDef
        {
            Kind = EffectKind.CopyCardsToPile,
            Target = EffectTarget.Self,
            Pick = CardPick.InPile(PileKind.Hand, PickMode.Choose, 1),
            Amount = 1,
            TargetPile = PileKind.DrawPile,
            Placement = PilePlacement.Top,
        },
    },
});

// --- 卡 16b：自动打出 + 重复（重复的是"打出"，不是整条效果）---
//
// 自动打出的重复必须是"把打出的动作再做几轮"，**不能重新选牌**。套通用重复循环的话，
// "取牌堆"会被一起重复（等于每轮重新挑一次牌堆），那不是这个意思。
// 所以生成的代码应当是：牌堆查询在循环外，里面是"打 N 张 × 重复 R 轮"。
project.Cards.Add(new CardDef
{
    Id = "REPEATED_RECITAL",
    Name = new LocalizedText("eng", "Repeated Recital").With("zhs", "反复吟诵"),
    Description = new LocalizedText("eng", "Auto-play 2 cards from your draw pile, 2 times.")
        .With("zhs", "自动打出抽牌堆中的 2 张牌，重复 2 次。"),
    Pool = "ironclad",
    Type = CardType.Skill,
    Rarity = CardRarity.Rare,
    Target = CardTargetType.Self,
    EnergyCost = 1,
    Effects =
    {
        new EffectDef
        {
            Kind = EffectKind.AutoPlay,
            Target = EffectTarget.Self,
            Pick = CardPick.InPile(PileKind.Hand),
            Amount = 2,
            
            Repeat = 2,
        },
    },
});

// --- 卡 16c：卡牌自己的重放属性（不是效果）---
//
// "重放"是**卡牌属性**（CardModel.BaseReplayCount，与费用、关键词并列），
// 不是某条效果：它说的是"这张牌被打出时执行几遍"。原版 578 张里没有一张天生带它，
// 只被"隐藏宝石""变形""士兵炖菜"这类效果临时加上去。
//
// 生成上是一条 [SavedProperty] 自有属性：setter 把它写进 BaseReplayCount，标准值由
// AfterCreated 给（那个钩子要求"已可变"的实例）。**不能只写在 AfterCreated 里**——
// 游戏源码注明它不在读档时调用（读档走 FromSerializable → AfterDeserialized），
// 只写那儿的话读档后重放次数会丢。
project.Cards.Add(new CardDef
{
    Id = "DOUBLE_TAP_STRIKE",
    Name = new LocalizedText("eng", "Twin Execution").With("zhs", "双重执行"),
    Description = new LocalizedText("eng", "Deal 5 damage. This card is played twice.")
        .With("zhs", "造成 5 点伤害。这张牌被打出两次。"),
    Pool = "ironclad",
    Type = CardType.Attack,
    Rarity = CardRarity.Uncommon,
    Target = CardTargetType.AnyEnemy,
    EnergyCost = 1,
    ReplayCount = 1,
    Effects =
    {
        new EffectDef { Kind = EffectKind.DealDamage, Target = EffectTarget.Target, Amount = 5 },
    },
});

// 给**别的牌**加重放（效果版）：与本卡自身重放是两件事，这条改目标牌的字段。
project.Cards.Add(new CardDef
{
    Id = "ECHO_BLESSING",
    Name = new LocalizedText("eng", "Echo Blessing").With("zhs", "回响祝福"),
    Description = new LocalizedText("eng", "Your cards in hand are played 1 additional time this combat.")
        .With("zhs", "使手牌中所有牌在本场战斗中额外被打出 1 次。"),
    Pool = "ironclad",
    Type = CardType.Skill,
    Rarity = CardRarity.Rare,
    Target = CardTargetType.Self,
    EnergyCost = 1,
    Effects =
    {
        new EffectDef
        {
            Kind = EffectKind.AddReplay,
            Target = EffectTarget.Self,
            Pick = CardPick.InPile(PileKind.Hand, PickMode.All),
            Amount = 1,
            CostDuration = CostDuration.ThisCombat,
        },
    },
});

// 永久版：写进目标牌自己的 [SavedProperty] 字段（只有本 mod 的牌有）。
// 与永久增伤同一套运行时接口，但**不**覆写 AfterDowngraded——重放是普通字段，
// 升级/降级不会重建它，补差值反而会多加。
project.Cards.Add(new CardDef
{
    Id = "ETERNAL_ECHO",
    Name = new LocalizedText("eng", "Eternal Echo").With("zhs", "永恒回响"),
    Description = new LocalizedText("eng", "Permanently play your Strikes 1 additional time.")
        .With("zhs", "使打击牌永久额外被打出 1 次。"),
    Pool = "ironclad",
    Type = CardType.Skill,
    Rarity = CardRarity.Rare,
    Target = CardTargetType.Self,
    EnergyCost = 1,
    Effects =
    {
        new EffectDef
        {
            Kind = EffectKind.AddReplay,
            Target = EffectTarget.Self,
            Pick = new CardPick { Tags = { CardTag.Strike } },
            Amount = 1,
            CostDuration = CostDuration.Permanent,
        },
    },
});

// --- 卡 17：消耗牌 + 维度筛选 + 至多 + 计算型张数（界面上"十流程"真正会拼出来的那种组合）---
//
// 单牌堆 + 维度筛选 + 至多：走 FromSimpleGrid（自己拼的候选列表 + 维度谓词），
// 而"至多 N 张"是 CardSelectorPrefs(prompt, 0, N)；N 还是算出来的（手牌数 × 0.5）。
// 这一条是拿真程序集编译的：三个决定各自都有别的夹具覆盖，但**组合起来**的这条只有这里验。
project.Cards.Add(new CardDef
{
    Id = "SMART_EXHAUST",
    Name = new LocalizedText("eng", "Smart Exhaust").With("zhs", "顺手消耗"),
    Description = new LocalizedText("eng", "Exhaust up to half your hand (Attack cards only).")
        .With("zhs", "至多消耗手牌中一半的攻击牌（张数 = 手牌数 × 0.5）。"),
    Pool = "ironclad",
    Type = CardType.Skill,
    Rarity = CardRarity.Uncommon,
    Target = CardTargetType.Self,
    EnergyCost = 1,
    Effects =
    {
        new EffectDef
        {
            Kind = EffectKind.ExhaustCards,
            Target = EffectTarget.Self,

            // 张数就是这条效果的数值：1 + 手牌数 × 0.5（变量来源走变量模块，
            // 选牌器自己不带变量——见 CardPickPolicy.CountFromEffect）。
            Amount = 1,
            Source = new ValueSource
            {
                Actor = ValueSourceActor.Self,
                Kind = ValueSourceKind.HandCount,
                PerUnit = 0.5m,
            },
            Pick = new CardPick
            {
                Source = PickSource.Piles,
                Piles = { PileKind.Hand },
                Types = { CardType.Attack },
                Mode = PickMode.AtMost,
            },
        },
    },
});

// --- 卡 18：回合开始/结束 + 牌堆范围 ---
//
// 从前"回合结束时若在手牌中"是独立的触发时机；现在它是"回合结束时 + 牌堆范围 = 手牌"。
// 四种范围都生成一遍，确认 PileType 的映射没写错。
project.Cards.Add(new CardDef
{
    Id = "PERSISTENT_VIGIL",
    Name = new LocalizedText("eng", "Persistent Vigil").With("zhs", "恒常守望"),
    Description = new LocalizedText("eng", "At the start of your turn, if this card is in your draw pile, draw 1 card. At the end of your turn, if this card is in your hand, gain 3 Block.")
        .With("zhs", "回合开始时，若这张牌在抽牌堆中，抽 1 张牌。回合结束时，若这张牌在手牌中，获得 3 点格挡。"),
    Pool = "ironclad",
    Type = CardType.Skill,
    Rarity = CardRarity.Uncommon,
    Target = CardTargetType.Self,
    EnergyCost = 1,
    Effects =
    {
        new EffectDef
        {
            Kind = EffectKind.DrawCards,
            Target = EffectTarget.Self,
            Amount = 1,
            Trigger = EffectTrigger.StartOfTurn,
            PileScope = PileScope.DrawPile,
        },
        new EffectDef
        {
            Kind = EffectKind.GainBlock,
            Target = EffectTarget.Self,
            Amount = 3,
            Trigger = EffectTrigger.EndOfTurn,
            PileScope = PileScope.Hand,
        },
    },
});

// --- 卡 17b/17c：让这张牌自己从牌堆里出来（原版轰击那种）---
//
// 与上一条"自动打出牌堆中的牌"是两件事：那条打出的是**别的牌**，这条打出的是**这张牌自己**。
// 原版轰击写的是"你的回合开始时，如果这张牌在你的消耗牌堆中，则将其打出"。
//
// 两种回合时机各生成一张：它们走的是引擎里不同的钩子
// （AfterAutoPrePlayPhaseEnteredEarly / AfterAutoPostPlayPhaseEntered），
// 只测一个的话另一个分支写错了也不会被发现。
project.Cards.Add(new CardDef
{
    Id = "RESURFACE",
    Name = new LocalizedText("eng", "Resurface").With("zhs", "浮出水面"),
    Description = new LocalizedText("eng", "At the start of your turn, if this card is in your exhaust pile, play it.")
        .With("zhs", "回合开始时，若这张牌在消耗牌堆中，将自己打出。"),
    Pool = "ironclad",
    Type = CardType.Attack,
    Rarity = CardRarity.Rare,
    Target = CardTargetType.AnyEnemy,
    EnergyCost = 2,
    Keywords = { CardKeyword.Exhaust },
    Effects =
    {
        new EffectDef
        {
            Kind = EffectKind.DealDamage,
            Target = EffectTarget.Target,
            Amount = 8,
        },
        new EffectDef
        {
            // 自动打出 → 此牌（原版轰击那种）。
            Kind = EffectKind.AutoPlay,
            Target = EffectTarget.Self,
            Pick = CardPick.This(),
            Trigger = EffectTrigger.StartOfTurn,
            PileScope = PileScope.ExhaustPile,
        },
    },
});

// --- 卡 17b：回合开始时把此牌重复打出 2 次 ---
//
// "重复打出次数"对"此牌"这一档**是有效的**："若在消耗堆中，将此牌重复打出 2 次"。
// 生成上要注意两点：
//   1. 牌堆条件（Pile?.Type）只判**一次**——打完一张它就离开那个牌堆了，
//      每轮重判会导致第二次不触发；
//   2. 循环体里只有打出这一句。
project.Cards.Add(new CardDef
{
    Id = "ECHOING_PRAYER",
    Name = new LocalizedText("eng", "Echoing Prayer").With("zhs", "回响祷言"),
    Description = new LocalizedText("eng", "At the start of your turn, if this card is in your exhaust pile, play it twice.")
        .With("zhs", "回合开始时，若这张牌在消耗堆中，将此牌打出 2 次。"),
    Pool = "necrobinder",
    Type = CardType.Skill,
    Rarity = CardRarity.Rare,
    Target = CardTargetType.Self,
    EnergyCost = 1,
    Effects =
    {
        new EffectDef
        {
            Kind = EffectKind.AutoPlay,
            Target = EffectTarget.Self,
            Pick = CardPick.This(),
            Trigger = EffectTrigger.StartOfTurn,
            PileScope = PileScope.ExhaustPile,
            Repeat = 2,
        },
    },
});

project.Cards.Add(new CardDef
{
    Id = "LAST_WORD",
    Name = new LocalizedText("eng", "Last Word").With("zhs", "遗言"),
    Description = new LocalizedText("eng", "At the end of your turn, if this card is in your discard pile, play it.")
        .With("zhs", "回合结束时，若这张牌在弃牌堆中，将自己打出。"),
    Pool = "necrobinder",
    Type = CardType.Skill,
    Rarity = CardRarity.Uncommon,
    Target = CardTargetType.Self,
    EnergyCost = 1,
    Effects =
    {
        new EffectDef
        {
            Kind = EffectKind.GainBlock,
            Target = EffectTarget.Self,
            Amount = 6,
        },
        new EffectDef
        {
            Kind = EffectKind.AutoPlay,
            Target = EffectTarget.Self,
            Pick = CardPick.This(),
            Trigger = EffectTrigger.EndOfTurn,
            PileScope = PileScope.DiscardPile,
        },
    },
});

// --- 卡 17d：同一时机、两条**牌堆范围不同**的效果 ---
//
// 这一张是回归用：曾经生成器会在钩子开头按"第一条有范围的效果"整段早退，于是范围不同的
// 后一条变成死代码——编译完全正常，只是永远不生效。所以这条必须单独存在：
// 若哪天那个整段判断被加回来，下面的断言会立刻失败。
project.Cards.Add(new CardDef
{
    Id = "TWO_SCOPES",
    Name = new LocalizedText("eng", "Two Scopes").With("zhs", "两种范围"),
    Description = new LocalizedText("eng", "At the start of your turn: if this card is in your draw pile, draw 1 card; if it is in your hand, gain 3 Block.")
        .With("zhs", "回合开始时：若这张牌在抽牌堆中，抽 1 张牌；若在手牌中，获得 3 点格挡。"),
    Pool = "ironclad",
    Type = CardType.Skill,
    Rarity = CardRarity.Uncommon,
    Target = CardTargetType.Self,
    EnergyCost = 1,
    Effects =
    {
        new EffectDef
        {
            Kind = EffectKind.DrawCards,
            Target = EffectTarget.Self,
            Amount = 1,
            Trigger = EffectTrigger.StartOfTurn,
            PileScope = PileScope.DrawPile,
        },
        new EffectDef
        {
            Kind = EffectKind.GainBlock,
            Target = EffectTarget.Self,
            Amount = 3,
            Trigger = EffectTrigger.StartOfTurn,
            PileScope = PileScope.Hand,
        },
    },
});

// --- 卡 17e：诅咒牌那种"回合结束时若在手牌中就受伤" ---
//
// 这是原版诅咒牌（灼伤/疑虑/腐朽/悔恨）的通用形状，只有一条效果、没有"打出时"的效果。
// 它验证的是"只靠 OnTurnEndInHand 的牌"也能生成完整（不用挂打出钩子、也不用战斗钩子）。
project.Cards.Add(new CardDef
{
    Id = "LINGERING_DOUBT",
    Name = new LocalizedText("eng", "Lingering Doubt").With("zhs", "萦怀之疑"),
    Description = new LocalizedText("eng", "At the end of your turn, if this card is in your hand, lose 3 HP.")
        .With("zhs", "在你的回合结束时，如果这张牌在你的手牌中，失去 3 点生命。"),
    Pool = "curse",
    Type = CardType.Curse,
    Rarity = CardRarity.Curse,
    Target = CardTargetType.None,
    EnergyCost = -1,
    Effects =
    {
        new EffectDef
        {
            Kind = EffectKind.LoseHp,
            Target = EffectTarget.Self,
            Amount = 3,
            Trigger = EffectTrigger.EndOfTurn,
            PileScope = PileScope.Hand,
        },
    },
});

// --- 卡 17f：费用类效果（降费 / 加费 / 免费）---
//
// 覆盖三件事，每一件都对应游戏里不同的接口：
//   降费   -> CardEnergyCost.AddThisTurnOrUntilPlayed（只碰能量）
//   免费   -> CardModel.SetToFreeThisTurn（同时清能量与星辰）
//   挑牌   -> 全部 / 随机 N 张 / 选择 N 张 三条取牌路径
// 三种持续范围（本回合 / 本场战斗 / 直到打出）也要各生成一遍，否则某个分支写错不会被发现。
project.Cards.Add(new CardDef
{
    Id = "BULLET_TIME_STYLE",
    Name = new LocalizedText("eng", "Hair Trigger").With("zhs", "千钧一发"),
    Description = new LocalizedText("eng", "Reduce the cost of ALL cards in your hand by 1 this turn.")
        .With("zhs", "本回合手牌中所有牌的费用降低 1 点。"),
    Pool = "silent",
    Type = CardType.Skill,
    Rarity = CardRarity.Uncommon,
    Target = CardTargetType.Self,
    EnergyCost = 2,
    Effects =
    {
        new EffectDef
        {
            Kind = EffectKind.ModifyCardCost,
            Target = EffectTarget.Self,
            Amount = -1,
            
            
            CostDuration = CostDuration.ThisTurn,
        Pick = CardPick.InPile(PileKind.Hand, PickMode.All),
        },
    },
});

project.Cards.Add(new CardDef
{
    Id = "ENLIGHTENED_FORGE",
    Name = new LocalizedText("eng", "Enlightened Forge").With("zhs", "启蒙熔炉"),
    Description = new LocalizedText("eng", "Reduce the cost of 2 random cards in your draw pile by 1 this combat.")
        .With("zhs", "本场战斗中抽牌堆里随机 2 张牌的费用降低 1 点。"),
    Pool = "ironclad",
    Type = CardType.Skill,
    Rarity = CardRarity.Rare,
    Target = CardTargetType.Self,
    EnergyCost = 1,
    Effects =
    {
        new EffectDef
        {
            Kind = EffectKind.ModifyCardCost,
            Target = EffectTarget.Self,
            Amount = -1,
            
            
            Pick = CardPick.InPile(PileKind.DrawPile, PickMode.Random, 2),
            CostDuration = CostDuration.ThisCombat,
        },
    },
});

project.Cards.Add(new CardDef
{
    Id = "TOLL_OF_THE_PILE",
    Name = new LocalizedText("eng", "Toll of the Pile").With("zhs", "牌堆之税"),
    Description = new LocalizedText("eng", "Choose a card in your discard pile. It costs 2 more until played.")
        .With("zhs", "选择弃牌堆中的 1 张牌，它的费用提高 2 点直到被打出。"),
    Pool = "necrobinder",
    Type = CardType.Skill,
    Rarity = CardRarity.Uncommon,
    Target = CardTargetType.Self,
    EnergyCost = 1,
    Effects =
    {
        new EffectDef
        {
            Kind = EffectKind.ModifyCardCost,
            Target = EffectTarget.Self,
            Amount = 2,
            
            
            Pick = CardPick.InPile(PileKind.DiscardPile, PickMode.Choose, 1),
            CostDuration = CostDuration.UntilPlayed,
        },
    },
});

// 免费：与原版"子弹时间"同形（本回合），另一张用本场战斗，两条接口都要生成到。
project.Cards.Add(new CardDef
{
    Id = "FREEHAND_CHANNEL",
    Name = new LocalizedText("eng", "Freehand Channel").With("zhs", "信手导引"),
    Description = new LocalizedText("eng", "ALL cards in your hand cost 0 this turn.")
        .With("zhs", "本回合手牌中所有牌的费用变为 0。"),
    Pool = "defect",
    Type = CardType.Skill,
    Rarity = CardRarity.Rare,
    Target = CardTargetType.Self,
    EnergyCost = 3,
    Effects =
    {
        new EffectDef
        {
            Kind = EffectKind.SetCardsFree,
            Target = EffectTarget.Self,
            
            
            CostDuration = CostDuration.ThisTurn,
        Pick = CardPick.InPile(PileKind.Hand, PickMode.All),
        },
    },
});

project.Cards.Add(new CardDef
{
    Id = "ETERNAL_GRATIS",
    Name = new LocalizedText("eng", "Eternal Gratis").With("zhs", "恒久免费"),
    Description = new LocalizedText("eng", "Choose 2 cards in your draw pile. They cost 0 this combat.")
        .With("zhs", "选择抽牌堆中的 2 张牌，它们本场战斗的费用变为 0。"),
    Pool = "regent",
    Type = CardType.Skill,
    Rarity = CardRarity.Rare,
    Target = CardTargetType.Self,
    EnergyCost = 2,
    Effects =
    {
        new EffectDef
        {
            Kind = EffectKind.SetCardsFree,
            Target = EffectTarget.Self,
            
            
            Pick = CardPick.InPile(PileKind.DrawPile, PickMode.Choose, 2),
            CostDuration = CostDuration.ThisCombat,
        },
    },
});

// --- 卡 17g：升级牌堆里的牌（原版"神化"/"武装"）---
//
// 三种挑选方式各生成一张，因为升级那条走的是 CardCmd.Upgrade（它自己会跳过不可升级的牌，
// 所以生成代码里**不该**出现我们自己的"能不能升级"判断）。
project.Cards.Add(new CardDef
{
    Id = "APOTHEOSIS_STYLE",
    Name = new LocalizedText("eng", "Ascendant Bloom").With("zhs", "登峰之华"),
    Description = new LocalizedText("eng", "Upgrade ALL cards in your hand.")
        .With("zhs", "升级手牌中所有牌。"),
    Pool = "ironclad",
    Type = CardType.Skill,
    Rarity = CardRarity.Rare,
    Target = CardTargetType.Self,
    EnergyCost = 2,
    Effects =
    {
        new EffectDef
        {
            Kind = EffectKind.UpgradeCardsInPile,
            Target = EffectTarget.Self,
            
            
        Pick = CardPick.InPile(PileKind.Hand, PickMode.All),
        },
    },
});

project.Cards.Add(new CardDef
{
    Id = "FIELD_ARMING",
    Name = new LocalizedText("eng", "Field Arming").With("zhs", "临阵武装"),
    Description = new LocalizedText("eng", "Choose a card in your hand to upgrade.")
        .With("zhs", "选择手牌中的 1 张牌升级。"),
    Pool = "ironclad",
    Type = CardType.Skill,
    Rarity = CardRarity.Common,
    Target = CardTargetType.Self,
    EnergyCost = 1,
    Effects =
    {
        new EffectDef
        {
            Kind = EffectKind.UpgradeCardsInPile,
            Target = EffectTarget.Self,
            
            
            Pick = CardPick.InPile(PileKind.Hand, PickMode.Choose, 1),
        },
    },
});

project.Cards.Add(new CardDef
{
    Id = "SCATTERED_INSIGHT",
    Name = new LocalizedText("eng", "Scattered Insight").With("zhs", "零星顿悟"),
    Description = new LocalizedText("eng", "Upgrade 2 random cards in your draw pile.")
        .With("zhs", "升级抽牌堆中随机 2 张牌。"),
    Pool = "defect",
    Type = CardType.Skill,
    Rarity = CardRarity.Uncommon,
    Target = CardTargetType.Self,
    EnergyCost = 1,
    Effects =
    {
        new EffectDef
        {
            Kind = EffectKind.UpgradeCardsInPile,
            Target = EffectTarget.Self,
            
            
            Pick = CardPick.InPile(PileKind.DrawPile, PickMode.Random, 2),
        },
    },
});

// --- 卡 17h：变化牌堆里的牌（原版"冲锋！！"指定牌 / "降灵"随机）---
//
// 两条路径都要生成：随机走 TransformToRandom，指定走 TransformTo<T>。
// 指定那条还带"升级版"（原版"冲锋！！"升级后会顺手把变出来的牌也升一级）。
project.Cards.Add(new CardDef
{
    Id = "SUMMON_THE_RANKS",
    Name = new LocalizedText("eng", "Summon the Ranks").With("zhs", "召来卒伍"),
    Description = new LocalizedText("eng", "Transform 2 cards in your draw pile into Minion Dive Bombs.")
        .With("zhs", "将抽牌堆中的 2 张牌变为小卒突袭。"),
    Pool = "regent",
    Type = CardType.Skill,
    Rarity = CardRarity.Uncommon,
    Target = CardTargetType.Self,
    EnergyCost = 1,
    Effects =
    {
        new EffectDef
        {
            Kind = EffectKind.TransformCardsInPile,
            Target = EffectTarget.Self,
            
            
            Pick = CardPick.InPile(PileKind.DrawPile, PickMode.Choose, 2),
            TransformInto = TransformInto.SpecificCard,
            CardId = "CARD.MINION_DIVE_BOMB",
            Upgraded = true,
        },
    },
});

project.Cards.Add(new CardDef
{
    Id = "WILD_MUTATION",
    Name = new LocalizedText("eng", "Wild Mutation").With("zhs", "狂野畸变"),
    Description = new LocalizedText("eng", "Transform ALL cards in your hand into random cards.")
        .With("zhs", "将手牌中所有牌变为随机牌。"),
    Pool = "necrobinder",
    Type = CardType.Skill,
    Rarity = CardRarity.Rare,
    Target = CardTargetType.Self,
    EnergyCost = 2,
    Effects =
    {
        new EffectDef
        {
            Kind = EffectKind.TransformCardsInPile,
            Target = EffectTarget.Self,
            
            
            TransformInto = TransformInto.Random,
        Pick = CardPick.InPile(PileKind.Hand, PickMode.All),
        },
    },
});

project.Cards.Add(new CardDef
{
    Id = "STRAY_ECHO",
    Name = new LocalizedText("eng", "Stray Echo").With("zhs", "游荡回声"),
    Description = new LocalizedText("eng", "Transform a random card in your discard pile.")
        .With("zhs", "将弃牌堆中随机 1 张牌变化。"),
    Pool = "silent",
    Type = CardType.Skill,
    Rarity = CardRarity.Common,
    Target = CardTargetType.Self,
    EnergyCost = 0,
    Effects =
    {
        new EffectDef
        {
            Kind = EffectKind.TransformCardsInPile,
            Target = EffectTarget.Self,
            
            
            Pick = CardPick.InPile(PileKind.DiscardPile, PickMode.Random, 1),
            TransformInto = TransformInto.Random,
        },
    },
});

// --- 卡 17i：在牌堆间移动牌（原版"头槌"/"万事通"）---
//
// 三种挑选方式 + 三个目标牌堆都生成到。来源牌堆用常规的 Pile，目标是 TargetPile。
project.Cards.Add(new CardDef
{
    Id = "RECALL_FROM_GRAVE",
    Name = new LocalizedText("eng", "Recall from the Grave").With("zhs", "自墓中召回"),
    Description = new LocalizedText("eng", "Choose a card in your discard pile. Put it on top of your draw pile.")
        .With("zhs", "选择弃牌堆中的 1 张牌，放到抽牌堆顶。"),
    Pool = "ironclad",
    Type = CardType.Skill,
    Rarity = CardRarity.Common,
    Target = CardTargetType.Self,
    EnergyCost = 1,
    Effects =
    {
        new EffectDef
        {
            Kind = EffectKind.MoveCardsBetweenPiles,
            Target = EffectTarget.Self,
            
            TargetPile = PileKind.DrawPile,
            Placement = PilePlacement.Top,
            
            Pick = CardPick.InPile(PileKind.DiscardPile, PickMode.Choose, 1),
        },
    },
});

project.Cards.Add(new CardDef
{
    Id = "GATHER_THE_FALLEN",
    Name = new LocalizedText("eng", "Gather the Fallen").With("zhs", "收拢残骸"),
    Description = new LocalizedText("eng", "Move 2 random cards from your discard pile into your hand.")
        .With("zhs", "将弃牌堆中随机 2 张牌移入手牌。"),
    Pool = "necrobinder",
    Type = CardType.Skill,
    Rarity = CardRarity.Uncommon,
    Target = CardTargetType.Self,
    EnergyCost = 1,
    Effects =
    {
        new EffectDef
        {
            Kind = EffectKind.MoveCardsBetweenPiles,
            Target = EffectTarget.Self,
            
            TargetPile = PileKind.Hand,
            
            Pick = CardPick.InPile(PileKind.DiscardPile, PickMode.Random, 2),
        },
    },
});

project.Cards.Add(new CardDef
{
    Id = "SCATTER_TO_WIND",
    Name = new LocalizedText("eng", "Scatter to the Wind").With("zhs", "随风四散"),
    Description = new LocalizedText("eng", "Move ALL cards in your hand to your discard pile.")
        .With("zhs", "将手牌中所有牌移入弃牌堆。"),
    Pool = "silent",
    Type = CardType.Skill,
    Rarity = CardRarity.Rare,
    Target = CardTargetType.Self,
    EnergyCost = 1,
    Effects =
    {
        new EffectDef
        {
            Kind = EffectKind.MoveCardsBetweenPiles,
            Target = EffectTarget.Self,
            
            TargetPile = PileKind.DiscardPile,
            
        Pick = CardPick.InPile(PileKind.Hand, PickMode.All),
        },
    },
});

// --- 卡 17j：战斗历史计数作为数值来源 ---
//
// 四种口径各生成一次（本回合/本场战斗 × 打出/抽到），确认生成的表达式能编译。
project.Cards.Add(new CardDef
{
    Id = "TEMPO_SURGE",
    Name = new LocalizedText("eng", "Tempo Surge").With("zhs", "节拍涌动"),
    Description = new LocalizedText("eng", "Deal damage equal to the cards you played this turn.")
        .With("zhs", "造成 等于 本回合打出牌数 的伤害。"),
    Pool = "silent",
    Type = CardType.Attack,
    Rarity = CardRarity.Uncommon,
    Target = CardTargetType.AnyEnemy,
    EnergyCost = 1,
    Effects =
    {
        new EffectDef
        {
            Kind = EffectKind.DealDamage,
            Target = EffectTarget.Target,
            Amount = 0,
            Source = new ValueSource { Actor = ValueSourceActor.Self, Kind = ValueSourceKind.CardsPlayedThisTurn },
        },
    },
});

project.Cards.Add(new CardDef
{
    Id = "STUDIED_REFLECTION",
    Name = new LocalizedText("eng", "Studied Reflection").With("zhs", "静思反照"),
    Description = new LocalizedText("eng", "Gain Block equal to the cards you drew this combat.")
        .With("zhs", "获得 等于 本场战斗抽到牌数 的格挡。"),
    Pool = "defect",
    Type = CardType.Skill,
    Rarity = CardRarity.Uncommon,
    Target = CardTargetType.Self,
    EnergyCost = 1,
    Effects =
    {
        new EffectDef
        {
            Kind = EffectKind.GainBlock,
            Target = EffectTarget.Self,
            Amount = 0,
            Source = new ValueSource { Actor = ValueSourceActor.Self, Kind = ValueSourceKind.CardsDrawnThisCombat },
        },
    },
});

project.Cards.Add(new CardDef
{
    Id = "MOMENTUM_READING",
    Name = new LocalizedText("eng", "Momentum Reading").With("zhs", "势能测算"),
    Description = new LocalizedText("eng", "Draw 1 card. Gain energy equal to the cards you drew this turn.")
        .With("zhs", "抽 1 张牌。获得 等于 本回合抽到牌数 的能量。"),
    Pool = "regent",
    Type = CardType.Skill,
    Rarity = CardRarity.Common,
    Target = CardTargetType.Self,
    EnergyCost = 0,
    Effects =
    {
        new EffectDef
        {
            Kind = EffectKind.DrawCards,
            Target = EffectTarget.Self,
            Amount = 1,
        },
        new EffectDef
        {
            Kind = EffectKind.GainEnergy,
            Target = EffectTarget.Self,
            Amount = 0,
            Source = new ValueSource { Actor = ValueSourceActor.Self, Kind = ValueSourceKind.CardsDrawnThisTurn },
        },
    },
});

// --- 卡 17k：按条件数牌（原版"完美打击"/"飞镖"）---
//
// 条件是**卡牌标签**（CardTag），不是名字——原版中文写成"名字中含有『打击』"只是措辞。
project.Cards.Add(new CardDef
{
    Id = "PERFECTED_STRIKE_STYLE",
    Name = new LocalizedText("eng", "Chain Strike").With("zhs", "连环打击"),
    Description = new LocalizedText("eng", "Deal 6 damage plus 2 per Strike card you have.")
        .With("zhs", "造成 6 点伤害，你每有一张打击牌就额外造成 2 点伤害。"),
    Pool = "ironclad",
    Type = CardType.Attack,
    Rarity = CardRarity.Common,
    Target = CardTargetType.AnyEnemy,
    EnergyCost = 2,
    Effects =
    {
        new EffectDef
        {
            Kind = EffectKind.DealDamage,
            Target = EffectTarget.Target,
            Amount = 6,
            Source = new ValueSource
            {
                Actor = ValueSourceActor.Self,
                Kind = ValueSourceKind.FilteredCardCount,
                Filter = new CardFilter { Tags = { CardTag.Strike } },
                CountScope = CountScope.AllPiles,
                PerUnit = 2,
            },
        },
    },
});

project.Cards.Add(new CardDef
{
    Id = "FLECHETTE_STYLE",
    Name = new LocalizedText("eng", "Flechette Volley").With("zhs", "飞镖齐射"),
    Description = new LocalizedText("eng", "Deal damage equal to the Skill cards in your hand.")
        .With("zhs", "造成 等于 手牌中技能牌数 的伤害。"),
    Pool = "silent",
    Type = CardType.Attack,
    Rarity = CardRarity.Uncommon,
    Target = CardTargetType.AnyEnemy,
    EnergyCost = 1,
    Effects =
    {
        new EffectDef
        {
            Kind = EffectKind.DealDamage,
            Target = EffectTarget.Target,
            Amount = 0,
            Source = new ValueSource
            {
                Actor = ValueSourceActor.Self,
                Kind = ValueSourceKind.FilteredCardCount,
                Filter = new CardFilter { Types = { CardType.Skill } },
                CountScope = CountScope.Hand,
            },
        },
    },
});

project.Cards.Add(new CardDef
{
    Id = "STARGAZER_STYLE",
    Name = new LocalizedText("eng", "Stargazer").With("zhs", "观星者"),
    Description = new LocalizedText("eng", "Gain Block equal to the cards in all piles that cost stars.")
        .With("zhs", "获得 等于 所有牌堆中星辰消耗大于 0 的牌数 的格挡。"),
    Pool = "regent",
    Type = CardType.Skill,
    Rarity = CardRarity.Uncommon,
    Target = CardTargetType.Self,
    EnergyCost = 1,
    Effects =
    {
        new EffectDef
        {
            Kind = EffectKind.GainBlock,
            Target = EffectTarget.Self,
            Amount = 0,
            Source = new ValueSource
            {
                Actor = ValueSourceActor.Self,
                Kind = ValueSourceKind.FilteredCardCount,
                // 星辰这一维：比阈值（阈值是筛选自己的常数，不是效果级三件套）。
                Filter = new CardFilter
                {
                    StarComparison = CostComparison.GreaterThan,
                    StarThreshold = 0,
                },
                CountScope = CountScope.AllPiles,
            },
        },
    },
});

// --- 卡 17l：作用于"这张牌自己"（原版"王者之踢"改自身费用）---
//
// 同一条效果、同一套选择器，"作用的牌"选"这张牌自己"即可——不必为"作用于自己"单开效果。
// 三种效果各来一张，确认生成的代码走的是 `new List<CardModel> { this }`。
project.Cards.Add(new CardDef
{
    Id = "SELF_TEMPERING",
    Name = new LocalizedText("eng", "Self-Tempering").With("zhs", "自砺"),
    Description = new LocalizedText("eng", "This card costs 1 less this combat.")
        .With("zhs", "本场战斗中这张牌的费用降低 1 点。"),
    Pool = "ironclad",
    Type = CardType.Skill,
    Rarity = CardRarity.Common,
    Target = CardTargetType.Self,
    EnergyCost = 1,
    Effects =
    {
        new EffectDef
        {
            Kind = EffectKind.ModifyCardCost,
            Target = EffectTarget.Self,
            Amount = -1,
            Pick = CardPick.This(),
            CostDuration = CostDuration.ThisCombat,
        },
    },
});

project.Cards.Add(new CardDef
{
    Id = "SELF_REFINEMENT",
    Name = new LocalizedText("eng", "Self-Refinement").With("zhs", "自省"),
    Description = new LocalizedText("eng", "Upgrade this card. Draw 1 card.")
        .With("zhs", "升级这张牌。抽 1 张牌。"),
    Pool = "silent",
    Type = CardType.Skill,
    Rarity = CardRarity.Uncommon,
    Target = CardTargetType.Self,
    EnergyCost = 1,
    Effects =
    {
        new EffectDef
        {
            Kind = EffectKind.UpgradeCardsInPile,
            Target = EffectTarget.Self,
            Pick = CardPick.This(),
        },
        new EffectDef
        {
            Kind = EffectKind.DrawCards,
            Target = EffectTarget.Self,
            Amount = 1,
        },
    },
});

project.Cards.Add(new CardDef
{
    Id = "MOLT_AND_REFORGE",
    Name = new LocalizedText("eng", "Molt and Reforge").With("zhs", "蜕壳重铸"),
    Description = new LocalizedText("eng", "Make this card free to play this turn.")
        .With("zhs", "本回合这张牌可以免费打出。"),
    Pool = "defect",
    Type = CardType.Skill,
    Rarity = CardRarity.Common,
    Target = CardTargetType.Self,
    EnergyCost = 1,
    Effects =
    {
        new EffectDef
        {
            Kind = EffectKind.SetCardsFree,
            Target = EffectTarget.Self,
            Pick = CardPick.This(),
            CostDuration = CostDuration.ThisTurn,
        },
    },
});

// --- 卡 17m：作用对象 = "指定牌名" ---
//
// 作用是"所有该牌的副本"（扫全部战斗牌堆），所以没有"牌堆"与"作用方式"可言。
// 三种改造效果各来一张。
project.Cards.Add(new CardDef
{
    Id = "STRIKE_DOCTRINE",
    Name = new LocalizedText("eng", "Strike Doctrine").With("zhs", "打击教条"),
    Description = new LocalizedText("eng", "Upgrade every Strike in your deck.")
        .With("zhs", "升级你牌组中所有打击牌。"),
    Pool = "ironclad",
    Type = CardType.Skill,
    Rarity = CardRarity.Uncommon,
    Target = CardTargetType.Self,
    EnergyCost = 1,
    Effects =
    {
        new EffectDef
        {
            Kind = EffectKind.UpgradeCardsInPile,
            Target = EffectTarget.Self,
            Pick = new CardPick { CardIds = { "CARD.STRIKE_IRONCLAD" } },
        },
    },
});

project.Cards.Add(new CardDef
{
    Id = "DEFEND_RECLAIM",
    Name = new LocalizedText("eng", "Reclaim Defenses").With("zhs", "收回防线"),
    Description = new LocalizedText("eng", "Make every Defend free to play this combat.")
        .With("zhs", "本场战斗中你所有的防御牌费用变为 0。"),
    Pool = "silent",
    Type = CardType.Skill,
    Rarity = CardRarity.Rare,
    Target = CardTargetType.Self,
    EnergyCost = 1,
    Effects =
    {
        new EffectDef
        {
            Kind = EffectKind.SetCardsFree,
            Target = EffectTarget.Self,
            Pick = new CardPick { CardIds = { "CARD.DEFEND_IRONCLAD" } },
            CostDuration = CostDuration.ThisCombat,
        },
    },
});

project.Cards.Add(new CardDef
{
    Id = "DWARF_THE_STRIKES",
    Name = new LocalizedText("eng", "Dwarf the Strikes").With("zhs", "削弱打击"),
    Description = new LocalizedText("eng", "Transform every Strike into a random card.")
        .With("zhs", "将你所有打击牌变为随机牌。"),
    Pool = "necrobinder",
    Type = CardType.Skill,
    Rarity = CardRarity.Rare,
    Target = CardTargetType.Self,
    EnergyCost = 2,
    Effects =
    {
        new EffectDef
        {
            Kind = EffectKind.TransformCardsInPile,
            Target = EffectTarget.Self,
            Pick = new CardPick { CardIds = { "CARD.STRIKE_IRONCLAD" } },
            TransformInto = TransformInto.Random,
        },
    },
});

// --- 卡 18：计算型 + 目标属性（"目标的已损失生命"，而不是自身）---
project.Cards.Add(new CardDef
{
    Id = "EXECUTE_THE_WOUNDED",
    Name = new LocalizedText("eng", "Execute the Wounded").With("zhs", "处决伤者"),
    Description = new LocalizedText("eng", "Deal damage equal to the target's missing HP.")
        .With("zhs", "造成 等于 目标已损失生命 的伤害。"),
    Pool = "ironclad",
    Type = CardType.Attack,
    Rarity = CardRarity.Rare,
    Target = CardTargetType.AnyEnemy,
    EnergyCost = 2,
    Effects =
    {
        new EffectDef
        {
            Kind = EffectKind.DealDamage,
            Target = EffectTarget.Target,
            Source = new ValueSource
            {
                Actor = ValueSourceActor.Target,
                Kind = ValueSourceKind.MissingHp,
            },
        },
    },
});

// --- 卡 21：一张牌两次独立的计算型伤害（两条算式不同）---
//
// 这是"先打手牌数×3、再打已损失生命×0.25"那种设计，也是这次改动的主要目标：
// 每条效果各占一套变量（CalculatedDamage / CalculatedDamage2 + 各自的 CalculationBase /
// ExtraDamage），各算各的、各升各的。
//
// 这张卡同时钉住三件事：
//   1. 第二条的**算式**要真的进代码（原先会被 First() 丢掉，整段蒸发）；
//   2. 第二个槽用 Runtime/SlotVar.cs 那个子类读自己那两个变量（原版类型固定读
//      无序号的那两个名字，不改它会读成第一条的数值）；
//   3. 第一个槽仍走原版形状（CalculationBase + ExtraDamage + CalculatedDamageVar），
//      卡面与描述与原版完全一致。
project.Cards.Add(new CardDef
{
    Id = "DOUBLE_RECKONING",
    Name = new LocalizedText("eng", "Double Reckoning").With("zhs", "双重清算"),
    Description = new LocalizedText("eng", "Deal damage equal to 3 times the cards in your hand, then damage equal to a quarter of the target's missing HP.")
        .With("zhs", "造成 等于 3 × 手牌数 的伤害。再造成 等于 目标已损失生命 ×0.25 的伤害。"),
    Pool = "silent",
    Type = CardType.Attack,
    Rarity = CardRarity.Rare,
    Target = CardTargetType.AnyEnemy,
    EnergyCost = 2,
    Effects =
    {
        // 第一条：3 × 手牌数（基础值 0，纯系数）
        new EffectDef
        {
            Kind = EffectKind.DealDamage,
            Target = EffectTarget.Target,
            Source = new ValueSource { Actor = ValueSourceActor.Self, Kind = ValueSourceKind.HandCount, PerUnit = 3 },
            Upgrade = new EffectUpgrade { PerUnitDelta = 1 },
        },

        // 第二条：0.25 × 目标已损失生命（同样基础值 0）
        new EffectDef
        {
            Kind = EffectKind.DealDamage,
            Target = EffectTarget.Target,
            Source = new ValueSource { Actor = ValueSourceActor.Target, Kind = ValueSourceKind.MissingHp, PerUnit = 0.25m },
            Upgrade = new EffectUpgrade { PerUnitDelta = 0.25m },
        },
    },
});

// --- 卡 22：铸造（储君体系）---
//
// 对应 ForgeCmd.Forge(数量, 玩家, 来源)：没有君王之剑时先生成一张加到手牌，
// 然后把数量加到所有副本（含消耗堆里的）的伤害上。原版形状（"淬锋"）是声明 ForgeVar、
// OnPlay 调用、升级改那个变量——我们照搬，所以这张卡要断言三件事都在。
//
// 两条铸造效果（数值不同）也顺便覆盖：各占一个槽（Forge / Forge2），升级各改各的——
// 与伤害的多槽是同一套规划。
project.Cards.Add(new CardDef
{
    Id = "SMITHS_APPRENTICE",
    Name = new LocalizedText("eng", "Smith's Apprentice").With("zhs", "铸剑学徒"),
    Description = new LocalizedText("eng", "Gain 6 Block. Forge 5.")
        .With("zhs", "获得 6 点[gold]格挡[/gold]。[gold]铸造[/gold] 5 点。"),
    Pool = "regent",
    Type = CardType.Skill,
    Rarity = CardRarity.Common,
    Target = CardTargetType.Self,
    EnergyCost = 1,
    Effects =
    {
        new EffectDef
        {
            Kind = EffectKind.GainBlock, Target = EffectTarget.Self,
            Amount = 6,
            Upgrade = new EffectUpgrade { AmountDelta = 3 },
        },

        // 固定铸造 5，升级 +4（原版"淬锋"的升级量）。
        new EffectDef
        {
            Kind = EffectKind.Forge, Target = EffectTarget.Self,
            Amount = 5,
            Upgrade = new EffectUpgrade { AmountDelta = 4 },
        },

        // 第二条铸造（数值不同）：验证 Forge2 槽。
        new EffectDef
        {
            Kind = EffectKind.Forge, Target = EffectTarget.Self,
            Amount = 2,
            Upgrade = new EffectUpgrade { AmountDelta = 1 },
        },
    },
});

// 计算型铸造：铸造 等于手牌数 的点数。
// 计算型的铸造没有游戏变量可走（SlotBaseName 只给伤害/格挡发计算槽），
// 走的是"其它计算型"的取值方法那条路——这里验证它也能编译并对上 ForgeCmd 的参数。
project.Cards.Add(new CardDef
{
    Id = "LIVING_FORGE",
    Name = new LocalizedText("eng", "Living Forge").With("zhs", "活炉"),
    Description = new LocalizedText("eng", "Forge an amount equal to the cards in your hand.")
        .With("zhs", "[gold]铸造[/gold] 等于 手牌数 的点数。"),
    Pool = "regent",
    Type = CardType.Power,
    Rarity = CardRarity.Uncommon,
    Target = CardTargetType.Self,
    EnergyCost = 1,
    Effects =
    {
        new EffectDef
        {
            Kind = EffectKind.Forge, Target = EffectTarget.Self,
            Source = new ValueSource { Actor = ValueSourceActor.Self, Kind = ValueSourceKind.HandCount },
            Upgrade = new EffectUpgrade { PerUnitDelta = 1 },
        },
    },
});

// --- 卡 22b：战斗历史计数的新来源（受击 / 攻击 / 消耗 / 伤害量）---
//
// 这张卡把新增的历史来源各用一遍，主要目的是**编译验证发射出来的表达式**——
// 这些表达式是手写的字符串，条目类型名或字段名写错的话，编译对游戏程序集这一步才会炸。
project.Cards.Add(new CardDef
{
    Id = "BATTLE_CHRONICLE",
    Name = new LocalizedText("eng", "Battle Chronicle").With("zhs", "战地纪事"),
    Description = new LocalizedText("eng", "Deal damage equal to the number of attacks you made this turn. Gain Block equal to the damage you took this combat. Exhaust a card, then forge an amount equal to the cards you exhausted this turn.")
        .With("zhs", "造成 等于 本回合攻击次数 的伤害。获得 等于 本场战斗受到的伤害 的[gold]格挡[/gold]。消耗 1 张牌，[gold]铸造[/gold] 等于 本回合消耗的牌数 的点数。"),
    Pool = "regent",
    Type = CardType.Attack,
    Rarity = CardRarity.Rare,
    Target = CardTargetType.AnyEnemy,
    EnergyCost = 2,
    Effects =
    {
        // 伤害 = 本回合攻击次数
        new EffectDef
        {
            Kind = EffectKind.DealDamage, Target = EffectTarget.Target,
            Source = new ValueSource { Actor = ValueSourceActor.Self, Kind = ValueSourceKind.AttacksThisTurn },
        },

        // 格挡 = 本场战斗受到的伤害（求和，不是次数）
        new EffectDef
        {
            Kind = EffectKind.GainBlock, Target = EffectTarget.Self,
            Source = new ValueSource { Actor = ValueSourceActor.Self, Kind = ValueSourceKind.DamageTakenThisCombat },
        },

        // 消耗手牌里选中的 1 张，再按"本回合消耗的牌数"铸造
        new EffectDef
        {
            Kind = EffectKind.ExhaustCards, Target = EffectTarget.Self,
            // 张数 = 效果数值：选 1 张（Amount=1），所以这里只要给选牌的那一半。
            Amount = 1,
            Pick = CardPick.InPile(PileKind.Hand, PickMode.Choose, 1),
        },
        new EffectDef
        {
            Kind = EffectKind.Forge, Target = EffectTarget.Self,
            Source = new ValueSource { Actor = ValueSourceActor.Self, Kind = ValueSourceKind.CardsExhaustedThisTurn },
        },
    },
});

// --- 卡 22c：恢复生命的计数（自制全局计数器）---
//
// 游戏没有治疗的历史与钩子，这四个来源读的是我们自己的计数器
// （Runtime/HealTracker.cs，Harmony 观察 Creature.HealInternal）。
// 这张卡验证三件事：
//   1. 计数器文件会被发出去（Runtime/HealTracker.cs）；
//   2. ModEntry 的 Harmony.PatchAll 因为它而发出（否则计数器不挂载，永远读 0）；
//   3. 生成代码里的表达式调用的是计数器的方法。
project.Cards.Add(new CardDef
{
    Id = "HEALER_NEVER_CLOSES",
    Name = new LocalizedText("eng", "Healer Never Closes").With("zhs", "不打烊的医师"),
    Description = new LocalizedText("eng", "Heal 3 HP. Deal damage equal to the number of times you healed this combat.")
        .With("zhs", "回复 3 点生命。造成 等于 本场战斗恢复生命次数 的伤害。"),
    Pool = "regent",
    Type = CardType.Skill,
    Rarity = CardRarity.Uncommon,
    Target = CardTargetType.Self,
    EnergyCost = 1,
    Effects =
    {
        // 先回 3 点血：这样打出去本身就会让计数 +1（造成伤害按次数算）。
        new EffectDef
        {
            Kind = EffectKind.Heal, Target = EffectTarget.Self,
            Amount = 3,
        },

        // 次数口径（本场战斗）。
        new EffectDef
        {
            Kind = EffectKind.DealDamage, Target = EffectTarget.Target,
            Source = new ValueSource { Actor = ValueSourceActor.Self, Kind = ValueSourceKind.HealsThisCombat },
        },

        // 总量口径（本回合）——四个来源都至少生成一遍。
        new EffectDef
        {
            Kind = EffectKind.GainBlock, Target = EffectTarget.Self,
            Source = new ValueSource { Actor = ValueSourceActor.Self, Kind = ValueSourceKind.HealAmountThisTurn },
        },
    },
});

// --- 卡 22d：多次升级（等级表）---
//
// 游戏侧能做的：MaxUpgradeLevel 可覆写（原版只有 0/1），卡名会自动显示 +2、+3；
// 升级时一级一级地调 OnUpgrade（读档也是这样重放的）。
//
// 我们发出来的是 **switch (CurrentUpgradeLevel) + 绝对值赋值**，不是增量：
// 只有这样才表达得了"每级加得不一样"（这张牌是 6 → 9 → 13，每级 +3/+4）。
//
// 这张卡同时钉住"每级换算式来源"：第 1 级按手牌数，第 2 级改按弃牌堆张数——
// 那是增量模型做不到的事（乘数方法要按等级分支）。
//
// 如实说明：卡面**描述**只有"升级前/升级后"两态（游戏的 {IfUpgraded:…|…} 分不出第几级），
// 所以描述里引用变量只会显示当前等级的数，看不到"下一级是多少"。
project.Cards.Add(new CardDef
{
    Id = "ASCENDING_FURY",
    Name = new LocalizedText("eng", "Ascending Fury").With("zhs", "渐盛之怒"),
    Description = new LocalizedText("eng", "Deal 6 damage. Deals 3 more damage for each card in your hand.")
        .With("zhs", "造成 6 点伤害，每有一张手牌额外造成 3 点。"),
    Pool = "ironclad",
    Type = CardType.Attack,
    Rarity = CardRarity.Uncommon,
    Target = CardTargetType.AnyEnemy,
    EnergyCost = 1,
    MaxUpgradeLevel = 2,

    Effects =
    {
        new EffectDef
        {
            Kind = EffectKind.DealDamage, Target = EffectTarget.Target,
            Amount = 6,
            Source = new ValueSource { Actor = ValueSourceActor.Self, Kind = ValueSourceKind.HandCount, PerUnit = 3 },

            // 第 1 级：基础值 9、系数 4（仍是按手牌数）
            // 第 2 级：基础值 13、系数 1，**换成按弃牌堆张数**
            Levels = new List<EffectLevel?>
            {
                new EffectLevel { Amount = 9, PerUnit = 4m },
                new EffectLevel
                {
                    Amount = 13,
                    PerUnit = 1m,
                    Source = new ValueSource { Actor = ValueSourceActor.Self, Kind = ValueSourceKind.DiscardPileCount },
                },
            },
        },
    },
});

// --- 卡 19：提高最大生命（原版"狂宴"那种）---
//
// 走 CreatureCmd.GainMaxHp。它不接受 PlayerChoiceContext（只改上限再补回复），
// 与 LoseMaxHp 的签名不同——两个都生成一遍，免得其中一个写错。
// 数值从声明的 MaxHp 变量读回（原版 Feed 声明 MaxHpVar），所以升级也能跟着变。
project.Cards.Add(new CardDef
{
    Id = "GLUTTONOUS_BLOW",
    Name = new LocalizedText("eng", "Gluttonous Blow").With("zhs", "饕餮之击"),
    Description = new LocalizedText("eng", "Deal 10 damage. Raise your Max HP by 3.")
        .With("zhs", "造成 10 点伤害。提高 3 点最大生命。"),
    Pool = "ironclad",
    Type = CardType.Attack,
    Rarity = CardRarity.Rare,
    Target = CardTargetType.AnyEnemy,
    EnergyCost = 1,
    Keywords = { CardKeyword.Exhaust },
    Effects =
    {
        new EffectDef
        {
            Kind = EffectKind.DealDamage,
            Target = EffectTarget.Target,
            Amount = 10,
            Upgrade = new EffectUpgrade { AmountDelta = 2 },
        },
        new EffectDef
        {
            Kind = EffectKind.GainMaxHp,
            Target = EffectTarget.Self,
            Amount = 3,
            Upgrade = new EffectUpgrade { AmountDelta = 1 },
        },
    },
});

// 降低最大生命 + 计算型数值（等于失去生命的次数）：两张覆盖 LoseMaxHp 与计算型最大生命。
project.Cards.Add(new CardDef
{
    Id = "WASTING_AWAY",
    Name = new LocalizedText("eng", "Wasting Away").With("zhs", "日渐消瘦"),
    Description = new LocalizedText("eng", "Lose Max HP equal to the number of times you lost HP this combat.")
        .With("zhs", "降低 等于本场战斗失去生命的次数 的最大生命。"),
    Pool = "curse",
    Type = CardType.Curse,
    Rarity = CardRarity.Curse,
    Target = CardTargetType.None,
    EnergyCost = -1,
    Effects =
    {
        new EffectDef
        {
            Kind = EffectKind.LoseMaxHp,
            Target = EffectTarget.Self,
            Source = new ValueSource { Actor = ValueSourceActor.Self, Kind = ValueSourceKind.HpLostThisCombat },
        },
    },
});

// --- 卡 20：计算型的**重复次数**（原版"扯碎"那种）---
//
// 关键点：伤害是**固定 5**，次数才是算出来的（1 + 本场战斗失去生命的次数）。
// 所以次数有自己的一套基础值与来源，不能借用数值那一套——这张卡就是专门盯这件事的：
// 如果哪天有人图省事把两者合成一套，这张卡的伤害会跟着次数一起变成计算型。
project.Cards.Add(new CardDef
{
    Id = "GRUDGE_REPAID",
    Name = new LocalizedText("eng", "Grudge Repaid").With("zhs", "偿还宿怨"),
    Description = new LocalizedText("eng", "Deal 5 damage. Repeat once for each time you lost HP this combat.")
        .With("zhs", "造成 5 点伤害。本场战斗中你每失去过一次生命，就额外造成一次伤害。"),
    Pool = "ironclad",
    Type = CardType.Attack,
    Rarity = CardRarity.Rare,
    Target = CardTargetType.AnyEnemy,
    EnergyCost = 2,
    Effects =
    {
        new EffectDef
        {
            Kind = EffectKind.DealDamage,
            Target = EffectTarget.Target,
            Amount = 5,

            CalculatedRepeat = true,
            RepeatBase = 1,
            RepeatSource = new ValueSource { Actor = ValueSourceActor.Self, Kind = ValueSourceKind.HpLostThisCombat },

            // 两个升级一起用：数值的（伤害 +3）与次数基础值的（1 → 2）。
            // 计算型次数改的是算次数的基础值，不是 RepeatVar。
            Upgrade = new EffectUpgrade { AmountDelta = 3, RepeatDelta = 1 },
        },
    },
});

// 本回合口径的失去生命次数，同时也验证"数值计算型 + 次数计算型"可以并存。
project.Cards.Add(new CardDef
{
    Id = "BLOOD_PRICE",
    Name = new LocalizedText("eng", "Blood Price").With("zhs", "血的代价"),
    Description = new LocalizedText("eng", "Lose 2 HP. Gain Block equal to the number of times you lost HP this turn.")
        .With("zhs", "失去 2 点生命。获得 等于本回合失去生命的次数 的格挡。"),
    Pool = "ironclad",
    Type = CardType.Skill,
    Rarity = CardRarity.Uncommon,
    Target = CardTargetType.Self,
    EnergyCost = 0,
    Effects =
    {
        new EffectDef
        {
            Kind = EffectKind.LoseHp,
            Target = EffectTarget.Self,
            Amount = 2,
        },
        new EffectDef
        {
            Kind = EffectKind.GainBlock,
            Target = EffectTarget.Self,
            Source = new ValueSource { Actor = ValueSourceActor.Self, Kind = ValueSourceKind.HpLostThisTurn },
        },
    },
});

// --- 卡 18：打出一张牌时（原版精密瞄准 / 踩踏 / 女妖之嚎 / 如此甚好那种）---
//
// 这是本次新加的时机：反应的是**任意一张被打出的牌**，用卡牌筛选决定哪些算数。
// 几种形状各生成一张，因为它们走生成器里不同的分支：
//
//   18a  不限 + 不计数           —— 每打出一张牌就触发（最简形式）
//   18b  按类型 + 不计数         —— 原版踩踏 / 精密瞄准（每打出一张攻击牌降 1 费）
//   18c  按关键词 + 计数(本回合)  —— 原版女妖之嚎按关键词（虚无）、如此甚好按张数计数
//   18d  指定牌 + 计数(本场战斗)  —— 覆盖"指定卡"分支与"本场战斗"归零范围
//
// 还要覆盖"同一张牌上既有打出后的效果、又有打出一张牌时的效果"——两者由游戏同一个钩子
// 派发，生成器必须合成**一个** AfterCardPlayed 覆写（两个同名 override 是编译错误）。
// 18c 就是两种效果并存的那种，所以它编译过就说明合并写对了。
// 注意这里都是**攻击/技能牌**，不是能力牌：能力牌打完就从战斗里消失（PileType.None），
// 之后不再收到钩子，根本看不见别人出牌——校验器会把它拦下来（见文末的反例检查）。
project.Cards.Add(new CardDef
{
    Id = "ECHO_OF_FURY",
    Name = new LocalizedText("eng", "Echo of Fury").With("zhs", "狂怒回声"),
    Description = new LocalizedText("eng", "Whenever you play a card, gain 1 Block.")
        .With("zhs", "每当你打出一张牌，获得 1 点格挡。"),
    Pool = "ironclad",
    Type = CardType.Skill,
    Rarity = CardRarity.Uncommon,
    Target = CardTargetType.Self,
    EnergyCost = 1,
    Effects =
    {
        new EffectDef
        {
            Kind = EffectKind.GainBlock,
            Target = EffectTarget.Self,
            Amount = 1,
            Trigger = EffectTrigger.CardPlayed,
            CardFilter = CardFilterKind.Any,
        },
    },
});

project.Cards.Add(new CardDef
{
    Id = "TIGHT_FOCUS",
    Name = new LocalizedText("eng", "Tight Focus").With("zhs", "凝神瞄准"),
    Description = new LocalizedText("eng", "Deal 15 damage. Costs 1 less for each Attack played this turn.")
        .With("zhs", "造成 15 点伤害。你在本回合中每打出过一张攻击牌，其耗能减少 1。"),
    Pool = "silent",
    Type = CardType.Attack,
    Rarity = CardRarity.Uncommon,
    Target = CardTargetType.AnyEnemy,
    EnergyCost = 3,
    Effects =
    {
        new EffectDef
        {
            Kind = EffectKind.DealDamage,
            Target = EffectTarget.Target,
            Amount = 15,
        },

        // 每打出一张攻击牌就给自己降 1 费（原版踩踏的形状）。
        new EffectDef
        {
            Kind = EffectKind.ModifyCardCost,
            Target = EffectTarget.Self,
            Amount = -1,
            Pick = CardPick.This(),
            CostDuration = CostDuration.ThisTurn,
            Trigger = EffectTrigger.CardPlayed,
            CardFilter = CardFilterKind.TypeAttack,
        },
    },
});

project.Cards.Add(new CardDef
{
    Id = "WRAITH_ROAR",
    Name = new LocalizedText("eng", "Wraith Roar").With("zhs", "幽嚎"),
    Description = new LocalizedText("eng", "Deal 5 damage. Every 3 Ethereal cards you play this turn, gain 1 energy. After this card is played, draw 1 card.")
        .With("zhs", "造成 5 点伤害。本回合你每打出 3 张虚无牌，获得 1 点能量。打出后，抽 1 张牌。"),
    Pool = "necrobinder",
    Type = CardType.Attack,
    Rarity = CardRarity.Rare,
    Target = CardTargetType.AnyEnemy,
    EnergyCost = 2,
    Effects =
    {
        new EffectDef
        {
            Kind = EffectKind.DealDamage,
            Target = EffectTarget.Target,
            Amount = 5,
        },

        // 关键词筛选 + 计数器（本回合归零）。
        new EffectDef
        {
            Kind = EffectKind.GainEnergy,
            Target = EffectTarget.Self,
            Amount = 1,
            Trigger = EffectTrigger.CardPlayed,
            CardFilter = CardFilterKind.KeywordEthereal,
            UseCounter = true,
            CounterTarget = 3,
            CounterReset = CounterReset.ThisTurn,
        },

        // 与上面那条**同一个钩子**：生成器要合并成一个 AfterCardPlayed。
        new EffectDef
        {
            Kind = EffectKind.DrawCards,
            Target = EffectTarget.Self,
            Amount = 1,
            Trigger = EffectTrigger.AfterPlayed,
        },
    },
});

project.Cards.Add(new CardDef
{
    Id = "FATED_RHYTHM",
    Name = new LocalizedText("eng", "Fated Rhythm").With("zhs", "命定节拍"),
    Description = new LocalizedText("eng", "Whenever you play a Strike, gain 1 Block. Every 2 Strikes you play this combat, draw 1 card.")
        .With("zhs", "每当你打出一张打击牌，获得 1 点格挡。本场战斗你每打出 2 张打击牌，抽 1 张牌。"),
    Pool = "ironclad",
    Type = CardType.Skill,
    Rarity = CardRarity.Rare,
    Target = CardTargetType.Self,
    EnergyCost = 1,
    Effects =
    {
        // 同一时机上两条效果、两张过滤器、两种计数方式：局部变量与计数器字段都不能撞名。
        new EffectDef
        {
            Kind = EffectKind.GainBlock,
            Target = EffectTarget.Self,
            Amount = 1,
            Trigger = EffectTrigger.CardPlayed,
            CardFilter = CardFilterKind.TagStrike,
        },
        new EffectDef
        {
            Kind = EffectKind.DrawCards,
            Target = EffectTarget.Self,
            Amount = 1,
            Trigger = EffectTrigger.CardPlayed,
            CardFilter = CardFilterKind.TagStrike,
            UseCounter = true,
            CounterTarget = 2,
            CounterReset = CounterReset.ThisCombat,
        },
    },
});

// 指定某一张牌 + 计数（本场战斗）：覆盖"指定卡"这个筛选分支。
project.Cards.Add(new CardDef
{
    Id = "CLAW_CHORUS",
    Name = new LocalizedText("eng", "Claw Chorus").With("zhs", "爪击合唱"),
    Description = new LocalizedText("eng", "Deal 4 damage. Every 2 Claws you play this combat, gain 2 Block.")
        .With("zhs", "造成 4 点伤害。本场战斗你每打出 2 张爪击，获得 2 点格挡。"),
    Pool = "ironclad",
    Type = CardType.Attack,
    Rarity = CardRarity.Uncommon,
    Target = CardTargetType.AnyEnemy,
    EnergyCost = 1,
    Effects =
    {
        new EffectDef
        {
            Kind = EffectKind.DealDamage,
            Target = EffectTarget.Target,
            Amount = 4,
        },
        new EffectDef
        {
            Kind = EffectKind.GainBlock,
            Target = EffectTarget.Self,
            Amount = 2,
            Trigger = EffectTrigger.CardPlayed,
            CardFilter = CardFilterKind.SpecificCard,
            CardId = "CARD.CLAW",
            UseCounter = true,
            CounterTarget = 2,
            CounterReset = CounterReset.ThisCombat,
        },
    },
});

DescriptionModule.Names = new VanillaNames(gameDir);

Console.WriteLine("=== 效果说明（中 / 英）===");
foreach (CardDef card in project.Cards)
{
    Console.WriteLine();
    Console.WriteLine($"{card.Id}  ({card.Name.GetOrFallback("zhs")})");
    foreach (EffectDef effect in EffectResolver.Resolve(project, card.Effects))
    {
        Console.WriteLine($"   [zh] {DescriptionModule.Describe(project, effect, "zhs")}");
        Console.WriteLine($"   [en] {DescriptionModule.Describe(project, effect, "en")}");
    }
}

Console.WriteLine();
Console.WriteLine("=== 自动填充的描述（中文）===");
foreach (CardDef card in project.Cards)
{
    Console.WriteLine($"  {card.Id}: {DescriptionPlaceholders.Suggest(project, card, "zhs").Replace("\n", " / ")}");
}

// 真正把整个 mod 生成并编译一遍：API 用错在这里就会暴露。
Console.WriteLine();
Console.WriteLine("=== 生成并编译 ===");

// 先做一次静态检查：编译只能发现"名字写错"，发现不了"变量没声明"——后者是
// DynamicVars["X"] 这种索引器调用，编译期完全合法，运行时才抛 KeyNotFoundException。
// 用户那张牌正是这么坏的（升级时改一个不存在的状态变量），所以这里专门扫一遍：
// 把 CanonicalVars 里声明的变量名收集起来，凡是引用到未声明名字的就报错。
//
// 声明的名字有三种来源，都要认：
//   new PowerVar<DoomPower>(…)                    —— 名字是泛型参数的类型名
//   new PowerVar<DoomPower>("Name2", 1m)          —— 泛型参数 + **显式名字**（同一个状态
//                                                    施加多次、数值各不相同时会这样发）
//   new DynamicVar("Name", …)                     —— 名字是字面量
//   new DamageVar(6m, …)                          —— 名字是类型自带的常量（DamageVar → "Damage"）
// 最后一种不能靠猜类名的大小写，所以从游戏程序集里读各 Var 类的 base("…") 常量。
IReadOnlyDictionary<string, string> generated = new CodeGenerator(project).GenerateAll();
var varTypeNames = DynamicVarNames(gameDir);
var undeclared = new List<string>();

foreach ((string file, string source) in generated)
{
    var declared = new HashSet<string>(StringComparer.Ordinal);

    // 先认"泛型参数 + 显式名字"这种（`new PowerVar<X>("X2", 1m)`）：它的变量名是**字面量**，
    // 不是泛型参数的类型名。这一步必须在泛型分支之前——否则会被当成 X，漏掉 X2。
    foreach (Match m in Regex.Matches(
        source, @"new\s+\w+Var\s*<[^>]+>\s*\(\s*""([^""]+)"""))
    {
        declared.Add(m.Groups[1].Value);
    }

    // 再认我们自己那个运行时子类：`new SlotCalculatedVar("CalculatedDamage2", "CalculationBase2",
    // "ExtraDamage2", …)` —— 第一个参数是**计算变量名**，后两个是它读的基础值/增量变量名。
    // 类名可能带命名空间前缀（生成的是 ModForge.Generated.Runtime.SlotCalculatedVar），
    // 所以正则用 \w+\. 前缀可选；它不叫 XVar，上面两条正则都认不到。
    foreach (Match m in Regex.Matches(
        source, @"new\s+(?:[\w.]+\.)?SlotCalculatedVar\s*\(\s*""([^""]+)""\s*,\s*""([^""]+)""\s*,\s*""([^""]+)"""))
    {
        declared.Add(m.Groups[1].Value);
        declared.Add(m.Groups[2].Value);
        declared.Add(m.Groups[3].Value);
    }

    foreach (Match m in Regex.Matches(source, @"new\s+(\w+Var)\s*(?:<\s*([\w.]+)\s*>)?\s*\("))
    {
        string typeName = m.Groups[1].Value;
        string genericArg = m.Groups[2].Success ? m.Groups[2].Value : string.Empty;

        // 泛型参数给了名字（PowerVar<DoomPower> → DoomPower）。
        //
        // 除非这个构造带了显式名字（`("Name", …)`）——那时名字是那个字面量，
        // 上面那一轮已经收下了（也正是在上面先处理的原因）。
        if (genericArg.Length > 0)
        {
            bool hasExplicitName = Regex.IsMatch(
                source[m.Index..],
                @"\Gnew\s+\w+Var\s*<[^>]+>\s*\(\s*""");

            if (!hasExplicitName)
            {
                declared.Add(genericArg[(genericArg.LastIndexOf('.') + 1)..]);
            }

            continue;
        }

        // 第一个参数是字符串字面量的话，那就是名字。
        //
        // 必须锚定在这个构造调用的紧邻位置：这个正则若允许向前扫描，会跳过本调用的
        // 参数列表、越过逗号与换行，去匹配**后面另一个变量**的字面量，于是本变量被判成
        // 没声明（曾经如此：`new DamageVar(3m, …)` 后面跟着 `new DynamicVar("Increase", …)`
        // 时，DamageVar 被记成了 Increase，Damage 反而"未声明"）。
        Match withName = Regex.Match(
            source[m.Index..],
            @"\Gnew\s+(?:\w+Var)\s*\(\s*""([^""]+)""");

        if (withName.Success)
        {
            declared.Add(withName.Groups[1].Value);
            continue;
        }

        // 否则用类型自带的常量名。
        if (varTypeNames.TryGetValue(typeName, out string? implicitName))
        {
            declared.Add(implicitName);
        }
    }

    // 引用形式：DynamicVars["X"] 与 DynamicVars.X
    foreach (Match m in Regex.Matches(source, @"DynamicVars\s*\[\s*""([^""]+)""\s*\]"))
    {
        if (!declared.Contains(m.Groups[1].Value))
        {
            undeclared.Add($"{file}: DynamicVars[\"{m.Groups[1].Value}\"] 未在 CanonicalVars 里声明");
        }
    }

    foreach (Match m in Regex.Matches(source, @"DynamicVars\.([A-Z]\w+)"))
    {
        string name = m.Groups[1].Value;

        // 这些都是 DynamicVarSet 上以变量名为键的强类型属性，与索引器同源，同样要声明过。
        // 少数几个不是变量（集合成员），列在排除表里。
        if (name is "Count" or "Keys" or "Values" or "ContainsKey" or "TryGetValue")
        {
            continue;
        }

        if (!declared.Contains(name))
        {
            undeclared.Add($"{file}: DynamicVars.{name} 未在 CanonicalVars 里声明");
        }
    }
}

if (undeclared.Count > 0)
{
    Console.WriteLine();
    foreach (string issue in undeclared.Distinct())
    {
        Console.WriteLine("  [未声明变量] " + issue);
    }

    Console.WriteLine();
    Console.WriteLine("RESULT: FAILED (undeclared dynamic vars)");
    return 1;
}

Console.WriteLine("  生成的代码里没有引用未声明的动态变量。");

// 同一时机下两条牌堆范围不同的效果，必须都留下自己的判断。
//
// 这类错误编译得过、跑起来也不报错，只是后一条永远不会生效——静态检查与编译都抓不到，
// 所以直接查生成的源码：两个分支都要在，而且不该出现"按第一条范围整段早退"的写法。
{
    string twoScopes = generated["Cards/TwoScopes.cs"];
    string hook = twoScopes[twoScopes.IndexOf("AfterSideTurnStart", StringComparison.Ordinal)..];

    bool hasDrawBranch = hook.Contains("PileType.Draw");
    bool hasHandBranch = hook.Contains("PileType.Hand");

    // "整段早退"的写法：开头一句 `!= PileType.X` 就 return。逐条判断用的是 `==`。
    bool hasBlanketBailout = Regex.IsMatch(hook, @"!=\s*PileType\.\w+\s*\)\s*\r?\n\s*\{\s*\r?\n\s*return;");

    if (!hasDrawBranch || !hasHandBranch || hasBlanketBailout)
    {
        Console.WriteLine();
        Console.WriteLine("  [失败] 同一时机下两种牌堆范围没有同时生效"
            + $"（Draw={hasDrawBranch}，Hand={hasHandBranch}，整段早退={hasBlanketBailout}）。");
        Console.WriteLine("         后一条会变成死代码：编译正常，只是永远不触发。");
        Console.WriteLine();
        Console.WriteLine("RESULT: FAILED (mixed pile scopes are dead code)");
        return 1;
    }

    Console.WriteLine("  同一时机下两种牌堆范围都留下了各自的判断。");
}

// "回合结束时 + 若在手牌中"必须走 OnTurnEndInHand，不能走 AfterSideTurnEnd。
//
// 走错钩子的话代码照样编译、也能跑，只是永远不会触发：那个钩子执行时手牌已经被弃掉了
// （引擎顺序是 FlushPlayerHand -> Hook.AfterTurnEnd），而且引擎自己的 OnTurnEndInHand
// 还会先把牌临时移进 Play 牌堆，所以连在正确的钩子里写"牌堆==手牌"都不成立。
// 这条只有查源码才看得出来，所以直接断言生成结果。
{
    string vigil = generated["Cards/PersistentVigil.cs"];

    bool usesInHandHook = vigil.Contains("HasTurnEndInHandEffect => true")
        && vigil.Contains("OnTurnEndInHand(PlayerChoiceContext");

    // 手牌那条效果不该出现在 AfterSideTurnEnd 里（那里必然读不到手牌）。
    bool turnEndBody = vigil.Contains("AfterSideTurnEnd");
    bool badGuard = Regex.IsMatch(vigil, @"Pile\?\.Type == PileType\.Hand");

    if (!usesInHandHook || turnEndBody || badGuard)
    {
        Console.WriteLine();
        Console.WriteLine("  [失败] \"回合结束时 + 若在手牌中\"没有走 OnTurnEndInHand"
            + $"（钩子={usesInHandHook}，误用AfterSideTurnEnd={turnEndBody}，写了手牌牌堆判断={badGuard}）。");
        Console.WriteLine("         这个组合会变成永不触发：执行时机比弃手牌晚，且调用期间牌不在手牌堆。");
        Console.WriteLine();
        Console.WriteLine("RESULT: FAILED (end-of-turn-in-hand uses the wrong hook)");
        return 1;
    }

    Console.WriteLine("\"回合结束时 + 若在手牌中\"走了 OnTurnEndInHand（弃手牌之前），也没有多余的手牌判断。");
}

// "打出后"与"打出一张牌时"必须**合成一个** AfterCardPlayed 覆写。
//
// 两者由游戏同一个钩子派发，各发一个的话 C# 直接编译失败（同名 override 重复）。但更隐蔽的
// 是另一种写法：只发一个、另一个静默丢掉——那样编译正常，只是少了一种行为。
// 所以这里断言三件事同时成立：只有一个覆写、自己那半段的判断在、别人那半段的筛选也在。
{
    string roar = generated["Cards/WraithRoar.cs"];

    int overrides = Regex.Matches(roar, @"override\s+async\s+Task\s+AfterCardPlayed\s*\(").Count;

    // 自己的那半段：`cardPlay.Card == this` 包起来的"打出后抽 1 张"。
    bool hasSelfHalf = roar.Contains("cardPlay.Card == this");

    // 别人那半段：按关键词筛打出的那张牌（虚无）。
    bool hasOtherHalf = roar.Contains("cardPlay.Card.Keywords.Contains(CardKeyword.Ethereal)");

    if (overrides != 1 || !hasSelfHalf || !hasOtherHalf)
    {
        Console.WriteLine();
        Console.WriteLine("  [失败] \"打出后\"与\"打出一张牌时\"没有合成一个 AfterCardPlayed"
            + $"（覆写数={overrides}，自身判断={hasSelfHalf}，任意牌筛选={hasOtherHalf}）。");
        Console.WriteLine("         要么编译不过，要么其中一种行为被静默丢掉。");
        Console.WriteLine();
        Console.WriteLine("RESULT: FAILED (card-played triggers were not merged)");
        return 1;
    }

    Console.WriteLine("\"打出后\"与\"打出一张牌时\"合成了一个 AfterCardPlayed，两半都在。");
}

// 计数器：只在"启用且阈值>1"时发字段；"本回合"的要在回合开始与回合结束各归零一次。
//
// 归零漏掉任一处都会读到过期的累计值，而这类错误在游戏里表现为"数字偶尔不对"，
// 光靠编译与试玩很难发现，所以直接查生成的源码。
{
    string roar = generated["Cards/WraithRoar.cs"];     // 关键词 + 计数 + 本回合
    string rhythm = generated["Cards/FatedRhythm.cs"];  // 按标签 + 计数 + 本场战斗
    string echo = generated["Cards/EchoOfFury.cs"];     // 不限 + 不计数

    bool hasCounterField = roar.Contains("private int _playedCount");
    bool increments = roar.Contains("_playedCount0++;");
    bool resetsOnZero = Regex.IsMatch(roar, @"_playedCount0\s*=\s*0;");

    // "本回合"：两个回合钩子里都要有归零。
    string startHook = roar.Contains("AfterSideTurnStart") ? roar[roar.IndexOf("AfterSideTurnStart", StringComparison.Ordinal)..] : "";
    int turnStartReset = startHook.Length > 0
        ? Regex.Matches(startHook, @"_playedCount0\s*=\s*0;").Count
        : 0;

    int totalResets = Regex.Matches(roar, @"_playedCount0\s*=\s*0;").Count;

    // "本场战斗"不该有回合归零（只能出现在"攒够就归零"那一处），所以只该有 1 次。
    int combatResets = Regex.Matches(rhythm, @"_playedCount1\s*=\s*0;").Count;

    // 不启用计数器就不该发字段。
    bool echoHasField = echo.Contains("private int _playedCount");

    // 只用"打出一张牌时"的牌也要表明接收战斗钩子——这个标记漏了的话钩子根本不会派发过来，
    // 而代码照常编译、卡也在牌堆里，属于最难发现的那种静默失效。
    bool echoHasHookFlag = echo.Contains("ShouldReceiveCombatHooks => true");

    bool ok = hasCounterField && increments && resetsOnZero
        && turnStartReset >= 1 && totalResets >= 3 && combatResets == 1 && !echoHasField
        && echoHasHookFlag;

    if (!ok)
    {
        Console.WriteLine();
        Console.WriteLine("  [失败] 计数器的生成不对"
            + $"（字段={hasCounterField}，自增={increments}，归零={resetsOnZero}，"
            + $"回合开始归零={turnStartReset}，总归零次数={totalResets}，"
            + $"本场战斗归零={combatResets}，不启用却发了字段={echoHasField}，"
            + $"只有出牌时机的牌有钩子标记={echoHasHookFlag}）。");
        Console.WriteLine("         本回合的必须在回合结束与回合开始各归零一次，否则会读到过期累计值。");
        Console.WriteLine();
        Console.WriteLine("RESULT: FAILED (card-played counter is wrong)");
        return 1;
    }

    Console.WriteLine("计数器按启用与否发出，\"本回合\"的在回合结束与回合开始各归零一次。");
}

// 同一个状态、不同时机、不同数值：两条都要读**自己**那个 PowerVar，且互不干扰。
//
// 这里守的是一个实测踩到过的错：原本两条都按状态类型名硬拼变量名，于是都读
// DynamicVars["StrengthPower"]——第二条的 1 层会被第一条的 2 层顶掉，"回合开始时获得 1 点力量"
// 变成"获得 2 点力量"，而且升级也会叠到同一个变量上。代码编译正常、卡也能用，
// 只有实际数值不对，属于最难发现的那种。
//
// 修法是给每组不同的数值各发一个变量（后续的加序号），取值与升级两处按同一份规划去查。
{
    string stages = generated["Cards/MightByStages.cs"];

    bool twoVars = stages.Contains("new PowerVar<MegaCrit.Sts2.Core.Models.Powers.StrengthPower>(2m)")
        && stages.Contains("\"StrengthPower2\", 1m)");

    // 两个施加点各读各的变量（不是都读 StrengthPower）。
    bool readsFirst = stages.Contains("DynamicVars[\"StrengthPower\"].BaseValue");
    bool readsSecond = stages.Contains("DynamicVars[\"StrengthPower2\"].BaseValue");

    // 施加两次（都在），且这张牌声明了接收战斗钩子（回合开始时那条要靠它）。
    int applies = Regex.Matches(stages, @"PowerCmd\.Apply<").Count;
    bool hookFlag = stages.Contains("ShouldReceiveCombatHooks => true");

    if (!twoVars || !readsFirst || !readsSecond || applies != 2 || !hookFlag)
    {
        Console.WriteLine();
        Console.WriteLine("  [失败] 同一状态施加多次的变量分配不对"
            + $"（两个变量={twoVars}，读第一个={readsFirst}，读第二个={readsSecond}，"
            + $"施加次数={applies}，钩子标记={hookFlag}）。");
        Console.WriteLine("         两条都读同一个变量的话，量小的那次会被顶成量大的。");
        Console.WriteLine();
        Console.WriteLine("RESULT: FAILED (repeated power application shares one variable)");
        return 1;
    }

    Console.WriteLine("同一状态施加多次时各用各的变量，施加两次都在，钩子标记也发了。");
}

// 不同**种类**的数值在同一张牌上互不干扰：伤害用 CalculationBase/ExtraDamage、格挡用自己的
// BlockVar，各是各的键，连"共用"都谈不上。
//
// 这条断言守的是"别把限制扩大到不该管的范围"。前面几条限制了"同一种数值只能一条效果"，
// 容易让人以为"一张牌只容得下一套数值"——实际不是：伤害与格挡各自独立，
// 一个用计算型、另一个用固定值也完全没问题（原版"君主之刃"就是这个形状）。
{
    var both = new ModProject { Id = "BothProbe", Name = "两值探针" };
    both.Cards.Add(new CardDef
    {
        Id = "BLADE_AND_GUARD",
        Name = new LocalizedText("eng", "Blade and Guard"),
        Pool = "ironclad",
        Type = CardType.Attack,
        Rarity = CardRarity.Common,
        Target = CardTargetType.AnyEnemy,
        EnergyCost = 1,
        Effects =
        {
            // 计算型伤害，基础值 3
            new EffectDef
            {
                Kind = EffectKind.DealDamage, Target = EffectTarget.Target,
                Amount = 3,
                Source = new ValueSource { Actor = ValueSourceActor.Self, Kind = ValueSourceKind.HandCount, PerUnit = 2 },
                Upgrade = new EffectUpgrade { AmountDelta = 1 },
            },

            // 固定格挡 4
            new EffectDef
            {
                Kind = EffectKind.GainBlock, Target = EffectTarget.Self,
                Amount = 4,
                Upgrade = new EffectUpgrade { AmountDelta = 2 },
            },
        },
    });

    string src = new CodeGenerator(both).GenerateAll()["Cards/BladeAndGuard.cs"];

    // 两者各自的变量都在，且互不冒充：伤害的基础值进 CalculationBase、格挡进 Block。
    bool damageBase = src.Contains("new CalculationBaseVar(3m)");
    bool blockVar = src.Contains("new BlockVar(4m");
    bool damageUpgrade = src.Contains("DynamicVars[\"CalculationBase\"].UpgradeValueBy(1m)");
    bool blockUpgrade = src.Contains("DynamicVars[\"Block\"].UpgradeValueBy(2m)");

    // 也不该被校验器误拦（它管的是"同一种数值两条"，不是"两种数值各一条"）。
    bool noError = !ProjectValidator.Validate(both, null)
        .Any(i => i.Severity == "error" && i.Message.Contains("keeps one"));

    if (!damageBase || !blockVar || !damageUpgrade || !blockUpgrade || !noError)
    {
        Console.WriteLine();
        Console.WriteLine("  [失败] 伤害与格挡的数值串了"
            + $"（伤害基础值={damageBase}，格挡固定值={blockVar}，"
            + $"伤害升级={damageUpgrade}，格挡升级={blockUpgrade}，未被误拦={noError}）。");
        Console.WriteLine();
        Console.WriteLine("RESULT: FAILED (damage and block interfere)");
        return 1;
    }

    Console.WriteLine("伤害与格挡各用各的变量，升级也各改各的（两个数值互不覆盖）。");
}

// 计算型的**重复次数**：伤害保持固定，次数才走计算，两者不能合并成一套。
//
// 原版"扯碎"的形状就是"固定 5 点伤害 + 次数 = 1 + 失去生命的次数"。若哪天有人图省事把
// 次数与数值合成一套来源，这张卡的伤害会跟着一起变成计算型——那时 DamageVar 会被换成
// CalculatedDamageVar，一眼能从源码看出来，所以这里断言两边都在且各是各的。
{
    string grudge = generated["Cards/GrudgeRepaid.cs"];

    // 伤害必须是固定值（声明 DamageVar，而不是 CalculatedDamageVar）。
    bool fixedDamage = grudge.Contains("new DamageVar(5m");

    // 次数从算出来的方法里取，且循环读的是它。
    bool loopUsesCalc = Regex.IsMatch(grudge, @"for \(int i = 0; i < \(int\)CalcRepeat\d+\(cardPlay\.Target\); i\+\+\)");

    // 次数的基础值与数值的基础值是两个字段（1 与 5 各自独立）。
    bool separateBase = grudge.Contains("decimal _calcBase0 = 1m") && !grudge.Contains("_calcBase0 = 5m");

    // 升级：次数基础值 +1、伤害 +3，各改各的。
    bool upgradesBoth = grudge.Contains("_calcBase0 += 1m") && grudge.Contains("DynamicVars[\"Damage\"].UpgradeValueBy(3m)");

    if (!fixedDamage || !loopUsesCalc || !separateBase || !upgradesBoth)
    {
        Console.WriteLine();
        Console.WriteLine("  [失败] 计算型重复次数生成不对"
            + $"（固定伤害={fixedDamage}，循环用计算值={loopUsesCalc}，"
            + $"次数基础值独立={separateBase}，两处升级都在={upgradesBoth}）。");
        Console.WriteLine("         次数与数值必须各有一套基础值/来源，合并会导致伤害跟着次数一起变。");
        Console.WriteLine();
        Console.WriteLine("RESULT: FAILED (calculated repeat is wrong)");
        return 1;
    }

    Console.WriteLine("计算型重复次数与数值各用一套基础值/来源（伤害固定、次数计算），升级也各改各的。");
}

// 提高最大生命：走 GainMaxHp（不带 choiceContext），数值从声明的 MaxHpVar 读回，
// 升级改的是同一个变量——三者缺一就会"卡面显示 3 但实际给 0"或"升级看不见效果"。
{
    string glutton = generated["Cards/GluttonousBlow.cs"];

    bool declaresVar = glutton.Contains("new MaxHpVar(3m)");

    // GainMaxHp 的签名是 (Creature, decimal)，**不带** PlayerChoiceContext——传了编译不过，
    // 所以这里断言调用里没有它，顺手把这个易错点钉住。
    bool callsApi = glutton.Contains("CreatureCmd.GainMaxHp(")
        && !glutton.Contains("GainMaxHp(choiceContext");

    bool readsVar = glutton.Contains("DynamicVars[\"MaxHp\"].BaseValue");
    bool upgradesVar = glutton.Contains("DynamicVars[\"MaxHp\"].UpgradeValueBy(1m)");

    if (!declaresVar || !callsApi || !readsVar || !upgradesVar)
    {
        Console.WriteLine();
        Console.WriteLine("  [失败] 提高最大生命生成不对"
            + $"（声明变量={declaresVar}，调用 API={callsApi}，读变量={readsVar}，升级改变量={upgradesVar}）。");
        Console.WriteLine();
        Console.WriteLine("RESULT: FAILED (max HP effect is wrong)");
        return 1;
    }

    Console.WriteLine("提高最大生命声明了 MaxHpVar、按它取值、升级也改它。");
}

var exporter = new Exporter(gameDir)
{
    WebpConverter = ModForge.Core.Gfx.WebpConverter.Discover(@"D:\Tools", ""),
};
ExportResult result = exporter.Export(project, outDir);

foreach (string message in result.Messages)
{
    Console.WriteLine("  " + message);
}

if (!result.Success)
{
    Console.WriteLine();
    Console.WriteLine("RESULT: FAILED (export/compile)");
    return 1;
}

Console.WriteLine($"  Mod written under {outDir}");

// 校验器要能提示"计算型伤害与格挡共用基础值"这种游戏层面的冲突。
// 用一个反例确认提示真的会出现——否则这条检查有可能永远沉默。
Console.WriteLine();
Console.WriteLine("=== 校验器检查（两个基础值不一致**不**报错）===");

// 这条是刻意的**反向**断言：两个基础值不同时**不该**有校验警告。
//
// 共用一个 CalculationBase 是游戏的结构，后果是"填的值不会各按各的生效"。这件事界面上
// 已经写明了（勾选"启用基础值"时那行小字），再弹一条校验警告只是把同一句话重复一遍，
// 徒增噪音。所以这里断言"没有提到 CalculationBase 的提示"——哪天有人把那条警告加回来，
// 这个测试会立刻失败，逼他先想清楚是不是又要加噪音。
var conflict = new ModProject { Id = "ConflictProbe", Name = "基础值不一致探针", Author = "ModForge" };
conflict.Cards.Add(new CardDef
{
    Id = "CONFLICT_PROBE",
    Name = new LocalizedText("eng", "Conflict Probe"),

    // 描述引用这张卡**真的有**的变量，免得掺进"描述引用了不存在的数值"那条警告——
    // 那条会把可用占位符列出来（含 CalculationBase），于是老写法
    // （消息里含 "CalculationBase" 就算命中）会被无关警告误伤，测试就测不准了。
    Description = new LocalizedText("eng", "Deal {CalculatedDamage:diff()} damage and gain {CalculatedBlock:diff()} Block."),
    Pool = "ironclad",
    Type = CardType.Attack,
    Rarity = CardRarity.Common,
    Target = CardTargetType.AnyEnemy,
    EnergyCost = 1,
    Effects =
    {
        new EffectDef
        {
            Kind = EffectKind.DealDamage, Target = EffectTarget.Target, Amount = 3,
            Source = new ValueSource { Actor = ValueSourceActor.Self, Kind = ValueSourceKind.HandCount },
        },
        new EffectDef
        {
            Kind = EffectKind.GainBlock, Target = EffectTarget.Self, Amount = 7,
            Source = new ValueSource { Actor = ValueSourceActor.Self, Kind = ValueSourceKind.HandCount },
        },
    },
});

// 直接断言"**一条提示都没有**"：这才是这条用例真正要的结论。
// 用"消息里含某个词"当代理指标的话，任何一条无关提示（哪怕是对的）都会误伤。
IReadOnlyList<ProjectIssue> probeIssues = ProjectValidator.Validate(conflict, null);

if (probeIssues.Count > 0)
{
    Console.WriteLine("  [失败] 两个基础值不一致时冒出了校验提示——它们现在各占一个变量，不该再有话说。");
    foreach (ProjectIssue issue in probeIssues)
    {
        Console.WriteLine($"         {issue.Severity}: {issue.Message}");
    }

    Console.WriteLine();
    Console.WriteLine("RESULT: FAILED (validator added redundant noise)");
    return 1;
}

Console.WriteLine("  两个基础值不一致时不弹任何提示（各占一个变量，各升各的）。");

// "自动打出这张牌"缺条件时必须报错：这两种情况生成出来的代码要么静默失效、要么无限递归，
// 都不该等到游戏里才发现。逐个缺一样，确认都能拦下。
Console.WriteLine();
Console.WriteLine("=== 校验器检查（自动打出这张牌缺条件）===");

var missingTrigger = new ModProject { Id = "SelfPlayProbe", Name = "自动打出探针" };
missingTrigger.Cards.Add(new CardDef
{
    Id = "NO_TRIGGER",
    Name = new LocalizedText("eng", "No Trigger"),
    Pool = "ironclad",
    Type = CardType.Attack,
    Rarity = CardRarity.Common,
    Target = CardTargetType.AnyEnemy,
    EnergyCost = 1,
    Effects =
    {
        new EffectDef
        {
            Kind = EffectKind.AutoPlay, Target = EffectTarget.Self,
            Pick = CardPick.This(),

            // 时机是默认的"打出时"——会在打出时再打出自己。
            PileScope = PileScope.ExhaustPile,
        },
    },
});

bool caughtTrigger = ProjectValidator.Validate(missingTrigger, null)
    .Any(i => i.Severity == "error" && i.Message.Contains("StartOfTurn or EndOfTurn"));

var missingPile = new ModProject { Id = "SelfPlayProbe2", Name = "自动打出探针2" };
missingPile.Cards.Add(new CardDef
{
    Id = "NO_PILE",
    Name = new LocalizedText("eng", "No Pile"),
    Pool = "ironclad",
    Type = CardType.Attack,
    Rarity = CardRarity.Common,
    Target = CardTargetType.AnyEnemy,
    EnergyCost = 1,
    Effects =
    {
        new EffectDef
        {
            Kind = EffectKind.AutoPlay, Target = EffectTarget.Self,
            Pick = CardPick.This(),
            Trigger = EffectTrigger.StartOfTurn,

            // 没给牌堆范围——会退化成"在手牌里就自己出来"。
        },
    },
});

bool caughtPile = ProjectValidator.Validate(missingPile, null)
    .Any(i => i.Severity == "error" && i.Message.Contains("pile scope"));

if (!caughtTrigger || !caughtPile)
{
    Console.WriteLine($"  [失败] 缺条件没有被拦下（缺时机={caughtTrigger}，缺牌堆={caughtPile}）。");
    Console.WriteLine();
    Console.WriteLine("RESULT: FAILED (validator missed a self-auto-play mistake)");
    return 1;
}

Console.WriteLine("  缺时机、缺牌堆范围都如实报错（这两种情况分别会导致递归与误触发）。");

// 费用类的坏组合也要拦下：游戏对"免费"只给了本回合/本场战斗两种接口，而"直到打出"的
// 免费没法生成；张数 < 1 则什么也挑不到。两种情况都不该静默生成出空转的代码。
Console.WriteLine();
Console.WriteLine("=== 校验器检查（费用效果坏组合）===");

var freeUntilPlayed = new ModProject { Id = "CostProbe", Name = "费用探针" };
freeUntilPlayed.Cards.Add(new CardDef
{
    Id = "FREE_UNTIL_PLAYED",
    Name = new LocalizedText("eng", "Free Until Played"),
    Pool = "ironclad",
    Type = CardType.Skill,
    Rarity = CardRarity.Common,
    Target = CardTargetType.Self,
    EnergyCost = 1,
    Effects =
    {
        new EffectDef
        {
            Kind = EffectKind.SetCardsFree, Target = EffectTarget.Self,
            

            // 游戏没有"免费直到打出"这个接口。
            CostDuration = CostDuration.UntilPlayed,
        Pick = CardPick.InPile(PileKind.Hand, PickMode.All),
        },
    },
});

bool caughtFreeDuration = ProjectValidator.Validate(freeUntilPlayed, null)
    .Any(i => i.Severity == "error" && i.Message.Contains("until played"));

var zeroCount = new ModProject { Id = "CostProbe2", Name = "费用探针2" };
zeroCount.Cards.Add(new CardDef
{
    Id = "ZERO_COUNT",
    Name = new LocalizedText("eng", "Zero Count"),
    Pool = "ironclad",
    Type = CardType.Skill,
    Rarity = CardRarity.Common,
    Target = CardTargetType.Self,
    EnergyCost = 1,
    Effects =
    {
        new EffectDef
        {
            Kind = EffectKind.ModifyCardCost, Target = EffectTarget.Self,
            Amount = -1, 

            // 随机 0 张 —— 挑不到任何牌。
            
            Pick = CardPick.InPile(PileKind.Hand, PickMode.Random, 0),
        },
    },
});

bool caughtZeroCount = ProjectValidator.Validate(zeroCount, null)
    .Any(i => i.Severity == "error" && i.Message.Contains("count of at least 1"));

// "能力牌 + 打出一张牌时"必须被拦下。
//
// 它不报错也编译得过，只是永远不触发：能力牌打出后归入 PileType.None（不进任何牌堆），
// 而钩子只发给"待在战斗牌堆里"的卡，于是它连自己那一次出牌都看不见。
// 这种"静默失效"正是最该由校验器说话的情况，所以专门测一遍。
var powerReact = new ModProject { Id = "PowerReactProbe", Name = "能力反应探针" };
powerReact.Cards.Add(new CardDef
{
    Id = "POWER_REACT",
    Name = new LocalizedText("eng", "Power React"),
    Pool = "ironclad",
    Type = CardType.Power,
    Rarity = CardRarity.Rare,
    Target = CardTargetType.Self,
    EnergyCost = 1,
    Effects =
    {
        new EffectDef
        {
            Kind = EffectKind.GainBlock, Target = EffectTarget.Self, Amount = 1,
            Trigger = EffectTrigger.CardPlayed,
        },
    },
});

bool caughtPowerReact = ProjectValidator.Validate(powerReact, null)
    .Any(i => i.Severity == "error" && i.Message.Contains("Power card cannot react"));

// "指定卡"却没填是哪张：校验器要报错，生成器要**不生成任何代码**（而不是默认成"任意牌"，
// 那等于静默换成另一个效果；也不是 `if (false)`，那会引出 CS0162 不可达代码警告）。
var missingCard = new ModProject { Id = "MissingCardProbe", Name = "缺卡名探针" };
missingCard.Cards.Add(new CardDef
{
    Id = "MISSING_CARD",
    Name = new LocalizedText("eng", "Missing Card"),
    Pool = "ironclad",
    Type = CardType.Skill,
    Rarity = CardRarity.Common,
    Target = CardTargetType.Self,
    EnergyCost = 1,
    Effects =
    {
        new EffectDef
        {
            Kind = EffectKind.GainBlock, Target = EffectTarget.Self, Amount = 1,
            Trigger = EffectTrigger.CardPlayed, CardFilter = CardFilterKind.SpecificCard,
        },
    },
});

bool caughtMissingCard = ProjectValidator.Validate(missingCard, null)
    .Any(i => i.Severity == "error" && i.Message.Contains("filtered to one specific card"));

string missingCardSource = new CodeGenerator(missingCard).GenerateAll()["Cards/MissingCard.cs"];

if (!caughtFreeDuration || !caughtZeroCount || !caughtPowerReact || !caughtMissingCard
    || missingCardSource.Contains("if (false)")
    || missingCardSource.Contains("cardPlay.Card.Owner == Owner"))
{
    Console.WriteLine($"  [失败] 坏组合没有被拦下（免费+直到打出={caughtFreeDuration}，"
        + $"张数 0={caughtZeroCount}，能力牌反应出牌={caughtPowerReact}，"
        + $"指定卡缺卡名={caughtMissingCard}，"
        + $"或缺卡名时仍生成了判断={missingCardSource.Contains("cardPlay.Card.Owner == Owner")}）。");
    Console.WriteLine();
    Console.WriteLine("RESULT: FAILED (validator missed a cost-effect mistake)");
    return 1;
}

Console.WriteLine("  \"免费+直到打出\"、\"张数 0\"、\"能力牌反应别人出牌\"、\"指定卡缺卡名\""
    + "都如实报错，也不会生成空转或不可达的代码。");

// ---------------------------------------------------------------------------
// 目标那三条规则：玩家轴的"目标"必须是玩家、单人里的"任意友方"、状态里的"目标"。
//
// 三条都不报错也编译得过，代价是**运行时空引用**或"这档名不副实"——正是该由校验器说话的
// 那类问题，所以逐条测。反例（合法组合）也要测：不许把对的组合一起拦下。
// ---------------------------------------------------------------------------
Console.WriteLine();
Console.WriteLine("=== 校验器检查（目标）===");

var playerToEnemy = new ModProject { Id = "TargetProbe", Name = "目标探针" };
playerToEnemy.Cards.Add(new CardDef
{
    Id = "ENERGY_TO_ENEMY",
    Name = new LocalizedText("eng", "Energy To Enemy"),
    Pool = "ironclad",
    Type = CardType.Skill,
    Rarity = CardRarity.Common,
    Target = CardTargetType.AnyEnemy,
    EnergyCost = 1,

    // 玩家轴（获得能量）+ 选定目标：卡牌目标是敌人 → 生成 `cardPlay.Target.Player!` 会空引用。
    Effects = { new EffectDef { Kind = EffectKind.GainEnergy, Target = EffectTarget.Target, Amount = 1 } },
});

bool caughtPlayerToEnemy = ProjectValidator.Validate(playerToEnemy, null)
    .Any(i => i.Severity == "error" && i.Message.Contains("which gives no player"));

// 同一个效果换成"任意友方"就是合法的（那里选定的目标确实是一个玩家）。
playerToEnemy.Cards[0].Target = CardTargetType.AnyAlly;

bool allowedPlayerToAlly = !ProjectValidator.Validate(playerToEnemy, null)
    .Any(i => i.Severity == "error" && i.Message.Contains("which gives no player"));

bool warnedAnyAlly = ProjectValidator.Validate(playerToEnemy, null)
    .Any(i => i.Severity == "warn" && i.Message.Contains("multiplayer co-op"));

// 状态里的"目标"：没有出牌目标，这一档按旧口径落地，所以要提醒一句（不是错误——老工程
// 里这一档是常态，报错误等于把他们的工程判成坏的）。
var powerWithTarget = new ModProject { Id = "TargetProbe2", Name = "目标探针2" };
powerWithTarget.Powers.Add(new PowerDef
{
    Id = "DOOM_TICK",
    Name = new LocalizedText("eng", "Doom Tick"),
    Description = "At the end of your turn, deal damage.",
    Kind = PowerKind.Buff,
    Triggers =
    {
        new TriggeredEffect
        {
            When = PowerTrigger.TurnEnd,
            Effect = new EffectDef { Kind = EffectKind.DealDamage, Target = EffectTarget.Target, Amount = 2 },
        },
    },
});

IReadOnlyList<ProjectIssue> powerTargetIssues = ProjectValidator.Validate(powerWithTarget, null);

if (!caughtPlayerToEnemy || !allowedPlayerToAlly || !warnedAnyAlly
    || !powerTargetIssues.Any(i => i.Severity == "warn" && i.Message.Contains("no played target")))
{
    Console.WriteLine($"  [失败] 目标那三条规则没测住（玩家轴给敌人={caughtPlayerToEnemy}，"
        + $"换成友方后仍报错={!allowedPlayerToAlly}，任意友方没提醒={!warnedAnyAlly}，"
        + $"状态里的目标没提醒={!powerTargetIssues.Any(i => i.Message.Contains("no played target"))}）。");

    foreach (ProjectIssue issue in ProjectValidator.Validate(playerToEnemy, null).Concat(powerTargetIssues))
    {
        Console.WriteLine($"    [{issue.Severity}] {issue.Where}: {issue.Message}");
    }

    Console.WriteLine();
    Console.WriteLine("RESULT: FAILED (validator missed a target mistake)");
    return 1;
}

Console.WriteLine("  \"玩家轴的效果给了敌人\"报错并给出改法，\"任意友方\"与\"状态里的目标\"各给一条提醒"
    + "（老工程的那一档按旧口径落地，所以只提醒、不判错）。");

// 玩家宠物那一组：现在只有**一条**不成立路径——玩家轴的效果用不了它
// （那些命令收 `Player`，而宠物是 `Creature`，编译不过）。
//
// "这条效果支不支持宠物"那条判据**已经不是一条独立规则**了：判据是作用域
// （造物轴 ⇒ 支持，因为造物轴的命令都收 Creature，而宠物就是 Creature）。所以以前
// 拿"失去生命"当反例的那种组合现在是**合法**的——正是用户报的缺口。
Console.WriteLine();
Console.WriteLine("=== 校验器检查（玩家宠物那一组）===");

var petProbe = new ModProject { Id = "PetProbe", Name = "宠物探针" };

petProbe.Powers.Add(new PowerDef
{
    Id = "PET_MARK",
    Name = new LocalizedText("eng", "Pet Mark").With("zhs", "宠物印记"),
    Description = "Test.",
    Kind = PowerKind.Buff,
});

// （1）非法：玩家轴的效果用了宠物档（收 Player 的命令接不住 Creature）。
petProbe.Cards.Add(new CardDef
{
    Id = "PET_ON_PLAYER_EFFECT",
    Name = new LocalizedText("eng", "Pet On Player Effect"),
    Description = new LocalizedText("eng", "Test."),
    Pool = "ironclad",
    Type = CardType.Skill,
    Rarity = CardRarity.Common,
    Target = CardTargetType.Self,
    EnergyCost = 1,
    Effects =
    {
        new EffectDef { Kind = EffectKind.DrawCards, Target = EffectTarget.SelfPet, Amount = 1 },
    },
});

// （2）非法：玩家轴上留着旧值 RandomAlly（那一轴上它已改成 RandomPlayer / RandomOtherAlly
// 两档；RandomAlly 现在只属于造物轴，那里它的意思是"含自己"）。提示要指出改用哪一档。
petProbe.Cards.Add(new CardDef
{
    Id = "PET_OLD_RANDOM_ALLY",
    Name = new LocalizedText("eng", "Old Random Ally"),
    Description = new LocalizedText("eng", "Test."),
    Pool = "ironclad",
    Type = CardType.Skill,
    Rarity = CardRarity.Common,
    Target = CardTargetType.Self,
    EnergyCost = 1,
    Effects =
    {
        new EffectDef { Kind = EffectKind.DrawCards, Target = EffectTarget.RandomAlly, Amount = 1 },
    },
});

// （3）非法：「目标的奥斯提」要一名玩家，而这张牌的目标是敌人（给不出玩家）。
petProbe.Cards.Add(new CardDef
{
    Id = "PET_TARGET_NOT_PLAYER",
    Name = new LocalizedText("eng", "Pet Target Not Player"),
    Description = new LocalizedText("eng", "Test."),
    Pool = "ironclad",
    Type = CardType.Attack,
    Rarity = CardRarity.Common,
    Target = CardTargetType.AnyEnemy,
    EnergyCost = 1,
    Effects =
    {
        new EffectDef { Kind = EffectKind.DealDamage, Target = EffectTarget.TargetPet, Amount = 2 },
    },
});

// （4）合法：那几条以前"配不了宠物"的效果现在都配得上——它们是造物轴。
petProbe.Cards.Add(new CardDef
{
    Id = "PET_OK",
    Name = new LocalizedText("eng", "Pet Ok"),
    Description = new LocalizedText("eng", "Test."),
    Pool = "ironclad",
    Type = CardType.Skill,
    Rarity = CardRarity.Common,
    Target = CardTargetType.AnyAlly,
    EnergyCost = 1,
    Effects =
    {
        // 以前只有前两条能配。
        new EffectDef
        {
            Kind = EffectKind.ApplyPower, Target = EffectTarget.SelfPet,
            PowerId = "POWER.PET_MARK", Amount = 1,
        },
        new EffectDef { Kind = EffectKind.GainBlock, Target = EffectTarget.AllAlliesPet, Amount = 1 },

        // 这几条是用户报的缺口。
        new EffectDef { Kind = EffectKind.LoseHp, Target = EffectTarget.SelfPet, Amount = 1 },
        new EffectDef { Kind = EffectKind.GainMaxHp, Target = EffectTarget.SelfPet, Amount = 1 },
        new EffectDef { Kind = EffectKind.LoseMaxHp, Target = EffectTarget.AllAlliesPet, Amount = 1 },
        new EffectDef
        {
            Kind = EffectKind.RemovePower, Target = EffectTarget.SelfPet,
            PowerId = "POWER.PET_MARK",
        },
        new EffectDef { Kind = EffectKind.TriggerDoom, Target = EffectTarget.RandomAllyPet },

        // 「目标的奥斯提」+ 卡牌目标是"任意友方"（给得出一名玩家）→ 合法。
        new EffectDef { Kind = EffectKind.DealDamage, Target = EffectTarget.TargetPet, Amount = 2 },
    },
});

IReadOnlyList<ProjectIssue> petIssues = ProjectValidator.Validate(petProbe, null);

bool caughtPlayerEffect = petIssues.Any(i =>
    i.Severity == "error" && i.Where == "PET_ON_PLAYER_EFFECT"
    && i.Message.Contains("only creature-scoped effects can act on it"));
// 提示按"要一名玩家"那套写（`which gives no player to act on`）。
bool caughtTargetBand = petIssues.Any(i =>
    i.Severity == "error" && i.Where == "PET_TARGET_NOT_PLAYER"
    && i.Message.Contains("which gives no player"));

// 玩家轴上的旧 RandomAlly：报错，并指出该改成 RandomPlayer（含自己）或 RandomOtherAlly（不含）。
bool caughtOldRandomAlly = petIssues.Any(i =>
    i.Severity == "error" && i.Where == "PET_OLD_RANDOM_ALLY"
    && i.Message.Contains("RandomOtherAlly"));
bool allowedPetOk = !petIssues.Any(i => i.Severity == "error" && i.Where == "PET_OK");

// （5）"玩家宠物 + 状态"：状态里没有出牌目标，「目标的奥斯提」落不了地，所以报错。
var petPower = new ModProject { Id = "PetPowerProbe", Name = "宠物状态探针" };
petPower.Powers.Add(new PowerDef
{
    Id = "PET_DOOM",
    Name = new LocalizedText("eng", "Pet Doom"),
    Description = "At the end of your turn, deal damage.",
    Kind = PowerKind.Buff,
    Triggers =
    {
        new TriggeredEffect
        {
            When = PowerTrigger.TurnEnd,
            Effect = new EffectDef
            {
                Kind = EffectKind.DealDamage, Target = EffectTarget.TargetPet, Amount = 2,
            },
        },
    },
});

bool caughtPetPower = ProjectValidator.Validate(petPower, null)
    .Any(i => i.Severity == "error" && i.Message.Contains("powers have no played target"));

if (!caughtPlayerEffect || !caughtTargetBand || !caughtOldRandomAlly || !allowedPetOk || !caughtPetPower)
{
    Console.WriteLine($"  [失败] 宠物档那几条没测住（玩家轴用了宠物档={caughtPlayerEffect}，"
        + $"目标的奥斯提缺玩家={caughtTargetBand}，玩家轴旧 RandomAlly={caughtOldRandomAlly}，"
        + $"合法的造物轴 + 宠物被误拦={!allowedPetOk}，"
        + $"状态里的目标的奥斯提={caughtPetPower}）。");

    foreach (ProjectIssue issue in petIssues.Concat(ProjectValidator.Validate(petPower, null)))
    {
        Console.WriteLine($"    [{issue.Severity}] {issue.Where}: {issue.Message}");
    }

    Console.WriteLine();
    Console.WriteLine("RESULT: FAILED (validator missed a pet-band mistake)");
    return 1;
}

Console.WriteLine("  玩家轴用宠物档、目标的奥斯提缺玩家、玩家轴上的旧 RandomAlly、"
    + "状态里的目标的奥斯提——四种都报错；"
    + "造物轴九条（含原来漏掉的失去生命 / 两个最大生命 / 移除状态 / 灾厄判定）配上宠物都不被拦。");

// 临时牌集合那四条规则：ID 不能进键、一个 ID 只能定义一次、读的必须有人定义、
// 而且"存/取集合"的能力要与效果种类对上。四种错法**都是静默的**（那条效果什么也不做），
// 所以必须拦在这里。反例（合法的一对）也要测。
Console.WriteLine();
Console.WriteLine("=== 校验器检查（临时牌集合）===");

var setProbe = new ModProject { Id = "SetProbe", Name = "集合探针" };

// （1）ID 里带点：键用 "." 分段，带点的 ID 会让"按地址前缀清空"误伤别的地址。
setProbe.Cards.Add(new CardDef
{
    Id = "SET_BAD_ID",
    Name = new LocalizedText("eng", "Bad Id"),
    Description = new LocalizedText("eng", "Test."),
    Pool = "ironclad",
    Type = CardType.Skill,
    Rarity = CardRarity.Common,
    Target = CardTargetType.Self,
    EnergyCost = 1,
    Effects =
    {
        new EffectDef
        {
            Kind = EffectKind.DrawCards, Target = EffectTarget.Self, Amount = 1, SetId = "A.B",
        },
    },
});

// （2）同一个 ID 定义两次：读它的那条不知道读哪一个。
setProbe.Cards.Add(new CardDef
{
    Id = "SET_TWICE",
    Name = new LocalizedText("eng", "Twice"),
    Description = new LocalizedText("eng", "Test."),
    Pool = "ironclad",
    Type = CardType.Skill,
    Rarity = CardRarity.Common,
    Target = CardTargetType.Self,
    EnergyCost = 1,
    Effects =
    {
        new EffectDef { Kind = EffectKind.DrawCards, Target = EffectTarget.Self, Amount = 1, SetId = "Same" },
        new EffectDef { Kind = EffectKind.DrawCards, Target = EffectTarget.Self, Amount = 1, SetId = "Same" },
    },
});

// （3）读一个没人定义的集合（最要紧的一条：生成出来是空集合，游戏里什么都不做）。
setProbe.Cards.Add(new CardDef
{
    Id = "SET_UNKNOWN",
    Name = new LocalizedText("eng", "Unknown"),
    Description = new LocalizedText("eng", "Test."),
    Pool = "ironclad",
    Type = CardType.Skill,
    Rarity = CardRarity.Common,
    Target = CardTargetType.Self,
    EnergyCost = 1,
    Effects =
    {
        new EffectDef
        {
            Kind = EffectKind.UpgradeCardsInPile, Target = EffectTarget.Self,
            Pick = new CardPick { Source = PickSource.Set, SetId = "Nope", Mode = PickMode.All },
        },
    },
});

// （4）"存集合"挂在不产牌的效果上（伤害没有东西可存）。
setProbe.Cards.Add(new CardDef
{
    Id = "SET_BAD_KIND",
    Name = new LocalizedText("eng", "Bad Kind"),
    Description = new LocalizedText("eng", "Test."),
    Pool = "ironclad",
    Type = CardType.Attack,
    Rarity = CardRarity.Common,
    Target = CardTargetType.AnyEnemy,
    EnergyCost = 1,
    Effects =
    {
        new EffectDef
        {
            Kind = EffectKind.DealDamage, Target = EffectTarget.Target, Amount = 6, SetId = "Damage",
        },
    },
});

// （5）反例：合法的一对（抽牌存 → 降费取）不许被拦。
setProbe.Cards.Add(new CardDef
{
    Id = "SET_OK",
    Name = new LocalizedText("eng", "Set Ok"),
    Description = new LocalizedText("eng", "Test."),
    Pool = "ironclad",
    Type = CardType.Skill,
    Rarity = CardRarity.Common,
    Target = CardTargetType.Self,
    EnergyCost = 1,
    Effects =
    {
        new EffectDef { Kind = EffectKind.DrawCards, Target = EffectTarget.Self, Amount = 1, SetId = "Drawn" },
        new EffectDef
        {
            Kind = EffectKind.ModifyCardCost, Target = EffectTarget.Self, Amount = -1,
            CostDuration = CostDuration.ThisTurn,
            Pick = new CardPick { Source = PickSource.Set, SetId = "Drawn", Mode = PickMode.All },
        },
    },
});

IReadOnlyList<ProjectIssue> setIssues = ProjectValidator.Validate(setProbe, null);

bool caughtBadId = setIssues.Any(i => i.Severity == "error" && i.Where == "SET_BAD_ID");
bool caughtTwice = setIssues.Any(i => i.Severity == "error" && i.Where == "SET_TWICE");
bool caughtUnknown = setIssues.Any(i => i.Severity == "error" && i.Where == "SET_UNKNOWN");
bool caughtBadKind = setIssues.Any(i => i.Severity == "error" && i.Where == "SET_BAD_KIND");
bool allowedSetOk = !setIssues.Any(i => i.Severity == "error" && i.Where == "SET_OK");

if (!caughtBadId || !caughtTwice || !caughtUnknown || !caughtBadKind || !allowedSetOk)
{
    Console.WriteLine($"  [失败] 临时牌集合那几条没测住（ID 进不了键={caughtBadId}，重复定义={caughtTwice}，"
        + $"读了没人定义的={caughtUnknown}，不产牌却要存={caughtBadKind}，合法组合被误拦={!allowedSetOk}）。");

    foreach (ProjectIssue issue in setIssues)
    {
        Console.WriteLine($"    [{issue.Severity}] {issue.Where}: {issue.Message}");
    }

    Console.WriteLine();
    Console.WriteLine("RESULT: FAILED (validator missed a card-set mistake)");
    return 1;
}

Console.WriteLine("  ID 进不了键 / 重复定义 / 读了没人定义的集合 / 不产牌却要存——四种错法各有各的报错，"
    + "合法的\"抽牌存 → 降费取\"不被拦。");

// "计算型次数却没选来源"这个坏组合，以及负数最大生命
// （GainMaxHp/LoseMaxHp 都要求非负，负数是"用另一个效果"的意思）。
//
// 前者以前是个能表达出来的状态、靠校验器报 error 拦下。现在**表达不出来了**：
// 算不算计算型由来源推出（见 EffectDef.CalculatedRepeat），没有来源就是固定次数，
// 所以这里改成断言这条不变量——不生成"算出来却永远不变"的循环次数。
Console.WriteLine();
Console.WriteLine("=== 检查（计算次数的不变量 / 最大生命的坏组合）===");

var badRepeat = new ModProject { Id = "BadRepeatProbe", Name = "坏次数探针" };
var noSourceRepeat = new EffectDef
{
    Kind = EffectKind.DealDamage, Target = EffectTarget.Target, Amount = 5,

    // 想设成计算型，但没给来源 —— 结果是固定次数（而不是一个算出来却永远不变的数）。
    CalculatedRepeat = true,
    RepeatSource = new ValueSource { Kind = ValueSourceKind.None },
};

badRepeat.Cards.Add(new CardDef
{
    Id = "BAD_REPEAT",
    Name = new LocalizedText("eng", "Bad Repeat"),
    Pool = "ironclad",
    Type = CardType.Attack,
    Rarity = CardRarity.Common,
    Target = CardTargetType.AnyEnemy,
    EnergyCost = 1,
    Effects = { noSourceRepeat },
});

var badMaxHp = new ModProject { Id = "BadMaxHpProbe", Name = "坏最大生命探针" };
badMaxHp.Cards.Add(new CardDef
{
    Id = "BAD_MAX_HP",
    Name = new LocalizedText("eng", "Bad Max HP"),
    Pool = "ironclad",
    Type = CardType.Skill,
    Rarity = CardRarity.Common,
    Target = CardTargetType.Self,
    EnergyCost = 1,
    Effects =
    {
        new EffectDef { Kind = EffectKind.GainMaxHp, Target = EffectTarget.Self, Amount = -3 },
    },
});

bool repeatDegradedToFixed = !noSourceRepeat.CalculatedRepeat;

bool caughtBadMaxHp = ProjectValidator.Validate(badMaxHp, null)
    .Any(i => i.Severity == "error" && i.Message.Contains("non-negative amount"));

if (!repeatDegradedToFixed || !caughtBadMaxHp)
{
    Console.WriteLine($"  [失败] 坏组合没有被处理（没来源的次数退回固定={repeatDegradedToFixed}，"
        + $"负数最大生命={caughtBadMaxHp}）。");
    Console.WriteLine();
    Console.WriteLine("RESULT: FAILED (a sourceless calculated repeat / max-HP mistake got through)");
    return 1;
}

Console.WriteLine("  没有来源的\"计算型次数\"退回固定次数、负数最大生命如实报错。");

// 状态里的**计算型**数值必须拦下：状态没有动态变量表，那条路走不通。
//
// 生成状态时不会发 CanonicalVars（状态描述用的是 {Amount}，不是卡牌那套变量），
// 于是 DynamicVars 是个空集；而计算型的伤害/格挡在生成时会写成
// `DynamicVars.CalculatedDamage.Calculate(...)`——那句话只有卡牌成立。
// 运行时会在**画状态说明时**（战斗刚开始）抛 KeyNotFoundException，离改的时候很远。
//
// 所以校验器直接拦：要么填固定值，要么做成卡牌效果。
Console.WriteLine();
Console.WriteLine("=== 校验器检查（状态里的计算型数值）===");

var calcPower = new ModProject { Id = "CalcPowerProbe", Name = "状态计算探针" };
calcPower.Powers.Add(new PowerDef
{
    Id = "CALC_POWER",
    Name = new LocalizedText("eng", "Calc Power"),
    Description = new LocalizedText("eng", "Deal damage equal to your hand size each turn.")
        .With("zhs", "每回合造成 等于手牌数 的伤害。"),
    Kind = PowerKind.Buff,
    Stack = PowerStack.Counter,
    Triggers =
    {
        new TriggeredEffect
        {
            When = PowerTrigger.TurnStart,
            Effect = new EffectDef
            {
                Kind = EffectKind.DealDamage, Target = EffectTarget.Target,
                Source = new ValueSource { Actor = ValueSourceActor.Self, Kind = ValueSourceKind.HandCount },
            },
        },
    },
});

IReadOnlyList<ProjectIssue> powerIssues = ProjectValidator.Validate(calcPower, null);
bool powerCalculatedCaught = powerIssues.Any(i => i.Severity == "error" && i.Message.Contains("no dynamic variables"));

if (!powerCalculatedCaught)
{
    Console.WriteLine("  [失败] 状态里的计算型数值没有被拦下——它会在画状态说明时抛 KeyNotFoundException。");
    foreach (ProjectIssue issue in powerIssues)
    {
        Console.WriteLine($"         {issue.Severity}: {issue.Message}");
    }

    Console.WriteLine();
    Console.WriteLine("RESULT: FAILED (power-side calculated value not rejected)");
    return 1;
}

Console.WriteLine("  状态里的计算型数值如实报错（状态没有可用的动态变量表）。");

// 同一种数值的两个计算型：**现在合法了**，两条各占一个槽、各算各的。
//
// 这原先是个错误（生成器把所有同种效果合并到一个变量：第二条的算式完全蒸发，
// 代码编译正常、卡也能用，只是行为完全不是写的那样）。生成器改成按效果分配槽位后，
// 这类牌可以正常做出来——原版"流光"也有两个格挡值各自升级，机制上本来就是支持的。
Console.WriteLine();
Console.WriteLine("=== 同一种数值的两个计算型（现在可用）===");

var twoCalc = new ModProject { Id = "TwoCalcProbe", Name = "双计算探针" };
twoCalc.Cards.Add(new CardDef
{
    Id = "TWO_CALC",
    Name = new LocalizedText("eng", "Two Calc"),
    Pool = "silent",
    Type = CardType.Attack,
    Rarity = CardRarity.Common,
    Target = CardTargetType.AnyEnemy,
    EnergyCost = 1,
    Effects =
    {
        // 第一条：3 × 手牌数 + 2
        new EffectDef
        {
            Kind = EffectKind.DealDamage, Target = EffectTarget.Target,
            Amount = 2,
            Source = new ValueSource { Actor = ValueSourceActor.Self, Kind = ValueSourceKind.HandCount, PerUnit = 3 },
            Upgrade = new EffectUpgrade { AmountDelta = 1, PerUnitDelta = 1 },
        },

        // 第二条：0.25 × 目标已损失生命（不带基础值，纯系数）
        new EffectDef
        {
            Kind = EffectKind.DealDamage, Target = EffectTarget.Target,
            Source = new ValueSource { Actor = ValueSourceActor.Target, Kind = ValueSourceKind.MissingHp, PerUnit = 0.25m },
            Upgrade = new EffectUpgrade { PerUnitDelta = 0.25m },
        },
    },
});

if (ProjectValidator.Validate(twoCalc, null).Any(i => i.Severity == "error"))
{
    Console.WriteLine("  [失败] 两条计算型伤害被校验器拦下了——它们现在应当是可用的。");
    foreach (ProjectIssue issue in ProjectValidator.Validate(twoCalc, null).Where(i => i.Severity == "error"))
    {
        Console.WriteLine($"         {issue.Message}");
    }

    Console.WriteLine();
    Console.WriteLine("RESULT: FAILED (two calculated values wrongly rejected)");
    return 1;
}

{
    string src = new CodeGenerator(twoCalc).GenerateAll()["Cards/TwoCalc.cs"];

    // 两条各有一套变量：第二个计算值、第二套基础值/增量，都要在。
    bool twoCalcVars = src.Contains("\"CalculatedDamage2\"") && src.Contains("\"CalculationBase2\"")
        && src.Contains("\"ExtraDamage2\"");

    // 两个乘数方法各在（第一条按手牌数、第二条按已损失生命），方法名不能撞
    // （第二个带序号：DamageMultiplier2）。
    bool twoMultipliers = Regex.Matches(src, @"private static decimal \w*Multiplier\d*\(CardModel card").Count == 2;

    // 第二条的**算式**真的进了代码（这正是原先整段蒸发的那部分）：
    // 它的乘数方法里按目标的已损失生命取数（生成出来是 target!.MaxHp - target!.CurrentHp）。
    bool secondFormulaKept = Regex.IsMatch(src, @"\.MaxHp\s*-\s*[\w!.]*\.CurrentHp");

    // 两条各读各的计算变量（第一条走原版属性，第二条走索引器 + 转型）。
    bool readsFirst = src.Contains("DynamicVars.CalculatedDamage.Calculate(");
    bool readsSecond = src.Contains("((CalculatedVar)DynamicVars[\"CalculatedDamage2\"]).Calculate(");

    // 升级各改各的：两条的增量分别落在自己的变量上。
    bool separateUpgrades = src.Contains("DynamicVars[\"ExtraDamage\"].UpgradeValueBy(1m)")
        && src.Contains("DynamicVars[\"ExtraDamage2\"].UpgradeValueBy(0.25m)");

    // 第二个槽要用运行时子类（原版类型固定读无序号的那两个名字）。
    bool usesSlotVar = src.Contains("SlotCalculatedVar");

    if (!twoCalcVars || !twoMultipliers || !secondFormulaKept || !readsFirst || !readsSecond
        || !separateUpgrades || !usesSlotVar)
    {
        Console.WriteLine();
        Console.WriteLine("  [失败] 两条计算型伤害没有各占一套变量"
            + $"（两套变量={twoCalcVars}，两个乘数方法={twoMultipliers}，"
            + $"第二条算式保留={secondFormulaKept}，各读各的={readsFirst}/{readsSecond}，"
            + $"升级分开={separateUpgrades}，用子类={usesSlotVar}）。");
        Console.WriteLine("         第二条的算式若丢失，卡面写的与实际打出的就不是一回事了。");
        Console.WriteLine();
        Console.WriteLine("RESULT: FAILED (second calculated value lost)");
        return 1;
    }

    Console.WriteLine("  两条计算型伤害各有一套变量与算式，升级各改各的（第一条仍走原版形状）。");
}

// **计算型伤害 + 计算型格挡**（两种数值各一条）：这一类原先会静默串味。
//
// 当时的判据是"同类里第几个"：伤害与格挡各自都是同类第一个，于是两个槽都拿了原版类型。
// 但基础值是按**全局**编号的（游戏只有一组 CalculationBase），后出现的那个槽拿到的是
// CalculationBase2——原版类型却写死了读 CalculationBase，于是**格挡数跟着伤害的基础值走**。
// 这种错误编译正常、卡面预览还"看起来对"（因为预览读的是自己那个变量），
// 只在结算时才现形，是最难查的一类。
Console.WriteLine();
Console.WriteLine("=== 计算型伤害 + 计算型格挡（槽位不串味）===");

{
    var mixed = new ModProject { Id = "MixedCalcProbe", Name = "混合计算探针" };
    mixed.Cards.Add(new CardDef
    {
        Id = "MIXED_CALC",
        Name = new LocalizedText("eng", "Mixed Calc"),
        Pool = "ironclad",
        Type = CardType.Attack,
        Rarity = CardRarity.Uncommon,
        Target = CardTargetType.AnyEnemy,
        EnergyCost = 1,
        Effects =
        {
            // 计算型伤害：3 × 手牌数 + 5（先出现，占原版名字）
            new EffectDef
            {
                Kind = EffectKind.DealDamage, Target = EffectTarget.Target,
                Amount = 5,
                Source = new ValueSource { Actor = ValueSourceActor.Self, Kind = ValueSourceKind.HandCount, PerUnit = 3 },
                Upgrade = new EffectUpgrade { AmountDelta = 2 },
            },

            // 计算型格挡：2 × 手牌数 + 7（后出现，基础值必须落到 CalculationBase2）
            new EffectDef
            {
                Kind = EffectKind.GainBlock, Target = EffectTarget.Self,
                Amount = 7,
                Source = new ValueSource { Actor = ValueSourceActor.Self, Kind = ValueSourceKind.HandCount, PerUnit = 2 },
                Upgrade = new EffectUpgrade { AmountDelta = 3 },
            },
        },
    });

    string src = new CodeGenerator(mixed).GenerateAll()["Cards/MixedCalc.cs"];

    // 两个基础值各占一个名字（全局编号：先出现的拿原名）。
    bool twoBases = src.Contains("new CalculationBaseVar(5m)")
        && src.Contains("\"CalculationBase2\", 7m");

    // **关键**：格挡槽不能再走原版类型——它会读 CalculationBase（伤害的那个），
    // 必须换成读自己名字的运行时子类，且 isDamage: false（预览走格挡钩子）。
    bool blockUsesSlotVar = Regex.IsMatch(
        src, @"SlotCalculatedVar\(""CalculatedBlock"", ""CalculationBase2"", ""CalculationExtra""[^)]*isDamage: false");

    // 取值同理：格挡走索引器 + 转型（属性会转型成原版类型，抛 InvalidCastException）。
    bool blockReadsOwn = src.Contains("((CalculatedVar)DynamicVars[\"CalculatedBlock\"]).Calculate(");

    // 升级各改各的：伤害的基础值进 CalculationBase、格挡的进 CalculationBase2。
    bool separateUpgrades = src.Contains("DynamicVars[\"CalculationBase\"].UpgradeValueBy(2m)")
        && src.Contains("DynamicVars[\"CalculationBase2\"].UpgradeValueBy(3m)");

    if (!twoBases || !blockUsesSlotVar || !blockReadsOwn || !separateUpgrades)
    {
        Console.WriteLine();
        Console.WriteLine("  [失败] 计算型伤害与计算型格挡没各占一套"
            + $"（两个基础值={twoBases}，格挡用子类={blockUsesSlotVar}，"
            + $"格挡读自己={blockReadsOwn}，升级分开={separateUpgrades}）。");
        Console.WriteLine("         格挡若走原版类型，会静默读到伤害的基础值——只在结算时现形。");
        Console.WriteLine();
        Console.WriteLine("RESULT: FAILED (mixed calculated values share a base)");
        return 1;
    }

    Console.WriteLine("  计算型伤害与计算型格挡各占一个基础值，格挡走子类读自己那个（不再串味）。");
}

// 同一个计算型槽的**增量变量**也要认自己那个名字（伤害的 ExtraDamage、格挡的 CalculationExtra）。
//
// 这两个名字是游戏自己埋的坑：CalculatedDamageVar 覆写了 GetExtraVar 取 ExtraDamage，
// CalculatedBlockVar 走基类取 CalculationExtra。写错不会编译失败，是运行时 KeyNotFoundException。
Console.WriteLine();
Console.WriteLine("=== 计算型增量的两套名字（游戏埋的坑）===");

{
    var trap = new ModProject { Id = "ExtraNameProbe", Name = "增量名探针" };
    trap.Cards.Add(new CardDef
    {
        Id = "EXTRA_NAME",
        Name = new LocalizedText("eng", "Extra Name"),
        Pool = "ironclad",
        Type = CardType.Skill,
        Rarity = CardRarity.Common,
        Target = CardTargetType.Self,
        EnergyCost = 1,
        Effects =
        {
            new EffectDef
            {
                Kind = EffectKind.GainBlock, Target = EffectTarget.Self,
                Amount = 2,
                Source = new ValueSource { Actor = ValueSourceActor.Self, Kind = ValueSourceKind.HandCount, PerUnit = 1 },
            },
        },
    });

    string src = new CodeGenerator(trap).GenerateAll()["Cards/ExtraName.cs"];

    // 格挡的增量必须是 CalculationExtraVar（不是 ExtraDamageVar）。
    bool rightName = src.Contains("new CalculationExtraVar(1m)") && !src.Contains("new ExtraDamageVar(1m)");

    if (!rightName)
    {
        Console.WriteLine();
        Console.WriteLine("  [失败] 计算型格挡的增量变量名不对（必须是 CalculationExtra，写错只在运行时抛异常）。");
        Console.WriteLine();
        Console.WriteLine("RESULT: FAILED (wrong extra var name for block)");
        return 1;
    }

    Console.WriteLine("  计算型格挡用 CalculationExtraVar、伤害用 ExtraDamageVar（两边不串）。");
}

// 第二个**计算型伤害**槽的增量变量：用运行时子类承载，附魔预览才与
// 描述里引用的那个数一致（原版 ExtraDamageVar 会把乘法附魔摊进显示值，普通 DynamicVar 不会）。
Console.WriteLine();
Console.WriteLine("=== 第二个计算型伤害的增量变量 ===");

{
    string src = new CodeGenerator(twoCalc).GenerateAll()["Cards/TwoCalc.cs"];

    bool usesSlotExtra = src.Contains("new ModForge.Generated.Runtime.SlotExtraDamageVar(\"ExtraDamage2\", 0.25m)");

    if (!usesSlotExtra)
    {
        Console.WriteLine();
        Console.WriteLine("  [失败] 第二个计算型伤害的增量没走 SlotExtraDamageVar——");
        Console.WriteLine("         描述里引用它时，附魔牌的卡面会与结算值差一截（缺少乘法附魔部分）。");
        Console.WriteLine();
        Console.WriteLine("RESULT: FAILED (second extra damage var lacks enchant preview)");
        return 1;
    }

    Console.WriteLine("  第二个计算型伤害的增量走 SlotExtraDamageVar（附魔预览与原版一致）。");
}

// 两个**固定值**伤害同样各占一个槽（原先会被合并：变量取较大值，小的被顶上去）。
Console.WriteLine();
Console.WriteLine("=== 两个固定值伤害（现在可用）===");

{
    var twinProject = new ModProject { Id = "TwinProbe", Name = "双固定值探针" };
    twinProject.Cards.Add(new CardDef
    {
        Id = "TWIN_STRIKE",
        Name = new LocalizedText("eng", "Twin Strike"),
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
                Amount = 4, Repeat = 2,
                Upgrade = new EffectUpgrade { AmountDelta = 2 },
            },
            new EffectDef
            {
                Kind = EffectKind.DealDamage, Target = EffectTarget.Target,
                Amount = 9,
                Upgrade = new EffectUpgrade { AmountDelta = 3 },
            },
        },
    });

    if (ProjectValidator.Validate(twinProject, null).Any(i => i.Severity == "error"))
    {
        Console.WriteLine("  [失败] 两个固定值伤害被校验器拦下了——它们现在应当是可用的。");
        Console.WriteLine();
        Console.WriteLine("RESULT: FAILED (two fixed values wrongly rejected)");
        return 1;
    }

    string src = new CodeGenerator(twinProject).GenerateAll()["Cards/TwinStrike.cs"];

    // 两个变量：Damage 与 Damage2，各带各的值（4 与 9 都在，不是被顶成一个）。
    bool twoVars = src.Contains("new DamageVar(4m") && src.Contains("\"Damage2\", 9m");

    // 两条各读各的变量。
    bool readsBoth = src.Contains("DynamicVars[\"Damage\"].BaseValue")
        && src.Contains("DynamicVars[\"Damage2\"].BaseValue");

    // 升级各改各的（2 与 3 分别落在两个变量上）。
    bool upgradesBoth = src.Contains("DynamicVars[\"Damage\"].UpgradeValueBy(2m)")
        && src.Contains("DynamicVars[\"Damage2\"].UpgradeValueBy(3m)");

    if (!twoVars || !readsBoth || !upgradesBoth)
    {
        Console.WriteLine();
        Console.WriteLine("  [失败] 两个固定值伤害没有各占一个变量"
            + $"（两个变量={twoVars}，各读各的={readsBoth}，升级分开={upgradesBoth}）。");
        Console.WriteLine();
        Console.WriteLine("RESULT: FAILED (two fixed values share one variable)");
        return 1;
    }

    Console.WriteLine("  两个固定值伤害各占一个变量（4 与 9 都在），升级各改各的。");
}

// 费用升级：界面上能填（"升级时费用变化"），生成器必须真的输出它。
//
// 这原先是个真 bug：`CardDef.UpgradeCostDelta` 在界面上可填、也存进了工程文件，
// 但生成器从来不读它——填了完全没效果，也没有任何提示。原版最常见的升级方式就是它
// （"痛击" -2、"全身撞击" -1），对应 CardEnergyCost.UpgradeBy(addend)。
Console.WriteLine();
Console.WriteLine("=== 升级时费用变化 ===");

{
    var costProject = new ModProject { Id = "CostUpProbe", Name = "费用升级探针" };

    // 只有费用升级、没有任何效果升级：这种更容易漏——若 OnUpgrade 的发出条件只看效果升级，
    // 整张牌连 OnUpgrade 都不会生成，费用永远不变。
    costProject.Cards.Add(new CardDef
    {
        Id = "COST_ONLY",
        Name = new LocalizedText("eng", "Cost Only"),
        Pool = "ironclad",
        Type = CardType.Skill,
        Rarity = CardRarity.Common,
        Target = CardTargetType.Self,
        EnergyCost = 2,
        UpgradeCostDelta = -1,
        Effects =
        {
            new EffectDef { Kind = EffectKind.GainBlock, Target = EffectTarget.Self, Amount = 5 },
        },
    });

    string src = new CodeGenerator(costProject).GenerateAll()["Cards/CostOnly.cs"];

    bool hasOnUpgrade = src.Contains("protected override void OnUpgrade()");
    bool hasCostLine = src.Contains("EnergyCost.UpgradeBy(-1)");

    if (!hasOnUpgrade || !hasCostLine)
    {
        Console.WriteLine();
        Console.WriteLine("  [失败] 费用升级没有生成"
            + $"（OnUpgrade 存在={hasOnUpgrade}，费用那一行={hasCostLine}）。");
        Console.WriteLine("         只有费用升级的牌也必须生成 OnUpgrade，否则升级了费用不变。");
        Console.WriteLine();
        Console.WriteLine("RESULT: FAILED (cost upgrade not emitted)");
        return 1;
    }

    Console.WriteLine("  费用升级输出为 EnergyCost.UpgradeBy(-1)，且只有费用升级时也会生成 OnUpgrade。");
}

// 铸造（储君）：固定与计算型两条路 + 多条铸造各占一个槽。
//
// 原版形状（"淬锋"）是 ForgeVar 声明、OnPlay 调用 ForgeCmd.Forge、升级改变量——
// 这三条都要在。两条铸造（数值不同）各占 Forge / Forge2，升级也各改各的；
// 混着写的话第二条会读第一条的变量，两个铸造会叠加成同一个数。
{
    string apprentice = generated["Cards/SmithsApprentice.cs"];

    bool declaresVars = apprentice.Contains("new ForgeVar(5)")
        && apprentice.Contains("\"Forge2\", 2)");

    // 两条各读各的变量（不是都读 Forge）。
    bool readsBoth = apprentice.Contains("DynamicVars[\"Forge\"].BaseValue")
        && apprentice.Contains("DynamicVars[\"Forge2\"].BaseValue");

    // 两次 ForgeCmd.Forge 都在，且升级各改各的（4 与 1 分别落在两个变量上）。
    int forgeCalls = Regex.Matches(apprentice, @"ForgeCmd\.Forge\(").Count;
    bool upgradesBoth = apprentice.Contains("DynamicVars[\"Forge\"].UpgradeValueBy(4m)")
        && apprentice.Contains("DynamicVars[\"Forge2\"].UpgradeValueBy(1m)");

    if (!declaresVars || !readsBoth || forgeCalls != 2 || !upgradesBoth)
    {
        Console.WriteLine();
        Console.WriteLine("  [失败] 铸造的生成不对"
            + $"（两个变量={declaresVars}，各读各的={readsBoth}，"
            + $"调用次数={forgeCalls}，升级分开={upgradesBoth}）。");
        Console.WriteLine();
        Console.WriteLine("RESULT: FAILED (forge emission is wrong)");
        return 1;
    }

    Console.WriteLine("  铸造声明 ForgeVar、按它调用 ForgeCmd.Forge，两条各占一个槽、升级各改各的。");
}

// 计算型的铸造：走取值方法那条路（铸造没有游戏自带的计算变量），
// 但参数必须落在 ForgeCmd.Forge 上——写错入口的话，铸的就不是君王之剑了。
{
    string living = generated["Cards/LivingForge.cs"];

    bool calcMethod = Regex.IsMatch(living, @"ForgeCmd\.Forge\(CalcAmount\d+\(cardPlay\.Target\)");
    bool upgradesField = living.Contains("_calcPerUnit0 += 1m;");

    if (!calcMethod || !upgradesField)
    {
        Console.WriteLine();
        Console.WriteLine("  [失败] 计算型铸造不对"
            + $"（走取值方法={calcMethod}，升级改字段={upgradesField}）。");
        Console.WriteLine();
        Console.WriteLine("RESULT: FAILED (calculated forge is wrong)");
        return 1;
    }

    Console.WriteLine("  计算型铸造走取值方法（数量在结算时算），升级改每单位字段。");
}

// 恢复生命的自制计数器：计数器文件要发、PatchAll 要挂、表达式要调它。
//
// 游戏没有治疗的历史与钩子，这四个来源读的是我们自己的计数器
// （Runtime/HealTracker.cs，Harmony 观察 Creature.HealInternal）。
// 三个环节缺一个都会"永远读 0"——而且编译与运行都不报错，是最隐蔽的那种失效，
// 所以三条都要断言。
{
    string healer = generated["Cards/HealerNeverCloses.cs"];

    bool trackerEmitted = generated.ContainsKey("Runtime/HealTracker.cs");
    bool exprCallsTracker = healer.Contains("HealTracker.HealsThisCombat(")
        && healer.Contains("HealTracker.HealAmountThisTurn(");

    // ModEntry：PatchAll 必须在（计数器的补丁靠它挂载）。
    string modEntry = generated["ModEntry.cs"];
    bool patchAll = modEntry.Contains("PatchAll(typeof(ModEntry).Assembly)");

    // 计数器本体：补丁目标是 HealInternal，口径是"实际恢复量 > 0 才记"。
    string tracker = generated["Runtime/HealTracker.cs"];
    bool patchTarget = tracker.Contains("HarmonyPatch(typeof(Creature), nameof(Creature.HealInternal))");
    bool actualOnly = tracker.Contains("Math.Min(amount, _missingBefore)");

    if (!trackerEmitted || !exprCallsTracker || !patchAll || !patchTarget || !actualOnly)
    {
        Console.WriteLine();
        Console.WriteLine("  [失败] 恢复生命计数器没有接好"
            + $"（计数器发出={trackerEmitted}，表达式调用={exprCallsTracker}，"
            + $"PatchAll={patchAll}，补丁目标={patchTarget}，实际量口径={actualOnly}）。");
        Console.WriteLine("         缺任何一环，四个恢复生命来源都会永远读 0 且无任何报错。");
        Console.WriteLine();
        Console.WriteLine("RESULT: FAILED (heal tracker not wired)");
        return 1;
    }

    Console.WriteLine("  恢复生命计数器已接好：计数器随 mod 发出、PatchAll 挂载、表达式调用它。");
}

// 计算型伤害 + 计算型格挡同在一张牌（TEMPERED_STANCE）：两个槽的基础值不能串。
//
// 这是上面那条探针的"真实卡"版本——探针查的是生成文本，这里查的是**编译进 mod 的那张卡**，
// 两者都要在：探针保证规划对，这里保证规划真的走到了产物里。
{
    string stance = generated["Cards/TemperedStance.cs"];

    // 伤害拿原版名（CalculationBase），格挡拿下一个（CalculationBase2）。
    bool damageKeepsOriginal = stance.Contains("new CalculationBaseVar(3m)")
        && stance.Contains("new CalculatedDamageVar(");
    bool blockUsesSlotVar = Regex.IsMatch(
        stance, @"SlotCalculatedVar\(""CalculatedBlock"", ""CalculationBase2""[^)]*isDamage: false");

    // 两条各读各的：伤害走属性，格挡走索引器 + 转型。
    bool damageReadsProp = stance.Contains("DynamicVars.CalculatedDamage.Calculate(");
    bool blockReadsIndex = stance.Contains("((CalculatedVar)DynamicVars[\"CalculatedBlock\"]).Calculate(");

    // 升级各改各的（伤害 2 → CalculationBase，格挡 2 → CalculationBase2）。
    bool separateUpgrades = stance.Contains("DynamicVars[\"CalculationBase\"].UpgradeValueBy(2m)")
        && stance.Contains("DynamicVars[\"CalculationBase2\"].UpgradeValueBy(2m)");

    if (!damageKeepsOriginal || !blockUsesSlotVar || !damageReadsProp || !blockReadsIndex || !separateUpgrades)
    {
        Console.WriteLine();
        Console.WriteLine("  [失败] 计算型伤害+计算型格挡在同一张牌上串了"
            + $"（伤害占原名={damageKeepsOriginal}，格挡用子类={blockUsesSlotVar}，"
            + $"伤害读属性={damageReadsProp}，格挡读索引={blockReadsIndex}，"
            + $"升级分开={separateUpgrades}）。");
        Console.WriteLine("         格挡若走原版类型，会静默读到伤害的基础值——只在结算时现形。");
        Console.WriteLine();
        Console.WriteLine("RESULT: FAILED (mixed calculated slots collide in compiled card)");
        return 1;
    }

    Console.WriteLine("  计算型伤害+计算型格挡：各占一个基础值，格挡走子类读自己那个。");
}

// 第二个计算型伤害槽**带基础值**（CASCADING_RECKONING）：CalculationBase2 里是真数，
// 升级真的改它，整数与增量也都各自落在自己的变量上。
{
    string cascade = generated["Cards/CascadingReckoning.cs"];

    bool secondBase = cascade.Contains("\"CalculationBase2\", 4m");
    bool secondExtra = cascade.Contains("SlotExtraDamageVar(\"ExtraDamage2\", 1m)");
    bool secondCalcVar = Regex.IsMatch(cascade, @"SlotCalculatedVar\(""CalculatedDamage2"", ""CalculationBase2"", ""ExtraDamage2""");
    bool secondReads = cascade.Contains("((CalculatedVar)DynamicVars[\"CalculatedDamage2\"]).Calculate(");

    // 升级：基础值 2 进 CalculationBase2、增量 0.5 进 ExtraDamage2（第一条的 1/1 进各自原名）。
    bool upgrades = cascade.Contains("DynamicVars[\"CalculationBase2\"].UpgradeValueBy(2m)")
        && cascade.Contains("DynamicVars[\"ExtraDamage2\"].UpgradeValueBy(0.5m)");

    if (!secondBase || !secondExtra || !secondCalcVar || !secondReads || !upgrades)
    {
        Console.WriteLine();
        Console.WriteLine("  [失败] 带基础值的第二个计算型伤害没配齐"
            + $"（第二基础值={secondBase}，第二增量={secondExtra}，"
            + $"第二计算值={secondCalcVar}，第二条读数={secondReads}，升级={upgrades}）。");
        Console.WriteLine();
        Console.WriteLine("RESULT: FAILED (second calculated damage slot incomplete)");
        return 1;
    }

    Console.WriteLine("  第二个计算型伤害带自己的基础值/增量，升级各改各的。");
}

// 伤害增益作用于多伤害槽的牌：必须加**每一个**伤害槽，不是只加第一个。
//
// 目标牌是运行时挑的，生成代码不知道它有几个槽，所以走运行时辅助 DamageSlots.AddToAll
// （按命名约定扫 Damage / Damage2 / …）。原来的写法只 `DynamicVars["Damage"]`，
// 另一条伤害会漏掉——而 README 一直承诺"每个伤害槽都加"，两处必须一致。
{
    string disciple = generated["Cards/SharpenDisciple.cs"];
    string claw = generated["Cards/ClawDoctrine.cs"];

    bool combatUsesHelper = disciple.Contains("ModForge.Generated.Runtime.DamageSlots.AddToAll(");
    bool permanentUsesHelper = claw.Contains("ModForge.Generated.Runtime.DamageSlots.AddToAll(")
        || generated["Cards/ClawDoctrine.cs"].Contains("PermanentDamageBoost.Apply(");

    bool helperEmitted = generated.ContainsKey("Runtime/DamageSlots.cs");

    // 辅助本体：只认固定值伤害槽——必须以名字判断，且拒绝非数字后缀。
    string helper = generated.ContainsKey("Runtime/DamageSlots.cs") ? generated["Runtime/DamageSlots.cs"] : string.Empty;
    bool helperScansSlots = helper.Contains("IsDamageSlot(") && helper.Contains("name[i] < '0' || name[i] > '9'");

    if (!combatUsesHelper || !helperEmitted || !helperScansSlots)
    {
        Console.WriteLine();
        Console.WriteLine("  [失败] 伤害增益没有覆盖每个伤害槽"
            + $"（本场战斗走辅助={combatUsesHelper}，辅助发出={helperEmitted}，"
            + $"辅助按名扫描={helperScansSlots}）。");
        Console.WriteLine("         只加第一个槽的话，同牌的第二条伤害会漏掉。");
        Console.WriteLine();
        Console.WriteLine("RESULT: FAILED (damage boost only touches the first slot)");
        return 1;
    }

    Console.WriteLine("  伤害增益走 DamageSlots.AddToAll，目标牌的每个伤害槽都会加上（永久那条同样）。");
}

// 多次升级（等级表）：MaxUpgradeLevel 要发、每级要**绝对赋值**、每级换算式要按等级分支。
//
// 增量模型表达不了"每级加得不一样"（第 1 级 +3、第 2 级 +4）与"每级换算式来源"，
// 这两件事正是等级表存在的理由，所以两件都要断言到。绝对值与增量的差别是本质的：
// 写成 UpgradeValueBy 的话，第 2 级会在第 1 级的基础上再加一次，数值就不对了。
{
    string fury = generated["Cards/AscendingFury.cs"];

    bool maxLevel = fury.Contains("public override int MaxUpgradeLevel => 2;");

    // 每一级把该级该有的数**直接写上去**（不是加增量）。
    bool level1 = fury.Contains("DynamicVars[\"CalculationBase\"].BaseValue = 9m;")
        && fury.Contains("DynamicVars[\"ExtraDamage\"].BaseValue = 4m;");

    bool level2 = fury.Contains("DynamicVars[\"CalculationBase\"].BaseValue = 13m;")
        && fury.Contains("DynamicVars[\"ExtraDamage\"].BaseValue = 1m;");

    // 不能出现增量写法——那会在上一级的基础上再叠一次。
    bool noDelta = !fury.Contains("UpgradeValueBy");

    // switch 按 CurrentUpgradeLevel 分派。
    bool switchOn = fury.Contains("switch (CurrentUpgradeLevel)");

    // 每级换算式：第 2 级起按弃牌堆、否则按手牌数，两者都在同一个乘数方法里。
    //
    // 判据只看"等级分派 + 两个来源各自出现"：取值的具体写法由变量来源模块决定
    // （SourceEmitter，形如 player.PlayerCombatState!.DiscardPile.Cards.Count），锁死整句会让
    // 取值实现一改测试就红。等级那行同理——生成的是 `int level = ...` 再用它比较。
    bool branched = fury.Contains("int level = card.CurrentUpgradeLevel;")
        && fury.Contains("if (level >= 2)")
        && fury.Contains("DiscardPile.Cards.Count")
        && fury.Contains("Hand.Cards.Count");

    if (!branched)
    {
        // 把实际生成的那一段打出来，名字对不上时能直接看出差在哪。
        Match m = Regex.Match(fury, @"private static decimal DamageMultiplier\([\s\S]{0,900}?\n    \}");

        if (m.Success)
        {
            Console.WriteLine();
            Console.WriteLine("  --- 生成出来的乘数方法 ---");
            Console.WriteLine(m.Value);
        }
    }

    if (!maxLevel || !level1 || !level2 || !noDelta || !switchOn || !branched)
    {
        Console.WriteLine();
        Console.WriteLine("  [失败] 多次升级（等级表）没生成对"
            + $"（上限={maxLevel}，第1级={level1}，第2级={level2}，"
            + $"没有增量写法={noDelta}，按等级分派={switchOn}，算式分支={branched}）。");
        Console.WriteLine("         写成增量的话第 2 级会在第 1 级基础上再叠一次，数值就不对了。");
        Console.WriteLine();
        Console.WriteLine("RESULT: FAILED (upgrade level table not emitted correctly)");
        return 1;
    }

    Console.WriteLine("  多次升级：MaxUpgradeLevel=2、每级绝对赋值、算式按等级分支（第 2 级改按弃牌堆）。");
}

// 等级表的边界：填了等级表但只能升一次、说了能升多次却没有等级表 —— 两种都要有提示，
// 否则作者以为配好了、实际第 2 级什么都不会发生。
{
    string furySummary = DescriptionPlaceholders.Suggest(project, project.Cards.Last(c => c.Id == "ASCENDING_FURY"), "zhs");

    // 描述仍引用**第 0 级**的变量（升级后由变量自己变），不写死数字。
    bool summaryOk = furySummary.Contains("{CalculationBase:diff()}");

    var warnProject = new ModProject { Id = "LevelWarnProbe", Name = "等级表提示探针" };
    warnProject.Cards.Add(new CardDef
    {
        Id = "LEVEL_ORPHAN",
        Name = new LocalizedText("eng", "Level Orphan"),
        Pool = "ironclad",
        Type = CardType.Attack,
        Rarity = CardRarity.Common,
        Target = CardTargetType.AnyEnemy,
        EnergyCost = 1,

        // 只能升一次，却给了等级表：第 2 项永远读不到。
        MaxUpgradeLevel = 1,
        Effects =
        {
            new EffectDef
            {
                Kind = EffectKind.DealDamage, Target = EffectTarget.Target, Amount = 5,
                Levels = new List<EffectLevel?> { new EffectLevel { Amount = 8 }, new EffectLevel { Amount = 12 } },
            },
        },
    });

    warnProject.Cards.Add(new CardDef
    {
        Id = "LEVEL_MISSING",
        Name = new LocalizedText("eng", "Level Missing"),
        Pool = "ironclad",
        Type = CardType.Attack,
        Rarity = CardRarity.Common,
        Target = CardTargetType.AnyEnemy,
        EnergyCost = 1,

        // 能升 3 次，却一条等级表都没有：第 2、3 级什么都不会发生。
        MaxUpgradeLevel = 3,
        Effects =
        {
            new EffectDef { Kind = EffectKind.DealDamage, Target = EffectTarget.Target, Amount = 5 },
        },
    });

    IReadOnlyList<ProjectIssue> levelIssues = ProjectValidator.Validate(warnProject, null);
    bool orphanWarned = levelIssues.Any(i => i.Message.Contains("only the first entry is ever used"));
    bool missingWarned = levelIssues.Any(i => i.Message.Contains("the 2nd upgrade would change nothing"));

    if (!summaryOk || !orphanWarned || !missingWarned)
    {
        Console.WriteLine();
        Console.WriteLine("  [失败] 等级表的边界情况没提示"
            + $"（描述引用变量={summaryOk}，等级表用不上={orphanWarned}，缺等级表={missingWarned}）。");
        Console.WriteLine();
        Console.WriteLine("RESULT: FAILED (upgrade level edge cases not reported)");
        return 1;
    }

    Console.WriteLine("  等级表的两种坏配置都有提示（用不上的等级表、说了多级却没表）。");
}

// "自动打出"是一个效果、两个分支（作用对象决定"打出牌堆里的牌"还是"这张牌自己"）。
//
// 早先它是两个独立的效果种类，选牌器统一后合并成一个。合并后两条路必须都还走得通，
// 而且走的是**不同的生成方式**：
//   牌堆里的牌 —— 普通的打出时语句（CardCmd.AutoPlay(…, autoPlaying, …)）
//   这张牌自己 —— 引擎专门的自动打出手时机（AfterAutoPrePlayPhaseEnteredEarly），
//                否则会变成"打出时又打出自己"，无限递归
// 所以两种都在编译进 mod 的真实卡里断言，避免哪天合并把其中一条弄丢。
{
    string recite = generated["Cards/ReciteFromMemory.cs"];
    string resurface = generated["Cards/Resurface.cs"];

    // 打出牌堆里的牌：走打出时循环，参数是被取出的那张牌（不是 this）。
    bool pileBranch = recite.Contains("CardCmd.AutoPlay(") && recite.Contains("autoPlaying");

    // 打出这张牌自己：只出现在引擎的自动打出相位钩子里，参数是 this。
    bool selfBranch = resurface.Contains("AfterAutoPrePlayPhaseEnteredEarly")
        && Regex.IsMatch(resurface, @"CardCmd\.AutoPlay\([^;]*\bthis\b");

    // 这张牌自己**不该**在普通的 OnPlay 里自动打出（那会递归）。
    bool noSelfInOnPlay = !Regex.IsMatch(
        resurface,
        @"OnPlay\([\s\S]*?CardCmd\.AutoPlay\(choiceContext,\s*\(CardModel\)\(object\)this");

    // "重复打出"：牌堆查询必须在重复循环**外面**，否则每轮会重新选一次牌堆
    // （那是"重复整条效果"，不是"重复打出"）。
    string repeated = generated["Cards/RepeatedRecital.cs"];

    int pileLookup = repeated.IndexOf("autoPlayPile =");
    int repeatLoop = repeated.IndexOf("for (int r = 0;");
    bool lookupBeforeLoop = pileLookup >= 0 && repeatLoop >= 0 && pileLookup < repeatLoop;

    // 循环里只重复"打几张"，不再查牌堆。
    bool noPileInLoop = !Regex.IsMatch(repeated, @"for \(int r = 0;[\s\S]*?GetPile\(");

    if (!pileBranch || !selfBranch || !noSelfInOnPlay || !lookupBeforeLoop || !noPileInLoop)
    {
        Console.WriteLine();
        Console.WriteLine("  [失败] \"自动打出\"有问题"
            + $"（打出牌堆里的牌={pileBranch}，打出这张牌自己={selfBranch}，"
            + $"自己那条没混进 OnPlay={noSelfInOnPlay}，"
            + $"牌堆查询在重复循环之前={lookupBeforeLoop}，循环里没有再选牌={noPileInLoop}）。");
        Console.WriteLine();
        Console.WriteLine("RESULT: FAILED (auto-play repeat or branch is wrong)");
        return 1;
    }

    Console.WriteLine("  自动打出：两条分支都通，且重复的是\"打出\"、牌堆只选一次。");
}

// "自动打出 → 此牌"的重复：「若在消耗堆中，将此牌重复打出 2 次」。
//
// 关键是**牌堆条件只判一次**：牌堆判断在循环外面，循环体里只有打出这一句。
// 反过来的话（每轮重判牌堆）打完第一张牌就离开消耗堆了，第二次永远不触发。
{
    string echo = generated["Cards/EchoingPrayer.cs"];

    int guard = echo.IndexOf("Pile?.Type ==");
    int loop = echo.IndexOf("for (int i = 0;");

    bool guardBeforeLoop = guard >= 0 && loop >= 0 && guard < loop;

    // 循环体里不该再有牌堆判断。
    bool guardOnlyOnce = Regex.Matches(echo, @"Pile\?\.Type ==").Count == 1;

    // 循环里是"打出此牌"。
    bool playsThis = Regex.IsMatch(echo, @"for \(int i = 0;[\s\S]{0,200}?CardCmd\.AutoPlay\([^;]*\bthis\b");

    if (!guardBeforeLoop || !guardOnlyOnce || !playsThis)
    {
        Console.WriteLine();
        Console.WriteLine("  [失败] \"自动打出 → 此牌\"的重复不对"
            + $"（牌堆判断在循环前={guardBeforeLoop}，只判一次={guardOnlyOnce}，"
            + $"循环里打出此牌={playsThis}）。");
        Console.WriteLine("         每轮重判牌堆的话，第二次会因已离开该牌堆而不触发。");
        Console.WriteLine();
        Console.WriteLine("RESULT: FAILED (self auto-play repeat is wrong)");
        return 1;
    }

    Console.WriteLine("  \"自动打出 → 此牌\"的重复：牌堆判断只一次，循环里重复打出。");
}

// "在牌堆间移动牌"合并了"将此牌置于牌堆"：作用对象 = 此牌 时移动的是这一个实例，
// 且目的地带顶/底（原版"深挖"把它放回抽牌堆顶）。
{
    string healer = generated["Cards/GluttonousBlow.cs"];   // 占位：真正断言在下面
    _ = healer;

    // 直接用探针工程验证两种作用对象生成出的目标牌堆不同。
    var moveProbe = new ModProject { Id = "MoveProbe", Name = "移动探针" };
    moveProbe.Cards.Add(new CardDef
    {
        Id = "MOVE_SELF",
        Name = new LocalizedText("eng", "Move Self"),
        Pool = "ironclad", Type = CardType.Skill, Rarity = CardRarity.Common,
        Target = CardTargetType.Self, EnergyCost = 1,
        Effects =
        {
            new EffectDef
            {
                Kind = EffectKind.MoveCardsBetweenPiles, Target = EffectTarget.Self,
                Pick = CardPick.This(), TargetPile = PileKind.DrawPile, Placement = PilePlacement.Top,
            },
        },
    });

    string src = new CodeGenerator(moveProbe).GenerateAll()["Cards/MoveSelf.cs"];

    // 移动的是这一个实例，目的地是抽牌堆。
    bool movesSelf = src.Contains("new List<CardModel> { this }")
        && Regex.IsMatch(src, @"CardPileCmd\.Add\(target, PileType\.Draw, CardPilePosition\.Top\)");

    if (!movesSelf)
    {
        Console.WriteLine();
        Console.WriteLine("  [失败] \"移动牌 → 此牌\"没有生成出正确的调用（应为 把 this 加到抽牌堆顶）。");
        Console.WriteLine();
        Console.WriteLine("RESULT: FAILED (move-this-card merge is wrong)");
        return 1;
    }

    Console.WriteLine("  \"移动牌 → 此牌\"生成 把 this 加到抽牌堆顶（原\"将此牌置于牌堆\"的能力已并入）。");
}

// 复制牌并加入牌堆：作用对象决定"复制谁"，amount 是**每张**几份。
//
// 三个要点：
//   1. 副本是新生成的牌，走 AddGeneratedCardToCombat（写战斗历史），不是普通 Add；
//   2. 目的地是 TargetPile（不是 Pile——后者在"作用对象 = 牌堆"时是**来源**）；
//   3. 两层循环：外层遍历挑到的牌，内层复制 N 份。
{
    string twinned = generated["Cards/TwinnedEcho.cs"];
    string mirrored = generated["Cards/MirroredDraw.cs"];

    // "复制此牌"：挑出来的就是这一个实例，目的地手牌。
    bool copiesSelf = twinned.Contains("new List<CardModel> { this }")
        && Regex.IsMatch(twinned, @"AddGeneratedCardToCombat\(original\.CreateClone\(\), PileType\.Hand");

    // "复制某个牌堆里的牌"：来源是手牌，目的地是抽牌堆，且用了选牌界面。
    bool copiesPicked = mirrored.Contains("PileType.Hand.GetPile(")
        && Regex.IsMatch(mirrored, @"AddGeneratedCardToCombat\(original\.CreateClone\(\), PileType\.Draw");

    // 两层循环（每张 × 每张几份）。
    bool twoLoops = Regex.IsMatch(twinned, @"foreach \(var original in pickedCards\)[\s\S]{0,120}?for \(int n = 0;");

    // 用的是生成牌入口，不是普通的 Add。
    bool generatedEntry = twinned.Contains("AddGeneratedCardToCombat")
        && !Regex.IsMatch(twinned, @"[^d]CardPileCmd\.Add\(original");

    if (!copiesSelf || !copiesPicked || !twoLoops || !generatedEntry)
    {
        Console.WriteLine();
        Console.WriteLine("  [失败] \"复制牌并加入牌堆\"生成不对"
            + $"（复制此牌={copiesSelf}，复制选中的牌={copiesPicked}，"
            + $"两层循环={twoLoops}，走生成牌入口={generatedEntry}）。");
        Console.WriteLine();
        Console.WriteLine("RESULT: FAILED (copy cards emission is wrong)");
        return 1;
    }

    Console.WriteLine("  复制牌并加入牌堆：作用对象决定复制谁，目的地走 TargetPile，副本走生成牌入口。");
}

// 附魔：层数是变量、挑牌用 CanEnchant、同种附魔加层数。
//
// 三件事缺一不可，而且都不显眼：
//   1. 层数走 DynamicVars["Enchant"]（可升级/可计算），不是写死 1；
//   2. 挑牌用游戏自己的 CanEnchant（不自己写适用范围），但已带同种附魔的牌要留下；
//   3. 已有同种附魔直接 Amount += —— 交给 CardCmd.Enchant 会抛异常（CanEnchant 那里先挂了）。
{
    string edge = generated["Cards/SharpenEdge.cs"];

    // 层数是变量，且升级改它。
    bool layersVar = edge.Contains("DynamicVars[\"Enchant\"]")
        && edge.Contains("DynamicVars[\"Enchant\"].UpgradeValueBy(1m)");

    // 挑牌用 API 判定；例外只在"该附魔有层数（ShowAmount）且已带同种附魔"时放行。
    //
    // 不能改成判 IsStackable：CanEnchant 对"同种且可叠加"本来就放行，用它做例外等于没加；
    // 而原版附魔全都没覆写 IsStackable，那样写会让例外永不成立（功能等于关掉）。
    bool apiFilter = edge.Contains("ModelDb.Enchantment<") && edge.Contains(".CanEnchant(")
        && edge.Contains(".ShowAmount")
        && Regex.IsMatch(edge, @"ShowAmount.*c\.Enchantment is ");

    // 同种附魔走加层数，而不是再调一次 CardCmd.Enchant。
    // 生成形如 `existing.Amount += (int)DynamicVars["Enchant"].BaseValue;`；
    // 判据只看"给现有的 Amount 加 DynamicVars 里的层数"这个形状，不锁死整句。
    bool addsLayers = Regex.IsMatch(edge, @"\.Amount \+= \(int\)\(DynamicVars\[""Enchant""\]")
        && edge.Contains("else")
        && edge.Contains("CardCmd.Enchant<");

    // 数值不再被当成"附几张牌"（不该有 Take）。
    bool noTake = !Regex.IsMatch(edge, @"pickedCards\.Take\(");

    if (!layersVar || !apiFilter || !addsLayers || !noTake)
    {
        Console.WriteLine();
        Console.WriteLine("  [失败] 附魔生成不对"
            + $"（层数走变量={layersVar}，用 CanEnchant 过滤={apiFilter}，"
            + $"同种加层数={addsLayers}，不再截断张数={noTake}）。");
        Console.WriteLine();
        Console.WriteLine("RESULT: FAILED (enchant emission is wrong)");
        return 1;
    }

    Console.WriteLine("  附魔：层数走变量、挑牌用 CanEnchant、同种附魔加层数（不再当成张数）。");
}

// 重放的两条路：卡牌自己的属性、以及给**别的牌**加重放的效果（直接改目标牌）。
//
// 两者是不同的东西，所以分别断言：
//   卡牌属性 —— 每张牌都带一条 [SavedProperty] ReplayCount（默认 0），setter 写进
//               BaseReplayCount；标准值由 AfterCreated **赋值**（不是累加：读档/重建会再跑）。
//   效果     —— 本场战斗直接 `BaseReplayCount += N`；永久加在目标的 ReplayCount 上。
{
    string innate = generated["Cards/DoubleTapStrike.cs"];
    string blessing = generated["Cards/EchoBlessing.cs"];

    // 卡牌属性：每张牌都有那条自有属性，默认 0（这张牌的重放数是 1，所以标准值是 1）。
    bool savedProperty = innate.Contains("[SavedProperty]")
        && Regex.IsMatch(innate, @"public int ReplayCount[\s\S]{0,200}?BaseReplayCount = value;");

    // 标准值是**赋值**（`ReplayCount = 1;`），不能是累加。
    //
    // 判据只能看 AfterCreated 那一段，而且要认"还没被赋过值时才给"这个守卫：
    // 读档不经过 AfterCreated，但战斗内的副本是克隆出来的，无脑覆盖会把带回的值抹掉。
    bool innateInAfterCreated = Regex.IsMatch(
        innate, @"public override void AfterCreated\(\)[\s\S]{0,300}?if \(_replayCount == 0\)[\s\S]{0,120}?ReplayCount = 1;");

    bool innateNotAccumulating = !Regex.IsMatch(
        innate, @"public override void AfterCreated\(\)[\s\S]{0,300}?BaseReplayCount \+=");

    // 效果：本场战斗直接加在目标牌上，且用"重放"变量算出来的值。
    bool effectAdds = Regex.IsMatch(blessing, @"\.BaseReplayCount \+= replay\d+;");

    // 效果版不该给本卡自己设标准重放（那是卡牌属性那条路）。
    bool effectNoAfterCreated = !blessing.Contains("public override void AfterCreated()");

    // 永久版：走运行时 Apply，加在目标的 ReplayCount 上；且**不**覆写 AfterDowngraded
    // （重放最终写在普通字段上，升级/降级不重建它，补差值会多加）。
    string eternal = generated["Cards/EternalEcho.cs"];
    bool permanentApply = eternal.Contains("Runtime.ReplayBoost.Apply(");
    bool permanentToOwnProperty = Regex.IsMatch(eternal, @"public void ApplyPermanentReplay\(int extra\)[\s\S]{0,200}?ReplayCount \+= extra;");
    // 只有**发出的方法**算数：注释里也会出现 AfterDowngraded 这个词，不能按文本搜。
    bool noDowngradePatch = !Regex.IsMatch(eternal, @"protected override void AfterDowngraded");

    if (!savedProperty || !innateInAfterCreated || !innateNotAccumulating || !effectAdds
        || !effectNoAfterCreated || !permanentApply || !permanentToOwnProperty || !noDowngradePatch)
    {
        Console.WriteLine();
        Console.WriteLine("  [失败] 重放的两条路没分清"
            + $"（自有属性={savedProperty}，标准值在 AfterCreated 赋值={innateInAfterCreated}，"
            + $"没有误用累加={innateNotAccumulating}，效果改目标牌={effectAdds}，"
            + $"效果不碰自己={effectNoAfterCreated}，永久走 Apply={permanentApply}，"
            + $"永久加在自有属性上={permanentToOwnProperty}，不该补 AfterDowngraded={noDowngradePatch}）。");
        Console.WriteLine();
        Console.WriteLine("RESULT: FAILED (replay card-property vs effect mixed up)");
        return 1;
    }

    Console.WriteLine("  重放两条路都在：自有存档属性 + 给别的牌加（效果改目标）。");
}

// ---- 加入指定卡牌（从卡池 N 选一）的校验器三关 ----
// 游戏那条选牌界面候选 >3 直接抛异常，所以要在编辑器里拦；候选 ≥2 时数值不参与、
// 指定牌那一路没填牌名，都该有话说。
{
    ModProject PoolProbe(string id, Action<EffectDef> tweak)
    {
        var p = new ModProject { Id = id, Name = id };

        var effect = new EffectDef
        {
            Kind = EffectKind.AddNamedCardToPile, Target = EffectTarget.Self,
            AddMode = AddCardMode.ChooseFromPool, Candidates = 3, Amount = 1,
            TargetPile = PileKind.Hand,
        };
        tweak(effect);
        p.Cards.Add(new CardDef
        {
            Id = "POOL_CARD", Name = new LocalizedText("eng", "Pool Card"), Pool = "ironclad",
            Type = CardType.Skill, Rarity = CardRarity.Common, Target = CardTargetType.Self,
            EnergyCost = 1, Effects = { effect },
        });

        return p;
    }

    bool caughtTooMany = ProjectValidator.Validate(PoolProbe("PoolProbe1", e => e.Candidates = 4), null)
        .Any(i => i.Severity == "error" && i.Message.Contains("1-3"));
    bool caughtTooFew = ProjectValidator.Validate(PoolProbe("PoolProbe2", e => e.Candidates = 0), null)
        .Any(i => i.Severity == "error" && i.Message.Contains("1-3"));
    bool warnedValueUnused = ProjectValidator.Validate(PoolProbe("PoolProbe3", e => e.Amount = 5), null)
        .Any(i => i.Severity == "warn" && i.Message.Contains("value is not used"));
    bool quietWhenRight = !ProjectValidator.Validate(PoolProbe("PoolProbe4", _ => { }), null)
        .Any(i => i.Message.Contains("candidates") || i.Message.Contains("value is not used"));

    var namedProbe = new ModProject { Id = "PoolProbe5", Name = "NamedProbe" };
    namedProbe.Cards.Add(new CardDef
    {
        Id = "NAMED_NO_CARD", Name = new LocalizedText("eng", "Named No Card"), Pool = "ironclad",
        Type = CardType.Skill, Rarity = CardRarity.Common, Target = CardTargetType.Self,
        EnergyCost = 1,
        Effects =
        {
            // 指定牌那一路（默认 Named）却没填牌名：生成出来是一行注释，什么都不做。
            new EffectDef { Kind = EffectKind.AddNamedCardToPile, Target = EffectTarget.Self },
        },
    });
    bool warnedNoCard = ProjectValidator.Validate(namedProbe, null)
        .Any(i => i.Severity == "warn" && i.Message.Contains("none is chosen"));

    if (!caughtTooMany || !caughtTooFew || !warnedValueUnused || !quietWhenRight || !warnedNoCard)
    {
        Console.WriteLine();
        Console.WriteLine("  [失败] 生成牌的校验没拦住（候选过多={caughtTooMany}，候选过少={caughtTooFew}，"
            + $"数值不参与提醒={warnedValueUnused}，合法配置误报={quietWhenRight}，缺牌名提醒={warnedNoCard}）。");
        Console.WriteLine();
        Console.WriteLine("RESULT: FAILED (pool-mode validation is wrong)");
        return 1;
    }

    Console.WriteLine("  从卡池 N 选一：候选数越界拦截、数值不参与提醒、缺牌名提醒都成立。");
}

// ---- 启用条件的校验器 ----
// 两侧全固定时条件成立与否静态可知：恒不成立是 error（效果永远不跑），恒成立是 warn
// （条件是摆设）；系数升级在没有来源时"填了不生效"；合法组合不报任何条件类问题。
{
    ModProject CondProbe(EffectCondition condition)
    {
        var p = new ModProject { Id = "CondProbe", Name = "条件探针" };
        p.Cards.Add(new CardDef
        {
            Id = "COND_CARD", Name = new LocalizedText("eng", "Cond Card"), Pool = "ironclad",
            Type = CardType.Skill, Rarity = CardRarity.Common, Target = CardTargetType.Self,
            EnergyCost = 1,
            Effects =
            {
                new EffectDef
                {
                    Kind = EffectKind.DrawCards, Target = EffectTarget.Self, Amount = 1,
                    Condition = condition,
                },
            },
        });

        return p;
    }

    bool caughtNever = ProjectValidator.Validate(
        CondProbe(new EffectCondition
        {
            Comparison = ConditionComparison.AtLeast,
            LeftAmount = 0,
            RightAmount = 3,
        }), null)
        .Any(i => i.Severity == "error" && i.Message.Contains("never true"));

    bool caughtAlways = ProjectValidator.Validate(
        CondProbe(new EffectCondition
        {
            Comparison = ConditionComparison.AtLeast,
            LeftAmount = 3,
            RightAmount = 1,
        }), null)
        .Any(i => i.Severity == "warn" && i.Message.Contains("always true"));

    bool caughtPerUnitUpgrade = ProjectValidator.Validate(
        CondProbe(new EffectCondition
        {
            Comparison = ConditionComparison.AtLeast,
            LeftAmount = 0,
            RightAmount = 3,
            LeftPerUnitUpgrade = 1m,
        }), null)
        .Any(i => i.Severity == "warn" && i.Message.Contains("coefficient upgrade does nothing"));

    bool quietWhenLegal = !ProjectValidator.Validate(
        CondProbe(new EffectCondition
        {
            Comparison = ConditionComparison.AtLeast,
            LeftSource = new ValueSource { Actor = ValueSourceActor.Self, Kind = ValueSourceKind.HandCount },
            RightAmount = 3,
            LeftUpgrade = 1,
        }), null)
        .Any(i => i.Message.Contains("condition"));

    if (!caughtNever || !caughtAlways || !caughtPerUnitUpgrade || !quietWhenLegal)
    {
        Console.WriteLine();
        Console.WriteLine("  [失败] 条件的校验没拦住（恒不成立={caughtNever}，恒成立={caughtAlways}，"
            + $"无来源系数升级={caughtPerUnitUpgrade}，合法组合误报={quietWhenLegal}）。");
        Console.WriteLine();
        Console.WriteLine("RESULT: FAILED (condition validation is wrong)");
        return 1;
    }

    Console.WriteLine("  启用条件：恒不成立拦截、恒成立提醒、无来源系数升级提醒都成立。");
}

Console.WriteLine();
Console.WriteLine("RESULT: OK");
return 0;

/// <summary>
/// 每个 DynamicVar 子类"隐含"的变量名（不带字符串参数构造时用的那个）。
///
/// 从游戏程序集里读：每个类都有一条 <c>: base("Name", …)</c> 调用，取它的第一个字面量参数。
/// 不靠猜类名，是因为有的类名与变量名并不一致（OstyDamageVar → "OstyDamage" 还好，
/// 但若哪天出现不一致就会误报）。
/// </summary>
static Dictionary<string, string> DynamicVarNames(string gameDir)
{
    var result = new Dictionary<string, string>(StringComparer.Ordinal);

    string dll = Path.Combine(gameDir, "data_sts2_windows_x86_64", "sts2.dll");

    using FileStream fs = File.OpenRead(dll);
    using var pe = new PEReader(fs);
    MetadataReader reader = pe.GetMetadataReader();

    foreach (TypeDefinitionHandle handle in reader.TypeDefinitions)
    {
        TypeDefinition type = reader.GetTypeDefinition(handle);
        string ns = reader.GetString(type.Namespace);

        if (ns != "MegaCrit.Sts2.Core.Localization.DynamicVars")
        {
            continue;
        }

        string typeName = reader.GetString(type.Name);

        // 只认构造函数里 base(...) 的第一个字符串参数。用 IL 里 ldstr 出现的第一个
        // 字面量近似——这些类的构造函数就是一句 base 调用，没有别的字符串。
        foreach (MethodDefinitionHandle methodHandle in type.GetMethods())
        {
            MethodDefinition method = reader.GetMethodDefinition(methodHandle);

            if (reader.GetString(method.Name) != ".ctor")
            {
                continue;
            }

            if (method.RelativeVirtualAddress == 0)
            {
                continue;
            }

            MethodBodyBlock body;

            try
            {
                body = pe.GetMethodBody(method.RelativeVirtualAddress);
            }
            catch (BadImageFormatException)
            {
                continue;
            }

            BlobReader il = body.GetILReader();

            while (il.RemainingBytes > 0)
            {
                int op = il.ReadByte();

                // ldstr 是 0x72，后面跟 4 字节的 user-string token。
                if (op == 0x72 && il.RemainingBytes >= 4)
                {
                    int token = il.ReadInt32();
                    UserStringHandle ush = MetadataTokens.UserStringHandle(token & 0x00FFFFFF);

                    if (!ush.IsNil)
                    {
                        string? text = reader.GetUserString(ush);

                        if (!string.IsNullOrEmpty(text))
                        {
                            result[typeName] = text;
                        }
                    }

                    break;
                }
            }

            if (result.ContainsKey(typeName))
            {
                break;
            }
        }
    }

    return result;
}
