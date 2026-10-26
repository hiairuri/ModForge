using System.Text;
using ModForge.Core.Model;
using ModForge.Core.Modules.CardPicking;
using ModForge.Core.Modules.CardSets;
using ModForge.Core.Modules.Cards;
using ModForge.Core.Modules.Effects;
using ModForge.Core.Modules.Timing;
using ModForge.Core.Modules.ValueSources;
using ModForge.Core.Modules.Variables;

namespace ModForge.Core.Generate;

/// <summary>
/// Generates the C# source for a mod.
///
/// The generated code targets the game's own model types directly (no third-party
/// framework): one CardModel subclass per card and one genuine PowerModel subclass
/// per power. A power is a real buff, so it appears in the power bar, stacks,
/// decays, and participates in combat hooks exactly like vanilla content.
/// </summary>
public sealed class CodeGenerator
{
    private readonly ModProject _project;

    /// <summary>
    /// 这个工程是否用到"永久增加伤害"。
    ///
    /// 用到时才发"可持久化增伤"那套支持（运行时接口 + 每张有伤害的牌上实现它），
    /// 免得没用到也往生成物里塞一堆没人用的代码。
    /// </summary>
    private bool UsesPermanentBoost => ModProject
        .AllEffects(_project)
        .Any(e => e.Kind == EffectKind.BoostCardDamage && e.CostDuration == CostDuration.Permanent);

    /// <summary>
    /// 这个工程是否用到"永久增加重放次数"。
    ///
    /// 与永久增伤同一套：用到时才发运行时接口，并给**每张牌**实现它
    /// （重放针对整张牌，不挂在某个数值槽上，所以不像增伤那样还要求"有伤害"）。
    /// </summary>
    private bool UsesPermanentReplay => ModProject
        .AllEffects(_project)
        .Any(e => e.Kind == EffectKind.AddReplay && e.CostDuration == CostDuration.Permanent);

    /// <summary>
    /// 有没有效果"使某些牌的伤害增加"（永久或本场战斗）。
    ///
    /// 有的话才发 <c>Runtime/DamageSlots.cs</c>——它按命名约定扫出目标牌的**每个**伤害槽，
    /// 因为目标牌是运行时挑出来的，生成代码不知道它有几个槽。只加第一个槽的话，
    /// 同牌的第二条伤害会漏掉。
    /// </summary>
    private bool UsesDamageSlots => ModProject
        .AllEffects(_project)
        .Any(e => e.Kind == EffectKind.BoostCardDamage);

    /// <summary>
    /// 有没有哪个计算型槽**没占到原版那几个名字**，需要 <c>Runtime/SlotVar.cs</c> 里的子类。
    ///
    /// 判据是"这个槽的基础值变量不是 CalculationBase"——原版 <c>CalculatedDamageVar</c> /
    /// <c>CalculatedBlockVar</c> 都写死了从 <c>CalculationBase</c> 读基础值，全牌只有**全局第一个**
    /// 计算型槽拿得到那个名字（见变量模块的全局编号）。
    ///
    /// 不能按"同类里第几个"判断：一张牌同时有计算型伤害与计算型格挡时，两者各自都是**同类第一个**
    /// （名字分别是 CalculatedDamage 与 CalculatedBlock），但基础值只有先出现的那个占着
    /// CalculationBase，后出现的那个拿到 CalculationBase2——它要是还走原版类型，
    /// 就会静默读到先出现那个的基础值（格挡数跟着伤害的基础值走，只在结算时现形）。
    /// </summary>
    private bool UsesSlotCalculatedVar => _project.Cards.Any(card =>
    {
        IReadOnlyList<EffectDef> resolved = EffectResolver.Resolve(_project, card.Effects);

        return VariableModule.Slots(_project, resolved)
            .Any(s => s.Effect.IsCalculated && !s.UsesOriginalCalcNames);
    });

    /// <summary>
    /// 有没有效果用到"恢复生命"的自制计数来源。
    ///
    /// 有的话才发 <c>Runtime/HealTracker.cs</c>（Harmony 观察治疗的计数器），
    /// 并且 ModEntry 的 <c>Harmony.PatchAll</c> 也要跟着发出——否则计数器不会挂上去，
    /// 四个来源永远读到 0。
    /// </summary>
    private bool UsesHealTracker => ModProject.AllEffects(_project).Any(e =>
        e.Source is { IsUsed: true } && e.Source.Kind is
            ValueSourceKind.HealsThisTurn or ValueSourceKind.HealsThisCombat or
            ValueSourceKind.HealAmountThisTurn or ValueSourceKind.HealAmountThisCombat);

    /// <summary>
    /// 工程里有没有用到**临时牌集合**（存或取任意一处）——用它决定发不发那个运行时件。
    ///
    /// 只算卡牌的效果：集合的键里有一段是"一次出牌结算"的地址，状态没有这个东西
    /// （校验器也会把状态里用集合报出来，那时生成的代码里没有地址、取不到东西）。
    /// </summary>
    private bool UsesCardSets => _project.Cards.Any(card => card.Effects.Any(e =>
        (CardSetCatalog.Stores(e.Kind) && !string.IsNullOrWhiteSpace(e.SetId))
        || (e.Pick.Source == PickSource.Set && !string.IsNullOrWhiteSpace(e.Pick.SetId))));

    private readonly StringBuilder _log = new();

    public CodeGenerator(ModProject project)
    {
        _project = project;
    }

    public string Log => _log.ToString();

