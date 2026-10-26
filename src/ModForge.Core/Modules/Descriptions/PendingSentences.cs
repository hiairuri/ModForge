using ModForge.Core.Model;
using ModForge.Core.Modules.CardPicking;
using ModForge.Core.Modules.Effects;

namespace ModForge.Core.Modules.Descriptions;

/// <summary>
/// 描述模块到效果目录的**入口**：30 种效果全部搬进了 <c>Modules/Effects/Kinds/</c>，
/// 这一句由各自的行为出——这里只负责"入口已经备好 who/num/kind 三个词"与"问目录谁来说"
/// （<see cref="EffectCatalog.Find"/>），外加一条**永远不该走到**的 "unknown" 兜底
/// （phrasetest 断言没有任何形状走到它）。
/// </summary>
internal static class PendingSentences
{
    /// <summary>把一条效果写成一句话（不含重复次数）。</summary>
    public static string Sentence(
        ModProject? project,
        EffectDef effect,
        SentenceSet sentences,
        DescriptionModule.EffectNames? names = null)
    {
        var vars = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // "谁"要按作用域取词：同一档在两轴上的读法不同（"所有友方"在玩家轴上读"所有玩家"），
            // 而玩家宠物那一组自带"…的奥斯提"的说法。
            ["who"] = DescriptionWords.Who(EffectCatalog.ScopeOf(effect.Kind), effect.Target, sentences),

            // 固定值时这里就是"这个效果的量"，所以给它的变量（Damage / Damage2 / Block…）。
            // 计算型时它不参与句子（模板走 source/plus 那套），保持原值以防某条模板用到。
            ["num"] = effect.IsCalculated
                ? effect.Amount.ToString()
                : (names is null ? effect.Amount.ToString() : names.Token(names.Value, effect.Amount)),

            ["kind"] = effect.Kind.ToString(),
        };

        // 已搬进 Kinds/ 的效果：这一句由行为自己出（入口已经备好 who/num/kind 三个词）。
        if (EffectCatalog.Find(effect.Kind) is IEffectBehavior behavior)
        {
            return behavior.Describe(new EffectDescribe(project, effect, sentences, names, vars));
        }

        // 走到这里说明这个种类**还没搬进 Kinds/**、而且上面每一支都不认领它。
        // 渲染 "unknown" 那条通用句子（里面是枚举名），phrasetest 会断言**没有**任何形状
        // 走到这一支——所以"新增种类却忘了写描述"会当场现形，而不是印一句看似正常的话。
        ValueWording.AddCalculationVars(project, vars, effect, sentences, names);
        return sentences.Render("unknown", vars);
    }
}
