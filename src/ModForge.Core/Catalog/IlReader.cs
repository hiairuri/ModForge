using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace ModForge.Core.Catalog;

/// <summary>一条 IL 指令里我们需要用到的部分。</summary>
internal enum IlOpCode
{
    Other,

    /// <summary>把整数常量压栈（ldc.i4 系列，已统一成 int32）。</summary>
    LdcI4,

    /// <summary>把 double 常量压栈（ldc.r8）。</summary>
    LdcR8,

    /// <summary>把字符串压栈。</summary>
    LdStr,

    NewObj,
    Call,
    CallVirt,
    NewArr,
    Stelem,
    Ret,

    /// <summary>读静态字段（升级增量可能放在静态字段里）。</summary>
    Ldsfld,

    /// <summary>写静态字段（静态构造函数里给增量赋值）。</summary>
    Stsfld,
}

/// <summary>
/// 一条 IL 指令的摘要：只需要判断"是不是常量/调用"，以及调用的是谁。
///
/// 这里不做完整的求值栈模拟——卡牌的定义方式很规整（构造参数都是紧挨着的常量），
/// 按模式取相邻常量就够了，代码量小得多，也更容易看懂。
/// </summary>
internal readonly record struct IlInstruction(
    IlOpCode OpCode,
    int? Int32 = null,
    double? Number = null,
    string? Text = null,

    /// <summary>被调用方法的类型名（去掉命名空间、泛型参数与 `1 后缀）。</summary>
    string? DeclaringTypeName = null,

    /// <summary>被调用方法名。</summary>
    string? MethodName = null,

    /// <summary>泛型实参名，例如 PowerVar&lt;StrengthPower&gt; 里的 StrengthPower。</summary>
    string? GenericArgument = null,

    /// <summary>
    /// 被调用方法/构造函数的参数个数。用来正确消费求值栈——参数个数直接决定
    /// <c>newobj</c> 会弹掉几个值。
    /// </summary>
    int ParameterCount = 0,

    /// <summary>元数据 token（方法、字段等），用于需要进一步解析指令的场合。</summary>
    int? Token = null);

/// <summary>读取方法体里的 IL 指令，只提取我们需要的常量与调用。</summary>
internal static class IlReader
{
    /// <summary>
    /// 操作码表：字节值 → 操作数长度与是否为整数常量。
    ///
    /// 从 <see cref="System.Reflection.Emit.OpCodes"/> 反射得到（见 <see cref="BuildTable"/>），
    /// 而不是手写 switch——手写极易漏掉单字节指令（例如 ldarg.0 是 0x02），一旦漏掉就会
    /// 在解析中途停下，表现为"一张卡都读不出来"，很难查。
    /// </summary>
    private static readonly (Dictionary<byte, OpInfo> OneByte, Dictionary<int, OpInfo> TwoByte) Tables = BuildTable();

    private static readonly Dictionary<byte, OpInfo> OpCodeTable = Tables.OneByte;

    private static readonly Dictionary<int, OpInfo> TwoByteOpCodes = Tables.TwoByte;

    /// <summary>
    /// 逐条读出指令。遇到无法解析的指令就停止——继续读会错位，读到的常量是错的。
    /// </summary>
    public static IReadOnlyList<IlInstruction> Read(PEReader pe, MethodDefinition method)
    {
        var result = new List<IlInstruction>();

        if (method.RelativeVirtualAddress == 0)
        {
            return result;
        }

        MethodBodyBlock body;

        try
        {
            body = pe.GetMethodBody(method.RelativeVirtualAddress);
        }
        catch (BadImageFormatException)
        {
            return result;
        }

        BlobReader reader = body.GetILReader();

        while (reader.RemainingBytes > 0)
        {
            IlInstruction? instruction = ReadOne(pe, ref reader);

            if (instruction is null)
            {
                break;
            }

            result.Add(instruction.Value);
        }

        return result;
    }

