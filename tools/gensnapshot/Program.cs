using System.Text;
using ModForge.Core;
using ModForge.Core.Generate;
using ModForge.Core.Model;
using ModForge.Core.Modules.CardPicking;
using ModForge.Core.Modules.Descriptions;
using ModForge.Core.Modules.Effects;
using ModForge.Core.Modules.Variables;

// 抓"生成结果"的快照：把每个能拿到的工程的全部生成源码，连同每张牌的自动描述，
// 按 <projectId>/<路径> 写到 outDir 下。
//
// 存在的理由是**重构的验收**。生成是纯函数（没有时间戳、没有 GUID、没有随机），所以
// 结构改动前后各跑一次、diff -r 应当为空——空了就证明"只收拢了代码路径，没改行为"。
// 这条比"跑一遍能编译"强得多：编译过的代码可以行为不同，逐字节相同不可以。
//
// 用法: gensnapshot <outDir> [projectsRoot]
//   projectsRoot 默认 D:\ModForge\projects；其下每个含 project.json 的子目录都会被载入。

string outDir = args.Length > 0 ? args[0] : @"D:\ModForge\out\gensnapshot";
string projectsRoot = args.Length > 1 ? args[1] : @"D:\ModForge\projects";

var projects = new List<ModProject>();

// 1. 磁盘上真实的工程（用户实际在编辑的那些），最先纳入。
//
// 载入失败就跳过并说明：工程 JSON 可能比当前代码旧（枚举改过名），那是工程自己的事，
// 不该让快照工具整个挂掉——本工具要的是"生成结果"的覆盖面，样例与矩阵已足够。
if (Directory.Exists(projectsRoot))
{
    foreach (string dir in Directory.GetDirectories(projectsRoot).OrderBy(d => d, StringComparer.Ordinal))
    {
        string json = Path.Combine(dir, "project.json");

        if (!File.Exists(json))
        {
            continue;
        }

        try
        {
            projects.Add(ModProject.Load(json));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  [跳过] {Path.GetFileName(dir)}: {ex.GetType().Name}: {ex.Message}");
        }
    }
}

// 2. 样例工程：几种基础效果。
projects.Add(SampleProject.Create());

// 3. 变量矩阵：把"变量模块"关心的每一种形状都造一张牌（见文件末尾）。
projects.Add(VariableMatrix());

// 4. 效果矩阵：**每一种效果各来一张牌**（见文件末尾）。
//
// 这一份是效果模块搬迁的验收基础。少了它，"逐字节相同"只说明没弄坏别的，说明不了搬过来的
// 那几种仍然正确——这一点是踩过的：批 1 搬了六种效果（获得能量 / 星辰、消耗此牌、三种充能球），
// 而当时所有夹具一个都没用到它们，`diff` 为空其实什么都没验证。
projects.Add(EffectMatrix());

// 5. 时机矩阵：每一种触发时机、牌堆范围、计数器、自动打出各来一张（见文件末尾）。
//
// 与效果矩阵同一个道理，而且这次先查了覆盖：时机是**第七个横切关注点**，
// 而 gensnapshot 原来的夹具对它的覆盖是**零**（OnPlay 是默认值，所以连它都没显式出现过）。
// 时机模块搬迁没有这份夹具，"逐字节不变"同样会是一句空话。
projects.Add(TimingMatrix());

// 6. 计数矩阵：每一种"数出来的"来源各来一张（见文件末尾）。
//
// 同样是先查覆盖：计数有 30 种，而所有夹具原来只用到 5 种。
projects.Add(CounterMatrix());

Console.WriteLine($"projects: {string.Join(", ", projects.Select(p => p.Id))}");

int files = 0;

foreach (ModProject project in projects)
{
    string dir = Path.Combine(outDir, project.Id);
    Directory.CreateDirectory(dir);

    IReadOnlyDictionary<string, string> sources = new CodeGenerator(project).GenerateAll();

    foreach (string key in sources.Keys.OrderBy(k => k, StringComparer.Ordinal))
    {
        string path = Path.Combine(dir, key.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // 统一换行再写：Windows 与别处的行尾差异不应该混进 diff。
        File.WriteAllText(path, sources[key].Replace("\r\n", "\n"));
        files++;
    }

    // 自动描述也一并快照。描述里引用的是**变量名占位符**（{Damage2:diff()}），
    // 所以变量名一旦改动，这里会先于游戏现形。
    var descriptions = new StringBuilder();

    foreach (CardDef card in project.Cards.OrderBy(c => c.Id, StringComparer.Ordinal))
    {
        foreach (string language in new[] { "zhs", "eng" })
        {
            descriptions.AppendLine($"=== {card.Id} [{language}] ===");
            descriptions.AppendLine(DescriptionPlaceholders.Suggest(project, card, language));
        }
    }

    File.WriteAllText(
        Path.Combine(dir, "_descriptions.txt"),
        descriptions.ToString().Replace("\r\n", "\n"));
    files++;

    Console.WriteLine($"  {project.Id}: {sources.Count} source files");
}

// 界面 schema 也一起快照。它是**生成出来的**（界面照着它渲染表单），但以前没有任何工具
// 覆盖它——改了某个效果的元数据而漏掉 schema，只会表现为"界面上少一格"，很难发现。
// 用与 App 相同的命名策略（camelCase），这样这条快照和界面实际拿到的那份是一致的。
string schemaJson = System.Text.Json.JsonSerializer.Serialize(
    EditorSchema.Build(),
    new System.Text.Json.JsonSerializerOptions
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    });

File.WriteAllText(Path.Combine(outDir, "_schema.json"), schemaJson.Replace("\r\n", "\n"));
files++;

Console.WriteLine($"wrote {files} files under {outDir}");
Console.WriteLine("RESULT: OK");
return 0;

