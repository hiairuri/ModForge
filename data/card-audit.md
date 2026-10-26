# 原版卡牌审计（游戏 v0.107.1）

按「牌池 → 稀有度」分组，供分批核对。

每张卡带一个结论，就是这次要回答的问题：

- **可直接实现** —— 现有效果就能照搬
- **实现方式不同** —— 能做，但原版走的是我们表达不了的路径（多半是额外钩子）
- **可能实现错误** —— 能做，但我们的映射有歧义，可能选错语义
- **无法实现** —— 用到了我们完全没有的操作，得先加效果
- **无需实现** —— 废牌 / 纯关键词牌

每张卡列出的信息：**我们识别的效果** / **无法实现**（我们缺的操作）/ 
**额外钩子**（依赖特殊时机）/ **数值来源** / **描述**（原版中文，用来核对识别结果）。

## 批次总表

按此顺序分批核对：无法实现与可能实现错误的卡优先。

| 牌池 | 稀有度 | 张数 | 可直接实现 | 实现方式不同 | 可能实现错误 | 无法实现 | 无需实现 |
|---|---|---:|---:|---:|---:|---:|---:|
| ironclad | basic | 3 | 3 | 0 | 0 | 0 | 0 |
| ironclad | common | 20 | 20 | 0 | 0 | 0 | 0 |
| ironclad | uncommon | 36 | 27 | 9 | 0 | 0 | 0 |
| ironclad | rare | 26 | 18 | 8 | 0 | 0 | 0 |
| ironclad | ancient | 2 | 2 | 0 | 0 | 0 | 0 |
| silent | basic | 4 | 4 | 0 | 0 | 0 | 0 |
| silent | common | 20 | 18 | 2 | 0 | 0 | 0 |
| silent | uncommon | 36 | 23 | 10 | 0 | 3 | 0 |
| silent | rare | 26 | 15 | 10 | 0 | 1 | 0 |
| silent | ancient | 2 | 1 | 1 | 0 | 0 | 0 |
| defect | basic | 4 | 4 | 0 | 0 | 0 | 0 |
| defect | common | 20 | 16 | 2 | 0 | 2 | 0 |
| defect | uncommon | 36 | 22 | 10 | 0 | 4 | 0 |
| defect | rare | 26 | 14 | 9 | 0 | 3 | 0 |
| defect | ancient | 2 | 1 | 1 | 0 | 0 | 0 |
| necrobinder | basic | 4 | 4 | 0 | 0 | 0 | 0 |
| necrobinder | common | 20 | 14 | 4 | 0 | 2 | 0 |
| necrobinder | uncommon | 36 | 22 | 11 | 0 | 3 | 0 |
| necrobinder | rare | 26 | 14 | 9 | 0 | 3 | 0 |
| necrobinder | ancient | 2 | 1 | 1 | 0 | 0 | 0 |
| regent | basic | 4 | 4 | 0 | 0 | 0 | 0 |
| regent | common | 20 | 15 | 5 | 0 | 0 | 0 |
| regent | uncommon | 36 | 24 | 10 | 0 | 2 | 0 |
| regent | rare | 26 | 15 | 9 | 0 | 2 | 0 |
| regent | ancient | 2 | 1 | 1 | 0 | 0 | 0 |
| colorless | uncommon | 39 | 27 | 11 | 0 | 1 | 0 |
| colorless | rare | 25 | 13 | 8 | 0 | 4 | 0 |
| curse | curse | 18 | 3 | 4 | 0 | 2 | 9 |
| status | status | 11 | 5 | 0 | 0 | 2 | 4 |
| event | ancient | 8 | 5 | 2 | 0 | 1 | 0 |
| event | event | 12 | 8 | 4 | 0 | 0 | 0 |
| quest | quest | 3 | 0 | 1 | 0 | 1 | 1 |
| token | token | 10 | 9 | 1 | 0 | 0 | 0 |
| token | status | 4 | 0 | 4 | 0 | 0 | 0 |
| (none) | event | 7 | 5 | 2 | 0 | 0 | 0 |
| (none) | status | 2 | 1 | 0 | 0 | 1 | 0 |

## 我们缺的操作

按卡住的卡数排序。这些是要新增的效果，或者明确放弃的能力。

| 缺的操作 | 卡住的卡数 | 涉及哪些卡 |
|---|---:|---|
| `按战斗历史计数作为数值来源` | 9 | 锻打成型、终结技、群起攻之、螺旋钻击、月面射击、亡魂牵引、辐射、猛晃、电流相生 |
| `CardPileCmd.RemoveFromDeck` | 3 | 弃用卡牌、愧疚、藏宝图 |
| `按充能球数量作为数值来源` | 3 | 弹幕齐射、编译冲击、同步 |
| `CardCmd.ApplyKeyword` | 2 | 雕琢打击、响指 |
| `CreatureCmd.Kill` | 2 | 碎骨、牺牲 |
| `OrbCmd.Passive` | 2 | 漆黑、特斯拉线圈 |
| `PlayerCmd.GainGold` | 2 | 贪婪之手、藏宝图 |
| `PowerCmd.ModifyAmount` | 2 | 狂乱逃离、苦难 |
| `超出范围的按条件数牌` | 2 | 撕碎、灵魂风暴 |
| `BaseReplayCount` | 1 | 未掘宝石 |
| `CardCmd.ApplySingleTurnSly` | 1 | 手上技法 |
| `CardCmd.Enchant` | 1 | 墨之刃 |
| `CardPileCmd.Shuffle` | 1 | 重启 |
| `CreatureCmd.LoseBlock` | 1 | 暴露 |
| `CreatureCmd.Stun` | 1 | 吹哨 |
| `OrbCmd.RemoveSlots` | 1 | 暴涨 |
| `PlayerCmd.CompleteQuest` | 1 | 藏宝图 |
| `PlayerCmd.EndTurn` | 1 | 虚空形态 |
| `PlayerCmd.LoseEnergy` | 1 | 虚空 |
| `PlayerCmd.LoseGold` | 1 | 债务 |
| `PotionCmd.TryToProcure` | 1 | 炼制药水 |
| `PowerCmd.FindExistingInstanceForStacking` | 1 | 苦难 |

## 可能实现错误的映射

这些卡我们能做，但映射有歧义——核对时要逐张确认用的是哪种语义。

---

## 逐张明细

## ironclad（87 张）

### basic（3 张）

#### 打击 `STRIKE_IRONCLAD` — 可直接实现

- 费用 1 · attack · 目标 any_enemy
- 我们识别的效果：DealDamage
- 描述：造成{Damage:diff()}点伤害。

#### 痛击 `BASH` — 可直接实现

- 费用 2 · attack · 目标 any_enemy
- 我们识别的效果：ApplyPower、DealDamage
- 施加的状态：VulnerablePower
- 描述：造成{Damage:diff()}点伤害。 / 给予{VulnerablePower:diff()}层[gold]易伤[/gold]。

#### 防御 `DEFEND_IRONCLAD` — 可直接实现

- 费用 1 · skill · 目标 self
- 我们识别的效果：GainBlock
- 描述：获得{Block:diff()}点[gold]格挡[/gold]。

### common（20 张）

#### 余烬 `CINDER` — 可直接实现

- 费用 2 · attack · 目标 any_enemy
- 我们识别的效果：DealDamage、ExhaustCardsInPile
- 描述：造成{Damage:diff()}点伤害。 / 随机[gold]消耗[/gold]1张牌。

#### 全身撞击 `BODY_SLAM` — 可直接实现

- 费用 1 · attack · 目标 any_enemy
- 我们识别的效果：DealDamage
- 额外钩子：Calculated
- 数值来源：Block
- 描述：造成你当前[gold]格挡[/gold]值的伤害。{InCombat: / （造成{CalculatedDamage:diff()}点伤害）|}

#### 剑柄打击 `POMMEL_STRIKE` — 可直接实现

- 费用 1 · attack · 目标 any_enemy
- 我们识别的效果：DealDamage、DrawCards
- 描述：造成{Damage:diff()}点伤害。 / 抽{Cards:diff()}张牌。

#### 双重打击 `TWIN_STRIKE` — 可直接实现

- 费用 1 · attack · 目标 any_enemy
- 我们识别的效果：DealDamage
- 描述：造成{Damage:diff()}点伤害两次。

#### 坚毅 `TRUE_GRIT` — 可直接实现

- 费用 1 · skill · 目标 self
- 我们识别的效果：ExhaustCardsInPile、GainBlock
- 描述：获得{Block:diff()}点[gold]格挡[/gold]。 / {IfUpgraded:show:| 随机}[gold]消耗[/gold]1张牌。

#### 头槌 `HEADBUTT` — 可直接实现

- 费用 1 · attack · 目标 any_enemy
- 我们识别的效果：DealDamage、MoveCardsBetweenPiles
- 描述：造成{Damage:diff()}点伤害。 / 将你[gold]弃牌堆[/gold]中的一张牌放到[gold]抽牌堆[/gold]顶部。

#### 完美打击 `PERFECTED_STRIKE` — 可直接实现

- 费用 2 · attack · 目标 any_enemy
- 我们识别的效果：DealDamage
- 额外钩子：Calculated
- 描述：造成{CalculatedDamage:diff()}点伤害。 / 你每有一张名字中含有“打击”的牌，伤害+{ExtraDamage:diff()}。

#### 愤怒 `ANGER` — 可直接实现

- 费用 0 · attack · 目标 any_enemy
- 我们识别的效果：AddNamedCardToPile、DealDamage
- 描述：造成{Damage:diff()}点伤害。 / 将一张此牌的复制品加入你的[gold]弃牌堆[/gold]。

#### 战栗 `TREMBLE` — 可直接实现

- 费用 1 · skill · 目标 any_enemy · EXHAUST
- 我们识别的效果：ApplyPower
- 施加的状态：VulnerablePower
- 描述：给予{VulnerablePower:diff()}层[gold]易伤[/gold]。

#### 放血 `BLOODLETTING` — 可直接实现

- 费用 0 · skill · 目标 self
- 我们识别的效果：GainEnergy、LoseHp
- 描述：失去{HpLoss:diff()}点生命。 / 获得{Energy:energyIcons()}。

#### 武装 `ARMAMENTS` — 可直接实现

- 费用 1 · skill · 目标 self
- 我们识别的效果：GainBlock、UpgradeCardsInPile
- 描述：获得{Block:diff()}点[gold]格挡[/gold]。 / [gold]升级[/gold]你[gold]手牌[/gold]中的{IfUpgraded:show:所有牌|一张牌}。

#### 熔融之拳 `MOLTEN_FIST` — 可直接实现

- 费用 1 · attack · 目标 any_enemy · EXHAUST
- 我们识别的效果：ApplyPower、DealDamage
- 施加的状态：VulnerablePower
- 描述：造成{Damage:diff()}点伤害。 / 将该敌人身上的[gold]易伤[/gold]层数翻倍。

#### 破灭 `HAVOC` — 可直接实现

- 费用 1 · skill · 目标 self
- 我们识别的效果：AutoPlay
- 描述：打出[gold]抽牌堆[/gold]顶部的牌并将其[gold]消耗[/gold]。

#### 突破 `BREAKTHROUGH` — 可直接实现

- 费用 1 · attack · 目标 all_enemies
- 我们识别的效果：DealDamage、LoseHp
- 描述：失去{HpLoss:diff()}点生命。 / 对所有敌人造成{Damage:diff()}点伤害。

#### 耸肩无视 `SHRUG_IT_OFF` — 可直接实现

- 费用 1 · skill · 目标 self
- 我们识别的效果：DrawCards、GainBlock
- 描述：获得{Block:diff()}点[gold]格挡[/gold]。 / 抽{Cards:diff()}张牌。

#### 血墙 `BLOOD_WALL` — 可直接实现

- 费用 2 · skill · 目标 self
- 我们识别的效果：GainBlock、LoseHp
- 描述：失去{HpLoss:diff()}点生命。 / 获得{Block:diff()}点[gold]格挡[/gold]。

#### 铁斩波 `IRON_WAVE` — 可直接实现

- 费用 1 · attack · 目标 any_enemy
- 我们识别的效果：DealDamage、GainBlock
- 描述：获得{Block:diff()}点[gold]格挡[/gold]。 / 造成{Damage:diff()}点伤害。

#### 闪电霹雳 `THUNDERCLAP` — 可直接实现

- 费用 1 · attack · 目标 all_enemies
- 我们识别的效果：ApplyPower、DealDamage
- 施加的状态：VulnerablePower
- 描述：对所有敌人造成{Damage:diff()}点伤害，给予{VulnerablePower:diff()}层[gold]易伤[/gold]。

#### 预备打击 `SETUP_STRIKE` — 可直接实现

- 费用 1 · attack · 目标 any_enemy
- 我们识别的效果：ApplyPower、DealDamage
- 施加的状态：SetupStrikePower
- 描述：造成{Damage:diff()}点伤害。 / 在本回合内获得{StrengthPower:diff()}点[gold]力量[/gold]。

#### 飞剑回旋镖 `SWORD_BOOMERANG` — 可直接实现

- 费用 1 · attack · 目标 random_enemy
- 我们识别的效果：DealDamage
- 额外钩子：Repeat
- 描述：随机对敌人造成{Damage:diff()}点伤害{Repeat:diff()}次。

### uncommon（36 张）

#### 上勾拳 `UPPERCUT` — 可直接实现

- 费用 2 · attack · 目标 any_enemy
- 我们识别的效果：ApplyPower、DealDamage
- 施加的状态：VulnerablePower、WeakPower
- 描述：造成{Damage:diff()}点伤害。 / 给予{Power:diff()}层[gold]虚弱[/gold]。 / 给予{Power:diff()}层[gold]易伤[/gold]。

#### 与我一战！ `FIGHT_ME` — 可直接实现

- 费用 2 · attack · 目标 any_enemy
- 我们识别的效果：ApplyPower、DealDamage
- 施加的状态：StrengthPower
- 额外钩子：Repeat
- 描述：造成{Damage:diff()}点伤害两次。 / 获得{StrengthPower:diff()}点[gold]力量[/gold]。 / 该敌人获得{EnemyStrength:diff()}点[gold]力量[/gold]。

#### 主宰 `DOMINATE` — 可直接实现

- 费用 1 · skill · 目标 any_enemy · EXHAUST
- 我们识别的效果：ApplyPower
- 施加的状态：StrengthPower、VulnerablePower
- 描述：给予{VulnerablePower:diff()}层[gold]易伤[/gold]。 / 敌人身上每有一层[gold]易伤[/gold]，就获得{StrengthPerVulnerable:diff()}点[gold]力量[/gold]。

#### 凶恶 `VICIOUS` — 实现方式不同

- 费用 1 · power · 目标 self
- 我们识别的效果：ApplyPower、DrawCards
- 施加的状态：ViciousPower
- 额外钩子：AfterPowerAmountChanged（来自状态 ViciousPower）
- 描述：每当你给予[gold]易伤[/gold]时，抽{Cards:diff()}张牌。

#### 劫掠 `PILLAGE` — 可直接实现

- 费用 1 · attack · 目标 any_enemy
- 我们识别的效果：DealDamage、DrawCards
- 描述：造成{Damage:diff()}点伤害。 / 抽牌直到你抽到一张非攻击牌。

#### 地狱之刃 `INFERNAL_BLADE` — 可直接实现

- 费用 1 · skill · 目标 self · EXHAUST
- 我们识别的效果：AddNamedCardToPile、SetCardsFree
- 描述：将一张随机攻击牌加入你的[gold]手牌[/gold]。那张牌在本回合内可以免费打出。

#### 岩石铠甲 `STONE_ARMOR` — 实现方式不同

- 费用 1 · power · 目标 self
- 我们识别的效果：ApplyPower
- 施加的状态：PlatingPower
- 额外钩子：AfterApplied（来自状态 PlatingPower）、BeforeSideTurnEndEarly（来自状态 PlatingPower）
- 描述：获得{PlatingPower:diff()}层[gold]覆甲[/gold]。

#### 巨像 `COLOSSUS` — 可直接实现

- 费用 1 · skill · 目标 self
- 我们识别的效果：ApplyPower、GainBlock
- 施加的状态：ColossusPower
- 描述：获得{Block:diff()}点[gold]格挡[/gold]。 / 在本回合中，有[gold]易伤[/gold]状态的敌人对你造成的伤害降低50%。

#### 彼岸咆哮 `HOWL_FROM_BEYOND` — 可直接实现

- 费用 3 · attack · 目标 all_enemies
- 我们识别的效果：AutoPlay、DealDamage
- 额外钩子：AfterAutoPostPlayPhaseEntered
- 描述：对所有敌人造成{Damage:diff()}点伤害。 / 在你的回合结束时，如果这张牌在你的[gold]消耗牌堆[/gold]中，则将其打出。

#### 御血术 `HEMOKINESIS` — 可直接实现

- 费用 1 · attack · 目标 any_enemy
- 我们识别的效果：DealDamage、LoseHp
- 描述：失去{HpLoss:diff()}点生命。 / 造成{Damage:diff()}点伤害。

#### 怨恨 `SPITE` — 可直接实现

- 费用 0 · attack · 目标 any_enemy
- 我们识别的效果：DealDamage
- 额外钩子：Repeat
- 描述：造成{Damage:diff()}点伤害。 / 如果你在本回合失去过生命值， / 则攻击{Repeat:diff()}次。

#### 恶魔护盾 `DEMONIC_SHIELD` — 可直接实现

- 费用 0 · skill · 目标 any_ally · EXHAUST
- 我们识别的效果：GainBlock、LoseHp
- 额外钩子：Calculated
- 数值来源：Block
- 描述：失去{HpLoss:diff()}点生命。 / 给予另一位玩家你当前[gold]格挡[/gold]值的[gold]格挡[/gold]。{InCombat: / （给予{CalculatedBlock:diff()}点[gold]格挡[/gold]）|}

#### 惊逃 `STAMPEDE` — 实现方式不同

- 费用 2 · power · 目标 self
- 我们识别的效果：ApplyPower
- 施加的状态：StampedePower
- 额外钩子：AfterAutoPostPlayPhaseEntered（来自状态 StampedePower）
- 描述：在你的回合结束时，随机打出你[gold]手牌[/gold]中的{Power:diff()}张攻击牌攻击随机敌人。

#### 战斗专注 `BATTLE_TRANCE` — 实现方式不同

- 费用 0 · skill · 目标 self
- 我们识别的效果：ApplyPower、DrawCards
- 施加的状态：NoDrawPower
- 额外钩子：ShouldDraw（来自状态 NoDrawPower）
- 描述：抽{Cards:diff()}张牌。 / 你在本回合内不能再抽任何牌。

#### 战鼓 `DRUM_OF_BATTLE` — 可直接实现

- 费用 1 · skill · 目标 self
- 我们识别的效果：DrawCards、GainEnergy
- 额外钩子：AfterCardExhausted
- 描述：抽{Cards:diff()}张牌。 / 这张牌被[gold]消耗[/gold]时，获得{Energy:energyIcons()}。

#### 拆卸 `DISMANTLE` — 可直接实现

- 费用 1 · attack · 目标 any_enemy
- 我们识别的效果：DealDamage
- 描述：造成{Damage:diff()}点伤害。 / 如果该敌人有[gold]易伤[/gold]状态，则攻击两次。

#### 挑衅 `TAUNT` — 可直接实现

- 费用 1 · skill · 目标 any_enemy
- 我们识别的效果：ApplyPower、GainBlock
- 施加的状态：VulnerablePower
- 描述：获得{Block:diff()}点[gold]格挡[/gold]。 / 给予{VulnerablePower:diff()}层[gold]易伤[/gold]。

#### 撕裂 `RUPTURE` — 实现方式不同

- 费用 1 · power · 目标 self
- 我们识别的效果：ApplyPower
- 施加的状态：RupturePower
- 额外钩子：BeforeCardPlayed（来自状态 RupturePower）
- 描述：每当你在你的回合失去生命值时, 获得{StrengthPower:diff()}点[gold]力量[/gold]。

#### 旋风斩 `WHIRLWIND` — 可直接实现

- 费用 X · attack · 目标 all_enemies
- 我们识别的效果：DealDamage
- 描述：对所有敌人造成{Damage:diff()}点伤害X次。

#### 无情猛攻 `UNRELENTING` — 实现方式不同

- 费用 2 · attack · 目标 any_enemy
- 我们识别的效果：ApplyPower、DealDamage
- 施加的状态：FreeAttackPower
- 额外钩子：BeforeCardPlayed（来自状态 FreeAttackPower）
- 描述：造成{Damage:diff()}点伤害。 / 你打出的下一张攻击牌耗能变为0{energyPrefix:energyIcons(1)}。

#### 无惧疼痛 `FEEL_NO_PAIN` — 实现方式不同

- 费用 1 · power · 目标 self
- 我们识别的效果：ApplyPower
- 施加的状态：FeelNoPainPower
- 额外钩子：AfterCardExhausted（来自状态 FeelNoPainPower）
- 描述：每当有一张牌被[gold]消耗[/gold]时，获得{Power:diff()}点[gold]格挡[/gold]。

#### 暴走 `RAMPAGE` — 可直接实现

- 费用 1 · attack · 目标 any_enemy
- 我们识别的效果：BoostCardDamage、DealDamage
- 描述：造成{Damage:diff()}点伤害。 / 将这张牌在本场战斗中的伤害增加{Increase:diff()}。

#### 杂耍 `JUGGLING` — 实现方式不同

- 费用 1 · power · 目标 self
- 我们识别的效果：ApplyPower
- 施加的状态：JugglingPower
- 额外钩子：AfterApplied（来自状态 JugglingPower）
- 描述：将你在每回合打出的第三张[gold]攻击牌[/gold]的复制品加入你的[gold]手牌[/gold]。

#### 欺凌 `BULLY` — 可直接实现

- 费用 0 · attack · 目标 any_enemy
- 我们识别的效果：DealDamage
- 额外钩子：Calculated
- 描述：造成{CalculatedDamage:diff()}点伤害。 / 该敌人身上每有一层[gold]易伤[/gold]就额外造成{ExtraDamage:diff()}点伤害。

#### 火焰屏障 `FLAME_BARRIER` — 可直接实现

- 费用 2 · skill · 目标 self
- 我们识别的效果：ApplyPower、GainBlock
- 施加的状态：FlameBarrierPower
- 描述：获得{Block:diff()}点[gold]格挡[/gold]。 / 你在这个回合每受到一次攻击，都会对攻击者造成{DamageBack:diff()}点伤害。

#### 灰烬打击 `ASHEN_STRIKE` — 可直接实现

- 费用 1 · attack · 目标 any_enemy
- 我们识别的效果：DealDamage
- 额外钩子：Calculated
- 数值来源：ExhaustPileCount
- 描述：造成{CalculatedDamage:diff()}点伤害。 / 你的[gold]消耗牌堆[/gold]中每有一张牌，伤害增加{ExtraDamage:diff()}。

#### 燃烧 `INFLAME` — 可直接实现

- 费用 1 · power · 目标 self
- 我们识别的效果：ApplyPower
- 施加的状态：StrengthPower
- 描述：获得{StrengthPower:diff()}点[gold]力量[/gold]。

#### 燃烧契约 `BURNING_PACT` — 可直接实现

- 费用 1 · skill · 目标 self
- 我们识别的效果：DrawCards、ExhaustCardsInPile
- 描述：[gold]消耗[/gold]1张牌。 / 抽{Cards:diff()}张牌。

#### 狂怒 `RAGE` — 可直接实现

- 费用 0 · skill · 目标 self
- 我们识别的效果：ApplyPower
- 施加的状态：RagePower
- 描述：打出此牌后，你在这个回合内每打出一张攻击牌，获得{Power:diff()}点[gold]格挡[/gold]。

#### 狱火 `INFERNO` — 可直接实现

- 费用 1 · power · 目标 self
- 我们识别的效果：ApplyPower
- 施加的状态：InfernoPower
- 描述：在你的回合开始时，失去1点生命。 / 每当你在你的回合内失去生命时，对所有敌人造成{InfernoPower:diff()}点伤害。

#### 被遗忘的仪式 `FORGOTTEN_RITUAL` — 可直接实现

- 费用 1 · skill · 目标 self · EXHAUST
- 我们识别的效果：GainEnergy
- 描述：如果你在本回合[gold]消耗过[/gold]卡牌，则获得{Energy:energyIcons()}。

#### 跃跃欲试 `EXPECT_A_FIGHT` — 实现方式不同

- 费用 2 · skill · 目标 self
- 我们识别的效果：ApplyPower、GainEnergy
- 施加的状态：NoEnergyGainPower
- 额外钩子：Calculated、AfterModifyingEnergyGain（来自状态 NoEnergyGainPower）、ModifyEnergyGain（来自状态 NoEnergyGainPower）
- 数值来源：HandCount
- 描述：你的[gold]手牌[/gold]中每有一张攻击牌，就获得{energyPrefix:energyIcons(1)}。 / 你在本回合内 / 不能再获得{energyPrefix:energyIcons(1)}。{InCombat: / （获得{CalculatedEnergy:energyIcons()}）|}

#### 踩踏 `STOMP` — 可直接实现

- 费用 3 · attack · 目标 all_enemies
- 我们识别的效果：DealDamage、ModifyCardCost
- 额外钩子：AfterCardEnteredCombat（≈我们的"打出一张牌时"，但战斗中生成的这张牌会少算之前打出的张数）、BeforeCardPlayed（≈我们的"打出一张牌时"）
- 描述：对所有敌人造成{Damage:diff()}点伤害。 / 你在本回合中每打出过一张攻击牌，其耗能减少1{energyPrefix:energyIcons(1)}。

#### 邪眼 `EVIL_EYE` — 可直接实现

- 费用 1 · skill · 目标 self
- 我们识别的效果：GainBlock
- 描述：获得{Block:diff()}点[gold]格挡[/gold]。 / 如果你在本回合[gold]消耗[/gold]过卡牌，则额外获得{Block:diff()}点[gold]格挡[/gold]。

#### 重振精神 `SECOND_WIND` — 可直接实现

- 费用 1 · skill · 目标 self
- 我们识别的效果：ExhaustCardsInPile、GainBlock
- 描述：[gold]消耗[/gold][gold]手牌[/gold]中所有非攻击牌，每张获得{Block:diff()}点[gold]格挡[/gold]。

#### 重锤 `BLUDGEON` — 可直接实现

