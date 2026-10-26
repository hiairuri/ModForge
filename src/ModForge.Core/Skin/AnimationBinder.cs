namespace ModForge.Core.Skin;

/// <summary>
/// Chooses a sensible default animation for each gameplay slot.
///
/// Skin packs name their animations freely (idle_loop, idle_breath, relaxed_loopA,
/// Attack, Dead, ...), so binding a slot is a guess that the user then confirms. This
/// helper makes the guess in the right order: an exact match for the preferred name
/// beats a partial one, and a partial match for the preferred name beats an exact
/// match for a merely-related fallback.
/// </summary>
public static class AnimationBinder
{
    /// <summary>
    /// Candidate names for each slot, best first. The first entry is the name the
    /// game itself requests for that moment.
    /// </summary>
    private static readonly Dictionary<string, string[]> SlotPreferences = new(StringComparer.OrdinalIgnoreCase)
    {
        ["idle"] = new[] { "idle_loop", "idle", "loop", "relaxed" },
        ["attack"] = new[] { "attack", "attack1", "attack_2" },
        ["cast"] = new[] { "cast", "caste", "cast1", "skill" },
        ["hurt"] = new[] { "hurt", "hit", "damage" },
        ["die"] = new[] { "die", "death", "dead" },
        ["entry"] = new[] { "entry", "spawn", "intro", "appear" },
    };

    /// <summary>
    /// Picks the best available animation for a slot, or an empty string when the pack
    /// offers nothing plausible.
    ///
    /// Each preference is tried in full before moving to the next, and within a
    /// preference an exact match, then a prefix match, then a word-boundary match is used.
    /// Plain substring matching is deliberately avoided: it maps "entry" onto "sentry",
    /// which would bind a slot to an unrelated animation.
    /// </summary>
    public static string Suggest(IReadOnlyList<string> candidates, string slot)
    {
        if (candidates.Count == 0)
        {
            return string.Empty;
        }

        if (!SlotPreferences.TryGetValue(slot, out string[]? preferences))
        {
            return string.Empty;
        }

        foreach (string want in preferences)
        {
            string? exact = candidates.FirstOrDefault(
                c => c.Equals(want, StringComparison.OrdinalIgnoreCase));
            if (exact is not null)
            {
                return exact;
            }

            // A name starting with the keyword is the next most reliable, e.g. "idle_breath".
            string? prefix = candidates.FirstOrDefault(
                c => c.StartsWith(want, StringComparison.OrdinalIgnoreCase));
            if (prefix is not null)
            {
                return prefix;
            }

            // Otherwise require the keyword to appear as a whole word, so "attack" matches
            // "player_attack_1" but "entry" does not match "sentry".
            string? word = candidates.FirstOrDefault(c => ContainsWord(c, want));
            if (word is not null)
            {
                return word;
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// True when <paramref name="word"/> appears in <paramref name="name"/> bounded by a
    /// separator or the string ends.
    /// </summary>
    private static bool ContainsWord(string name, string word)
    {
        int from = 0;
        while (true)
        {
            int at = name.IndexOf(word, from, StringComparison.OrdinalIgnoreCase);
            if (at < 0)
            {
                return false;
            }

            bool startOk = at == 0 || !char.IsLetterOrDigit(name[at - 1]);
            int after = at + word.Length;
            bool endOk = after >= name.Length || !char.IsLetterOrDigit(name[after]);

            if (startOk && endOk)
            {
                return true;
            }

            from = at + 1;
        }
    }

    /// <summary>Suggests bindings for every standard slot at once.</summary>
    public static Dictionary<string, string> SuggestAll(IReadOnlyList<string> candidates)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (string slot in new[] { "idle", "attack", "cast", "hurt", "die", "entry" })
        {
            string pick = Suggest(candidates, slot);
            if (!string.IsNullOrEmpty(pick))
            {
                result[slot] = pick;
            }
        }

        return result;
    }
}