// ---------------------------------------------------------------------------
// 变量矩阵：刻意把"同一个数值种类出现多条""计算型与固定型混用""等级表""永久"这些
// 会走到不同变量形状的组合各造一张牌，好让快照能真正覆盖变量模块的每条路径。
// ---------------------------------------------------------------------------
static ModProject VariableMatrix()
{
    var project = new ModProject
    {
        Id = "VarMatrix",
        Name = "Variable Matrix",
        Version = "1.0.0",
    };

    // 一个本工程自己的状态：施加状态要声明 PowerVar，名字按类型名走。
    project.Powers.Add(new PowerDef
    {
        Id = "MIGHT",
        Name = new LocalizedText("eng", "Might").With("zhs", "伟力"),
        Kind = PowerKind.Buff,
    });

    // 状态的钩子里也会出现计算型数值——那条路径用的是生成类的私有字段
    // （_calcBase{n}），不是游戏的计算变量，所以单独造一个。
    //
    // 作用对象故意留成**默认的"目标"**：状态里没有出牌目标，这一档按旧口径落地
    // （造物轴＝所有敌人），这里正好把那条兼容路径钉在快照里——它同时覆盖了
    // "状态里的造物轴循环"（表达式与卡牌那份不同）。
    project.Powers.Add(new PowerDef
    {
        Id = "SIPHON",
        Name = new LocalizedText("eng", "Siphon").With("zhs", "汲取"),
        Description = "At the end of your turn, deal damage equal to your hand size.",
        Kind = PowerKind.Buff,
        Stack = PowerStack.Counter,
        Triggers =
        {
            new TriggeredEffect
            {
                When = PowerTrigger.TurnEnd,
                Effect = new EffectDef
                {
                    Kind = EffectKind.DealDamage,
                    Target = EffectTarget.Target,
                    Amount = 2,
                    Source = new ValueSource { Actor = ValueSourceActor.Self, Kind = ValueSourceKind.HandCount },
                },
            },
        },
    });

    var cards = new List<CardDef>();

    // ---- "多人游戏卡牌"（按玩家人数的限制）----
    //
    // 两档各来一张：游戏在**卡池与商店**那一侧过滤它，所以生成物里那个 override
    // 是这张牌"会不会出现在单人游戏里"的唯一依据——发错了不会被编译或测试抓到，
    // 只会静默地多给或少给一张牌。默认档（两种模式都有）**不写**，另有一张卡专门钉这一点。
    cards.Add(Card("FIXED_MULTIPLAYER", "Multiplayer Only", 5, e =>
    {
        e.Type = CardType.Skill;
        e.Target = CardTargetType.AnyAlly;
        e.Multiplayer = CardMultiplayerConstraint.MultiplayerOnly;
        e.Effects.Clear();
        e.Effects.Add(new EffectDef
        {
            Kind = EffectKind.GainEnergy, Target = EffectTarget.Target, Amount = 1,
        });
    }));
    cards.Add(Card("FIXED_SINGLEPLAYER", "Single Player Only", 5, e =>
    {
        e.Type = CardType.Skill;
        e.Target = CardTargetType.Self;
        e.Multiplayer = CardMultiplayerConstraint.SingleplayerOnly;
        e.Effects.Clear();
        e.Effects.Add(Block(5, upgrade: 0));
    }));

    // ---- 固定值：同类第一个沿用原版名，第二个起带序号 ----
    cards.Add(Card("FIXED_ONE", "Fixed One", 6));
    cards.Add(Card("FIXED_TWO_DAMAGE", "Two Damages", 7, e =>
    {
        // 第二条伤害：走 "Damage2" 这个名字。
        e.Effects.Add(Damage(9, upgrade: 3));
    }));
    cards.Add(Card("FIXED_BLOCK_TWO", "Two Blocks", 5, e =>
    {
        e.Type = CardType.Skill;
        e.Target = CardTargetType.Self;
        e.Effects.Clear();
        e.Effects.Add(Block(4, upgrade: 2));
        e.Effects.Add(Block(6, upgrade: 3));
    }));

    // ---- 每种"有自己变量类型"的固定值各来一张 ----
    cards.Add(Card("KIND_SUMMON", "Summon Kind", 1, e =>
    {
        e.Type = CardType.Skill;
        e.Target = CardTargetType.Self;
        e.Effects.Clear();
        e.Effects.Add(new EffectDef { Kind = EffectKind.Summon, Target = EffectTarget.Self, Amount = 4, Upgrade = new EffectUpgrade { AmountDelta = 2 } });
        e.Effects.Add(new EffectDef { Kind = EffectKind.Summon, Target = EffectTarget.Self, Amount = 8 });
    }));
    cards.Add(Card("KIND_FORGE", "Forge Kind", 1, e =>
    {
        e.Type = CardType.Skill;
        e.Target = CardTargetType.Self;
        e.Effects.Clear();
        e.Effects.Add(new EffectDef { Kind = EffectKind.Forge, Target = EffectTarget.Self, Amount = 3, Upgrade = new EffectUpgrade { AmountDelta = 1 } });
    }));
    cards.Add(Card("KIND_MAXHP", "Max HP Kinds", 1, e =>
    {
        e.Type = CardType.Skill;
        e.Target = CardTargetType.Self;
        e.Effects.Clear();
        e.Effects.Add(new EffectDef { Kind = EffectKind.GainMaxHp, Target = EffectTarget.Self, Amount = 5, Upgrade = new EffectUpgrade { AmountDelta = 2 } });
        e.Effects.Add(new EffectDef { Kind = EffectKind.LoseMaxHp, Target = EffectTarget.Self, Amount = 2 });
    }));
    cards.Add(Card("KIND_DRAW_EXHAUST", "Draw And Exhaust", 1, e =>
    {
        e.Type = CardType.Skill;
        e.Target = CardTargetType.Self;
        e.Effects.Clear();
        e.Effects.Add(new EffectDef { Kind = EffectKind.DrawCards, Target = EffectTarget.Self, Amount = 2, Upgrade = new EffectUpgrade { AmountDelta = 1 } });
        e.Effects.Add(new EffectDef
        {
            Kind = EffectKind.ExhaustCards, Target = EffectTarget.Self,
            // 张数就是这条效果自己的数值（见 CardPickPolicy.CountFromEffect），
            // 所以填在 Amount 上，不是选牌器的 Count 里。
            Amount = 3,
            Pick = new CardPick
            {
                Source = PickSource.Piles, Piles = { PileKind.DrawPile },
                Mode = PickMode.Random,
            },
        });
    }));
    cards.Add(Card("KIND_ENCHANT", "Enchant Kind", 1, e =>
    {
        e.Type = CardType.Skill;
        e.Target = CardTargetType.Self;
        e.Effects.Clear();
        e.Effects.Add(new EffectDef
        {
            Kind = EffectKind.EnchantCardsInPile,
            Target = EffectTarget.Self,
            Amount = 2,
            EnchantmentId = "ENCHANTMENT.SHARP",
            Pick = new CardPick { Piles = { PileKind.Hand }, Mode = PickMode.All },
            Upgrade = new EffectUpgrade { AmountDelta = 1 },
        });
    }));
    cards.Add(Card("KIND_POWER", "Power Kinds", 1, e =>
    {
        e.Type = CardType.Skill;
        e.Target = CardTargetType.Self;
        e.Effects.Clear();
        e.Effects.Add(new EffectDef { Kind = EffectKind.ApplyPower, Target = EffectTarget.Self, PowerId = "POWER.STRENGTH_POWER", Amount = 2, Upgrade = new EffectUpgrade { AmountDelta = 1 } });
        e.Effects.Add(new EffectDef { Kind = EffectKind.ApplyPower, Target = EffectTarget.Self, PowerId = "POWER.DEXTERITY_POWER", Amount = 3 });
        e.Effects.Add(new EffectDef { Kind = EffectKind.ApplyPower, Target = EffectTarget.Self, PowerId = "MIGHT", Amount = 4 });
    }));
    cards.Add(Card("KIND_BOOST", "Boost Kind", 1, e =>
    {
        e.Type = CardType.Skill;
        e.Target = CardTargetType.Self;
        e.Effects.Clear();
        e.Effects.Add(new EffectDef
        {
            Kind = EffectKind.BoostCardDamage,
            Target = EffectTarget.Self,
            Amount = 2,
            Pick = new CardPick { Source = PickSource.ThisCard },
            CostDuration = CostDuration.ThisCombat,
            Upgrade = new EffectUpgrade { AmountDelta = 1 },
        });
    }));

    // ---- 计算型：第一个槽沿用原版名字，第二个起各占一套 ----
    // 第一条计算型伤害 = CalculationBase + ExtraDamage + CalculatedDamageVar。
    cards.Add(Card("CALC_DAMAGE_ONE", "Calculated Damage", 1, e =>
    {
        e.Effects.Clear();
        e.Effects.Add(CalcDamage(3, 2));
    }));

    // 两条计算型伤害：第二条走 CalculationBase2 + ExtraDamage2 + SlotCalculatedVar。
    cards.Add(Card("CALC_DAMAGE_TWO", "Two Calculated Damages", 1, e =>
    {
        e.Effects.Clear();
        e.Effects.Add(CalcDamage(3, 2));
        e.Effects.Add(CalcDamage(1, 3));
    }));

    // 计算型伤害 + 计算型格挡：基础值编号是**全局**的，所以格挡那个槽占
    // CalculationBase2——这是"两个同类第一个、但只有一个能占原版名字"的关键情形。
    cards.Add(Card("CALC_DAMAGE_BLOCK", "Calculated Damage And Block", 1, e =>
    {
        e.Type = CardType.Skill;
        e.Target = CardTargetType.AnyEnemy;
        e.Effects.Clear();
        e.Effects.Add(CalcDamage(2, 1));
        e.Effects.Add(new EffectDef
        {
            Kind = EffectKind.GainBlock,
            Target = EffectTarget.Self,
            Amount = 4,
            Source = new ValueSource { Actor = ValueSourceActor.Self, Kind = ValueSourceKind.Block, PerUnit = 2 },
        });
    }));

    // 计算型**非**伤害/格挡：游戏没有对应的计算变量，走生成类的私有字段。
    cards.Add(Card("CALC_POWER", "Calculated Power", 1, e =>
    {
        e.Type = CardType.Skill;
        e.Target = CardTargetType.Self;
        e.Effects.Clear();
        e.Effects.Add(new EffectDef
        {
            Kind = EffectKind.ApplyPower,
            Target = EffectTarget.Self,
            PowerId = "POWER.STRENGTH_POWER",
            Amount = 0,
            Source = new ValueSource { Actor = ValueSourceActor.Self, Kind = ValueSourceKind.HandCount, PerUnit = 1 },
            Upgrade = new EffectUpgrade { AmountDelta = 1, PerUnitDelta = 0.5m },
        });
    }));

    // 两条计算型非伤害/格挡：两个私有字段各占一个序号。
    cards.Add(Card("CALC_POWER_TWO", "Two Calculated Powers", 1, e =>
    {
        e.Type = CardType.Skill;
        e.Target = CardTargetType.Self;
        e.Effects.Clear();
        e.Effects.Add(new EffectDef
        {
            Kind = EffectKind.ApplyPower,
            Target = EffectTarget.Self,
            PowerId = "POWER.STRENGTH_POWER",
            Amount = 1,
            Source = new ValueSource { Actor = ValueSourceActor.Self, Kind = ValueSourceKind.HandCount },
        });
        e.Effects.Add(new EffectDef
        {
            Kind = EffectKind.ApplyPower,
            Target = EffectTarget.Self,
            PowerId = "POWER.DEXTERITY_POWER",
            Amount = 2,
            Source = new ValueSource { Actor = ValueSourceActor.Self, Kind = ValueSourceKind.DiscardPileCount },
        });
    }));

    // ---- 重复次数 ----
    // 固定次数 + 可升级 → RepeatVar（升级改的是变量）。
    cards.Add(Card("REPEAT_UPGRADE", "Repeat Upgrade", 1, e =>
    {
        e.Effects.Clear();
        e.Effects.Add(new EffectDef
        {
            Kind = EffectKind.DealDamage,
            Target = EffectTarget.Target,
            Amount = 4,
            Repeat = 2,
            Upgrade = new EffectUpgrade { AmountDelta = 1, RepeatDelta = 1 },
        });
    }));

    // 两条可升级重复：各占一个 RepeatVar（Repeat / Repeat2）。
    cards.Add(Card("REPEAT_TWO", "Two Repeats", 1, e =>
    {
        e.Effects.Clear();
        e.Effects.Add(new EffectDef
        {
            Kind = EffectKind.DealDamage,
            Target = EffectTarget.Target,
            Amount = 3,
            Repeat = 2,
            Upgrade = new EffectUpgrade { RepeatDelta = 1 },
        });
        e.Effects.Add(new EffectDef
        {
            Kind = EffectKind.DealDamage,
            Target = EffectTarget.Target,
            Amount = 5,
            Repeat = 3,
            Upgrade = new EffectUpgrade { RepeatDelta = 2 },
        });
    }));

    // 计算型次数：次数由 _calcBase/_calcPerUnit 算出，没有 RepeatVar 可改；
    // 升级改的是那两个字段（ForRepeat 那一份）。
    cards.Add(Card("REPEAT_CALC", "Calculated Repeat", 1, e =>
    {
        e.Effects.Clear();
        e.Effects.Add(new EffectDef
        {
            Kind = EffectKind.DealDamage,
            Target = EffectTarget.Target,
            Amount = 5,
            Repeat = 1,
            CalculatedRepeat = true,
            RepeatBase = 1,
            RepeatSource = new ValueSource { Actor = ValueSourceActor.Self, Kind = ValueSourceKind.HpLostThisCombat },
            Upgrade = new EffectUpgrade { RepeatDelta = 1 },
        });
    }));

    // **数值计算型 + 次数计算型同时存在**：两个方法，序号必须区分开，
    // 否则数值的增量会加到算次数的基础值上（这就是 GrudgeRepaid 那种组合）。
    cards.Add(Card("CALC_BOTH", "Calculated Value And Repeat", 1, e =>
    {
        e.Effects.Clear();
        e.Effects.Add(new EffectDef
        {
            Kind = EffectKind.ApplyPower,
            Target = EffectTarget.Self,
            PowerId = "POWER.STRENGTH_POWER",
            Amount = 1,
            Source = new ValueSource { Actor = ValueSourceActor.Self, Kind = ValueSourceKind.HandCount },
            Repeat = 1,
            CalculatedRepeat = true,
            RepeatBase = 1,
            RepeatSource = new ValueSource { Actor = ValueSourceActor.Self, Kind = ValueSourceKind.CardsPlayedThisCombat },
            Upgrade = new EffectUpgrade { AmountDelta = 2, RepeatDelta = 1 },
        });
    }));

    // ---- 等级表（多次升级）----
    // 每一级把该级该有的数值**绝对赋值**：固定值、计算型伤害/格挡（走游戏变量）、
    // 其它计算型（走私有字段）、固定次数（走 RepeatVar）、计算次数（走字段）各一档。
    cards.Add(Card("LEVEL_FIXED", "Level Fixed", 6, e =>
    {
        e.MaxUpgradeLevel = 3;
        e.EnergyCost = 2;
        e.UpgradeCostDelta = -1;
        e.Effects.Clear();
        e.Effects.Add(new EffectDef
        {
            Kind = EffectKind.DealDamage,
            Target = EffectTarget.Target,
            Amount = 6,
            Levels = new List<EffectLevel?>
            {
                new EffectLevel { Amount = 9 },
                new EffectLevel { Amount = 13 },
            },
        });
    }));
    cards.Add(Card("LEVEL_CALC", "Level Calculated", 1, e =>
    {
        e.MaxUpgradeLevel = 2;
        e.Effects.Clear();
        e.Effects.Add(new EffectDef
        {
            Kind = EffectKind.DealDamage,
            Target = EffectTarget.Target,
            Amount = 4,
            Source = new ValueSource { Actor = ValueSourceActor.Self, Kind = ValueSourceKind.HandCount, PerUnit = 2 },
            Levels = new List<EffectLevel?>
            {
                new EffectLevel { Amount = 6, PerUnit = 3 },
            },
        });
    }));
    cards.Add(Card("LEVEL_CALC_POWER", "Level Calculated Power", 1, e =>
    {
        e.Type = CardType.Skill;
        e.Target = CardTargetType.Self;
        e.MaxUpgradeLevel = 3;
        e.Effects.Clear();
        e.Effects.Add(new EffectDef
        {
            Kind = EffectKind.ApplyPower,
            Target = EffectTarget.Self,
            PowerId = "POWER.STRENGTH_POWER",
            Amount = 2,
            Source = new ValueSource { Actor = ValueSourceActor.Self, Kind = ValueSourceKind.HandCount },
            Levels = new List<EffectLevel?>
            {
                new EffectLevel { Amount = 3 },
                new EffectLevel { Amount = 5, PerUnit = 2 },
            },
        });
    }));
    cards.Add(Card("LEVEL_REPEAT", "Level Repeat", 1, e =>
    {
        e.MaxUpgradeLevel = 2;
        e.Effects.Clear();
        e.Effects.Add(new EffectDef
        {
            Kind = EffectKind.DealDamage,
            Target = EffectTarget.Target,
            Amount = 3,
            Repeat = 2,
            Levels = new List<EffectLevel?>
            {
                new EffectLevel { Amount = 4, Repeat = 3 },
            },
        });
    }));
    cards.Add(Card("LEVEL_REPEAT_CALC", "Level Calculated Repeat", 1, e =>
    {
        e.MaxUpgradeLevel = 2;
        e.Effects.Clear();
        e.Effects.Add(new EffectDef
        {
            Kind = EffectKind.DealDamage,
            Target = EffectTarget.Target,
            Amount = 3,
            Repeat = 1,
            CalculatedRepeat = true,
            RepeatBase = 1,
            RepeatSource = new ValueSource { Actor = ValueSourceActor.Self, Kind = ValueSourceKind.HpLostThisTurn },
            Levels = new List<EffectLevel?>
            {
                new EffectLevel { Repeat = 3 },
            },
        });
    }));

    // 每级换算式来源：按等级分支发 if / else if（增量模型表达不了这件事）。
    cards.Add(Card("LEVEL_SOURCE_VARIANT", "Level Source Variant", 1, e =>
    {
        e.MaxUpgradeLevel = 2;
        e.Effects.Clear();
        e.Effects.Add(new EffectDef
        {
            Kind = EffectKind.DealDamage,
            Target = EffectTarget.Target,
            Amount = 0,
            Source = new ValueSource { Actor = ValueSourceActor.Self, Kind = ValueSourceKind.HandCount },
            Levels = new List<EffectLevel?>
            {
                new EffectLevel
                {
                    Source = new ValueSource { Actor = ValueSourceActor.Self, Kind = ValueSourceKind.DiscardPileCount },
                },
            },
        });
    }));

    // ---- 永久（写存档字段）：增伤与重放各来一张 ----
    cards.Add(Card("PERMANENT_BOOST", "Permanent Boost", 1, e =>
    {
        e.Type = CardType.Skill;
        e.Target = CardTargetType.Self;
        e.Effects.Clear();
        e.Effects.Add(new EffectDef
        {
            Kind = EffectKind.BoostCardDamage,
            Target = EffectTarget.Self,
            Amount = 2,
            Pick = new CardPick { Source = PickSource.ThisCard },
            CostDuration = CostDuration.Permanent,
        });
    }));
    cards.Add(Card("ADD_REPLAY", "Add Replay", 1, e =>
    {
        e.Type = CardType.Skill;
        e.Target = CardTargetType.Self;
        e.Effects.Clear();
        e.Effects.Add(new EffectDef
        {
            Kind = EffectKind.AddReplay,
            Target = EffectTarget.Self,
            Amount = 1,
            Pick = new CardPick { Source = PickSource.ThisCard },
            CostDuration = CostDuration.ThisCombat,
        });
        e.Effects.Add(new EffectDef
        {
            Kind = EffectKind.AddReplay,
            Target = EffectTarget.Self,
            Amount = 2,
            Pick = new CardPick { Source = PickSource.ThisCard },
            CostDuration = CostDuration.Permanent,
        });
    }));

    // ---- 选牌器的几种来源与筛选，都走一遍（结构重构要保证出码不变）----
    cards.Add(Card("PICK_SCOPES", "Pick Scopes", 1, e =>
    {
        e.Type = CardType.Skill;
        e.Target = CardTargetType.Self;
        e.Effects.Clear();
        e.Effects.Add(new EffectDef
        {
            Kind = EffectKind.ModifyCardCost,
            Target = EffectTarget.Self,
            Amount = -1,
            Pick = new CardPick { Piles = { PileKind.Hand }, Mode = PickMode.Choose, Count = new NumericValue(1, new ValueSource()) },
            CostDuration = CostDuration.ThisCombat,
            Upgrade = new EffectUpgrade { AmountDelta = -1 },
        });
        e.Effects.Add(new EffectDef
        {
            Kind = EffectKind.UpgradeCardsInPile,
            Target = EffectTarget.Self,
            Amount = 1,
            Pick = new CardPick { Piles = { PileKind.DrawPile }, Mode = PickMode.Random, Count = new NumericValue(2, new ValueSource()) },
        });
        e.Effects.Add(new EffectDef
        {
            Kind = EffectKind.MoveCardsBetweenPiles,
            Target = EffectTarget.Self,
            Amount = 1,
            TargetPile = PileKind.DrawPile,
            Placement = PilePlacement.Top,
            Pick = new CardPick
            {
                Piles = { PileKind.DiscardPile },
                CardIds = { "CARD.STRIKE_IRONCLAD" },
            },
        });
        e.Effects.Add(new EffectDef
        {
            Kind = EffectKind.CopyCardsToPile,
            Target = EffectTarget.Self,
            Amount = 1,
            Pick = new CardPick { Tags = { CardTag.Strike } },
            TargetPile = PileKind.Hand,
        });
        e.Effects.Add(new EffectDef
        {
            Kind = EffectKind.TransformCardsInPile,
            Target = EffectTarget.Self,
            Amount = 1,
            Pick = new CardPick { Piles = { PileKind.Hand } },
            TransformInto = TransformInto.Random,
        });
    }));

    // ---- 两个费用维度 + 多维度并存 ----
    //
    // 覆盖新增的几条代码路径：固定阈值 / 算出来的阈值 / X 档（不读阈值）/ 阈值挂升级增量 /
    // 两个费用维度同时出现 / 维度之间"与"、维度内部"或"。描述一起进快照，所以定语链与
    // 费用从句（"能量消耗大于 2 的稀有攻击牌"那种）也在这里钉住。
    cards.Add(Card("COST_FILTER", "Cost Filter", 2, e =>
    {
        e.Type = CardType.Attack;
        e.Rarity = CardRarity.Rare;
        e.Effects.Clear();

        // 用户给的那句例子：稀有的攻击牌，且能量消耗 > 2、星辰消耗 >= 0。
        e.Effects.Add(new EffectDef
        {
            Kind = EffectKind.ModifyCardCost,
            Target = EffectTarget.Self,
            Amount = -1,
            CostDuration = CostDuration.ThisCombat,
            Pick = new CardPick
            {
                Piles = { PileKind.Hand },
                Rarities = { CardRarity.Rare },
                Types = { CardType.Attack },
                EnergyComparison = CostComparison.GreaterThan,
                StarComparison = CostComparison.AtLeast,
            },
            EnergyAmount = 2,
            StarAmount = 0,

            // 阈值也走升级增量（"升级后筛能量消耗大于 3 的牌"）。
            Upgrade = new EffectUpgrade { AmountDelta = -1, EnergyDelta = 1 },
        });

        // 阈值是算出来的（"能量消耗不大于 手牌数"）。
        e.Effects.Add(new EffectDef
        {
            Kind = EffectKind.UpgradeCardsInPile,
            Target = EffectTarget.Self,
            Amount = 1,
            Pick = new CardPick
            {
                Piles = { PileKind.Hand },
                EnergyComparison = CostComparison.AtMost,
            },
            EnergyAmount = 1,
            EnergySource = new ValueSource { Actor = ValueSourceActor.Self, Kind = ValueSourceKind.HandCount },
        });

        // X 档：不比数字，所以没有阈值（"筛 X 费的能量牌"）。
        e.Effects.Add(new EffectDef
        {
            Kind = EffectKind.CopyCardsToPile,
            Target = EffectTarget.Self,
            Amount = 1,
            TargetPile = PileKind.Hand,
            Pick = new CardPick
            {
                Piles = { PileKind.DrawPile },
                EnergyComparison = CostComparison.IsX,
                Rarities = { CardRarity.Common, CardRarity.Uncommon },
                Tags = { CardTag.Strike },
            },
        });

        // 只筛星辰（"筛需要星辰的牌"）——旧那个独立的"需要星辰"开关现在由这一维表达。
        e.Effects.Add(new EffectDef
        {
            Kind = EffectKind.ExhaustCards,
            Target = EffectTarget.Self,
            Amount = 2,
            Pick = new CardPick
            {
                Piles = { PileKind.Hand },
                StarComparison = CostComparison.NotEqual,
            },
            StarAmount = -1,
        });
    }));

    // 费用阈值 + 等级表：阈值的基础值住在卡上的变量里（EnergyAmount），
    // 等级表每一级把它绝对赋值（增量模型与等级表二者择一，与数值那边同一条规矩）。
    cards.Add(Card("COST_LEVELS", "Cost Levels", 1, e =>
    {
        e.MaxUpgradeLevel = 3;
        e.Effects.Clear();
        e.Effects.Add(new EffectDef
        {
            Kind = EffectKind.UpgradeCardsInPile,
            Target = EffectTarget.Self,
            Amount = 1,
            Pick = new CardPick
            {
                Piles = { PileKind.Hand },
                EnergyComparison = CostComparison.AtMost,
            },
            EnergyAmount = 2,
            Levels = new List<EffectLevel?>
            {
                new EffectLevel { EnergyAmount = 3 },
                new EffectLevel { EnergyAmount = 4 },
            },
        });
    }));

    // 费用阈值 + **逐级换算式来源**：阈值的来源也像数值那样可以"第 1 级改按弃牌堆数"，
    // 生成时按等级分支（分支包在阈值的前置语句里）。系数同时逐级改一次，把"基础值 /
    // 变量系数 / 来源三项都能逐级生效"这条一起覆盖到。
    cards.Add(Card("COST_SOURCE_LEVELS", "Cost Source Levels", 1, e =>
    {
        e.MaxUpgradeLevel = 2;
        e.Effects.Clear();
        e.Effects.Add(new EffectDef
        {
            Kind = EffectKind.UpgradeCardsInPile,
            Target = EffectTarget.Self,
            Amount = 1,
            Pick = new CardPick
            {
                Piles = { PileKind.Hand },
                EnergyComparison = CostComparison.AtMost,
            },
            EnergyAmount = 0,
            EnergySource = new ValueSource { Actor = ValueSourceActor.Self, Kind = ValueSourceKind.HandCount },
            Levels = new List<EffectLevel?>
            {
                new EffectLevel
                {
                    EnergySource = new ValueSource { Actor = ValueSourceActor.Self, Kind = ValueSourceKind.DiscardPileCount },
                    EnergyPerUnit = 2m,
                },
            },
        });
    }));

    // 张数是**算出来的**（手牌数 + 1）而筛选条件里的阈值**也是算出来的**（"所有牌堆里
    // 能量消耗大于 1 的牌数 + 1"）：两句"等于…"叠在一起，是消耗牌句子里最难读的一种形状。
    // 句子长这样："消耗 等于 手牌数 + 1 张能量消耗大于 … + 1 的牌。"——张数带量词"张"，
    // 不与以"…的牌"结尾的定语链叠"的"（这条曾经写错过，见 zhs.json 那段注释）。
    cards.Add(Card("EXHAUST_COUNT_FILTER", "Exhaust Count Filter", 2, e =>
    {
        e.Type = CardType.Skill;
        e.Target = CardTargetType.Self;
        e.Effects.Clear();
        e.Effects.Add(new EffectDef
        {
            Kind = EffectKind.ExhaustCards,
            Target = EffectTarget.Self,
            Amount = 1,
            Source = new ValueSource { Actor = ValueSourceActor.Self, Kind = ValueSourceKind.HandCount },
            Pick = new CardPick
            {
                Piles = { PileKind.Hand },
                Mode = PickMode.Choose,
                EnergyComparison = CostComparison.GreaterThan,
            },
            EnergyAmount = 1,
            EnergySource = new ValueSource
            {
                Actor = ValueSourceActor.Self,
                Kind = ValueSourceKind.FilteredCardCount,
                CountScope = CountScope.AllPiles,
                Filter = new CardFilter
                {
                    EnergyComparison = CostComparison.GreaterThan,
                    EnergyThreshold = 1,
                },
            },
        });
    }));

    // 变量来源的每一类读法都走一遍：玩家资源 / X 值 / 状态层数（自身与目标）/ 生物属性
    // （自身与目标）/ 计数。
    //
    // 为什么单独一张卡：来源的读法有五个分支，而原来的夹具只碰到了"牌堆张数"与"格挡"
    // 两种——结构改动时"逐字节不变"在那三种上就是空话（这条教训在效果模块搬迁时报过）。
    // 每一类各一两条，取值语句的差异（读 player / 读 who / 读 powerOwner / 转计数模块）就全在
    // 快照覆盖里了。
    cards.Add(Card("SOURCE_MATRIX", "Source Matrix", 2, e =>
    {
        e.Type = CardType.Skill;
        e.Target = CardTargetType.Self;
        e.Effects.Clear();

        var might = new ValueSource
        {
            Kind = ValueSourceKind.PowerStacks,
            PowerId = "POWER.STRENGTH_POWER",
        };

        // 状态层数：自身（取自谁不写"目标"就是自身）
        e.Effects.Add(new EffectDef
        {
            Kind = EffectKind.GainBlock,
            Target = EffectTarget.Self,
            Amount = 2,
            Source = might.Clone(),
        });

        // 状态层数：目标 + 小数系数
        e.Effects.Add(new EffectDef
        {
            Kind = EffectKind.DealDamage,
            Target = EffectTarget.Target,
            Amount = 0,
            Source = new ValueSource
            {
                Kind = ValueSourceKind.PowerStacks,
                Actor = ValueSourceActor.Target,
                PowerId = "POWER.DEXTERITY_POWER",
                PerUnit = 1.5m,
            },
        });

        // 生物属性的四种 × 自身 / 目标
        e.Effects.Add(new EffectDef
        {
            Kind = EffectKind.GainBlock,
            Target = EffectTarget.Self,
            Amount = 0,
            Source = new ValueSource { Kind = ValueSourceKind.CurrentHp },
        });
        e.Effects.Add(new EffectDef
        {
            Kind = EffectKind.Heal,
            Target = EffectTarget.Self,
            Amount = 0,
            Source = new ValueSource { Kind = ValueSourceKind.MissingHp, Actor = ValueSourceActor.Target },
        });
        e.Effects.Add(new EffectDef
        {
            Kind = EffectKind.GainBlock,
            Target = EffectTarget.Self,
            Amount = 0,
            Source = new ValueSource { Kind = ValueSourceKind.Block, Actor = ValueSourceActor.Target },
        });
        e.Effects.Add(new EffectDef
        {
            Kind = EffectKind.LoseHp,
            Target = EffectTarget.Self,
            Amount = 0,
            Source = new ValueSource { Kind = ValueSourceKind.MaxHp },
        });

        // 玩家资源与 X 值
        e.Effects.Add(new EffectDef
        {
            Kind = EffectKind.GainEnergy,
            Target = EffectTarget.Self,
            Amount = 0,
            Source = new ValueSource { Kind = ValueSourceKind.Energy },
        });
        e.Effects.Add(new EffectDef
        {
            Kind = EffectKind.GainStars,
            Target = EffectTarget.Self,
            Amount = 0,
            Source = new ValueSource { Kind = ValueSourceKind.Stars, PerUnit = 0.5m },
        });
        e.Effects.Add(new EffectDef
        {
            Kind = EffectKind.GainEnergy,
            Target = EffectTarget.Self,
            Amount = 0,
            Source = new ValueSource { Kind = ValueSourceKind.Gold },
        });
        e.Effects.Add(new EffectDef
        {
            Kind = EffectKind.GainStars,
            Target = EffectTarget.Self,
            Amount = 0,
            Source = new ValueSource { Kind = ValueSourceKind.TurnNumber },
        });
        e.Effects.Add(new EffectDef
        {
            Kind = EffectKind.GainBlock,
            Target = EffectTarget.Self,
            Amount = 0,
            Source = new ValueSource { Kind = ValueSourceKind.EnergyX },
        });
        e.Effects.Add(new EffectDef
        {
            Kind = EffectKind.GainBlock,
            Target = EffectTarget.Self,
            Amount = 0,
            Source = new ValueSource { Kind = ValueSourceKind.StarsX },
        });

        // 计数（读法在计数模块里，本模块只转交）：牌堆张数 + 战斗历史
        e.Effects.Add(new EffectDef
        {
            Kind = EffectKind.GainBlock,
            Target = EffectTarget.Self,
            Amount = 0,
            Source = new ValueSource { Kind = ValueSourceKind.HandCount },
        });
        e.Effects.Add(new EffectDef
        {
            Kind = EffectKind.DealDamage,
            Target = EffectTarget.Target,
            Amount = 3,
            Source = new ValueSource { Kind = ValueSourceKind.DamageDealtThisCombat },
        });

        // 按条件数牌：**多维并存 + 两个费用阈值**（与选牌模块同一套条件、同一个谓词生成处）。
        // 阈值是筛选自己的常数，所以谓词里写的是字面数。
        e.Effects.Add(new EffectDef
        {
            Kind = EffectKind.GainBlock,
            Target = EffectTarget.Self,
            Amount = 0,
            Source = new ValueSource
            {
                Kind = ValueSourceKind.FilteredCardCount,
                CountScope = CountScope.Hand,
                Filter = new CardFilter
                {
                    Rarities = { CardRarity.Rare },
                    Types = { CardType.Attack },
                    EnergyComparison = CostComparison.GreaterThan,
                    EnergyThreshold = 2,
                    StarComparison = CostComparison.AtLeast,
                    StarThreshold = 0,
                },
            },
        });

        // 只按关键词筛（"手牌里的消耗牌有几张"）：单维度那一档。
        e.Effects.Add(new EffectDef
        {
            Kind = EffectKind.DrawCards,
            Target = EffectTarget.Self,
            Amount = 0,
            Source = new ValueSource
            {
                Kind = ValueSourceKind.FilteredCardCount,
                CountScope = CountScope.DrawPile,
                Filter = new CardFilter { Keywords = { CardKeyword.Exhaust } },
            },
        });

        // 四个维度和**同一维多值**（类型三个、关键词两个、牌名两个）→ 走到**显式从句**写法。
        // 这是句式判据的上界那一档：定语栈在这里念不通（三种类型粘成一段、关键词自带的"的"
        // 还会叠起来），所以整句改用「…的且…的」。
        e.Effects.Add(new EffectDef
        {
            Kind = EffectKind.DealDamage,
            Target = EffectTarget.RandomEnemy,
            Amount = 0,
            Source = new ValueSource
            {
                Kind = ValueSourceKind.FilteredCardCount,
                CountScope = CountScope.AllPiles,
                Filter = new CardFilter
                {
                    Types = { CardType.Attack, CardType.Skill, CardType.Power },
                    Keywords = { CardKeyword.Retain, CardKeyword.Exhaust },
                    CardIds = { "CARD.STRIKE_IRONCLAD", "CARD.DEFEND_IRONCLAD" },
                    EnergyComparison = CostComparison.AtMost,
                    EnergyThreshold = 2,
                },
            },
        });

        // 星辰阈值也算出来一次（能量阈值那种在 COST_FILTER 里）
        e.Effects.Add(new EffectDef
        {
            Kind = EffectKind.ExhaustCards,
            Target = EffectTarget.Self,
            Amount = 1,
            Pick = new CardPick
            {
                Piles = { PileKind.Hand },
                StarComparison = CostComparison.LessThan,
            },
            StarAmount = 0,
            StarSource = new ValueSource { Kind = ValueSourceKind.TurnNumber },
        });
    }));

    project.Cards.AddRange(cards);
    return project;

    // ---- 小工具（只用于构造上面的矩阵）----

    static CardDef Card(string id, string name, int cost, Action<CardDef>? tweak = null)
    {
        var card = new CardDef
        {
            Id = id,
            Name = new LocalizedText("eng", name).With("zhs", name),
            Description = "Deal {Damage:diff()} damage.",
            Pool = "ironclad",
            Type = CardType.Attack,
            Rarity = CardRarity.Common,
            Target = CardTargetType.AnyEnemy,
            EnergyCost = cost,
            Effects = { Damage(6, upgrade: 3) },
        };

        tweak?.Invoke(card);
        return card;
    }

    static EffectDef Damage(int amount, int upgrade) => new()
    {
        Kind = EffectKind.DealDamage,
        Target = EffectTarget.Target,
        Amount = amount,
        Upgrade = new EffectUpgrade { AmountDelta = upgrade },
    };

    static EffectDef Block(int amount, int upgrade) => new()
    {
        Kind = EffectKind.GainBlock,
        Target = EffectTarget.Self,
        Amount = amount,
        Upgrade = new EffectUpgrade { AmountDelta = upgrade },
    };

    static EffectDef CalcDamage(int baseValue, decimal perUnit) => new()
    {
        Kind = EffectKind.DealDamage,
        Target = EffectTarget.Target,
        Amount = baseValue,
        Source = new ValueSource
        {
            Actor = ValueSourceActor.Self,
            Kind = ValueSourceKind.HandCount,
            PerUnit = perUnit,
        },
    };
}