- 费用 3 · attack · 目标 any_enemy
- 我们识别的效果：DealDamage
- 描述：造成{Damage:diff()}点伤害。

### rare（26 张）

#### 倾泻 `CASCADE` — 可直接实现

- 费用 X · skill · 目标 self
- 我们识别的效果：AutoPlay
- 描述：打出你[gold]抽牌堆[/gold]顶部的X{IfUpgraded:show:+1}张牌。

#### 凌虐 `MANGLE` — 可直接实现

- 费用 3 · attack · 目标 any_enemy
- 我们识别的效果：ApplyPower、DealDamage
- 施加的状态：ManglePower
- 描述：造成{Damage:diff()}点伤害。 / 敌人在本回合失去{StrengthLoss:diff()}点[gold]力量[/gold]。

#### 势不可当 `JUGGERNAUT` — 实现方式不同

- 费用 2 · power · 目标 self
- 我们识别的效果：ApplyPower
- 施加的状态：JuggernautPower
- 额外钩子：AfterBlockGained（来自状态 JuggernautPower）
- 描述：每当你获得[gold]格挡[/gold]时，对随机敌人造成{JuggernautPower:diff()}点伤害。

#### 原始力量 `PRIMAL_FORCE` — 可直接实现

- 费用 0 · skill · 目标 self
- 我们识别的效果：TransformCardsInPile、UpgradeCardsInPile
- 描述：将[gold]手牌[/gold]中的所有攻击牌变化为[gold]{IfUpgraded:show:巨石+|巨石}[/gold]。

#### 地狱狂徒 `HELLRAISER` — 实现方式不同

- 费用 2 · power · 目标 self
- 我们识别的效果：ApplyPower
- 施加的状态：HellraiserPower
- 额外钩子：AfterCardDrawnEarly（来自状态 HellraiserPower）、BeforeAttack（来自状态 HellraiserPower）
- 描述：每当你抽到名字中有“打击”的牌时，对一名随机敌人打出这张牌。

#### 坚定不移 `UNMOVABLE` — 实现方式不同

- 费用 2 · power · 目标 self
- 我们识别的效果：ApplyPower
- 施加的状态：UnmovablePower
- 额外钩子：ModifyBlockMultiplicative（来自状态 UnmovablePower）
- 描述：翻倍你每回合第一次从卡牌中获得的[gold]格挡[/gold]。

#### 壁垒 `BARRICADE` — 实现方式不同

- 费用 3 · power · 目标 self
- 我们识别的效果：ApplyPower
- 施加的状态：BarricadePower
- 额外钩子：AfterApplied（来自状态 BarricadePower）、ShouldClearBlock（来自状态 BarricadePower）
- 描述：[gold]格挡[/gold]不再在你的回合开始时消失。

#### 契约终结 `PACTS_END` — 可直接实现

- 费用 0 · attack · 目标 all_enemies
- 我们识别的效果：DealDamage、DrawCards
- 描述：如果你的[gold]消耗牌堆[/gold]拥有大于等于{Cards:diff()}张{Cards:plural:牌|牌}， / 则对所有敌人造成{Damage:diff()}点伤害。

#### 好勇斗狠 `AGGRESSION` — 可直接实现

- 费用 1 · power · 目标 self
- 我们识别的效果：ApplyPower
- 施加的状态：AggressionPower
- 描述：在你的回合开始时，将你[gold]弃牌堆[/gold]的一张随机攻击牌放入你的[gold]手牌[/gold]并将其[gold]升级[/gold]。

#### 岿然不动 `IMPERVIOUS` — 可直接实现

- 费用 2 · skill · 目标 self · EXHAUST
- 我们识别的效果：GainBlock
- 描述：获得{Block:diff()}点[gold]格挡[/gold]。

#### 恶魔之焰 `FIEND_FIRE` — 可直接实现

- 费用 2 · attack · 目标 any_enemy · EXHAUST
- 我们识别的效果：DealDamage、ExhaustCardsInPile
- 描述：[gold]消耗[/gold]所有[gold]手牌[/gold]。 / 每张被[gold]消耗[/gold]的牌造成{Damage:diff()}点伤害。

#### 恶魔形态 `DEMON_FORM` — 可直接实现

- 费用 3 · power · 目标 self
- 我们识别的效果：ApplyPower
- 施加的状态：DemonFormPower
- 描述：在你的回合开始时，获得{StrengthPower:diff()}点[gold]力量[/gold]。

#### 扯碎 `TEAR_ASUNDER` — 可直接实现

- 费用 2 · attack · 目标 any_enemy
- 我们识别的效果：DealDamage
- 额外钩子：Calculated、Repeat
- 描述：造成{Damage:diff()}点伤害。 / 在本场战斗中，你每失去过一次生命值，这张牌就额外造成一次伤害。{InCombat: / （命中{CalculatedHits:diff()}次）|}

#### 时候未到 `NOT_YET` — 可直接实现

- 费用 2 · skill · 目标 self · EXHAUST
- 我们识别的效果：Heal
- 描述：回复{Heal:diff()}点生命。

#### 残酷 `CRUELTY` — 可直接实现

- 费用 1 · power · 目标 self
- 我们识别的效果：ApplyPower
- 施加的状态：CrueltyPower
- 描述：有[gold]易伤[/gold]状态的敌人额外受到{CrueltyPower:diff()}%的伤害。

#### 添柴 `STOKE` — 可直接实现

- 费用 1 · skill · 目标 self
- 我们识别的效果：AddNamedCardToPile、ExhaustCardsInPile、UpgradeCardsInPile
- 描述：[gold]消耗[/gold]所有[gold]手牌[/gold]。 / 每[gold]消耗[/gold]一张牌，将1张随机{IfUpgraded:show:已[gold]升级[/gold]的|}牌加入你的[gold]手牌[/gold]。

#### 烙印 `BRAND` — 可直接实现

- 费用 0 · skill · 目标 self
- 我们识别的效果：ApplyPower、ExhaustCardsInPile、LoseHp
- 施加的状态：StrengthPower
- 描述：失去{HpLoss:diff()}点生命。 / [gold]消耗[/gold]1张牌。 / 获得{StrengthPower:diff()}点[gold]力量[/gold]。

#### 焚烧 `CONFLAGRATION` — 可直接实现

- 费用 1 · attack · 目标 all_enemies
- 我们识别的效果：DealDamage
- 额外钩子：Repeat
- 描述：对所有敌人造成{Damage:diff()}点伤害{Repeat:diff()}次。

#### 狂宴 `FEED` — 可直接实现

- 费用 1 · attack · 目标 any_enemy · EXHAUST
- 我们识别的效果：DealDamage、GainMaxHp
- 描述：造成{Damage:diff()}点伤害。 / [gold]斩杀[/gold]时，永久获得{MaxHp:diff()}点最大生命值。

#### 痛殴 `THRASH` — 可直接实现

- 费用 1 · attack · 目标 any_enemy
- 我们识别的效果：BoostCardDamage、DealDamage、ExhaustCardsInPile
- 描述：造成{Damage:diff()}点伤害两次。 / [gold]消耗[/gold]你的[gold]手牌[/gold]中随机一张攻击牌，并将它的伤害添加给这张牌。

#### 祭品 `OFFERING` — 可直接实现

- 费用 0 · skill · 目标 self · EXHAUST
- 我们识别的效果：DrawCards、GainEnergy、LoseHp
- 描述：失去{HpLoss:diff()}点生命。 / 获得{Energy:energyIcons()}。 / 抽{Cards:diff()}张牌。

#### 绯红披风 `CRIMSON_MANTLE` — 可直接实现

- 费用 1 · power · 目标 self
- 我们识别的效果：ApplyPower
- 施加的状态：CrimsonMantlePower
- 描述：在你的回合开始时，失去1点生命并获得{CrimsonMantlePower:diff()}点[gold]格挡[/gold]。

#### 肉盾 `TANK` — 实现方式不同

- 费用 1 · power · 目标 self
- 我们识别的效果：ApplyPower
- 施加的状态：TankPower
- 额外钩子：AfterApplied（来自状态 TankPower）
- 描述：承受双倍来自敌人的伤害。 / 敌人的伤害对盟友减半。

#### 薪火之源 `PYRE` — 实现方式不同

- 费用 2 · power · 目标 self
- 我们识别的效果：ApplyPower、GainEnergy
- 施加的状态：PyrePower
- 额外钩子：ModifyMaxEnergy（来自状态 PyrePower）
- 描述：在回合开始时，获得{Energy:energyIcons()}。

#### 连环拳 `ONE_TWO_PUNCH` — 实现方式不同

- 费用 1 · skill · 目标 self
- 我们识别的效果：ApplyPower
- 施加的状态：OneTwoPunchPower
- 额外钩子：AfterModifyingCardPlayCount（来自状态 OneTwoPunchPower）、ModifyCardPlayCount（来自状态 OneTwoPunchPower）
- 描述：在这个回合，你打出的下{Attacks:diff()}张攻击牌会被额外打出一次。

#### 黑暗之拥 `DARK_EMBRACE` — 实现方式不同

- 费用 2 · power · 目标 self
- 我们识别的效果：ApplyPower
- 施加的状态：DarkEmbracePower
- 额外钩子：AfterCardExhausted（来自状态 DarkEmbracePower）
- 描述：每当有一张牌被[gold]消耗[/gold]时， / 抽1张牌。

### ancient（2 张）

#### 破击 `BREAK` — 可直接实现

- 费用 1 · attack · 目标 any_enemy
- 我们识别的效果：ApplyPower、DealDamage
- 施加的状态：VulnerablePower
- 描述：造成{Damage:diff()}点伤害。 / 给予{VulnerablePower:diff()}层[gold]易伤[/gold]。

#### 腐化 `CORRUPTION` — 可直接实现

- 费用 3 · power · 目标 self
- 我们识别的效果：ApplyPower
- 施加的状态：CorruptionPower
- 描述：技能牌消耗变为0{energyPrefix:energyIcons(1)}。 / 每当你打出一张技能牌时，将其[gold]消耗[/gold]。

## silent（88 张）

### basic（4 张）

#### 中和 `NEUTRALIZE` — 可直接实现

- 费用 0 · attack · 目标 any_enemy
- 我们识别的效果：ApplyPower、DealDamage
- 施加的状态：WeakPower
- 描述：造成{Damage:diff()}点伤害。 / 给予{WeakPower:diff()}层[gold]虚弱[/gold]。

#### 打击 `STRIKE_SILENT` — 可直接实现

- 费用 1 · attack · 目标 any_enemy
- 我们识别的效果：DealDamage
- 描述：造成{Damage:diff()}点伤害。

#### 生存者 `SURVIVOR` — 可直接实现

- 费用 1 · skill · 目标 self
- 我们识别的效果：DiscardCardsInPile、GainBlock
- 描述：获得{Block:diff()}点[gold]格挡[/gold]。 / 丢弃1张牌。

#### 防御 `DEFEND_SILENT` — 可直接实现

- 费用 1 · skill · 目标 self
- 我们识别的效果：GainBlock
- 描述：获得{Block:diff()}点[gold]格挡[/gold]。

### common（20 张）

#### 偏折 `DEFLECT` — 可直接实现

- 费用 0 · skill · 目标 self
- 我们识别的效果：GainBlock
- 描述：获得{Block:diff()}点[gold]格挡[/gold]。

#### 先制打击 `LEADING_STRIKE` — 可直接实现

- 费用 1 · attack · 目标 any_enemy
- 我们识别的效果：DealDamage、DrawCards
- 描述：造成{Damage:diff()}点伤害。 / 将{Shivs:diff()}张[gold]小刀[/gold]加入你的[gold]手牌[/gold]。

#### 刀刃之舞 `BLADE_DANCE` — 可直接实现

- 费用 1 · skill · 目标 self · EXHAUST
- 我们识别的效果：DrawCards
- 描述：添加{Cards:diff()}张[gold]小刀[/gold]到你的[gold]手牌[/gold]。

#### 切割 `SLICE` — 可直接实现

- 费用 0 · attack · 目标 any_enemy
- 我们识别的效果：DealDamage
- 描述：造成{Damage:diff()}点伤害。

#### 匕首雨 `DAGGER_SPRAY` — 可直接实现

- 费用 1 · attack · 目标 all_enemies
- 我们识别的效果：DealDamage
- 描述：对所有敌人造成{Damage:diff()}点伤害两次。

#### 后空翻 `BACKFLIP` — 可直接实现

- 费用 1 · skill · 目标 self
- 我们识别的效果：DrawCards、GainBlock
- 描述：获得{Block:diff()}点[gold]格挡[/gold]。 / 抽2张牌。

#### 尖啸 `PIERCING_WAIL` — 可直接实现

- 费用 1 · skill · 目标 all_enemies · EXHAUST
- 我们识别的效果：ApplyPower
- 施加的状态：PiercingWailPower
- 描述：所有敌人在本回合失去{StrengthLoss:diff()}点[gold]力量[/gold]。

#### 带毒刺击 `POISONED_STAB` — 可直接实现

- 费用 1 · attack · 目标 any_enemy
- 我们识别的效果：ApplyPower、DealDamage
- 施加的状态：PoisonPower
- 描述：造成{Damage:diff()}点伤害。 / 给予{PoisonPower:diff()}层[gold]中毒[/gold]。

#### 投掷匕首 `DAGGER_THROW` — 可直接实现

- 费用 1 · attack · 目标 any_enemy
- 我们识别的效果：DealDamage、DiscardCardsInPile、DrawCards
- 描述：造成{Damage:diff()}点伤害。 / 抽1张牌。 / 丢弃1张牌。

#### 斗篷与匕首 `CLOAK_AND_DAGGER` — 可直接实现

- 费用 1 · skill · 目标 self
- 我们识别的效果：DrawCards、GainBlock
- 描述：获得{Block:diff()}点[gold]格挡[/gold]。 / 将{Cards:diff()}张[gold]小刀[/gold]添加到你的[gold]手牌[/gold]。

#### 早有准备 `PREPARED` — 可直接实现

- 费用 0 · skill · 目标 self
- 我们识别的效果：DiscardCardsInPile、DrawCards
- 描述：抽{Cards:diff()}张牌。 / 丢弃{Cards:diff()}张牌。

#### 猎杀者 `PREDATOR` — 实现方式不同

- 费用 2 · attack · 目标 any_enemy
- 我们识别的效果：ApplyPower、DealDamage
- 施加的状态：DrawCardsNextTurnPower
- 额外钩子：ModifyHandDraw（来自状态 DrawCardsNextTurnPower）
- 描述：造成{Damage:diff()}点伤害。 / 在下个回合抽2张牌。

#### 突然一拳 `SUCKER_PUNCH` — 可直接实现

- 费用 1 · attack · 目标 any_enemy
- 我们识别的效果：ApplyPower、DealDamage
- 施加的状态：WeakPower
- 描述：造成{Damage:diff()}点伤害。 / 给予{WeakPower:diff()}层[gold]虚弱[/gold]。

#### 翻越撑击 `FLICK_FLACK` — 可直接实现

- 费用 1 · attack · 目标 all_enemies · SLY
- 我们识别的效果：DealDamage
- 描述：对所有敌人造成{Damage:diff()}点伤害。

#### 致命毒药 `DEADLY_POISON` — 可直接实现

- 费用 1 · skill · 目标 any_enemy
- 我们识别的效果：ApplyPower
- 施加的状态：PoisonPower
- 描述：给予{PoisonPower:diff()}层[gold]中毒[/gold]。

#### 蛇咬 `SNAKEBITE` — 可直接实现

- 费用 2 · skill · 目标 any_enemy · RETAIN
- 我们识别的效果：ApplyPower
- 施加的状态：PoisonPower
- 描述：给予{PoisonPower:diff()}层[gold]中毒[/gold]。

#### 触不可及 `UNTOUCHABLE` — 可直接实现

- 费用 2 · skill · 目标 self · SLY
- 我们识别的效果：GainBlock
- 描述：获得{Block:diff()}点[gold]格挡[/gold]。

#### 连续反弹 `RICOCHET` — 可直接实现

- 费用 2 · attack · 目标 random_enemy · SLY
- 我们识别的效果：DealDamage
- 额外钩子：Repeat
- 描述：随机对敌人造成{Damage:diff()}点伤害{Repeat:diff()}次。

#### 闪躲翻滚 `DODGE_AND_ROLL` — 实现方式不同

- 费用 1 · skill · 目标 self
- 我们识别的效果：ApplyPower、GainBlock
- 施加的状态：BlockNextTurnPower
- 额外钩子：AfterBlockCleared（来自状态 BlockNextTurnPower）
- 描述：获得{Block:diff()}点[gold]格挡[/gold]。 / 在下个回合，获得{Block:diff()}点[gold]格挡[/gold]。

#### 预判 `ANTICIPATE` — 可直接实现

- 费用 0 · skill · 目标 self
- 我们识别的效果：ApplyPower
- 施加的状态：AnticipatePower
- 描述：在本回合获得{DexterityPower:diff()}点[gold]敏捷[/gold]。

### uncommon（36 张）

#### 串刺 `SKEWER` — 可直接实现

- 费用 X · attack · 目标 any_enemy
- 我们识别的效果：DealDamage
- 描述：造成{Damage:diff()}点伤害X次。

#### 冲刺 `DASH` — 可直接实现

- 费用 2 · attack · 目标 any_enemy
- 我们识别的效果：DealDamage、GainBlock
- 描述：获得{Block:diff()}点[gold]格挡[/gold]。 / 造成{Damage:diff()}点伤害。

#### 咕嘟冒泡 `BUBBLE_BUBBLE` — 可直接实现

- 费用 1 · skill · 目标 any_enemy
- 我们识别的效果：ApplyPower
- 施加的状态：PoisonPower
- 描述：如果敌方拥有[gold]中毒[/gold]，则给予{PoisonPower:diff()}层[gold]中毒[/gold]。

#### 夹击 `FLANKING` — 实现方式不同

- 费用 2 · skill · 目标 any_enemy
- 我们识别的效果：ApplyPower
- 施加的状态：FlankingPower
- 额外钩子：AfterApplied（来自状态 FlankingPower）
- 描述：一名敌人在本回合受到的来自其他玩家的攻击伤害翻倍。

#### 幻影之刃 `PHANTOM_BLADES` — 实现方式不同

- 费用 1 · power · 目标 self
- 我们识别的效果：ApplyPower
- 施加的状态：PhantomBladesPower
- 额外钩子：AfterApplied（来自状态 PhantomBladesPower）
- 描述：[gold]小刀[/gold]获得[gold]保留[/gold]。 / 你在每回合打出的第一张[gold]小刀[/gold]额外造成{PhantomBladesPower:diff()}点伤害。

#### 弹跳药瓶 `BOUNCING_FLASK` — 可直接实现

- 费用 2 · skill · 目标 random_enemy
- 我们识别的效果：ApplyPower
- 施加的状态：PoisonPower
- 额外钩子：Repeat
- 描述：随机给予敌人{PoisonPower:diff()}层[gold]中毒[/gold]{Repeat:diff()}次。

#### 恫吓 `SCARE` — 可直接实现

- 费用 0 · skill · 目标 all_enemies · EXHAUST
- 我们识别的效果：ApplyPower
- 施加的状态：WeakPower
- 描述：给予所有敌人1层[gold]虚弱[/gold]。

#### 战术大师 `TACTICIAN` — 可直接实现

- 费用 3 · skill · 目标 self · SLY
- 我们识别的效果：GainEnergy
- 描述：获得{Energy:energyIcons()}。

#### 手上技法 `HAND_TRICK` — 无法实现

- 费用 1 · skill · 目标 self
- 我们识别的效果：GainBlock
- **无法实现**：CardCmd.ApplySingleTurnSly（本回合附加奇巧）
- 描述：获得{Block:diff()}点[gold]格挡[/gold]。 / 在本回合给你[gold]手牌[/gold]中的一张技能牌添加[gold]奇巧[/gold]。

#### 扫腿 `LEG_SWEEP` — 可直接实现

- 费用 2 · skill · 目标 any_enemy
- 我们识别的效果：ApplyPower、GainBlock
- 施加的状态：WeakPower
- 描述：给予{WeakPower:diff()}层[gold]虚弱[/gold]。 / 获得{Block:diff()}点[gold]格挡[/gold]。

#### 无尽刀刃 `INFINITE_BLADES` — 实现方式不同

- 费用 1 · power · 目标 self
- 我们识别的效果：ApplyPower
- 施加的状态：InfiniteBladesPower
- 额外钩子：BeforeHandDraw（来自状态 InfiniteBladesPower）
- 描述：在你的回合开始时，在你的[gold]手牌[/gold]中加入1张[gold]小刀[/gold]。

#### 暴露 `EXPOSE` — 无法实现

- 费用 0 · skill · 目标 any_enemy · EXHAUST
- 我们识别的效果：ApplyPower、RemovePower
- **无法实现**：CreatureCmd.LoseBlock（移除格挡）
- 施加的状态：VulnerablePower
- 描述：去除敌人身上的所有[gold]格挡[/gold]值和[gold]人工制品[/gold]。 / 并给予{Power:diff()}层[gold]易伤[/gold]。

#### 本能反应 `REFLEX` — 可直接实现

- 费用 3 · skill · 目标 self · SLY
- 我们识别的效果：DrawCards
- 描述：抽{Cards:diff()}张牌。

#### 杂技 `ACROBATICS` — 可直接实现

- 费用 1 · skill · 目标 self
- 我们识别的效果：DiscardCardsInPile、DrawCards
- 描述：抽{Cards:diff()}张牌。 / 丢弃1张牌。

#### 残影 `BLUR` — 实现方式不同

- 费用 1 · skill · 目标 self
- 我们识别的效果：ApplyPower、GainBlock
- 施加的状态：BlurPower
- 额外钩子：AfterPreventingBlockClear（来自状态 BlurPower）、ShouldClearBlock（来自状态 BlurPower）
- 描述：获得{Block:diff()}点[gold]格挡[/gold]。 / 你的下一回合开始时[gold]格挡[/gold]不会消失。

#### 毒性爆发 `OUTBREAK` — 实现方式不同

- 费用 1 · power · 目标 self
- 我们识别的效果：ApplyPower
- 施加的状态：OutbreakPower
- 额外钩子：Repeat、AfterPowerAmountChanged（来自状态 OutbreakPower）
- 描述：每当你给予{Repeat:diff()}次[gold]中毒[/gold]，就对所有敌人造成{OutbreakPower:diff()}点伤害。

#### 毒雾 `NOXIOUS_FUMES` — 可直接实现

- 费用 1 · power · 目标 self
- 我们识别的效果：ApplyPower
- 施加的状态：NoxiousFumesPower
- 描述：在你的回合开始时，给予所有敌人{PoisonPerTurn:diff()}层[gold]中毒[/gold]。

#### 灵动步法 `FOOTWORK` — 实现方式不同

- 费用 1 · power · 目标 self
- 我们识别的效果：ApplyPower
- 施加的状态：DexterityPower
- 额外钩子：ModifyBlockAdditive（来自状态 DexterityPower）
- 描述：获得{DexterityPower:diff()}点[gold]敏捷[/gold]。

#### 独门技术 `EXPERTISE` — 可直接实现

- 费用 1 · skill · 目标 self
- 我们识别的效果：DrawCards
- 描述：抽牌直至你的[gold]手牌[/gold]有{Cards:diff()}张牌。

#### 猛扑 `POUNCE` — 实现方式不同

- 费用 2 · attack · 目标 any_enemy
- 我们识别的效果：ApplyPower、DealDamage
- 施加的状态：FreeSkillPower
- 额外钩子：BeforeCardPlayed（来自状态 FreeSkillPower）
- 描述：造成{Damage:diff()}点伤害。 / 你的下一张技能牌耗能变为0{energyPrefix:energyIcons(1)}。

#### 精准 `ACCURACY` — 可直接实现

- 费用 1 · power · 目标 self
- 我们识别的效果：ApplyPower
- 施加的状态：AccuracyPower
- 描述：[gold]小刀[/gold]额外造成{AccuracyPower:diff()}点伤害。

#### 精密瞄准 `PINPOINT` — 可直接实现

- 费用 3 · attack · 目标 any_enemy
- 我们识别的效果：DealDamage、ModifyCardCost
- 额外钩子：AfterCardEnteredCombat（≈我们的"打出一张牌时"，但战斗中生成的这张牌会少算之前打出的张数）、AfterCardPlayed（≈我们的"打出一张牌时"）
- 描述：造成{Damage:diff()}点伤害。 / 你在本回合中每打出过一张技能牌，其耗能减少1{energyPrefix:energyIcons(1)}。

#### 精确切击 `PRECISE_CUT` — 可直接实现

- 费用 0 · attack · 目标 any_enemy
- 我们识别的效果：DealDamage
- 额外钩子：Calculated
- 数值来源：HandCount
- 描述：造成{CalculatedDamage:diff()}点伤害。 / 你的[gold]手牌[/gold]中每有一张牌，此牌的伤害就降低{ExtraDamage:inverseDiff()}点。

#### 紧勒 `STRANGLE` — 实现方式不同

- 费用 1 · attack · 目标 any_enemy
- 我们识别的效果：ApplyPower、DealDamage
- 施加的状态：StranglePower
- 额外钩子：BeforeCardPlayed（来自状态 StranglePower）
- 描述：造成{Damage:diff()}点伤害。 / 你在这个回合内每出一张牌，该名敌人都会失去{StranglePower:diff()}点生命。

#### 终结技 `FINISHER` — 无法实现

- 费用 1 · attack · 目标 any_enemy
- 我们识别的效果：DealDamage
- **无法实现**：按战斗历史计数作为数值来源（超出我们支持的条目或筛选维度）
- 额外钩子：Calculated
- 描述：在这个回合内每打出过一张攻击牌，就造成{Damage:diff()}点伤害一次。{InCombat: / （攻击{CalculatedHits:diff()}次）|}

#### 背刺 `BACKSTAB` — 可直接实现

- 费用 0 · attack · 目标 any_enemy · EXHAUST/INNATE
- 我们识别的效果：DealDamage
- 描述：造成{Damage:diff()}点伤害。

#### 蜃景 `MIRAGE` — 可直接实现

- 费用 1 · skill · 目标 self · EXHAUST
- 我们识别的效果：GainBlock
- 额外钩子：Calculated
- 描述：获得等同于所有敌人[gold]中毒[/gold]层数的[gold]格挡[/gold]。{InCombat: / （获得{CalculatedBlock:diff()}点[gold]格挡[/gold]）|}

