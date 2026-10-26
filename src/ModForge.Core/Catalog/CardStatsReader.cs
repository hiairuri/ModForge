using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace ModForge.Core.Catalog;

/// <summary>
/// 从游戏程序集 <c>sts2.dll</c> 里读出每张原版卡牌的数值。
///
/// 只能读字节码，原因：卡牌的费用/类型/数值是编译进程序集的常量，本地化表里只有
/// <c>{Damage:diff()}</c> 这类占位符。参考别的做法（SlaySP2Manger 用 Rust 解析同一套 IL）
/// 与反编译结果，卡牌的定义方式高度统一，所以可以按固定模式取值：
///
///   构造函数     base(cost, type, rarity, target, showInLibrary) —— 前面 5 个 ldc.i4 就是参数
///   CanonicalVars  newobj DamageVar(6m, …) / BlockVar(5m, …) —— 取构造参数里的数字
///   OnUpgrade    DynamicVars["Damage"].UpgradeValueBy(3m) —— 记成增量
///   CanonicalKeywords  静态数组或逐项 AddKeyword
///
/// 反过来，凡是用 <c>CalculatedDamageVar</c>（"等于当前格挡的伤害"）这类运行时计算的卡，
/// 数值在字节码里没有常量，只能标记为"计算型"——界面据此如实说明，而不是编一个数字出来。
/// </summary>
public sealed class CardStatsReader
{
    /// <summary>卡牌所在的命名空间。</summary>
    private const string CardNamespace = "MegaCrit.Sts2.Core.Models.Cards";

    private const string CardBaseType = "CardModel";

    private readonly MetadataReader _reader;
    private readonly PEReader _pe;

    /// <summary>静态字段值的缓存（解析成功的才记进来）。</summary>
    private readonly Dictionary<int, double> _staticFieldCache = new();

    /// <summary>正在解析中的静态字段，用于打断自引用。</summary>
    private readonly HashSet<int> _staticFieldInProgress = new();

    private readonly byte[] _image;

    private CardStatsReader(PEReader pe, MetadataReader reader, byte[] image)
    {
        _pe = pe;
        _reader = reader;
        _image = image;
    }

    /// <summary>读游戏安装目录下的卡牌数据。</summary>
    public static CardStatsTable Read(string gameDir)
    {
        string dll = Path.Combine(gameDir, "data_sts2_windows_x86_64", "sts2.dll");

        if (!File.Exists(dll))
        {
            throw new FileNotFoundException($"找不到游戏程序集：{dll}");
        }

        byte[] image = File.ReadAllBytes(dll);

        using var pe = new PEReader(new MemoryStream(image, writable: false));

        if (!pe.HasMetadata)
        {
            throw new InvalidOperationException("游戏程序集里没有托管元数据。");
        }

        MetadataReader reader = pe.GetMetadataReader();

        // 连镜像一起带上：字段的常量数据（静态数组、被提升的小数常量）要按 RVA 从原始
        // 字节里读，而 PEReader 拿不到那部分。
        var self = new CardStatsReader(pe, reader, image);
        Dictionary<string, CardStats> cards = self.ReadCards();

        GameVersion version = GameVersion.Read(gameDir);

        // 卡图路径是牌池的唯一现成来源（卡池类里的清单需要另外解析，收益不大）。
        self.AssignArt(cards, gameDir);

        return new CardStatsTable
        {
            GameVersion = version.Version,
            Commit = version.Commit,
            ByEntry = cards,
        };
    }

    /// <summary>遍历所有卡牌类型并逐个解析。</summary>
    private Dictionary<string, CardStats> ReadCards()
    {
        var result = new Dictionary<string, CardStats>(StringComparer.Ordinal);

        foreach (TypeDefinitionHandle handle in _reader.TypeDefinitions)
        {
            TypeDefinition type = _reader.GetTypeDefinition(handle);

            if (!string.Equals(Namespace(type), CardNamespace, StringComparison.Ordinal))
            {
                continue;
            }

            string className = Name(type);

            // 跳过基类、Mock 与已废弃的占位卡。
            if (className == CardBaseType || className.StartsWith("Mock", StringComparison.Ordinal))
            {
                continue;
            }

            if (!DerivesFromCardModel(type))
            {
                continue;
            }

            CardStats? stats = ReadCard(type, className);

            if (stats is not null)
            {
                result[stats.Entry] = stats;
            }
        }

        return result;
    }

    /// <summary>解析一张卡。</summary>
    private CardStats? ReadCard(TypeDefinition type, string className)
    {
        MethodDefinition? ctor = FindParameterlessCtor(type);

        if (ctor is null)
        {
            return null;
        }

        BaseCtorCall? baseCall = ReadBaseCtorCall(ctor.Value);

        if (baseCall is null)
        {
            // 构造函数里找不到对 CardModel::.ctor 的调用就没法判断费用与类型，跳过。
            return null;
        }

        var vars = new List<CardStatVar>();
        bool computed = false;

        foreach (MethodDefinitionHandle method in type.GetMethods())
        {
            MethodDefinition definition = _reader.GetMethodDefinition(method);
            string name = _reader.GetString(definition.Name);

            if (name == "get_CanonicalVars")
            {
                (List<CardStatVar> parsed, bool hasComputed) = ReadVars(definition);
                vars.AddRange(parsed);
                computed |= hasComputed;
            }
        }

        IReadOnlyList<string> keywords = ReadKeywords(type);
        CardStatUpgrade upgrade = ReadUpgrade(type, vars, keywords);

        bool xCost = OverridesTrue(type, "get_HasEnergyCostX");

        return new CardStats
        {
            Entry = PascalToSnake(className).ToUpperInvariant(),
            ClassName = className,
            EnergyCost = baseCall.Energy,
            EnergyCostX = xCost,
            CardType = CardTypeName(baseCall.CardType),
            Rarity = RarityName(baseCall.Rarity),
            Target = TargetName(baseCall.Target),
            Upgradable = baseCall.Upgradable,
            Vars = vars,
            Keywords = keywords,
            Upgrade = upgrade,
            HasComputedValues = computed,
        };
    }