//
// 效果模块搬迁时，一批搬完就跑一次快照对比；没有这份夹具，"逐字节相同"只说明没弄坏别的，
// 说明不了搬过来的那几种仍然正确。
// ---------------------------------------------------------------------------
static ModProject EffectMatrix()
{
    var project = new ModProject
    {
        Id = "EffMatrix",
        Name = "Effect Matrix",
        Version = "1.0.0",
    };

    // 生成物里会引用 `Generated.Powers` 命名空间，所以工程要有一个状态。
    project.Powers.Add(new PowerDef
    {
        Id = "MARK",
        Name = new LocalizedText("eng", "Mark").With("zhs", "印记"),
        Kind = PowerKind.Buff,
    });

    var cards = new List<CardDef>();

    // ---- 打出类 ----
    cards.Add(Card("FX_DAMAGE", EffectKind.DealDamage, e => { e.Target = EffectTarget.Target; e.Amount = 6; }));
    cards.Add(Card("FX_DAMAGE_CALC", EffectKind.DealDamage, e => { e.Target = EffectTarget.Target; e.Source = CalcSource(2); }));
    cards.Add(Card("FX_BLOCK", EffectKind.GainBlock, e => { e.Target = EffectTarget.Self; e.Amount = 5; }));
    cards.Add(Card("FX_BLOCK_CALC", EffectKind.GainBlock, e => { e.Target = EffectTarget.Self; e.Source = CalcSource(1); }));
    cards.Add(Card("FX_DRAW", EffectKind.DrawCards, e => { e.Target = EffectTarget.Self; e.Amount = 2; }));
    cards.Add(Card("FX_DRAW_CALC", EffectKind.DrawCards, e => { e.Target = EffectTarget.Self; e.Source = CalcSource(1); }));
    cards.Add(Card("FX_ENERGY", EffectKind.GainEnergy, e => { e.Target = EffectTarget.Self; e.Amount = 1; }));
    cards.Add(Card("FX_ENERGY_CALC", EffectKind.GainEnergy, e => { e.Target = EffectTarget.Self; e.Source = CalcSource(1); }));
    cards.Add(Card("FX_STARS", EffectKind.GainStars, e => { e.Target = EffectTarget.Self; e.Amount = 2; }));
    cards.Add(Card("FX_STARS_CALC", EffectKind.GainStars, e => { e.Target = EffectTarget.Self; e.Source = CalcSource(1); }));
    cards.Add(Card("FX_HEAL", EffectKind.Heal, e => { e.Target = EffectTarget.Self; e.Amount = 4; }));
    cards.Add(Card("FX_HEAL_OTHER", EffectKind.Heal, e => { e.Target = EffectTarget.Target; e.Amount = 3; }));
    cards.Add(Card("FX_LOSE_HP", EffectKind.LoseHp, e => { e.Target = EffectTarget.Self; e.Amount = 3; }));
    cards.Add(Card("FX_GAIN_MAXHP", EffectKind.GainMaxHp, e => { e.Target = EffectTarget.Self; e.Amount = 5; }));
    cards.Add(Card("FX_LOSE_MAXHP", EffectKind.LoseMaxHp, e => { e.Target = EffectTarget.Self; e.Amount = 2; }));

    // ---- 批 2 补的覆盖：这六条的**其余分支** ----
    //
    // 上面每一条只走了一种形状，而"选哪个句式 / 发哪条语句"现在由各效果自己的行为决定，
    // 所以**没走到的那一支就等于没验**（这条教训在效果模块批 1 上记过：夹具没覆盖时
    // 逐字节 diff 为空说明不了任何事）。这里把两条轴上都补齐：
    //   目标轴 —— 给别人的（blockOther）/ 给队友的（AllAllies 那支循环）
    //   数值轴 —— 计算型的（*Calc）
    cards.Add(Card("FX_BLOCK_OTHER", EffectKind.GainBlock,
        e => { e.Target = EffectTarget.Target; e.Amount = 4; }));
    cards.Add(Card("FX_BLOCK_OTHER_CALC", EffectKind.GainBlock,
        e => { e.Target = EffectTarget.Target; e.Source = CalcSource(2); }));
    cards.Add(Card("FX_BLOCK_ALLIES", EffectKind.GainBlock,
        e => { e.Target = EffectTarget.AllAllies; e.Amount = 3; }));
    cards.Add(Card("FX_HEAL_CALC", EffectKind.Heal,
        e => { e.Target = EffectTarget.Self; e.Source = CalcSource(1); }));
    cards.Add(Card("FX_HEAL_OTHER_CALC", EffectKind.Heal,
        e => { e.Target = EffectTarget.Target; e.Source = CalcSource(1); }));
    cards.Add(Card("FX_HEAL_ALLIES", EffectKind.Heal,
        e => { e.Target = EffectTarget.AllAllies; e.Amount = 2; }));
    cards.Add(Card("FX_LOSE_HP_CALC", EffectKind.LoseHp,
        e => { e.Target = EffectTarget.Self; e.Source = CalcSource(1); }));
    cards.Add(Card("FX_GAIN_MAXHP_CALC", EffectKind.GainMaxHp,
        e => { e.Target = EffectTarget.Self; e.Source = CalcSource(1); }));
    cards.Add(Card("FX_LOSE_MAXHP_CALC", EffectKind.LoseMaxHp,
        e => { e.Target = EffectTarget.Self; e.Source = CalcSource(1); }));
    // 降低最大生命给敌人（这是它唯一"给别人"的用法，走 CardTarget 那一支）。
    cards.Add(Card("FX_LOSE_MAXHP_OTHER", EffectKind.LoseMaxHp,
        e => { e.Target = EffectTarget.Target; e.Amount = 1; }));

    // ---- 状态类 ----
    cards.Add(Card("FX_APPLY_POWER", EffectKind.ApplyPower,
        e => { e.Target = EffectTarget.Self; e.PowerId = "POWER.STRENGTH_POWER"; e.Amount = 2; }));
    cards.Add(Card("FX_APPLY_POWER_OTHER", EffectKind.ApplyPower,
        e => { e.Target = EffectTarget.Target; e.PowerId = "POWER.VULNERABLE_POWER"; e.Amount = 2; }));
    cards.Add(Card("FX_APPLY_POWER_CALC", EffectKind.ApplyPower,
        e => { e.Target = EffectTarget.Self; e.PowerId = "POWER.STRENGTH_POWER"; e.Source = CalcSource(1); }));
    cards.Add(Card("FX_REMOVE_POWER", EffectKind.RemovePower,
        e => { e.Target = EffectTarget.Target; e.PowerId = "POWER.STRENGTH_POWER"; }));

    // ---- 批 4 补的覆盖：移除状态的另外几条路径 ----
    //
    // 这个效果现在有**两条轴**：移除方式（整个 / 只减几层）× 移除哪一类
    //（按 id / 所有正面 / 所有负面）。上面那张只走了"整个 + 按 id"那一格，
    // 而其余五格各有各的语句与句子——**没走到的那一格等于没验**
    // （这条教训在批 1 上记过：夹具没覆盖时逐字节 diff 为空说明不了任何事）。
    cards.Add(Card("FX_REMOVE_PARTIAL", EffectKind.RemovePower, e =>
    {
        e.Target = EffectTarget.Target;
        e.PowerId = "POWER.STRENGTH_POWER";
        e.Removal = PowerRemoval.Partial;
        e.Amount = 2;
    }));

    // 部分移除 + 层数是**算出来的**（走三件套里的来源）。
    cards.Add(Card("FX_REMOVE_PARTIAL_CALC", EffectKind.RemovePower, e =>
    {
        e.Target = EffectTarget.Target;
        e.PowerId = "POWER.VULNERABLE_POWER";
        e.Removal = PowerRemoval.Partial;
        e.Source = CalcSource(1);
    }));

    cards.Add(Card("FX_REMOVE_BUFF", EffectKind.RemovePower, e =>
    {
        e.Target = EffectTarget.Self;
        e.Selection = PowerSelection.Buff;
    }));

    cards.Add(Card("FX_REMOVE_DEBUFF", EffectKind.RemovePower, e =>
    {
        e.Target = EffectTarget.Self;
        e.Selection = PowerSelection.Debuff;
    }));

    // 按类别 + 部分移除：每个匹配到的状态各减 N 层（这条同时覆盖"遍历时用快照"那条注释）。
    cards.Add(Card("FX_REMOVE_DEBUFF_PARTIAL", EffectKind.RemovePower, e =>
    {
        e.Target = EffectTarget.Target;
        e.Selection = PowerSelection.Debuff;
        e.Removal = PowerRemoval.Partial;
        e.Amount = 1;
    }));

    // ---- 目标选择器模块的覆盖 ----
    //
    // 目标现在由一个模块展开（Modules/Targets/），分**两个作用域**：造物轴落到 Creature、
    // 玩家轴落到 Player。每一轴各有"一批"与"随机一个"两种写法，玩家轴的"一批"取的是
    // CombatState.Players——这些分支没走到就等于没验（与上面几条同一条教训）。
    //
    // 两轴各自的目标集合是模块给的（造物轴七档、玩家轴四档），所以这里只需覆盖**展开形状**。
    cards.Add(Card("FX_TARGET_RANDOM_ALLY", EffectKind.DealDamage,
        e => { e.Target = EffectTarget.RandomAlly; e.Amount = 3; }));
    cards.Add(Card("FX_TARGET_EVERYONE", EffectKind.DealDamage,
        e => { e.Target = EffectTarget.All; e.Amount = 2; }));
    cards.Add(Card("FX_TARGET_EVERYONE_OTHER", EffectKind.ApplyPower, e =>
    {
        e.Target = EffectTarget.All;
        e.PowerId = "POWER.VULNERABLE_POWER";
        e.Amount = 1;
    }));

    // 玩家轴：两条"一批玩家"（单行体 / 多行体）与两条"随机一个玩家"。
    cards.Add(Card("FX_TARGET_STARS_ALLIES", EffectKind.GainStars,
        e => { e.Target = EffectTarget.AllAllies; e.Amount = 1; }));
    cards.Add(Card("FX_TARGET_ORBSLOTS_ALLIES", EffectKind.AddOrbSlots,
        e => { e.Target = EffectTarget.AllAllies; e.Amount = 1; }));
    cards.Add(Card("FX_TARGET_CHANNEL_ALLIES", EffectKind.ChannelOrb,
        e => { e.Target = EffectTarget.AllAllies; e.Amount = 1; e.Orb = OrbKind.Lightning; }));
    cards.Add(Card("FX_TARGET_EVOKE_ALLIES", EffectKind.EvokeOrbs,
        e => { e.Target = EffectTarget.AllAllies; e.Amount = 1; }));
    cards.Add(Card("FX_TARGET_SUMMON_ALLIES", EffectKind.Summon,
        e => { e.Target = EffectTarget.AllAllies; e.Amount = 4; }));
    cards.Add(Card("FX_TARGET_FORGE_ALLIES", EffectKind.Forge,
        e => { e.Target = EffectTarget.AllAllies; e.Amount = 2; }));
    cards.Add(Card("FX_TARGET_DRAW_ALLY", EffectKind.DrawCards,
        e => { e.Target = EffectTarget.RandomAlly; e.Amount = 1; }));

    // 玩家轴的"选定目标"：生成的是 `cardPlay.Target.Player!`，所以这张牌的目标要能给出
    // 一个**玩家**（"任意友方"是唯一那一档）。
    cards.Add(Card("FX_TARGET_ENERGY_OTHER", EffectKind.GainEnergy,
        e => { e.Target = EffectTarget.Target; e.Amount = 1; }));
    cards[^1].Target = CardTargetType.AnyAlly;

    // 状态里的玩家轴：这条要多一行 `using …Entities.Players;`（按需发，见 CodeGenerator），
    // 而且目标循环走的是状态上下文的表达式——两种上下文各验一次。
    project.Powers.Add(new PowerDef
    {
        Id = "RALLY",
        Name = new LocalizedText("eng", "Rally").With("zhs", "鼓舞"),
        Description = "At the end of your turn, all players gain 1 energy.",
        Kind = PowerKind.Buff,
        Triggers =
        {
            new TriggeredEffect
            {
                When = PowerTrigger.TurnEnd,
                Effect = new EffectDef
                {
                    Kind = EffectKind.GainEnergy,
                    Target = EffectTarget.AllAllies,
                    Amount = 1,
                },
            },
        },
    });

    // 随机一名玩家（含自己）——玩家轴专有的一档（造物轴上那等于先随机一个人再取生物，
    // 与"随机一名友方"重复）。
    cards.Add(Card("FX_TARGET_RANDOM_PLAYER", EffectKind.GainEnergy,
        e => { e.Target = EffectTarget.RandomPlayer; e.Amount = 1; }));

    // ---- "其他友方 / 其他玩家"（不含自己）----
    //
    // 与"所有友方 / 随机一名友方"的分界就是**含不含自己**：原版"所有友方"含自己，
    // 而"任意友方"（AnyAlly）的候选排除自己。两种口径各来一条，且两轴各覆盖一次
    // （造物轴落到其它玩家的生物、玩家轴落到其它玩家）。
    cards.Add(Card("FX_TARGET_OTHER_ALLIES", EffectKind.GainBlock, e =>
    {
        e.Target = EffectTarget.AllOtherAllies;
        e.Amount = 2;
    }));
    cards.Add(Card("FX_TARGET_RANDOM_OTHER_ALLY", EffectKind.DealDamage, e =>
    {
        e.Target = EffectTarget.RandomOtherAlly;
        e.Amount = 3;
    }));
    cards.Add(Card("FX_TARGET_OTHER_PLAYERS", EffectKind.GainStars, e =>
    {
        e.Target = EffectTarget.AllOtherAllies;
        e.Amount = 1;
    }));
    cards.Add(Card("FX_TARGET_RANDOM_OTHER_PLAYER", EffectKind.GainEnergy, e =>
    {
        e.Target = EffectTarget.RandomOtherAlly;
        e.Amount = 1;
    }));

    // 「随机一名友方」抽的是**全集**（含自己），与"随机一名敌人"从全体敌人里抽同一个口径；
    // 「随机一名友方的奥斯提」同理（含自己那只）。下面两条与上面的"其他"正好对照。
    cards.Add(Card("FX_TARGET_RANDOM_ALLY_SELF", EffectKind.GainBlock, e =>
    {
        e.Target = EffectTarget.RandomAlly;
        e.Amount = 1;
    }));
    cards.Add(Card("FX_PET_RANDOM_ALLY_SELF", EffectKind.ApplyPower, e =>
    {
        e.Target = EffectTarget.RandomAllyPet;
        e.PowerId = "POWER.MARK";
        e.Amount = 1;
    }));

    // ---- 玩家宠物那一组（从玩家派生：`player.Osty`）----
    //
    // 它是**独立的一组目标档**（"自己的奥斯提 / 目标的奥斯提 / 所有友方的奥斯提 /
    // 随机一名友方的奥斯提"），不是"某一档 + 勾一个取其宠物"。四档各有各的形状：
    // 自己那只 / 卡牌选中那名玩家的 / 每个玩家各一只 / 随机一名玩家的，
    // 而每一档都**必须带判空**（不在场时 Osty 是 null）。四条分支没走到就等于没验。
    //
    // 用九种效果各来一条：宠物那一组对**所有造物轴的效果**都成立
    // （造物轴的命令都收 Creature，宠物就是 Creature），所以夹具要铺开覆盖面。
    cards.Add(Card("FX_PET_SELF", EffectKind.ApplyPower, e =>
    {
        e.Target = EffectTarget.SelfPet;
        e.PowerId = "POWER.MARK";
        e.Amount = 2;
    }));
    cards.Add(Card("FX_PET_ALL_ALLIES", EffectKind.ApplyPower, e =>
    {
        e.Target = EffectTarget.AllAlliesPet;
        e.PowerId = "POWER.MARK";
        e.Amount = 1;
    }));
    cards.Add(Card("FX_PET_RANDOM_ALLY", EffectKind.GainBlock, e =>
    {
        e.Target = EffectTarget.RandomAllyPet;
        e.Amount = 3;
    }));
    cards.Add(Card("FX_PET_TARGET", EffectKind.Heal, e =>
    {
        e.Target = EffectTarget.TargetPet;
        e.Amount = 4;
    }));
    cards[^1].Target = CardTargetType.AnyAlly;

    cards.Add(Card("FX_PET_MAXHP", EffectKind.GainMaxHp, e =>
    {
        e.Target = EffectTarget.SelfPet;
        e.Amount = 5;
    }));
    cards.Add(Card("FX_PET_LOSE_MAXHP", EffectKind.LoseMaxHp, e =>
    {
        e.Target = EffectTarget.AllAlliesPet;
        e.Amount = 2;
    }));
    cards.Add(Card("FX_PET_LOSE_HP", EffectKind.LoseHp, e =>
    {
        e.Target = EffectTarget.RandomAllyPet;
        e.Amount = 3;
    }));
    cards.Add(Card("FX_PET_REMOVE_POWER", EffectKind.RemovePower, e =>
    {
        e.Target = EffectTarget.TargetPet;
        e.PowerId = "POWER.MARK";
        e.Removal = PowerRemoval.Partial;
        e.Amount = 2;
    }));
    cards[^1].Target = CardTargetType.AnyAlly;
    cards.Add(Card("FX_PET_DOOM", EffectKind.TriggerDoom, e =>
    {
        e.Target = EffectTarget.RandomAllyPet;
    }));

    // "其他玩家的奥斯提"（不含自己那只）：一批各一次 + 随机一只。
    // 与上面的 AllAlliesPet / RandomAllyPet 正好是"含自己 vs 不含自己"的对照。
    cards.Add(Card("FX_PET_OTHER_ALLIES", EffectKind.ApplyPower, e =>
    {
        e.Target = EffectTarget.AllOtherAlliesPet;
        e.PowerId = "POWER.MARK";
        e.Amount = 1;
    }));
    cards.Add(Card("FX_PET_RANDOM_OTHER", EffectKind.Heal, e =>
    {
        e.Target = EffectTarget.RandomOtherAllyPet;
        e.Amount = 3;
    }));
    cards[^1].Target = CardTargetType.AnyAlly;

    // "所有友方"**不含宠物**：生成的是 `PlayerCreatures`（原版口径，见 LegionOfBone），
    // 想作用到宠物上得显式选宠物那一档。上面 FX_PET_ALL_ALLIES 与这条正好是一对对照。
    cards.Add(Card("FX_ALLY_NO_PET", EffectKind.GainBlock, e =>
    {
        e.Target = EffectTarget.AllAllies;
        e.Amount = 2;
    }));

    // 灾厄判定作用于**自己**：模板里那个 {who} 在"自己"那一档是空词，
    // 所以自己与别人必须分成两条模板（不分就念成"立即对执行灾厄判定"）。
    cards.Add(Card("FX_DOOM_SELF", EffectKind.TriggerDoom, e =>
    {
        e.Target = EffectTarget.Self;
    }));

    // 状态里的宠物档：上下文不同（`Owner.Player!` 而不是 `Owner`）。
    project.Powers.Add(new PowerDef
    {
        Id = "PET_TICK",
        Name = new LocalizedText("eng", "Pet Tick").With("zhs", "宠物节拍"),
        Description = "At the end of your turn, your pet gains 1 Mark.",
        Kind = PowerKind.Buff,
        Triggers =
        {
            new TriggeredEffect
            {
                When = PowerTrigger.TurnEnd,
                Effect = new EffectDef
                {
                    Kind = EffectKind.ApplyPower,
                    Target = EffectTarget.SelfPet,
                    PowerId = "POWER.MARK",
                    Amount = 1,
                },
            },
        },
    });

    // ---- 临时牌集合（把一组牌存起来，后面的效果再动它们）----
    //
    // 一次出牌结算期间：存（抽牌 / 挑牌）→ 取（降费 / 附魔…）。四条路径各要覆盖：
    //   抽到的牌 → 降费（卡最典型的用法，也是用户举的例子）
    //   挑出的牌（消耗牌）→ 同一个集合被两条效果引用（取两次）
    //   加入指定卡牌 → 附魔
    //   取一个**没人定义**的集合（校验器报错，生成侧要给空列表而不是崩）
    cards.Add(Card("FX_SET_DRAW", EffectKind.DrawCards, e =>
    {
        e.Target = EffectTarget.Self;
        e.Amount = 2;
        e.SetId = "Drawn";
    }));
    cards[^1].Effects.Add(new EffectDef
    {
        Kind = EffectKind.ModifyCardCost,
        Target = EffectTarget.Self,
        Amount = -1,
        CostDuration = CostDuration.ThisCombat,
        SetId = null,
        Pick = new CardPick { Source = PickSource.Set, SetId = "Drawn", Mode = PickMode.All },
    });

    // 同一个集合被两条效果各取一次（键里的"定义编号"必须两条都指到抽牌那一条）。
    cards.Add(Card("FX_SET_TWICE", EffectKind.ExhaustCards, e =>
    {
        e.Target = EffectTarget.Self;
        e.Amount = 2;
        e.SetId = "Picked";
        e.Pick = new CardPick
        {
            Source = PickSource.Piles, Piles = { PileKind.Hand }, Mode = PickMode.All,
        };
    }));
    // 两条**无名**的选牌效果（降费 + 升级）：这是"局部变量重名"那个坑的最小复现
    //（两条都写 `var pickedCards` 就编译不过），第二个起必须带后缀。
    cards[^1].Effects.Add(new EffectDef
    {
        Kind = EffectKind.ModifyCardCost,
        Target = EffectTarget.Self,
        Amount = -1,
        Pick = new CardPick { Source = PickSource.Set, SetId = "Picked", Mode = PickMode.All },
    });
    cards[^1].Effects.Add(new EffectDef
    {
        Kind = EffectKind.UpgradeCardsInPile,
        Target = EffectTarget.Self,
        Pick = new CardPick { Source = PickSource.Set, SetId = "Picked", Mode = PickMode.All },
    });
    cards[^1].MaxUpgradeLevel = 0;

    // 加入指定卡牌 → 给这 2 张附魔；抽牌还作用于**多个玩家**（集合要在循环外攒）。
    cards.Add(Card("FX_SET_ADD", EffectKind.AddNamedCardToPile, e =>
    {
        e.Target = EffectTarget.Self;
        e.Amount = 2;
        e.CardId = "CARD.STRIKE_IRONCLAD";
        e.TargetPile = PileKind.Hand;
        e.SetId = "Added";
        e.Pick = new CardPick();
    }));
    cards[^1].Effects.Add(new EffectDef
    {
        Kind = EffectKind.EnchantCardsInPile,
        Target = EffectTarget.Self,
        Amount = 1,
        EnchantmentId = "ENCHANTMENT.SHARP",
        Pick = new CardPick { Source = PickSource.Set, SetId = "Added", Mode = PickMode.All },
    });

    // 抽牌作用于所有玩家（玩家轴）+ 存集合：集合的攒法要包在目标循环外面。
    cards.Add(Card("FX_SET_DRAW_ALLIES", EffectKind.DrawCards, e =>
    {
        e.Target = EffectTarget.AllAllies;
        e.Amount = 1;
        e.SetId = "Each";
    }));
    cards[^1].Effects.Add(new EffectDef
    {
        Kind = EffectKind.SetCardsFree,
        Target = EffectTarget.Self,
        Pick = new CardPick { Source = PickSource.Set, SetId = "Each", Mode = PickMode.All },
        CostDuration = CostDuration.ThisTurn,
    });

    // ---- 牌堆操作 ----
    // 消耗："此牌"是作用对象的一档，所以这两张卡考的是同一效果的两种作用对象。
    cards.Add(Card("FX_EXHAUST_SELF", EffectKind.ExhaustCards, e =>
    {
        e.Target = EffectTarget.Self; e.Pick = new CardPick { Source = PickSource.ThisCard };
    }));
    cards.Add(Card("FX_COPY", EffectKind.CopyCardsToPile, e =>
    {
        e.Target = EffectTarget.Self; e.Amount = 1;
        e.Pick = new CardPick { Source = PickSource.ThisCard }; e.TargetPile = PileKind.Hand;
    }));
    cards.Add(Card("FX_ADD_NAMED", EffectKind.AddNamedCardToPile, e =>
    {
        e.Target = EffectTarget.Self; e.Amount = 1;
        e.CardId = "CARD.STRIKE_IRONCLAD"; e.TargetPile = PileKind.Hand;
    }));
    cards.Add(Card("FX_EXHAUST_PILE", EffectKind.ExhaustCards, e =>
    {
        e.Target = EffectTarget.Self;

        // 张数 = 效果数值（Amount）——消耗牌走这条；并且带一个升级增量，
        // 于是快照里能看到"升级改的是 ExhaustCount 那个变量"。
        e.Amount = 2;
        e.Upgrade = new EffectUpgrade { AmountDelta = 1 };
        e.Pick = new CardPick
        {
            Source = PickSource.Piles, Piles = { PileKind.DrawPile },
            Mode = PickMode.Choose,
        };
    }));
    // 张数是**算出来的**：0 + 星辰数 × 1，而且"至多"（玩家可以不选，所以张数不保底到 1）。
    // 这一条考的是"变量模块给的表达式直接当张数"那条路（选牌器自己不再带变量）。
    cards.Add(Card("FX_EXHAUST_CALC", EffectKind.ExhaustCards, e =>
    {
        e.Target = EffectTarget.Self;
        e.Amount = 0;
        e.Source = new ValueSource { Actor = ValueSourceActor.Self, Kind = ValueSourceKind.Stars };
        e.Pick = new CardPick
        {
            Source = PickSource.Piles, Piles = { PileKind.Hand },
            Mode = PickMode.AtMost,
        };
    }));
    cards.Add(Card("FX_DISCARD_PILE", EffectKind.DiscardCardsInPile, e =>
    {
        e.Target = EffectTarget.Self;
        e.Pick = new CardPick
        {
            Source = PickSource.Piles, Piles = { PileKind.Hand },
            Mode = PickMode.Random, Count = new NumericValue(2, new ValueSource()),
        };
    }));

    // ---- 从卡池 N 选一（生成方式第二路，见 AddCardMode）----
    // 弹选牌界面那一路：候选数 3、按"稀有攻击"筛（谓词走 CardPickFilter，对卡池的牌判）。
    // 选中的牌存进临时牌集合——这条效果是集合的**写侧**（它生成牌，不改造牌）。
    cards.Add(Card("FX_ADD_POOL", EffectKind.AddNamedCardToPile, e =>
    {
        e.Target = EffectTarget.Self;
        e.AddMode = AddCardMode.ChooseFromPool;
        e.Candidates = 3;
        e.PoolFilter.Rarities.Add(CardRarity.Rare);
        e.PoolFilter.Types.Add(CardType.Attack);
        e.TargetPile = PileKind.Hand;
        e.SetId = "Chosen";
    }));
    cards[^1].Effects.Add(new EffectDef
    {
        Kind = EffectKind.SetCardsFree,
        Target = EffectTarget.Self,
        Pick = new CardPick { Source = PickSource.Set, SetId = "Chosen", Mode = PickMode.All },
        CostDuration = CostDuration.ThisTurn,
    });

    // 随机那一路（候选数 1）：不弹界面，张数走**算出来的**表达式（0 + 手牌数 × 1），
    // 而且不筛任何维度（生成"整个卡池随机"——游戏自己的生成门槛仍然生效）。
    cards.Add(Card("FX_ADD_POOL_RAND", EffectKind.AddNamedCardToPile, e =>
    {
        e.Target = EffectTarget.Self;
        e.AddMode = AddCardMode.ChooseFromPool;
        e.Candidates = 1;
        e.Amount = 0;
        e.Source = new ValueSource { Actor = ValueSourceActor.Self, Kind = ValueSourceKind.HandCount };
        e.TargetPile = PileKind.DrawPile;
        e.Placement = PilePlacement.Random;
    }));

    // ---- 启用条件 ----
    // "若手牌数不小于 3，造成 6 点伤害"：左值是算出来的（0 + 手牌数 × 1，进字段 + 取值方法），
    // 右值是字面量；左值带升级增量（升级放宽条件），升级语句走"字段 +="。
    // 守卫包在整条效果外面——与数值、升级语句的相对位置在快照里看得到。
    cards.Add(Card("FX_COND", EffectKind.DealDamage, e =>
    {
        e.Target = EffectTarget.Target;
        e.Amount = 6;
        e.Condition = new EffectCondition
        {
            Comparison = ConditionComparison.AtLeast,
            LeftAmount = 0,
            LeftSource = new ValueSource { Actor = ValueSourceActor.Self, Kind = ValueSourceKind.HandCount },
            RightAmount = 3,
            LeftUpgrade = 1,
        };
    }));

    // 反向组合：左值固定（字面量，不进字段）、右值是算出来的（方法在右侧），
    // 比较方式换"等于"一档，且没有升级。两侧的取法在这里对调着验一遍。
    cards.Add(Card("FX_COND_RIGHT", EffectKind.GainBlock, e =>
    {
        e.Target = EffectTarget.Self;
        e.Amount = 5;
        e.Condition = new EffectCondition
        {
            Comparison = ConditionComparison.Equal,
            LeftAmount = 2,
            RightSource = new ValueSource { Actor = ValueSourceActor.Self, Kind = ValueSourceKind.Stars },
        };
    }));

    // ---- 充能球 ----
    cards.Add(Card("FX_CHANNEL", EffectKind.ChannelOrb,
        e => { e.Target = EffectTarget.Self; e.Amount = 1; e.Orb = OrbKind.Lightning; }));
    cards.Add(Card("FX_CHANNEL_FROST", EffectKind.ChannelOrb,
        e => { e.Target = EffectTarget.Self; e.Amount = 2; e.Orb = OrbKind.Frost; }));
    cards.Add(Card("FX_EVOKE", EffectKind.EvokeOrbs, e => { e.Target = EffectTarget.Self; e.Amount = 2; }));
    // 最左侧那一路（另一份接口：EvokeLast）。
    cards.Add(Card("FX_EVOKE_LEFT", EffectKind.EvokeOrbs, e =>
    {
        e.Target = EffectTarget.Self; e.Amount = 1; e.EvokeTarget = OrbEvokeTarget.Leftmost;
    }));
    // 全部（原版碎片的形状）。
    cards.Add(Card("FX_EVOKE_ALL", EffectKind.EvokeOrbs, e =>
    {
        e.Target = EffectTarget.Self; e.Amount = 2; e.EvokeTarget = OrbEvokeTarget.All;
    }));
    // 随机（自己挑一个：要挪到队首再激发）。
    cards.Add(Card("FX_EVOKE_RANDOM", EffectKind.EvokeOrbs, e =>
    {
        e.Target = EffectTarget.Self; e.Amount = 1; e.EvokeTarget = OrbEvokeTarget.Random;
    }));
    // 限定种类（按球的模型 id 筛）。
    cards.Add(Card("FX_EVOKE_TYPED", EffectKind.EvokeOrbs, e =>
    {
        e.Target = EffectTarget.Self; e.Amount = 1;
        e.EvokeTarget = OrbEvokeTarget.All;
        e.OrbKinds.Add(OrbKind.Lightning);
        e.OrbKinds.Add(OrbKind.Frost);
    }));
    cards.Add(Card("FX_ORB_SLOTS", EffectKind.AddOrbSlots, e => { e.Target = EffectTarget.Self; e.Amount = 1; }));

    // 数值是**算出来的**那三条球效果：卡面的说法与固定值不同
    // （"获得 等于 手牌数 个充能球栏位"），所以各来一张，让快照把两种说法都钉住。
    cards.Add(Card("FX_ORB_SLOTS_CALC", EffectKind.AddOrbSlots, e => { e.Target = EffectTarget.Self; e.Source = CalcSource(1); }));
    cards.Add(Card("FX_CHANNEL_CALC", EffectKind.ChannelOrb, e =>
    {
        e.Target = EffectTarget.Self; e.Source = CalcSource(1); e.Orb = OrbKind.Frost;
    }));
    cards.Add(Card("FX_EVOKE_CALC", EffectKind.EvokeOrbs, e =>
    {
        e.Target = EffectTarget.Self; e.Source = CalcSource(1);
        e.EvokeTarget = OrbEvokeTarget.All;
        e.OrbKinds.Add(OrbKind.Lightning);
    }));

    // ---- 召唤 / 铸造 ----
    cards.Add(Card("FX_SUMMON", EffectKind.Summon, e => { e.Target = EffectTarget.Self; e.Amount = 4; }));
    // 召唤的**计算型**：生命值是算出来的（"召唤等于手牌数生命的召唤物"）。
    // 这一支以前没有夹具，而"算出来的句子要念来源"正是批 3 修掉的那条缺口——
    // 没夹具的话，那句错话（只念基础值）在逐字节对比里根本不会露面。
    cards.Add(Card("FX_SUMMON_CALC", EffectKind.Summon,
        e => { e.Target = EffectTarget.Self; e.Source = CalcSource(1); }));
    cards.Add(Card("FX_FORGE", EffectKind.Forge, e => { e.Target = EffectTarget.Self; e.Amount = 3; }));
    cards.Add(Card("FX_FORGE_CALC", EffectKind.Forge,
        e => { e.Target = EffectTarget.Self; e.Source = CalcSource(1); }));

    // ---- 附魔 / 伤害增益 / 灾厄 ----
    cards.Add(Card("FX_ENCHANT", EffectKind.EnchantCardsInPile, e =>
    {
        e.Target = EffectTarget.Self; e.Amount = 1;
        e.EnchantmentId = "ENCHANTMENT.SHARP";
        e.Pick = new CardPick
        {
            Source = PickSource.Piles, Piles = { PileKind.Hand }, Mode = PickMode.All,
        };
    }));
    cards.Add(Card("FX_BOOST", EffectKind.BoostCardDamage, e =>
    {
        e.Target = EffectTarget.Self; e.Amount = 2;
        e.Pick = new CardPick { Source = PickSource.ThisCard }; e.CostDuration = CostDuration.ThisCombat;
    }));
    cards.Add(Card("FX_BOOST_PERMANENT", EffectKind.BoostCardDamage, e =>
    {
        e.Target = EffectTarget.Self; e.Amount = 2;
        e.Pick = new CardPick { Source = PickSource.ThisCard }; e.CostDuration = CostDuration.Permanent;
    }));
    cards.Add(Card("FX_DOOM", EffectKind.TriggerDoom, e => e.Target = EffectTarget.Target));
    // 灾厄判定的另外两支目标写法：全体敌人走 foreach、随机敌人走"先挑一个"。
    // 这两支以前没夹具（只有"单个目标"那一支），而选哪一支现在由它自己的行为决定。
    cards.Add(Card("FX_DOOM_ALL", EffectKind.TriggerDoom, e => e.Target = EffectTarget.AllEnemies));
    cards.Add(Card("FX_DOOM_RANDOM", EffectKind.TriggerDoom, e => e.Target = EffectTarget.RandomEnemy));

    // ---- 自动打出 ----
    cards.Add(Card("FX_AUTOPLAY", EffectKind.AutoPlay, e =>
    {
        e.Target = EffectTarget.Self; e.Amount = 1;
        e.Pick = new CardPick { Piles = { PileKind.DrawPile }, Mode = PickMode.All };
    }));

    // ---- 费用类 ----
    cards.Add(Card("FX_COST_DOWN", EffectKind.ModifyCardCost, e =>
    {
        e.Target = EffectTarget.Self; e.Amount = -1;
        e.Pick = new CardPick { Piles = { PileKind.Hand }, Mode = PickMode.Choose };
        e.CostDuration = CostDuration.ThisCombat;
    }));
    cards.Add(Card("FX_COST_UP", EffectKind.ModifyCardCost, e =>
    {
        e.Target = EffectTarget.Self; e.Amount = 2;
        e.Pick = new CardPick { Piles = { PileKind.Hand }, Mode = PickMode.All };
        e.CostDuration = CostDuration.ThisTurn;
    }));
    cards.Add(Card("FX_FREE", EffectKind.SetCardsFree, e =>
    {
        e.Target = EffectTarget.Self;
        e.Pick = new CardPick { Piles = { PileKind.Hand }, Mode = PickMode.All };
        e.CostDuration = CostDuration.ThisTurn;
    }));

    // ---- 改造牌堆里的牌 ----
    cards.Add(Card("FX_UPGRADE", EffectKind.UpgradeCardsInPile, e =>
    {
        e.Target = EffectTarget.Self; e.Amount = 1;
        e.Pick = new CardPick { Piles = { PileKind.Hand }, Mode = PickMode.Choose };

    }));
    cards.Add(Card("FX_REPLAY", EffectKind.AddReplay, e =>
    {
        e.Target = EffectTarget.Self; e.Amount = 1;
        e.Pick = new CardPick { Source = PickSource.ThisCard }; e.CostDuration = CostDuration.ThisCombat;
    }));
    cards.Add(Card("FX_TRANSFORM", EffectKind.TransformCardsInPile, e =>
    {
        e.Target = EffectTarget.Self; e.Amount = 1;
        e.Pick = new CardPick { Piles = { PileKind.Hand }, Mode = PickMode.Choose };
        e.TransformInto = TransformInto.Random;
    }));
    cards.Add(Card("FX_MOVE", EffectKind.MoveCardsBetweenPiles, e =>
    {
        e.Target = EffectTarget.Self; e.Amount = 1;
        e.TargetPile = PileKind.DrawPile;
        e.Placement = PilePlacement.Top;
        e.Pick = new CardPick { Source = PickSource.ThisCard };
    }));

    project.Cards.AddRange(cards);
    return project;

    static ValueSource CalcSource(decimal perUnit) => new()
    {
        Actor = ValueSourceActor.Self,
        Kind = ValueSourceKind.HandCount,
        PerUnit = perUnit,
    };
}