#### 袖里乾坤 `UP_MY_SLEEVE` — 可直接实现

- 费用 2 · skill · 目标 self
- 我们识别的效果：DrawCards、ModifyCardCost
- 描述：将{Cards:diff()}张[gold]小刀[/gold]加入你的[gold]手牌[/gold]。 / 这张牌的耗能减少1。

#### 计划妥当 `WELL_LAID_PLANS` — 实现方式不同

- 费用 1 · power · 目标 self
- 我们识别的效果：ApplyPower
- 施加的状态：WellLaidPlansPower
- 额外钩子：BeforeFlushLate（来自状态 WellLaidPlansPower）
- 描述：在你的回合结束时，[gold]保留[/gold]最多{RetainAmount:diff()}张牌。

#### 计算下注 `CALCULATED_GAMBLE` — 可直接实现

- 费用 0 · skill · 目标 self · EXHAUST
- 我们识别的效果：DiscardCardsInPile、DrawCards
- 描述：丢弃你的所有[gold]手牌[/gold], / 然后抽相同数量的牌。

#### 迷雾 `HAZE` — 可直接实现

- 费用 3 · skill · 目标 all_enemies · SLY
- 我们识别的效果：ApplyPower
- 施加的状态：PoisonPower
- 描述：给予所有敌人{PoisonPower:diff()}层[gold]中毒[/gold]。

#### 逃脱计划 `ESCAPE_PLAN` — 可直接实现

- 费用 0 · skill · 目标 self
- 我们识别的效果：DrawCards、GainBlock
- 描述：抽1张牌。 / 如果抽到的是技能牌，则获得{Block:diff()}点[gold]格挡[/gold]。

#### 速行者 `SPEEDSTER` — 实现方式不同

- 费用 2 · power · 目标 self
- 我们识别的效果：ApplyPower
- 施加的状态：SpeedsterPower
- 额外钩子：AfterCardDrawn（来自状态 SpeedsterPower）
- 描述：每当你在回合进行中抽到一张牌时，对所有敌人造成{SpeedsterPower:diff()}点伤害。

#### 铭记死亡 `MEMENTO_MORI` — 可直接实现

- 费用 1 · attack · 目标 any_enemy
- 我们识别的效果：DealDamage
- 额外钩子：Calculated
- 描述：造成{CalculatedDamage:diff()}点伤害。 / 本回合每丢弃过一张牌，则增加{ExtraDamage:diff()}点额外伤害。

#### 隐秘匕首 `HIDDEN_DAGGERS` — 可直接实现

- 费用 0 · skill · 目标 self
- 我们识别的效果：DiscardCardsInPile、DrawCards、UpgradeCardsInPile
- 描述：丢弃{Cards:diff()}张牌。 / 将{Shivs:diff()}张[gold]{IfUpgraded:show:小刀+|小刀}[/gold]加入你的[gold]手牌[/gold]。

#### 飞镖 `FLECHETTES` — 可直接实现

- 费用 1 · attack · 目标 any_enemy
- 我们识别的效果：DealDamage
- 额外钩子：Calculated
- 数值来源：HandCount
- 描述：[gold]手牌[/gold]中每有一张技能牌，造成{Damage:diff()}点伤害。{InCombat: / （攻击{CalculatedHits:diff()}次）|}

### rare（26 张）

#### 余像 `AFTERIMAGE` — 实现方式不同

- 费用 1 · power · 目标 self
- 我们识别的效果：ApplyPower
- 施加的状态：AfterimagePower
- 额外钩子：BeforeCardPlayed（来自状态 AfterimagePower）
- 描述：你每打出一张牌，都获得{AfterimagePower:diff()}点[gold]格挡[/gold]。

#### 刀刃陷阱 `KNIFE_TRAP` — 可直接实现

- 费用 2 · skill · 目标 any_enemy
- 我们识别的效果：AutoPlay、UpgradeCardsInPile
- 额外钩子：Calculated
- 数值来源：ExhaustPileCount
- 描述：将你[gold]消耗牌堆[/gold]中的所有[gold]小刀[/gold]{IfUpgraded:show:[gold]升级[/gold]然后|}对一名敌人打出。{InCombat: / （打出{CalculatedShivs:diff()}张[gold]小刀[/gold]）|}

#### 刀扇 `FAN_OF_KNIVES` — 可直接实现

- 费用 2 · power · 目标 self
- 我们识别的效果：ApplyPower、DrawCards
- 施加的状态：FanOfKnivesPower
- 描述：[gold]小刀[/gold]现在会攻击所有敌人。 / 将{Shivs:diff()}张[gold]小刀[/gold]添加到你的[gold]手牌[/gold]。

#### 刺杀 `ASSASSINATE` — 可直接实现

- 费用 0 · attack · 目标 any_enemy · INNATE/EXHAUST
- 我们识别的效果：ApplyPower、DealDamage
- 施加的状态：VulnerablePower
- 描述：造成{Damage:diff()}点伤害。 / 给予{VulnerablePower:diff()}层[gold]易伤[/gold]。

#### 华丽收场 `GRAND_FINALE` — 可直接实现

- 费用 0 · attack · 目标 all_enemies
- 我们识别的效果：DealDamage
- 描述：只有当你的[gold]抽牌堆[/gold]中没有牌时才能打出。对所有敌人造成{Damage:diff()}点伤害。

#### 回响斩击 `ECHOING_SLASH` — 可直接实现

- 费用 1 · attack · 目标 all_enemies
- 我们识别的效果：DealDamage
- 描述：对所有敌人造成{Damage:diff()}点伤害。 / 每有一名敌人被击杀，就重复此效果。

#### 墨之刃 `BLADE_OF_INK` — 无法实现

- 费用 1 · skill · 目标 self
- 我们识别的效果：DrawCards
- **无法实现**：CardCmd.Enchant（战斗内附魔一张牌）
- 描述：添加{Cards:diff()}张[purple]墨影[/purple][gold]小刀[/gold]到你的[gold]手牌[/gold]。

#### 夜魇 `NIGHTMARE` — 实现方式不同

- 费用 3 · skill · 目标 self · EXHAUST
- 我们识别的效果：ApplyPower
- 施加的状态：NightmarePower
- 额外钩子：BeforeHandDraw（来自状态 NightmarePower）
- 描述：选择一张牌。 / 在下个回合将这张牌的3张复制品加入你的[gold]手牌[/gold]。

#### 子弹时间 `BULLET_TIME` — 实现方式不同

- 费用 3 · skill · 目标 self
- 我们识别的效果：ApplyPower、SetCardsFree
- 施加的状态：NoDrawPower
- 额外钩子：ShouldDraw（来自状态 NoDrawPower）
- 描述：你在本回合内不能再抽牌。你[gold]手牌[/gold]中的所有牌在本回合免费打出。

#### 必备工具 `TOOLS_OF_THE_TRADE` — 实现方式不同

- 费用 1 · power · 目标 self
- 我们识别的效果：ApplyPower
- 施加的状态：ToolsOfTheTradePower
- 额外钩子：ModifyHandDraw（来自状态 ToolsOfTheTradePower）
- 描述：在你的回合开始时，抽1张牌，丢弃1张牌。

#### 暗影步 `SHADOW_STEP` — 可直接实现

- 费用 1 · skill · 目标 self
- 我们识别的效果：ApplyPower、DiscardCardsInPile、DrawCards
- 施加的状态：ShadowStepPower
- 描述：丢弃所有[gold]手牌[/gold]。 / 在下个回合，你所有的攻击伤害翻倍。

#### 涂毒 `ENVENOM` — 实现方式不同

- 费用 2 · power · 目标 self
- 我们识别的效果：ApplyPower
- 施加的状态：EnvenomPower
- 额外钩子：AfterDamageGiven（来自状态 EnvenomPower）
- 描述：每有一次攻击造成未被格挡的伤害，就给予{EnvenomPower:diff()}层[gold]中毒[/gold]。

#### 爆发 `BURST` — 实现方式不同

- 费用 1 · skill · 目标 self
- 我们识别的效果：ApplyPower
- 施加的状态：BurstPower
- 额外钩子：AfterModifyingCardPlayCount（来自状态 BurstPower）、ModifyCardPlayCount（来自状态 BurstPower）
- 描述：在这个回合，你打出的下{Skills:choose(1):一|{:diff()}}张技能牌会被额外打出一次。

#### 狩猎 `THE_HUNT` — 可直接实现

- 费用 1 · attack · 目标 any_enemy · EXHAUST
- 我们识别的效果：ApplyPower、DealDamage
- 施加的状态：TheHuntPower
- 描述：造成{Damage:diff()}点伤害。 / [gold]斩杀[/gold]时，额外获得一次卡牌奖励。

#### 磨蚀 `ABRASIVE` — 实现方式不同

- 费用 3 · power · 目标 self · SLY
- 我们识别的效果：ApplyPower
- 施加的状态：DexterityPower、ThornsPower
- 额外钩子：ModifyBlockAdditive（来自状态 DexterityPower）、BeforeDamageReceived（来自状态 ThornsPower）
- 描述：获得{DexterityPower:diff()}点[gold]敏捷[/gold]。 / 获得{ThornsPower:diff()}点[gold]荆棘[/gold]。

#### 群蛇形态 `SERPENT_FORM` — 实现方式不同

- 费用 3 · power · 目标 self
- 我们识别的效果：ApplyPower
- 施加的状态：SerpentFormPower
- 额外钩子：BeforeCardPlayed（来自状态 SerpentFormPower）
- 描述：你每打出一张牌，就对随机一名敌人造成{SerpentFormPower:diff()}点伤害。

#### 肾上腺素 `ADRENALINE` — 可直接实现

- 费用 0 · skill · 目标 self · EXHAUST
- 我们识别的效果：DrawCards、GainEnergy
- 描述：获得{Energy:energyIcons()}。 / 抽2张牌。

#### 腐蚀波 `CORROSIVE_WAVE` — 实现方式不同

- 费用 1 · skill · 目标 self
- 我们识别的效果：ApplyPower
- 施加的状态：CorrosiveWavePower
- 额外钩子：AfterCardDrawn（来自状态 CorrosiveWavePower）
- 描述：打出此牌后，你在本回合每抽到一张牌，就给予所有敌人{CorrosiveWave:diff()}层[gold]中毒[/gold]。

#### 萎靡 `MALAISE` — 可直接实现

- 费用 X · skill · 目标 any_enemy · EXHAUST
- 我们识别的效果：ApplyPower
- 施加的状态：StrengthPower、WeakPower
- 描述：敌人失去X{IfUpgraded:show:+1}点[gold]力量[/gold]。给予X{IfUpgraded:show:+1}层[gold]虚弱[/gold]。

#### 融入暗影 `SHADOWMELD` — 实现方式不同

- 费用 1 · skill · 目标 self
- 我们识别的效果：ApplyPower
- 施加的状态：ShadowmeldPower
- 额外钩子：ModifyBlockMultiplicative（来自状态 ShadowmeldPower）
- 描述：本回合你获得的[gold]格挡[/gold]值翻倍。

#### 触媒 `ACCELERANT` — 可直接实现

- 费用 1 · power · 目标 self
- 我们识别的效果：ApplyPower
- 施加的状态：AccelerantPower
- 描述：[gold]中毒[/gold]会额外触发{Accelerant:diff()}次。

#### 谋划专家 `MASTER_PLANNER` — 可直接实现

- 费用 2 · power · 目标 self
- 我们识别的效果：ApplyPower
- 施加的状态：MasterPlannerPower
- 描述：当你打出技能牌时，该牌获得[gold]奇巧[/gold]。

#### 谋杀 `MURDER` — 可直接实现

- 费用 3 · attack · 目标 any_enemy
- 我们识别的效果：DealDamage
- 额外钩子：Calculated
- 描述：造成{CalculatedDamage:diff()}点伤害。 / 你在本场战斗中每抽过一张牌，此牌就额外造成{ExtraDamage:diff()}点伤害。

#### 跟踪 `TRACKING` — 可直接实现

- 费用 2 · power · 目标 self
- 我们识别的效果：ApplyPower
- 施加的状态：TrackingPower
- 描述：对[gold]虚弱[/gold]状态的敌人，你所造成的攻击伤害翻倍。

#### 钢铁风暴 `STORM_OF_STEEL` — 可直接实现

- 费用 1 · skill · 目标 self
- 我们识别的效果：DiscardCardsInPile、UpgradeCardsInPile
- 描述：丢弃所有[gold]手牌[/gold]。 / 每丢弃一张牌，就将一张[gold]{IfUpgraded:show:小刀+|小刀}[/gold]添加至你的[gold]手牌[/gold]。

#### 鬼祟 `SNEAKY` — 可直接实现

- 费用 2 · power · 目标 self · SLY
- 我们识别的效果：ApplyPower
- 施加的状态：SneakyPower
- 描述：每当其他玩家攻击一名敌人时，获得{SneakyPower:diff()}点[gold]格挡[/gold]。

### ancient（2 张）

#### 压制 `SUPPRESS` — 可直接实现

- 费用 0 · attack · 目标 any_enemy · INNATE
- 我们识别的效果：ApplyPower、DealDamage
- 施加的状态：WeakPower
- 描述：造成{Damage:diff()}点伤害。 / 给予{WeakPower:diff()}层[gold]虚弱[/gold]。

#### 幽魂形态 `WRAITH_FORM` — 实现方式不同

- 费用 3 · power · 目标 self
- 我们识别的效果：ApplyPower
- 施加的状态：IntangiblePower、WraithFormPower
- 额外钩子：AfterModifyingDamageAmount（来自状态 IntangiblePower）、AfterModifyingHpLostAfterOsty（来自状态 IntangiblePower）、ModifyDamageCap（来自状态 IntangiblePower）、ModifyHpLostAfterOsty（来自状态 IntangiblePower）
- 描述：获得{IntangiblePower:diff()}[gold]无实体[/gold]。 / 在你的回合开始时，失去{WraithFormPower:diff()}点[gold]敏捷[/gold]。

## defect（88 张）

### basic（4 张）

#### 双重释放 `DUALCAST` — 可直接实现

- 费用 1 · skill · 目标 self
- 我们识别的效果：EvokeOrbs
- 描述：[gold]激发[/gold]你最右侧的充能球两次。

#### 打击 `STRIKE_DEFECT` — 可直接实现

- 费用 1 · attack · 目标 any_enemy
- 我们识别的效果：DealDamage
- 描述：造成{Damage:diff()}点伤害。

#### 电击 `ZAP` — 可直接实现

- 费用 1 · skill · 目标 self
- 我们识别的效果：ChannelOrb
- 描述：[gold]生成[/gold]1个[gold]闪电[/gold]充能球。

#### 防御 `DEFEND_DEFECT` — 可直接实现

- 费用 1 · skill · 目标 self
- 我们识别的效果：GainBlock
- 描述：获得{Block:diff()}点[gold]格挡[/gold]。

### common（20 张）

#### 充电 `CHARGE_BATTERY` — 实现方式不同

- 费用 1 · skill · 目标 self
- 我们识别的效果：ApplyPower、GainBlock、GainEnergy
- 施加的状态：EnergyNextTurnPower
- 额外钩子：AfterEnergyReset（来自状态 EnergyNextTurnPower）
- 描述：获得{Block:diff()}点[gold]格挡[/gold]。 / 在下个回合获得{Energy:energyIcons()}。

#### 光束射线 `BEAM_CELL` — 可直接实现

- 费用 0 · attack · 目标 any_enemy
- 我们识别的效果：ApplyPower、DealDamage
- 施加的状态：VulnerablePower
- 描述：造成{Damage:diff()}点伤害。 / 给予{VulnerablePower:diff()}层[gold]易伤[/gold]。

#### 全息影像 `HOLOGRAM` — 可直接实现

- 费用 1 · skill · 目标 self · EXHAUST
- 我们识别的效果：GainBlock、MoveCardsBetweenPiles
- 描述：获得{Block:diff()}点[gold]格挡[/gold]。 / 将[gold]弃牌堆[/gold]中的一张牌放入你的[gold]手牌[/gold]。

#### 内核加速 `TURBO` — 可直接实现

- 费用 0 · skill · 目标 self
- 我们识别的效果：AddNamedCardToPile、GainEnergy
- 描述：获得{Energy:energyIcons()}。 / 将一张[gold]虚空[/gold]加入你的[gold]弃牌堆[/gold]。

#### 冷静头脑 `COOLHEADED` — 可直接实现

- 费用 1 · skill · 目标 self
- 我们识别的效果：ChannelOrb、DrawCards
- 描述：[gold]生成[/gold]1个[gold]冰霜[/gold]充能球。 / 抽{Cards:diff()}张牌。

#### 寒流 `COLD_SNAP` — 可直接实现

- 费用 1 · attack · 目标 any_enemy
- 我们识别的效果：ChannelOrb、DealDamage
- 描述：造成{Damage:diff()}点伤害。 / [gold]生成[/gold]1个[gold]冰霜[/gold]充能球。

#### 引雷针 `LIGHTNING_ROD` — 实现方式不同

- 费用 1 · skill · 目标 self
- 我们识别的效果：ApplyPower、GainBlock
- 施加的状态：LightningRodPower
- 额外钩子：AfterEnergyReset（来自状态 LightningRodPower）
- 描述：获得{Block:diff()}点[gold]格挡[/gold]。 / 在下{LightningRodPower:diff()}个回合开始时,[gold]生成[/gold]1个[gold]闪电[/gold]充能球。

#### 弹幕齐射 `BARRAGE` — 无法实现

- 费用 1 · attack · 目标 any_enemy
- 我们识别的效果：DealDamage
- **无法实现**：按充能球数量作为数值来源（我们没有）
- 额外钩子：Calculated
- 描述：当前每有一个[gold]充能球[/gold]，造成{Damage:diff()}点伤害。{InCombat: / （命中{CalculatedHits:diff()}次）|}

#### 扫荡射线 `SWEEPING_BEAM` — 可直接实现

- 费用 1 · attack · 目标 all_enemies
- 我们识别的效果：DealDamage、DrawCards
- 描述：对所有敌人造成{Damage:diff()}点伤害。 / 抽{Cards:diff()}张牌。

#### 污秽攻击 `GUNK_UP` — 可直接实现

- 费用 1 · attack · 目标 any_enemy
- 我们识别的效果：AddNamedCardToPile、DealDamage
- 额外钩子：Repeat
- 描述：造成{Damage:diff()}点伤害{Repeat:diff()}次。 / 在你的[gold]弃牌堆[/gold]中加入一张[gold]黏液[/gold]。

#### 热修复 `HOTFIX` — 可直接实现

- 费用 0 · skill · 目标 self · EXHAUST
- 我们识别的效果：ApplyPower
- 施加的状态：HotfixPower
- 描述：在本回合获得{FocusPower:diff()}点[gold]集中[/gold]。

#### 爪击 `CLAW` — 可直接实现

- 费用 0 · attack · 目标 any_enemy
- 我们识别的效果：BoostCardDamage、DealDamage
- 描述：造成{Damage:diff()}点伤害。 / 本场战斗中所有爪击卡牌的伤害增加{Increase:diff()}点。

#### 球状闪电 `BALL_LIGHTNING` — 可直接实现

- 费用 1 · attack · 目标 any_enemy
- 我们识别的效果：ChannelOrb、DealDamage
- 描述：造成{Damage:diff()}点伤害。 / [gold]生成[/gold]1个[gold]闪电[/gold]充能球。

#### 眼部攻击 `GO_FOR_THE_EYES` — 可直接实现

- 费用 0 · attack · 目标 any_enemy
- 我们识别的效果：ApplyPower、DealDamage
- 施加的状态：WeakPower
- 描述：造成{Damage:diff()}点伤害。 / 如果敌人的意图是攻击，则给予{WeakPower:diff()}层[gold]虚弱[/gold]。

#### 编译冲击 `COMPILE_DRIVER` — 无法实现

- 费用 1 · attack · 目标 any_enemy
- 我们识别的效果：DealDamage、DrawCards
- **无法实现**：按充能球数量作为数值来源（我们没有）
- 额外钩子：Calculated
- 描述：造成{Damage:diff()}点伤害。 / 你每有一种不同的充能球，就抽一张牌。{InCombat: / （抽{CalculatedCards:diff()}张牌）}

#### 趁势打击 `MOMENTUM_STRIKE` — 可直接实现

- 费用 1 · attack · 目标 any_enemy
- 我们识别的效果：DealDamage、ModifyCardCost
- 描述：造成{Damage:diff()}点伤害。 / 这张牌的耗能降为0{energyPrefix:energyIcons(1)}。

#### 集中打击 `FOCUSED_STRIKE` — 可直接实现

- 费用 1 · attack · 目标 any_enemy
- 我们识别的效果：ApplyPower、DealDamage
- 施加的状态：FocusedStrikePower
- 描述：造成{Damage:diff()}点伤害。 / 在本回合获得{FocusPower:diff()}点[gold]集中[/gold]。

#### 飞跃 `LEAP` — 可直接实现

- 费用 1 · skill · 目标 self
- 我们识别的效果：GainBlock
- 描述：获得{Block:diff()}点[gold]格挡[/gold]。

#### 骚动 `UPROAR` — 可直接实现

- 费用 2 · attack · 目标 any_enemy
- 我们识别的效果：AutoPlay、DealDamage
- 描述：造成{Damage:diff()}点伤害两次。 / 随机打出你的[gold]抽牌堆[/gold]中的1张攻击牌。

#### 高速脱离 `BOOST_AWAY` — 可直接实现

- 费用 0 · skill · 目标 self
- 我们识别的效果：AddNamedCardToPile、GainBlock
- 描述：获得{Block:diff()}点[gold]格挡[/gold]。 / 将一张[gold]晕眩[/gold]添加到你的[gold]弃牌堆[/gold]中。

### uncommon（36 张）

#### 人工合成 `SYNTHESIS` — 实现方式不同

- 费用 2 · attack · 目标 any_enemy
- 我们识别的效果：ApplyPower、DealDamage
- 施加的状态：FreePowerPower
- 额外钩子：BeforeCardPlayed（来自状态 FreePowerPower）
- 描述：造成{Damage:diff()}点伤害。 / 你打出的下一张能力牌耗能变为0{energyPrefix:energyIcons(1)}。

#### 内存清理 `SCAVENGE` — 实现方式不同

- 费用 1 · skill · 目标 self
- 我们识别的效果：ApplyPower、ExhaustCardsInPile、GainEnergy
- 施加的状态：EnergyNextTurnPower
- 额外钩子：AfterEnergyReset（来自状态 EnergyNextTurnPower）
- 描述：[gold]消耗[/gold]一张牌。 / 在下个回合获得{Energy:energyIcons()}。

#### 冰寒 `CHILL` — 可直接实现

- 费用 0 · skill · 目标 self · EXHAUST
- 我们识别的效果：ChannelOrb
- 描述：当前每有一名敌人，就[gold]生成[/gold]1个[gold]冰霜[/gold]充能球。

#### 冰川 `GLACIER` — 可直接实现

- 费用 2 · skill · 目标 self
- 我们识别的效果：ChannelOrb、GainBlock
- 描述：获得{Block:diff()}点[gold]格挡[/gold]。 / [gold]生成[/gold]2个[gold]冰霜[/gold]充能球。

#### 冰雹风暴 `HAILSTORM` — 实现方式不同

- 费用 1 · power · 目标 self
- 我们识别的效果：ApplyPower
- 施加的状态：HailstormPower
- 额外钩子：BeforeSideTurnEnd（来自状态 HailstormPower）
- 描述：在你的回合结束时，如果你有[gold]冰霜[/gold]充能球，则对所有敌人造成{HailstormPower:diff()}点伤害。

#### 分离 `SUNDER` — 可直接实现

- 费用 3 · attack · 目标 any_enemy
- 我们识别的效果：DealDamage、GainEnergy
- 描述：造成{Damage:diff()}点伤害。 / 如果这张牌击杀了敌人，则获得{Energy:energyIcons()}。

#### 刮削 `SCRAPE` — 可直接实现

- 费用 1 · attack · 目标 any_enemy
- 我们识别的效果：DealDamage、DiscardCardsInPile、DrawCards
- 描述：造成{Damage:diff()}点伤害。 / 抽{Cards:diff()}张牌。 / 丢弃抽到的牌中耗能不为0{energyPrefix:energyIcons(1)}的牌。

#### 压缩 `COMPACT` — 可直接实现

- 费用 1 · skill · 目标 self
- 我们识别的效果：GainBlock、TransformCardsInPile、UpgradeCardsInPile
- 描述：获得{Block:diff()}点[gold]格挡[/gold]。 / 将你[gold]手牌[/gold]中的全部状态牌[gold]变化[/gold]为[gold]{IfUpgraded:show:燃料+|燃料}[/gold]。

#### 双倍能量 `DOUBLE_ENERGY` — 可直接实现

- 费用 1 · skill · 目标 self · EXHAUST
- 我们识别的效果：GainEnergy
- 描述：将你的能量翻倍。

#### 同步 `SYNCHRONIZE` — 无法实现

- 费用 1 · skill · 目标 self · EXHAUST
- 我们识别的效果：ApplyPower
- **无法实现**：按充能球数量作为数值来源（我们没有）
- 施加的状态：SynchronizePower
- 额外钩子：Calculated
- 描述：你每有一种不同的充能球，就在本回合获得{CalculationExtra:diff()}点[gold]集中[/gold]。{InCombat: / （获得{CalculatedFocus:diff()}点[gold]集中[/gold]）|}

#### 启动流程 `BOOT_SEQUENCE` — 可直接实现

- 费用 0 · skill · 目标 self · INNATE/EXHAUST
- 我们识别的效果：GainBlock
- 描述：获得{Block:diff()}点[gold]格挡[/gold]。

#### 子程序 `SUBROUTINE` — 实现方式不同

- 费用 1 · power · 目标 self
- 我们识别的效果：ApplyPower
- 施加的状态：SubroutinePower
- 额外钩子：BeforeCardPlayed（来自状态 SubroutinePower）
- 描述：当你打出一张能力牌时，获得{energyPrefix:energyIcons(1)}。

#### 强撑 `FIGHT_THROUGH` — 可直接实现

- 费用 1 · skill · 目标 self
- 我们识别的效果：AddNamedCardToPile、GainBlock
- 描述：获得{Block:diff()}点[gold]格挡[/gold]。 / 将2张[gold]伤口[/gold]加入你的[gold]弃牌堆[/gold]。

