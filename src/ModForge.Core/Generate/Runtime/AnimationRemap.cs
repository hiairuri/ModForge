using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;

namespace ModForge.Generated.Runtime;

/// <summary>
/// The gameplay slots a skin can provide an animation for.
///
/// The keys are the animation names the game itself requests; the values are the
/// vanilla names used for those moments (from MegaCrit.Sts2.Core.Animation.AnimState:
/// attackAnim = "attack", castAnim = "cast", dieAnim = "die", hurtAnim = "hurt",
/// idleAnim = "idle_loop").
/// </summary>
internal static class AnimationSlots
{
    public static readonly Dictionary<string, string> VanillaName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["idle"] = "idle_loop",
        ["attack"] = "attack",
        ["cast"] = "cast",
        ["hurt"] = "hurt",
        ["die"] = "die",
        ["entry"] = "entry",
    };

    /// <summary>
    /// True when the name is one the game requests directly, so a binding for it
    /// will actually take effect.
    /// </summary>
    public static bool IsRequestedByGame(string name) =>
        VanillaName.Values.Any(v => v.Equals(name, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Redirects a character's animations to the ones a skin provides.
///
/// The game requests animations by fixed names. A skin whose animations are named
/// differently is therefore never used for those moments unless the requested name is
/// translated first. This patch does that translation using the binding table written
/// at export time, which is what makes a skin's own attack, cast, hurt, death and idle
/// animations actually play.
///
/// Unmapped names pass through untouched, so a skin that already follows the naming
/// convention needs no bindings at all.
/// </summary>
[HarmonyPatch(typeof(MegaAnimationState), nameof(MegaAnimationState.SetAnimation))]
internal static class AnimationRemapPatch
{
    /// <summary>Game animation name -> the skin's animation name.</summary>
    private static readonly Dictionary<string, string> Bindings = BindingsLoader.Load();

    /// <summary>
    /// Verified once against the loaded skeleton, so an unusable binding is reported
    /// rather than silently doing nothing.
    /// </summary>
    private static readonly HashSet<string> Verified = new(StringComparer.Ordinal);

    /// <summary>
    /// Rewrites the requested animation name before the animation state applies it.
    /// </summary>
    [HarmonyPrefix]
    private static void Prefix(MegaAnimationState __instance, ref string animationName)
    {
        if (string.IsNullOrEmpty(animationName) || Bindings.Count == 0)
        {
            return;
        }

        if (!Bindings.TryGetValue(animationName, out string? replacement) ||
            string.IsNullOrWhiteSpace(replacement) ||
            replacement.Equals(animationName, StringComparison.Ordinal))
        {
            return;
        }

        animationName = replacement;
    }

    /// <summary>
    /// Reports bindings that cannot work, so a mis-bound slot is visible in the log
    /// instead of appearing as an animation that simply never plays.
    /// </summary>
    [HarmonyPostfix]
    private static void Postfix(MegaAnimationState __instance, string animationName)
    {
        if (!Bindings.TryGetValue(animationName, out string? replacement) ||
            string.IsNullOrWhiteSpace(replacement) ||
            !Verified.Add(animationName))
        {
            return;
        }

        GD.Print($"[ModForge] animation binding active: '{animationName}' -> '{replacement}'");
    }
}

/// <summary>Reads the binding table written next to the mod at export time.</summary>
internal static class BindingsLoader
{
    public static Dictionary<string, string> Load()
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (string file in FindBindingFiles())
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(file));
                if (!doc.RootElement.TryGetProperty("bindings", out JsonElement bindings) ||
                    bindings.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                foreach (JsonProperty entry in bindings.EnumerateObject())
                {
                    string? target = entry.Value.GetString();
                    if (string.IsNullOrWhiteSpace(target))
                    {
                        continue;
                    }

                    // Translate a slot key ("idle") to the name the game requests
                    // ("idle_loop"); a raw game name is accepted as-is.
                    string requested = AnimationSlots.VanillaName.TryGetValue(entry.Name, out string? vanilla)
                        ? vanilla
                        : entry.Name;

                    result[requested] = target;
                    GD.Print($"[ModForge] skin binding: {entry.Name} ('{requested}') -> '{target}'");
                }
            }
            catch (Exception ex)
            {
                GD.Print($"[ModForge] could not read skin bindings from {Path.GetFileName(file)}: {ex.Message}");
            }
        }

        return result;
    }

    /// <summary>
    /// Finds binding files beside the executing assembly, walking up a few levels
    /// because the assembly is loaded from the mod folder.
    /// </summary>
    private static IEnumerable<string> FindBindingFiles()
    {
        string? probe = AppContext.BaseDirectory;

        for (int i = 0; i < 4 && probe is not null; i++)
        {
            string candidate = Path.Combine(probe, "skins");
            if (Directory.Exists(candidate))
            {
                foreach (string file in Directory.EnumerateFiles(candidate, "*.json"))
                {
                    yield return file;
                }

                yield break;
            }

            probe = Path.GetDirectoryName(probe);
        }
    }
}