// 造一张只带一条效果的牌（牌名与描述由 id 生成，够快照用）。
static CardDef Card(string id, EffectKind kind, Action<EffectDef>? tweak = null)
{
    var effect = new EffectDef
    {
        Kind = kind,
        Target = EffectTarget.Target,
        Amount = 1,
    };

    tweak?.Invoke(effect);

    return new CardDef
    {
        Id = id,
        Name = new LocalizedText("eng", id).With("zhs", id),
        Description = new LocalizedText("eng", "Test."),
        Pool = "ironclad",
        Type = kind == EffectKind.DealDamage ? CardType.Attack : CardType.Skill,
        Rarity = CardRarity.Common,
        Target = CardTargetType.AnyEnemy,
        EnergyCost = 1,
        Effects = { effect },
    };
}

// ---------------------------------------------------------------------------
// 时机矩阵：**每一种触发时机、每种牌堆范围、计数器、自动打出各来一张**。
//
// 时机是第七个横切关注点（它不属于任何单个效果），而它牵动的分支很碎：八种时机各有各的游戏
// 钩子与早退条件，"打出后"与"打出一张牌时"还要**合成一个覆写**（同一个游戏钩子派发），
// 回合类时机要按牌堆范围逐条包判断，还有它自己的计数器与自动打出的相位钩子。
// 少了这份夹具，时机模块的搬迁就没有验收基础。
// ---------------------------------------------------------------------------
static ModProject TimingMatrix()
{
    var project = new ModProject
    {
        Id = "TimingMatrix",
        Name = "Timing Matrix",
        Version = "1.0.0",
    };

    project.Powers.Add(new PowerDef
    {
        Id = "MARK",
        Name = new LocalizedText("eng", "Mark").With("zhs", "印记"),
        Kind = PowerKind.Buff,
    });

    var cards = new List<CardDef>();

    // ---- 八种时机各来一张。OnPlay 是默认值，也显式来一张，免得"默认路径"没被覆盖 ----
    cards.Add(TimedCard("TM_ON_PLAY", new[] { Fx(EffectTrigger.OnPlay) }));
    cards.Add(TimedCard("TM_AFTER_PLAYED", new[] { Fx(EffectTrigger.AfterPlayed) }));
    cards.Add(TimedCard("TM_ON_DRAW", new[] { Fx(EffectTrigger.OnDraw) }));
    cards.Add(TimedCard("TM_ON_DISCARD", new[] { Fx(EffectTrigger.OnDiscard) }));
    cards.Add(TimedCard("TM_ON_EXHAUST", new[] { Fx(EffectTrigger.OnExhaust) }));
    cards.Add(TimedCard("TM_START_OF_TURN", new[] { Fx(EffectTrigger.StartOfTurn) }));
    cards.Add(TimedCard("TM_END_OF_TURN", new[] { Fx(EffectTrigger.EndOfTurn) }));
    cards.Add(TimedCard("TM_CARD_PLAYED", new[] { Fx(EffectTrigger.CardPlayed) }));

    // ---- 打出后 + 打出一张牌时同在**一张卡**上 ----
    //
    // 两者由游戏的同一个钩子（AfterCardPlayed）派发，所以只能有一个覆写：只有"打出后"时
    // 可以直接早退，两者都有时必须把自身那半段包进 if 里。这是最容易改错的一处结构。
    cards.Add(TimedCard("TM_BOTH_AFTER_PLAYED", new[]
    {
        Fx(EffectTrigger.AfterPlayed),
        Fx(EffectTrigger.CardPlayed),
    }));

    // ---- "打出一张牌时"的筛选条件与计数器 ----
    //
    // 筛选：不限 / 标签 / 指定牌（有 id）/ 指定牌（没 id —— 那条"不会触发"的分支）。
    cards.Add(TimedCard("TM_PLAYED_FILTER_ANY", new[] { Fx(EffectTrigger.CardPlayed) }));
    cards.Add(TimedCard("TM_PLAYED_FILTER_TAG", new[]
    {
        Fx(EffectTrigger.CardPlayed, e => e.CardFilter = CardFilterKind.TagStrike),
    }));
    cards.Add(TimedCard("TM_PLAYED_FILTER_NAMED", new[]
    {
        Fx(EffectTrigger.CardPlayed, e =>
        {
            e.CardFilter = CardFilterKind.SpecificCard;
            e.CardId = "CARD.STRIKE_IRONCLAD";
        }),
    }));
    cards.Add(TimedCard("TM_PLAYED_FILTER_NAMED_EMPTY", new[]
    {
        Fx(EffectTrigger.CardPlayed, e => e.CardFilter = CardFilterKind.SpecificCard),
    }));

    // 计数器：启用且阈值 > 1 才需要字段；归零范围两种各来一张
    // （"本回合"的要在回合开始与结束各归零一次）。
    cards.Add(TimedCard("TM_COUNTER_TURN", new[]
    {
        Fx(EffectTrigger.CardPlayed, e =>
        {
            e.UseCounter = true;
            e.CounterTarget = 3;
            e.CounterReset = CounterReset.ThisTurn;
        }),
    }));
    cards.Add(TimedCard("TM_COUNTER_COMBAT", new[]
    {
        Fx(EffectTrigger.CardPlayed, e =>
        {
            e.UseCounter = true;
            e.CounterTarget = 2;
            e.CounterReset = CounterReset.ThisCombat;
        }),
    }));

    // 阈值 1 等价于不启用 —— 不该生成计数器字段。
    cards.Add(TimedCard("TM_COUNTER_ONE", new[]
    {
        Fx(EffectTrigger.CardPlayed, e => { e.UseCounter = true; e.CounterTarget = 1; }),
    }));

    // ---- 回合类时机的牌堆范围 ----
    //
    // 四种牌堆各一张 + 不限（默认）一张。
    foreach (PileScope scope in new[]
    {
        PileScope.Any, PileScope.Hand, PileScope.DrawPile, PileScope.DiscardPile, PileScope.ExhaustPile,
    })
    {
        cards.Add(TimedCard($"TM_SCOPE_START_{scope.ToString().ToUpperInvariant()}", new[]
        {
            Fx(EffectTrigger.StartOfTurn, e => e.PileScope = scope),
        }));
    }

    // 同一时机下**两条效果的范围不同** —— 范围判断必须逐条做，按第一条整体早退会把
    // 另一条变成死代码（编译正常、永远不生效）。
    cards.Add(TimedCard("TM_SCOPE_MIXED", new[]
    {
        Fx(EffectTrigger.StartOfTurn, e => e.PileScope = PileScope.DrawPile),
        Fx(EffectTrigger.EndOfTurn, e => e.PileScope = PileScope.Hand),
    }));

    // "回合结束时 + 若在手牌中"走的是另一个钩子（OnTurnEndInHand），不是 AfterSideTurnEnd。
    cards.Add(TimedCard("TM_END_IN_HAND", new[]
    {
        Fx(EffectTrigger.EndOfTurn, e => e.PileScope = PileScope.Hand),
    }));

    // ---- 自动打出这张牌（相位钩子）----
    //
    // 它不是"回合开始时做点什么"，而是"这张牌在某个牌堆里时自己出来"，走自动打出的相位钩子。
    cards.Add(TimedCard("TM_SELF_PLAY_START", new[]
    {
        Fx(EffectTrigger.StartOfTurn, e =>
        {
            e.Kind = EffectKind.AutoPlay;
            e.Pick = new CardPick { Source = PickSource.ThisCard };
            e.PileScope = PileScope.ExhaustPile;
        }),
    }));
    cards.Add(TimedCard("TM_SELF_PLAY_END", new[]
    {
        Fx(EffectTrigger.EndOfTurn, e =>
        {
            e.Kind = EffectKind.AutoPlay;
            e.Pick = new CardPick { Source = PickSource.ThisCard };
            e.PileScope = PileScope.DrawPile;
        }),
    }));

    // 重复打出：牌堆条件只判一次、循环在里层（"重复打出 2 次"不是"每轮重判一次"）。
    cards.Add(TimedCard("TM_SELF_PLAY_REPEAT", new[]
    {
        Fx(EffectTrigger.StartOfTurn, e =>
        {
            e.Kind = EffectKind.AutoPlay;
            e.Pick = new CardPick { Source = PickSource.ThisCard };
            e.PileScope = PileScope.ExhaustPile;
            e.Repeat = 2;
        }),
    }));

    // ---- 打出时 + 回合时并存：ShouldReceiveCombatHooks 要看全部钩子算出 ----
    cards.Add(TimedCard("TM_ON_PLAY_AND_TURN", new[]
    {
        Fx(EffectTrigger.OnPlay),
        Fx(EffectTrigger.StartOfTurn),
        Fx(EffectTrigger.EndOfTurn),
    }));

    // 只有"打出时"的卡不需要 ShouldReceiveCombatHooks（它不靠钩子派发）。
    cards.Add(TimedCard("TM_ONLY_ON_PLAY", new[] { Fx(EffectTrigger.OnPlay) }));

    // ---- 打出时的目标参数检查 ----
    //
    // 攻击牌 + 有一条以"目标"为对象的效果时，OnPlay 要发一句 ArgumentNullException.ThrowIfNull。
    cards.Add(TimedCard("TM_TARGET_CHECK", new[] { Fx(EffectTrigger.OnPlay) }, isAttack: true));
    cards.Add(TimedCard("TM_NO_TARGET_CHECK", new[]
    {
        Fx(EffectTrigger.OnPlay, e => e.Target = EffectTarget.Self),
    }, isAttack: true));

    project.Cards.AddRange(cards);

    // ---- 状态侧：四种触发时机 + 三种衰减 ----
    project.Powers.Add(new PowerDef
    {
        Id = "ALL_TRIGGERS",
        Name = new LocalizedText("eng", "All Triggers").With("zhs", "全时机"),
        Description = "Fires at every supported moment.",
        Kind = PowerKind.Buff,
        Stack = PowerStack.Counter,
        Decay = DecayMode.OnePerTurnEnd,
        Triggers =
        {
            new TriggeredEffect { When = PowerTrigger.TurnStart, Effect = new EffectDef { Kind = EffectKind.GainBlock, Target = EffectTarget.Self, Amount = 2 } },
            new TriggeredEffect { When = PowerTrigger.CardPlayed, Effect = new EffectDef { Kind = EffectKind.GainEnergy, Target = EffectTarget.Self, Amount = 1 } },
            new TriggeredEffect { When = PowerTrigger.DamageTaken, Effect = new EffectDef { Kind = EffectKind.GainBlock, Target = EffectTarget.Self, Amount = 3 } },
            new TriggeredEffect { When = PowerTrigger.TurnEnd, Effect = new EffectDef { Kind = EffectKind.GainStars, Target = EffectTarget.Self, Amount = 1 } },
        },
    });

    project.Powers.Add(new PowerDef
    {
        Id = "DECAY_ALL",
        Name = new LocalizedText("eng", "Decay All").With("zhs", "全衰减"),
        Description = "Loses all stacks at end of turn.",
        Kind = PowerKind.Debuff,
        Stack = PowerStack.Counter,
        Decay = DecayMode.AllAtTurnEnd,
    });

    return project;

    // 造一张只带指定效果的牌（时机全由参数里的效果决定）。
    static CardDef TimedCard(string id, IReadOnlyList<EffectDef> effects, bool isAttack = false)
    {
        return new CardDef
        {
            Id = id,
            Name = new LocalizedText("eng", id).With("zhs", id),
            Description = new LocalizedText("eng", "Test."),
            Pool = "ironclad",
            Type = isAttack ? CardType.Attack : CardType.Skill,
            Rarity = CardRarity.Common,
            Target = isAttack ? CardTargetType.AnyEnemy : CardTargetType.Self,
            EnergyCost = 1,
            Effects = effects.ToList(),
        };
    }

    // 一条默认"给自己 3 点格挡"的效果，只把时机（以及各用例要改的字段）调出来。
    static EffectDef Fx(EffectTrigger trigger, Action<EffectDef>? tweak = null)
    {
        var effect = new EffectDef
        {
            Kind = EffectKind.GainBlock,
            Target = EffectTarget.Self,
            Amount = 3,
            Trigger = trigger,
        };

        tweak?.Invoke(effect);
        return effect;
    }
}