#### 循环 `LOOP` — 可直接实现

- 费用 1 · power · 目标 self
- 我们识别的效果：ApplyPower
- 施加的状态：LoopPower
- 描述：在你的回合开始时，触发你最右侧的一个充能球的被动能力{IfUpgraded:show:2次}。

#### 快速检索 `SKIM` — 可直接实现

- 费用 1 · skill · 目标 self
- 我们识别的效果：DrawCards
- 描述：抽{Cards:diff()}张牌。

#### 扩容 `CAPACITOR` — 可直接实现

- 费用 1 · power · 目标 self
- 我们识别的效果：AddOrbSlots
- 额外钩子：Repeat
- 描述：获得{Repeat:diff()}个充能球栏位。

#### 折射 `REFRACT` — 可直接实现

- 费用 3 · attack · 目标 any_enemy
- 我们识别的效果：ChannelOrb、DealDamage
- 额外钩子：Repeat
- 描述：造成{Damage:diff()}点伤害两次。 / [gold]生成[/gold]{Repeat:diff()}个[gold]玻璃[/gold]充能球。

#### 暗影之盾 `SHADOW_SHIELD` — 可直接实现

- 费用 2 · skill · 目标 self
- 我们识别的效果：ChannelOrb、GainBlock
- 描述：获得{Block:diff()}点[gold]格挡[/gold]。 / [gold]生成[/gold]1个[gold]黑暗[/gold]充能球。

#### 暴涨 `BULK_UP` — 无法实现

- 费用 2 · power · 目标 self
- 我们识别的效果：ApplyPower
- **无法实现**：OrbCmd.RemoveSlots（移除充能球栏位）
- 施加的状态：DexterityPower、StrengthPower
- 额外钩子：ModifyBlockAdditive（来自状态 DexterityPower）
- 描述：失去{OrbSlots:diff()}个充能球栏位。 / 获得{StrengthPower:diff()}点[gold]力量[/gold]。 / 获得{DexterityPower:diff()}点[gold]敏捷[/gold]。

#### 暴风雨 `TEMPEST` — 可直接实现

- 费用 X · skill · 目标 self
- 我们识别的效果：ChannelOrb
- 描述：[gold]生成[/gold]{IfUpgraded:show:X+1|X}个[gold]闪电[/gold]充能球。

#### 混沌 `CHAOS` — 可直接实现

- 费用 1 · skill · 目标 self
- 我们识别的效果：ChannelOrb
- 额外钩子：Repeat
- 描述：[gold]生成[/gold]{Repeat:diff()}个随机充能球。

#### 漆黑 `DARKNESS` — 无法实现

- 费用 1 · skill · 目标 self
- 我们识别的效果：ChannelOrb
- **无法实现**：OrbCmd.Passive（触发充能球被动）
- 描述：[gold]生成[/gold]1[gold]黑暗[/gold]充能球。 / 触发所有[gold]黑暗[/gold]充能球的被动{IfUpgraded:show:两次|}。

#### 火箭飞拳 `ROCKET_PUNCH` — 实现方式不同

- 费用 2 · attack · 目标 any_enemy
- 我们识别的效果：DealDamage、DrawCards、ModifyCardCost
- 额外钩子：AfterCardGeneratedForCombat
- 描述：造成{Damage:diff()}点伤害。 / 抽{Cards:diff()}张牌。 / 每当你生成状态牌时，此牌的耗能将在下一次打出前降为0{energyPrefix:energyIcons(1)}。

#### 烟囱 `SMOKESTACK` — 实现方式不同

- 费用 1 · power · 目标 self
- 我们识别的效果：ApplyPower
- 施加的状态：SmokestackPower
- 额外钩子：AfterCardGeneratedForCombat（来自状态 SmokestackPower）
- 描述：每当你生成一张状态牌时，对所有敌人造成{SmokestackPower:diff()}点伤害。

#### 特斯拉线圈 `TESLA_COIL` — 无法实现

- 费用 0 · attack · 目标 any_enemy
- 我们识别的效果：DealDamage
- **无法实现**：OrbCmd.Passive（触发充能球被动）
- 描述：造成{Damage:diff()}点伤害。 / 对该敌人触发你的所有[gold]闪电[/gold]充能球的被动{IfUpgraded:show: 两次|}。

#### 玻璃工艺 `GLASSWORK` — 可直接实现

- 费用 1 · skill · 目标 self
- 我们识别的效果：ChannelOrb、GainBlock
- 描述：获得{Block:diff()}点[gold]格挡[/gold]。 / [gold]生成[/gold]1个[gold]玻璃[/gold]充能球。

#### 白噪声 `WHITE_NOISE` — 可直接实现

- 费用 1 · skill · 目标 self · EXHAUST
- 我们识别的效果：AddNamedCardToPile、SetCardsFree
- 描述：将一张随机能力牌加入你的[gold]手牌[/gold]。这张牌在本回合内免费打出。

#### 空值 `NULL` — 可直接实现

- 费用 2 · attack · 目标 any_enemy
- 我们识别的效果：ApplyPower、ChannelOrb、DealDamage
- 施加的状态：WeakPower
- 描述：造成{Damage:diff()}点伤害。 / 给予{WeakPower:diff()}层[gold]虚弱[/gold]。 / [gold]生成[/gold]1个[gold]黑暗[/gold]充能球。

#### 聚变 `FUSION` — 可直接实现

- 费用 1 · skill · 目标 self · EXHAUST
- 我们识别的效果：ChannelOrb
- 描述：[gold]生成[/gold]1个[gold]等离子[/gold]充能球。

#### 能量涌动 `ENERGY_SURGE` — 可直接实现

- 费用 1 · skill · 目标 all_allies · EXHAUST
- 我们识别的效果：GainEnergy
- 描述：所有玩家获得{Energy:energyIcons()}。

#### 超越光速 `FTL` — 可直接实现

- 费用 0 · attack · 目标 any_enemy
- 我们识别的效果：DealDamage、DrawCards
- 描述：造成{Damage:diff()}点伤害。 / 如果你在这回合打出的牌数小于{PlayMax:diff()}张，抽1张牌。

#### 超频 `OVERCLOCK` — 可直接实现

- 费用 0 · skill · 目标 self
- 我们识别的效果：AddNamedCardToPile、DrawCards
- 描述：抽{Cards:diff()}张牌。 / 将一张[gold]灼伤[/gold]加入你的[gold]弃牌堆[/gold]。

#### 迭代 `ITERATION` — 实现方式不同

- 费用 1 · power · 目标 self
- 我们识别的效果：ApplyPower
- 施加的状态：IterationPower
- 额外钩子：AfterCardDrawn（来自状态 IterationPower）
- 描述：每回合你第一次抽到状态牌时，抽{IterationPower:diff()}张牌。

#### 野性 `FERAL` — 实现方式不同

- 费用 2 · power · 目标 self
- 我们识别的效果：ApplyPower
- 施加的状态：FeralPower
- 额外钩子：AfterApplied（来自状态 FeralPower）、AfterModifyingCardPlayResultPileOrPosition（来自状态 FeralPower）
- 描述：你每回合打出的{FeralPower:choose(1):第一张|前{FeralPower:diff()}张} / 耗能为0{energyPrefix:energyIcons(1)}的攻击牌， / 会放回你的[gold]手牌[/gold]。

#### 雷暴 `STORM` — 实现方式不同

- 费用 1 · power · 目标 self
- 我们识别的效果：ApplyPower
- 施加的状态：StormPower
- 额外钩子：BeforeCardPlayed（来自状态 StormPower）
- 描述：每当你打出一张能力牌时，[gold]生成[/gold]{StormPower:diff()}个[gold]闪电[/gold]充能球。

#### 雷霆 `THUNDER` — 实现方式不同

- 费用 1 · power · 目标 self
- 我们识别的效果：ApplyPower
- 施加的状态：ThunderPower
- 额外钩子：AfterOrbEvoked（来自状态 ThunderPower）
- 描述：每当你[gold]激发闪电[/gold]充能球时，对被命中的敌人造成{ThunderPower:diff()}点伤害。

### rare（26 张）

#### 万物一心 `ALL_FOR_ONE` — 可直接实现

- 费用 2 · attack · 目标 any_enemy
- 我们识别的效果：DealDamage、MoveCardsBetweenPiles
- 描述：造成{Damage:diff()}点伤害。 / 将你[gold]弃牌堆[/gold]中的所有0{energyPrefix:energyIcons(1)}牌放入你的[gold]手牌[/gold]。

#### 信号增强 `SIGNAL_BOOST` — 实现方式不同

- 费用 1 · skill · 目标 self · EXHAUST
- 我们识别的效果：ApplyPower
- 施加的状态：SignalBoostPower
- 额外钩子：AfterModifyingCardPlayCount（来自状态 SignalBoostPower）、ModifyCardPlayCount（来自状态 SignalBoostPower）
- 描述：你的下一张能力牌会额外打出一次。

#### 冰之长枪 `ICE_LANCE` — 可直接实现

- 费用 3 · attack · 目标 any_enemy
- 我们识别的效果：ChannelOrb、DealDamage
- 额外钩子：Repeat
- 描述：造成{Damage:diff()}点伤害。 / [gold]生成[/gold]{Repeat:diff()}个[gold]冰霜[/gold]充能球。

#### 冷却剂 `COOLANT` — 可直接实现

- 费用 1 · power · 目标 self
- 我们识别的效果：ApplyPower
- 施加的状态：CoolantPower
- 描述：在你的回合开始时，你每有一种不同的充能球，就获得{CoolantPower:diff()}点[gold]格挡[/gold]。

#### 创造性AI `CREATIVE_AI` — 实现方式不同

- 费用 3 · power · 目标 self
- 我们识别的效果：ApplyPower
- 施加的状态：CreativeAiPower
- 额外钩子：BeforeHandDraw（来自状态 CreativeAiPower）
- 描述：在你的回合开始时，将一张随机能力牌加入你的[gold]手牌[/gold]。

#### 化废为宝 `TRASH_TO_TREASURE` — 实现方式不同

- 费用 1 · power · 目标 self
- 我们识别的效果：ApplyPower
- 施加的状态：TrashToTreasurePower
- 额外钩子：AfterCardGeneratedForCombat（来自状态 TrashToTreasurePower）
- 描述：每当你生成状态牌的时候，随机[gold]生成[/gold]一个充能球。

#### 吞噬暗影 `CONSUMING_SHADOW` — 可直接实现

- 费用 2 · power · 目标 self
- 我们识别的效果：ApplyPower、ChannelOrb
- 施加的状态：ConsumingShadowPower
- 额外钩子：Repeat
- 描述：[gold]生成[/gold]{Repeat:diff()}个[gold]黑暗[/gold]充能球。 / 在你的回合结束时，[gold]激发[/gold]你最左侧的充能球。

#### 回响形态 `ECHO_FORM` — 实现方式不同

- 费用 3 · power · 目标 self · ETHEREAL
- 我们识别的效果：ApplyPower
- 施加的状态：EchoFormPower
- 额外钩子：AfterModifyingCardPlayCount（来自状态 EchoFormPower）、ModifyCardPlayCount（来自状态 EchoFormPower）
- 描述：你每回合打出的第一张牌会被打出两次。

#### 多重释放 `MULTI_CAST` — 可直接实现

- 费用 X · skill · 目标 self
- 我们识别的效果：EvokeOrbs
- 描述：[gold]激发[/gold]你最右侧的充能球{IfUpgraded:show:X+1|X}次。

#### 引火 `IGNITION` — 可直接实现

- 费用 1 · skill · 目标 any_ally · EXHAUST
- 我们识别的效果：ChannelOrb
- 描述：使另一名玩家[gold]生成[/gold][gold]等离子[/gold]充能球。

#### 彩虹 `RAINBOW` — 可直接实现

- 费用 2 · skill · 目标 self · EXHAUST
- 我们识别的效果：ChannelOrb
- 描述：[gold]生成[/gold]1个[gold]闪电[/gold]充能球。 / [gold]生成[/gold]1个[gold]冰霜[/gold]充能球。 / [gold]生成[/gold]1个[gold]黑暗[/gold]充能球。

#### 打碎 `SHATTER` — 可直接实现

- 费用 1 · attack · 目标 all_enemies
- 我们识别的效果：DealDamage、EvokeOrbs
- 描述：对所有敌人造成{Damage:diff()}点伤害。 / [gold]激发[/gold]你的所有充能球两次。

#### 散射炮 `FLAK_CANNON` — 可直接实现

- 费用 2 · attack · 目标 random_enemy
- 我们识别的效果：DealDamage、ExhaustCardsInPile
- 额外钩子：Calculated、ExhaustKeyword
- 描述：[gold]消耗[/gold]你所有的状态牌。 / 每有一张被[gold]消耗[/gold]的牌，就随机对敌人造成{Damage:diff()}点伤害。{InCombat: / （攻击{CalculatedHits:diff()}次）|}

#### 旋转工艺 `SPINNER` — 实现方式不同

- 费用 1 · power · 目标 self
- 我们识别的效果：ApplyPower、ChannelOrb
- 施加的状态：SpinnerPower
- 额外钩子：AfterEnergyReset（来自状态 SpinnerPower）
- 描述：{IfUpgraded:show:[gold]生成[/gold]1个[gold]玻璃[/gold]充能球。 / |}在你的回合开始时，[gold]生成[/gold]1个[gold]玻璃[/gold]充能球。

#### 机器学习 `MACHINE_LEARNING` — 实现方式不同

- 费用 1 · power · 目标 self
- 我们识别的效果：ApplyPower、DrawCards
- 施加的状态：MachineLearningPower
- 额外钩子：ModifyHandDraw（来自状态 MachineLearningPower）
- 描述：在你的回合开始时，额外抽{Cards:diff()}张牌。

#### 模组改造 `MODDED` — 可直接实现

- 费用 0 · skill · 目标 self
- 我们识别的效果：AddOrbSlots、DrawCards、ModifyCardCost
- 额外钩子：Repeat
- 描述：获得{Repeat:diff()}个充能球栏位。 / 抽{Cards:diff()}张牌。这张牌的耗能加1。

#### 电流相生 `VOLTAIC` — 无法实现

- 费用 3 · skill · 目标 self · EXHAUST
- 我们识别的效果：ChannelOrb
- **无法实现**：按战斗历史计数作为数值来源（超出我们支持的条目或筛选维度）
- 额外钩子：Calculated
- 描述：[gold]生成[/gold]等量于你在这场战斗中[gold]生成过[/gold]的[gold]闪电[/gold]充能球数量的[gold]闪电[/gold]充能球。{InCombat: / （[gold]生成[/gold]{CalculatedChannels:diff()}个[gold]闪电[/gold]充能球）|}

#### 碎片整理 `DEFRAGMENT` — 实现方式不同

- 费用 1 · power · 目标 self
- 我们识别的效果：ApplyPower
- 施加的状态：FocusPower
- 额外钩子：ModifyOrbValue（来自状态 FocusPower）
- 描述：获得{FocusPower:diff()}点[gold]集中[/gold]。

#### 缓冲 `BUFFER` — 实现方式不同

- 费用 2 · power · 目标 self
- 我们识别的效果：ApplyPower
- 施加的状态：BufferPower
- 额外钩子：AfterModifyingHpLostAfterOsty（来自状态 BufferPower）、ModifyHpLostAfterOstyLate（来自状态 BufferPower）
- 描述：阻止下{BufferPower:diff()}次你受到的生命值损伤。

#### 螺旋钻击 `HELIX_DRILL` — 无法实现

- 费用 0 · attack · 目标 any_enemy
- 我们识别的效果：DealDamage
- **无法实现**：按战斗历史计数作为数值来源（超出我们支持的条目或筛选维度）
- 额外钩子：Calculated
- 描述：在本回合中，每个被使用的{energyPrefix:energyIcons(1)}，都会使此牌造成{Damage:diff()}点伤害一次。{InCombat: / （命中{CalculatedHits:diff()}次）|}

#### 超临界态 `SUPERCRITICAL` — 可直接实现

- 费用 0 · skill · 目标 self · EXHAUST
- 我们识别的效果：GainEnergy
- 描述：获得{Energy:energyIcons()}。

#### 超能光束 `HYPERBEAM` — 实现方式不同

- 费用 2 · attack · 目标 all_enemies
- 我们识别的效果：ApplyPower、DealDamage
- 施加的状态：FocusPower
- 额外钩子：ModifyOrbValue（来自状态 FocusPower）
- 描述：对所有敌人造成{Damage:diff()}点伤害。 / 失去{FocusPower:diff()}点[gold]集中[/gold]。

#### 适应打击 `ADAPTIVE_STRIKE` — 可直接实现

- 费用 2 · attack · 目标 any_enemy
- 我们识别的效果：AddNamedCardToPile、DealDamage、ModifyCardCost
- 描述：造成{Damage:diff()}点伤害。 / 将这张牌的一张0{energyPrefix:energyIcons(1)}复制品添加到你的[gold]弃牌堆[/gold]。

#### 遗传算法 `GENETIC_ALGORITHM` — 可直接实现

- 费用 1 · skill · 目标 self · EXHAUST
- 我们识别的效果：GainBlock
- 描述：获得{Block:diff()}点[gold]格挡[/gold]。 / 每打出一次，这张牌在本局游戏中的[gold]格挡[/gold]值永久增加{Increase:diff()}点。

#### 重启 `REBOOT` — 无法实现

- 费用 0 · skill · 目标 self · EXHAUST
- 我们识别的效果：DrawCards、MoveCardsBetweenPiles
- **无法实现**：CardPileCmd.Shuffle（洗牌）
- 描述：将你的所有未消耗的卡牌重新洗牌放入[gold]抽牌堆[/gold]。 / 抽{Cards:diff()}张牌。

#### 陨石打击 `METEOR_STRIKE` — 可直接实现

- 费用 5 · attack · 目标 any_enemy
- 我们识别的效果：ChannelOrb、DealDamage
- 描述：造成{Damage:diff()}点伤害。 / [gold]生成[/gold]3个[gold]等离子[/gold]充能球。

### ancient（2 张）

#### 偏差认知 `BIASED_COGNITION` — 实现方式不同

- 费用 1 · power · 目标 self
- 我们识别的效果：ApplyPower
- 施加的状态：BiasedCognitionPower、FocusPower
- 额外钩子：ModifyOrbValue（来自状态 FocusPower）
- 描述：获得{FocusPower:diff()}点[gold]集中[/gold]。 / 在你的回合开始时，失去{BiasedCognitionPower:diff()}点[gold]集中[/gold]。

#### 四重释放 `QUADCAST` — 可直接实现

- 费用 1 · skill · 目标 self
- 我们识别的效果：EvokeOrbs
- 额外钩子：Repeat
- 描述：[gold]激发[/gold]你最右侧的充能球{Repeat:diff()}次。

## necrobinder（88 张）

### basic（4 张）

#### 出击 `UNLEASH` — 可直接实现

- 费用 1 · attack · 目标 any_enemy
- 我们识别的效果：DealDamage
- 额外钩子：Calculated
- 数值来源：CurrentHp
- 描述：[gold]奥斯提[/gold]造成{CalculatedDamage:diff()}点伤害。 / 这张牌额外造成等量于[gold]奥斯提[/gold]当前生命值的伤害。

#### 打击 `STRIKE_NECROBINDER` — 可直接实现

- 费用 1 · attack · 目标 any_enemy
- 我们识别的效果：DealDamage
- 描述：造成{Damage:diff()}点伤害。

#### 护卫 `BODYGUARD` — 可直接实现

- 费用 1 · skill · 目标 self
- 我们识别的效果：Summon
- 描述：[gold]召唤[/gold]{Summon:diff()}。

#### 防御 `DEFEND_NECROBINDER` — 可直接实现

- 费用 1 · skill · 目标 self
- 我们识别的效果：GainBlock
- 描述：获得{Block:diff()}点[gold]格挡[/gold]。

### common（20 张）

#### 剥夺 `REAVE` — 可直接实现

- 费用 1 · attack · 目标 any_enemy
- 我们识别的效果：AddNamedCardToPile、DealDamage、DrawCards、UpgradeCardsInPile
- 描述：造成{Damage:diff()}点伤害。 / 将一张[gold]{IfUpgraded:show:灵魂+|灵魂}[/gold]加入你的[gold]抽牌堆[/gold]。

#### 吸引仇恨 `PULL_AGGRO` — 可直接实现

- 费用 2 · skill · 目标 self
- 我们识别的效果：GainBlock、Summon
- 描述：[gold]召唤[/gold]{Summon:diff()}。 / 获得{Block:diff()}点[gold]格挡[/gold]。

#### 响指 `SNAP` — 无法实现

- 费用 1 · attack · 目标 any_enemy
- 我们识别的效果：DealDamage
- **无法实现**：CardCmd.ApplyKeyword（附加关键词）
- 描述：[gold]奥斯提[/gold]造成{OstyDamage:diff()}点伤害。 / 给[gold]手牌[/gold]中的一张牌添加[gold]保留[/gold]。

#### 唤起 `INVOKE` — 实现方式不同

- 费用 1 · skill · 目标 self
- 我们识别的效果：ApplyPower、GainEnergy、Summon
- 施加的状态：EnergyNextTurnPower、SummonNextTurnPower
- 额外钩子：AfterEnergyReset（来自状态 EnergyNextTurnPower）
- 描述：在下个回合[gold]召唤[/gold]{Summon:diff()}并获得{Energy:energyIcons()}。

#### 坟冢爆射 `GRAVEBLAST` — 可直接实现

- 费用 1 · attack · 目标 any_enemy · EXHAUST
- 我们识别的效果：DealDamage、MoveCardsBetweenPiles
- 描述：造成{Damage:diff()}点伤害。 / 将[gold]弃牌堆[/gold]中的一张牌放入你的[gold]手牌[/gold]。

#### 守墓人 `GRAVE_WARDEN` — 可直接实现

- 费用 1 · skill · 目标 self
- 我们识别的效果：AddNamedCardToPile、DrawCards、GainBlock
- 描述：获得{Block:diff()}点[gold]格挡[/gold]。 / 将一张[gold]灵魂[/gold]放入你的[gold]抽牌堆[/gold]中。

#### 恐惧 `FEAR` — 可直接实现

- 费用 1 · attack · 目标 any_enemy · ETHEREAL
- 我们识别的效果：ApplyPower、DealDamage
- 施加的状态：VulnerablePower
- 描述：造成{Damage:diff()}点伤害。 / 给予{VulnerablePower:diff()}层[gold]易伤[/gold]。

#### 戳击 `POKE` — 可直接实现

- 费用 0 · attack · 目标 any_enemy
- 我们识别的效果：DealDamage
- 描述：[gold]奥斯提[/gold]造成{OstyDamage:diff()}点伤害。

#### 播种 `SOW` — 可直接实现

- 费用 1 · attack · 目标 all_enemies · RETAIN
- 我们识别的效果：DealDamage
- 描述：对所有敌人造成{Damage:diff()}点伤害。

#### 收割 `REAP` — 可直接实现

- 费用 3 · attack · 目标 any_enemy · RETAIN
- 我们识别的效果：DealDamage
- 描述：造成{Damage:diff()}点伤害。

#### 来生 `AFTERLIFE` — 可直接实现

- 费用 1 · skill · 目标 self · EXHAUST
- 我们识别的效果：Summon
- 描述：[gold]召唤[/gold]{Summon:diff()}。

#### 玷污 `DEFILE` — 可直接实现

- 费用 1 · attack · 目标 any_enemy · ETHEREAL
- 我们识别的效果：DealDamage
- 描述：造成{Damage:diff()}点伤害。

#### 能量汲取 `DRAIN_POWER` — 可直接实现

- 费用 1 · attack · 目标 any_enemy
- 我们识别的效果：DealDamage、DrawCards、UpgradeCardsInPile
- 描述：造成{Damage:diff()}点伤害。 / 随机[gold]升级[/gold]你[gold]弃牌堆[/gold]中的{Cards:diff()}张牌。

#### 荒疫打击 `BLIGHT_STRIKE` — 实现方式不同

- 费用 1 · attack · 目标 any_enemy
- 我们识别的效果：ApplyPower、DealDamage
- 施加的状态：DoomPower
- 额外钩子：BeforeSideTurnEnd（来自状态 DoomPower）
- 描述：造成{Damage:diff()}点伤害。 / 给予等量于所造成伤害的[gold]灾厄[/gold]。

#### 负能量脉冲 `NEGATIVE_PULSE` — 实现方式不同

- 费用 1 · skill · 目标 all_enemies
- 我们识别的效果：ApplyPower、GainBlock
- 施加的状态：DoomPower
- 额外钩子：BeforeSideTurnEnd（来自状态 DoomPower）
- 描述：获得{Block:diff()}点[gold]格挡[/gold]。 / 给予所有敌人{DoomPower:diff()}层[gold]灾厄[/gold]。

#### 违逆 `DEFY` — 可直接实现

- 费用 1 · skill · 目标 any_enemy · ETHEREAL
- 我们识别的效果：ApplyPower、GainBlock
- 施加的状态：WeakPower
- 描述：获得{Block:diff()}点[gold]格挡[/gold]。 / 给予{WeakPower:diff()}层[gold]虚弱[/gold]。

#### 重压 `FLATTEN` — 可直接实现

- 费用 2 · attack · 目标 any_enemy
- 我们识别的效果：DealDamage、ModifyCardCost
- 额外钩子：AfterCardEnteredCombat（≈我们的"打出一张牌时"，但战斗中生成的这张牌会少算之前打出的张数）
- 描述：[gold]奥斯提[/gold]造成{OstyDamage:diff()}点伤害。 / 如果[gold]奥斯提[/gold]本回合攻击过，则这张牌的耗能变为0{energyPrefix:energyIcons(1)}。

#### 雕琢打击 `SCULPTING_STRIKE` — 无法实现

- 费用 1 · attack · 目标 any_enemy
- 我们识别的效果：DealDamage
- **无法实现**：CardCmd.ApplyKeyword（附加关键词）
- 描述：造成{Damage:diff()}点伤害。 / 为一张[gold]手牌[/gold]添加[gold]虚无[/gold]。

#### 鞭打 `SCOURGE` — 实现方式不同

- 费用 1 · skill · 目标 any_enemy
- 我们识别的效果：ApplyPower、DrawCards
- 施加的状态：DoomPower
- 额外钩子：BeforeSideTurnEnd（来自状态 DoomPower）
- 描述：给予{DoomPower:diff()}层[gold]灾厄[/gold]。 / 抽{Cards:diff()}张牌。

