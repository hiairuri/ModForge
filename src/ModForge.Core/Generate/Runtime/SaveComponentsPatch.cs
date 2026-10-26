using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace ModForge.Generated.Runtime;

/// <summary>
/// 卡牌序列化两头的组件挂点。
///
/// 游戏的存档格式对"卡上有什么"是封闭的（id + 升级 + 该类自己的 SavedProperty + 附魔），
/// 但每张卡的存档形状里有一个**本来就在**的 props 块（<c>SerializableCard.Props</c>，
/// 联机同步也走它）——它按属性名反射回填、找不到就跳过，所以往里放**自己前缀**的条目
/// 对游戏与其它 mod 都无害。两个 Harmony postfix 就挂在这条通道的两头：
///
///   写档（ToSerializable）——组件仓库里有数据的卡，把组件值塞进 props；
///     没有组件的卡一个字节不动（大多数卡的存档形状与原版完全一致）。
///   读档（FromSerializable）——props 里有自己条目的卡，把数值读回组件仓库；
///     这张卡若是牌组原件，之后每次进战斗由 <see cref="ComponentHookHost"/> 应用到副本。
///
/// 补丁由游戏装 mod 时的默认 <c>Harmony.PatchAll</c> 自动打上（ModManager 加载 mod DLL
/// 时对程序集里所有 [HarmonyPatch] 类型执行）——ModEntry 里不需要任何手动调用。
///
/// **多 mod**：两个 ModForge mod 同时装时各有各的这份补丁，各自只读写自己前缀的条目，
/// 对同一张卡是叠加而非覆盖；与其它 mod 写进 props 的任何条目互不相干。
/// </summary>
[HarmonyPatch]
internal static class SaveComponentsPatch
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(CardModel), nameof(CardModel.ToSerializable))]
    private static void AfterSerialize(CardModel __instance, SerializableCard __result)
    {
        CardComponentStore.WriteToSerializable(__instance, __result);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(CardModel), nameof(CardModel.FromSerializable), new[] { typeof(SerializableCard) })]
    private static void AfterDeserialize(SerializableCard save, CardModel __result)
    {
        CardComponentStore.ReadFromSerializable(__result, save);
    }
}
