using System.Reflection;
using System.Runtime.Loader;

// 离线加载生成的 mod 程序集，逐张卡牌实例化并调用 OnUpgrade。
//
// 为什么需要它：有些错误编译期完全合法、卡面预览也看不出来，只在游戏里某一刻才炸。
// 用户那张牌就是这样的——OnUpgrade 去改一个从未声明的动态变量，于是每次升级都
// KeyNotFoundException，而且是在卡牌网格刷新时抛（卡直接画不出来）。
//
// 静态扫描（newfx2 里那个）能提前抓住"引用未声明变量"，但抓不到方法体里的其它问题
// （取空引用、调错 API、反射来的类型名写错）。这个工具真的把方法跑一遍，是最后一道网。
//
// 用法: modprobe <gameDir> <modDll>

string gameDir = args.Length > 0
    ? args[0]
    : @"D:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2";
string modDll = args.Length > 1
    ? args[1]
    : @"D:\ModForge\build\mods\ArtDemo\ArtDemo.dll";

if (!File.Exists(modDll))
{
    Console.Error.WriteLine($"找不到 mod 程序集：{modDll}");
    return 2;
}

string dataDir = Path.Combine(gameDir, "data_sts2_windows_x86_64");

// 用可回收的 AssemblyLoadContext，并从游戏目录解析依赖（sts2.dll 及 GodotSharp 等）。
var alc = new AssemblyLoadContext("modprobe", isCollectible: true);
alc.Resolving += (ctx, name) =>
{
    string candidate = Path.Combine(dataDir, name.Name + ".dll");
    return File.Exists(candidate) ? ctx.LoadFromAssemblyPath(candidate) : null;
};

Console.WriteLine($"mod : {modDll}");
Console.WriteLine($"game: {gameDir}");
Console.WriteLine();

Assembly mod = alc.LoadFromAssemblyPath(modDll);

// 打开游戏的测试模式。
//
// 升级路径要在**可变**实例上跑才真实（游戏里升级的就是可变副本），而 canonical 实例
// 的 EnergyCost.UpgradeBy 会被 AssertMutable 挡下来——那正是原版最常见的升级方式
// （痛击 -2、全身撞击 -1），不打开的话这条路径一次都测不到，等于网破了个洞。
//
// 游戏自己留了开关（TestMode.TurnOnInternal，注释写明它只该被测试运行器调用）——
// 这个工具就是干那个的：离线加载程序集、把方法跑一遍。游戏本体不在这个进程里，
// 所以打开它不会影响任何正在运行的游戏。
Assembly? gameAssembly = null;

try
{
    gameAssembly = alc.LoadFromAssemblyPath(Path.Combine(dataDir, "sts2.dll"));
    gameAssembly.GetType("MegaCrit.Sts2.Core.TestSupport.TestMode")
        ?.GetMethod("TurnOnInternal", BindingFlags.Public | BindingFlags.Static)
        ?.Invoke(null, null);
}
catch (Exception ex)
{
    Console.WriteLine($"[提示] 没能打开测试模式（{ex.GetType().Name}）：升级路径只能在 canonical 实例上跑。");
}

int cards = 0;
int failures = 0;

