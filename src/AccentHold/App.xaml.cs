using System.IO;
using System.Windows;
using AccentHold.Core;
using AccentHold.UI;

namespace AccentHold;

public partial class App : Application
{
    private Mutex? _mutex;
    private Settings? _settings;
    private HoldController? _controller;
    private TrayIcon? _tray;
    private AccentPopup? _popup;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, ex) => LogFatal(ex.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, ex) => LogFatal(ex.ExceptionObject as Exception);

        _mutex = new Mutex(true, @"Local\AccentHold.SingleInstance", out var isFirst);
        if (!isFirst)
        {
            Shutdown();
            return;
        }

        _settings = new Settings();
        _controller = new HoldController(Dispatcher, PopupFactory, _settings);

        if (e.Args.Contains("--demo"))
        {
            RunDemo();
            return;
        }

        _controller.Install();
        _tray = new TrayIcon(_controller, _settings, Shutdown);
    }

    private AccentPopup PopupFactory() => _popup ??= new AccentPopup();

    // Visual smoke test: cycles the popup at a fixed on-screen point to exercise the reshow path.
    private void RunDemo()
    {
        var table = new AccentTable();
        var letters = new[] { 'e', 'a', 'o', 'u', 'c' };
        var screen = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;
        var caret = new Native.RECT
        {
            Left = screen.Width / 2,
            Top = screen.Height / 2,
            Right = screen.Width / 2 + 2,
            Bottom = screen.Height / 2 + 24,
        };
        var i = 0;
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
        timer.Tick += (_, _) =>
        {
            if (i >= letters.Length) { Shutdown(); return; }
            table.TryGetVariants(letters[i], upper: false, out var variants);
            PopupFactory().ShowAt(caret, approximate: false, variants, _settings!.Scale, _ => { });
            PopupFactory().SetSelection(1);
            i++;
        };
        timer.Start();
    }

    // Background app: log crashes instead of showing a Watson dialog, then die cleanly.
    private static void LogFatal(Exception? ex)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AccentHold");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "error.log"), $"[{DateTime.Now:O}] {ex}\n");
        }
        catch { }
        Environment.Exit(1);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _controller?.Dispose();
        _tray?.Dispose();
        _settings?.Dispose();
        _popup?.Close();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