#### 鬼火 `WISP` — 可直接实现

- 费用 0 · skill · 目标 self · EXHAUST
- 我们识别的效果：GainEnergy
- 描述：获得{Energy:energyIcons()}。

### uncommon（36 张）

#### 书页风暴 `PAGESTORM` — 实现方式不同

- 费用 1 · power · 目标 self
- 我们识别的效果：ApplyPower、DrawCards
- 施加的状态：PagestormPower
- 额外钩子：AfterCardDrawn（来自状态 PagestormPower）
- 描述：每当你抽到一张[gold]虚无[/gold]牌时, 抽{Cards:diff()}张牌。

#### 亡魂牵引 `PULL_FROM_BELOW` — 无法实现

- 费用 1 · attack · 目标 any_enemy
- 我们识别的效果：DealDamage
- **无法实现**：按战斗历史计数作为数值来源（超出我们支持的条目或筛选维度）
- 额外钩子：Calculated
- 描述：本场战斗中每打出过一张[gold]虚无[/gold]牌，此牌就造成{Damage:diff()}点伤害一次。{InCombat: / （命中{CalculatedHits:diff()}次）|}

#### 倒数计时 `COUNTDOWN` — 可直接实现

- 费用 1 · power · 目标 self
- 我们识别的效果：ApplyPower
- 施加的状态：CountdownPower
- 描述：在你的回合开始时，给予随机敌人{CountdownPower:diff()}层[gold]灾厄[/gold]。

#### 击掌 `HIGH_FIVE` — 可直接实现

- 费用 2 · attack · 目标 all_enemies
- 我们识别的效果：ApplyPower、DealDamage
- 施加的状态：VulnerablePower
- 描述：[gold]奥斯提[/gold]对所有敌人 / 造成{OstyDamage:diff()}点伤害并给予它们 / {VulnerablePower:diff()}层[gold]易伤[/gold]。

#### 切断 `SEVERANCE` — 可直接实现

- 费用 2 · attack · 目标 any_enemy
- 我们识别的效果：AddNamedCardToPile、DealDamage
- 描述：造成{Damage:diff()}点伤害。 / 将一张[gold]灵魂[/gold]分别加入你的[gold]抽牌堆[/gold]，[gold]手牌[/gold]和[gold]弃牌堆[/gold]中。

#### 刺破帷幕 `VEILPIERCER` — 实现方式不同

- 费用 1 · attack · 目标 any_enemy
- 我们识别的效果：ApplyPower、DealDamage
- 施加的状态：VeilpiercerPower
- 额外钩子：BeforeCardPlayed（来自状态 VeilpiercerPower）
- 描述：造成{Damage:diff()}点伤害。 / 你打出的下一张[gold]虚无[/gold]牌耗能变为0{energyPrefix:energyIcons(1)}。

#### 厄运之衣 `SHROUD` — 实现方式不同

- 费用 1 · power · 目标 self
- 我们识别的效果：ApplyPower、GainBlock
- 施加的状态：ShroudPower
- 额外钩子：AfterPowerAmountChanged（来自状态 ShroudPower）
- 描述：每当你给予[gold]灾厄[/gold]时，获得{Block:diff()}点[gold]格挡[/gold]。

#### 友谊 `FRIENDSHIP` — 实现方式不同

- 费用 1 · power · 目标 self
- 我们识别的效果：ApplyPower、GainEnergy
- 施加的状态：FriendshipPower、StrengthPower
- 额外钩子：ModifyMaxEnergy（来自状态 FriendshipPower）
- 描述：失去{StrengthPower:inverseDiff()}点[gold]力量[/gold]。 / 在每个回合开始时获得{Energy:energyIcons()}。

#### 取回 `FETCH` — 可直接实现

- 费用 0 · attack · 目标 any_enemy
- 我们识别的效果：DealDamage、DrawCards
- 描述：[gold]奥斯提[/gold]造成{OstyDamage:diff()}点伤害。 / 如果这是这张牌第一次在本回合被打出，则抽{Cards:diff()}张牌。

#### 埋葬 `BURY` — 可直接实现

- 费用 4 · attack · 目标 any_enemy
- 我们识别的效果：DealDamage
- 描述：造成{Damage:diff()}点伤害。

#### 增生 `SPUR` — 可直接实现

- 费用 1 · skill · 目标 self · RETAIN
- 我们识别的效果：Heal、Summon
- 描述：[gold]召唤[/gold]{Summon:diff()}。 / [gold]奥斯提[/gold]回复{Heal:diff()}点生命。

#### 弱化之触 `ENFEEBLING_TOUCH` — 可直接实现

- 费用 1 · skill · 目标 any_enemy · ETHEREAL
- 我们识别的效果：ApplyPower
- 施加的状态：EnfeeblingTouchPower
- 描述：让一名敌人在本回合内失去{StrengthLoss:diff()}点[gold]力量[/gold]。

#### 得力助手 `RIGHT_HAND_HAND` — 可直接实现

- 费用 0 · attack · 目标 any_enemy
- 我们识别的效果：DealDamage、GainEnergy、MoveCardsBetweenPiles
- 额外钩子：AfterCardPlayedLate（≈我们的"打出一张牌时"）
- 描述：[gold]奥斯提[/gold]造成{OstyDamage:diff()}点伤害。 / 每当你打出耗能为{Energy:energyIcons()}或以上的牌，将此牌从[gold]弃牌堆[/gold]放回你的[gold]手牌[/gold]。

#### 忧郁 `MELANCHOLY` — 实现方式不同

- 费用 3 · skill · 目标 self
- 我们识别的效果：GainBlock、GainEnergy、ModifyCardCost
- 额外钩子：AfterDeath
- 描述：获得{Block:diff()}点[gold]格挡[/gold]。 / 每当有任何生物死亡时，这张牌的耗能减少{Energy:energyIcons()}。

#### 拖延 `DELAY` — 实现方式不同

- 费用 2 · skill · 目标 self
- 我们识别的效果：ApplyPower、GainBlock、GainEnergy
- 施加的状态：EnergyNextTurnPower
- 额外钩子：AfterEnergyReset（来自状态 EnergyNextTurnPower）
- 描述：获得{Block:diff()}点[gold]格挡[/gold]。 / 在下个回合， / 获得{Energy:energyIcons()}。

#### 挽歌 `DIRGE` — 可直接实现

- 费用 X · skill · 目标 self · EXHAUST
- 我们识别的效果：AddNamedCardToPile、Summon、UpgradeCardsInPile
- 描述：[gold]召唤[/gold]{Summon:diff()}X次。 / 将X张[gold]{IfUpgraded:show:灵魂+|灵魂}[/gold]添加到你的[gold]抽牌堆[/gold]中。

#### 捕捉灵魂 `CAPTURE_SPIRIT` — 可直接实现

- 费用 1 · skill · 目标 any_enemy
- 我们识别的效果：AddNamedCardToPile、DealDamage、DrawCards
- 描述：敌人失去{Damage:diff()}点生命。 / 将{Cards:diff()}张[gold]灵魂[/gold]加入你的[gold]抽牌堆[/gold]。

#### 摧残 `DEBILITATE` — 可直接实现

- 费用 1 · attack · 目标 any_enemy
- 我们识别的效果：ApplyPower、DealDamage
- 施加的状态：DebilitatePower
- 描述：造成{Damage:diff()}点伤害。 / 在接下来的{DebilitatePower:diff()}回合内，该敌人身上的[gold]易伤[/gold]与[gold]虚弱[/gold]效果翻倍。

#### 无处可逃 `NO_ESCAPE` — 实现方式不同

- 费用 1 · skill · 目标 any_enemy
- 我们识别的效果：ApplyPower
- 施加的状态：DoomPower
- 额外钩子：Calculated、BeforeSideTurnEnd（来自状态 DoomPower）
- 描述：给予{CalculationBase:diff()}层[gold]灾厄[/gold]，敌人身上每有{DoomThreshold:diff()}层[gold]灾厄[/gold]，则额外给予这名敌人{CalculationExtra:diff()}层[gold]灾厄[/gold]。{IsTargeting: / （给予{CalculatedDoom:diff()}层[gold]灾厄[/gold]）|}

#### 死亡之舞 `DANSE_MACABRE` — 实现方式不同

- 费用 1 · power · 目标 self
- 我们识别的效果：ApplyPower、GainEnergy
- 施加的状态：DanseMacabrePower
- 额外钩子：BeforeCardPlayed（来自状态 DanseMacabrePower）
- 描述：每当你打出一张耗能大于等于{Energy:energyIcons()}的牌时，获得{DanseMacabrePower:diff()}点[gold]格挡[/gold]。

#### 死亡之门 `DEATHS_DOOR` — 可直接实现

- 费用 1 · skill · 目标 self
- 我们识别的效果：GainBlock
- 额外钩子：Repeat
- 描述：获得{Block:diff()}点[gold]格挡[/gold]。 / 如果你在本回合中曾给予过[gold]灾厄[/gold]，则额外获得{Repeat:diff()}次[gold]格挡[/gold]。

#### 死亡使者 `DEATHBRINGER` — 实现方式不同

- 费用 2 · skill · 目标 all_enemies
- 我们识别的效果：ApplyPower
- 施加的状态：DoomPower、WeakPower
- 额外钩子：BeforeSideTurnEnd（来自状态 DoomPower）
- 描述：给予所有敌人{DoomPower:diff()}层[gold]灾厄[/gold]和{WeakPower:diff()}层[gold]虚弱[/gold]。

#### 死亡行军 `DEATH_MARCH` — 可直接实现

- 费用 1 · attack · 目标 any_enemy
- 我们识别的效果：DealDamage
- 额外钩子：Calculated
- 描述：造成{CalculatedDamage:diff()}点伤害。 / 你在回合进行中每抽到一张牌，都会使其额外造成{ExtraDamage:diff()}点伤害。

#### 洁净 `CLEANSE` — 可直接实现

- 费用 1 · skill · 目标 self
- 我们识别的效果：ExhaustCardsInPile、Summon
- 描述：[gold]召唤[/gold]{Summon:diff()}。 / 从你的[gold]抽牌堆[/gold]中选择一张牌将其[gold]消耗[/gold]。

#### 清淤 `DREDGE` — 可直接实现

- 费用 1 · skill · 目标 self · EXHAUST
- 我们识别的效果：DrawCards、MoveCardsBetweenPiles
- 描述：将{Cards:diff()}张牌从[gold]弃牌堆[/gold]加入你的[gold]手牌[/gold]。

#### 猛晃 `RATTLE` — 无法实现

- 费用 1 · attack · 目标 any_enemy
- 我们识别的效果：DealDamage
- **无法实现**：按战斗历史计数作为数值来源（超出我们支持的条目或筛选维度）
- 额外钩子：Calculated
- 描述：[gold]奥斯提[/gold]造成{OstyDamage:diff()}点伤害。 / 本回合每使用过一次奥斯提攻击牌，此牌就额外造成一次伤害。{InCombat: / （命中{CalculatedHits:diff()}次）|}

#### 碎骨 `BONE_SHARDS` — 无法实现

- 费用 1 · attack · 目标 all_enemies
- 我们识别的效果：DealDamage、GainBlock
- **无法实现**：CreatureCmd.Kill（直接击杀）
- 描述：如果[gold]奥斯提[/gold]存活，则他对所有敌人造成{OstyDamage:diff()}点伤害并且你获得{Block:diff()}点[gold]格挡[/gold]。 / 然后[gold]奥斯提[/gold]死去。

#### 紧追不放 `SIC_EM` — 实现方式不同

- 费用 1 · attack · 目标 any_enemy
- 我们识别的效果：ApplyPower、DealDamage
- 施加的状态：SicEmPower
- 额外钩子：AfterDamageGiven（来自状态 SicEmPower）
- 描述：[gold]奥斯提[/gold]造成{OstyDamage:diff()}点伤害。 / 在本回合内，每当[gold]奥斯提[/gold]攻击这名敌人时，[gold]召唤[/gold]{SicEmPower:diff()}。

#### 纠缠 `HAUNT` — 可直接实现

- 费用 1 · power · 目标 self
- 我们识别的效果：ApplyPower、LoseHp
- 施加的状态：HauntPower
- 描述：每当你打出一张[gold]灵魂[/gold]时，随机一名敌人失去{HpLoss:diff()}点生命。

#### 腐败 `PUTREFY` — 可直接实现

- 费用 1 · skill · 目标 any_enemy · EXHAUST
- 我们识别的效果：ApplyPower
- 施加的状态：VulnerablePower、WeakPower
- 描述：给予{Power:diff()}层[gold]虚弱[/gold]。 / 给予{Power:diff()}层[gold]易伤[/gold]。

#### 致死性 `LETHALITY` — 可直接实现

- 费用 1 · power · 目标 self · ETHEREAL
- 我们识别的效果：ApplyPower
- 施加的状态：LethalityPower
- 描述：每回合的第一张攻击牌会造成{LethalityPower:diff()}%额外伤害。

#### 血肉戏法 `SLEIGHT_OF_FLESH` — 实现方式不同

- 费用 2 · power · 目标 self
- 我们识别的效果：ApplyPower
- 施加的状态：SleightOfFleshPower
- 额外钩子：AfterPowerAmountChanged（来自状态 SleightOfFleshPower）
- 描述：每当你给予一个敌人负面状态时，使其受到{SleightOfFleshPower:diff()}点伤害。

#### 钙化 `CALCIFY` — 可直接实现

- 费用 1 · power · 目标 self
- 我们识别的效果：ApplyPower
- 施加的状态：CalcifyPower
- 描述：[gold]奥斯提[/gold]的攻击额外造成{CalcifyPower:diff()}点伤害。

#### 预借时间 `BORROWED_TIME` — 可直接实现

- 费用 1 · skill · 目标 self
- 我们识别的效果：ApplyPower、GainEnergy
- 施加的状态：BorrowedTimePower
- 描述：获得{Energy:energyIcons()}。 / 所有卡牌在本回合耗能增加{ExtraCost:energyIcons()}。

#### 领会 `PARSE` — 可直接实现

- 费用 1 · skill · 目标 self · ETHEREAL
- 我们识别的效果：DrawCards
- 描述：抽{Cards:diff()}张牌。

#### 骸骨军团 `LEGION_OF_BONE` — 可直接实现

- 费用 2 · skill · 目标 all_allies · EXHAUST
- 我们识别的效果：Summon
- 描述：所有玩家[gold]召唤[/gold]{Summon:diff()}。

### rare（26 张）

#### 不死 `UNDEATH` — 可直接实现

- 费用 0 · skill · 目标 self
- 我们识别的效果：AddNamedCardToPile、GainBlock
- 描述：获得{Block:diff()}点[gold]格挡[/gold]。 / 在[gold]弃牌堆[/gold]放入一张此牌的复制品。

#### 亡灵精通 `NECRO_MASTERY` — 实现方式不同

- 费用 2 · power · 目标 self
- 我们识别的效果：ApplyPower、Summon
- 施加的状态：NecroMasteryPower
- 额外钩子：AfterCurrentHpChanged（来自状态 NecroMasteryPower）
- 描述：[gold]召唤[/gold]{Summon:diff()}。 / 每当[gold]奥斯提[/gold]失去生命值时， / 所有敌人失去等量生命值。

#### 吊杀 `HANG` — 可直接实现

- 费用 1 · attack · 目标 any_enemy
- 我们识别的效果：ApplyPower、DealDamage
- 施加的状态：HangPower
- 描述：造成{Damage:diff()}点伤害。 / 让所有“吊杀”牌对这名敌人造成的伤害翻倍。

#### 吞噬生命 `DEVOUR_LIFE` — 可直接实现

- 费用 1 · power · 目标 self
- 我们识别的效果：ApplyPower
- 施加的状态：DevourLifePower
- 描述：每当你打出一张[gold]灵魂[/gold]时，[gold]召唤[/gold]{DevourLifePower:diff()}。

#### 命运同担 `SHARED_FATE` — 可直接实现

- 费用 0 · skill · 目标 any_enemy · EXHAUST
- 我们识别的效果：ApplyPower
- 施加的状态：StrengthPower
- 描述：失去{PlayerStrengthLoss:diff()}点[gold]力量[/gold]。 / 敌人失去{EnemyStrengthLoss:diff()}点[gold]力量[/gold]。

#### 哨卫模式 `SENTRY_MODE` — 实现方式不同

- 费用 2 · power · 目标 self
- 我们识别的效果：ApplyPower
- 施加的状态：SentryModePower
- 额外钩子：BeforeHandDraw（来自状态 SentryModePower）
- 描述：在你的回合开始时，将{SentryModePower:diff()}张[gold]扫荡凝视[/gold]加入你的[gold]手牌[/gold]。

#### 大限已至 `TIMES_UP` — 可直接实现

- 费用 2 · attack · 目标 any_enemy · EXHAUST
- 我们识别的效果：DealDamage
- 额外钩子：Calculated
- 描述：造成等量于该敌人身上的[gold]灾厄[/gold]层数的伤害。{IsTargeting: / （造成{CalculatedDamage:diff()}点伤害）|}

#### 女妖之嚎 `BANSHEES_CRY` — 可直接实现

- 费用 9 · attack · 目标 all_enemies
- 我们识别的效果：DealDamage、GainEnergy、ModifyCardCost
- 额外钩子：AfterCardEnteredCombat（≈我们的"打出一张牌时"，但战斗中生成的这张牌会少算之前打出的张数）、AfterCardPlayed（≈我们的"打出一张牌时"）
- 描述：对所有敌人造成{Damage:diff()}点伤害。 / 本场战斗中每打出过一张[gold]虚无[/gold]牌，此牌的耗能就减少{Energy:energyIcons()}。

#### 巨镰 `THE_SCYTHE` — 可直接实现

- 费用 2 · attack · 目标 any_enemy · EXHAUST
- 我们识别的效果：BoostCardDamage、DealDamage
- 描述：造成{Damage:diff()}点伤害。 / 这张牌在本局游戏中的伤害永久性增加{Increase:diff()}。

#### 幻景 `EIDOLON` — 实现方式不同

- 费用 2 · skill · 目标 self
- 我们识别的效果：ApplyPower、ExhaustCardsInPile
- 施加的状态：IntangiblePower
- 额外钩子：ExhaustKeyword、AfterModifyingDamageAmount（来自状态 IntangiblePower）、AfterModifyingHpLostAfterOsty（来自状态 IntangiblePower）、ModifyDamageCap（来自状态 IntangiblePower）、ModifyHpLostAfterOsty（来自状态 IntangiblePower）
- 描述：[gold]消耗[/gold]所有[gold]手牌[/gold]。 / 如果有9张牌被[gold]消耗[/gold]，则获得1层[gold]无实体[/gold]。

#### 彼岸一瞥 `GLIMPSE_BEYOND` — 可直接实现

- 费用 1 · skill · 目标 all_allies · EXHAUST
- 我们识别的效果：AddNamedCardToPile、DrawCards
- 描述：在所有玩家的[gold]抽牌堆[/gold]中加入{Cards:diff()}张[gold]灵魂[/gold]。

#### 末日降临 `END_OF_DAYS` — 实现方式不同

- 费用 3 · skill · 目标 all_enemies
- 我们识别的效果：ApplyPower
- 施加的状态：DoomPower
- 额外钩子：BeforeSideTurnEnd（来自状态 DoomPower）
- 描述：给予所有敌人{DoomPower:diff()}层[gold]灾厄[/gold]。 / 杀死所有[gold]灾厄[/gold]大于等于当前生命值的敌人。

#### 根除 `ERADICATE` — 可直接实现

- 费用 X · attack · 目标 any_enemy · RETAIN
- 我们识别的效果：DealDamage
- 描述：造成{Damage:diff()}点伤害X次。

#### 榨取 `SQUEEZE` — 可直接实现

- 费用 3 · attack · 目标 any_enemy
- 我们识别的效果：DealDamage
- 额外钩子：Calculated
- 描述：[gold]奥斯提[/gold]造成{CalculatedDamage:diff()}点伤害。 / 你每有一张[gold]奥斯提[/gold]的攻击牌，这张牌就额外造成{ExtraDamage:diff()}点伤害。

#### 死神形态 `REAPER_FORM` — 实现方式不同

- 费用 3 · power · 目标 self
- 我们识别的效果：ApplyPower
- 施加的状态：ReaperFormPower
- 额外钩子：AfterDamageGiven（来自状态 ReaperFormPower）
- 描述：每当你的攻击造成伤害时，同时给予等量的[gold]灾厄[/gold]。

#### 死者苏生 `REANIMATE` — 可直接实现

- 费用 3 · skill · 目标 self · EXHAUST
- 我们识别的效果：Summon
- 描述：[gold]召唤[/gold]{Summon:diff()}。

#### 湮灭 `OBLIVION` — 实现方式不同

- 费用 0 · skill · 目标 any_enemy
- 我们识别的效果：ApplyPower
- 施加的状态：OblivionPower
- 额外钩子：BeforeCardPlayed（来自状态 OblivionPower）
- 描述：打出此牌后，你在本回合内每打出一张牌，就给予该敌人{DoomPower:diff()}层[gold]灾厄[/gold]。

#### 灰烬之灵 `SPIRIT_OF_ASH` — 实现方式不同

- 费用 1 · power · 目标 self
- 我们识别的效果：ApplyPower
- 施加的状态：SpiritOfAshPower
- 额外钩子：BeforeCardPlayed（来自状态 SpiritOfAshPower）
- 描述：每当你打出一张[gold]虚无[/gold]牌时，获得{BlockOnExhaust:diff()}点[gold]格挡[/gold]。

#### 灵魂风暴 `SOUL_STORM` — 无法实现

- 费用 1 · attack · 目标 any_enemy
- 我们识别的效果：DealDamage
- **无法实现**：超出范围的按条件数牌（我们只支持标签/类型/关键词/星辰这几类条件）
- 额外钩子：Calculated
- 描述：造成{CalculatedDamage:diff()}点伤害。 / 你的[gold]消耗牌堆[/gold]中每有一张[gold]灵魂[/gold]，伤害增加{ExtraDamage:diff()}。

#### 牺牲 `SACRIFICE` — 无法实现

- 费用 1 · skill · 目标 self · RETAIN
- 我们识别的效果：GainBlock
- **无法实现**：CreatureCmd.Kill（直接击杀）
- 额外钩子：Calculated
- 数值来源：MaxHp
- 描述：如果[gold]奥斯提[/gold]存活，则他死去，然后你获得等量于其双倍最大生命值的[gold]格挡[/gold]。{InCombat: / （获得{IsOstyAlive:{CalculatedBlock:diff()}|0}点[gold]格挡[/gold]）|}

#### 精神过载 `NEUROSURGE` — 可直接实现

- 费用 0 · power · 目标 self
- 我们识别的效果：ApplyPower、DrawCards、GainEnergy
- 施加的状态：NeurosurgePower
- 描述：获得{Energy:energyIcons()}。 / 抽{Cards:diff()}张牌。 / 在你的回合开始时，给予自身{NeurosurgePower:diff()}层[gold]灾厄[/gold]。

#### 苦难 `MISERY` — 无法实现

- 费用 0 · attack · 目标 any_enemy
- 我们识别的效果：ApplyPower、DealDamage
- **无法实现**：PowerCmd.FindExistingInstanceForStacking（查找已有状态实例（辅助））、PowerCmd.ModifyAmount（直接改状态层数）
- 描述：造成{Damage:diff()}点伤害。 / 给予其他敌人该名敌人身上的所有负面效果。

#### 虚空之唤 `CALL_OF_THE_VOID` — 实现方式不同

- 费用 1 · power · 目标 self
- 我们识别的效果：ApplyPower、DrawCards
- 施加的状态：CallOfTheVoidPower
- 额外钩子：BeforeHandDraw（来自状态 CallOfTheVoidPower）
- 描述：在你的回合开始时，将{Cards:diff()}张随机牌添加到你的[gold]手牌[/gold]中。添加的牌会获得[gold]虚无[/gold]。

#### 重构 `TRANSFIGURE` — 可直接实现

- 费用 1 · skill · 目标 self · EXHAUST
- 我们识别的效果：GainEnergy、ModifyCardCost
- 描述：给一张[gold]手牌[/gold]添加[gold]重放[/gold]。 / 其耗能增加1{Energy:energyIcons()}。

#### 降灵 `SEANCE` — 可直接实现

- 费用 1 · skill · 目标 self · ETHEREAL
- 我们识别的效果：DrawCards、TransformCardsInPile
- 描述：将你[gold]抽牌堆[/gold]中的一张牌变化为[gold]灵魂[/gold]。

#### 领域 `DEMESNE` — 实现方式不同

- 费用 3 · power · 目标 self · ETHEREAL
- 我们识别的效果：ApplyPower、DrawCards、GainEnergy
- 施加的状态：DemesnePower
- 额外钩子：ModifyHandDraw（来自状态 DemesnePower）、ModifyMaxEnergy（来自状态 DemesnePower）
- 描述：在你的回合开始时，获得{Energy:energyIcons()}并额外多抽{Cards:diff()}张牌。

### ancient（2 张）

#### 守护者 `PROTECTOR` — 可直接实现

- 费用 1 · attack · 目标 any_enemy
- 我们识别的效果：DealDamage
- 额外钩子：Calculated
- 数值来源：MaxHp
- 描述：[gold]奥斯提[/gold]造成{CalculatedDamage:diff()}点伤害。 / 额外造成等量于[gold]奥斯提[/gold][gold]最大生命值[/gold]的伤害。

#### 禁忌魔典 `FORBIDDEN_GRIMOIRE` — 实现方式不同

- 费用 2 · power · 目标 self · ETERNAL
- 我们识别的效果：ApplyPower
- 施加的状态：ForbiddenGrimoirePower
- 额外钩子：AfterCombatEnd（来自状态 ForbiddenGrimoirePower）
- 描述：在战斗结束时，你可以从你的[gold]牌组[/gold]中选一张牌移除。

## regent（88 张）

### basic（4 张）

#### 崇拜 `VENERATE` — 可直接实现

- 费用 1 · skill · 目标 self
- 我们识别的效果：GainStars
- 描述：获得{Stars:starIcons()}。

#### 打击 `STRIKE_REGENT` — 可直接实现