    // ---------------------------------------------------------------- 构造函数

    private sealed record BaseCtorCall(int Energy, int CardType, int Rarity, int Target, bool Upgradable);

    /// <summary>
    /// 读 <c>base(cost, type, rarity, target, showInLibrary)</c> 的 5 个参数。
    ///
    /// 这 5 个值在 IL 里是紧挨着的 ldc.i4 序列，随后才是 call，所以直接取调用前的
    /// 5 个常量即可，不必完整模拟求值栈。
    /// </summary>
    private BaseCtorCall? ReadBaseCtorCall(MethodDefinition ctor)
    {
        if (ctor.RelativeVirtualAddress == 0)
        {
            return null;
        }

        IReadOnlyList<IlInstruction> instructions = IlReader.Read(_pe, ctor);
        var pending = new List<int>();

        foreach (IlInstruction instruction in instructions)
        {
            if (instruction.OpCode == IlOpCode.LdcI4 && instruction.Int32 is int value)
            {
                pending.Add(value);
                continue;
            }

            if (instruction.OpCode == IlOpCode.Call)
            {
                // CardModel::.ctor 就是要找的那个调用；它的 5 个参数紧随其后。
                if (instruction.MethodName == ".ctor" && instruction.DeclaringTypeName == CardBaseType)
                {
                    // 第一个参数是 this，所以取最后 5 个常量。
                    if (pending.Count >= 5)
                    {
                        int start = pending.Count - 5;

                        return new BaseCtorCall(
                            pending[start],
                            pending[start + 1],
                            pending[start + 2],
                            pending[start + 3],
                            pending[start + 4] != 0);
                    }

                    return null;
                }

                pending.Clear();
                continue;
            }

            // 其它指令不影响这 5 个参数的取值，但 `newobj`（构造 Decimal 之类）会消耗数字，
            // 所以只保留纯常量序列。
            if (instruction.OpCode is IlOpCode.NewObj)
            {
                pending.Clear();
            }
        }

        return null;
    }

    // ---------------------------------------------------------------- 数值

    /// <summary>
    /// 读 CanonicalVars 里的数值。
    ///
    /// 卡牌的数值写成 <c>new DamageVar(6m, ValueProp.Move)</c>，编译后是
    /// <c>ldc.i4.6</c> + <c>newobj Decimal::.ctor(int32)</c> + <c>ldc.i4.8</c>(ValueProp) +
    /// <c>newobj DamageVar</c>。
    ///
    /// 关键在于要真的维护一个求值栈：数组元素是"数组、下标、值"三段压栈再 stelem，所以
    /// 下标（0/1/…）也会出现在栈上。不做栈计算就会把下标或 ValueProp 当成数值——之前
    /// "打击显示 8 点伤害"就是把 ValueProp.Move(=8) 当成了伤害。
    /// </summary>
    /// <summary>
    /// 读 CanonicalVars 里的数值。
    ///
    /// 卡牌的数值写成 <c>new DamageVar(6m, ValueProp.Move)</c>，编译后是
    /// <c>ldc.i4.6</c> + <c>newobj Decimal::.ctor(int32)</c> + <c>ldc.i4.8</c>(ValueProp) +
    /// <c>newobj DamageVar</c>。
    ///
    /// 这里维护一个求值栈，因为数组元素是"数组、下标、值"三段压栈再 stelem——下标（0/1/…）
    /// 同样是小整数，不区分就会把下标或 ValueProp 当成数值（"打击显示 8 点伤害"就是这么来的）。
    /// </summary>
    private (List<CardStatVar> Vars, bool HasComputed) ReadVars(MethodDefinition method)
    {
        var vars = new List<CardStatVar>();
        bool computed = false;

        if (method.RelativeVirtualAddress == 0)
        {
            return (vars, computed);
        }

        IReadOnlyList<IlInstruction> instructions = IlReader.Read(_pe, method);

        // 求值栈：只记录"是数字/字符串"的值，对象引用记 null（仅用于占位对消）。
        var stack = new List<IlInstruction?>();
        string? lastString = null;

        foreach (IlInstruction instruction in instructions)
        {
            switch (instruction.OpCode)
            {
                case IlOpCode.LdcI4:
                case IlOpCode.LdcR8:
                case IlOpCode.LdStr:
                    stack.Add(instruction);

                    if (instruction.OpCode == IlOpCode.LdStr && !string.IsNullOrEmpty(instruction.Text))
                    {
                        lastString = instruction.Text;
                    }

                    continue;

                case IlOpCode.Ldsfld:
                    // 静态字段也可能是数值来源，放进栈里由 FirstNumber 解析。
                    stack.Add(instruction);
                    continue;

                case IlOpCode.NewObj:
                {
                    List<IlInstruction?> args = Pop(stack, instruction.ParameterCount);
                    string owner = instruction.DeclaringTypeName ?? "";

                    // Decimal 只是把整数包成小数：它的参数就是外层构造函数要的数值，
                    // 所以把它自己作为占位继续留在栈上，让外层取到那个数字。
                    if (owner == "Decimal")
                    {
                        stack.Add(args.Count > 0 ? args[0] : null);
                        continue;
                    }

                    if (owner is "CalculatedDamageVar" or "CalculatedBlockVar" or "CalculatedVar")
                    {
                        computed = true;
                        stack.Add(null);
                        continue;
                    }

                    // 泛型变量（PowerVar<StrengthPower>）也是数值变量。用"有泛型实参"兜底，
                    // 因为泛型实例的声明类型是 TypeSpecification，类型名解析更绕。
                    // 但要排除编译器生成的集合容器（<>z__ReadOnlyArray<DynamicVar> 等），
                    // 它们只是把数值装起来，不是卡牌声明的数值。
                    bool isVar = IsVarType(owner) ||
                                 (instruction.GenericArgument is not null &&
                                  instruction.ParameterCount <= 2 &&
                                  !IsCompilerHelper(owner));

                    if (isVar)
                    {
                        double? value = FirstNumber(args);
                        string kind = KindFromTypeName(owner);
                        string key = instruction.GenericArgument ?? lastString ?? kind;

                        if (vars.All(v => !(v.Kind == kind && v.Key == key)))
                        {
                            vars.Add(new CardStatVar(kind, key, value));
                        }

                        lastString = null;
                    }

                    stack.Add(null);
                    continue;
                }

                case IlOpCode.Call:
                case IlOpCode.CallVirt:
                    Pop(stack, instruction.ParameterCount);
                    stack.Add(null);
                    continue;

                case IlOpCode.NewArr:
                    Pop(stack, 1);
                    stack.Add(null);
                    continue;

                case IlOpCode.Stelem:
                    // 数组、下标、值。
                    Pop(stack, 3);
                    continue;

                case IlOpCode.Ret:
                    stack.Clear();
                    continue;

                default:
                    continue;
            }
        }

        return (vars, computed);
    }

