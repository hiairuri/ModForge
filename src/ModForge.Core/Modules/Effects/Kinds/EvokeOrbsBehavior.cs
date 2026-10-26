using ModForge.Core.Model;
using ModForge.Core.Modules.Descriptions;

namespace ModForge.Core.Modules.Effects;

/// <summary>
/// 激发能量球。**激发哪些球**由两格决定（见 <see cref="OrbEvokeTarget"/> 与
/// <see cref="EffectDef.OrbKinds"/>）：哪一头（最右 / 最左 / 全部 / 随机）× 哪些种类（可多选，空 = 不限）。
///
/// 数值 m 的含义对四档是同一个：**每个被选中的球各激发几次**。
///   最右 / 最左 / 随机 —— 只有那一个球，m 就是它被激发的次数；
///   全部               —— 每个球各 m 次（原版"碎片"就是 m=2："激发你所有的充能球两次"）。
/// 效果的**重复次数 n** 是另一层：把整件事做 n 遍。所以 m 与 n 各管一件事，不重叠。
///
/// 球在**谁**的球位上：命令收 <c>Player</c>，所以作用域是玩家轴
/// （见 <see cref="TargetScope.Player"/>）——激发队友的充能球也写得出来。
///
/// "m 次"的做法照抄原版（MultiCast / Quadcast / Shatter）：前面几次
/// <c>dequeue: false</c> 让**同一个球**反复激发，最后一次才把它移出队列。逐次都出队的话
/// 激发的是 m 个**不同**的球——"激发最右侧的球两次"会变成激发最右和最左两个球，那是错的。
///
/// 出码分三种形态，按"哪种最贴近游戏自己的写法"来选：
///
///   1. 最右 / 最左、不限种类 —— 直接用 <c>EvokeNext</c> / <c>EvokeLast</c>。原版形状。
///   2. 全部、不限种类        —— 数一遍球数，每个球按"m 次"激发。<c>Shatter</c> 的形状。
///   3. 其余（随机、或带种类筛选）—— 先把要激发的球挑出来，**逐个挪到队首**再激发。
///      游戏只给了"激发队首 / 队尾"两个接口（<c>Evoke</c> 是私有的），没有"激发任意一个"的
///      入口；<c>OrbQueue.Remove</c> + <c>Insert(0, …)</c> 都是公开的普通列表操作，所以那条路
///      走得通。代价是队列顺序会被改（画面上球可能跳一下），但激发的**是哪个球**这件事
///      与作者写的一致——比起悄悄激发错的那个，这个代价是值得的。
/// </summary>
internal sealed class EvokeOrbsBehavior : IEffectBehavior
{
    public EffectKind Kind => EffectKind.EvokeOrbs;

    public void Emit(EffectEmit a)
    {
        EffectDef effect = a.Effect;
        bool filtered = effect.OrbKinds.Count > 0;

        // 形态 1：一端、不限种类。
        if (!filtered && effect.EvokeTarget is OrbEvokeTarget.Rightmost or OrbEvokeTarget.Leftmost)
        {
            string call = effect.EvokeTarget == OrbEvokeTarget.Leftmost ? "EvokeLast" : "EvokeNext";

            a.AgainstTargetsBlock((player, at) => new[]
            {
                $"{at}int evokeCount = (int){a.Amount};",
                $"{at}for (int i = 0; i < evokeCount; i++)",
                $"{at}{{",
                $"{at}    await OrbCmd.{call}({a.Ctx.ChoiceContext}, {player}, i == evokeCount - 1);",
                $"{at}}}",
            });
            return;
        }

        // 形态 2/3 都要一对中间变量（队列与候选），而一张牌上可能有好几条激发效果，
        // 所以名字带上卡内唯一的编号——不带的后果是"两条激发效果在同一个方法里重名"，
        // 编译不过（实测）。
        int n = a.Emitter.NextLocalIndex();
        string queueVar = $"evokeQueue{n}";
        string orbsVar = $"evokeOrbs{n}";

        // 形态 2：全部、不限种类（原版"碎片"）。
        bool every = !filtered && effect.EvokeTarget == OrbEvokeTarget.All;

        a.AgainstTargetsBlock((player, at) =>
        {
            var lines = new List<string> { $"{at}var {queueVar} = {player}.PlayerCombatState.OrbQueue;" };

            if (every)
            {
                lines.Add($"{at}int evokeTotal{n} = {queueVar}.Orbs.Count;");
                lines.Add($"{at}for (int evokeIndex{n} = 0; evokeIndex{n} < evokeTotal{n}; evokeIndex{n}++)");
                lines.Add($"{at}{{");

                // 每个球都按"m 次"激发；最后一次出队，所以下一轮循环正好轮到下一个球。
                lines.Add($"{at}    int evokeCount = (int){a.Amount};");
                lines.Add($"{at}    for (int i = 0; i < evokeCount; i++)");
                lines.Add($"{at}    {{");
                lines.Add($"{at}        await OrbCmd.EvokeNext({a.Ctx.ChoiceContext}, {player}, i == evokeCount - 1);");
                lines.Add($"{at}    }}");
                lines.Add($"{at}}}");
                return lines;
            }

            // 形态 3：随机 / 带种类筛选。挑出候选，按目标那一档选出要激发的球，
            // 逐个挪到队首再激发（EvokeNext 只认队首）。
            lines.Add($"{at}var {orbsVar} = {CandidateExpr(a, queueVar)};");
            lines.Add($"{at}foreach (var evokeOrb in {ChosenExpr(a, effect, orbsVar, player)})");
            lines.Add($"{at}{{");

            // 已经在队首就不动它（多数情况下最右侧那个本来就在队首，这一步是空操作）。
            lines.Add($"{at}    if ({queueVar}.Orbs.IndexOf(evokeOrb) > 0)");
            lines.Add($"{at}    {{");
            lines.Add($"{at}        {queueVar}.Remove(evokeOrb);");
            lines.Add($"{at}        {queueVar}.Insert(0, evokeOrb);");
            lines.Add($"{at}    }}");

            lines.Add($"{at}    int evokeCount = (int){a.Amount};");
            lines.Add($"{at}    for (int i = 0; i < evokeCount; i++)");
            lines.Add($"{at}    {{");
            lines.Add($"{at}        await OrbCmd.EvokeNext({a.Ctx.ChoiceContext}, {player}, i == evokeCount - 1);");
            lines.Add($"{at}    }}");
            lines.Add($"{at}}}");
            return lines;
        });
    }

