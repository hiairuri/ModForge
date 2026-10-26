using System.Text.RegularExpressions;

namespace ModForge.Core;

/// <summary>
/// Reproduces the game's identifier derivation.
///
/// A model's id is "CATEGORY.ENTRY" where ENTRY comes from the type name via
/// <see cref="Slugify"/> and CATEGORY is derived from the direct base type below
/// AbstractModel (e.g. CardModel -> "CARD", PowerModel -> "POWER").
///
/// This matters because a model's localization keys are built from the entry
/// alone ("&lt;ENTRY&gt;.title"), and two models resolving to the same id make the
/// game throw DuplicateModelException at startup.
/// </summary>
public static partial class ModelIds
{
    [GeneratedRegex(@"([A-Za-z0-9]|\G(?!^))([A-Z])")]
    private static partial Regex CamelCaseRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"[^A-Z0-9_]")]
    private static partial Regex SpecialCharRegex();

    public static string Slugify(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        // Insert an underscore before interior capitals, then upper-case.
        string separated = CamelCaseRegex().Replace(text.Trim(), "$1_$2");
        string upper = WhitespaceRegex().Replace(separated.ToUpperInvariant(), "_");
        return SpecialCharRegex().Replace(upper, string.Empty);
    }

    /// <summary>
    /// Derives the category name from the model type's ancestors, matching
    /// ModelDb.GetCategory: walk up to the direct child of AbstractModel and drop
    /// a trailing "_MODEL".
    /// </summary>
    public static string SlugifyCategory(string categoryName)
    {
        string slug = Slugify(categoryName);
        return slug.EndsWith("_MODEL", StringComparison.Ordinal)
            ? slug[..^"_MODEL".Length]
            : slug;
    }
}