- 费用 1 · attack · 目标 any_enemy
- 我们识别的效果：DealDamage
- 描述：造成{Damage:diff()}点伤害。

#### 防御 `DEFEND_REGENT` — 可直接实现

- 费用 1 · skill · 目标 self
- 我们识别的效果：GainBlock
- 描述：获得{Block:diff()}点[gold]格挡[/gold]。

#### 陨星 `FALLING_STAR` — 可直接实现

- 费用 0 · attack · 目标 any_enemy
- 我们识别的效果：ApplyPower、DealDamage
- 施加的状态：VulnerablePower、WeakPower
- 描述：造成{Damage:diff()}点伤害。 / 给予{WeakPower:diff()}层[gold]虚弱[/gold]。 / 给予{VulnerablePower:diff()}层[gold]易伤[/gold]。

### common（20 张）

#### 下去！ `BEGONE` — 可直接实现

- 费用 1 · skill · 目标 self
- 我们识别的效果：TransformCardsInPile、UpgradeCardsInPile
- 描述：选择你[gold]手牌[/gold]中的一张牌，将其[gold]变化[/gold]为[gold]{IfUpgraded:show:仆从打击+|仆从打击}[/gold]。

#### 下砸 `CRUSH_UNDER` — 可直接实现

- 费用 1 · attack · 目标 all_enemies
- 我们识别的效果：ApplyPower、DealDamage
- 施加的状态：CrushUnderPower
- 描述：对所有敌人造成{Damage:diff()}点伤害。所有敌人在本回合失去{StrengthLoss:diff()}点[gold]力量[/gold]。

#### 何人僭越 `KNOW_THY_PLACE` — 可直接实现

- 费用 0 · skill · 目标 any_enemy · EXHAUST
- 我们识别的效果：ApplyPower
- 施加的状态：VulnerablePower、WeakPower
- 描述：给予{WeakPower:diff()}层[gold]虚弱[/gold]。 / 给予{VulnerablePower:diff()}层[gold]易伤[/gold]。

#### 光子切割 `PHOTON_CUT` — 可直接实现

- 费用 1 · attack · 目标 any_enemy
- 我们识别的效果：DealDamage、DrawCards、MoveCardsBetweenPiles
- 描述：造成{Damage:diff()}点伤害。 / 抽{Cards:diff()}张牌。 / 将[gold]手牌[/gold]中的一张牌放到[gold]抽牌堆[/gold]顶部。

#### 天穹之力 `CELESTIAL_MIGHT` — 可直接实现

- 费用 2 · attack · 目标 any_enemy
- 我们识别的效果：DealDamage
- 额外钩子：Repeat
- 描述：造成{Damage:diff()}点伤害{Repeat:diff()}次。

#### 太阳打击 `SOLAR_STRIKE` — 可直接实现

- 费用 1 · attack · 目标 any_enemy
- 我们识别的效果：DealDamage、GainStars
- 描述：造成{Damage:diff()}点伤害。 / 获得{Stars:starIcons()}。

#### 宇宙冷漠 `COSMIC_INDIFFERENCE` — 可直接实现

- 费用 1 · skill · 目标 self
- 我们识别的效果：GainBlock、MoveCardsBetweenPiles
- 描述：获得{Block:diff()}点[gold]格挡[/gold]。 / 将[gold]弃牌堆[/gold]中的一张牌放到你的[gold]抽牌堆[/gold]的顶部。

#### 引导之星 `GUIDING_STAR` — 可直接实现

- 费用 1 · attack · 目标 any_enemy
- 我们识别的效果：DealDamage、DrawCards
- 描述：造成{Damage:diff()}点伤害。 / 抽{Cards:diff()}张牌。

#### 战利品 `SPOILS_OF_BATTLE` — 可直接实现

- 费用 1 · skill · 目标 self
- 我们识别的效果：DrawCards、Forge
- 额外钩子：Forge
- 描述：[gold]铸造[/gold]{Forge:diff()}。 / 抽{Cards:diff()}张牌。

#### 战火铸就 `WROUGHT_IN_WAR` — 可直接实现

- 费用 1 · attack · 目标 any_enemy
- 我们识别的效果：DealDamage、Forge
- 额外钩子：Forge
- 描述：造成{Damage:diff()}点伤害。 / [gold]铸造[/gold]{Forge:diff()}。

#### 收集光辉 `GATHER_LIGHT` — 可直接实现

- 费用 1 · skill · 目标 self
- 我们识别的效果：GainBlock、GainStars
- 描述：获得{Block:diff()}点[gold]格挡[/gold]。 / 获得{Stars:starIcons()}。

#### 新月长矛 `CRESCENT_SPEAR` — 可直接实现

- 费用 1 · attack · 目标 any_enemy
- 我们识别的效果：DealDamage
- 额外钩子：Calculated
- 描述：造成{CalculatedDamage:diff()}点伤害。 / 你每有一张拥有{singleStarIcon}耗能的卡牌，这张牌就额外造成{ExtraDamage:diff()}点伤害。

#### 星星点点 `PATTER` — 实现方式不同

- 费用 1 · skill · 目标 self
- 我们识别的效果：ApplyPower、GainBlock
- 施加的状态：VigorPower
- 额外钩子：AfterAttack（来自状态 VigorPower）、BeforeAttack（来自状态 VigorPower）
- 描述：获得{Block:diff()}点[gold]格挡[/gold]。 / 获得{VigorPower:diff()}点[gold]活力[/gold]。

#### 星界脉冲 `ASTRAL_PULSE` — 可直接实现

- 费用 0 · attack · 目标 all_enemies
- 我们识别的效果：DealDamage
- 描述：对所有敌人造成{Damage:diff()}点伤害两次。

#### 流光溢彩 `GLITTERSTREAM` — 实现方式不同

- 费用 2 · skill · 目标 self
- 我们识别的效果：ApplyPower、GainBlock
- 施加的状态：BlockNextTurnPower
- 额外钩子：AfterBlockCleared（来自状态 BlockNextTurnPower）
- 描述：获得{Block:diff()}点[gold]格挡[/gold]。 / 在下一回合获得{BlockNextTurn:diff()}点[gold]格挡[/gold]。

#### 淬炼刀刃 `REFINE_BLADE` — 实现方式不同

- 费用 1 · skill · 目标 self
- 我们识别的效果：ApplyPower、Forge、GainEnergy
- 施加的状态：EnergyNextTurnPower
- 额外钩子：Forge、AfterEnergyReset（来自状态 EnergyNextTurnPower）
- 描述：[gold]铸造[/gold]{Forge:diff()}。 / 在下个回合获得{Energy:energyIcons()}。

#### 碰撞轨迹 `COLLISION_COURSE` — 可直接实现

- 费用 0 · attack · 目标 any_enemy
- 我们识别的效果：AddNamedCardToPile、DealDamage
- 描述：造成{Damage:diff()}点伤害。 / 将一张[gold]碎屑[/gold]添加至你的[gold]手牌[/gold]。

#### 群星斗篷 `CLOAK_OF_STARS` — 可直接实现

- 费用 0 · skill · 目标 self
- 我们识别的效果：GainBlock
- 描述：获得{Block:diff()}点[gold]格挡[/gold]。

#### 辉光 `GLOW` — 实现方式不同

- 费用 1 · skill · 目标 self
- 我们识别的效果：ApplyPower、DrawCards、GainStars
- 施加的状态：DrawCardsNextTurnPower
- 额外钩子：ModifyHandDraw（来自状态 DrawCardsNextTurnPower）
- 描述：获得{Stars:starIcons()}。 / 抽{Cards:diff()}张牌。 / 下一回合抽{Cards:diff()}张牌{Cards:plural:|}。

#### 隐秘藏品 `HIDDEN_CACHE` — 实现方式不同

- 费用 1 · skill · 目标 self
- 我们识别的效果：ApplyPower、GainStars
- 施加的状态：StarNextTurnPower
- 额外钩子：AfterEnergyReset（来自状态 StarNextTurnPower）
- 描述：获得{Stars:starIcons()}。 / 在下个回合获得{StarNextTurnPower:starIcons()}。

### uncommon（36 张）

#### 伽马爆破 `GAMMA_BLAST` — 可直接实现

- 费用 0 · attack · 目标 any_enemy
- 我们识别的效果：ApplyPower、DealDamage
- 施加的状态：VulnerablePower、WeakPower
- 描述：造成{Damage:diff()}点伤害。 / 给予{WeakPower:diff()}层[gold]虚弱[/gold]。 / 给予{VulnerablePower:diff()}层[gold]易伤[/gold]。

#### 倒映 `REFLECT` — 可直接实现

- 费用 1 · skill · 目标 self
- 我们识别的效果：ApplyPower、GainBlock
- 施加的状态：ReflectPower
- 描述：获得{Block:diff()}点[gold]格挡[/gold]。 / 在本回合将你格挡掉的攻击伤害反弹给攻击者。

#### 光谱偏移 `SPECTRUM_SHIFT` — 实现方式不同

- 费用 2 · power · 目标 self
- 我们识别的效果：ApplyPower、DrawCards
- 施加的状态：SpectrumShiftPower
- 额外钩子：BeforeHandDraw（来自状态 SpectrumShiftPower）
- 描述：在你的回合开始时，将{Cards:diff()}张随机无色牌添加到你的[gold]手牌[/gold]中。

#### 共鸣 `RESONANCE` — 可直接实现

- 费用 1 · skill · 目标 all_enemies
- 我们识别的效果：ApplyPower
- 施加的状态：StrengthPower
- 描述：获得{StrengthPower:diff()}点[gold]力量[/gold]。所有敌人失去1点[gold]力量[/gold]。

#### 冲锋！！ `CHARGE` — 可直接实现

- 费用 1 · skill · 目标 self
- 我们识别的效果：DrawCards、TransformCardsInPile、UpgradeCardsInPile
- 描述：选择你[gold]抽牌堆[/gold]中的{Cards:diff()}张牌，将其[gold]变化[/gold]为 / [gold]{IfUpgraded:show:仆从俯冲+|仆从俯冲}[/gold]。

#### 决胜一击 `KNOCKOUT_BLOW` — 可直接实现

- 费用 3 · attack · 目标 any_enemy
- 我们识别的效果：DealDamage、GainStars
- 描述：造成{Damage:diff()}点伤害。 / 如果此牌击杀了敌人，获得{Stars:starIcons()}。

#### 创世之柱 `PILLAR_OF_CREATION` — 实现方式不同

- 费用 1 · power · 目标 self
- 我们识别的效果：ApplyPower、GainBlock
- 施加的状态：PillarOfCreationPower
- 额外钩子：AfterCardGeneratedForCombat（来自状态 PillarOfCreationPower）
- 描述：每当你生成一张卡牌, 就获得{Block:diff()}点[gold]格挡[/gold]。

#### 君权自授 `MANIFEST_AUTHORITY` — 可直接实现

- 费用 1 · skill · 目标 self
- 我们识别的效果：AddNamedCardToPile、GainBlock、UpgradeCardsInPile
- 描述：获得{Block:diff()}点[gold]格挡[/gold]。 / 将一张随机{IfUpgraded:show:[gold]升级过的[/gold]}无色牌加入你的[gold]手牌[/gold]。

#### 地形改造 `TERRAFORMING` — 实现方式不同

- 费用 1 · skill · 目标 self
- 我们识别的效果：ApplyPower
- 施加的状态：VigorPower
- 额外钩子：AfterAttack（来自状态 VigorPower）、BeforeAttack（来自状态 VigorPower）
- 描述：获得{VigorPower:diff()}点[gold]活力[/gold]。

#### 征召上前 `SUMMON_FORTH` — 可直接实现

- 费用 1 · skill · 目标 self
- 我们识别的效果：Forge、MoveCardsBetweenPiles
- 额外钩子：Forge
- 描述：不论何处，将[gold]君王之剑[/gold]放入你的[gold]手牌[/gold]。 / [gold]铸造[/gold]{Forge:diff()}。

#### 征服者 `CONQUEROR` — 可直接实现

- 费用 1 · skill · 目标 any_enemy
- 我们识别的效果：ApplyPower、Forge
- 施加的状态：ConquerorPower
- 额外钩子：Forge
- 描述：[gold]铸造[/gold]{Forge:diff()}。 / [gold]君王之剑[/gold]在本回合对敌人造成双倍伤害。

#### 微光 `GLIMMER` — 可直接实现

- 费用 1 · skill · 目标 self
- 我们识别的效果：DrawCards、MoveCardsBetweenPiles
- 描述：抽{Cards:diff()}张牌。 / 将你[gold]手牌[/gold]中的{PutBack:diff()}张牌放到[gold]抽牌堆[/gold]顶部。

#### 慷慨捐助 `LARGESSE` — 可直接实现

- 费用 0 · skill · 目标 any_ally
- 我们识别的效果：AddNamedCardToPile、UpgradeCardsInPile
- 描述：将一张随机的{IfUpgraded:show:[gold]升级过的[/gold]}无色牌添加至一位其他玩家的[gold]手牌[/gold]中。

#### 招架 `PARRY` — 可直接实现

- 费用 1 · power · 目标 self
- 我们识别的效果：ApplyPower
- 施加的状态：ParryPower
- 描述：[gold]君王之剑[/gold]现在能让你获得{ParryPower:diff()}点[gold]格挡[/gold]。

#### 明耀打击 `SHINING_STRIKE` — 可直接实现

- 费用 1 · attack · 目标 any_enemy
- 我们识别的效果：DealDamage、GainStars、MoveCardsBetweenPiles
- 描述：造成{Damage:diff()}点伤害。 / 获得{Stars:starIcons()}。 / 将这张牌放置于你的[gold]抽牌堆[/gold]顶部。

#### 星位序列 `ALIGNMENT` — 可直接实现

- 费用 0 · skill · 目标 self
- 我们识别的效果：GainEnergy
- 描述：获得{Energy:energyIcons()}。

#### 星尘 `STARDUST` — 可直接实现

- 费用 0 · attack · 目标 random_enemy
- 我们识别的效果：DealDamage
- 描述：随机对敌人造成{Damage:diff()}点伤害X次。

#### 暗淡蓝点 `PALE_BLUE_DOT` — 实现方式不同

- 费用 1 · power · 目标 self
- 我们识别的效果：ApplyPower、DrawCards
- 施加的状态：PaleBlueDotPower
- 额外钩子：AfterModifyingHandDraw（来自状态 PaleBlueDotPower）、ModifyHandDraw（来自状态 PaleBlueDotPower）
- 描述：如果你在一回合内打出了大于等于{CardPlay}张牌，在下个回合开始时抽{Cards:diff()}张牌。

#### 月面射击 `LUNAR_BLAST` — 无法实现

- 费用 0 · attack · 目标 any_enemy
- 我们识别的效果：DealDamage
- **无法实现**：按战斗历史计数作为数值来源（超出我们支持的条目或筛选维度）
- 额外钩子：Calculated
- 描述：本回合中每打出过一张技能牌，此牌额外造成{Damage:diff()}点伤害一次。{InCombat: / （命中{CalculatedHits:diff()}次）|}

#### 汇流 `CONVERGENCE` — 实现方式不同

- 费用 1 · skill · 目标 self
- 我们识别的效果：ApplyPower、GainEnergy、GainStars
- 施加的状态：EnergyNextTurnPower、RetainHandPower、StarNextTurnPower
- 额外钩子：AfterEnergyReset（来自状态 EnergyNextTurnPower）、ShouldFlush（来自状态 RetainHandPower）、AfterEnergyReset（来自状态 StarNextTurnPower）
- 描述：在本回合[gold]保留[/gold]你的[gold]手牌[/gold]。 / 在下个回合， / 获得{Energy:energyIcons()}与{Stars:starIcons()}。

#### 熔炉 `FURNACE` — 可直接实现

- 费用 1 · power · 目标 self
- 我们识别的效果：ApplyPower
- 施加的状态：FurnacePower
- 额外钩子：Forge
- 描述：在你的回合开始时，[gold]铸造[/gold]{Forge:diff()}。

#### 独白 `MONOLOGUE` — 实现方式不同

- 费用 0 · skill · 目标 self
- 我们识别的效果：ApplyPower
- 施加的状态：MonologuePower
- 额外钩子：BeforeCardPlayed（来自状态 MonologuePower）
- 描述：每当你在本回合打出卡牌时，在本回合获得{Power:diff()}点[gold]力量[/gold]。

#### 王者之拳 `KINGLY_PUNCH` — 可直接实现

- 费用 1 · attack · 目标 any_enemy
- 我们识别的效果：BoostCardDamage、DealDamage
- 额外钩子：AfterCardDrawn
- 描述：造成{Damage:diff()}点伤害。 / 每当你抽到这张牌时，在这场战斗中其伤害增加{Increase:diff()}。

#### 王者之踢 `KINGLY_KICK` — 可直接实现

- 费用 4 · attack · 目标 any_enemy
- 我们识别的效果：DealDamage、ModifyCardCost
- 额外钩子：AfterCardDrawn
- 描述：造成{Damage:diff()}点伤害。 / 每当你抽到这张牌时，这张牌的耗能减少1。

#### 环绕轨道 `ORBIT` — 实现方式不同

- 费用 2 · power · 目标 self
- 我们识别的效果：ApplyPower、GainEnergy
- 施加的状态：OrbitPower
- 额外钩子：AfterEnergySpent（来自状态 OrbitPower）
- 描述：你每花费{energyPrefix:energyIcons(4)}， / 就获得{Energy:energyIcons()}。

#### 类星体 `QUASAR` — 可直接实现

- 费用 0 · skill · 目标 self
- 我们识别的效果：AddNamedCardToPile、UpgradeCardsInPile
- 描述：从3张随机{IfUpgraded:show:[gold]升级过[/gold]的|}无色牌中选择1张加入你的[gold]手牌[/gold]。

#### 粒子墙 `PARTICLE_WALL` — 可直接实现

- 费用 0 · skill · 目标 self
- 我们识别的效果：GainBlock
- 描述：获得{Block:diff()}点[gold]格挡[/gold]。 / 将此牌返回你的[gold]手牌[/gold]。

#### 群星之子 `CHILD_OF_THE_STARS` — 实现方式不同

- 费用 1 · power · 目标 self
- 我们识别的效果：ApplyPower
- 施加的状态：ChildOfTheStarsPower
- 额外钩子：AfterStarsSpent（来自状态 ChildOfTheStarsPower）
- 描述：每当你花费{singleStarIcon}时，每花费一点{singleStarIcon}，获得{BlockForStars:diff()}点[gold]格挡[/gold]。

#### 胜券在王 `ROYAL_GAMBLE` — 可直接实现

- 费用 0 · skill · 目标 self · EXHAUST
- 我们识别的效果：GainStars
- 描述：获得{Stars:diff()}{singleStarIcon}。

#### 葬送 `DEVASTATE` — 可直接实现

- 费用 1 · attack · 目标 any_enemy
- 我们识别的效果：DealDamage
- 描述：造成{Damage:diff()}点伤害。

#### 超质量体 `SUPERMASSIVE` — 可直接实现

- 费用 1 · attack · 目标 any_enemy
- 我们识别的效果：DealDamage
- 额外钩子：Calculated
- 描述：造成{CalculatedDamage:diff()}点伤害。 / 你在本场战斗中每生成过一张牌，这张牌就额外造成{ExtraDamage:diff()}点伤害。

#### 辐射 `RADIATE` — 无法实现

- 费用 0 · attack · 目标 all_enemies
- 我们识别的效果：DealDamage、GainStars
- **无法实现**：按战斗历史计数作为数值来源（超出我们支持的条目或筛选维度）
- 额外钩子：Calculated
- 描述：你在本回合每获得一点{Stars:starIcons()}，此牌就对所有敌人造成{Damage:diff()}点伤害一次。{InCombat: / （命中{CalculatedHits:diff()}次）|}

#### 铸墙 `BULWARK` — 可直接实现

- 费用 2 · skill · 目标 self
- 我们识别的效果：Forge、GainBlock
- 额外钩子：Forge
- 描述：获得{Block:diff()}点[gold]格挡[/gold]。 / [gold]铸造[/gold]{Forge:diff()}。

#### 霸权 `HEGEMONY` — 实现方式不同

- 费用 2 · attack · 目标 any_enemy
- 我们识别的效果：ApplyPower、DealDamage、GainEnergy
- 施加的状态：EnergyNextTurnPower
- 额外钩子：AfterEnergyReset（来自状态 EnergyNextTurnPower）
- 描述：造成{Damage:diff()}点伤害。 / 在下个回合获得{Energy:energyIcons()}。

#### 预言 `PROPHESIZE` — 可直接实现

- 费用 2 · skill · 目标 self
- 我们识别的效果：DrawCards
- 描述：抽{Cards:diff()}张牌。

#### 黑洞 `BLACK_HOLE` — 实现方式不同

- 费用 1 · power · 目标 self
- 我们识别的效果：ApplyPower
- 施加的状态：BlackHolePower
- 额外钩子：AfterStarsGained（来自状态 BlackHolePower）
- 描述：每当你花费或获得{singleStarIcon}时，对所有敌人造成{BlackHolePower:diff()}点伤害。

### rare（26 张）

#### 七星 `SEVEN_STARS` — 可直接实现

- 费用 2 · attack · 目标 all_enemies
- 我们识别的效果：DealDamage
- 额外钩子：Repeat
- 描述：对所有敌人造成{Damage:diff()}点伤害{Repeat:diff()}次。

#### 中子护盾 `NEUTRON_AEGIS` — 实现方式不同

- 费用 1 · power · 目标 self
- 我们识别的效果：ApplyPower
- 施加的状态：PlatingPower
- 额外钩子：AfterApplied（来自状态 PlatingPower）、BeforeSideTurnEndEarly（来自状态 PlatingPower）
- 描述：获得{PlatingPower:diff()}层[gold]覆甲[/gold]。

#### 传承之锤 `HEIRLOOM_HAMMER` — 可直接实现

- 费用 2 · attack · 目标 any_enemy
- 我们识别的效果：AddNamedCardToPile、DealDamage
- 额外钩子：Repeat
- 描述：造成{Damage:diff()}点伤害。 / 选择你[gold]手牌[/gold]中的一张无色牌。将这张牌的一张复制品放入你的[gold]手牌[/gold]。

#### 创世纪 `GENESIS` — 实现方式不同

- 费用 2 · power · 目标 self
- 我们识别的效果：ApplyPower
- 施加的状态：GenesisPower
- 额外钩子：AfterEnergyReset（来自状态 GenesisPower）
- 描述：在你的回合开始时，获得{StarsPerTurn:starIcons()}。

#### 剑圣 `SWORD_SAGE` — 实现方式不同

- 费用 2 · power · 目标 self
- 我们识别的效果：ApplyPower
- 施加的状态：SwordSagePower
- 额外钩子：AfterPowerAmountChanged（来自状态 SwordSagePower）、AfterRemoved（来自状态 SwordSagePower）
- 描述：[gold]君王之剑[/gold]获得[gold]重放[/gold]{SwordSagePower:diff()}。

#### 大爆炸 `BIG_BANG` — 可直接实现

- 费用 0 · skill · 目标 self · EXHAUST
- 我们识别的效果：DrawCards、Forge、GainEnergy、GainStars
- 额外钩子：Forge
- 描述：抽{Cards:diff()}张牌。 / 获得{Energy:energyIcons()}。 / 获得{Stars:starIcons()}。 / [gold]铸造[/gold]{Forge:diff()}。

#### 天际钻头 `HEAVENLY_DRILL` — 可直接实现

- 费用 X · attack · 目标 any_enemy
- 我们识别的效果：DealDamage、GainEnergy
- 描述：造成{Damage:diff()}点伤害X次。 / 如果X的最终数值为{Energy:diff()}或以上，则将X的数值翻倍。

#### 如此甚好 `MAKE_IT_SO` — 可直接实现

- 费用 0 · attack · 目标 any_enemy
- 我们识别的效果：DealDamage、DrawCards、MoveCardsBetweenPiles
- 额外钩子：AfterCardPlayedLate（≈我们的"打出一张牌时"）
- 描述：造成{Damage:diff()}点伤害。 / 你每在一回合内打出{Cards:diff()}张技能牌，就将这张牌放入你的[gold]手牌[/gold]。

#### 彗星 `COMET` — 可直接实现

- 费用 0 · attack · 目标 any_enemy
- 我们识别的效果：ApplyPower、DealDamage
- 施加的状态：VulnerablePower、WeakPower
- 描述：造成{Damage:diff()}点伤害。 / 给予{WeakPower:diff()}层[gold]虚弱[/gold]。 / 给予{VulnerablePower:diff()}层[gold]易伤[/gold]。

#### 所向无敌 `I_AM_INVINCIBLE` — 可直接实现

- 费用 1 · skill · 目标 self
- 我们识别的效果：AutoPlay、GainBlock
- 额外钩子：AfterAutoPostPlayPhaseEntered
- 描述：获得{Block:diff()}点[gold]格挡[/gold]。 / 在你的回合结束时，如果这张牌位于[gold]抽牌堆[/gold]顶部，则将其打出。

#### 抉择，抉择 `DECISIONS_DECISIONS` — 可直接实现

- 费用 0 · skill · 目标 self · EXHAUST
- 我们识别的效果：AutoPlay、DrawCards
- 额外钩子：Repeat
- 描述：抽{Cards:diff()}张牌。 / 选择你[gold]手牌[/gold]中的一张技能牌，并将其打出{Repeat:diff()}次。

#### 护驾！！！ `GUARDS` — 可直接实现

- 费用 2 · skill · 目标 self · EXHAUST
- 我们识别的效果：TransformCardsInPile、UpgradeCardsInPile
- 描述：将你[gold]手牌[/gold]中的任意张牌[gold]变化[/gold]为[gold]{IfUpgraded:show:仆从捐躯+|仆从捐躯}[/gold]。

#### 新生之喜 `BUNDLE_OF_JOY` — 可直接实现

- 费用 1 · skill · 目标 self · EXHAUST
- 我们识别的效果：AddNamedCardToPile、DrawCards
- 描述：将{Cards:diff()}张随机无色牌添加到你的[gold]手牌[/gold]。

#### 既定事项 `FOREGONE_CONCLUSION` — 实现方式不同

- 费用 1 · skill · 目标 self
- 我们识别的效果：ApplyPower、DrawCards
- 施加的状态：ForegoneConclusionPower
- 额外钩子：BeforeHandDraw（来自状态 ForegoneConclusionPower）
- 描述：在下个回合，从你的[gold]抽牌堆[/gold]中选择{Cards:diff()}张牌放入你的[gold]手牌[/gold]。

