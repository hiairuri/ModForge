using ModForge.Core.Model;
using ModForge.Core.Modules.Descriptions;

namespace ModForge.Core.Modules.Effects;

/// <summary>
/// 给牌增加"重放"次数：这张牌下次被打出时，整张牌多执行 N 次
/// （对应 <c>CardModel.BaseReplayCount</c>，原版"隐藏宝石""变形"用的就是它）。
///
/// 与"使牌的伤害增加"同构（都是运行时挑出一批牌去改），所以挑牌逻辑完全一样；
/// 区别只在改的是什么字段。
///
/// 两个持续范围走**不同的地方**：
///   本场战斗 —— 直接 `BaseReplayCount += N`。战斗中的牌是临时副本，战斗结束自然失效。
///               卡面的悬停说明由游戏自己跟着这个字段更新（它会加一条"重放 N 次"）。
///   永久     —— 写进目标牌自己的 `[SavedProperty]` 字段（运行时接口），跨战斗保留；
///               **原版卡**的类改不了，运行时自动落到**组件**那条路（数值随存档 props
///               往返、进战斗时应用，见运行时 <c>CardComponents</c>）。
/// </summary>
internal sealed class AddReplayBehavior : IEffectBehavior
{
    public EffectKind Kind => EffectKind.AddReplay;

    public void Emit(EffectEmit a)
    {
        // 同一张牌上可以有多条重放效果，局部变量带序号，避免两次 `pickedCards` 重名。
        string suffix = a.Emitter.NextBoostIndex();

        a.Line($"int replay{suffix} = (int){a.Amount};");

        foreach (string line in a.Emitter.PickCardsLines(a.Effect, a.Ctx, a.Amount, suffix))
        {
            a.Line(line);
        }

        a.Line($"foreach (var replayed in pickedCards{suffix})");
        a.Line("{");

        // 下面的行**自带缩进**（inner 已含一层），直接写、不走 a.Line。
        string inner = a.Indent + "    ";

        if (a.Effect.CostDuration == CostDuration.Permanent)
        {
            a.Sb.AppendLine($"{inner}// 永久：写进目标自己的存档字段，并同步牌组里的那张牌。");
            a.Sb.AppendLine($"{inner}// 原版卡没有那个字段，运行时会改挂随存档往返的组件（见 CardComponents）。");
            a.Sb.AppendLine($"{inner}ModForge.Generated.Runtime.ReplayBoost.Apply(replayed, replay{suffix});");
        }
        else
        {
            a.Sb.AppendLine($"{inner}// 本场战斗：直接加在目标的重放次数上。战斗中的牌是临时副本，战斗结束自然失效。");
            a.Sb.AppendLine($"{inner}// 卡面的悬停说明由游戏自己跟着这个字段更新（它会加一条 \"重放 N 次\"）。");
            a.Sb.AppendLine($"{inner}replayed.BaseReplayCount += replay{suffix};");
        }

        a.Line("}");
    }

    /// <summary>
    /// 措辞按"持续多久"与主语单复数变（与"伤害增加"同一套判断）。
    /// </summary>
    public string Describe(EffectDescribe a)
    {
        a.AddPileVars();

        bool singular = a.Effect.Pick.Source == Modules.CardPicking.PickSource.ThisCard ||
                        a.Effect.Pick.IsNamedPick;

        string key = (a.Effect.CostDuration == CostDuration.Permanent, singular) switch
        {
            (true, true) => "addReplayPermanentSingular",
            (true, false) => "addReplayPermanent",
            (false, true) => "addReplayCombatSingular",
            _ => "addReplayCombat",
        };

        // {num} 要重新算一遍（与入口预填的差别在**计算型**：那种要引用变量名，
        // 入口为防某条模板用到才填字面量）。
        string num = a.Names is null
            ? a.Effect.Amount.ToString()
            : a.Names.Token(a.Names.Value, a.Effect.Amount);

        return a
            .With("num", num)
            .With("pick", PickerWording.PickLabel(
                a.Project, a.Effect, a.Sentences, a.Names, sentenceStart: true))
            .Render(key);
    }

    public EffectSchemaEntry Schema => new(
        kind: "", labelZh: "给牌添加重放", labelEn: "Add replay to cards",
        scope: TargetScope.Cards, amount: true,
        durations: new[] { CostDuration.ThisCombat, CostDuration.Permanent }
            .Select(d => d.ToString()).ToArray());
}