    /// <summary>形态 3 里"这一轮要激发哪几个球"的表达式。</summary>
    private static string ChosenExpr(EffectEmit a, EffectDef effect, string orbsVar, string player) =>
        effect.EvokeTarget switch
        {
            OrbEvokeTarget.Rightmost => $"{orbsVar}.Take(1)",
            OrbEvokeTarget.Leftmost => $"{orbsVar}.TakeLast(1)",
            OrbEvokeTarget.All => orbsVar,

            // 随机那一档用的**不是**目标那一路随机数：它抽的是"哪个球"，所以用球的那一路
            // （CombatOrbGeneration），原版生成球也用这条序列。
            _ => $"{orbsVar}.Count == 0 ? Array.Empty<OrbModel>()"
                 + $" : new[] {{ {orbsVar}[{player}.RunState.Rng.CombatOrbGeneration.NextInt({orbsVar}.Count)] }}",
        };

    /// <summary>候选球的表达式：按种类筛（没筛就是整条队列，按队首在前）。</summary>
    private static string CandidateExpr(EffectEmit a, string queueVar)
    {
        string queue = $"{queueVar}.Orbs";

        if (a.Effect.OrbKinds.Count == 0)
        {
            return $"{queue}.ToList()";
        }

        // 球的种类用**游戏自己的 id** 判（`orb.Id.Entry`，例如 LIGHTNING_ORB）：
        // 那是它做本地化查表用的身份，比类型名稳，将来游戏加球种也不会改名。
        string predicate = string.Join(
            " || ",
            a.Effect.OrbKinds.Select(kind => $"o.Id.Entry == \"{OrbEntry(kind)}\""));

        return $"{queue}.Where(o => {predicate}).ToList()";
    }

    /// <summary>球种对应的模型条目名（与生成类名同一个规矩：<c>LightningOrb</c> → <c>LIGHTNING_ORB</c>）。</summary>
    private static string OrbEntry(OrbKind kind) => $"{kind.ToString().ToUpperInvariant()}_ORB";

    // 激发哪一档 + 哪些种类都要嵌进句子（"激发你最右侧的闪电充能球 2 次"），
    // 否则读不出激发的是哪一个/哪几个。数值**算出来的**时另有一套模板
    // （"激发你全部的充能球 等于 手牌数 次"）——不调 AddCalculationVars 的话，
    // 句子会把基础值当成次数写出来（"0 次"）。
    public string Describe(EffectDescribe a)
    {
        EffectDef effect = a.Effect;

        bool all = effect.EvokeTarget == OrbEvokeTarget.All;

        // "哪几个球"的说法要跟着**作用对象**换物主：自己就是"你最右侧的"，作用于队友则是
        // "他们的最右侧的"（英文那一侧的区别就落在这里，中文同一句话）。
        string evokeWord = (effect.EvokeTarget switch
        {
            OrbEvokeTarget.Leftmost => "evokeTargetLeftmost",
            OrbEvokeTarget.All => "evokeTargetAll",
            OrbEvokeTarget.Random => "evokeTargetRandom",
            _ => "evokeTargetRightmost",
        }) + (a.SelfSubject ? string.Empty : "Other");

        string target = a.Word(evokeWord);

        // 种类的写法两边不同：中文顿号连成一串（"闪电、冰霜"），英文用 or（"Lightning or Frost"）；
        // 列表后面与"充能球 / Orb"之间还要一个连接（中文空、英文一个空格），否则英文会印成
        // "FrostOrb"。连接由句式表给（见 words.orbJoinSuffix），不在这里判语言。
        string orbJoin = a.Word("orbJoin");
        var names = new List<string>();

        foreach (OrbKind kind in effect.OrbKinds)
        {
            names.Add(a.OrbNameFor(kind));
        }

        string orbs = names.Count == 0
            ? string.Empty
            : DescriptionNames.JoinOrbNames(names, orbJoin) + a.Word("orbJoinSuffix");

        a.AddCalculationVars();

        // 四档句式名由两段拼出来：激发哪些（全部 / 一端）× 数值是不是算出来的；
        // "作用于别人"再多插一段 Other（作用域是玩家轴，见 TargetedKey）。
        string key = a.TargetedKey(all ? "evokeOrbsAll" : "evokeOrbs", effect.IsCalculated);

        return a
            .With("target", target)
            .With("orbs", orbs)
            .Render(key);
    }

    public EffectSchemaEntry Schema => new(
        kind: "", labelZh: "激发充能球", labelEn: "Evoke orbs",
        scope: TargetScope.Player, amount: true,
        needsEvokeTarget: true, needsOrbFilter: true);
}
