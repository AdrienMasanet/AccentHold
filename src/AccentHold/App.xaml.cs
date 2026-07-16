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

        _settings = new Settings();

        // Demo mode installs no hooks and exits on its own; it may run beside the real instance.
        if (e.Args.Contains("--demo"))
        {
            RunDemo();
            return;
        }

        _mutex = new Mutex(true, @"Local\AccentHold.SingleInstance", out var isFirst);
        if (!isFirst)
        {
            Shutdown();
            return;
        }

        _controller = new HoldController(Dispatcher, PopupFactory, _settings);
        _controller.Install();
        _tray = new TrayIcon(_controller, _settings, Shutdown);
    }

    private AccentPopup PopupFactory() => _popup ??= new AccentPopup();

    // Visual smoke test: cycles the popup between two screen spots, with a hide in between,
    // to exercise the exact hide/reposition/reshow path used while typing.
    private void RunDemo()
    {
        var table = new AccentTable();
        var letters = new[] { 'e', 'a', 'o', 'u', 'c' };
        var screen = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;
        var i = 0;
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(750) };
        timer.Tick += (_, _) =>
        {
            if (i >= letters.Length * 2) { Shutdown(); return; }
            if (i % 2 == 1)
            {
                PopupFactory().HideNow();
            }
            else
            {
                table.TryGetVariants(letters[i / 2], upper: false, out var variants);
                var x = screen.Width / 2 + (i % 4 == 0 ? -280 : 280);
                var caret = new Native.RECT { Left = x, Top = screen.Height / 2, Right = x + 2, Bottom = screen.Height / 2 + 24 };
                PopupFactory().ShowAt(caret, approximate: false, variants, _settings!.Scale, avoid: null, _ => { });
                PopupFactory().SetSelection(1);
            }
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
