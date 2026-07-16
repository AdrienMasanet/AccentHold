using System.Globalization;

namespace AccentHold.Core;

// Minimal, dependency-free INI reader: [sections], key = value, ';' or '#' comments.
// Keys are case-SENSITIVE: the accent table has distinct 'a' and 'A' entries.
internal sealed class Ini
{
    private readonly Dictionary<string, Dictionary<string, string>> _sections = new(StringComparer.OrdinalIgnoreCase);

    public static Ini Parse(string text)
    {
        var ini = new Ini();
        var current = "";
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] is ';' or '#') continue;
            if (line[0] == '[' && line[^1] == ']')
            {
                current = line[1..^1].Trim();
                continue;
            }
            // Search from index 1 so '=' itself can be used as a key (e.g. "= = ≠ ≈").
            var eq = line.IndexOf('=', 1);
            if (eq <= 0) continue;
            var key = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim();
            if (!ini._sections.TryGetValue(current, out var s)) ini._sections[current] = s = new(StringComparer.Ordinal);
            s[key] = value;
        }
        return ini;
    }

    public IEnumerable<(string Key, string Value)> Section(string section) =>
        _sections.TryGetValue(section, out var s) ? s.Select(kv => (kv.Key, kv.Value)) : [];

    private string? Get(string section, string key) =>
        _sections.TryGetValue(section, out var s) && s.TryGetValue(key, out var v) ? v : null;

    public int GetInt(string section, string key, int fallback) =>
        int.TryParse(Get(section, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    public double GetDouble(string section, string key, double fallback) =>
        double.TryParse(Get(section, key), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;
}
