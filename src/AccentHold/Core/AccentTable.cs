namespace AccentHold.Core;

// macOS press-and-hold accent variants, keyed by base letter, with optional user overrides merged in.
internal sealed class AccentTable
{
    private static readonly Dictionary<char, string[]> DefaultLower = new()
    {
        ['a'] = ["à", "á", "â", "ä", "æ", "ã", "å", "ā"],
        ['c'] = ["ç", "ć", "č"],
        ['e'] = ["è", "é", "ê", "ë", "ē", "ė", "ę"],
        ['i'] = ["î", "ï", "í", "ī", "į", "ì"],
        ['l'] = ["ł"],
        ['n'] = ["ñ", "ń"],
        ['o'] = ["ô", "ö", "ò", "ó", "œ", "ø", "ō", "õ"],
        ['s'] = ["ß", "ś", "š"],
        ['u'] = ["û", "ü", "ù", "ú", "ū"],
        ['y'] = ["ÿ"],
        ['z'] = ["ž", "ź", "ż"],
    };

    private static readonly Dictionary<char, string[]> DefaultUpper = new()
    {
        ['a'] = ["À", "Á", "Â", "Ä", "Æ", "Ã", "Å", "Ā"],
        ['c'] = ["Ç", "Ć", "Č"],
        ['e'] = ["È", "É", "Ê", "Ë", "Ē", "Ė", "Ę"],
        ['i'] = ["Î", "Ï", "Í", "Ī", "Į", "Ì"],
        ['l'] = ["Ł"],
        ['n'] = ["Ñ", "Ń"],
        ['o'] = ["Ô", "Ö", "Ò", "Ó", "Œ", "Ø", "Ō", "Õ"],
        ['s'] = ["ẞ", "Ś", "Š"],
        ['u'] = ["Û", "Ü", "Ù", "Ú", "Ū"],
        ['y'] = ["Ÿ"],
        ['z'] = ["Ž", "Ź", "Ż"],
    };

    private readonly Dictionary<char, string[]> _lower;
    private readonly Dictionary<char, string[]> _upper;

    public AccentTable(IReadOnlyDictionary<char, string[]>? overrides = null)
    {
        _lower = new Dictionary<char, string[]>(DefaultLower);
        _upper = new Dictionary<char, string[]>(DefaultUpper);
        if (overrides is null) return;
        foreach (var (key, variants) in overrides)
        {
            _lower[key] = variants;
            _upper[key] = Array.ConvertAll(variants, v => v.ToUpperInvariant());
        }
    }

    public bool TryGetVariants(char baseLower, bool upper, out string[] variants) =>
        (upper ? _upper : _lower).TryGetValue(baseLower, out variants!);

    public bool Contains(char baseLower) => _lower.ContainsKey(baseLower);
}
