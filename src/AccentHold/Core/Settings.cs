using System.IO;

namespace AccentHold.Core;

// User settings from %APPDATA%\AccentHold\config.ini, created on first run and hot-reloaded on edit.
internal sealed class Settings : IDisposable
{
    public static string Dir => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AccentHold");
    public static string Path => System.IO.Path.Combine(Dir, "config.ini");

    // Delay before the accent menu appears while a key is held.
    public int HoldDelayMs { get; private set; } = 180;
    // Menu size multiplier (1.0 = default).
    public double Scale { get; private set; } = 1.0;
    // Per-letter accent overrides parsed from the [accents] section.
    public IReadOnlyDictionary<char, string[]> AccentOverrides { get; private set; } = new Dictionary<char, string[]>();

    public event Action? Changed;

    private readonly FileSystemWatcher _watcher;
    private DateTime _lastLoad;

    public Settings()
    {
        Directory.CreateDirectory(Dir);
        if (!File.Exists(Path)) File.WriteAllText(Path, DefaultIni);
        Load();

        _watcher = new FileSystemWatcher(Dir, "config.ini")
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true,
        };
        _watcher.Changed += (_, _) => OnFileChanged();
    }

    private void OnFileChanged()
    {
        // Editors fire several events per save; debounce and swallow transient read errors.
        if ((DateTime.UtcNow - _lastLoad).TotalMilliseconds < 250) return;
        try { Load(); Changed?.Invoke(); } catch { }
    }

    private void Load()
    {
        _lastLoad = DateTime.UtcNow;
        var ini = Ini.Parse(File.ReadAllText(Path));

        HoldDelayMs = Math.Clamp(ini.GetInt("general", "hold_delay_ms", 180), 50, 2000);
        Scale = Math.Clamp(ini.GetDouble("general", "scale", 1.0), 0.7, 2.5);

        var overrides = new Dictionary<char, string[]>();
        foreach (var (key, value) in ini.Section("accents"))
        {
            if (key.Length != 1) continue;
            var variants = value.Split([' ', ',', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (variants.Length > 0) overrides[char.ToLowerInvariant(key[0])] = variants;
        }
        AccentOverrides = overrides;
    }

    public void OpenInEditor()
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Path) { UseShellExecute = true });
    }

    public void Dispose() => _watcher.Dispose();

    private const string DefaultIni = """
        ; AccentHold configuration. Saved changes apply instantly, no restart needed.

        [general]
        ; Delay in milliseconds before the accent menu appears while holding a key (50-2000).
        hold_delay_ms = 180

        ; Menu size multiplier: 1.0 is default, 1.5 is larger, 0.8 is smaller (0.7-2.5).
        scale = 1.0

        [accents]
        ; Optional overrides. One base letter per line, variants separated by spaces,
        ; ordered as they should appear. Uppercase variants are derived automatically.
        ; Uncomment and edit to customize, or add letters/symbols of your own:
        ; e = e a c ' i o u

        """;
}
