using System.Text.Json;

namespace ModForge.Core.Pck;

/// <summary>
/// Reads the game's localization tables straight out of a PCK.
///
/// Tables are stored as uncompressed UTF-8 JSON at
/// "localization/&lt;lang&gt;/&lt;table&gt;.json", which lets an editor show real
/// display names for vanilla content without launching the game.
/// </summary>
public sealed class PckLocReader
{
    private readonly Dictionary<string, byte[]> _entries;

    public PckLocReader(string pckPath)
    {
        _entries = PckFile.Read(pckPath);
    }

    public IReadOnlyCollection<string> EntryNames => _entries.Keys;

    /// <summary>
    /// Loads one table, e.g. ("eng", "cards"). Returns an empty map when absent.
    /// </summary>
    public IReadOnlyDictionary<string, string> LoadTable(string language, string table)
    {
        string wanted = $"localization/{language}/{table}.json";
        if (!_entries.TryGetValue(wanted, out byte[]? raw))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        return ParseTable(raw);
    }

    public static IReadOnlyDictionary<string, string> ParseTable(byte[] raw)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        using var doc = JsonDocument.Parse(raw);

        if (doc.RootElement.ValueKind != JsonValueKind.Object)
        {
            return result;
        }

        foreach (JsonProperty prop in doc.RootElement.EnumerateObject())
        {
            if (prop.Value.ValueKind == JsonValueKind.String)
            {
                result[prop.Name] = prop.Value.GetString() ?? string.Empty;
            }
        }

        return result;
    }
}
