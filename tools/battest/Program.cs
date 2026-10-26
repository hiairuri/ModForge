// 检查仓库里的 .bat 脚本能不能被 cmd.exe 正确解析。
//
// 只查一件事，但它足够隐蔽：**批处理文件必须用 CRLF 换行**。
//
// cmd.exe 读以裸 LF 结尾的批处理文件时会解析错乱，而且失败方式一点也不明显：
// 注释行里的词会被当成命令去执行（rem ... the editor 会试图运行 "the"），
// 检查到某处之后剩下的行会被跳过。实测过一次，症状是启动脚本"构建成功"却报
// 「Build reported success but  is missing.」——路径变量全是空的，因为定义它们的
// 那些行根本没被执行。看起来完全不像换行的问题，所以这里用工具盯着。
//
// 为什么容易复发：我们的编辑器与文件工具默认写 LF，把 .bat 改一下就踩回去了。
//
// Usage: battest [仓库根目录]

string? root = args.FirstOrDefault(a => Directory.Exists(a)) ?? FindRepoRoot();

if (root is null)
{
    Console.Error.WriteLine("找不到仓库根目录（没看到 ModForge.slnx）。可以从仓库根目录运行，或把路径作为参数传入。");
    return 1;
}

List<string> scripts = Directory
    .EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
    .Where(f => f.EndsWith(".bat", StringComparison.OrdinalIgnoreCase) ||
                f.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase))
    .Where(f => !IsGenerated(root, f))
    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
    .ToList();

if (scripts.Count == 0)
{
    Console.WriteLine("没有找到 .bat / .cmd 文件。");
    Console.WriteLine();
    Console.WriteLine("RESULT: OK");
    return 0;
}

Console.WriteLine($"仓库根目录: {root}");
Console.WriteLine($"批处理脚本: {scripts.Count} 个");
Console.WriteLine();

var problems = new List<string>();

foreach (string path in scripts)
{
    byte[] bytes = File.ReadAllBytes(path);
    string relative = Path.GetRelativePath(root, path);

    // 逐个字节数换行，不按文本行读：按行读会把换行形式吃掉，那正是要检查的东西。
    int crlf = 0;
    int bareLf = 0;
    int bareCr = 0;

    for (int i = 0; i < bytes.Length; i++)
    {
        if (bytes[i] == (byte)'\r')
        {
            if (i + 1 < bytes.Length && bytes[i + 1] == (byte)'\n')
            {
                crlf++;
                i++;
            }
            else
            {
                bareCr++;
            }
        }
        else if (bytes[i] == (byte)'\n')
        {
            bareLf++;
        }
    }

    var notes = new List<string>();

    if (bareLf > 0)
    {
        notes.Add($"{bareLf} 处裸 LF 换行（cmd.exe 会解析错乱，必须全部改成 CRLF）");
    }

    if (bareCr > 0)
    {
        notes.Add($"{bareCr} 处裸 CR");
    }

    // 批处理文件带 UTF-8 BOM 时，cmd 会把 BOM 当成第一条命令的一部分，
    // 首行报「'?@echo' 不是内部或外部命令」——同样是看着莫名其妙的症状。
    if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
    {
        notes.Add("带 UTF-8 BOM（cmd 会把 BOM 当命令的一部分，首行会报错）");
    }

    if (crlf == 0 && notes.Count == 0)
    {
        notes.Add("没有任何换行");
    }

    if (notes.Count > 0)
    {
        problems.Add(relative);
        Console.WriteLine($"  FAIL {relative}");
        foreach (string note in notes)
        {
            Console.WriteLine($"         {note}");
        }
    }
    else
    {
        Console.WriteLine($"  ok   {relative}  ({crlf} 行，全部 CRLF)");
    }
}

Console.WriteLine();

if (problems.Count > 0)
{
    Console.WriteLine("修法：把文件里的裸 LF 全部替换成 CRLF（编辑器里通常是「存为 CRLF」或改文件末尾换行设置），");
    Console.WriteLine("      然后重新运行本工具确认。只需改换行，别改内容。");
    Console.WriteLine();
    Console.WriteLine("RESULT: FAILED (批处理脚本的换行会破坏 cmd 解析)");
    return 1;
}

Console.WriteLine("RESULT: OK");
return 0;

/// <summary>
/// 从当前目录向上找含 ModForge.slnx 的那一层。
/// </summary>
static string? FindRepoRoot()
{
    string? probe = Directory.GetCurrentDirectory();

    for (int i = 0; i < 8 && probe is not null; i++)
    {
        if (File.Exists(Path.Combine(probe, "ModForge.slnx")))
        {
            return probe;
        }

        probe = Path.GetDirectoryName(probe);
    }

    return null;
}

/// <summary>
/// 生成目录与依赖目录不算：它们不是我们维护的脚本，改了也会被覆盖。
/// </summary>
static bool IsGenerated(string root, string file)
{
    string relative = Path.GetRelativePath(root, file);
    string[] segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    return segments.Any(s => s is "bin" or "obj" or "out" or "decompiled" or "node_modules" or ".git");
}