foreach (Type cardType in mod.GetTypes()
    .Where(t => t.IsClass && !t.IsAbstract && t.Namespace?.EndsWith(".Cards") == true)
    // 排除编译器为 lambda / 迭代器生成的嵌套类型。
    //
    // 它们与卡牌在同一个命名空间下，但名字形如 <>c、<>c__DisplayClass0_0，既不是
    // CardModel 也不该被实例化。不排除的话，只要某张卡的方法体里用了 lambda——
    // 例如把筛选条件传给 FromCombatPile 的 Where(c => c.IsTransformable)——就会多出
    // 几个"卡牌"，本体调用 CanonicalVars 时抛空引用，把结果误报成失败（实测踩过）。
    .Where(t => !t.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), inherit: false)
                && !t.Name.Contains('<'))
    .OrderBy(t => t.Name, StringComparer.Ordinal))
{
    cards++;
    Console.WriteLine($"== {cardType.Name} ==");

    object? card;

    try
    {
        card = Activator.CreateInstance(cardType);
        Console.WriteLine("   实例化: OK");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"   实例化: 失败 {ex.GetType().Name}: {ex.Message}");
        failures++;
        continue;
    }

    // 枚举 CanonicalVars：游戏加载模型时就会做这一步，变量集不一致会在这里露出来。
    string declared = "(无)";

    try
    {
        PropertyInfo? vars = FindProperty(cardType, "CanonicalVars");
        var names = new List<string>();

        foreach (object v in (System.Collections.IEnumerable)vars!.GetValue(card)!)
        {
            PropertyInfo? nameProp = v.GetType().GetProperty("Name");
            names.Add(nameProp?.GetValue(v)?.ToString() ?? v.GetType().Name);
        }

        declared = names.Count == 0 ? "(无)" : string.Join(", ", names);
        Console.WriteLine($"   CanonicalVars: OK  [{declared}]");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"   CanonicalVars: 失败 {ex.GetType().Name}: {ex.Message}");
        failures++;
        continue;
    }

    // 用户那张牌崩在这里。在**可变**实例上走游戏真正的升级入口
    // （CardModel.UpgradeInternal：等级 +1 → OnUpgrade → 重算计算变量），
    // 而不是只调 OnUpgrade——后者不跑 RecalculateForUpgradeOrEnchant，
    // 而计算变量的名字若写错，正是在那一步抛 KeyNotFoundException。
    //
    // 实例先标成可变：游戏里升级发生在可变副本上，canonical 实例的费用升级
    // （EnergyCost.UpgradeBy）会被 AssertMutable 挡下来——那恰是原版最常见的升级方式
    // （痛击 -2、全身撞击 -1），不这样做这条路径一次都测不到。
    // 升级**到上限**，不是只升一次：多次升级的牌（MaxUpgradeLevel > 1）每一级都可能出错，
    // 只升一次会漏掉后面的等级。游戏读档时也是这样一级一级重放的。
    try
    {
        MethodInfo upgrade = FindMethod(cardType, "UpgradeInternal")
            ?? throw new InvalidOperationException("找不到 UpgradeInternal");

        // 先碰一下 DynamicVars（游戏加载时也会做），变量集有问题就在这里现形。
        _ = FindProperty(cardType, "DynamicVars")?.GetValue(card);

        MethodInfo? setMutable = FindMethod(cardType, "NeverEverCallThisOutsideOfTests_SetIsMutable");
        bool mutable = false;

        if (setMutable is not null)
        {
            setMutable.Invoke(card, new object?[] { true });
            mutable = true;
        }

        try
        {
            int max = FindProperty(cardType, "MaxUpgradeLevel")?.GetValue(card) is int m ? m : 1;

            for (int level = 0; level < Math.Max(1, max); level++)
            {
                upgrade.Invoke(card, null);
            }

            Console.WriteLine(max > 1 ? $"   OnUpgrade: OK（升到 {max} 级）" : "   OnUpgrade: OK");
        }
        finally
        {
            if (mutable)
            {
                setMutable!.Invoke(card, new object?[] { false });
            }
        }
    }
    catch (Exception ex)
    {
        Exception real = Unwrap(ex);
        Console.WriteLine($"   OnUpgrade: 失败 {real.GetType().Name}: {real.Message}");

        if (real is KeyNotFoundException)
        {
            Console.WriteLine("     -> 升级时会崩：改了一个未声明的动态变量。");
        }

        failures++;
    }

    // 计算型的"单位数量"回调也要跑：游戏画卡面时每帧都会调它，抛异常卡面就是空白的。
    foreach (MethodInfo m in cardType
        .GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
        .Where(m => m.ReturnType == typeof(decimal) && m.GetParameters().Length == 2))
    {
        try
        {
            m.Invoke(null, new object?[] { card, null });
            Console.WriteLine($"   {m.Name}(card, null): OK");
        }
        catch (Exception ex)
        {
            Exception real = Unwrap(ex);

            // 这两个异常都表示"需要战斗中带 Owner 的可变实例"，canonical 模型测不了，
            // 不是代码有问题。
            if (real.GetType().Name == "CanonicalModelException" || real is NullReferenceException)
            {
                Console.WriteLine($"   {m.Name}(card, null): 跳过（需要战斗中的可变实例）");
                continue;
            }

            Console.WriteLine($"   {m.Name}(card, null): 失败 {real.GetType().Name}: {real.Message}");
            failures++;
        }
    }
}

Console.WriteLine();

if (cards == 0)
{
    Console.WriteLine("RESULT: FAILED (程序集里没有找到任何卡牌)");
    return 1;
}

if (failures > 0)
{
    Console.WriteLine($"RESULT: FAILED ({cards} 张卡，{failures} 处问题)");
    return 1;
}

Console.WriteLine($"RESULT: OK ({cards} 张卡全部加载并升级通过)");
return 0;

/// <summary>反射调用抛出的异常都包了一层 TargetInvocationException，取出里层的。</summary>
static Exception Unwrap(Exception ex) =>
    ex is TargetInvocationException { InnerException: not null } tie ? tie.InnerException : ex;

/// <summary>沿基类链找方法（OnUpgrade 是覆写，DynamicVars 是继承来的属性）。</summary>
static MethodInfo? FindMethod(Type type, string name)
{
    for (Type? t = type; t is not null; t = t.BaseType)
    {
        MethodInfo? m = t.GetMethod(
            name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        if (m is not null)
        {
            return m;
        }
    }

    return null;
}

/// <summary>沿基类链找属性。</summary>
static PropertyInfo? FindProperty(Type type, string name)
{
    for (Type? t = type; t is not null; t = t.BaseType)
    {
        PropertyInfo? p = t.GetProperty(
            name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        if (p is not null)
        {
            return p;
        }
    }

    return null;
}