// ---------------------------------------------------------------------------
// 计数矩阵：**每一种"数出来的"来源各来一张牌**（见文件末尾的说明）。
//
// 与效果矩阵、时机矩阵同一个道理，而且这次也先查了覆盖：计数有 30 种，而原来所有夹具只用到
// 5 种（手牌数、弃牌堆数、本场打出牌数、失去生命两档）。计数模块搬迁没有这份夹具，
// "逐字节不变"会是一句空话。
//
// 用法上这些来源都当成"计算型数值的来源"，所以每种都会在生成的 DamageMultiplier 方法里
// 留下自己的读表达式——正是要被 diff 与断言盯住的那几行。
// ---------------------------------------------------------------------------
static ModProject CounterMatrix()
{
    var project = new ModProject
    {
        Id = "CountMatrix",
        Name = "Counter Matrix",
        Version = "1.0.0",
    };

    project.Powers.Add(new PowerDef
    {
        Id = "MARK",
        Name = new LocalizedText("eng", "Mark").With("zhs", "印记"),
        Kind = PowerKind.Buff,
    });

    var cards = new List<CardDef>();

    void Add(string id, ValueSourceKind kind, Action<ValueSource>? tweak = null)
    {
        var source = new ValueSource { Actor = ValueSourceActor.Self, Kind = kind, PerUnit = 1 };

        tweak?.Invoke(source);

        cards.Add(new CardDef
        {
            Id = id,
            Name = new LocalizedText("eng", id).With("zhs", id),
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
                    Kind = EffectKind.DealDamage,
                    Target = EffectTarget.Target,
                    Amount = 0,
                    Source = source,
                },
            },
        });
    }

    // ---- 战斗历史（20 种：十类口径 × 本回合/本场战斗）----
    Add("CN_PLAYED_TURN", ValueSourceKind.CardsPlayedThisTurn);
    Add("CN_PLAYED_COMBAT", ValueSourceKind.CardsPlayedThisCombat);
    Add("CN_DRAWN_TURN", ValueSourceKind.CardsDrawnThisTurn);
    Add("CN_DRAWN_COMBAT", ValueSourceKind.CardsDrawnThisCombat);
    Add("CN_HPLOST_TURN", ValueSourceKind.HpLostThisTurn);
    Add("CN_HPLOST_COMBAT", ValueSourceKind.HpLostThisCombat);
    Add("CN_HIT_TURN", ValueSourceKind.HitsTakenThisTurn);
    Add("CN_HIT_COMBAT", ValueSourceKind.HitsTakenThisCombat);
    Add("CN_ATTACK_TURN", ValueSourceKind.AttacksThisTurn);
    Add("CN_ATTACK_COMBAT", ValueSourceKind.AttacksThisCombat);
    Add("CN_EXHAUST_TURN", ValueSourceKind.CardsExhaustedThisTurn);
    Add("CN_EXHAUST_COMBAT", ValueSourceKind.CardsExhaustedThisCombat);
    Add("CN_DISCARD_TURN", ValueSourceKind.CardsDiscardedThisTurn);
    Add("CN_DISCARD_COMBAT", ValueSourceKind.CardsDiscardedThisCombat);
    Add("CN_GENERATED_TURN", ValueSourceKind.CardsGeneratedThisTurn);
    Add("CN_GENERATED_COMBAT", ValueSourceKind.CardsGeneratedThisCombat);
    Add("CN_TAKEN_TURN", ValueSourceKind.DamageTakenThisTurn);
    Add("CN_TAKEN_COMBAT", ValueSourceKind.DamageTakenThisCombat);
    Add("CN_DEALT_TURN", ValueSourceKind.DamageDealtThisTurn);
    Add("CN_DEALT_COMBAT", ValueSourceKind.DamageDealtThisCombat);

    // ---- 牌堆张数（5 种）----
    Add("CN_HAND", ValueSourceKind.HandCount);
    Add("CN_DRAW_PILE", ValueSourceKind.DrawPileCount);
    Add("CN_DISCARD_PILE", ValueSourceKind.DiscardPileCount);
    Add("CN_EXHAUST_PILE", ValueSourceKind.ExhaustPileCount);
    Add("CN_ALL_PILES", ValueSourceKind.AllPilesCount);

    // ---- 按条件数牌：五个范围各来一个（范围表达式有五种，各不同）----
    Add("CN_FILTER_ALL", ValueSourceKind.FilteredCardCount, s => s.CountScope = CountScope.AllPiles);
    Add("CN_FILTER_HAND", ValueSourceKind.FilteredCardCount, s => s.CountScope = CountScope.Hand);
    Add("CN_FILTER_DRAW", ValueSourceKind.FilteredCardCount, s => s.CountScope = CountScope.DrawPile);
    Add("CN_FILTER_DISCARD", ValueSourceKind.FilteredCardCount, s => s.CountScope = CountScope.DiscardPile);
    Add("CN_FILTER_EXHAUST", ValueSourceKind.FilteredCardCount, s => s.CountScope = CountScope.ExhaustPile);

    // ---- 自建跟踪：恢复生命（4 种）。用到它们会连带发出 Runtime/HealTracker.cs 与
    //      ModEntry 里的 Harmony 补丁——那两处也在快照范围内（生成源码）。----
    Add("CN_HEALS_TURN", ValueSourceKind.HealsThisTurn);
    Add("CN_HEALS_COMBAT", ValueSourceKind.HealsThisCombat);
    Add("CN_HEAL_AMOUNT_TURN", ValueSourceKind.HealAmountThisTurn);
    Add("CN_HEAL_AMOUNT_COMBAT", ValueSourceKind.HealAmountThisCombat);

    project.Cards.AddRange(cards);
    return project;
}
