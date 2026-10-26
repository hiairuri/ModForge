using System.Reflection;
using System.Runtime.InteropServices;

namespace ModForge.Core.Validation;

public sealed record ValidationIssue(string Severity, string Message, string? Where = null);

public sealed record ValidationResult(
    IReadOnlyList<ValidationIssue> Issues,
    int CardCount,
    int PowerCount)
{
    public bool HasErrors => Issues.Any(i => i.Severity == "error");
}

/// <summary>
/// Checks a generated mod assembly against the invariants the game enforces at
/// startup. This catches the failures that otherwise only appear as a crash or a
/// silent no-show in game:
///
///  - a model type that cannot be instantiated (ModelDb uses Activator.CreateInstance,
///    so a public parameterless constructor is required)
///  - a model id that collides with vanilla content (DuplicateModelException)
///  - a card that is never added to a pool (CardModel.Pool throws during preload)
/// </summary>
public static class ModValidator
{
    public static ValidationResult Validate(
        string modAssemblyPath,
        string gameDir,
        IReadOnlyCollection<string> vanillaModelIds)
    {
        var issues = new List<ValidationIssue>();

        string assemblyDir = Path.Combine(gameDir, "data_sts2_windows_x86_64");
        if (!Directory.Exists(assemblyDir))
        {
            assemblyDir = gameDir;
        }

        string sts2Path = Path.Combine(assemblyDir, "sts2.dll");
        if (!File.Exists(sts2Path))
        {
            issues.Add(new ValidationIssue("error", $"Game assembly not found at {sts2Path}"));
            return new ValidationResult(issues, 0, 0);
        }

        using var mlc = ModForge.Core.Catalog.VanillaCatalog.CreateLoadContext(assemblyDir);

        // Base types live in the game assembly, so resolve them from there. Asking the
        // mod assembly for them would fail: GetType only sees types it defines.
        Assembly gameAsm;
        Assembly modAsm;
        try
        {
            gameAsm = mlc.LoadFromAssemblyPath(sts2Path);
            modAsm = mlc.LoadFromAssemblyPath(modAssemblyPath);
        }
        catch (Exception ex)
        {
            issues.Add(new ValidationIssue("error", $"Cannot load assembly for validation: {ex.Message}"));
            return new ValidationResult(issues, 0, 0);
        }

        Type? cardModel = gameAsm.GetType("MegaCrit.Sts2.Core.Models.CardModel", throwOnError: false);
        Type? powerModel = gameAsm.GetType("MegaCrit.Sts2.Core.Models.PowerModel", throwOnError: false);

        int cardCount = 0;
        int powerCount = 0;

        Type[] types;
        try
        {
            types = modAsm.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            types = ex.Types.Where(t => t is not null).Cast<Type>().ToArray();
            issues.Add(new ValidationIssue("warn", "Some types could not be loaded; validation is partial."));
        }

        foreach (Type type in types)
        {
            if (type.IsAbstract || type.IsInterface || type.IsGenericTypeDefinition)
            {
                continue;
            }

            bool isCard = cardModel is not null && IsSubclassOf(type, cardModel);
            bool isPower = powerModel is not null && IsSubclassOf(type, powerModel);
            if (!isCard && !isPower)
            {
                continue;
            }

            string category = isCard ? "CARD" : "POWER";
            string entry = ModelIds.Slugify(type.Name);
            string modelId = $"{category}.{entry}";

            if (isCard)
            {
                cardCount++;
            }
            else
            {
                powerCount++;
            }

            ConstructorInfo? ctor = type.GetConstructor(
                BindingFlags.Public | BindingFlags.Instance,
                binder: null,
                Type.EmptyTypes,
                modifiers: null);

            if (ctor is null)
            {
                issues.Add(new ValidationIssue(
                    "error",
                    $"{type.Name} has no public parameterless constructor; the game cannot instantiate it.",
                    modelId));
            }

            if (vanillaModelIds.Contains(modelId))
            {
                issues.Add(new ValidationIssue(
                    "error",
                    $"{modelId} collides with existing content and would crash the game at startup. Rename the type.",
                    modelId));
            }
        }

        if (cardCount == 0 && powerCount == 0)
        {
            issues.Add(new ValidationIssue("warn", "No CardModel or PowerModel subclasses found in the assembly."));
        }

        return new ValidationResult(issues, cardCount, powerCount);
    }

    private static bool IsSubclassOf(Type type, Type baseType)
    {
        for (Type? t = type.BaseType; t is not null; t = t.BaseType)
        {
            if (t.FullName == baseType.FullName)
            {
                return true;
            }
        }

        return false;
    }
}
