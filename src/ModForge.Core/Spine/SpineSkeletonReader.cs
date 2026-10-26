using System.Text;
using System.Text.RegularExpressions;

namespace ModForge.Core.Spine;

/// <summary>
/// Extracts the names held in a Spine binary skeleton (.skel, or Godot's .spskel).
///
/// The binary stores every name as an inline length-prefixed UTF-8 string, and
/// there is no index that separates animations from slots or attachments. Rather
/// than reverse-engineer the exact section layout (which varies between Spine and
/// exporter versions), this reads maximal ASCII identifier runs and ranks those
/// that look like animation names.
///
/// The result is deliberately a candidate list: the editor shows the candidates and
/// lets the user choose one or type a name by hand, so a missed or extra candidate
/// is never fatal. Verified against real skin packs, where it recovers names such as
/// attack1, death_pop, idle_breath and hurt3.
/// </summary>
public static partial class SpineSkeletonReader
{
    public sealed record Result(
        string Version,
        IReadOnlyList<string> AllNames,
        IReadOnlyList<string> AnimationCandidates,
        string? Error);

    /// <summary>
    /// Names conventionally used for combat animations. Used to rank and flag
    /// candidates, never to hide other names.
    /// </summary>
    private static readonly string[] AnimationHints =
    {
        "idle", "attack", "cast", "hurt", "hit", "die", "death", "dead",
        "entry", "skill", "spawn", "run", "walk", "powerup", "power_up",
        "revive", "loop", "relaxed", "shiv", "victory", "intro", "sleep",
        "special", "ult", "low_health", "win", "spawn", "appear",
    };

    [GeneratedRegex(@"[A-Za-z][A-Za-z0-9_\-]{2,47}")]
    private static partial Regex IdentifierRun();

    /// <summary>
    /// The slots a character skin normally needs. Each maps to a gameplay moment,
    /// which is what a user is really choosing when they bind an animation.
    /// </summary>
    public static IReadOnlyList<SlotInfo> StandardSlots { get; } = new[]
    {
        new SlotInfo("idle", "Idle", "Standing pose, looping."),
        new SlotInfo("attack", "Attack", "Played when the character attacks."),
        new SlotInfo("cast", "Cast", "Played when casting a power or spell."),
        new SlotInfo("hurt", "Hurt", "Played when the character takes damage."),
        new SlotInfo("die", "Death", "Played when the character dies."),
        new SlotInfo("entry", "Entry", "Played when combat begins."),
    };

    /// <summary>
    /// Lower-case names used to recognise the core combat animations, so a candidate
    /// list can be ordered with the common choices first.
    /// </summary>
    public static IReadOnlySet<string> CoreSlotNames { get; } = new HashSet<string>(
        new[] { "idle", "idle_loop", "attack", "cast", "hurt", "hit", "die", "death", "dead", "entry" },
        StringComparer.Ordinal);

    public sealed record SlotInfo(string Key, string Label, string Description);

    public static Result Read(byte[] data)
    {
        if (data is null || data.Length < 32)
        {
            return new Result("unknown", Array.Empty<string>(), Array.Empty<string>(),
                "File is too small to be a Spine skeleton.");
        }

        string version = FindVersion(data) ?? "unknown";

        // Decode as Latin-1 so every byte maps to one char and offsets stay stable;
        // the names being extracted are ASCII identifiers.
        string text = Encoding.Latin1.GetString(data);

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in IdentifierRun().Matches(text))
        {
            string value = TrimToDeclaredLength(data, m.Index, m.Value).TrimEnd('-');

            // Bare numeric-ish tokens and very short fragments are attachment ids.
            if (value.Length < 3 || value.All(char.IsDigit))
            {
                continue;
            }

            names.Add(value);
        }

        var all = names.OrderBy(n => n, StringComparer.Ordinal).ToList();

        // Rank: exact hint matches first, then longer names, so the most animation-like
        // candidates appear at the top of the dropdown.
        var candidates = all
            .Where(LooksLikeAnimationName)
            .OrderByDescending(AnimationRank)
            .ThenBy(n => n, StringComparer.Ordinal)
            .ToList();

        return new Result(version, all, candidates, null);
    }

    /// <summary>
    /// Finds a version string such as "4.2.43" near the start of the file.
    ///
    /// The layout is: an optional 8-byte hash, then the version as a length-prefixed
    /// string. The hash is a file-format detail that varies between exporters, so the
    /// version is located by scanning rather than by assuming a fixed offset.
    /// </summary>
    private static string? FindVersion(byte[] data)
    {
        int limit = Math.Min(data.Length - 2, 72);

        for (int i = 0; i < limit; i++)
        {
            int len = data[i];
            if (len is < 5 or > 12 || i + 1 + len > data.Length)
            {
                continue;
            }

            int start = i + 1;
            string candidate = Encoding.ASCII.GetString(data, start, len);

            // The declared length can include trailing bytes beyond the version text, so
            // only the leading "major.minor.patch" run is considered.
            Match version = VersionRegex().Match(candidate);
            if (version.Success)
            {
                return version.Value;
            }
        }

        return null;
    }

    /// <summary>Matches a leading "4.2.43"-style version.</summary>
    [GeneratedRegex(@"^\d+(?:\.\d+)+")]
    private static partial Regex VersionRegex();

    /// <summary>
    /// Truncates a candidate to its true length using Spine's length prefix.
    ///
    /// Names are stored as a single length byte followed by that many bytes. Because the
    /// byte after a name is often also printable, a plain character scan can run past the
    /// end (turning "idle_loop" into "idle_loopk"), and a binding to a wrong name silently
    /// does nothing. When the preceding byte declares a shorter length, it is trusted.
    /// </summary>
    private static string TrimToDeclaredLength(byte[] data, int start, string candidate)
    {
        if (start <= 0)
        {
            return candidate;
        }

        int declared = data[start - 1];

        // A plausible declaration is shorter than the run and still yields a real name.
        if (declared >= 3 && declared < candidate.Length)
        {
            return candidate[..declared];
        }

        return candidate;
    }

    private static bool LooksLikeAnimationName(string s)
    {
        string lower = s.ToLowerInvariant();
        return AnimationHints.Any(h => lower.Contains(h, StringComparison.Ordinal));
    }

    private static int AnimationRank(string s)
    {
        string lower = s.ToLowerInvariant();
        int best = 0;

        for (int i = 0; i < AnimationHints.Length; i++)
        {
            if (!lower.Contains(AnimationHints[i], StringComparison.Ordinal))
            {
                continue;
            }

            // An exact match outranks a substring match.
            int score = lower == AnimationHints[i] ? 1000 : AnimationHints.Length - i;

            // Prefer shorter names: "attack" over "attack_particles_start".
            score -= lower.Length / 8;
            best = Math.Max(best, score);
        }

        return best;
    }
}