    /// <summary>弹掉若干参数并返回（保持声明顺序）。</summary>
    private static List<IlInstruction?> Pop(List<IlInstruction?> stack, int count)
    {
        var taken = new List<IlInstruction?>();

        if (count <= 0 || stack.Count == 0)
        {
            return taken;
        }

        int take = Math.Min(count, stack.Count);
        int start = stack.Count - take;

        for (int i = start; i < stack.Count; i++)
        {
            taken.Add(stack[i]);
        }

        stack.RemoveRange(start, take);
        return taken;
    }

    /// <summary>取参数序列里的第一个数值（字面量或静态字段）。</summary>
    private double? FirstNumber(IReadOnlyList<IlInstruction?> arguments)
    {
        foreach (IlInstruction? instruction in arguments)
        {
            if (instruction is null)
            {
                continue;
            }

            if (instruction.Value.OpCode == IlOpCode.LdcI4 && instruction.Value.Int32 is int i)
            {
                return i;
            }

            if (instruction.Value.OpCode == IlOpCode.LdcR8 && instruction.Value.Number is double d)
            {
                return d;
            }

            if (instruction.Value.OpCode == IlOpCode.Ldsfld && instruction.Value.Token is int token)
            {
                return ResolveStaticFieldValue(token);
            }
        }

        return null;
    }

    /// <summary>
    /// 静态字段值的缓存。"进行中"与"解析失败"用两个集合分开记，不能都塞进值缓存：
    /// 同一个字段在一次解析里可能被问到两次，若把"进行中"当成结果返回，就会把本来能解出的
    /// 值永久记成失败。
    /// </summary>
    private double? ResolveStaticFieldValue(int fieldToken)
    {
        if (_staticFieldCache.TryGetValue(fieldToken, out double cached))
        {
            return cached;
        }

        if (_staticFieldInProgress.Contains(fieldToken))
        {
            // 自引用（字段初始值就是它自己），没有常量可取。
            return null;
        }

        _staticFieldInProgress.Add(fieldToken);

        try
        {
            double? value = TryResolveStaticFieldValue(fieldToken);

            if (value is not null)
            {
                _staticFieldCache[fieldToken] = value.Value;
            }

            return value;
        }
        finally
        {
            _staticFieldInProgress.Remove(fieldToken);
        }
    }

