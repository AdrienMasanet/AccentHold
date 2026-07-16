using System.IO;
using System.Text;

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
    // Full accent table parsed from the [accents] section (defaults if the section is empty).
    public IReadOnlyDictionary<char, string[]> Accents { get; private set; } = AccentTable.Defaults;

    public event Action? Changed;

    private readonly FileSystemWatcher _watcher;
    private DateTime _lastLoad;

    public Settings()
    {
        Directory.CreateDirectory(Dir);
        if (!File.Exists(Path)) File.WriteAllText(Path, BuildDefaultIni());
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

        var accents = new Dictionary<char, string[]>();
        foreach (var (key, value) in ini.Section("accents"))
        {
            if (key.Length != 1) continue;
            var variants = value.Split([' ', ',', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (variants.Length > 0) accents[key[0]] = variants;
        }
        Accents = accents.Count > 0 ? accents : AccentTable.Defaults;
    }

    public void ResetToDefaults()
    {
        File.WriteAllText(Path, BuildDefaultIni());
        try { Load(); Changed?.Invoke(); } catch { }
    }

    public void OpenInEditor()
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Path) { UseShellExecute = true });
    }

    public void Dispose() => _watcher.Dispose();

    private static string BuildDefaultIni()
    {
        var sb = new StringBuilder();
        sb.AppendLine("; AccentHold configuration. Saved changes apply instantly, no restart needed.");
        sb.AppendLine();
        sb.AppendLine("[general]");
        sb.AppendLine("; Delay in milliseconds before the accent menu appears while holding a key (50-2000).");
        sb.AppendLine("hold_delay_ms = 180");
        sb.AppendLine();
        sb.AppendLine("; Menu size multiplier: 1.0 is default, 1.5 is larger, 0.8 is smaller (0.7-2.5).");
        sb.AppendLine("scale = 1.0");
        sb.AppendLine();
        sb.AppendLine("[accents]");
        sb.AppendLine("; The full accent table, entirely yours to edit. One base character per line,");
        sb.AppendLine("; variants separated by spaces, shown in that order. The letter defaults are");
        sb.AppendLine("; taken verbatim from macOS press-and-hold (upper and lower case have separate");
        sb.AppendLine("; lines because macOS orders them differently), followed by iOS-style extra");
        sb.AppendLine("; sets for digits and punctuation. Add, edit or delete any line freely.");
        var separatorEmitted = false;
        foreach (var (key, variants) in AccentTable.Defaults)
        {
            if (!separatorEmitted && !char.IsLetter(key))
            {
                sb.AppendLine();
                separatorEmitted = true;
            }
            sb.AppendLine($"{key} = {string.Join(' ', variants)}");
        }
        return sb.ToString();
    }
}
