using System;
using System.Reflection;

namespace ModForge.Generated.Runtime;

/// <summary>
/// 共享运行时的"宿主 / 转发"判定。
///
/// **要解决的问题**：多个由本编辑器生成的 mod 同时装的时候，"自建计数"（恢复生命的次数与量）
/// 需要**一份**记账——各记各的话，每个 mod 都观察同一批全局事件、各写一份账，数字虽然一致，
/// 但实现、补丁与内存都重复了；而一旦有人改了记账口径，各 mod 就又各算各的。
///
/// **做法（不需要声明依赖）**：每个 mod 的生成物里都带一份完全相同的运行时源码，启动时问一句
/// "该由谁记账"（<see cref="Adopt"/>）：
///
///   判给自己 → 本 mod 是**宿主**：自己装补丁、自己记账、自己算。
///   判给别人 → 本 mod **不记账**，查询转给那一份（见 <see cref="Host"/>）。
///
/// 于是无论装几个 mod，都是一份记账、一个补丁、一份存储。而且**天然容错**：把宿主那个 mod
/// 删掉或禁用，下一次启动剩下的会自动有一个成为宿主——不存在"缺了依赖就整个加载失败"
/// 这种硬失败（这也是不声明 <c>dependencies</c> 的原因）。
///
/// **为什么能靠类型名找到**：游戏的加载器把 mod 程序集都装进同一个
/// <c>AssemblyLoadContext</c>，所以别人的那份也在这个进程里。找的是**类型全名**
/// （每个 mod 的这份代码都一样，所以名字相同），不需要任何编译期引用。
///
/// 只有**带状态**的运行时件才需要共享。无状态的那些（伤害槽、永久增伤/重放的辅助函数）
/// 每 mod 一份完全无害——它们只操作传进来的那张牌；皮肤的动画重映射也是每 mod 一份，
/// 因为它管的是**自己的**皮肤映射。
/// </summary>
public static class SharedRuntime
{
    /// <summary>
    /// 计数实现（记账 + 补丁）的类型全名。
    ///
    /// 用字符串而不是 <c>typeof</c>：要找的是**别人那份**同名类型，
    /// 而 <c>typeof</c> 只会拿到自己这份。
    /// </summary>
    private const string CountersTypeName = "ModForge.Generated.Runtime.HealTracker";

    /// <summary>本 mod 是不是宿主（记账的那一份）。<see cref="Adopt"/> 之前默认为真。</summary>
    public static bool IsHost { get; private set; } = true;

    /// <summary>宿主那份计数实现的类型；自己是宿主时为 null。</summary>
    public static Type? Host { get; private set; }

    /// <summary>
    /// 在启动时问一句"该由谁记账"。**必须在装补丁之前调**——
    /// 不是宿主的话，补丁里的记账要跳过。
    ///
    /// **宿主的选定与加载顺序无关**：把已加载的候选按**程序集名**排序，最小的那个当宿主。
    /// 起初写的是"谁先调 Adopt 谁当宿主"，那样在"两个程序集都已加载"的情形下会互相找到、
    /// **双双变成非宿主**——计数就永远读到 0，而且不报错。按名字定胜负就没有这个失败模式：
    /// 无论谁先初始化，答案都一样（导出时程序集名按工程 id 取唯一名，所以有确定的大小关系）。
    ///
    /// 可重复调用，结果相同。
    /// </summary>
    public static void Adopt()
    {
        Assembly self = typeof(SharedRuntime).Assembly;
        string selfName = self.GetName().Name ?? string.Empty;

        Type? winner = null;
        string winnerName = string.Empty;

        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            // 跳过自己这一份。
            if (assembly == self)
            {
                continue;
            }

            Type? found;
            try
            {
                found = assembly.GetType(CountersTypeName, throwOnError: false);
            }
            catch
            {
                // 某些动态程序集上 GetType 会抛；它们显然不是我们的运行时。
                continue;
            }

            if (found is null)
            {
                continue;
            }

            string name = assembly.GetName().Name ?? string.Empty;

            if (winner is null || string.CompareOrdinal(name, winnerName) < 0)
            {
                winner = found;
                winnerName = name;
            }
        }

        // 没有别人，或者我的名字不比别人大 → 我当宿主。
        if (winner is null || string.CompareOrdinal(selfName, winnerName) <= 0)
        {
            IsHost = true;
            Host = null;
            return;
        }

        IsHost = false;
        Host = winner;
    }
}
