namespace AccentHold.Core;

// macOS press-and-hold accent variants (ABC layout), keyed by base letter.
internal static class AccentMap
{
    private static readonly Dictionary<char, string[]> Lower = new()
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

    private static readonly Dictionary<char, string[]> Upper = new()
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

    public static bool TryGetVariants(char baseLower, bool upper, out string[] variants) =>
        (upper ? Upper : Lower).TryGetValue(baseLower, out variants!);

    public static bool Contains(char baseLower) => Lower.ContainsKey(baseLower);
}