    /// <summary>
    /// 从字段的初始数据（RVA）里读常量值。
    ///
    /// 编译器把常量数据直接放在字段的数据区，所以按 RVA 读出字节即可。字段类型可能是
    /// <c>decimal</c>（16 字节）或整数（4/8 字节），这里按"先试小数、不行再当整数"处理：
    /// decimal 的 flags 字段有严格取值范围，可以据此判别，避免把整数误读成小数。
    /// </summary>
    private double? ReadFieldConstant(FieldDefinition field)
    {
        int rva = field.GetRelativeVirtualAddress();

        if (rva == 0)
        {
            return null;
        }

        byte[] data;

        try
        {
            data = IlReader.ReadFieldData(_pe, _image, rva, 16);
        }
        catch (BadImageFormatException)
        {
            return null;
        }

        if (data.Length >= 16)
        {
            // decimal 布局：lo, mid, hi（各 4 字节小端）+ flags（含小数位数与符号）。
            int lo = BitConverter.ToInt32(data, 0);
            int mid = BitConverter.ToInt32(data, 4);
            int hi = BitConverter.ToInt32(data, 8);
            int flags = BitConverter.ToInt32(data, 12);

            // flags 里只允许出现符号位与 0..28 的小数位数，其它位必须为 0。
            bool validFlags = (flags & 0x7F00FFFF) == 0 && ((flags >> 16) & 0xFF) <= 28;

            if (validFlags)
            {
                try
                {
                    return (double)new decimal(lo, mid, hi, (flags & unchecked((int)0x80000000)) != 0, (byte)((flags >> 16) & 0xFF));
                }
                catch (ArgumentException)
                {
                    // 位数非法，当作不是小数。
                }
            }
        }

        if (data.Length >= 4)
        {
            return BitConverter.ToInt32(data, 0);
        }

        return null;
    }

