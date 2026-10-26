using ModForge.Core.Pck;

// 一次性探针：把游戏本地化表里几张卡的描述打出来，看原版措辞用的是哪一侧。
//
// 用法: locprobe <pck路径> <表> <语言> <要查的键片段…>

string pck = args[0];
string table = args.Length > 1 ? args[1] : "cards";
string language = args.Length > 2 ? args[2] : "eng";

var reader = new PckLocReader(pck);
IReadOnlyDictionary<string, string> entries = reader.LoadTable(language, table);

Console.WriteLine($"表 {table} / {language}：{entries.Count} 条");

foreach (string key in entries.Keys.Where(k => k.EndsWith(".description", StringComparison.OrdinalIgnoreCase)))
{
    string id = key[..key.IndexOf('.')];

    if (args.Length > 3 && !args.Skip(3).Any(a => id.Contains(a, StringComparison.OrdinalIgnoreCase)))
    {
        continue;
    }

    if (args.Length > 3)
    {
        Console.WriteLine($"{id}: {entries[key]}");
    }
}

// 再按名字查一遍：这几张卡的 id 未必叫 Dualcast。
if (args.Length > 3)
{
    foreach (string key in entries.Keys)
    {
        string value = entries[key];

        if (args.Skip(3).Any(a =>
                (key.Contains(a, StringComparison.OrdinalIgnoreCase) ||
                 value.Contains(a, StringComparison.OrdinalIgnoreCase)) &&
                key.EndsWith(".description", StringComparison.OrdinalIgnoreCase)))
        {
            Console.WriteLine($"[命中] {key}: {value}");
        }
    }
}
