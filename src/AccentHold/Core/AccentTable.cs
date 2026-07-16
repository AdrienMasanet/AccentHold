namespace AccentHold.Core;

// Accent variants keyed by base character. The live table comes from config.ini; the
// defaults are transcribed verbatim from the macOS press-and-hold tables, which is why
// upper and lower case are stored separately: Apple's lists differ per case
// (e.g. s = ß ş ș ś š but S = ẞ Ś Š Ş Ș, and i has ı where I has İ).
internal sealed class AccentTable
{
    public static readonly IReadOnlyDictionary<char, string[]> Defaults = new Dictionary<char, string[]>()
    {
        ['a'] = ["à", "á", "â", "ä", "ǎ", "æ", "ã", "å", "ā", "ă", "ą"],
        ['A'] = ["À", "Á", "Â", "Ä", "Ǎ", "Æ", "Ã", "Å", "Ā", "Ă", "Ą"],
        ['c'] = ["ç", "ć", "č", "ċ"],
        ['C'] = ["Ç", "Ć", "Č", "Ċ"],
        ['d'] = ["ď", "ð"],
        ['D'] = ["Ď", "Ð"],
        ['e'] = ["è", "é", "ê", "ë", "ě", "ẽ", "ē", "ė", "ę"],
        ['E'] = ["È", "É", "Ê", "Ë", "Ě", "Ẽ", "Ē", "Ė", "Ę"],
        ['g'] = ["ğ", "ġ"],
        ['G'] = ["Ğ", "Ġ"],
        ['h'] = ["ħ"],
        ['H'] = ["Ħ"],
        ['i'] = ["ì", "í", "î", "ï", "ǐ", "ĩ", "ī", "ı", "į"],
        ['I'] = ["Ì", "Í", "Î", "Ï", "Ǐ", "Ĩ", "Ī", "İ", "Į"],
        ['k'] = ["ķ"],
        ['K'] = ["Ķ"],
        ['l'] = ["ł", "ļ", "ľ"],
        ['L'] = ["Ł", "Ļ", "Ľ"],
        ['n'] = ["ñ", "ń", "ņ", "ň"],
        ['N'] = ["Ñ", "Ń", "Ņ", "Ň"],
        ['o'] = ["ò", "ó", "ô", "ö", "ǒ", "œ", "ø", "õ", "ō", "ő"],
        ['O'] = ["Ò", "Ó", "Ô", "Ö", "Ǒ", "Œ", "Ø", "Õ", "Ō", "Ő"],
        ['r'] = ["ř"],
        ['R'] = ["Ř"],
        ['s'] = ["ß", "ş", "ș", "ś", "š"],
        ['S'] = ["ẞ", "Ś", "Š", "Ş", "Ș"],
        ['t'] = ["ț", "ť", "þ"],
        ['T'] = ["Ț", "Ť", "Þ"],
        ['u'] = ["ù", "ú", "û", "ü", "ǔ", "ũ", "ū", "ű", "ů", "ų"],
        ['U'] = ["Ù", "Ú", "Û", "Ü", "Ǔ", "Ũ", "Ū", "Ű", "Ů", "Ų"],
        ['w'] = ["ŵ"],
        ['W'] = ["Ŵ"],
        ['y'] = ["ý", "ŷ", "ÿ"],
        ['Y'] = ["Ý", "Ŷ", "Ÿ"],
        ['z'] = ["ź", "ž", "ż"],
        ['Z'] = ["Ź", "Ž", "Ż"],
        // Extra sets in the spirit of the iOS keyboard, enabled out of the box.
        ['0'] = ["°"],
        ['-'] = ["–", "—", "•"],
        ['/'] = ["÷"],
        ['?'] = ["¿"],
        ['!'] = ["¡"],
        ['$'] = ["€", "£", "¥", "¢", "₽", "₩"],
        ['%'] = ["‰"],
        ['='] = ["≠", "≈"],
        ['&'] = ["§"],
    };

    private readonly Dictionary<char, string[]> _map;

    public AccentTable(IReadOnlyDictionary<char, string[]>? map = null) =>
        _map = new Dictionary<char, string[]>(map is { Count: > 0 } ? map : Defaults);

    // Exact entry first; an uppercase char without its own entry falls back to the
    // lowercase one, uppercased (covers user-added lowercase-only custom lines).
    public bool TryGetVariants(char typed, out string[] variants)
    {
        if (_map.TryGetValue(typed, out variants!)) return true;
        var lower = char.ToLowerInvariant(typed);
        if (typed != lower && _map.TryGetValue(lower, out var fromLower))
        {
            variants = Array.ConvertAll(fromLower, Upper);
            return true;
        }
        return false;
    }

    // ToUpperInvariant would expand ß to SS; macOS shows the capital sharp s instead.
    private static string Upper(string s) => s == "ß" ? "ẞ" : s.ToUpperInvariant();
}
