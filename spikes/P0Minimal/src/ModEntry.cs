using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models.CardPools;

namespace P0Minimal;

/// <summary>
/// P0 spike: verify that a third-party mod can (1) put a real card into a vanilla
/// character's pool and (2) define a real PowerModel buff with no framework dependency.
/// </summary>
[ModInitializer(nameof(Initialize))]
public static class ModEntry
{
    public static void Initialize()
    {
        // Cards must be registered into a pool or the game throws in ModelDb.Preload().
        ModHelper.AddModelToPool<IroncladCardPool, Cards.ForgeStrike>();
        ModHelper.AddModelToPool<IroncladCardPool, Cards.ForgeWard>();

        // Powers need no registration at all: ModelDb discovers every PowerModel subclass.
        _ = typeof(Powers.ForgeMightPower);

        // Spike-only: confirm the registration actually reached the vanilla pool.
        new Harmony("P0Minimal").PatchAll(typeof(ModEntry).Assembly);
    }
}
