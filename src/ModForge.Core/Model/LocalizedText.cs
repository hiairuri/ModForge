using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModForge.Core.Model;

/// <summary>
/// 按语言存放的一段文本，例如一张卡在每种语言下的名字与描述。
///
/// 序列化成扁平对象 <c>{"eng": "...", "zhs": "..."}</c>，与游戏本地化文件的形状一致，
/// 读起来也直观。只保存真正填写过的语言，未填的语言不写出，由游戏的兜底机制处理。
/// </summary>
[JsonConverter(typeof(LocalizedTextJsonConverter))]
public sealed class LocalizedText
{
    private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);

    public LocalizedText()
    {
    }

    public LocalizedText(string language, string text)
    {
        if (!string.IsNullOrEmpty(text))
        {
            _values[language] = text;
        }
    }

    /// <summary>读取某种语言的文本；未填写时返回空字符串，不回退。</summary>
    public string this[string language]
    {
        get => _values.TryGetValue(language, out string? text) ? text : string.Empty;
        set
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                _values.Remove(language);
            }
            else
            {
                _values[language] = value;
            }
        }
    }

    /// <summary>已填写文本的语言代码。</summary>
    public IEnumerable<string> Languages => _values.Keys;

    public bool IsEmpty => _values.Count == 0;

    public int Count => _values.Count;

    /// <summary>
    /// 读取文本，缺失时依次回退到英语、再到任意一个已填语言。
    ///
    /// 游戏自己的本地化表以英语兜底，所以这里保持同样的顺序，编辑器预览与游戏内的实际
    /// 表现才一致。
    /// </summary>
    public string GetOrFallback(string language)
    {
        if (_values.TryGetValue(language, out string? exact) && !string.IsNullOrEmpty(exact))
        {
            return exact;
        }

        if (_values.TryGetValue(GameLanguages.Fallback, out string? english) && !string.IsNullOrEmpty(english))
        {
            return english;
        }

        return _values.Values.FirstOrDefault(v => !string.IsNullOrEmpty(v)) ?? string.Empty;
    }

    /// <summary>是否填了该语言，用于区分"翻译缺失"与"内容为空"。</summary>
    public bool Has(string language) => !string.IsNullOrEmpty(this[language]);

    /// <summary>把另一种语言的文本搬过来，仅在目标语言为空时使用。</summary>
    public void FillFrom(LocalizedText source, string language)
    {
        if (!Has(language) && source.Has(language))
        {
            this[language] = source[language];
        }
    }

    /// <summary>
    /// 设置一种语言的文本并返回自身，便于连续赋值：
    /// <c>new LocalizedText("eng", "Fire").With("zhs", "火焰")</c>。
    /// </summary>
    public LocalizedText With(string language, string text)
    {
        this[language] = text;
        return this;
    }

    public override string ToString() =>
        _values.Count == 0 ? "(empty)" : string.Join(", ", _values.Select(kv => $"{kv.Key}={kv.Value}"));

    /// <summary>
    /// 允许直接用字符串赋值，含义是"只填英语"。
    ///
    /// 让样例与测试项目保持简短可读；真正需要多语言的地方仍应显式按语言赋值。
    /// </summary>
    public static implicit operator LocalizedText(string text) =>
        new(GameLanguages.Fallback, text ?? string.Empty);
}

/// <summary>
/// 把 <see cref="LocalizedText"/> 读写为扁平的语言到文本的对象。
/// </summary>
public sealed class LocalizedTextJsonConverter : JsonConverter<LocalizedText>
{
    public override LocalizedText Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var result = new LocalizedText();

        if (reader.TokenType == JsonTokenType.Null)
        {
            return result;
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            // 容忍旧写法（单个字符串），当作英语，这样早期项目文件仍可读入。
            result[GameLanguages.Fallback] = reader.GetString() ?? string.Empty;
            return result;
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException($"本地化文本应为对象或字符串，实际是 {reader.TokenType}。");
        }

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                break;
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                continue;
            }

            string key = reader.GetString() ?? string.Empty;
            reader.Read();

            // 只接受游戏认识的语言代码，避免写错的语言悄悄进到导出结果里。
            string? language = GameLanguages.Normalize(key);
            if (language is not null)
            {
                result[language] = reader.TokenType == JsonTokenType.Null ? string.Empty : reader.GetString() ?? string.Empty;
            }
            else if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
            {
                reader.Skip();
            }
        }

        return result;
    }

    public override void Write(Utf8JsonWriter writer, LocalizedText value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        // 按语言代码排序，输出稳定，便于比较与审阅。
        foreach (string language in value.Languages.OrderBy(l => l, StringComparer.Ordinal))
        {
            writer.WriteString(language, value[language]);
        }

        writer.WriteEndObject();
    }
}
