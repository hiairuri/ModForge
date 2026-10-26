using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ModForge.Core.Compile;

public sealed record CompileDiagnostic(string Severity, string Id, string Message, string? File, int Line);

public sealed record CompileResult(bool Success, byte[]? Assembly, IReadOnlyList<CompileDiagnostic> Diagnostics)
{
    public IEnumerable<CompileDiagnostic> Errors => Diagnostics.Where(d => d.Severity == "error");
}

/// <summary>
/// Compiles generated C# in-process with Roslyn.
///
/// The game is a .NET 9 Godot build, so the compilation targets net9.0 and
/// references the game's own assemblies. Those references are never copied next to
/// the mod; the game already provides them at runtime.
/// </summary>
public static class ModCompiler
{
    /// <summary>默认程序集名（工具与测试用；导出时按工程 id 取唯一名）。</summary>
    public const string DefaultAssemblyName = "ModForgeGenerated";

    /// <param name="assemblyName">
    /// 产物程序集名。不传时用 <see cref="DefaultAssemblyName"/>。
    ///
    /// 导出时按工程 id 取唯一名（<c>ModForge.&lt;id&gt;</c>），因为多个 mod 会被装进**同一个**
    /// <c>AssemblyLoadContext</c>：名字相同会造成按名解析的歧义（而"共享运行时的宿主判定"
    /// 正是按类型名去找另一份实现的）。
    /// </param>
    public static CompileResult Compile(
        IReadOnlyDictionary<string, string> sources,
        string gameDir,
        string? assemblyName = null)
    {
        string assemblyDir = Path.Combine(gameDir, "data_sts2_windows_x86_64");
        if (!Directory.Exists(assemblyDir))
        {
            assemblyDir = gameDir;
        }

        var trees = sources
            .Select(kv => CSharpSyntaxTree.ParseText(
                kv.Value,
                new CSharpParseOptions(LanguageVersion.Latest),
                path: kv.Key))
            .ToList();

        var references = new List<MetadataReference>();

        // The game is self-contained, so its own runtime folder supplies both the
        // framework and the game assemblies the generated code targets.
        foreach (string dll in EnumerateReferences(assemblyDir))
        {
            references.Add(MetadataReference.CreateFromFile(dll));
        }

        var options = new CSharpCompilationOptions(
            OutputKind.DynamicallyLinkedLibrary,
            optimizationLevel: OptimizationLevel.Release,
            allowUnsafe: false,
            nullableContextOptions: NullableContextOptions.Enable);

        var compilation = CSharpCompilation.Create(
            assemblyName ?? DefaultAssemblyName,
            trees,
            references,
            options);

        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);

        var diagnostics = emit.Diagnostics
            .Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)
            .Where(d => !IsBenign(d))
            .Select(ToDiagnostic)
            .ToList();

        return emit.Success
            ? new CompileResult(true, stream.ToArray(), diagnostics)
            : new CompileResult(false, null, diagnostics);
    }

    /// <summary>
    /// Reference assemblies to compile against. Prefers the core framework and the
    /// game's own assemblies; unrelated native shims are skipped.
    /// </summary>
    private static IEnumerable<string> EnumerateReferences(string assemblyDir)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // The runtime pack from the installed SDK matches net9.0 and is stable.
        string? refPack = FindReferencePack();
        if (refPack is not null)
        {
            foreach (string dll in Directory.GetFiles(refPack, "*.dll"))
            {
                if (seen.Add(Path.GetFileName(dll)))
                {
                    yield return dll;
                }
            }
        }

        // Game and support assemblies (sts2, GodotSharp, 0Harmony, Steamworks, ...).
        foreach (string dll in Directory.GetFiles(assemblyDir, "*.dll"))
        {
            string name = Path.GetFileName(dll);

            // The game folder holds a whole runtime too; the ref pack already covers it.
            if (name.StartsWith("System.", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("mscorlib.dll", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("netstandard.dll", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Skip native shims (coreclr, hostfxr, steam_api64, ...): they carry no
            // managed metadata and Roslyn rejects them as references.
            if (!IsManagedAssembly(dll))
            {
                continue;
            }

            if (seen.Add(name))
            {
                yield return dll;
            }
        }
    }

    /// <summary>
    /// Returns true when the file is a .NET assembly rather than a native library.
    /// Managed PE files carry a CLI header in the data-directory table.
    /// </summary>
    private static bool IsManagedAssembly(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream);

            if (stream.Length < 0x40 || reader.ReadUInt16() != 0x5A4D)
            {
                return false; // not MZ
            }

            stream.Position = 0x3C;
            int peOffset = reader.ReadInt32();
            if (peOffset <= 0 || peOffset + 0x18 > stream.Length)
            {
                return false;
            }

            stream.Position = peOffset;
            if (reader.ReadUInt32() != 0x00004550) // "PE\0\0"
            {
                return false;
            }

            stream.Position = peOffset + 0x18; // optional header start
            ushort magic = reader.ReadUInt16();

            // Data directory offset differs between PE32 and PE32+; the CLI header
            // is entry 14 in both.
            int dataDirOffset = magic == 0x20B ? peOffset + 0x18 + 0x70 : peOffset + 0x18 + 0x60;
            if (dataDirOffset + (14 * 8) + 8 > stream.Length)
            {
                return false;
            }

            stream.Position = dataDirOffset + (14 * 8);
            return reader.ReadUInt32() != 0;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static string? FindReferencePack()
    {
        string dotnetRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "dotnet",
            "packs",
            "Microsoft.NETCore.App.Ref");

        if (!Directory.Exists(dotnetRoot))
        {
            return null;
        }

        // Highest installed version wins.
        return Directory.GetDirectories(dotnetRoot)
            .Select(d => (Dir: d, Name: Path.GetFileName(d)))
            .Where(x => Version.TryParse(x.Name, out _))
            .OrderByDescending(x => Version.Parse(x.Name))
            .Select(x => Path.Combine(x.Dir, "ref", "net9.0"))
            .FirstOrDefault(Directory.Exists);
    }

    /// <summary>
    /// Filters warnings that are expected here and would otherwise bury real problems.
    /// CS1701 is raised because the game's helper assemblies (GodotSharp, 0Harmony)
    /// were built against .NET 8 while the game itself runs .NET 9; the loader
    /// resolves these at runtime, and every released mod ships the same shape.
    /// </summary>
    private static bool IsBenign(Diagnostic d) => d.Id is "CS1701" or "CS1702";

    private static CompileDiagnostic ToDiagnostic(Diagnostic d)
    {
        FileLinePositionSpan span = d.Location.GetLineSpan();
        return new CompileDiagnostic(
            d.Severity == DiagnosticSeverity.Error ? "error" : "warning",
            d.Id,
            d.GetMessage(),
            d.Location.SourceTree?.FilePath,
            span.StartLinePosition.Line + 1);
    }
}
