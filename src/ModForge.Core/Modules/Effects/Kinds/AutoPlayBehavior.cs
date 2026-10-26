using ModForge.Core.Model;
using ModForge.Core.Modules.CardPicking;
using ModForge.Core.Modules.Descriptions;

namespace ModForge.Core.Modules.Effects;

/// <summary>
/// 自动打出。一条效果两种作用对象，由"这张牌自己"那一档区分
/// （<c>EffectDef.IsSelfAutoPlay</c>）：
///
///   某个牌堆   —— 打出该牌堆里的 N 张。与"消耗牌堆中的牌"同构：从末尾取（顶牌），
///                 逐个交给 <c>CardCmd.AutoPlay</c>。自动打出不走费用、也不进入正常
///                 出牌流程（不会有"打出后"的连击计数），这正是原版"浩劫"的语义。
///                 目标参数传 null：AutoPlay 会自己按卡牌的目标类型挑一个合法目标。
///
///                 **"重复打出"在这里实现，不套外面那层通用重复循环**（共用层
///                 <c>EffectEmitter.Emit</c> 为它开了直通道）。那条循环会把整条效果
///                 重复一遍，包括"取牌堆"（＝重新选牌）——而自动打出的重复应当是
///                 "把打出的动作再做几轮"，牌堆只认一次。所以牌堆查询写在循环
///                 **外面**，里面按 <c>打几张 × 重复几轮</c> 逐个打出；每打出一张
///                 它就离开牌堆，下一轮自然取到下一张。
///
///   这张牌自己 —— 这张牌从牌堆里出来，不是"打出时做什么"。共用层
///                 <c>CodeGenerator</c> 会把它挑出来走引擎的自动打出手时机，
///                 所以 <see cref="IEffectBehavior.Emit"/> 里什么也不发
///                 （句子另有一条模板："将自己打出"）。
/// </summary>
internal sealed class AutoPlayBehavior : IEffectBehavior
{
    public EffectKind Kind => EffectKind.AutoPlay;

    public void Emit(EffectEmit a)
    {
        if (a.Effect.IsSelfAutoPlay)
        {
            return;
        }

        // 从哪个牌堆自动打出：选牌器流程一/二那一格（"自动打出"的策略限定只有单个牌堆）。
        string source = $"PileType.{CardPickCollector.PileTypeName(a.Effect.Pick.SourcePile())}";

        // 选牌（取哪个牌堆）只做一次。
        a.Line($"var autoPlayPile = {source}.GetPile({a.Ctx.Player});");

        (IReadOnlyList<string> setup, string repeat) = a.Emitter.RepeatCount(a.Effect, a.Ctx, a.Indent);

        // 下面这些行**自带缩进**（inner 已含一层循环缩进），所以直接写、不走 a.Line
        // ——那个会给每行再垫一层最外层缩进。
        foreach (string line in setup)
        {
            a.Sb.AppendLine(line);
        }

        // 重复就一次（且没要先算的东西）时不发循环，省一层缩进。
        bool loop = repeat != "1";
        string inner = loop ? a.Indent + "    " : a.Indent;

        if (loop)
        {
            a.Sb.AppendLine($"{a.Indent}for (int r = 0; r < {repeat}; r++)");
            a.Sb.AppendLine($"{a.Indent}{{");
        }

        a.Sb.AppendLine($"{inner}int autoPlayCount = (int){a.Amount};");
        a.Sb.AppendLine($"{inner}for (int n = 0; n < autoPlayCount; n++)");
        a.Sb.AppendLine($"{inner}{{");

        // 牌堆空了就停：算出来的张数可能大于实际张数。
        a.Sb.AppendLine($"{inner}    if (autoPlayPile.Cards.Count <= 0)");
        a.Sb.AppendLine($"{inner}    {{");
        a.Sb.AppendLine($"{inner}        break;");
        a.Sb.AppendLine($"{inner}    }}");
        a.Blank();

        // 从末尾取（顶牌），这样取走一张不会让后面的索引错位。
        a.Sb.AppendLine($"{inner}    var autoPlaying = autoPlayPile.Cards[autoPlayPile.Cards.Count - 1];");
        a.Sb.AppendLine($"{inner}    await CardCmd.AutoPlay({a.Ctx.ChoiceContext}, autoPlaying, null);");
        a.Sb.AppendLine($"{inner}}}");

        if (loop)
        {
            a.Sb.AppendLine($"{a.Indent}}}");
        }
    }

    // 自动打出：按"作用对象"分两句。
    //
    //   某个牌堆   —— 与"消耗牌堆中的牌"同构："自动打出{pile}中的 {num} 张牌"。
    //   这张牌自己 —— 前半句的时机与牌堆条件由 TimingPhrase 拼上，这里只说动作，
    //                 合起来读就是原版轰击的写法："回合开始时，若这张牌在消耗牌堆中，将自己打出。"
    public string Describe(EffectDescribe a)
    {
        a.AddPileVars();

        return a.Render(a.Effect.IsSelfAutoPlay ? "autoPlayThisCard" : "autoPlayFromPile");
    }

    // "这张牌自己"那一档不要数值（打出的就是这张牌），由选牌策略的
    // CardPickPolicy.SelfNoAmount 声明（见 CardPickPolicies.For(AutoPlay)）。
    public EffectSchemaEntry Schema => new(
        kind: "", labelZh: "自动打出", labelEn: "Auto-play",
        scope: TargetScope.Cards, amount: true);
}