    /// <summary>
    /// 把字段引用（MemberRef）解析成真正的字段定义。
    ///
    /// 做法是按"类型名 + 字段名"在本程序集里找。这够用，因为编译器生成的常量容器
    /// （<c>&lt;PrivateImplementationDetails&gt;</c>）就在同一个程序集里。
    /// 找不到返回 null，调用方按"取不到值"处理。
    /// </summary>
    private FieldDefinition? ResolveFieldReference(MemberReferenceHandle handle)
    {
        MemberReference reference = _reader.GetMemberReference(handle);
        string fieldName = _reader.GetString(reference.Name);

        // 引用的是哪个类型（可能是 TypeReference，也可能是泛型实例 TypeSpecification）。
        string? typeName = reference.Parent.Kind switch
        {
            HandleKind.TypeReference => _reader.GetString(
                _reader.GetTypeReference((TypeReferenceHandle)reference.Parent).Name),
            HandleKind.TypeDefinition => _reader.GetString(
                _reader.GetTypeDefinition((TypeDefinitionHandle)reference.Parent).Name),
            _ => null,
        };

        if (Environment.GetEnvironmentVariable("MODFORGE_REF_DEBUG") == "1")
        {
            Console.WriteLine($"        memberref field='{fieldName}' parent={reference.Parent.Kind} type='{typeName}'");
        }

        if (typeName is null)
        {
            return null;
        }

        foreach (TypeDefinitionHandle typeHandle in _reader.TypeDefinitions)
        {
            TypeDefinition type = _reader.GetTypeDefinition(typeHandle);

            if (_reader.GetString(type.Name) != typeName)
            {
                continue;
            }

            foreach (FieldDefinitionHandle fieldHandle in type.GetFields())
            {
                FieldDefinition candidate = _reader.GetFieldDefinition(fieldHandle);

                if (_reader.GetString(candidate.Name) == fieldName)
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// 解析静态字段的值：优先读字段的初始数据，其次去静态构造函数里找赋值语句。
    /// </summary>
    private double? TryResolveStaticFieldValue(int fieldToken)
    {
        var handle = System.Reflection.Metadata.Ecma335.MetadataTokens.EntityHandle(fieldToken);

        // 先处理框架里的小数常量：升级增量常写成 1m / 0m，编译后是
        // ldsfld System.Decimal::One / ::Zero，字段定义不在本程序集里。
        if (handle.Kind == HandleKind.MemberReference)
        {
            MemberReference reference = _reader.GetMemberReference((MemberReferenceHandle)handle);
            string name = _reader.GetString(reference.Name);

            string? ownerName = reference.Parent.Kind == HandleKind.TypeReference
                ? _reader.GetString(_reader.GetTypeReference((TypeReferenceHandle)reference.Parent).Name)
                : null;

            if (ownerName == "Decimal" && DecimalConstant(name) is double known)
            {
                return known;
            }
        }

        // 字段引用有两种形态：
        //   FieldDefinition —— 字段就定义在当前程序集里
        //   MemberReference —— 引用别的类型里的字段
        FieldDefinition field;

        if (handle.Kind == HandleKind.FieldDefinition)
        {
            field = _reader.GetFieldDefinition((FieldDefinitionHandle)handle);
        }
        else if (handle.Kind == HandleKind.MemberReference)
        {
            FieldDefinition? resolved = ResolveFieldReference((MemberReferenceHandle)handle);

            if (resolved is null)
            {
                return null;
            }

            field = resolved.Value;
        }
        else
        {
            return null;
        }

        // 先看字段自己的初始数据（RVA）。
        //
        // Roslyn 把提升后的小数常量直接放在字段的数据区里，而不是在 .cctor 里赋值。
        // 升级增量大量写成 1m/2m，所以必须处理这种情况。
        double? fromRva = ReadFieldConstant(field);

        if (fromRva is not null)
        {
            return fromRva;
        }

        TypeDefinition owner = _reader.GetTypeDefinition(field.GetDeclaringType());

        foreach (MethodDefinitionHandle methodHandle in owner.GetMethods())
        {
            MethodDefinition method = _reader.GetMethodDefinition(methodHandle);

            if (_reader.GetString(method.Name) != ".cctor" || method.RelativeVirtualAddress == 0)
            {
                continue;
            }

            IReadOnlyList<IlInstruction> body = IlReader.Read(_pe, method);

            for (int i = 0; i < body.Count; i++)
            {
                // 只看"写回同一个字段"的那条语句。
                if (body[i].OpCode != IlOpCode.Stsfld || body[i].Token != fieldToken)
                {
                    continue;
                }

                // 值在它之前：字面量，或另一个静态字段（递归解析）。
                for (int j = i - 1; j >= 0 && j >= i - 6; j--)
                {
                    if (body[j].OpCode == IlOpCode.LdcI4 && body[j].Int32 is int iv)
                    {
                        return iv;
                    }

                    if (body[j].OpCode == IlOpCode.LdcR8 && body[j].Number is double rv)
                    {
                        return rv;
                    }

                    if (body[j].OpCode == IlOpCode.Ldsfld && body[j].Token is int other)
                    {
                        return ResolveStaticFieldValue(other);
                    }
                }
            }
        }

        return null;
    }

    /// <summary>
    /// 向前找本语句里的数字常量。
    ///
    /// 增量写成小数，编译后是 <c>ldc.i4.3 + newobj Decimal::.ctor</c>，所以数字在 newobj
    /// <b>之前</b>，不能把 newobj 当成边界。有的卡用静态字段（ldsfld）代替字面量，那就要
    /// 解析那个字段。真正的边界是"上一条语句的收尾调用"。
    /// </summary>
    private double? FindPrecedingNumber(IReadOnlyList<IlInstruction> instructions, int index)
    {
        for (int i = index - 1; i >= 0 && i >= index - 16; i--)
        {
            IlInstruction instruction = instructions[i];

            if (instruction.OpCode == IlOpCode.LdcI4 && instruction.Int32 is int i4)
            {
                return i4;
            }

            if (instruction.OpCode == IlOpCode.LdcR8 && instruction.Number is double r8)
            {
                return r8;
            }

            // 静态字段：解析它的值。
            if (instruction.OpCode == IlOpCode.Ldsfld && instruction.Token is int token)
            {
                return ResolveStaticFieldValue(token);
            }

            // 上一条语句结束：再往前就属于别的语句了。
            if (instruction.OpCode is IlOpCode.Call or IlOpCode.CallVirt &&
                instruction.MethodName is "UpgradeValueBy" or "UpgradeBy" or "AddKeyword" or "RemoveKeyword")
            {
                return null;
            }
        }

        return null;
    }

    /// <summary>
    /// 是否是编译器生成的辅助类型。
    ///
    /// 数组/集合字面量会生成 <c>&lt;&gt;z__ReadOnlyArray</c>、<c>&lt;&gt;z__ReadOnlySingleElementList</c>
    /// 这类类型名，它们只是"容器"，不是卡牌声明的数值。
    /// </summary>
    /// <summary>
    /// <c>System.Decimal</c> 的具名常量。
    ///
    /// 升级增量写成 <c>1m</c> 时，编译结果不是字面量，而是 <c>ldsfld System.Decimal::One</c>——
    /// 这个字段定义在框架程序集里，本程序集里找不到。不特判的话，绝大多数升级增量都会解析失败。
    /// </summary>
    private static double? DecimalConstant(string name) => name switch
    {
        "Zero" => 0d,
        "One" => 1d,
        "MinusOne" => -1d,
        "MaxValue" => (double)decimal.MaxValue,
        "MinValue" => (double)decimal.MinValue,
        _ => null,
    };

    private static bool IsCompilerHelper(string name) =>
        name.StartsWith("<>", StringComparison.Ordinal) ||
        name.StartsWith("__", StringComparison.Ordinal) ||
        name.Contains("ReadOnly", StringComparison.Ordinal);

    private static bool IsVarType(string name)
    {
        // 去掉泛型参数后的名字，例如 PowerVar`1。
        string bare = name.EndsWith("`1", StringComparison.Ordinal) ? name[..^2] : name;

        if (IsCompilerHelper(bare))
        {
            return false;
        }

        return bare.EndsWith("Var", StringComparison.Ordinal) ||
               bare is "DynamicVar" or "DamageVar" or "BlockVar" or "CardsVar" or "EnergyVar";
    }

    private static string KindFromTypeName(string name)
    {
        string bare = name.EndsWith("`1", StringComparison.Ordinal) ? name[..^2] : name;
        return bare.EndsWith("Var", StringComparison.Ordinal) ? bare[..^3] : bare;
    }

    // ---------------------------------------------------------------- 关键词

    /// <summary>
    /// 读自带关键词。三种写法都见过，都要支持：
    ///
    ///   1. 逐项存数组：<c>ldc.i4 &lt;序号&gt;</c> 紧跟 stelem（或 AddKeyword）
    ///   2. 静态数组 + InitializeArray：数据在字段的 RVA 里，要读原始字节
    ///   3. 直接返回空集合
    ///
    /// 不能见到 ldc.i4 就当关键词——数组长度也是 1..7 的小整数，会多出并不存在的关键词
    /// （例如"消耗"被算成三个）。第 1 种只看紧邻 stelem 的那一个。
    /// </summary>
    private IReadOnlyList<string> ReadKeywords(TypeDefinition type)
    {
        var keywords = new List<string>();

        foreach (MethodDefinitionHandle handle in type.GetMethods())
        {
            MethodDefinition method = _reader.GetMethodDefinition(handle);

            if (_reader.GetString(method.Name) != "get_CanonicalKeywords" ||
                method.RelativeVirtualAddress == 0)
            {
                continue;
            }

            IReadOnlyList<IlInstruction> instructions = IlReader.Read(_pe, method);

            // 第 1 种：逐项存数组。
            // 第 2 种：单元素集合，形如 ldc.i4 <序号> + newobj <ReadOnlySingleElementList>，
            //         集合构造函数只收一个元素，所以那个常量就是关键词。
            for (int i = 0; i < instructions.Count - 1; i++)
            {
                IlInstruction instruction = instructions[i];

                if (instruction.OpCode != IlOpCode.LdcI4 || instruction.Int32 is not int value)
                {
                    continue;
                }

                IlInstruction next = instructions[i + 1];

                bool storedInArray = next.OpCode == IlOpCode.Stelem;
                bool addedDirectly = next.OpCode is IlOpCode.Call or IlOpCode.CallVirt &&
                                     next.MethodName == "AddKeyword";

                // 单元素集合：ldc.i4 <关键词> + newobj <>z__ReadOnlySingleElementList<CardKeyword>。
                // 只有泛型实参是 CardKeyword 时才算——同形状也可能是
                // <...SingleElementList<DynamicVar>>（装数值用的），那个数字不是关键词。
                bool singleElementList = next.OpCode == IlOpCode.NewObj &&
                                         next.ParameterCount == 1 &&
                                         next.GenericArgument == "CardKeyword";

                if ((storedInArray || addedDirectly || singleElementList) &&
                    KeywordName(value) is string name)
                {
                    keywords.Add(name);
                }
            }

            // 第 2 种：静态数组的数据在字段 RVA 里。
            foreach (int value in ReadInitializedArrayValues(instructions))
            {
                if (KeywordName(value) is string name)
                {
                    keywords.Add(name);
                }
            }
        }

        return keywords.Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// 取出 <c>InitializeArray</c> 用的静态数组内容。
    ///
    /// 编译器把数组字面量放进一个字段的数据区（RVA），运行时原样拷贝。要拿到里面的值，就得
    /// 按 RVA 去读那一段字节；元素是 4 字节小整数。
    /// </summary>
    private IEnumerable<int> ReadInitializedArrayValues(IReadOnlyList<IlInstruction> instructions)
    {
        var values = new List<int>();

        for (int i = 0; i < instructions.Count; i++)
        {
            if (instructions[i].OpCode is not (IlOpCode.Call or IlOpCode.CallVirt) ||
                instructions[i].MethodName != "InitializeArray")
            {
                continue;
            }

            // 前一条是 ldtoken，带字段句柄。
            if (i == 0 || instructions[i - 1].Token is not int token)
            {
                continue;
            }

            var handle = System.Reflection.Metadata.Ecma335.MetadataTokens.EntityHandle(token);

            if (handle.Kind != HandleKind.FieldDefinition)
            {
                continue;
            }

            FieldDefinition field = _reader.GetFieldDefinition((FieldDefinitionHandle)handle);
            int rva = field.GetRelativeVirtualAddress();

            if (rva == 0)
            {
                continue;
            }

            try
            {
                byte[] data = IlReader.ReadFieldData(_pe, _image, rva, 64);

                // 静态数组的元素个数是 4 字节整数，所以按 4 字节小端读。
                for (int offset = 0; offset + 4 <= data.Length; offset += 4)
                {
                    int value = BitConverter.ToInt32(data, offset);

                    // 关键词序号只可能是小正数，其它字节（填充等）跳过。
                    if (value is > 0 and < 32)
                    {
                        values.Add(value);
                    }
                }
            }
            catch (BadImageFormatException)
            {
                // 读不到就算了，不影响其它字段。
            }
        }

        return values;
    }

    // ---------------------------------------------------------------- 升级

    /// <summary>
    /// 读 OnUpgrade 的改动：数值增减、费用增减、关键词增删。
    ///
    /// 数值部分要认出"改的是哪个变量"。升级代码形如
    /// <c>DynamicVars["Damage"].UpgradeValueBy(3m)</c>，所以向前找最近的字符串就是键；
    /// 用属性形式（<c>DynamicVars.Damage</c>）时则取 get_Damage 的方法名。
    /// </summary>
    private CardStatUpgrade ReadUpgrade(
        TypeDefinition type,
        IReadOnlyList<CardStatVar> vars,
        IReadOnlyList<string> baseKeywords)
    {
        int energyDelta = 0;
        var varDeltas = new Dictionary<string, double>(StringComparer.Ordinal);
        var added = new List<string>();
        var removed = new List<string>();

        foreach (MethodDefinitionHandle handle in type.GetMethods())
        {
            MethodDefinition method = _reader.GetMethodDefinition(handle);

            if (_reader.GetString(method.Name) != "OnUpgrade" || method.RelativeVirtualAddress == 0)
            {
                continue;
            }

            IReadOnlyList<IlInstruction> instructions = IlReader.Read(_pe, method);

            // 有些卡把升级增量放在静态字段里（OnUpgrade 里是 ldsfld 而不是字面量），
            // 所以先把本类型的静态数值字段解析出来，供下面查表。

            for (int i = 0; i < instructions.Count; i++)
            {
                IlInstruction instruction = instructions[i];

                if (instruction.OpCode is not (IlOpCode.Call or IlOpCode.CallVirt))
                {
                    continue;
                }

                switch (instruction.MethodName)
                {
                    case "UpgradeValueBy":
                    {
                        double? delta = FindPrecedingNumber(instructions, i);
                        string? key = FindVarKey(instructions, i, vars);


                        if (delta is null)
                        {
                            break;
                        }

                        if (key is not null)
                        {
                            varDeltas[key] = varDeltas.TryGetValue(key, out double existing)
                                ? existing + delta.Value
                                : delta.Value;
                        }

                        break;
                    }

                    case "UpgradeBy" when instruction.DeclaringTypeName == "CardEnergyCost":
                    {
                        if (FindPrecedingNumber(instructions, i) is double costDelta)
                        {
                            energyDelta += (int)costDelta;
                        }

                        break;
                    }

                    case "AddKeyword":
                    {
                        // 前面紧邻的常量就是关键词序号。
                        if (FindPrecedingKeyword(instructions, i) is string name)
                        {
                            added.Add(name);
                        }

                        break;
                    }

                    case "RemoveKeyword":
                    {
                        if (FindPrecedingKeyword(instructions, i) is string name)
                        {
                            removed.Add(name);
                        }

                        break;
                    }
                }
            }
        }

        // 升级按"取最大"合并：同一张卡不会给同一个变量两个增量。
        return new CardStatUpgrade
        {
            EnergyDelta = energyDelta,
            VarDeltas = varDeltas,
            AddedKeywords = added.Distinct(StringComparer.Ordinal).Except(baseKeywords).ToList(),
            RemovedKeywords = removed.Distinct(StringComparer.Ordinal).ToList(),
        };
    }

    /// <summary>
    /// 认出击中"改哪个变量"：向前找取变量名字的那一步。
    ///
    /// 升级语句形如 <c>DynamicVars["Damage"].UpgradeValueBy(3m)</c>，编译后是
    /// <c>get_DynamicVars</c> → <c>get_Item("Damage")</c> → 数字 → <c>UpgradeValueBy</c>。
    /// 所以向前找的时候会先碰到 <c>get_DynamicVars</c>，不能在那里停下——要一路找到
    /// **能在本卡声明的数值里对上号**的那个名字（get_Damage 或字符串 "Damage"）。
    /// </summary>
    private static string? FindVarKey(
        IReadOnlyList<IlInstruction> instructions,
        int index,
        IReadOnlyList<CardStatVar> vars)
    {
        for (int i = index - 1; i >= 0 && i >= index - 20; i--)
        {
            IlInstruction instruction = instructions[i];

            // DynamicVars["Damage"] 形式的字符串索引。
            if (instruction.OpCode == IlOpCode.LdStr && !string.IsNullOrEmpty(instruction.Text))
            {
                return instruction.Text;
            }

            if (instruction.OpCode is not (IlOpCode.Call or IlOpCode.CallVirt) ||
                instruction.MethodName is not string property ||
                !property.StartsWith("get_", StringComparison.Ordinal))
            {
                continue;
            }

            string name = property[4..];

            // 有同名的数值变量才认，避免把 DynamicVars 本身当成键。
            if (vars.Any(v => string.Equals(v.Key, name, StringComparison.Ordinal)))
            {
                return name;
            }

            // 泛型状态变量：声明的键是 PlatingPower，而属性名是 Plating。
            string? generic = vars.FirstOrDefault(
                v => v.Kind == "Power" && v.Key.StartsWith(name, StringComparison.Ordinal))?.Key;

            if (generic is not null)
            {
                return generic;
            }

            // 上一条语句的收尾调用：再往前就不属于这一条了。
            if (property is "UpgradeValueBy" or "UpgradeBy" or "AddKeyword" or "RemoveKeyword")
            {
                return null;
            }
        }

        return null;
    }

    /// <summary>认出击中"增删哪个关键词"。</summary>
    private static string? FindPrecedingKeyword(IReadOnlyList<IlInstruction> instructions, int index)
    {
        for (int i = index - 1; i >= 0 && i >= index - 6; i--)
        {
            if (instructions[i].OpCode == IlOpCode.LdcI4 &&
                instructions[i].Int32 is int value &&
                KeywordName(value) is string name)
            {
                return name;
            }
        }

        return null;
    }

    // ---------------------------------------------------------------- 杂项

    /// <summary>某个属性是否被重写成固定返回 true（用于识别 X 费）。</summary>
    private bool OverridesTrue(TypeDefinition type, string propertyGetterName)
    {
        foreach (MethodDefinitionHandle handle in type.GetMethods())
        {
            MethodDefinition method = _reader.GetMethodDefinition(handle);

            if (_reader.GetString(method.Name) != propertyGetterName ||
                method.RelativeVirtualAddress == 0)
            {
                continue;
            }

            // 方法体就是 ldc.i4.1 + ret。
            foreach (IlInstruction instruction in IlReader.Read(_pe, method))
            {
                if (instruction.OpCode == IlOpCode.LdcI4)
                {
                    return instruction.Int32 == 1;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// 判断是否直接继承 CardModel。
    ///
    /// 基类在元数据里既可能是 TypeReference（引用了别的程序集里的类型），也可能是
    /// TypeDefinition（同一个程序集里定义的类型，本项目就是这种）；两种都要处理，
    /// 否则一张卡都认不出来。
    /// </summary>
    private bool DerivesFromCardModel(TypeDefinition type)
    {
        EntityHandle baseType = type.BaseType;

        if (baseType.IsNil)
        {
            return false;
        }

        switch (baseType.Kind)
        {
            case HandleKind.TypeReference:
            {
                TypeReference reference = _reader.GetTypeReference((TypeReferenceHandle)baseType);
                return _reader.GetString(reference.Name) == CardBaseType;
            }

            case HandleKind.TypeDefinition:
            {
                TypeDefinition definition = _reader.GetTypeDefinition((TypeDefinitionHandle)baseType);
                return _reader.GetString(definition.Name) == CardBaseType;
            }

            default:
                return false;
        }
    }

    private MethodDefinition? FindParameterlessCtor(TypeDefinition type)
    {
        foreach (MethodDefinitionHandle handle in type.GetMethods())
        {
            MethodDefinition method = _reader.GetMethodDefinition(handle);

            if (_reader.GetString(method.Name) == ".ctor" &&
                method.GetParameters().Count == 0)
            {
                return method;
            }
        }

        return null;
    }

    private string? TypeNameOf(EntityHandle handle)
    {
        if (handle.Kind == HandleKind.TypeReference)
        {
            TypeReference reference = _reader.GetTypeReference((TypeReferenceHandle)handle);
            return _reader.GetString(reference.Name);
        }

        if (handle.Kind == HandleKind.TypeDefinition)
        {
            return Name(_reader.GetTypeDefinition((TypeDefinitionHandle)handle));
        }

        return null;
    }

    private string Name(TypeDefinition type) => _reader.GetString(type.Name);

    private string Namespace(TypeDefinition type) => _reader.GetString(type.Namespace);

    // ---------------------------------------------------------------- 枚举与命名

    private static string CardTypeName(int value) => value switch
    {
        1 => "attack",
        2 => "skill",
        3 => "power",
        4 => "status",
        5 => "curse",
        6 => "quest",
        _ => "none",
    };

    private static string RarityName(int value) => value switch
    {
        1 => "basic",
        2 => "common",
        3 => "uncommon",
        4 => "rare",
        5 => "ancient",
        6 => "event",
        7 => "token",
        8 => "status",
        9 => "curse",
        10 => "quest",
        _ => "none",
    };

    private static string TargetName(int value) => value switch
    {
        1 => "self",
        2 => "any_enemy",
        3 => "all_enemies",
        4 => "random_enemy",
        5 => "any_player",
        6 => "any_ally",
        7 => "all_allies",
        8 => "targeted_no_creature",
        9 => "osty",
        _ => "none",
    };

    /// <summary>关键词序号对应的条目名，与本地化表的键一致。</summary>
    private static string? KeywordName(int value) => value switch
    {
        1 => "EXHAUST",
        2 => "ETHEREAL",
        3 => "INNATE",
        4 => "UNPLAYABLE",
        5 => "RETAIN",
        6 => "SLY",
        7 => "ETERNAL",
        _ => null,
    };

    /// <summary>StrikeIronclad → strike_ironclad（本地化键就是它的大写形式）。</summary>
    public static string PascalToSnake(string value)
    {
        var sb = new System.Text.StringBuilder(value.Length + 8);

        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            char prev = i > 0 ? value[i - 1] : '\0';
            char next = i + 1 < value.Length ? value[i + 1] : '\0';

            if (char.IsUpper(c) && i > 0 &&
                (char.IsLower(prev) || char.IsDigit(prev) || char.IsLower(next)))
            {
                sb.Append('_');
            }

            sb.Append(char.ToLowerInvariant(c));
        }

        return sb.ToString();
    }

    // ---------------------------------------------------------------- 卡图与牌池

    /// <summary>
    /// 用卡图路径补上牌池与卡图名。
    ///
    /// 卡图按 <c>images/packed/card_portraits/&lt;牌池&gt;/&lt;名字&gt;.png.import</c> 存放，
    /// 所以那张清单同时给出"这张卡属于哪个牌池"和"它的图叫什么"——这也是唯一现成的对应关系。
    /// </summary>
    private void AssignArt(Dictionary<string, CardStats> cards, string gameDir)
    {
        Dictionary<string, (string Pool, string Stem)> byStem = CardArtIndex.Build(gameDir);

        foreach ((string entry, CardStats stats) in cards.ToList())
        {
            string stem = ChooseArtStem(stats.ClassName, stats.CardType);

            if (byStem.TryGetValue(stem, out (string Pool, string Stem) found))
            {
                cards[entry] = stats with { Pool = found.Pool, ArtStem = found.Stem };
            }
            else
            {
                cards[entry] = stats with { ArtStem = stem };
            }
        }
    }

    /// <summary>
    /// 卡图名一般就是类型名的下划线形式；MadScience 是唯一例外——它按形态分成两张图。
    /// </summary>
    private static string ChooseArtStem(string className, string cardType) =>
        className == "MadScience" ? $"mad_science_{cardType}" : PascalToSnake(className);
}