#### 星灭 `DYING_STAR` — 可直接实现

- 费用 1 · attack · 目标 all_enemies · ETHEREAL
- 我们识别的效果：ApplyPower、DealDamage
- 施加的状态：DyingStarPower
- 描述：对所有敌人造成{Damage:diff()}点伤害。所有敌人在本回合中失去{StrengthLoss:diff()}点[gold]力量[/gold]。

#### 暴政 `TYRANNY` — 实现方式不同

- 费用 1 · power · 目标 self
- 我们识别的效果：ApplyPower
- 施加的状态：TyrannyPower
- 额外钩子：ModifyHandDraw（来自状态 TyrannyPower）
- 描述：在你的回合开始时，抽一张牌，并从你的[gold]手牌[/gold]中[gold]消耗[/gold]1张牌。

#### 武器库 `ARSENAL` — 实现方式不同

- 费用 1 · power · 目标 self
- 我们识别的效果：ApplyPower
- 施加的状态：ArsenalPower
- 额外钩子：AfterCardGeneratedForCombat（来自状态 ArsenalPower）
- 描述：每当你生成一张牌，就获得{ArsenalPower:diff()}点[gold]力量[/gold]。

#### 王之凝视 `MONARCHS_GAZE` — 实现方式不同

- 费用 2 · power · 目标 self
- 我们识别的效果：ApplyPower
- 施加的状态：MonarchsGazePower
- 额外钩子：AfterDamageGiven（来自状态 MonarchsGazePower）
- 描述：每当你攻击敌人的时候，这名敌人在本回合失去{StrengthLoss:diff()}点[gold]力量[/gold]。

#### 王国资产 `ROYALTIES` — 实现方式不同

- 费用 1 · power · 目标 self
- 我们识别的效果：ApplyPower
- 施加的状态：RoyaltiesPower
- 额外钩子：AfterCombatEnd（来自状态 RoyaltiesPower）
- 描述：在战斗结束时，获得{Gold:diff()}[gold]金币[/gold]。

#### 虚空形态 `VOID_FORM` — 无法实现

- 费用 3 · power · 目标 self · ETHEREAL
- 我们识别的效果：ApplyPower
- **无法实现**：PlayerCmd.EndTurn（立刻结束回合）
- 施加的状态：VoidFormPower
- 额外钩子：BeforeApplied（来自状态 VoidFormPower）、BeforePowerAmountChanged（来自状态 VoidFormPower）
- 描述：结束你的回合。 / 你可以免费打出每回合的前{VoidFormPower:diff()}张牌。

#### 轰击 `BOMBARDMENT` — 可直接实现

- 费用 3 · attack · 目标 any_enemy · EXHAUST
- 我们识别的效果：AutoPlay、DealDamage
- 额外钩子：AfterAutoPrePlayPhaseEnteredEarly
- 描述：造成{Damage:diff()}点伤害。 / 你的回合开始时，如果这张牌在你的[gold]消耗牌堆[/gold]中，则将其打出。

#### 迫降 `CRASH_LANDING` — 可直接实现

- 费用 1 · attack · 目标 all_enemies
- 我们识别的效果：AddNamedCardToPile、DealDamage
- 描述：对所有敌人造成{Damage:diff()}点伤害。 / 用[gold]碎屑[/gold]填满你的[gold]手牌[/gold]。

#### 追踪之刃 `SEEKING_EDGE` — 可直接实现

- 费用 1 · power · 目标 self
- 我们识别的效果：ApplyPower、Forge
- 施加的状态：SeekingEdgePower
- 额外钩子：Forge
- 描述：[gold]铸造[/gold]{Forge:diff()}。 / [gold]君王之剑[/gold]现在会对所有敌人造成伤害。

#### 铸剑者 `THE_SMITH` — 可直接实现

- 费用 1 · skill · 目标 self
- 我们识别的效果：Forge
- 额外钩子：Forge
- 描述：[gold]铸造[/gold]{Forge:diff()}。

#### 锤子时间 `HAMMER_TIME` — 实现方式不同

- 费用 2 · power · 目标 self
- 我们识别的效果：ApplyPower
- 施加的状态：HammerTimePower
- 额外钩子：AfterForge（来自状态 HammerTimePower）
- 描述：每当你[gold]铸造[/gold]时，所有盟友也都[gold]铸造[/gold]相同的数值。

#### 锻打成型 `BEAT_INTO_SHAPE` — 无法实现

- 费用 1 · attack · 目标 any_enemy
- 我们识别的效果：DealDamage、Forge
- **无法实现**：按战斗历史计数作为数值来源（超出我们支持的条目或筛选维度）
- 额外钩子：Calculated
- 描述：造成{Damage:diff()}点伤害。 / [gold]铸造[/gold]{CalculatedForge:diff()}。 / 本回合内，你此前每击中过该敌人一次，这张牌就额外[gold]铸造[/gold]{CalculationExtra:diff()}点。

### ancient（2 张）

#### 封印王座 `THE_SEALED_THRONE` — 实现方式不同

- 费用 1 · power · 目标 self
- 我们识别的效果：ApplyPower
- 施加的状态：TheSealedThronePower
- 额外钩子：BeforeCardPlayed（来自状态 TheSealedThronePower）
- 描述：你每打出一张牌，获得{singleStarIcon}。

#### 流星雨 `METEOR_SHOWER` — 可直接实现

- 费用 0 · attack · 目标 all_enemies
- 我们识别的效果：ApplyPower、DealDamage
- 施加的状态：VulnerablePower、WeakPower
- 描述：对所有敌人造成{Damage:diff()}点伤害。 / 给予所有敌人{WeakPower:diff()}层[gold]虚弱[/gold]和[gold]易伤[/gold]。

## colorless（64 张）

### uncommon（39 张）

#### 万向斩 `OMNISLICE` — 可直接实现

- 费用 0 · attack · 目标 any_enemy
- 我们识别的效果：DealDamage
- 描述：造成{Damage:diff()}点伤害。 / 对所有其他敌人造成等量的伤害。

#### 亮剑 `FLASH_OF_STEEL` — 可直接实现

- 费用 0 · attack · 目标 any_enemy
- 我们识别的效果：DealDamage、DrawCards
- 描述：造成{Damage:diff()}点伤害。 / 抽1张牌。

#### 净化 `PURITY` — 可直接实现

- 费用 0 · skill · 目标 self · RETAIN/EXHAUST
- 我们识别的效果：DrawCards、ExhaustCardsInPile
- 描述：从[gold]手牌[/gold]中选择最多{Cards:diff()}张牌消耗。

#### 准备时间 `PREP_TIME` — 可直接实现

- 费用 1 · power · 目标 self
- 我们识别的效果：ApplyPower
- 施加的状态：PrepTimePower
- 描述：在你的回合开始时，获得{PrepTimePower:diff()}点[gold]活力[/gold]。

#### 勒紧 `FASTEN` — 实现方式不同

- 费用 1 · power · 目标 self
- 我们识别的效果：ApplyPower
- 施加的状态：FastenPower
- 额外钩子：AfterModifyingBlockAmount（来自状态 FastenPower）、ModifyBlockAdditive（来自状态 FastenPower）
- 描述：从“防御”牌中额外获得{ExtraBlock:diff()}点[gold]格挡[/gold]。

#### 协同配合 `COORDINATE` — 可直接实现

- 费用 1 · skill · 目标 any_ally
- 我们识别的效果：ApplyPower
- 施加的状态：CoordinatePower
- 描述：在本回合给予其他玩家{StrengthPower:diff()}点[gold]力量[/gold]。

#### 双打组合 `TAG_TEAM` — 实现方式不同

- 费用 2 · attack · 目标 any_enemy
- 我们识别的效果：ApplyPower、DealDamage
- 施加的状态：TagTeamPower
- 额外钩子：AfterModifyingCardPlayCount（来自状态 TagTeamPower）、ModifyCardPlayCount（来自状态 TagTeamPower）
- 描述：造成{Damage:diff()}点伤害。 / 其他玩家的下一张攻击牌将在该名敌人身上额外生效一次。

#### 发现 `DISCOVERY` — 可直接实现

- 费用 1 · skill · 目标 self · EXHAUST
- 我们识别的效果：AddNamedCardToPile、SetCardsFree
- 描述：从3张随机牌中选择1张加入你的[gold]手牌[/gold]。这张牌在本回合可以免费打出。

#### 均衡 `EQUILIBRIUM` — 实现方式不同

- 费用 2 · skill · 目标 self
- 我们识别的效果：ApplyPower、GainBlock
- 施加的状态：RetainHandPower
- 额外钩子：ShouldFlush（来自状态 RetainHandPower）
- 描述：获得{Block:diff()}点[gold]格挡[/gold]。 / 在本回合[gold]保留[/gold]你的[gold]手牌[/gold]。

#### 妙计 `FINESSE` — 可直接实现

- 费用 0 · skill · 目标 self
- 我们识别的效果：DrawCards、GainBlock
- 描述：获得{Block:diff()}点[gold]格挡[/gold]。 / 抽1张牌。

#### 应急按钮 `PANIC_BUTTON` — 实现方式不同

- 费用 0 · skill · 目标 self · EXHAUST
- 我们识别的效果：ApplyPower、GainBlock
- 施加的状态：NoBlockPower
- 额外钩子：ModifyBlockMultiplicative（来自状态 NoBlockPower）
- 描述：获得{Block:diff()}点[gold]格挡[/gold]。 / 你在接下来的{Turns:diff()}回合内无法再从卡牌中获得[gold]格挡[/gold]。

#### 延伸 `PROLONG` — 实现方式不同

- 费用 0 · skill · 目标 self · EXHAUST
- 我们识别的效果：ApplyPower
- 施加的状态：BlockNextTurnPower
- 额外钩子：AfterBlockCleared（来自状态 BlockNextTurnPower）
- 描述：在下个回合获得等量于你当前[gold]格挡[/gold]值的[gold]格挡[/gold]。

#### 心灵震慑 `MIND_BLAST` — 可直接实现

- 费用 1 · attack · 目标 any_enemy · INNATE
- 我们识别的效果：DealDamage
- 额外钩子：Calculated
- 数值来源：DrawPileCount
- 描述：造成你[gold]抽牌堆[/gold]中剩余牌数的伤害。{InCombat: / （造成{CalculatedDamage:diff()}点伤害）|}

#### 心神不宁 `RESTLESSNESS` — 可直接实现

- 费用 0 · skill · 目标 self · RETAIN
- 我们识别的效果：DrawCards、GainEnergy
- 描述：如果你的[gold]手牌[/gold]为空，则抽{Cards:diff()}张牌并获得{Energy:energyIcons()}。

#### 急躁 `IMPATIENCE` — 可直接实现

- 费用 0 · skill · 目标 self
- 我们识别的效果：DrawCards
- 描述：如果你的[gold]手牌[/gold]中没有攻击牌，抽{Cards:diff()}张牌。

#### 托举 `LIFT` — 可直接实现

- 费用 1 · skill · 目标 any_ally
- 我们识别的效果：GainBlock
- 描述：给另一名玩家{Block:diff()}点[gold]格挡[/gold]。

#### 抱团 `HUDDLE_UP` — 可直接实现

- 费用 1 · skill · 目标 all_allies · EXHAUST
- 我们识别的效果：DrawCards
- 描述：所有玩家抽{Cards:diff()}张牌。

#### 拦截 `INTERCEPT` — 实现方式不同

- 费用 1 · skill · 目标 any_ally
- 我们识别的效果：ApplyPower、GainBlock
- 施加的状态：CoveredPower
- 额外钩子：AfterApplied（来自状态 CoveredPower）、AfterDeath（来自状态 CoveredPower）
- 描述：获得{Block:diff()}点[gold]格挡[/gold]。 / 将本回合所有要对另一名玩家发起的攻击转移到你的身上。

#### 拳斗 `FISTICUFFS` — 可直接实现

- 费用 1 · attack · 目标 any_enemy
- 我们识别的效果：DealDamage、GainBlock
- 描述：造成{Damage:diff()}点伤害。 / 获得等量于所造成伤害的[gold]格挡[/gold]。

#### 探寻打击 `SEEKER_STRIKE` — 可直接实现

- 费用 1 · attack · 目标 any_enemy
- 我们识别的效果：DealDamage、DrawCards、MoveCardsBetweenPiles
- 描述：造成{Damage:diff()}点伤害。 / 从[gold]抽牌堆[/gold]的随机{Cards:diff()}张牌中选择一张加入你的[gold]手牌[/gold]。

#### 无休手斧 `THRUMMING_HATCHET` — 实现方式不同

- 费用 1 · attack · 目标 any_enemy
- 我们识别的效果：DealDamage、MoveCardsBetweenPiles
- 额外钩子：BeforeHandDraw
- 描述：造成{Damage:diff()}点伤害。 / 在你的下个回合开始时，将此卡返回你的[gold]手牌[/gold]。

#### 横祸 `CATASTROPHE` — 可直接实现

- 费用 2 · skill · 目标 self
- 我们识别的效果：AutoPlay、DrawCards
- 描述：从你的[gold]抽牌堆[/gold]中随机打出{Cards:diff()}张牌。

#### 深谋远虑 `THINKING_AHEAD` — 可直接实现

- 费用 0 · skill · 目标 self · EXHAUST
- 我们识别的效果：DrawCards、MoveCardsBetweenPiles
- 描述：抽{Cards:diff()}张牌。 / 将[gold]手牌[/gold]中的一张牌放到你的[gold]抽牌堆[/gold]的顶端。

#### 炸弹 `THE_BOMB` — 实现方式不同

- 费用 2 · skill · 目标 self
- 我们识别的效果：ApplyPower
- 施加的状态：TheBombPower
- 额外钩子：BeforeSideTurnEnd（来自状态 TheBombPower）
- 描述：在{Turns:diff()}回合结束后，对所有敌人造成{BombDamage:diff()}点伤害。

#### 生产制造 `PRODUCTION` — 可直接实现

- 费用 0 · skill · 目标 self · EXHAUST
- 我们识别的效果：GainEnergy
- 描述：获得{Energy:energyIcons()}。

#### 相信着你 `BELIEVE_IN_YOU` — 可直接实现

- 费用 0 · skill · 目标 any_ally
- 我们识别的效果：GainEnergy
- 描述：另一名玩家获得{Energy:energyIcons()}。

#### 神气制胜 `PANACHE` — 可直接实现

- 费用 0 · power · 目标 self
- 我们识别的效果：ApplyPower
- 施加的状态：PanachePower
- 描述：每当你在一回合内打出五张牌时，对所有敌人造成{PanacheDamage:diff()}点伤害。

#### 究极打击 `ULTIMATE_STRIKE` — 可直接实现

- 费用 1 · attack · 目标 any_enemy
- 我们识别的效果：DealDamage
- 描述：造成{Damage:diff()}点伤害。

#### 究极防御 `ULTIMATE_DEFEND` — 可直接实现

- 费用 1 · skill · 目标 self
- 我们识别的效果：GainBlock
- 描述：获得{Block:diff()}点[gold]格挡[/gold]。

#### 群起攻之 `GANG_UP` — 无法实现

- 费用 1 · attack · 目标 any_enemy
- 我们识别的效果：DealDamage
- **无法实现**：按战斗历史计数作为数值来源（超出我们支持的条目或筛选维度）
- 额外钩子：Calculated
- 描述：造成{CalculatedDamage:diff()}点伤害。 / 本回合其他玩家每攻击过一次该敌人，该牌造成的伤害就额外增加{ExtraDamage:diff()}点。

#### 自动化 `AUTOMATION` — 实现方式不同

- 费用 1 · power · 目标 self
- 我们识别的效果：ApplyPower、GainEnergy
- 施加的状态：AutomationPower
- 额外钩子：AfterCardDrawn（来自状态 AutomationPower）
- 描述：你每抽10张牌，获得{Energy:energyIcons()}。

#### 花样百出 `JACK_OF_ALL_TRADES` — 可直接实现

- 费用 0 · skill · 目标 self · EXHAUST
- 我们识别的效果：AddNamedCardToPile、DrawCards
- 描述：将{Cards:diff()}张随机无色牌加入你的[gold]手牌[/gold]。

#### 计策 `STRATAGEM` — 实现方式不同

- 费用 1 · power · 目标 self
- 我们识别的效果：ApplyPower
- 施加的状态：StratagemPower
- 额外钩子：AfterShuffle（来自状态 StratagemPower）
- 描述：每当你的[gold]抽牌堆[/gold]打乱洗牌时，选择一张牌放入你的[gold]手牌[/gold]。

#### 连射 `VOLLEY` — 可直接实现

- 费用 X · attack · 目标 random_enemy
- 我们识别的效果：DealDamage
- 描述：随机对敌人造成{Damage:diff()}点伤害X次。

#### 闪亮登场 `DRAMATIC_ENTRANCE` — 可直接实现

- 费用 0 · attack · 目标 all_enemies · EXHAUST/INNATE
- 我们识别的效果：DealDamage
- 描述：对所有敌人造成{Damage:diff()}点伤害。

#### 震荡波 `SHOCKWAVE` — 可直接实现

- 费用 2 · skill · 目标 all_enemies · EXHAUST
- 我们识别的效果：ApplyPower
- 施加的状态：VulnerablePower、WeakPower
- 描述：给予所有敌人{Power:diff()}层[gold]虚弱[/gold]和[gold]易伤[/gold]。

#### 非凡技艺 `PROWESS` — 实现方式不同

- 费用 1 · power · 目标 self
- 我们识别的效果：ApplyPower
- 施加的状态：DexterityPower、StrengthPower
- 额外钩子：ModifyBlockAdditive（来自状态 DexterityPower）
- 描述：获得{StrengthPower:diff()}点[gold]力量[/gold]。 / 获得{DexterityPower:diff()}点[gold]敏捷[/gold]。

#### 飞溅 `SPLASH` — 可直接实现

- 费用 1 · skill · 目标 self
- 我们识别的效果：AddNamedCardToPile、SetCardsFree、UpgradeCardsInPile
- 描述：从3张其他角色的{IfUpgraded:show:[gold]升级过的[/gold]}攻击牌中选择1张加入你的[gold]手牌[/gold]。这张牌在本回合免费打出。

#### 黑暗镣铐 `DARK_SHACKLES` — 可直接实现

- 费用 0 · skill · 目标 any_enemy · EXHAUST
- 我们识别的效果：ApplyPower
- 施加的状态：DarkShacklesPower
- 描述：使一名敌人在本回合失去{StrengthLoss:diff()}点[gold]力量[/gold]。

### rare（25 张）

#### 乱战 `MAYHEM` — 实现方式不同

- 费用 2 · power · 目标 self
- 我们识别的效果：ApplyPower
- 施加的状态：MayhemPower
- 额外钩子：AfterAutoPrePlayPhaseEntered（来自状态 MayhemPower）
- 描述：在你的回合开始时，打出你[gold]抽牌堆[/gold]顶部的牌。

#### 击倒 `KNOCKDOWN` — 实现方式不同

- 费用 3 · attack · 目标 any_enemy
- 我们识别的效果：ApplyPower、DealDamage
- 施加的状态：KnockdownPower
- 额外钩子：AfterApplied（来自状态 KnockdownPower）
- 描述：造成{Damage:diff()}点伤害。 / 该敌人在本回合受到的来自其他玩家的伤害变为{IfUpgraded:show:三倍|两倍}。

#### 劫难 `CALAMITY` — 实现方式不同

- 费用 3 · power · 目标 self
- 我们识别的效果：ApplyPower
- 施加的状态：CalamityPower
- 额外钩子：BeforeCardPlayed（来自状态 CalamityPower）
- 描述：每当你打出一张攻击牌时，将一张随机攻击牌添加到你的[gold]手牌[/gold]。

#### 大奖 `JACKPOT` — 可直接实现

- 费用 3 · attack · 目标 any_enemy
- 我们识别的效果：AddNamedCardToPile、DealDamage、DrawCards、UpgradeCardsInPile
- 描述：造成{Damage:diff()}点伤害。 / 将{Cards:diff()}张随机{IfUpgraded:show:[gold]升级过[/gold]的}0{energyPrefix:energyIcons(1)}的牌加入你的[gold]手牌[/gold]。

#### 天选 `ANOINTED` — 可直接实现

- 费用 1 · skill · 目标 self · EXHAUST
- 我们识别的效果：MoveCardsBetweenPiles
- 描述：将你[gold]抽牌堆[/gold]中的所有[gold]稀有[/gold]牌放入你的[gold]手牌[/gold]。

#### 孤注一掷 `THE_GAMBIT` — 可直接实现

- 费用 0 · skill · 目标 self
- 我们识别的效果：ApplyPower、GainBlock
- 施加的状态：TheGambitPower
- 描述：获得{Block:diff()}点[gold]格挡[/gold]。 / 如果你在本场战斗中受到未被格挡的攻击伤害，则立刻死亡。

#### 希望灯塔 `BEACON_OF_HOPE` — 实现方式不同

- 费用 1 · power · 目标 self
- 我们识别的效果：ApplyPower
- 施加的状态：BeaconOfHopePower
- 额外钩子：AfterBlockGained（来自状态 BeaconOfHopePower）
- 描述：每当你在你的回合获得[gold]格挡[/gold]时，其他玩家获得相应一半的[gold]格挡[/gold]。

#### 怀旧 `NOSTALGIA` — 实现方式不同

- 费用 1 · power · 目标 self
- 我们识别的效果：ApplyPower
- 施加的状态：NostalgiaPower
- 额外钩子：AfterModifyingCardPlayResultPileOrPosition（来自状态 NostalgiaPower）
- 描述：每回合首次打出攻击或技能牌时，将其置于你的[gold]抽牌堆[/gold]顶端。

#### 战略大师 `MASTER_OF_STRATEGY` — 可直接实现

- 费用 0 · skill · 目标 self · EXHAUST
- 我们识别的效果：DrawCards
- 描述：抽{Cards:diff()}张牌。

#### 拟态 `MIMIC` — 可直接实现

- 费用 1 · skill · 目标 any_ally · EXHAUST
- 我们识别的效果：GainBlock
- 额外钩子：Calculated
- 数值来源：Block
- 描述：获得等同于另一位玩家[gold]格挡[/gold]值的[gold]格挡[/gold]。

#### 撕碎 `REND` — 无法实现

- 费用 2 · attack · 目标 any_enemy
- 我们识别的效果：DealDamage
- **无法实现**：超出范围的按条件数牌（我们只支持标签/类型/关键词/星辰这几类条件）
- 额外钩子：Calculated
- 描述：造成{CalculatedDamage:diff()}点伤害。 / 该名敌人身上每有一种负面效果，就额外造成{ExtraDamage:diff()}点伤害。

#### 未掘宝石 `HIDDEN_GEM` — 无法实现

- 费用 1 · skill · 目标 self
- 我们识别的效果：（无）
- **无法实现**：BaseReplayCount（增加重放次数）
- 描述：你[gold]抽牌堆[/gold]中的一张没有[gold]重放[/gold]的随机牌获得{Replay:diff()}层[gold]重放[/gold]。

#### 永恒铠甲 `ETERNAL_ARMOR` — 实现方式不同

- 费用 3 · power · 目标 self
- 我们识别的效果：ApplyPower
- 施加的状态：PlatingPower
- 额外钩子：AfterApplied（来自状态 PlatingPower）、BeforeSideTurnEndEarly（来自状态 PlatingPower）
- 描述：获得{PlatingPower:diff()}层[gold]覆甲[/gold]。

#### 流星锤 `BOLAS` — 实现方式不同

- 费用 0 · attack · 目标 any_enemy
- 我们识别的效果：DealDamage、MoveCardsBetweenPiles
- 额外钩子：BeforeHandDraw
- 描述：造成{Damage:diff()}点伤害。 / 在你的下个回合开始时，将此卡返回你的[gold]手牌[/gold]。

#### 滚石 `ROLLING_BOULDER` — 可直接实现

- 费用 3 · power · 目标 self
- 我们识别的效果：ApplyPower
- 施加的状态：RollingBoulderPower
- 描述：在你的回合开始时，对所有敌人造成{RollingBoulderPower:diff()}点伤害，然后将该伤害增加{IncrementAmount:diff()}点。

#### 潦草急就 `SCRAWL` — 可直接实现

- 费用 1 · skill · 目标 self · EXHAUST
- 我们识别的效果：DrawCards
- 描述：抽牌直到抽满[gold]手牌[/gold]。

#### 炼制药水 `ALCHEMIZE` — 无法实现

- 费用 1 · skill · 目标 self · EXHAUST
- 我们识别的效果：（无）
- **无法实现**：PotionCmd.TryToProcure（获得药水）
- 描述：获得一瓶随机药水。

#### 熵 `ENTROPY` — 可直接实现

- 费用 1 · power · 目标 self
- 我们识别的效果：ApplyPower、DrawCards
- 施加的状态：EntropyPower
- 描述：在你的回合开始时，[gold]变化[/gold]你[gold]手牌[/gold]中的{Cards:diff()}张牌。

#### 狠揍 `BEAT_DOWN` — 可直接实现

- 费用 3 · skill · 目标 random_enemy
- 我们识别的效果：AutoPlay、DrawCards
- 描述：打出你[gold]弃牌堆[/gold]中的{Cards:diff()}张随机攻击牌。

#### 秘密技法 `SECRET_TECHNIQUE` — 可直接实现

- 费用 0 · skill · 目标 self · EXHAUST
- 我们识别的效果：MoveCardsBetweenPiles
- 描述：从[gold]抽牌堆[/gold]中选择一张技能牌放入你的[gold]手牌[/gold]。

#### 秘密武器 `SECRET_WEAPON` — 可直接实现

- 费用 0 · skill · 目标 self · EXHAUST
- 我们识别的效果：MoveCardsBetweenPiles
- 描述：从[gold]抽牌堆[/gold]中选择一张攻击牌放入你的[gold]手牌[/gold]。

#### 箭雨 `SALVO` — 实现方式不同

- 费用 1 · attack · 目标 any_enemy
- 我们识别的效果：ApplyPower、DealDamage
- 施加的状态：RetainHandPower
- 额外钩子：ShouldFlush（来自状态 RetainHandPower）
- 描述：造成{Damage:diff()}点伤害。 / 在本回合[gold]保留[/gold]你的[gold]手牌[/gold]。

