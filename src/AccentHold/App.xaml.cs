using System.IO;
using System.Windows;
using AccentHold.Core;
using AccentHold.UI;

namespace AccentHold;

public partial class App : Application
{
    private Mutex? _mutex;
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

        _controller = new HoldController(Dispatcher, PopupFactory);

        if (e.Args.Contains("--demo"))
        {
            RunDemo();
            return;
        }

        _controller.Install();
        _tray = new TrayIcon(_controller, Shutdown);
    }

    private AccentPopup PopupFactory() => _popup ??= new AccentPopup();

    // Visual smoke test: shows the popup near the mouse cursor for a few seconds.
    private void RunDemo()
    {
        AccentMap.TryGetVariants('e', upper: false, out var variants);
        var p = System.Windows.Forms.Cursor.Position;
        var caret = new Native.RECT { Left = p.X, Top = p.Y, Right = p.X + 2, Bottom = p.Y + 20 };
        PopupFactory().ShowAt(caret, approximate: false, variants, _ => { });
        PopupFactory().SetSelection(1);
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        timer.Tick += (_, _) => Shutdown();
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
        _popup?.Close();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
