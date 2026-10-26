using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Modding;

namespace ArtProbe;

/// <summary>
/// Spike entry point. Hooks ModelDb.Preload so the resource probe runs once the game
/// is fully initialized, then reports its findings to the log.
/// </summary>
[ModInitializer(nameof(Initialize))]
public static class ModEntry
{
    public static void Initialize()
    {
        new Harmony("ArtProbe").PatchAll(typeof(ModEntry).Assembly);
    }
}

[HarmonyPatch(typeof(ModelDb), nameof(ModelDb.Preload))]
internal static class ProbeHook
{
    [HarmonyPostfix]
    private static void Postfix() => Probe.Run();
}