    private static IlInstruction? ReadOne(PEReader pe, ref BlobReader reader)
    {
        int opcode = reader.ReadByte();

        // 0xFE 是双字节操作码前缀：用"前缀+第二字节"当键，避免和单字节指令混在一起。
        bool twoByte = opcode == 0xFE;

        if (twoByte)
        {
            if (reader.RemainingBytes == 0)
            {
                return null;
            }

            opcode = 0xFE00 | reader.ReadByte();
        }

        OpInfo info;

        if (!(twoByte
                ? TwoByteOpCodes.TryGetValue(opcode, out info)
                : OpCodeTable.TryGetValue((byte)opcode, out info)))
        {
            // 不认识的指令，长度未知，停下以免后续错位。
            return null;
        }

        // switch 指令的操作数长度由前面的元素个数决定。
        if (info.IsVariableSize)
        {
            if (reader.RemainingBytes < 4)
            {
                return null;
            }

            int count = reader.ReadInt32();
            int skip = count * 4;

            if (skip < 0 || skip > reader.RemainingBytes)
            {
                return null;
            }

            reader.ReadBytes(skip);
            return new IlInstruction(IlOpCode.Other);
        }

        // 整数常量：把操作数读出来，这是我们要的值。
        if (info.IsLdcI4)
        {
            int value = info.OperandSize switch
            {
                0 => opcode switch
                {
                    0x15 => -1,     // ldc.i4.m1
                    0x16 or 0x17 or 0x18 or 0x19 or 0x1A or
                    0x1B or 0x1C or 0x1D or 0x1E => opcode - 0x16,
                    _ => 0,
                },
                1 => reader.ReadSByte(),
                4 => reader.ReadInt32(),
                8 => (int)reader.ReadInt64(),
                _ => 0,
            };

            return new IlInstruction(IlOpCode.LdcI4, Int32: value);
        }

        // 其余指令：读取操作数并按需解析。
        switch (opcode)
        {
            case 0x23: // ldc.r8
                return new IlInstruction(IlOpCode.LdcR8, Number: info.OperandSize == 8 ? reader.ReadDouble() : 0);

            case 0x22: // ldc.r4
                return new IlInstruction(IlOpCode.LdcR8, Number: reader.ReadSingle());

            case 0x72: // ldstr
            {
                int token = reader.ReadInt32();
                return new IlInstruction(IlOpCode.LdStr, Text: ReadUserString(pe, token));
            }

            case 0x28: // call
            case 0x6F: // callvirt
            {
                int token = reader.ReadInt32();
                (string? owner, string? name) = ReadMethodRef(pe, token);
                IlOpCode kind = opcode == 0x28 ? IlOpCode.Call : IlOpCode.CallVirt;
                return new IlInstruction(kind, DeclaringTypeName: owner, MethodName: name, Token: token);
            }

            case 0x73: // newobj
            {
                int token = reader.ReadInt32();
                (string? owner, string? name) = ReadMethodRef(pe, token);
                return new IlInstruction(
                    IlOpCode.NewObj,
                    DeclaringTypeName: owner,
                    MethodName: name,
                    GenericArgument: ReadGenericArgument(pe, token),
                    ParameterCount: ReadParameterCount(pe, token),
                    Token: token);
            }

            case 0xD0: // ldtoken：静态数组初始化要用它拿字段句柄
            {
                int token = reader.ReadInt32();
                return new IlInstruction(IlOpCode.Other, Token: token);
            }

            case 0x7E: // ldsfld
            {
                int token = reader.ReadInt32();
                return new IlInstruction(IlOpCode.Ldsfld, Token: token);
            }

            case 0x80: // stsfld
            {
                int token = reader.ReadInt32();
                return new IlInstruction(IlOpCode.Stsfld, Token: token);
            }

            case 0x8D: // newarr
                reader.ReadInt32();
                return new IlInstruction(IlOpCode.NewArr);

            // 往数组里存元素。整个 stelem 家族都要认出来（0x9B..0xA2）：
            // 关键词与数值都是靠"常量 + stelem"装进数组的，漏一个变体就会整段读不出东西。
            // 各变体的操作数长度都是 0，只是因为元素类型不同而有不同编码。
            case 0x9B: // stelem.i1
            case 0x9C: // stelem.i2
            case 0x9D: // stelem.i4
            case 0x9E: // stelem.i8
            case 0x9F: // stelem.r4
            case 0xA0: // stelem.r6
            case 0xA1: // stelem.r8
            case 0xA2: // stelem.ref
                return new IlInstruction(IlOpCode.Stelem);

            case 0x8E: // stelem.i4（另一套编码，早年编译器会发这个）
                return new IlInstruction(IlOpCode.Stelem);

            case 0x2A: // ret
                return new IlInstruction(IlOpCode.Ret);

            default:
                // 其余指令只需按长度跳过操作数。
                if (info.OperandSize > 0 && info.OperandSize <= reader.RemainingBytes)
                {
                    reader.ReadBytes(info.OperandSize);
                }

                return new IlInstruction(IlOpCode.Other);
        }
    }