#### 贪婪之手 `HAND_OF_GREED` — 无法实现

- 费用 2 · attack · 目标 any_enemy
- 我们识别的效果：DealDamage
- **无法实现**：PlayerCmd.GainGold（获得金币）
- 描述：造成{Damage:diff()}点伤害。 / [gold]斩杀[/gold]时，获得{Gold:diff()}[gold]金币[/gold]。

#### 金斧 `GOLD_AXE` — 可直接实现

- 费用 1 · attack · 目标 any_enemy
- 我们识别的效果：DealDamage
- 额外钩子：Calculated
- 描述：造成本场战斗中所打出牌数的伤害。{InCombat: / （造成{CalculatedDamage:diff()}点伤害）|}

#### 集结 `RALLY` — 可直接实现

- 费用 2 · skill · 目标 all_allies
- 我们识别的效果：GainBlock
- 描述：所有玩家获得{Block:diff()}点[gold]格挡[/gold]。

## curse（18 张）

### curse（18 张）

#### 债务 `DEBT` — 无法实现

- 费用 -1 · curse · 目标 none · UNPLAYABLE
- 我们识别的效果：（无）
- **无法实现**：PlayerCmd.LoseGold（失去金币）
- 额外钩子：OnTurnEndInHand
- 描述：在你的回合结束时，如果这张牌在你的[gold]手牌[/gold]中，你失去{Gold:diff()}[gold]金币[/gold]。

#### 凡庸 `NORMALITY` — 实现方式不同

- 费用 -1 · curse · 目标 none · UNPLAYABLE
- 我们识别的效果：（无）
- 额外钩子：Calculated、ShouldPlay
- 描述：你在本回合不能打出超过3张牌。{InCombat: / （还剩{CalculatedCards}张牌）|}

#### 受伤 `INJURY` — 无需实现

- 费用 -1 · curse · 目标 none · UNPLAYABLE
- 我们识别的效果：（无）

#### 孢子心灵 `SPORE_MIND` — 无需实现

- 费用 1 · curse · 目标 none · EXHAUST
- 我们识别的效果：（无）

#### 悔恨 `REGRET` — 实现方式不同

- 费用 -1 · curse · 目标 none · UNPLAYABLE
- 我们识别的效果：DealDamage、LoseHp
- 额外钩子：BeforeSideTurnEnd、OnTurnEndInHand
- 描述：在你的回合结束时，如果这张牌在你的[gold]手牌[/gold]中，失去相当于[gold]手牌[/gold]数量的生命。

#### 愚行 `FOLLY` — 无需实现

- 费用 -1 · curse · 目标 none · UNPLAYABLE/ETERNAL/INNATE/ETHEREAL/SLY/RETAIN
- 我们识别的效果：（无）

#### 愧疚 `GUILTY` — 无法实现

- 费用 -1 · curse · 目标 none · UNPLAYABLE
- 我们识别的效果：（无）
- **无法实现**：CardPileCmd.RemoveFromDeck（从牌组里移除）
- 额外钩子：AfterCombatEnd
- 描述：在{Combats:diff()}场战斗后从你的[gold]牌组[/gold]中移除。

#### 执迷 `ENTHRALLED` — 实现方式不同

- 费用 2 · curse · 目标 none · ETERNAL
- 我们识别的效果：（无）
- 额外钩子：ShouldPlay
- 描述：如果这张牌在你的[gold]手牌[/gold]中，你必须优先打出这张牌。

#### 疑虑 `DOUBT` — 可直接实现

- 费用 -1 · curse · 目标 none · UNPLAYABLE
- 我们识别的效果：ApplyPower
- 施加的状态：WeakPower
- 额外钩子：OnTurnEndInHand
- 描述：在你的回合结束时，如果这张牌在你的[gold]手牌[/gold]中，获得{WeakPower:diff()}层[gold]虚弱[/gold]。

#### 睡眠不佳 `POOR_SLEEP` — 无需实现

- 费用 -1 · curse · 目标 none · UNPLAYABLE/RETAIN
- 我们识别的效果：（无）

#### 笨拙 `CLUMSY` — 无需实现

- 费用 -1 · curse · 目标 none · UNPLAYABLE/ETHEREAL
- 我们识别的效果：（无）

#### 羞耻 `SHAME` — 实现方式不同

- 费用 -1 · curse · 目标 none · UNPLAYABLE
- 我们识别的效果：ApplyPower
- 施加的状态：FrailPower
- 额外钩子：OnTurnEndInHand、ModifyBlockMultiplicative（来自状态 FrailPower）
- 描述：在你的回合结束时，如果这张牌在你的[gold]手牌[/gold]中，则获得{Frail:diff()}层[gold]脆弱[/gold]。

#### 腐朽 `DECAY` — 可直接实现

- 费用 -1 · curse · 目标 none · UNPLAYABLE
- 我们识别的效果：DealDamage
- 额外钩子：OnTurnEndInHand
- 描述：在你的回合结束时，如果这张牌在你的[gold]手牌[/gold]中, 你受到{Damage:diff()}点伤害。

#### 苦恼 `WRITHE` — 无需实现

- 费用 -1 · curse · 目标 none · INNATE/UNPLAYABLE
- 我们识别的效果：（无）

#### 贪婪 `GREED` — 无需实现

- 费用 -1 · curse · 目标 none · ETERNAL/UNPLAYABLE
- 我们识别的效果：（无）

#### 进阶之灾 `ASCENDERS_BANE` — 无需实现

- 费用 -1 · curse · 目标 none · ETERNAL/UNPLAYABLE/ETHEREAL/EXHAUST/INNATE/SLY/RETAIN
- 我们识别的效果：（无）

#### 铃铛的诅咒 `CURSE_OF_THE_BELL` — 无需实现

- 费用 -1 · curse · 目标 none · ETERNAL/UNPLAYABLE
- 我们识别的效果：（无）

#### 霉运 `BAD_LUCK` — 可直接实现

- 费用 -1 · curse · 目标 none · ETERNAL/UNPLAYABLE
- 我们识别的效果：LoseHp
- 额外钩子：OnTurnEndInHand
- 描述：在你的回合结束时，如果这张牌在你的手牌中，你失去{HpLoss:diff()}点生命。

## status（11 张）

### status（11 张）

#### 伤口 `WOUND` — 无需实现

- 费用 -1 · status · 目标 none · UNPLAYABLE
- 我们识别的效果：（无）

#### 呼唤 `BECKON` — 可直接实现

- 费用 1 · status · 目标 none
- 我们识别的效果：LoseHp
- 额外钩子：OnTurnEndInHand
- 描述：在你的回合结束时，如果这张牌在你的[gold]手牌[/gold]中， / 你失去{HpLoss}点生命。

#### 感染 `INFECTION` — 可直接实现

- 费用 -1 · status · 目标 none · UNPLAYABLE
- 我们识别的效果：DealDamage
- 额外钩子：OnTurnEndInHand
- 描述：在你的回合结束时，如果这张牌在你的[gold]手牌[/gold]中，你受到{Damage:diff()}点伤害。

#### 晕眩 `DAZED` — 无需实现

- 费用 -1 · status · 目标 none · ETHEREAL/UNPLAYABLE
- 我们识别的效果：（无）

#### 毒素 `TOXIC` — 可直接实现

- 费用 1 · status · 目标 none · EXHAUST
- 我们识别的效果：DealDamage
- 额外钩子：OnTurnEndInHand
- 描述：在你的回合结束时，如果这张牌在你的[gold]手牌[/gold]中，你受到{Damage:diff()}点伤害。

#### 灼伤 `BURN` — 可直接实现

- 费用 -1 · status · 目标 none · UNPLAYABLE
- 我们识别的效果：DealDamage
- 额外钩子：OnTurnEndInHand
- 描述：在你的回合结束时，如果这张牌在你的[gold]手牌[/gold]中，你受到{Damage:diff()}点伤害。

#### 煤灰 `SOOT` — 无需实现

- 费用 -1 · status · 目标 none · UNPLAYABLE
- 我们识别的效果：（无）

#### 狂乱逃离 `FRANTIC_ESCAPE` — 无法实现

- 费用 1 · status · 目标 self
- 我们识别的效果：ModifyCardCost
- **无法实现**：PowerCmd.ModifyAmount（直接改状态层数）
- 描述：远离。 / 将[gold]沙坑[/gold]的计数加1。 / 这张牌的耗能加1。

#### 碎屑 `DEBRIS` — 无需实现

- 费用 1 · status · 目标 none · EXHAUST
- 我们识别的效果：（无）

#### 虚空 `VOID` — 无法实现

- 费用 -1 · status · 目标 none · UNPLAYABLE/ETHEREAL
- 我们识别的效果：GainEnergy
- **无法实现**：PlayerCmd.LoseEnergy（失去能量）
- 额外钩子：AfterCardDrawn
- 描述：每当你抽到这张牌时，失去{Energy:energyIcons()}。

#### 黏液 `SLIMED` — 可直接实现

- 费用 1 · status · 目标 none · EXHAUST
- 我们识别的效果：DrawCards
- 描述：抽1张牌。

## event（20 张）

### ancient（8 张）

#### 吹哨 `WHISTLE` — 无法实现

- 费用 3 · attack · 目标 any_enemy · EXHAUST
- 我们识别的效果：DealDamage
- **无法实现**：CreatureCmd.Stun（击晕）
- 描述：造成{Damage:diff()}点伤害。 / [gold]击晕[/gold]该敌人。

#### 撕咬 `MAUL` — 可直接实现

- 费用 1 · attack · 目标 any_enemy
- 我们识别的效果：BoostCardDamage、DealDamage
- 描述：造成{Damage:diff()}点伤害两次。 / 在这场战斗中，将所有“撕咬”牌的伤害增加{Increase:diff()}。

#### 放松 `RELAX` — 实现方式不同

- 费用 3 · skill · 目标 self · EXHAUST
- 我们识别的效果：ApplyPower、DrawCards、GainBlock、GainEnergy
- 施加的状态：DrawCardsNextTurnPower、EnergyNextTurnPower
- 额外钩子：ModifyHandDraw（来自状态 DrawCardsNextTurnPower）、AfterEnergyReset（来自状态 EnergyNextTurnPower）
- 描述：获得{Block:diff()}点[gold]格挡[/gold]。 / 下个回合，抽{Cards:diff()}张牌并获得{Energy:energyIcons()}。

#### 涅奥之怒 `NEOWS_FURY` — 可直接实现

- 费用 1 · attack · 目标 any_enemy · EXHAUST
- 我们识别的效果：DealDamage、DrawCards、MoveCardsBetweenPiles
- 描述：造成{Damage:diff()}点伤害。 / 将你[gold]弃牌堆[/gold]中的至多{Cards:diff()}张牌放入你的[gold]手牌[/gold]。

#### 灵体 `APPARITION` — 实现方式不同

- 费用 1 · skill · 目标 self · ETHEREAL/EXHAUST
- 我们识别的效果：ApplyPower
- 施加的状态：IntangiblePower
- 额外钩子：AfterModifyingDamageAmount（来自状态 IntangiblePower）、AfterModifyingHpLostAfterOsty（来自状态 IntangiblePower）、ModifyDamageCap（来自状态 IntangiblePower）、ModifyHpLostAfterOsty（来自状态 IntangiblePower）
- 描述：获得{IntangiblePower:diff()}层[gold]无实体[/gold]。

#### 神化 `APOTHEOSIS` — 可直接实现

- 费用 2 · skill · 目标 self · EXHAUST/INNATE
- 我们识别的效果：UpgradeCardsInPile
- 描述：[gold]升级[/gold]你的全部卡牌。

#### 至亮之焰 `BRIGHTEST_FLAME` — 可直接实现

- 费用 0 · skill · 目标 self
- 我们识别的效果：DrawCards、GainEnergy、LoseMaxHp
- 描述：获得{Energy:energyIcons()}。 / 抽{Cards:diff()}张牌。 / 失去{MaxHp:diff()}点最大生命。

#### 许愿 `WISH` — 可直接实现

- 费用 0 · skill · 目标 self · EXHAUST
- 我们识别的效果：MoveCardsBetweenPiles
- 描述：将你[gold]抽牌堆[/gold]中的一张牌放入你的[gold]手牌[/gold]。

### event（12 张）

#### 你好世界 `HELLO_WORLD` — 实现方式不同

- 费用 1 · power · 目标 self
- 我们识别的效果：ApplyPower
- 施加的状态：HelloWorldPower
- 额外钩子：BeforeHandDraw（来自状态 HelloWorldPower）
- 描述：在你的回合开始时，将一张随机普通牌加入你的[gold]手牌[/gold]。

#### 压扁 `SQUASH` — 可直接实现

- 费用 1 · attack · 目标 any_enemy
- 我们识别的效果：ApplyPower、DealDamage
- 施加的状态：VulnerablePower
- 描述：造成{Damage:diff()}点伤害。 / 给予{VulnerablePower:diff()}层[gold]易伤[/gold]。

#### 啄击 `PECK` — 可直接实现

- 费用 1 · attack · 目标 any_enemy
- 我们识别的效果：DealDamage
- 额外钩子：Repeat
- 描述：造成{Damage:diff()}点伤害{Repeat:diff()}次。

#### 坚韧之环 `TORIC_TOUGHNESS` — 实现方式不同

- 费用 2 · skill · 目标 self
- 我们识别的效果：ApplyPower、GainBlock
- 施加的状态：ToricToughnessPower
- 额外钩子：AfterBlockCleared（来自状态 ToricToughnessPower）
- 描述：获得{Block:diff()}点[gold]格挡[/gold]。 / 在接下来的{Turns:diff()}个回合开始时，获得{Block:diff()}点[gold]格挡[/gold]。

#### 开悟 `ENLIGHTENMENT` — 可直接实现

- 费用 0 · skill · 目标 self · EXHAUST
- 我们识别的效果：ModifyCardCost
- 描述：在这{IfUpgraded:show:场战斗|个回合}，你当前[gold]手牌[/gold]中所有牌的耗能降低至1。

#### 异鸟扑击 `BYRD_SWOOP` — 可直接实现

- 费用 0 · attack · 目标 any_enemy
- 我们识别的效果：DealDamage
- 描述：造成{Damage:diff()}点伤害。

#### 杀灭 `EXTERMINATE` — 可直接实现

- 费用 1 · attack · 目标 all_enemies
- 我们识别的效果：DealDamage
- 额外钩子：Repeat
- 描述：对所有敌人造成{Damage:diff()}点伤害{Repeat:diff()}次。

#### 狂乱撕扯 `RIP_AND_TEAR` — 可直接实现

- 费用 1 · attack · 目标 random_enemy
- 我们识别的效果：DealDamage
- 描述：随机对敌人造成{Damage:diff()}点伤害两次。

#### 疯狂科学 `MAD_SCIENCE` — 实现方式不同

- 费用 1 · attack · 目标 any_enemy
- 我们识别的效果：ApplyPower、DealDamage、DrawCards、GainBlock、GainEnergy、MoveCardsBetweenPiles、SetCardsFree
- 施加的状态：CuriousPower、DexterityPower、ImprovementPower、StranglePower、StrengthPower、VulnerablePower、WeakPower
- 额外钩子：ModifyBlockAdditive（来自状态 DexterityPower）、AfterCombatEnd（来自状态 ImprovementPower）、BeforeCardPlayed（来自状态 StranglePower）
- 描述：{CardType:choose(Attack|Skill|Power):造成{Damage:diff()}点伤害{Violence:{ViolenceHits:diff()}次|}。|获得{Block:diff()}点[gold]格挡[/gold]。||？？？}{HasRider:{Sapping: / 给予{SappingWeak:diff()}层[gold]虚弱[/gold]。 / 给予{SappingVulnerable:diff()}层[gold]易伤[/gold]。|}{Choking: / 本回合，你每打出一张牌，该敌人失去{ChokingDamage:diff()}点生命。|}{Energized: / 获得{EnergizedEnergy:energyIcons()}。|}{Wisdom: / 抽{WisdomCards:diff()}张牌|}{Chaos: / 将一张随机牌放入你的[gold]手牌[/gold]，这张牌在本回合可以免费打出。|}{Expertise:获得{ExpertiseStrength:diff()}点[gold]力量[/gold]。 / 获得{ExpertiseDexterity:diff()}点[gold]敏捷[/gold]。|}{Curious:能力牌的耗能减少{CuriousReduction:diff()}{energyPrefix:energyIcons(1)}。|}{Improvement:在战斗结束时，[gold]升级[/gold]你牌组中的一张随机牌。|}|{CardType:choose(Attack|Skill|Power): / ？？？| / ？？？|？？？|？？？}}

#### 疯狂进食 `FEEDING_FRENZY` — 可直接实现

- 费用 0 · skill · 目标 self
- 我们识别的效果：ApplyPower
- 施加的状态：FeedingFrenzyPower
- 描述：在本回合内获得{StrengthPower:diff()}点[gold]力量[/gold]。

#### 羽化 `METAMORPHOSIS` — 可直接实现

- 费用 2 · skill · 目标 self · EXHAUST
- 我们识别的效果：AddNamedCardToPile、DrawCards、SetCardsFree
- 描述：在你的[gold]抽牌堆[/gold]中加入{Cards:diff()}张随机攻击牌。它们在本场战斗中可以被免费打出。

#### 铁蒺藜 `CALTROPS` — 实现方式不同

- 费用 1 · power · 目标 self
- 我们识别的效果：ApplyPower
- 施加的状态：ThornsPower
- 额外钩子：BeforeDamageReceived（来自状态 ThornsPower）
- 描述：每当你被攻击时，对攻击者造成{ThornsPower:diff()}点伤害。

## quest（3 张）

### quest（3 张）

#### 多尼斯异鸟蛋 `BYRDONIS_EGG` — 实现方式不同

- 费用 -1 · quest · 目标 none · UNPLAYABLE
- 我们识别的效果：（无）
- 额外钩子：TryModifyRestSiteOptions
- 描述：能在[gold]休息处[/gold]被孵化。

#### 灯火钥匙 `LANTERN_KEY` — 无需实现

- 费用 -1 · quest · 目标 self · UNPLAYABLE
- 我们识别的效果：（无）
- 描述：在下一阶段解锁一个特殊事件。

#### 藏宝图 `SPOILS_MAP` — 无法实现

- 费用 -1 · quest · 目标 self · UNPLAYABLE
- 我们识别的效果：（无）
- **无法实现**：CardPileCmd.RemoveFromDeck（从牌组里移除）、PlayerCmd.CompleteQuest（完成任务牌）、PlayerCmd.GainGold（获得金币）
- 额外钩子：AfterCreated、AfterMapGenerated、BeforeCardRemoved
- 描述：在下一阶段的地图上，标记一个有{Gold:diff()}额外[gold]金币[/gold]的地点。

## token（14 张）

### token（10 张）

#### 仆从俯冲 `MINION_DIVE_BOMB` — 可直接实现

- 费用 0 · attack · 目标 any_enemy · EXHAUST
- 我们识别的效果：DealDamage
- 描述：造成{Damage:diff()}点伤害。

#### 仆从打击 `MINION_STRIKE` — 可直接实现

- 费用 0 · attack · 目标 any_enemy · EXHAUST
- 我们识别的效果：DealDamage、DrawCards
- 描述：造成{Damage:diff()}点伤害。 / 抽{Cards:diff()}张牌。

#### 仆从捐躯 `MINION_SACRIFICE` — 可直接实现

- 费用 0 · skill · 目标 self · EXHAUST
- 我们识别的效果：GainBlock
- 描述：获得{Block:diff()}点[gold]格挡[/gold]。

#### 冷光 `LUMINESCE` — 可直接实现

- 费用 0 · skill · 目标 self · EXHAUST/RETAIN
- 我们识别的效果：GainEnergy
- 描述：获得{Energy:energyIcons()}。

#### 君王之剑 `SOVEREIGN_BLADE` — 实现方式不同

- 费用 2 · attack · 目标 any_enemy · RETAIN
- 我们识别的效果：BoostCardDamage、DealDamage、GainBlock
- 额外钩子：AfterTransformedFrom、Calculated、Repeat
- 描述：{TargetType:choose(AllEnemies):对所有敌人|}造成{Damage:diff()}点伤害{Repeat:choose(1):|{}次}。{GainsBlock:cond: / 获得{CalculatedBlock:diff()}点[gold]格挡[/gold].|}

#### 小刀 `SHIV` — 可直接实现

- 费用 0 · attack · 目标 any_enemy · EXHAUST
- 我们识别的效果：AddNamedCardToPile、DealDamage
- 描述：{TargetType:choose(AllEnemies):对所有敌人|}造成{Damage:diff()}点伤害。

#### 巨石 `GIANT_ROCK` — 可直接实现

- 费用 1 · attack · 目标 any_enemy
- 我们识别的效果：DealDamage
- 描述：造成{Damage:diff()}点伤害。

#### 扫荡凝视 `SWEEPING_GAZE` — 可直接实现

- 费用 0 · attack · 目标 random_enemy · ETHEREAL/EXHAUST
- 我们识别的效果：DealDamage
- 描述：[gold]奥斯提[/gold]对随机一名敌人造成{OstyDamage:diff()}点伤害。

#### 灵魂 `SOUL` — 可直接实现

- 费用 0 · skill · 目标 self · EXHAUST
- 我们识别的效果：AddNamedCardToPile、DrawCards
- 描述：抽{Cards:diff()}张牌。

#### 燃料 `FUEL` — 可直接实现

- 费用 0 · skill · 目标 self · EXHAUST
- 我们识别的效果：DrawCards、GainEnergy
- 描述：获得{Energy:energyIcons()}。 / 抽{Cards:diff()}张牌。

### status（4 张）

#### 心灵腐化 `MIND_ROT` — 实现方式不同

- 费用 -1 · status · 目标 none
- 我们识别的效果：ApplyPower
- 施加的状态：MindRotPower
- 额外钩子：AfterModifyingHandDraw（来自状态 MindRotPower）、ModifyHandDraw（来自状态 MindRotPower）
- 描述：每回合少抽{MindRotPower:diff()}张牌。

#### 懒惰 `SLOTH` — 实现方式不同

- 费用 -1 · status · 目标 none
- 我们识别的效果：ApplyPower
- 施加的状态：SlothPower
- 额外钩子：BeforeCardPlayed（来自状态 SlothPower）、ShouldPlay（来自状态 SlothPower）
- 描述：你在每个回合不能打出超过{SlothPower:diff()}张牌。

#### 瓦解 `DISINTEGRATION` — 实现方式不同

- 费用 -1 · status · 目标 none
- 我们识别的效果：ApplyPower
- 施加的状态：DisintegrationPower
- 额外钩子：AfterSideTurnEndLate（来自状态 DisintegrationPower）
- 描述：在你的回合结束时，受到{DisintegrationPower:diff()}点伤害。

#### 衰朽 `WASTE_AWAY` — 实现方式不同

- 费用 -1 · status · 目标 none
- 我们识别的效果：ApplyPower
- 施加的状态：WasteAwayPower
- 额外钩子：ModifyMaxEnergy（来自状态 WasteAwayPower）
- 描述：每回合失去{WasteAwayPower:diff()}点{energyPrefix:energyIcons(1)}。

## (none)（9 张）

### event（7 张）

#### 交锋 `CLASH` — 可直接实现

- 费用 0 · attack · 目标 any_enemy
- 我们识别的效果：DealDamage
- 描述：只有在[gold]手牌[/gold]中每一张牌都是攻击牌时才能被打出。 / 造成{Damage:diff()}点伤害。

#### 双持 `DUAL_WIELD` — 可直接实现

- 费用 1 · skill · 目标 self
- 我们识别的效果：AddNamedCardToPile、DrawCards
- 描述：选择一张攻击牌或能力牌。将该牌的{IfUpgraded:show:{Cards}张复制品|一张复制品}加入你的[gold]手牌[/gold]。

#### 堆栈 `STACK` — 可直接实现

- 费用 1 · skill · 目标 self
- 我们识别的效果：GainBlock
- 额外钩子：Calculated
- 数值来源：DiscardPileCount
- 描述：获得等量于与你当前[gold]弃牌堆[/gold]中牌数{IfUpgraded:show:+{CalculationBase}|}的[gold]格挡[/gold]值。{InCombat: / （获得{CalculatedBlock:diff()}点[gold]格挡[/gold]）|}

#### 声东击西 `DISTRACTION` — 可直接实现

- 费用 1 · skill · 目标 self · EXHAUST
- 我们识别的效果：AddNamedCardToPile、SetCardsFree
- 描述：将一张随机技能牌添加到你的[gold]手牌[/gold]中。这张牌在本回合内可以免费打出。

#### 巩固 `ENTRENCH` — 可直接实现

- 费用 2 · skill · 目标 self
- 我们识别的效果：GainBlock
- 描述：将你当前的[gold]格挡[/gold]翻倍。

#### 弹回 `REBOUND` — 实现方式不同

- 费用 1 · attack · 目标 any_enemy
- 我们识别的效果：ApplyPower、DealDamage
- 施加的状态：ReboundPower
- 额外钩子：AfterModifyingCardPlayResultPileOrPosition（来自状态 ReboundPower）
- 描述：造成{Damage:diff()}点伤害。 / 将你在本回合打出的下一张牌放置到你的[gold]抽牌堆[/gold]顶部。

#### 抢占先机 `OUTMANEUVER` — 实现方式不同

- 费用 1 · skill · 目标 self
- 我们识别的效果：ApplyPower、GainEnergy
- 施加的状态：EnergyNextTurnPower
- 额外钩子：AfterEnergyReset（来自状态 EnergyNextTurnPower）
- 描述：在下个回合获得{Energy:energyIcons()}。

### status（2 张）

#### 凋萎 `WITHER` — 可直接实现

- 费用 -1 · status · 目标 none · UNPLAYABLE
- 我们识别的效果：DealDamage
- 额外钩子：OnTurnEndInHand
- 描述：在你的回合结束时，如果这张牌在你的[gold]手牌[/gold]中，你受到{Damage:diff()}点伤害。

#### 弃用卡牌 `DEPRECATED_CARD` — 无法实现

- 费用 0 · status · 目标 none · EXHAUST
- 我们识别的效果：DrawCards
- **无法实现**：CardPileCmd.RemoveFromDeck（从牌组里移除）
- 描述：[blue]这张卡牌在近期的一次更新中被移除。[/blue] / 抽1张牌。 / 将这张牌从你的[gold]牌组[/gold]中移除。