    /// <summary>All generated files keyed by relative path.</summary>
    public IReadOnlyDictionary<string, string> GenerateAll()
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ModEntry.cs"] = GenerateEntryPoint(),
        };

        foreach (CardDef card in _project.Cards)
        {
            string typeName = _project.CardTypeName(card);
            files[$"Cards/{typeName}.cs"] = GenerateCard(card);
        }

        foreach (PowerDef power in _project.Powers)
        {
            string typeName = _project.PowerTypeName(power);
            files[$"Powers/{typeName}.cs"] = GeneratePower(power);
        }

        if (_project.SelfCheckOnStartup)
        {
            files["SelfCheck.cs"] = GenerateSelfCheck();
        }

        // Skins need the runtime remapper, which redirects the game's animation
        // requests to the names the skin actually provides.
        if (_project.Skins.Count > 0)
        {
            string? runtime = EmbeddedRuntime.AnimationRemapSource();
            if (runtime is not null)
            {
                files["Runtime/AnimationRemap.cs"] = runtime;
            }
        }

        // "永久增加伤害"要往目标牌自己的类上加 [SavedProperty] 字段（游戏的存档格式只认
        // 卡牌类自己声明的字段），所以需要一组运行时支持：接口 + 实现样板。
        if (UsesPermanentBoost)
        {
            string? runtime = EmbeddedRuntime.DamageBoostSource();
            if (runtime is not null)
            {
                files["Runtime/DamageBoost.cs"] = runtime;
            }
        }

        // "使某些牌的伤害增加"（本场战斗或永久）要改目标牌的**每个**伤害槽，
        // 需要按命名约定扫描的运行时辅助。
        if (UsesDamageSlots)
        {
            string? runtime = EmbeddedRuntime.DamageSlotsSource();
            if (runtime is not null)
            {
                files["Runtime/DamageSlots.cs"] = runtime;
            }
        }

        // "永久增加重放次数"要往目标牌自己的类上加 [SavedProperty] 字段（与永久增伤同理），
        // 所以需要运行时接口 + 实现样板。
        if (UsesPermanentReplay)
        {
            string? runtime = EmbeddedRuntime.ReplayBoostSource();
            if (runtime is not null)
            {
                files["Runtime/ReplayBoost.cs"] = runtime;
            }
        }

        // 永久增伤 / 永久重放任一在用时，发**原版牌**走的组件那套：组件仓库（含进战斗
        // 应用的钩子 host）+ 存档两头的 Harmony 补丁。补丁由游戏装 mod 时的默认
        // PatchAll 自动打上，ModEntry 只需要注册战斗钩子的订阅（见 ModEntry 模板）。
        if (UsesPermanentBoost || UsesPermanentReplay)
        {
            if (EmbeddedRuntime.CardComponentsSource() is { } components)
            {
                files["Runtime/CardComponents.cs"] = components;
            }

            if (EmbeddedRuntime.SaveComponentsPatchSource() is { } patch)
            {
                files["Runtime/SaveComponentsPatch.cs"] = patch;
            }
        }

        // 一张牌上有**第二个计算型数值**时需要它：游戏的 CalculatedDamageVar 固定读
        // CalculationBase + ExtraDamage 这两个名字，第二组变量得有个子类去读自己的名字。
        // 固定值不需要这个——给变量起名字就够了，原版"流光"的第二个 BlockVar
        // （BlockNextTurn，下回合格挡 buff 的数值）走的就是起名这条路。
        if (UsesSlotCalculatedVar)
        {
            string? runtime = EmbeddedRuntime.SlotVarSource();
            if (runtime is not null)
            {
                files["Runtime/SlotVar.cs"] = runtime;
            }
        }

        // 用到"恢复生命"来源时需要它：游戏没有治疗的历史与钩子，
        // 这个计数器用 Harmony 观察每次治疗，自己记账（见 HealTracker.cs 的说明）。
        //
        // 连带发出 SharedRuntime：计数是**带状态**的运行时件，多个由本编辑器生成的 mod 同时装时
        // 只能有一份账（先加载的当宿主，其余转发）。无状态的运行时件（伤害槽、两个 Apply 辅助）
        // 每 mod 一份无害，所以不带这个文件。
        if (UsesHealTracker)
        {
            string? runtime = EmbeddedRuntime.HealTrackerSource();
            if (runtime is not null)
            {
                files["Runtime/HealTracker.cs"] = runtime;
            }

            string? shared = EmbeddedRuntime.SharedRuntimeSource();
            if (shared is not null)
            {
                files["Runtime/SharedRuntime.cs"] = shared;
            }
        }

        // 临时牌集合：把"抽到的/生成的/选出的那几张牌"按 `地址.效果编号.集合ID` 存起来，
        // 供同一张牌的其它效果再动它们。无状态（一张静态表 + 一个发号器），所以只发这一个文件。
        if (UsesCardSets)
        {
            string? runtime = EmbeddedRuntime.TempCardSetsSource();
            if (runtime is not null)
            {
                files["Runtime/TempCardSets.cs"] = runtime;
            }
        }

        return files;
    }

    /// <summary>
    /// Optional startup self-check. Runs after ModelDb.Preload so it cannot disturb
    /// pool freezing, and reports exactly what the game registered for this mod.
    /// </summary>
    /// <summary>
    /// Optional startup self-check. Runs after ModelDb.Preload so it cannot disturb
    /// pool freezing, and reports exactly what the game registered for this mod.
    /// </summary>
    private string GenerateSelfCheck()
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Linq;");
        sb.AppendLine("using Godot;");
        sb.AppendLine("using HarmonyLib;");
        sb.AppendLine("using MegaCrit.Sts2.Core.Models;");
        sb.AppendLine();
        sb.AppendLine($"namespace {_project.Id}.Generated;");
        sb.AppendLine();
        sb.AppendLine("[HarmonyPatch(typeof(ModelDb), nameof(ModelDb.Preload))]");
        sb.AppendLine("internal static class SelfCheck");
        sb.AppendLine("{");
        sb.AppendLine("    [HarmonyPostfix]");
        sb.AppendLine("    private static void Postfix()");
        sb.AppendLine("    {");
        sb.AppendLine("        try");
        sb.AppendLine("        {");

        foreach (CardDef card in _project.Cards)
        {
            string entry = ModelIds.Slugify(_project.CardTypeName(card));
            sb.AppendLine($"            Check(\"card\", \"{card.Id}\", \"{entry}\",");
            sb.AppendLine($"                () => ModelDb.AllCards.Any(c => c.Id.Entry == \"{entry}\"));");
        }

        foreach (PowerDef power in _project.Powers)
        {
            string entry = ModelIds.Slugify(_project.PowerTypeName(power));
            sb.AppendLine($"            Check(\"power\", \"{power.Id}\", \"{entry}\",");
            sb.AppendLine($"                () => ModelDb.AllPowers.Any(p => p.Id.Entry == \"{entry}\"));");
        }

        sb.AppendLine("        }");
        sb.AppendLine("        catch (Exception ex)");
        sb.AppendLine("        {");
        sb.AppendLine($"            GD.Print(\"[{_project.Id}] self-check failed: \" + ex);");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    private static void Check(string kind, string id, string entry, Func<bool> present)");
        sb.AppendLine("    {");
        sb.AppendLine("        bool ok;");
        sb.AppendLine("        try");
        sb.AppendLine("        {");
        sb.AppendLine("            ok = present();");
        sb.AppendLine("        }");
        sb.AppendLine("        catch (Exception ex)");
        sb.AppendLine("        {");
        sb.AppendLine($"            GD.Print($\"[{_project.Id}] SELFCHECK {{kind}} {{id}} REGISTERED=false error={{ex.Message}}\");");
        sb.AppendLine("            return;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine($"        GD.Print($\"[{_project.Id}] SELFCHECK {{kind}} {{id}} REGISTERED={{ok}} entry={{entry}}\");");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private string GenerateEntryPoint()
    {
        // 组件的战斗钩子订阅用到 AbstractModel（MegaCrit.Sts2.Core.Models 命名空间）——
        // 只有发组件的工程才多这条 using，其余工程的 ModEntry 一个字节不变。
        bool usesComponents = UsesPermanentBoost || UsesPermanentReplay;

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using HarmonyLib;");
        sb.AppendLine("using MegaCrit.Sts2.Core.Modding;");
        if (usesComponents)
        {
            sb.AppendLine("using MegaCrit.Sts2.Core.Models;");
        }
        sb.AppendLine("using MegaCrit.Sts2.Core.Models.CardPools;");
        sb.AppendLine();
        sb.AppendLine($"namespace {_project.Id}.Generated;");
        sb.AppendLine();
        sb.AppendLine("/// <summary>Mod entry point. Registers generated content with the game.</summary>");
        sb.AppendLine("[ModInitializer(nameof(Initialize))]");
        sb.AppendLine("public static class ModEntry");
        sb.AppendLine("{");
        sb.AppendLine("    public static void Initialize()");
        sb.AppendLine("    {");

        if (_project.Cards.Count == 0)
        {
            sb.AppendLine("        // This project defines no cards.");
        }

        foreach (CardDef card in _project.Cards)
        {
            string poolType = PoolTypeName(card.Pool);
            string cardType = _project.CardTypeName(card);
            sb.AppendLine($"        // Without a pool a card throws during ModelDb.Preload(), so this registration is required.");
            sb.AppendLine($"        ModHelper.AddModelToPool<{poolType}, Cards.{cardType}>();");
        }

        if (_project.Powers.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("        // Powers are discovered automatically by ModelDb; referencing the types keeps them documented.");
            foreach (PowerDef power in _project.Powers)
            {
                sb.AppendLine($"        _ = typeof(Powers.{_project.PowerTypeName(power)});");
            }
        }

        if (UsesHealTracker)
        {
            sb.AppendLine();
            sb.AppendLine("        // 计数是带状态的运行时件：多个本编辑器的 mod 同时装时只能有一份账。");
            sb.AppendLine("        // 这里问一句\"别人已经建好了吗\"——不是宿主的话，补丁里的记账会跳过、");
            sb.AppendLine("        // 查询转发给宿主那一份（见 Runtime/SharedRuntime.cs）。");
            sb.AppendLine("        // 补丁本身下面照样打（统一用 PatchAll），非宿主只是\"什么也不做\"。");
            sb.AppendLine("        ModForge.Generated.Runtime.SharedRuntime.Adopt();");
        }

        if (UsesPermanentBoost || UsesPermanentReplay)
        {
            sb.AppendLine();
            sb.AppendLine("        // 原版牌的永久组件：战斗开始时由这个 host 把组件应用到战斗副本上");
            sb.AppendLine("        // （组件数据本身随存档 props 往返，见 Runtime/CardComponents.cs）。");
            sb.AppendLine("        // 组件仓库按卡实例存、条目名带本 mod 前缀，多个本编辑器的 mod");
            sb.AppendLine("        // 同时装时各记各的账、各应用各的，互不覆盖。");
            sb.AppendLine($"        ModHelper.SubscribeForCombatStateHooks(\"{_project.Id}.components\", state =>");
            sb.AppendLine("        {");
            sb.AppendLine("            ModForge.Generated.Runtime.ComponentHookHost.Instance.RememberCombat(state);");
            sb.AppendLine("            return new AbstractModel[] { ModForge.Generated.Runtime.ComponentHookHost.Instance };");
            sb.AppendLine("        });");
        }

        if (_project.Cards.Any(NeedsArtPatch) || _project.Powers.Any(p => p.IconFile is not null)
            || _project.SelfCheckOnStartup || UsesHealTracker
            || UsesPermanentBoost || UsesPermanentReplay)
        {
            // 说明按这份 mod 真正用到的补丁拼（贴图 / 治疗计数 / 原版牌组件 / 启动自检），
            // 不用的工程不出现"用到的"字样。
            var patchUsers = new List<string>();
            if (_project.Cards.Any(NeedsArtPatch) || _project.Powers.Any(p => p.IconFile is not null))
            {
                patchUsers.Add("自定义卡面贴图");
            }
            if (UsesHealTracker)
            {
                patchUsers.Add("治疗计数器对 Creature.HealInternal 的观察");
            }
            if (UsesPermanentBoost || UsesPermanentReplay)
            {
                patchUsers.Add("原版牌永久组件的存档两头挂点");
            }
            if (_project.SelfCheckOnStartup)
            {
                patchUsers.Add("启动自检");
            }

            sb.AppendLine();
            sb.AppendLine("        // 挂上本 mod 的 Harmony 补丁（" + string.Join("、", patchUsers) + "），");
            sb.AppendLine("        // 都靠 PatchAll 应用。");
            sb.AppendLine($"        new Harmony(\"{_project.Id}.art\").PatchAll(typeof(ModEntry).Assembly);");
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private bool NeedsArtPatch(CardDef card) => card.ArtFile is not null;

    // 按引用比较效果的比较器定义在 EffectResolver 里（生成器与描述都用到同一份）。
    private static readonly IEqualityComparer<EffectDef> PowerVarMapComparer =
        EffectResolver.EffectReferenceComparer.Instance;

    /// <summary>
    /// 等级表里**某一级**要写上去的语句（绝对赋值，不是增量）。
    ///
    /// 与增量模型是两套写法，因为要表达的语义不同：
    ///   <c>UpgradeValueBy(2m)</c> —— 每次升级 +2（每级加得一样）；
    ///   <c>.BaseValue = 8m</c>    —— 这一级就应该是 8（每级可以不一样）。
    ///
    /// 只写"这一级与 0 级不同"的部分？不——**每一项都写**。等级表可能某一级只改了伤害，
    /// 但那一级结束时格挡也该是它自己的值；漏写会让"上一次升级留下的值"留在那里。
    /// 所以这里按 <see cref="EffectDef.StateAt"/> 合成出该级的完整状态，逐项赋值。
    /// </summary>
    private static IEnumerable<string> AbsoluteLevelLines(
        EffectDef effect,
        VarSlot? slot,
        VarSlot? repeatSlot,
        int level,
        EffectEmitter body,
        Func<EffectDef, ValueRole, VarSlot?>? thresholdSlot = null)
    {
        EffectState state = effect.StateAt(level);

        if (effect.IsCalculated)
        {
            // 计算型：基础值、系数各写自己那一部分。走游戏变量的槽（计算型伤害/格挡）写
            // DynamicVars；其它种类没有专属变量，写生成类的私有字段——那条路要
            // "就地计算"的方法在（否则会写到 _calcBase-1 上）。
            int fieldIndex = body.FieldIndexFor(effect, forRepeat: false);
            VarSlot? target = slot ?? VariableModule.FieldSlot(effect, ValueRole.Value);

            if (target.Storage == VarStorage.GameVar || fieldIndex >= 0)
            {
                var value = new NumericValue(state.Amount, state.Source);

                yield return target.SetText(VarPart.Base, value.Base, fieldIndex);
                yield return target.SetText(VarPart.PerUnit, value.Coefficient, fieldIndex);
            }
        }
        else if (slot is not null)
        {
            // 固定值：写进这条效果自己那个槽。
            yield return slot.SetText(VarPart.Base, state.Amount);
        }

        // 重复次数：能升级的固定次数写 RepeatVar；计算次数写算次数的那个字段。
        //
        // 计算型的**基础值与变量系数都要写**：等级表是"这一级就该是这个数"的绝对语义，
        // 只写基础值的话，某一级把系数改大（或把来源换成另一个变量）不会生效——
        // 那是最难发现的一类问题：填了、也在文件里、就是不起作用。
        if (effect.CalculatedRepeat)
        {
            int fieldIndex = body.FieldIndexFor(effect, forRepeat: true);

            if (fieldIndex >= 0)
            {
                VarSlot repeatField = VariableModule.FieldSlot(effect, ValueRole.Repeat);

                yield return repeatField.SetText(VarPart.Base, state.Repeat, fieldIndex);
                yield return repeatField.SetText(VarPart.PerUnit, state.RepeatSource.PerUnit, fieldIndex);
            }
        }
        else if (repeatSlot is not null)
        {
            yield return repeatSlot.SetText(VarPart.Base, Math.Max(1, state.Repeat));
        }

        // 两个费用阈值：这一级该比什么数（绝对赋值，与增量模型二者择一）。
        //
        // **基础值与变量系数都写**：两个都是卡上的变量，等级表是"这一级就该是这个数"的
        // 绝对语义，只写其中一个的话，某一级把系数改了不会生效。
        // **来源**不在这里写：它决定"怎么算"，由 EffectEmitter 按等级分支（见
        // ThresholdSourceVariants），等级表只负责把这一级选定的来源存下来。
        foreach (ValueRole role in CostRoles.All)
        {
            if (thresholdSlot?.Invoke(effect, role) is not VarSlot costSlot)
            {
                continue;
            }

            CostState costState = role == ValueRole.Energy ? state.Energy : state.Star;

            yield return costSlot.SetText(VarPart.Base, costState.Amount);
            yield return costSlot.SetText(VarPart.PerUnit, costState.Source.PerUnit);
        }
    }

    /// <summary>
    /// 发出就地计算数值的方法体。
    ///
    /// 游戏只为伤害与格挡提供了计算变量，其它种类（施加状态的量、牌堆张数等）只能在代码里
    /// 自己算，所以由 EffectEmitter 登记、这里统一生成。
    /// </summary>
    /// <summary>
    /// 发出"就地计算"的取值方法。
    ///
    /// 这类效果（施加状态、召唤、牌堆张数……）游戏没有提供计算变量，只能自己写方法。
    /// 数值的构成与伤害/格挡一致：<c>基础值 + 变量 × 变量系数</c>。
    ///
    /// 基础值与增量放在**实例字段**里，而不是写进方法体的字面量：升级要能改它们。
    /// 也不能借用游戏的 CalculationBase / CalculationExtra 变量——一张牌只有一组，
    /// 同时存在计算型伤害与本类效果时会互相覆盖。
    /// </summary>
    /// <summary>
    /// 发出计算型伤害/格挡的"单位数量"方法。
    ///
    /// 游戏要求这个委托必须是**静态**的（<c>CalculatedVar.WithMultiplier</c> 会检查
    /// <c>multiplierCalc.Target is AbstractModel</c> 并抛异常），所以不能用闭包捕获 this，
    /// 只能做成静态方法、由游戏把卡牌实例与目标传进来。签名是游戏约定好的：
    /// <c>(CardModel, Creature?)</c>。
    ///
    /// 返回的是**单位数量**，不是最终值：最终值由游戏算成
    /// <c>CalculationBase + 增量变量 × 本方法返回值</c>。
    /// </summary>
    private void EmitMultiplier(StringBuilder sb, string methodName, EffectDef effect)
    {
        sb.AppendLine();
        sb.AppendLine("    /// <summary>");
        sb.AppendLine($"    /// 数值由计算得出：{effect.EffectiveBaseValue} + {effect.Source.PerUnit} × {effect.Source.Kind}。");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine($"    private static decimal {methodName}(CardModel card, Creature? target)");
        sb.AppendLine("    {");

        // 每级用**不同算式**时按等级分支（"0 级按手牌数、1 级按弃牌堆数"）。
        // 这是等级表能做到、增量模型做不到的事之一。
        IReadOnlyList<(int Level, ValueSource Source)> variants = SourceVariants(effect);

        if (variants.Count > 1)
        {
            // 等级从高到低写 if / else if，最后一档做成无条件兜底（不写 else）：
            // 低等级的兜底分支永远到不了（CurrentUpgradeLevel 不会小于 0），
            // 写出来只会在反编译结果里多一段死代码。
            sb.AppendLine("        // 每级用不同算式：按当前等级取值。");
            sb.AppendLine("        int level = card.CurrentUpgradeLevel;");
            sb.AppendLine();

            var branches = new List<(string Condition, ValueSource Source)>();

            for (int i = variants.Count - 1; i >= 1; i--)
            {
                branches.Add(($"level >= {LevelNumberFor(variants[i].Level)}", variants[i].Source));
            }

            branches.Add((string.Empty, variants[0].Source));

            for (int i = 0; i < branches.Count; i++)
            {
                (string condition, ValueSource source) = branches[i];

                string keyword = condition.Length == 0
                    ? "else"
                    : (i == 0 ? "if" : "else if");

                sb.AppendLine(condition.Length == 0
                    ? $"        {keyword}"
                    : $"        {keyword} ({condition})");

                sb.AppendLine("        {");

                // 每一支自己算自己的来源值，各自返回（各支的算式互不相干）。
                foreach (string line in SourceEmitter.Emit(
                             source, SourceContext(SourceEmitter.Context.ForStaticCard())))
                {
                    sb.AppendLine("            " + line);
                }

                sb.AppendLine("        }");
            }

            sb.AppendLine("    }");
            return;
        }

        // 游戏只给出卡牌实例，所以这里用卡牌维度的上下文取值。
        foreach (string line in SourceEmitter.Emit(
                     effect.Source, SourceContext(SourceEmitter.Context.ForStaticCard())))
        {
            sb.AppendLine("        " + line);
        }

        sb.AppendLine("    }");
    }

    /// <summary>
    /// 给取值上下文接上"状态 id → C# 类型"的解析（"状态层数"这个来源要用）。
    ///
    /// 解析必须走本工程：本工程自定义的状态住在 <c>{项目}.Generated.Powers</c>，
    /// 原版的住在游戏命名空间；猜错会编译不过。"牌 ID"那一维同理（本工程的卡住
    /// <c>{项目}.Generated.Cards</c>）。这两处是**唯一**需要工程知识的地方，
    /// 所以它们留在生成器里，取值模块只收两个"怎么解析"的委托。
    /// </summary>
    private SourceEmitter.Context SourceContext(SourceEmitter.Context ctx) =>
        ctx with
        {
            PowerTypeFor = id => PowerRefs.TypeRef(_project, id),
            CardTypeFor = id => CardRefs.TypeRef(_project, id),
        };

    /// <summary>
    /// 一条计算型效果在各级用到的**不同算式**（按等级升序，只保留真正变化的那些）。
    ///
    /// 返回超过一项时调用方按等级分支。只有一套算式时（绝大多数）返回一项，
    /// 生成的代码与没有等级表时完全一样。
    /// </summary>
    private static IReadOnlyList<(int Level, ValueSource Source)> SourceVariants(EffectDef effect)
    {
        if (!effect.IsCalculated)
        {
            return new[] { (0, effect.Source) };
        }

        // 没有等级表时的升级换来源：一档"0 级用原来的、1 级起用新的"。
        // 与等级表那一路是同一件事的两种写法（一个只能换一次，一个能逐级换）。
        if (!effect.HasLevels)
        {
            return effect.Upgrade.Source is ValueSource upgraded
                ? new[] { (0, effect.Source), (1, upgraded) }
                : new[] { (0, effect.Source) };
        }

        var result = new List<(int, ValueSource)> { (0, effect.Source) };
        int max = effect.Levels!.Count;

        for (int level = 1; level <= max; level++)
        {
            ValueSource source = effect.SourceAt(level);

            // 与上一档只有"每单位系数"不同、来源本身没变时不算新算式：
            // 系数是变量，升级时改它就够了（不需要按等级分支）。
            (int _, ValueSource prev) = result[^1];

            if (!SameSourceKind(source, prev))
            {
                result.Add((level, source));
            }
        }

        return result;
    }

    /// <summary>
    /// 两份来源是不是"同一套算式"。
    ///
    /// 判据在 <see cref="ValueSource.SameAs"/>（与费用阈值那边共用同一份）：只比**来源本身**
    /// （种类、取自谁、哪个状态、按条件数牌的条件与范围），不比系数——系数由变量承载，
    /// 升级时改那个变量就够了，不需要按等级分支。所以"第 1 级只是系数从 2 变 3"这种
    /// 情况不会多出分支代码。
    /// </summary>
    private static bool SameSourceKind(ValueSource a, ValueSource b) => a.SameAs(b);

    /// <summary>
    /// 一个**费用阈值的来源**在各级用的不同算式（按等级升序，只保留真正变化的那些）。
    ///
    /// 与 <see cref="SourceVariants"/> 同一条规矩、同一个用法，只是取的是阈值那个角色的来源。
    /// 多数效果只有一套算式（返回一项，方法体就平铺），等级表里换了来源才多出分支。
    /// </summary>
    private static IReadOnlyList<(int Level, ValueSource Source)> ThresholdSourceVariants(
        EffectDef effect,
        ValueRole role)
    {
        var result = new List<(int Level, ValueSource Source)> { (0, effect.SourceOf(role)) };

        if (!effect.HasLevels)
        {
            // 没有等级表时，"升级后换用的来源"（增量模型那一项）同样能换一次。
            return effect.Upgrade.SourceOf(role) is ValueSource upgraded
                ? new[] { (0, effect.SourceOf(role)), (1, upgraded) }
                : result;
        }

        int max = effect.Levels!.Count;

        for (int level = 1; level <= max; level++)
        {
            EffectState state = effect.StateAt(level);
            ValueSource source = role == ValueRole.Energy ? state.Energy.Source : state.Star.Source;
            (int _, ValueSource prev) = result[^1];

            if (!SameSourceKind(source, prev))
            {
                result.Add((level, source));
            }
        }

        return result;
    }

    /// <summary>
    /// 发出费用阈值的取值方法（<c>CalcPickEnergy0</c> 之类，见
    /// <see cref="EffectEmitter.PendingThresholdMethod"/>）：返回那个阈值的**来源值**。
    ///
    /// 逐级换算式时按等级分支——与 <see cref="EmitMultiplier"/> 同一套写法（"0 级按手牌数、
    /// 1 级按弃牌堆数"）。每支各调一次 <see cref="SourceEmitter.Emit"/>，各自 <c>return</c>，
    /// 所以每支的局部量互不相干。
    /// </summary>
    private void EmitThresholdMethods(
        StringBuilder sb,
        IEnumerable<EffectEmitter.PendingThresholdMethod> methods)
    {
        foreach (EffectEmitter.PendingThresholdMethod method in methods)
        {
            SourceEmitter.Context ctx = SourceContext(method.IsCard
                ? SourceEmitter.Context.ForCard()
                : SourceEmitter.Context.ForPower());

            IReadOnlyList<(int Level, ValueSource Source)> variants =
                ThresholdSourceVariants(method.Effect, method.Role);

            string what = method.Role == ValueRole.Energy ? "能量消耗" : "星辰消耗";

            string switchNote = variants.Count > 1 ? "（按等级换算式）" : string.Empty;

            sb.AppendLine();
            sb.AppendLine("    /// <summary>");
            sb.AppendLine($"    /// {what}筛选里\"比什么数\"的来源值{switchNote}。");
            sb.AppendLine("    /// </summary>");
            sb.AppendLine($"    private decimal {method.Name}(Creature? target)");
            sb.AppendLine("    {");

            if (variants.Count == 1)
            {
                AppendLines(sb, "        ", SourceEmitter.Emit(variants[0].Source, ctx));
            }
            else
            {
                sb.AppendLine("        int level = CurrentUpgradeLevel;");

                // 与 EmitMultiplier 同一条：等级从高到低，最后一档无条件兜底。
                var branches = new List<(string Condition, ValueSource Source)>();

                for (int i = variants.Count - 1; i >= 1; i--)
                {
                    branches.Add(($"level >= {LevelNumberFor(variants[i].Level)}", variants[i].Source));
                }

                branches.Add((string.Empty, variants[0].Source));

                for (int i = 0; i < branches.Count; i++)
                {
                    (string condition, ValueSource source) = branches[i];
                    string keyword = condition.Length == 0 ? "else" : (i == 0 ? "if" : "else if");

                    sb.AppendLine(condition.Length == 0 ? $"        {keyword}" : $"        {keyword} ({condition})");
                    sb.AppendLine("        {");
                    AppendLines(sb, "            ", SourceEmitter.Emit(source, ctx));
                    sb.AppendLine("        }");
                }
            }

            sb.AppendLine("    }");
        }
    }

    /// <summary>把若干行按给定缩进写进源码（几个发方法的地方共用，免得各写一遍循环）。</summary>
    private static void AppendLines(StringBuilder sb, string indent, IEnumerable<string> lines)
    {
        foreach (string line in lines)
        {
            sb.AppendLine(indent + line);
        }
    }

    /// <summary>
    /// 发出**条件侧**的取值方法（<c>CondSide0</c> 之类，见
    /// <see cref="EffectEmitter.PendingConditionMethod"/>）：返回那一侧的完整数值
    /// （基础值 + 来源 × 系数）。字段机制与就地计算的数值完全同一套
    /// （<see cref="EmitCalcMethods"/>），差别只在数值构成写死在登记项里。
    /// </summary>
    private void EmitConditionMethods(
        StringBuilder sb,
        IEnumerable<EffectEmitter.PendingConditionMethod> methods)
    {
        var list = methods.ToList();

        // 字段先声明，方法再引用。
        foreach (EffectEmitter.PendingConditionMethod method in list)
        {
            string what = method.IsLeft ? "条件左值" : "条件右值";
            VarSlot slot = VariableModule.FieldSlot(method.Effect, ValueRole.Value);

            sb.AppendLine();
            sb.AppendLine($"    // {what}构成：{method.Value.Base} + {method.Value.Coefficient} × {method.Value.Variable.Kind}。");

            foreach (string line in slot.FieldDeclarations(method.FieldIndex, method.Value))
            {
                sb.AppendLine("    " + line);
            }
        }

        foreach (EffectEmitter.PendingConditionMethod method in list)
        {
            SourceEmitter.Context ctx = SourceContext(method.IsCard
                ? SourceEmitter.Context.ForCard()
                : SourceEmitter.Context.ForPower());

            string what = method.IsLeft ? "条件左值" : "条件右值";
            VarSlot slot = VariableModule.FieldSlot(method.Effect, ValueRole.Value);

            sb.AppendLine();
            sb.AppendLine("    /// <summary>");
            sb.AppendLine($"    /// {what}：{method.Value.Base} + {method.Value.Coefficient} × {method.Value.Variable.Kind}。");
            sb.AppendLine("    /// </summary>");
            sb.AppendLine($"    private decimal {method.Name}(Creature? target)");
            sb.AppendLine("    {");

            foreach (string line in SourceEmitter.EmitValue(method.Value.Variable, ctx, "sourceValue"))
            {
                sb.AppendLine("        " + line);
            }

            sb.AppendLine($"        return {slot.FieldFormula(method.FieldIndex)};");
            sb.AppendLine("    }");
        }
    }


    /// <summary>等级数字面量（分支条件里用的那个数）。</summary>
    private static int LevelNumberFor(int level) => level;

    private void EmitCalcMethods(
        StringBuilder sb,
        IEnumerable<EffectEmitter.PendingCalcMethod> methods)
    {
        var list = methods.ToList();

        // 次数型与数值型的来源/基础值不同，各取各的（见 PendingCalcMethod.ForRepeat）。
        static NumericValue ValueOf(EffectEmitter.PendingCalcMethod m) => m.ForRepeat
            ? NumericValue.RepeatOf(m.Effect)
            : NumericValue.Of(m.Effect);

        // 每个方法的字段序号在建方法时就定下了（m.FieldIndex），这里直接用——
        // 不再按它在列表里的位置推，那样子列表顺序一变就会静默错位。
        static VarSlot SlotOf(EffectEmitter.PendingCalcMethod m) => VariableModule.FieldSlot(
            m.Effect,
            m.ForRepeat ? ValueRole.Repeat : ValueRole.Value);

        // 字段先声明，方法再引用。
        foreach (EffectEmitter.PendingCalcMethod method in list)
        {
            string what = method.ForRepeat ? "执行次数" : "数值";

            sb.AppendLine();
            sb.AppendLine($"    // {method.Effect.Kind} 的{what}构成：{ValueOf(method).Base} + {ValueOf(method).Coefficient} × {ValueOf(method).Variable.Kind}。");

            foreach (string line in SlotOf(method).FieldDeclarations(method.FieldIndex, ValueOf(method)))
            {
                sb.AppendLine("    " + line);
            }
        }

        foreach (EffectEmitter.PendingCalcMethod method in list)
        {
            SourceEmitter.Context ctx = SourceContext(method.IsCard
                ? SourceEmitter.Context.ForCard()
                : SourceEmitter.Context.ForPower());

            string what = method.ForRepeat ? "执行次数" : "数值";

            sb.AppendLine();
            sb.AppendLine("    /// <summary>");
            sb.AppendLine($"    /// {what}：{ValueOf(method).Base} + {ValueOf(method).Coefficient} × {ValueOf(method).Variable.Kind}。");
            sb.AppendLine("    /// </summary>");
            sb.AppendLine($"    private decimal {method.Name}(Creature? target)");
            sb.AppendLine("    {");

            // 来源值取进局部变量，再套上基础值与增量。
            foreach (string line in SourceEmitter.EmitValue(ValueOf(method).Variable, ctx, "sourceValue"))
            {
                sb.AppendLine("        " + line);
            }

            sb.AppendLine($"        return {SlotOf(method).FieldFormula(method.FieldIndex)};");
            sb.AppendLine("    }");
        }
    }

    private string GenerateCard(CardDef card)
    {
        string typeName = _project.CardTypeName(card);
        var sb = new StringBuilder();

        // Shared-effect references are expanded once here; everything below works on the
        // resolved list, so references and inline effects behave identically.
        //
        // 带编号展开：临时牌集合的键里要"这条效果在原列表里是第几条"（见 Modules/CardSets），
        // 所以这里保留编号。编号按**原始列表**数（界面上的"效果 N"），失效的引用也占一个号——
        // 与 Resolve 的顺序契约一致（Resolve 就是它的投影）。
        IReadOnlyList<EffectResolver.ResolvedEffect> numbered =
            EffectResolver.ResolveNumbered(_project, card.Effects);
        IReadOnlyList<EffectDef> resolved = numbered.Select(r => r.Effect).ToList();

        // "这条效果是第几条"与"这个集合是第几条定义的"两个查表：读写两侧靠它对上号。
        var effectNumbers = new Dictionary<EffectDef, int>(EffectResolver.EffectReferenceComparer.Instance);

        foreach (EffectResolver.ResolvedEffect entry in numbered)
        {
            effectNumbers[entry.Effect] = entry.Number;
        }

        // 固有属性（ID / 费用 / 星辰 / 升级增量 / X / 关键词 / 类型 / 稀有度 / 重放）的视图：
        // 出码与校验都从它读，默认值归一也在那里。工程 JSON 不变，所以既有工程照旧能用。
        CardCore core = CardCore.From(card);

        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using System.Linq;");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine("using MegaCrit.Sts2.Core.Combat;");
        // 战斗历史计数用到的 Entry 类型在 Combat.History.Entries 下。
        sb.AppendLine("using MegaCrit.Sts2.Core.Combat.History.Entries;");
        sb.AppendLine("using MegaCrit.Sts2.Core.Commands;");
        // 回合开始的钩子（AfterSideTurnStart）不带 PlayerChoiceContext，而抽牌、施加状态
        // 这类效果都需要一个，所以卡牌也要像状态那样自己造一个（见 HookContext）。
        sb.AppendLine("using MegaCrit.Sts2.Core.Context;");
        sb.AppendLine("using MegaCrit.Sts2.Core.Entities.Cards;");
        sb.AppendLine("using MegaCrit.Sts2.Core.Entities.Creatures;");
        sb.AppendLine("using MegaCrit.Sts2.Core.Entities.Multiplayer;");
        // 自动打出的相位钩子（AfterAutoPrePlayPhaseEnteredEarly 等）的签名里带 Player。
        sb.AppendLine("using MegaCrit.Sts2.Core.Entities.Players;");

        // PowerType 只在**按类别移除状态**时才用到（`PowerModel.TypeForCurrentAmount` 比"当前是
        // 正面还是负面"）。按需发：无条件发的话，每一张卡都多一行与它无关的 using——
        // 生成的代码是给人读的，也是"结构改动逐字节不变"那条验收的比对对象。
        if (resolved.Any(e => e.Kind == EffectKind.RemovePower && e.Selection != PowerSelection.PowerId))
        {
            sb.AppendLine("using MegaCrit.Sts2.Core.Entities.Powers;");
        }
        // 费用类效果："让玩家挑牌"要用 CardSelectCmd + CardSelectorPrefs；
        // 挑牌界面的提示语是 LocString（取自本牌的 cards 表条目）；
        // 随机挑牌要用 TakeRandom（MegaCrit.Sts2.Core.Extensions）。
        sb.AppendLine("using MegaCrit.Sts2.Core.CardSelection;");
        sb.AppendLine("using MegaCrit.Sts2.Core.Extensions;");
        sb.AppendLine("using MegaCrit.Sts2.Core.Localization;");
        sb.AppendLine("using MegaCrit.Sts2.Core.GameActions.Multiplayer;");
        sb.AppendLine("using MegaCrit.Sts2.Core.HoverTips;");
        sb.AppendLine("using MegaCrit.Sts2.Core.Localization.DynamicVars;");
        sb.AppendLine("using MegaCrit.Sts2.Core.Models;");
        // 状态类要用到（例如"立即执行灾厄判定"直接用 DoomPower），
        // 以及本工程自动合成的那些状态（它们在 .Generated.Powers 命名空间下）。
        sb.AppendLine("using MegaCrit.Sts2.Core.Models.Powers;");
        sb.AppendLine($"using {_project.Id}.Generated.Powers;");
        sb.AppendLine("using MegaCrit.Sts2.Core.Saves.Runs;");
        sb.AppendLine("using MegaCrit.Sts2.Core.ValueProps;");
        sb.AppendLine();
        sb.AppendLine($"namespace {_project.Id}.Generated.Cards;");
        sb.AppendLine();

        if (card.ArtFile is not null)
        {
            sb.AppendLine("/// <summary>Generated card. Art is loaded from a loose file next to the mod.</summary>");
        }
        else
        {
            sb.AppendLine("/// <summary>Generated card.</summary>");
        }

        // 变量槽：每条带数值的效果各占一个（规划见变量模块）。
        IReadOnlyList<VarSlot> slots = VariableModule.Slots(_project, resolved);

        // 费用阈值槽：筛了能量/星辰消耗的效果各占一个（EnergyAmount / StarAmount）。
        // 与数值槽分开是必须的——一条效果可以同时在筛两个费用，而"一条效果一个数值"的槽
        // 表达不了两个阈值。查表键是（效果, 角色），比较器按引用，与数值槽同一个。
        var thresholdSlots = new Dictionary<EffectDef, Dictionary<ValueRole, VarSlot>>(
            EffectResolver.EffectReferenceComparer.Instance);

        foreach (VarSlot slot in VariableModule.ThresholdSlots(_project, resolved))
        {
            if (!thresholdSlots.TryGetValue(slot.Effect, out var byRole))
            {
                byRole = new Dictionary<ValueRole, VarSlot>();
                thresholdSlots[slot.Effect] = byRole;
            }

            byRole[slot.Role] = slot;
        }

        VarSlot? ThresholdSlot(EffectDef effect, ValueRole role) =>
            thresholdSlots.TryGetValue(effect, out var byRole) && byRole.TryGetValue(role, out VarSlot? slot)
                ? slot
                : null;

        // 永久增伤要挂在"伤害"那个槽上，所以得先找到它（可能有多个，取第一个）。
        VarSlot? firstDamage = slots.FirstOrDefault(
            s => s.Effect.Kind == EffectKind.DealDamage && !s.Effect.IsCalculated);

        bool implementsPermanentBoost = UsesPermanentBoost && firstDamage is not null;

        // 永久重放：任何牌都能多打几次（重放针对整张牌，不挂在某个数值槽上），
        // 所以不像增伤那样还要求"这张牌有伤害"。
        bool implementsPermanentReplay = UsesPermanentReplay;

        var interfaces = new List<string>();

        if (implementsPermanentBoost)
        {
            interfaces.Add("ModForge.Generated.Runtime.IPermanentDamageBoost");
        }

        if (implementsPermanentReplay)
        {
            interfaces.Add("ModForge.Generated.Runtime.IAddReplay");
        }

        sb.AppendLine(interfaces.Count > 0
            ? $"public sealed class {typeName} : CardModel, {string.Join(", ", interfaces)}"
            : $"public sealed class {typeName} : CardModel");
        sb.AppendLine("{");

        // 固定值与计算型的声明都由槽自己给出（哪种游戏变量类型、带不带名字、原版类型还是
        // 我们的运行时子类，全在 VarSlot.Declarations 一处）。
        //
        // 分两趟：固定值全部在前，计算型全部在后。这不是风格问题——输出顺序变了，
        // 生成结果就不再逐字节相同。
        var varDecls = new List<string>();

        foreach (VarSlot slot in slots.Where(s => !s.Effect.IsCalculated))
        {
            varDecls.AddRange(slot.Declarations(NumericValue.Of(slot.Effect)));
        }

        // 计算型数值：每条各占一套变量（基础值 + 变量系数 + 计算值）。
        //
        // 第一个伤害槽沿用原版形状（CalculationBase + ExtraDamage + CalculatedDamageVar），
        // 卡面与描述完全兼容——原版"完美打击""全身撞击"就是这个结构。
        //
        // 没占到原版名字的槽（第二个起，以及"计算型伤害 + 计算型格挡"里后出现的那个）用我们
        // 自己的运行时子类（Runtime/SlotVar.cs）：原版类型固定读 CalculationBase，那些槽读的
        // 是 CalculationBase2 / 3…，必须让它读自己那两个变量。
        // "起名存第二套同类型变量"这条路原版也走过——"流光"的第二个 BlockVar（BlockNextTurn）
        // 就是起名的第二套值；只是计算型那几个类型没有带名字的构造，得自己接一层。
        //
        // 两个"增量变量"的名字不一样，这是游戏自己埋的坑：
        //   CalculatedDamageVar 覆写了 GetExtraVar()，取的是 **ExtraDamage**
        //   CalculatedBlockVar  用基类实现，取的是 **CalculationExtra**
        // 写错名字不会编译失败，而是运行时 DynamicVars[...] 抛 KeyNotFoundException。
        foreach (VarSlot slot in slots.Where(s => s.Effect.IsCalculated))
        {
            varDecls.AddRange(slot.Declarations(NumericValue.Of(slot.Effect)));
            EmitMultiplier(sb, slot.MultiplierName(), slot.Effect);
        }

        // 重复次数：只有"能升级的重复"才需要声明变量，否则循环里直接写字面量就够了。
        //
        // 每个可升级重复的效果**各占一个** RepeatVar（Repeat / Repeat2 / …）：
        // 两条"重复 2 次"各升各的时候，共用一个变量会让两条一起涨。
        //
        // 次数是**计算型**的不在此列：它的次数由 _calcBase/_calcPerUnit 两个字段算出来，
        // 没有 RepeatVar 可改（升级改的是那两个字段，见升级那一段）。
        //
        // 规划由变量模块给出（描述那边也调它）：两边各算一遍的话，名字一旦不同步，
        // 描述里引用的占位符在卡上就不存在——游戏内格式化失败，只在描述用过时现形。
        var repeatSlots = new Dictionary<EffectDef, VarSlot>(EffectResolver.EffectReferenceComparer.Instance);

        foreach (VarSlot slot in VariableModule.RepeatSlots(_project, resolved))
        {
            repeatSlots[slot.Effect] = slot;
            varDecls.AddRange(slot.Declarations(NumericValue.RepeatOf(slot.Effect)));
        }

        // 费用阈值：每个"筛了费用"的维度各占一个变量（EnergyAmount / StarAmount）。
        // 声明的是那条比较式里的基础值，取值处读它（见 EffectEmitter.CostThresholdExpr）、
        // 升级与等级表改它，描述里写 {EnergyAmount:diff()}。
        foreach (VarSlot slot in thresholdSlots.Values.SelectMany(m => m.Values))
        {
            varDecls.AddRange(slot.Declarations(slot.Effect.NumericOf(slot.Role)));
        }

        if (varDecls.Count == 1)
        {
            sb.AppendLine($"    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[] {{ {varDecls[0]} }};");
        }
        else if (varDecls.Count > 1)
        {
            sb.AppendLine("    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]");
            sb.AppendLine("    {");
            foreach (string decl in varDecls)
            {
                sb.AppendLine($"        {decl},");
            }

            sb.AppendLine("    };");
        }

        // 固有属性（关键词、星辰费用…）：出码由卡牌核心模块一处负责，这里只按固定位置调它。
        foreach (string line in CardCoreEmitter.Keywords(core))
        {
            sb.AppendLine(line);
        }

        foreach (string line in CardCoreEmitter.StarCost(core))
        {
            sb.AppendLine(line);
        }

        // 按玩家人数的限制（"多人游戏卡牌"）：默认档不写，所以已有的卡产物一个字节都不动。
        foreach (string line in CardCoreEmitter.Multiplayer(core))
        {
            sb.AppendLine(line);
        }

        // Hover tips for referenced powers.
        List<string> powerTypes = resolved
            .Where(e => e.Kind == EffectKind.ApplyPower && e.PowerId is not null)
            .Select(e => PowerRefs.TypeRef(_project, e.PowerId!))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (powerTypes.Count == 1)
        {
            sb.AppendLine();
            sb.AppendLine($"    protected override IEnumerable<IHoverTip> ExtraHoverTips => new IHoverTip[] {{ HoverTipFactory.FromPower<{powerTypes[0]}>() }};");
        }
        else if (powerTypes.Count > 1)
        {
            sb.AppendLine();
            sb.AppendLine("    protected override IEnumerable<IHoverTip> ExtraHoverTips => new IHoverTip[]");
            sb.AppendLine("    {");
            foreach (string pt in powerTypes)
            {
                sb.AppendLine($"        HoverTipFactory.FromPower<{pt}>(),");
            }

            sb.AppendLine("    };");
        }

        // Custom art: point the card's portrait at the .ctex shipped in the pack.
        if (card.ArtFile is not null)
        {
            string artRoute = Export.Exporter.CardArtRoute(_project, card);
            sb.AppendLine();
            sb.AppendLine("    // Art ships as a .ctex, which the engine's texture loader reads directly.");
            sb.AppendLine($"    public override string PortraitPath => \"res://{artRoute}\";");
        }

        // Constructor.
        sb.AppendLine();
        sb.AppendLine($"    public {typeName}()");
        sb.AppendLine($"        : {CardCoreEmitter.BaseCall(core)}");
        sb.AppendLine("    {");
        sb.AppendLine("    }");

        // X 费 / X 星：费用在打出时按消耗量决定，游戏靠这两个属性识别（卡面显示 X）。
        foreach (string line in CardCoreEmitter.XCostFlags(core))
        {
            sb.AppendLine(line);
        }

        // 按触发时机发出钩子。
        //
        // 每种时机对应游戏的一个虚方法：打出时是 OnPlay，抽到是 AfterCardDrawn，
        // 回合开始/结束是 AfterSideTurnStart/End 等。只发出实际用到的那几个，
        // 避免生成一堆空方法。
        // 条目名用于"让玩家挑牌"的选择界面提示语（游戏要求提示语来自本牌的 cards 表条目）。
        var body = new EffectEmitter(_project)
        {
            CardEntry = ModelIds.Slugify(typeName),
            PowerTypeFor = id => PowerRefs.TypeRef(_project, id),
            CardTypeFor = id => CardRefs.TypeRef(_project, id),
        };
        EmitContext cardCtx = EmitContext.ForCard();
        cardCtx = cardCtx with { HasDynamicVars = true };

        // 取值与升级都要按**这条效果自己**的槽去查变量名，不能按种类硬拼——
        // 同类有多条时，硬拼会让它们全读第一个槽（量小的被顶成量大的）。
        // 名字由变量模块一处算出，这里只做成查表。
        Dictionary<EffectDef, VarSlot> slotByEffect = slots
            .ToDictionary(s => s.Effect, EffectResolver.EffectReferenceComparer.Instance);

        cardCtx = cardCtx with
        {
            SlotFor = e => slotByEffect.TryGetValue(e, out VarSlot? s) ? s : null,
            RepeatSlotFor = e => repeatSlots.TryGetValue(e, out VarSlot? s) ? s : null,
            ThresholdSlotFor = ThresholdSlot,

            // 临时牌集合的两件事：这次结算的地址（那个局部量）与两个编号查表。
            // 两个查表都**只按这条卡的效果列表**算，所以读写两侧自动对上。
            SetAddress = CardHookEmitter.SetAddressName,
            EffectNumberFor = e => effectNumbers.TryGetValue(e, out int n) ? n : 1,
            SetNumberFor = id => CardSetCatalog.DefinitionNumber(card.Effects, id),
        };

        // 时机：哪些效果挂在哪个游戏钩子上、要不要计数器、要不要接收战斗钩子——
        // 全部由时机模块规划（那些规则以前散在下面这三百行里），这里只按固定顺序发出。
        CardTimingPlan timing = TimingModule.PlanCard(resolved);
        CardHookEmitter.Emit(sb, timing, body, cardCtx, card);

        // Upgrade.
        //
        // 每条效果自带升级增量。
        //
        // 计算型有两个可升级的部分，分别对应游戏的两个变量：
        // 计数值的升级目标随数值的种类而变：
        //   计算型伤害 -> 增量变量是 ExtraDamage
        //   计算型格挡 -> 增量变量是 CalculationExtra
        //   两者共有    -> 基础值是 CalculationBase
        //   其它计算型 -> 本类自己发的 _calcBase{n} / _calcPerUnit{n} 字段
        // 固定型则改该数值对应的变量。
        // 增量语句**按归属分开**：卡级的（费用）与每条效果自己的。
        //
        // 分这么细是为了让"等级表优先"落到实处：某条效果填了等级表，它的升级就完全由
        // 等级表说了算，**不再叠加增量**（那正是校验器提示的 "the delta is ignored"）；
        // 而同一条卡上没填等级表的效果，增量照旧每级加一次。
        // 混在一起写的话，两种情况分不开，只能二选一——不是丢了增量，就是让等级表与
        // 增量同时生效（与校验器说的不一致，而且往往把数值算重）。
        var cardUpgradeLines = new List<string>();
        var effectUpgradeLines = new List<(EffectDef Effect, List<string> Lines)>();

        // 升级语句的写法由槽给出（VarSlot.UpgradeText）：改哪个变量、还是改哪个私有字段，
        // 一处决定。同一条语句只写一次——两条效果升级到同一个变量时不能写两遍。
        List<string> LinesFor(EffectDef effect)
        {
            foreach ((EffectDef owner, List<string> lines) in effectUpgradeLines)
            {
                if (ReferenceEquals(owner, effect))
                {
                    return lines;
                }
            }

            var fresh = new List<string>();
            effectUpgradeLines.Add((effect, fresh));
            return fresh;
        }

        void AddUpgradeLine(EffectDef effect, string line)
        {
            List<string> lines = LinesFor(effect);

            if (!lines.Contains(line))
            {
                lines.Add(line);
            }
        }

        // 卡级的增量（费用）永远每级加一次。
        void AddCardUpgradeLine(string line)
        {
            if (!cardUpgradeLines.Contains(line))
            {
                cardUpgradeLines.Add(line);
            }
        }

        // 升级的次数上限：默认 1，等级表要多次升级时按 CardDef 发出的值来。
        int maxLevel = Math.Max(0, card.MaxUpgradeLevel);
        bool hasLevelTables = resolved.Any(e => e.HasLevels);

        // 用等级表时改成"按等级绝对赋值"——每次升级把这级该有的数**直接写上去**，
        // 而不是加一个增量。这样才表达得了"每级加得不一样"（第 1 级 +2、第 2 级 +3）。
        //
        // **单次升级也能用等级表**：它是唯一能换算式来源的写法（"升级后改成按弃牌堆数算"），
        // 那件事加法做不到。所以判据只看有没有表、能不能升级，不看"能升几次"——
        // 上一版要求 maxLevel > 1，于是"只写一格的等级表"整份被丢掉：升级什么都不做，
        // 而且哪儿都不报错（只有一个空着的 OnUpgrade）。
        bool perLevel = maxLevel > 0 && hasLevelTables;

        var levelLines = new Dictionary<int, List<string>>();

        void AddLevelLine(int level, string line)
        {
            if (!levelLines.TryGetValue(level, out List<string>? list))
            {
                list = new List<string>();
                levelLines[level] = list;
            }

            if (!list.Contains(line))
            {
                list.Add(line);
            }
        }

        // 费用升级。
        //
        // 原版最常见的升级方式就是它（"痛击" -2、"全身撞击" -1），对应
        // <c>CardEnergyCost.UpgradeBy(addend)</c>。X 费牌在游戏侧会自行跳过。
        //
        // 它排在效果升级**之前**：这里不依赖其它变量，先改费用更直观。
        //
        // 多次升级（等级表）时它同样每个等级加一次——费用是**增量**语义：
        // "每升一级便宜 1 点"。要表达"第 1 级 -1、第 2 级 -3"这种，
        // 用等级表做不到（那需要绝对赋值），所以多级时费用按每级同样的增量走。
        foreach (string line in CardCoreEmitter.UpgradeLines(core))
        {
            AddCardUpgradeLine(line);
        }

        foreach (EffectDef effect in resolved)
        {
            slotByEffect.TryGetValue(effect, out VarSlot? slot);
            repeatSlots.TryGetValue(effect, out VarSlot? repeatSlot);

            // ---- 等级表：这一级的数值直接写上去（绝对赋值）----
            //
            // 与增量模型的关系是"二者择一"：填了等级表就按等级绝对赋值，
            // 不再叠加 Upgrade 里的增量（校验器会提示两者都填的情况）。
            // **条件不在等级表里**（见 EffectCondition 的说明）——有等级表时条件增量
            // 同样被忽略，与其它增量的口径一致。
            if (effect.HasLevels)
            {
                for (int level = 1; level <= maxLevel; level++)
                {
                    foreach (string line in AbsoluteLevelLines(effect, slot, repeatSlot, level, body, ThresholdSlot))
                    {
                        AddLevelLine(level, line);
                    }
                }

                continue;
            }

            // ---- 条件侧的升级：改的是取值方法那两个字段 ----
            //
            // 条件的基础值/系数存在生成类的私有字段里（条件不占卡上的变量槽），
            // 所以升级语句是"字段 +="，与其它计算型数值的升级同一套写法。
            if (effect.Condition is { } condition)
            {
                var sides = new[] { (IsLeft: true, condition.LeftUpgrade, condition.LeftPerUnitUpgrade), (IsLeft: false, condition.RightUpgrade, condition.RightPerUnitUpgrade) };

                foreach ((bool isLeft, int baseDelta, decimal perUnitDelta) in sides)
                {
                    if (baseDelta == 0 && perUnitDelta == 0)
                    {
                        continue;
                    }

                    int fieldIndex = body.FieldIndexForCondition(effect, isLeft);

                    if (fieldIndex < 0)
                    {
                        continue;
                    }

                    VarSlot field = VariableModule.FieldSlot(effect, ValueRole.Value);

                    if (baseDelta != 0)
                    {
                        AddUpgradeLine(effect, field.UpgradeText(VarPart.Base, baseDelta, fieldIndex));
                    }

                    if (perUnitDelta != 0)
                    {
                        AddUpgradeLine(effect, field.UpgradeText(VarPart.PerUnit, perUnitDelta, fieldIndex));
                    }
                }
            }

            if (!effect.Upgrade.IsSet)
            {
                continue;
            }

            // 两个费用阈值：筛了那一维时升级改的是卡上那两个变量
            // （基础值 EnergyAmount / 变量系数 EnergyAmountExtra）。
            //
            // **两项都改**：阈值是"基础值 + 来源 × 变量系数"，两个都是卡上的变量，
            // 所以两个增量都落得下去（以前系数是算式里的字面量，填了没反应）。
            foreach (ValueRole role in CostRoles.All)
            {
                if (ThresholdSlot(effect, role) is not VarSlot costSlot)
                {
                    continue;
                }

                if (effect.Upgrade.AmountDeltaOf(role) != 0)
                {
                    AddUpgradeLine(effect, costSlot.UpgradeText(VarPart.Base, effect.Upgrade.AmountDeltaOf(role)));
                }

                if (effect.Upgrade.PerUnitDeltaOf(role) != 0)
                {
                    AddUpgradeLine(effect, costSlot.UpgradeText(VarPart.PerUnit, effect.Upgrade.PerUnitDeltaOf(role)));
                }
            }

            // 重复次数的升级独立于数值升级，两者可以同时存在。
            //
            // 次数是计算型时改的不是 RepeatVar（那是循环不读的键），而是算次数的字段——
            // 那种情况交给下面"其它计算型"分支按 ForRepeat 找方法。这里只处理固定次数。
            if (effect.Upgrade.RepeatDelta != 0 && !effect.CalculatedRepeat && repeatSlot is not null)
            {
                AddUpgradeLine(effect, repeatSlot.UpgradeText(VarPart.Base, effect.Upgrade.RepeatDelta));
            }

            // 次数是计算型的：次数 = Repeat + 每单位 × 来源，所以升级改的是算次数的那两个字段
            // ——基础值与变量系数各一项（"每失去过一次生命就多打一次"升级后系数变大）。
            // 上一版只写了基础值那一项，系数那一项填了被静默丢弃。
            if (effect.CalculatedRepeat &&
                (effect.Upgrade.RepeatDelta != 0 || effect.Upgrade.RepeatPerUnitDelta != 0))
            {
                int fieldIndex = body.FieldIndexFor(effect, forRepeat: true);

                if (fieldIndex >= 0)
                {
                    VarSlot repeatField = VariableModule.FieldSlot(effect, ValueRole.Repeat);

                    if (effect.Upgrade.RepeatDelta != 0)
                    {
                        AddUpgradeLine(effect, repeatField.UpgradeText(VarPart.Base, effect.Upgrade.RepeatDelta, fieldIndex));
                    }

                    if (effect.Upgrade.RepeatPerUnitDelta != 0)
                    {
                        AddUpgradeLine(effect, repeatField.UpgradeText(VarPart.PerUnit, effect.Upgrade.RepeatPerUnitDelta, fieldIndex));
                    }
                }
            }

            // 计算型的伤害与格挡：基础值/增量各改**自己那个槽**的变量。
            if (effect.IsCalculated && slot is not null && slot.Storage == VarStorage.GameVar)
            {
                if (effect.Upgrade.AmountDelta != 0)
                {
                    AddUpgradeLine(effect, slot.UpgradeText(VarPart.Base, effect.Upgrade.AmountDelta));
                }

                if (effect.Upgrade.PerUnitDelta != 0)
                {
                    AddUpgradeLine(effect, slot.UpgradeText(VarPart.PerUnit, effect.Upgrade.PerUnitDelta));
                }

                continue;
            }

            // 其它计算型：数值在本类自己的字段里。
            //
            // 不能像固定型那样去改 DynamicVars["<状态名>"]：计算型的施加状态不声明 PowerVar
            // （数值是算出来的，没有固定值可填），所以那个键根本不存在，改它会抛
            // KeyNotFoundException——而且只在升级时抛，卡面预览看不出来。
            //
            // 只找**数值**那一份（ForRepeat 为假）：同一张牌上"数值计算型 + 次数计算型"
            // 会有两个方法，不区分的话会把数值的增量加到算次数的基础值上（GrudgeRepaid
            // 就是这种组合，专门盯着这件事）。
            if (effect.IsCalculated)
            {
                int fieldIndex = body.FieldIndexFor(effect, forRepeat: false);

                if (fieldIndex >= 0)
                {
                    VarSlot field = VariableModule.FieldSlot(effect, ValueRole.Value);

                    if (effect.Upgrade.AmountDelta != 0)
                    {
                        AddUpgradeLine(effect, field.UpgradeText(VarPart.Base, effect.Upgrade.AmountDelta, fieldIndex));
                    }

                    if (effect.Upgrade.PerUnitDelta != 0)
                    {
                        AddUpgradeLine(effect, field.UpgradeText(VarPart.PerUnit, effect.Upgrade.PerUnitDelta, fieldIndex));
                    }

                    continue;
                }
            }

            // 固定值：改**这条效果自己**那个槽的变量。
            //
            // 不能按种类硬拼名字——同类有多条时（两条伤害、同一状态施加两次）
            // 那样会让所有效果都改第一个槽的变量，两条的升级混在一起。
            if (effect.Upgrade.AmountDelta != 0 && slot is not null)
            {
                AddUpgradeLine(effect, slot.UpgradeText(VarPart.Base, effect.Upgrade.AmountDelta));
            }
        }

        // 发出 OnUpgrade。
        //
        // 两种形状：
        //   等级表（这张牌有任一效果填了表，且能升级）—— switch (CurrentUpgradeLevel)，
        //     每一级把该级该有的数值**绝对赋值**。这样才表达得了"每级加得不一样"，
        //     也才表达得了"每级换算式来源"（算式分支见 EmitMultiplier）。
        //   增量（其余情况）—— 一列 UpgradeValueBy / += 语句，每次升级执行一遍。
        //
        // **等级表优先**：填了表的那条效果，这一级的数值完全由表说了算，不与增量叠加
        // （校验器也是这么说的）。同一条卡上**没填表**的效果不受影响，它的增量照旧每级加一次
        // ——那两种效果混在一张卡上是合法的（一条换算式来源、另一条只涨数值）。
        //
        // 卡级的增量（费用）与等级表无关：费用是"每升一级便宜 1 点"的增量语义，
        // 每一级都要加，所以它在每个 case 里都出现。
        bool anyLevelLines = levelLines.Count > 0;

        // 没有等级表、只有增量的效果：它们的增量要写进每个 case（每级加一次）。
        var plainIncrements = effectUpgradeLines
            .Where(e => !e.Effect.HasLevels)
            .SelectMany(e => e.Lines)
            .ToList();

        if (perLevel && (anyLevelLines || cardUpgradeLines.Count > 0 || plainIncrements.Count > 0))
        {
            sb.AppendLine();
            sb.AppendLine("    /// <summary>");
            sb.AppendLine($"    /// 这张牌可以升级 {maxLevel} 次。每一级把该级该有的数值直接写上去——");
            sb.AppendLine("    /// 等级表要表达的是\"每级加得不一样\"，加法做不到这件事。");
            sb.AppendLine("    /// </summary>");

            // 只升一次时不必发：那是 CardModel 的默认值。
            if (maxLevel != 1)
            {
                sb.AppendLine($"    public override int MaxUpgradeLevel => {maxLevel};");
                sb.AppendLine();
            }
            sb.AppendLine("    protected override void OnUpgrade()");
            sb.AppendLine("    {");
            sb.AppendLine("        switch (CurrentUpgradeLevel)");
            sb.AppendLine("        {");

            for (int level = 1; level <= maxLevel; level++)
            {
                sb.AppendLine($"            case {LevelNumberFor(level)}:");

                foreach (string line in cardUpgradeLines)
                {
                    sb.AppendLine($"                {line}");
                }

                foreach (string line in plainIncrements)
                {
                    sb.AppendLine($"                {line}");
                }

                if (levelLines.TryGetValue(level, out List<string>? lines))
                {
                    foreach (string line in lines)
                    {
                        sb.AppendLine($"                {line}");
                    }
                }

                sb.AppendLine("                break;");
            }

            sb.AppendLine("        }");
            sb.AppendLine("    }");
        }
        else
        {
            var allLines = new List<string>(cardUpgradeLines);

            foreach ((EffectDef _, List<string> lines) in effectUpgradeLines)
            {
                allLines.AddRange(lines);
            }

            if (allLines.Count == 0 && maxLevel == 1)
            {
                // 没有任何升级改动，也不需要说明能升几次。
            }
            else
            {
                // 不可升级（0）或多级但没填等级表时也要发 MaxUpgradeLevel：
                // 后者要让游戏知道能升几次，否则只升一次就到底了。
                if (maxLevel != 1)
                {
                    sb.AppendLine();
                    sb.AppendLine($"    public override int MaxUpgradeLevel => {maxLevel};");
                }

                if (allLines.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("    protected override void OnUpgrade()");
                    sb.AppendLine("    {");
                    foreach (string line in allLines)
                    {
                        sb.AppendLine($"        {line}");
                    }

                    sb.AppendLine("    }");
                }
            }
        }

        // 就地计算的数值方法。
        EmitCalcMethods(sb, body.PendingMethods);

        // 条件侧的取值方法（启用条件的两侧各至多一个，见 PendingConditionMethod）。
        EmitConditionMethods(sb, body.PendingConditions);

        // 费用阈值的来源取值方法（计算型阈值都走这一条，见 PendingThresholdMethod）。
        EmitThresholdMethods(sb, body.PendingThresholds);

        // "这张牌的伤害增加"需要辅助方法：本场战斗的直接加在当前实例上，永久的还要
        // 写进存档并同步牌组里的那张牌。

        // "使某些牌的伤害增加"需要一个能改**别人**的辅助方法（原版爪击/撕咬的做法）。
        //
        // 是否发出只看 implementsPermanentBoost——它同时也是类声明上挂不挂
        // IPermanentDamageBoost 的判据。两处条件必须一致：只写类声明不写实现的话
        // 会编译失败（接口成员缺失），那张牌只要用一次永久增伤就炸。
        if (implementsPermanentBoost)
        {
            EmitPermanentBoostImpl(sb, typeName);
        }

        // 永久重放同理：发出条件必须与类声明上挂不挂接口一致，否则接口成员缺失、编译不过。
        if (implementsPermanentReplay)
        {
            EmitPermanentReplayImpl(sb, typeName);
        }

        // 这张牌**自己**的重放次数（卡牌属性，不是效果）。
        //
        // 每张牌都发（默认 0 = 不重放）：它是一个 [SavedProperty] 自有属性，读档靠它回填——
        // 以前把标准值写在 AfterCreated 里，而那个钩子**不在读档时调用**，所以读档后会丢。
        foreach (string line in CardCoreEmitter.Replay(core))
        {
            sb.AppendLine(line);
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    /// <summary>
    /// 让这张牌**可以被永久增伤**：实现运行时接口，并把增益记在 [SavedProperty] 字段上。
    ///
    /// 为什么必须是"在自己的类上加字段"：游戏的存档格式只存卡牌 id、升级等级、
    /// **该类自己声明的 SavedProperty** 与附魔；动态数值不进存档，读档时按标准数值重建。
    /// 所以"永久改数值"唯一能持久的地方就是目标牌自己的类。
    ///
    /// 因此：本 mod 的牌之间可以永久互相增伤；原版卡牌不行（我们改不了它们的类）。
    /// 校验器会就这一点给出提示。
    ///
    /// 没有固定伤害值的牌（技能、能力）不实现——加了也没有 Damage 可改。
    /// 调用方只在确实要挂接口时才发（见 GenerateCard 里的注释）。
    /// </summary>
    private static void EmitPermanentBoostImpl(StringBuilder sb, string typeName)
    {
        sb.AppendLine();
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// 永久伤害增益。只有标了 SavedProperty 的字段会进存档，所以增益记在它身上；");
        sb.AppendLine("    /// 数值由 CanonicalVars 从这里读出来（原版「镰刀」TheScythe 是同样的写法），");
        sb.AppendLine("    /// 所以读档后增益还在。");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    [SavedProperty]");
        sb.AppendLine("    public int PermanentDamageBoost");
        sb.AppendLine("    {");
        sb.AppendLine("        get => _permanentDamageBoost;");
        sb.AppendLine("        set");
        sb.AppendLine("        {");
        sb.AppendLine("            AssertMutable();");
        sb.AppendLine();
        sb.AppendLine("            // 按增量调整，而不是整体重算：升级也改的是同一个数值，直接赋值");
        sb.AppendLine("            // 会把升级带来的提升抹掉。读档时这里从 0 加到存档值，正好。");
        sb.AppendLine("            int delta = value - _permanentDamageBoost;");
        sb.AppendLine("            _permanentDamageBoost = value;");
        sb.AppendLine();
        sb.AppendLine("            // 每个伤害槽都加（这张牌可能有多条伤害，各占一个变量）。");
        sb.AppendLine("            ModForge.Generated.Runtime.DamageSlots.AddToAll(this, delta);");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    private int _permanentDamageBoost;");
        sb.AppendLine();
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// 降级会从标准数值重建动态变量，把已经加上去的永久增益一起抹掉；");
        sb.AppendLine("    /// 累计的增量还记在字段里，这里补回去。原版「巨镰」TheScythe 的 AfterDowngraded");
        sb.AppendLine("    /// 处理的就是同一件事（它按 基数+累计 重算，我们按累计补差值）。");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    protected override void AfterDowngraded()");
        sb.AppendLine("    {");
        sb.AppendLine("        // 重建后的数值＝标准数值，所以直接把累计增量补上，不必再算差值。");
        sb.AppendLine("        ModForge.Generated.Runtime.DamageSlots.AddToAll(this, _permanentDamageBoost);");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// 把伤害永久加上去，并同步牌组里的那张牌。");
        sb.AppendLine("    /// 两张都要改：战斗中的是临时副本（只改它出了战斗就丢），");
        sb.AppendLine("    /// 牌组里的才是会被存档的那张。");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    public void ApplyPermanentDamageBoost(int extra)");
        sb.AppendLine("    {");
        sb.AppendLine("        if (extra == 0)");
        sb.AppendLine("        {");
        sb.AppendLine("            return;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        PermanentDamageBoost += extra;");
        sb.AppendLine();
        sb.AppendLine($"        if (DeckVersion is {typeName} deck && !ReferenceEquals(deck, this))");
        sb.AppendLine("        {");
        sb.AppendLine("            deck.PermanentDamageBoost += extra;");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
    }

    /// <summary>
    /// 让这张牌**可以被永久增加重放次数**：实现运行时接口。
    ///
    /// 这里**不再自己声明存档字段**——重放次数已经由卡牌核心发的那条
    /// <c>[SavedProperty] ReplayCount</c> 承载（每张牌都有，默认 0）。永久加成与这张牌
    /// 自身的重放数叠在同一个属性上，所以两者一起进存档、一起读回来，不会各记一份而对不上。
    ///
    /// 有一处**关键差别**值得写下来（早期版本踩过）：
    ///
    ///   伤害的永久增益作用于**动态变量**，而降级（<c>DowngradeInternal</c>）会把动态变量
    ///   从标准数值重建、把增益抹掉，所以增伤那边要覆写 <c>AfterDowngraded</c> 补回来。
    ///
    ///   重放次数最终写在 <c>CardModel.BaseReplayCount</c>（一个普通字段）上，
    ///   **降级不会重建它**。所以这边不能照抄：若也在 AfterDowngraded 里补一次，
    ///   降级一次就多加一遍。
    /// </summary>
    private static void EmitPermanentReplayImpl(StringBuilder sb, string typeName)
    {
        sb.AppendLine();
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// 把重放次数永久加上去，并同步牌组里的那张牌。");
        sb.AppendLine("    /// 两张都要改：战斗中的是临时副本（只改它出了战斗就丢），");
        sb.AppendLine("    /// 牌组里的才是会被存档的那张。");
        sb.AppendLine("    ///");
        sb.AppendLine("    /// 加在 ReplayCount 上（卡牌核心发的 [SavedProperty] 属性），所以这个加成");
        sb.AppendLine("    /// 与这张牌自身的重放数记在同一处、一起进存档。");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    public void ApplyPermanentReplay(int extra)");
        sb.AppendLine("    {");
        sb.AppendLine("        if (extra == 0)");
        sb.AppendLine("        {");
        sb.AppendLine("            return;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        ReplayCount += extra;");
        sb.AppendLine();
        sb.AppendLine($"        if (DeckVersion is {typeName} deck && !ReferenceEquals(deck, this))");
        sb.AppendLine("        {");
        sb.AppendLine("            deck.ReplayCount += extra;");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
    }

    /// <summary>
    /// 伤害增益作用范围的判断语句。
    ///
    /// 原版就是用卡牌标签判断的（例如 StrikeDummy"所有打击牌额外造成 3 点伤害"），
    /// 所以这里照着做：不限范围时只要求有来源卡牌；指定卡牌时还要类型相符。
    /// </summary>
    private string[] DamageFilterLines(PowerDef power)
    {
        switch (power.CardFilter)
        {
            case CardFilterKind.TagStrike:
                return new[]
                {
                    "// 只作用于带“打击”标签的牌，与原版的判断方式一致。",
                    "if (cardSource is null || !cardSource.Tags.Contains(CardTag.Strike))",
                    "{",
                    "    return 0m;",
                    "}",
                };

            case CardFilterKind.TagDefend:
                return new[]
                {
                    "if (cardSource is null || !cardSource.Tags.Contains(CardTag.Defend))",
                    "{",
                    "    return 0m;",
                    "}",
                };

            case CardFilterKind.TagShiv:
                return new[]
                {
                    "if (cardSource is null || !cardSource.Tags.Contains(CardTag.Shiv))",
                    "{",
                    "    return 0m;",
                    "}",
                };

            case CardFilterKind.SpecificCard when !string.IsNullOrWhiteSpace(power.FilterCardId):
                // 复用效果生成器的卡牌类型解析，本工程里的卡与其它卡牌的写法只有一处。
                string cardType = new EffectEmitter(_project).CardModelTypeRef(power.FilterCardId!);
                return new[]
                {
                    $"// 只作用于指定卡牌：{power.FilterCardId}。",
                    $"if (cardSource is not {cardType})",
                    "{",
                    "    return 0m;",
                    "}",
                };

            default:
                // 不限范围的伤害增益仍然要求伤害有来源卡牌，否则连"敌方环境伤害"也会被加成。
                return new[]
                {
                    "if (cardSource is null)",
                    "{",
                    "    return 0m;",
                    "}",
                };
        }
    }

    /// <summary>
    /// 这个状态的触发效果里有没有"玩家轴 + 一批"的目标（所有友方 / 随机一名友方）。
    ///
    /// 有的话生成物会写出 <c>Player</c> 这个类型名，得补一条 using。共享效果引用要先展开再判
    /// ——引用本身看到的只是"指向谁"，看不到那条效果的目标。
    /// </summary>
    private bool PowerUsesPlayerType(PowerDef power) =>
        power.Triggers.Any(t =>
        {
            EffectDef effect = EffectResolver.Resolve(_project, new[] { t.Effect }).FirstOrDefault()
                ?? t.Effect;

            return EffectCatalog.ScopeOf(effect.Kind) == TargetScope.Player
                && effect.Target is EffectTarget.AllAllies or EffectTarget.RandomAlly;
        });

    private string GeneratePower(PowerDef power)
    {
        string typeName = _project.PowerTypeName(power);
        var sb = new StringBuilder();

        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using System.Linq;");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine("using MegaCrit.Sts2.Core.Combat;");
        sb.AppendLine("using MegaCrit.Sts2.Core.Commands;");
        sb.AppendLine("using MegaCrit.Sts2.Core.Context;");
        sb.AppendLine("using MegaCrit.Sts2.Core.Entities.Cards;");
        sb.AppendLine("using MegaCrit.Sts2.Core.Entities.Creatures;");
        sb.AppendLine("using MegaCrit.Sts2.Core.Entities.Multiplayer;");
        sb.AppendLine("using MegaCrit.Sts2.Core.Entities.Powers;");

        // Player 这个**类型名**只在"玩家轴 + 一批"那两档里出现（`foreach (Player ally …)` /
        // `Player? picked0 = …`）。单目标那几档写成表达式（`Owner.Player!`），不必引入类型。
        // 按需发：无条件发的话每个状态文件都多一行与它无关的 using（生成物是给人读的，
        // 也是"结构改动逐字节不变"那条验收的比对对象）——与卡牌那边"Entities.Powers 按需发"
        // 同一个规矩。
        if (PowerUsesPlayerType(power))
        {
            sb.AppendLine("using MegaCrit.Sts2.Core.Entities.Players;");
        }

        sb.AppendLine("using MegaCrit.Sts2.Core.GameActions.Multiplayer;");
        sb.AppendLine("using MegaCrit.Sts2.Core.Models;");
        sb.AppendLine("using MegaCrit.Sts2.Core.ValueProps;");
        sb.AppendLine();
        sb.AppendLine($"namespace {_project.Id}.Generated.Powers;");
        sb.AppendLine();

        bool isDebuff = power.Kind == PowerKind.Debuff;
        sb.AppendLine("/// <summary>");
        sb.AppendLine($"/// Generated {(isDebuff ? "debuff" : "buff")}. A real PowerModel, so it lives in the power bar,");
        sb.AppendLine("/// stacks, and decays like any vanilla power.");
        sb.AppendLine("/// </summary>");
        sb.AppendLine($"public sealed class {typeName} : PowerModel");
        sb.AppendLine("{");
        sb.AppendLine($"    public override PowerType Type => PowerType.{(isDebuff ? "Debuff" : "Buff")};");
        sb.AppendLine();
        sb.AppendLine($"    public override PowerStackType StackType => PowerStackType.{(power.Stack == PowerStack.Counter ? "Counter" : "Single")};");

        // Damage modifier.
        if (power.DamageModifier != DamageModifier.None)
        {
            sb.AppendLine();
            if (power.DamageModifier == DamageModifier.Additive)
            {
                sb.AppendLine("    /// <summary>Adds its stack count as flat damage to matching attacks from the owner.</summary>");
                sb.AppendLine("    public override decimal ModifyDamageAdditive(");
                sb.AppendLine("        Creature? target,");
                sb.AppendLine("        decimal amount,");
                sb.AppendLine("        ValueProp props,");
                sb.AppendLine("        Creature? dealer,");
                sb.AppendLine("        CardModel? cardSource)");
                sb.AppendLine("    {");
                sb.AppendLine("        // Only powered attacks from the owner are affected; unattributed damage is left alone.");
                sb.AppendLine("        if (dealer != Owner || !props.IsPoweredAttack())");
                sb.AppendLine("        {");
                sb.AppendLine("            return 0m;");
                sb.AppendLine("        }");
                sb.AppendLine();

                foreach (string line in DamageFilterLines(power))
                {
                    sb.AppendLine("        " + line);
                }

                sb.AppendLine();
                sb.AppendLine("        return Amount;");
                sb.AppendLine("    }");
            }
            else
            {
                sb.AppendLine("    /// <summary>Multiplies damage the owner receives; Amount is a percentage increase.</summary>");
                sb.AppendLine("    public override decimal ModifyDamageMultiplicative(");
                sb.AppendLine("        Creature? target,");
                sb.AppendLine("        decimal amount,");
                sb.AppendLine("        ValueProp props,");
                sb.AppendLine("        Creature? dealer,");
                sb.AppendLine("        CardModel? cardSource)");
                sb.AppendLine("    {");
                sb.AppendLine("        if (target != Owner || !props.IsPoweredAttack())");
                sb.AppendLine("        {");
                sb.AppendLine("            return 1m;");
                sb.AppendLine("        }");
                sb.AppendLine();
                sb.AppendLine("        return 1m + (Amount / 100m);");
                sb.AppendLine("    }");
            }
        }

        // Trigger hooks, grouped so each hook is emitted once.
        //
        // Shared references are expanded first, so a trigger pointing at a shared effect
        // emits that effect's behaviour instead of an empty placeholder.
        List<TriggeredEffect> triggers = power.Triggers
            .Select(t => new TriggeredEffect
            {
                When = t.When,
                Effect = EffectResolver.Resolve(_project, new[] { t.Effect }).FirstOrDefault() ?? t.Effect,
            })
            .ToList();

        var emitter = new EffectEmitter(_project)
        {
            PowerTypeFor = id => PowerRefs.TypeRef(_project, id),
            CardTypeFor = id => CardRefs.TypeRef(_project, id),
        };
        EmitContext powerCtx = EmitContext.ForPower();
        EmitContext powerHookCtx = EmitContext.ForPowerWithoutContext();

        // 时机：四种触发各挂在哪个钩子上、要不要 HookContext、回合结束那个钩子还要不要发
        // （衰减也由它驱动）——由时机模块规划，这里只调一次发出来。
        PowerHookEmitter.Emit(sb, power, TimingModule.PlanPower(power), emitter, powerCtx, powerHookCtx);

        // 费用阈值的来源取值方法：状态里的效果也能筛费用（触发式效果），
        // 所以这里同样要把登记过的方法发出来——不发的话会留下一个没人定义的调用（编译不过）。
        EmitThresholdMethods(sb, emitter.PendingThresholds);

        // 条件侧的取值方法同理：状态的效果也能带条件。
        EmitConditionMethods(sb, emitter.PendingConditions);

        sb.AppendLine("}");
        return sb.ToString();
    }

    /// <summary>Maps a pool name to the vanilla pool type.</summary>
    public static string PoolTypeName(string pool) => pool.ToLowerInvariant() switch
    {
        "ironclad" => "IroncladCardPool",
        "silent" => "SilentCardPool",
        "defect" => "DefectCardPool",
        "necrobinder" => "NecrobinderCardPool",
        "regent" => "RegentCardPool",
        "colorless" => "ColorlessCardPool",
        "curse" => "CurseCardPool",
        "status" => "StatusCardPool",
        "event" => "EventCardPool",
        "quest" => "QuestCardPool",
        "token" => "TokenCardPool",
        _ => throw new ArgumentException(
            $"Unknown card pool '{pool}'. Valid pools: ironclad, silent, defect, necrobinder, regent, " +
            "colorless, curse, status, event, quest, token."),
    };

}
