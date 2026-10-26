namespace ModForge.Core.Modules.Files;

/// <summary>读取时发现的一处问题：哪个文件、哪个位置、错在哪。</summary>
/// <param name="File">出问题的文件（完整路径，界面会显示文件名）。</param>
/// <param name="Where">文件内的位置，例如 <c>effects/li[2]/base/count</c>；根元素处为空串。</param>
/// <param name="Message">说得清的一句话：期望什么、实际是什么、怎么改。</param>
/// <param name="Fatal">这条是否让整份文件（乃至整个实体）没能读进来。</param>
public sealed record ReadIssue(string File, string Where, string Message, bool Fatal = false)
{
    /// <summary>给界面与日志用的一行文字。</summary>
    public string Describe() =>
        Fatal
            ? $"{System.IO.Path.GetFileName(File)}: {Message}"
            : $"{System.IO.Path.GetFileName(File)}（{Where}）: {Message}";
}

/// <summary>
/// **读取组件的错误收集器**。
///
/// 工程文件是拿来手工编辑、分享、从别处拷来的东西，所以读取必须"坏一处不影响其它"：
/// 一个字段写错，只让那个字段退回兜底值；一条效果读不动，只丢那一条；一个文件坏掉，
/// 只丢那一个实体。整份工程照常打开，同时把每一处问题**记在这里**，由界面弹出来。
///
/// 这与"读写是同一层"的取舍有关：写出去的东西必须能被读回来，所以格式本身是严格的；
/// 而读进来的东西来自未知的手，所以**读到不认识的东西时宁可降级也不要中断**——
/// 中断的代价是"整个工程打不开"，而降级的代价只是"这一处没生效"，后者还有错误提示兜底。
///
/// 位置用 <see cref="Path"/> 拼：进一层压一个名字，出来时自动弹掉，
/// 所以报出来的永远是从根到那个元素的路径（<c>upgrade/li[1]/base/count</c>）。
/// </summary>
public sealed class ReadLog : IDisposable
{
    private readonly List<ReadIssue> _issues = new();
    private readonly List<string> _path = new();
    private readonly string _file;
    private readonly Action<IReadOnlyList<ReadIssue>>? _collect;

    /// <param name="file">正在读的文件。</param>
    /// <param name="collect">
    /// 收口：这个记录器用完（<see cref="Dispose"/>）时把攒下的问题交给谁。
    /// 读多个文件时用它把每一份的问题汇总到一处，界面就能一次性列全。
    /// </param>
    public ReadLog(string file, Action<IReadOnlyList<ReadIssue>>? collect = null)
    {
        _file = file;
        _collect = collect;
    }

    /// <summary>把这次读到的问题交出去（没给收集回调时什么也不做）。</summary>
    public void Dispose() => _collect?.Invoke(_issues);

    public IReadOnlyList<ReadIssue> Issues => _issues;

    /// <summary>当前文件。</summary>
    public string File => _file;

    /// <summary>当前所在位置（从根到当前元素）。</summary>
    public string Path => string.Join("/", _path);

    /// <summary>记一处问题：这一处没读成，已经用兜底值继续。</summary>
    public void Error(string message) => Add(message, fatal: false);

    /// <summary>记一处致命问题：这个实体（或这份文件）整个没能读进来。</summary>
    public void Fatal(string message) => Add(message, fatal: true);

    private void Add(string message, bool fatal)
    {
        // 同一个位置重复报同一句话没有意义（例如同一个坏值被读两遍）。
        if (_issues.Any(i => i.Where == Path && i.Message == message))
        {
            return;
        }

        _issues.Add(new ReadIssue(_file, Path, message, fatal));
    }

    /// <summary>进一层：<paramref name="name"/> 是元素名，<paramref name="index"/> 是它在同类里的序号（从 1 起）。</summary>
    public IDisposable Scope(string name, int index = 0)
    {
        _path.Add(index > 0 ? $"{name}[{index}]" : name);
        return new Popper(_path);
    }

    private sealed class Popper : IDisposable
    {
        private readonly List<string> _path;

        public Popper(List<string> path) => _path = path;

        public void Dispose()
        {
            if (_path.Count > 0)
            {
                _path.RemoveAt(_path.Count - 1);
            }
        }
    }
}