    /// <summary>
    /// 生成单字节与双字节两张操作码表。
    ///
    /// 从运行时的定义反射出来，而不是手写 switch：手写极易漏掉指令（例如 ldarg.0 是 0x02），
    /// 漏一个就会在解析中途停下，表现为"一张卡都读不出来"，很难查。
    /// </summary>
    private static (Dictionary<byte, OpInfo> OneByte, Dictionary<int, OpInfo> TwoByte) BuildTable()
    {
        var oneByte = new Dictionary<byte, OpInfo>();
        var twoByte = new Dictionary<int, OpInfo>();

        foreach (System.Reflection.FieldInfo field in typeof(System.Reflection.Emit.OpCodes)
                     .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
        {
            if (field.GetValue(null) is not System.Reflection.Emit.OpCode op)
            {
                continue;
            }

            var info = new OpInfo(
                OperandSize: OperandSizeOf(op.OperandType),
                IsLdcI4: op.Name.StartsWith("ldc.i4", StringComparison.Ordinal),
                IsVariableSize: op.OperandType == System.Reflection.Emit.OperandType.InlineSwitch);

            if (op.Size == 1)
            {
                oneByte[(byte)op.Value] = info;
            }
            else
            {
                // 双字节操作码的 Value 已经是 0xFE00 | 第二字节。
                twoByte[op.Value] = info;
            }
        }

        return (oneByte, twoByte);
    }

    private static int OperandSizeOf(System.Reflection.Emit.OperandType type) => type switch
    {
        System.Reflection.Emit.OperandType.InlineNone => 0,
        System.Reflection.Emit.OperandType.ShortInlineI => 1,
        System.Reflection.Emit.OperandType.ShortInlineVar => 1,
        System.Reflection.Emit.OperandType.ShortInlineBrTarget => 1,
        System.Reflection.Emit.OperandType.ShortInlineR => 4,
        System.Reflection.Emit.OperandType.InlineVar => 2,
        System.Reflection.Emit.OperandType.InlineI => 4,
        System.Reflection.Emit.OperandType.InlineBrTarget => 4,
        System.Reflection.Emit.OperandType.InlineMethod => 4,
        System.Reflection.Emit.OperandType.InlineField => 4,
        System.Reflection.Emit.OperandType.InlineType => 4,
        System.Reflection.Emit.OperandType.InlineString => 4,
        System.Reflection.Emit.OperandType.InlineSig => 4,
        System.Reflection.Emit.OperandType.InlineTok => 4,
        System.Reflection.Emit.OperandType.InlineSwitch => 4,
        System.Reflection.Emit.OperandType.InlineI8 => 8,
        System.Reflection.Emit.OperandType.InlineR => 8,
        _ => 4,
    };

    private readonly record struct OpInfo(int OperandSize, bool IsLdcI4, bool IsVariableSize);

    /// <summary>
    /// 把签名字节流里的类型句柄解出来。
    ///
    /// 注意不能直接丢给 <c>MetadataTokens.EntityHandle</c>：签名里存的是 TypeDefOrRef
    /// <b>编码索引</b>（低 2 位是"是 TypeDef / TypeRef / TypeSpec"的标记），而 EntityHandle
    /// 要的是完整 token（高位才是表号）。直接传会解出错误的类型，泛型变量就会认不出来。
    /// </summary>
    private static EntityHandle DecodeTypeHandle(int coded)
    {
        int row = coded >> 2;

        return (coded & 3) switch
        {
            0 => MetadataTokens.TypeDefinitionHandle(row),
            1 => MetadataTokens.TypeReferenceHandle(row),
            _ => MetadataTokens.TypeSpecificationHandle(row),
        };
    }

    /// <summary>
    /// 从方法引用签名里读参数个数。
    ///
    /// 签名开头是调用约定字节，紧接一个压缩整数就是参数个数。读不懂时返回 0，
    /// 调用方据此退化为"不弹栈"，不会崩。
    /// </summary>
    private static int ReadParameterCount(PEReader pe, int token)
    {
        MetadataReader reader = pe.GetMetadataReader();
        var handle = MetadataTokens.EntityHandle(token);

        try
        {
            BlobHandle signature;

            switch (handle.Kind)
            {
                case HandleKind.MemberReference:
                {
                    MemberReference reference = reader.GetMemberReference((MemberReferenceHandle)handle);
                    signature = reference.Signature;
                    break;
                }

                case HandleKind.MethodDefinition:
                {
                    MethodDefinition definition = reader.GetMethodDefinition((MethodDefinitionHandle)handle);
                    signature = definition.Signature;
                    break;
                }

                case HandleKind.MethodSpecification:
                {
                    MethodSpecification spec = reader.GetMethodSpecification((MethodSpecificationHandle)handle);
                    return ReadParameterCount(pe, MetadataTokens.GetToken(spec.Method));
                }

                default:
                    return 0;
            }

            BlobReader blob = reader.GetBlobReader(signature);
            blob.ReadByte();                                   // 调用约定
            return blob.ReadCompressedInteger();               // 参数个数
        }
        catch (BadImageFormatException)
        {
            return 0;
        }
    }

    /// <summary>解析方法引用，取出声明类型名与方法名。</summary>
    private static (string? Owner, string? Name) ReadMethodRef(PEReader pe, int token)
    {
        MetadataReader reader = pe.GetMetadataReader();
        var handle = MetadataTokens.EntityHandle(token);

        switch (handle.Kind)
        {
            case HandleKind.MemberReference:
            {
                MemberReference reference = reader.GetMemberReference((MemberReferenceHandle)handle);


                return (OwnerName(reader, reference.Parent), reader.GetString(reference.Name));
            }

            case HandleKind.MethodDefinition:
            {
                MethodDefinition definition = reader.GetMethodDefinition((MethodDefinitionHandle)handle);
                TypeDefinition owner = reader.GetTypeDefinition(definition.GetDeclaringType());
                return (TrimArity(reader.GetString(owner.Name)), reader.GetString(definition.Name));
            }

            case HandleKind.MethodSpecification:
            {
                MethodSpecification spec = reader.GetMethodSpecification((MethodSpecificationHandle)handle);
                return ReadMethodRef(pe, MetadataTokens.GetToken(spec.Method));
            }

            default:
                return (null, null);
        }
    }

    /// <summary>
    /// 构造 PowerVar&lt;StrengthPower&gt; 这类泛型实例时，取出实参名。
    ///
    /// 名字就是描述占位符用的键（{StrengthPower:diff()}），所以必须拿到；读不到也不影响
    /// 其它字段，只是那个变量的键会退化成类型名。
    /// </summary>
    private static string? ReadGenericArgument(PEReader pe, int token)
    {
        MetadataReader reader = pe.GetMetadataReader();
        var handle = MetadataTokens.EntityHandle(token);

        if (handle.Kind != HandleKind.MemberReference)
        {
            return null;
        }

        MemberReference reference = reader.GetMemberReference((MemberReferenceHandle)handle);

        if (reference.Parent.Kind != HandleKind.TypeSpecification)
        {
            return null;
        }

        TypeSpecification spec = reader.GetTypeSpecification((TypeSpecificationHandle)reference.Parent);
        BlobReader blob = reader.GetBlobReader(spec.Signature);

        try
        {
            // GENERICINST(0x15) CLASS/VALUETYPE(0x11|0x12) <类型> <参数个数> <实参>…
            if (blob.ReadByte() != 0x15)
            {
                return null;
            }

            byte ownerKind = blob.ReadByte();

            if (ownerKind is not (0x11 or 0x12))
            {
                return null;
            }

            blob.ReadCompressedInteger();                          // 泛型类型本身
            int count = blob.ReadCompressedInteger();              // 参数个数

            if (count <= 0)
            {
                return null;
            }

            // 第一个实参也是 "ELEMENT_TYPE_CLASS/VALUETYPE + 类型" 的形式，
            // 所以要先读掉元素类型字节再读句柄。
            byte argKind = blob.ReadByte();

            if (argKind is not (0x11 or 0x12))
            {
                return null;
            }

            int argument = blob.ReadCompressedInteger();
            return TypeNameOfHandle(reader, DecodeTypeHandle(argument));
        }
        catch (BadImageFormatException)
        {
            return null;
        }
    }

    private static string? OwnerName(MetadataReader reader, EntityHandle parent)
    {
        if (parent.Kind == HandleKind.TypeSpecification)
        {
            TypeSpecification spec = reader.GetTypeSpecification((TypeSpecificationHandle)parent);
            BlobReader blob = reader.GetBlobReader(spec.Signature);

            try
            {
                if (blob.ReadByte() != 0x15)
                {
                    return null;
                }

                blob.ReadByte();                                  // CLASS / VALUETYPE
                int owner = blob.ReadCompressedInteger();
                return TypeNameOfHandle(reader, DecodeTypeHandle(owner));
            }
            catch (BadImageFormatException)
            {
                return null;
            }
        }

        return TypeNameOfHandle(reader, parent);
    }

    private static string? TypeNameOfHandle(MetadataReader reader, EntityHandle handle)
    {
        switch (handle.Kind)
        {
            case HandleKind.TypeReference:
                return TrimArity(reader.GetString(reader.GetTypeReference((TypeReferenceHandle)handle).Name));

            case HandleKind.TypeDefinition:
                return TrimArity(reader.GetString(reader.GetTypeDefinition((TypeDefinitionHandle)handle).Name));

            default:
                return null;
        }
    }

    /// <summary>去掉 `1 这样的泛型参数个数后缀。</summary>
    private static string TrimArity(string name)
    {
        int tick = name.IndexOf('`', StringComparison.Ordinal);
        return tick >= 0 ? name[..tick] : name;
    }

    private static string? ReadUserString(PEReader pe, int token)
    {
        try
        {
            return pe.GetMetadataReader().GetUserString(MetadataTokens.UserStringHandle(token));
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// 按 RVA 读取字段的数据区（静态数组字面量、被提升的常量小数都放在那里）。
    ///
    /// 用 <c>GetEntireImage()</c> 是不行的——它只给出元数据那一段，而字段数据在别的节里，
    /// 这样永远读不到（此前升级增量一直解析不出来就是这个原因）。所以拿节表把 RVA 换算成
    /// 文件偏移，再从传进来的完整镜像字节里取。
    /// </summary>
    public static byte[] ReadFieldData(PEReader pe, byte[] image, int rva, int maxBytes)
    {
        foreach (SectionHeader section in pe.PEHeaders.SectionHeaders)
        {
            int start = section.VirtualAddress;
            int end = start + section.VirtualSize;

            if (rva < start || rva >= end)
            {
                continue;
            }

            int withinSection = rva - start;

            // 节里可能有一段"只在内存里"的部分（没有原始字节），那就没得读。
            if (withinSection < 0 || withinSection >= section.SizeOfRawData)
            {
                return Array.Empty<byte>();
            }

            long offset = (long)section.PointerToRawData + withinSection;

            if (offset < 0 || offset >= image.Length)
            {
                return Array.Empty<byte>();
            }

            int take = (int)Math.Min(
                Math.Min(maxBytes, section.SizeOfRawData - withinSection),
                image.Length - offset);

            return take <= 0 ? Array.Empty<byte>() : image.AsSpan((int)offset, take).ToArray();
        }

        return Array.Empty<byte>();
    }
}
