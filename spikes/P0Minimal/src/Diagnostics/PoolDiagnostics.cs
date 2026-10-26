using System;
using System.Linq;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;

namespace P0Minimal;

/// <summary>
/// Spike-only diagnostic. Confirms that the card registered via
/// ModHelper.AddModelToPool actually lands in the vanilla Ironclad card pool,
/// which is what makes it obtainable in rewards and shops.
///
/// This runs after ModelDb.Preload(), so it cannot disturb pool freezing.
/// </summary>
[HarmonyPatch(typeof(ModelDb), nameof(ModelDb.Preload))]
public static class PoolDiagnostics
{
    [HarmonyPostfix]
    public static void Postfix()
    {
        try
        {
            var ironclad = ModelDb.CardPool<IroncladCardPool>();
            var ironcladCards = ironclad.AllCards.ToList();
            var mine = ironcladCards.FirstOrDefault(c => c.GetType().Name == "ForgeStrike");

            GD.Print($"[P0DIAG] IroncladCardPool total={ironcladCards.Count} hasForgeStrike={mine is not null}");

            if (mine is not null)
            {
                GD.Print($"[P0DIAG] ForgeStrike id={mine.Id} pool={mine.Pool.Id} " +
                         $"rarity={mine.Rarity} type={mine.Type}");
                GD.Print($"[P0DIAG] ForgeStrike title='{mine.TitleLocString.GetFormattedText()}'");
            }
            else
            {
                GD.Print("[P0DIAG] ForgeStrike NOT in IroncladCardPool");
            }

            var allCards = ModelDb.AllCards.ToList();
            GD.Print($"[P0DIAG] ModelDb.AllCards total={allCards.Count} " +
                     $"hasForgeStrike={allCards.Any(c => c.GetType().Name == "ForgeStrike")}");

            var power = ModelDb.Power<Powers.ForgeMightPower>();
            GD.Print($"[P0DIAG] ForgeMightPower id={power.Id} type={power.Type} stack={power.StackType}");
        }
        catch (Exception ex)
        {
            GD.Print($"[P0DIAG] FAILED: {ex}");
        }
    }
}
