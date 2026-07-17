using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using AccentHold.Core;
using Microsoft.Win32;

namespace AccentHold.UI;

// Accent chooser popup: translucent Win11-style flyout, never takes focus, positioned near the caret.
// The window is created once and stays shown forever; "hidden" means opacity 0 + click-through.
// Hiding/showing the HWND would let the OS flash the last rendered frame at the old position.
public partial class AccentPopup : Window
{
    // Read by the low-level mouse hook thread to detect clicks outside the popup.
    public static nint InstanceHwnd;

    // Transparent shadow padding around the visible border (must match the Border margin in XAML).
    private const int ShadowMarginDip = 12;

    private readonly ObservableCollection<OptionVm> _options = [];
    private Action<int>? _onClick;
    private nint _hwnd;
    private bool _leaveTracked;

    public AccentPopup()
    {
        InitializeComponent();
        Items.ItemsSource = _options;
        new WindowInteropHelper(this).EnsureHandle();
        Show();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        InstanceHwnd = _hwnd;

        // Never steal focus, stay out of Alt+Tab, and start click-through while invisible.
        var ex = Native.GetWindowLongPtrW(_hwnd, Native.GWL_EXSTYLE);
        Native.SetWindowLongPtrW(_hwnd, Native.GWL_EXSTYLE,
            ex | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW | Native.WS_EX_TRANSPARENT);

        // Mouse input is handled from raw window messages: WPF's routed mouse events
        // are unreliable on a never-activated layered window (the first click is lost).
        HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        switch (msg)
        {
            // WPF activates the window on its very first click despite WS_EX_NOACTIVATE,
            // which stole the target app's focus; refuse activation at the source.
            case Native.WM_MOUSEACTIVATE:
                handled = true;
                return Native.MA_NOACTIVATE;
            case Native.WM_MOUSEMOVE:
                TrackLeave();
                SetHover(ItemIndexAt(lParam));
                break;
            case Native.WM_MOUSELEAVE:
                _leaveTracked = false;
                SetHover(null);
                break;
            case Native.WM_LBUTTONUP:
                if (ItemIndexAt(lParam) is { } index)
                {
                    handled = true;
                    _onClick?.Invoke(index);
                }
                break;
        }
        return 0;
    }

    // Maps client-pixel coordinates from a mouse message to the option under them.
    private int? ItemIndexAt(nint lParam)
    {
        var x = (short)((long)lParam & 0xFFFF);
        var y = (short)(((long)lParam >> 16) & 0xFFFF);
        if (PresentationSource.FromVisual(this)?.CompositionTarget is not { } target) return null;
        var pt = target.TransformFromDevice.Transform(new Point(x, y));
        int? found = null;
        VisualTreeHelper.HitTest(this, null, result =>
        {
            if ((result.VisualHit as FrameworkElement)?.DataContext is OptionVm vm)
            {
                found = vm.Index;
                return HitTestResultBehavior.Stop;
            }
            return HitTestResultBehavior.Continue;
        }, new PointHitTestParameters(pt));
        return found;
    }

    private void SetHover(int? index)
    {
        for (var i = 0; i < _options.Count; i++) _options[i].IsHovered = i == index;
    }

    private void TrackLeave()
    {
        if (_leaveTracked) return;
        _leaveTracked = true;
        var tme = new Native.TRACKMOUSEEVENT
        {
            cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Native.TRACKMOUSEEVENT>(),
            dwFlags = Native.TME_LEAVE,
            hwndTrack = _hwnd,
        };
        Native.TrackMouseEvent(ref tme);
    }

    internal void ShowAt(Native.RECT caretPx, bool approximate, IReadOnlyList<string> variants, double scale,
        Native.RECT? avoid, Action<int> onClick)
    {
        _onClick = onClick;
        ApplyTheme();
        Items.LayoutTransform = scale is > 0.99 and < 1.01 ? null : new ScaleTransform(scale, scale);

        _options.Clear();
        for (var i = 0; i < variants.Count; i++) _options.Add(new OptionVm(variants[i], i));

        // Lay out and move while fully transparent, so the first visible frame is final.
        BeginAnimation(OpacityProperty, null);
        Opacity = 0;
        // Two passes: moving onto a different-DPI monitor can re-scale the content.
        UpdateLayout();
        Position(caretPx, approximate, avoid);
        UpdateLayout();
        Position(caretPx, approximate, avoid);
        SetClickThrough(false);
        FadeIn();
    }

    public void SetSelection(int index)
    {
        for (var i = 0; i < _options.Count; i++) _options[i].IsSelected = i == index;
    }

    public void HideNow()
    {
        BeginAnimation(OpacityProperty, null);
        Opacity = 0;
        SetClickThrough(true);
        SetHover(null);
    }

    private void SetClickThrough(bool enabled)
    {
        var ex = Native.GetWindowLongPtrW(_hwnd, Native.GWL_EXSTYLE);
        var wanted = enabled ? ex | Native.WS_EX_TRANSPARENT : ex & ~(nint)Native.WS_EX_TRANSPARENT;
        if (wanted != ex) Native.SetWindowLongPtrW(_hwnd, Native.GWL_EXSTYLE, wanted);
    }

    private void Position(Native.RECT caret, bool approximate, Native.RECT? avoid)
    {
        var center = new Native.POINT { X = (caret.Left + caret.Right) / 2, Y = (caret.Top + caret.Bottom) / 2 };
        var monitor = Native.MonitorFromPoint(center, Native.MONITOR_DEFAULTTONEAREST);
        var info = new Native.MONITORINFO { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Native.MONITORINFO>() };
        Native.GetMonitorInfoW(monitor, ref info);
        Native.GetDpiForMonitor(monitor, 0, out var dpi, out _);
        var scale = dpi / 96.0;
        var wa = info.rcWork;

        // The window is larger than the visible border by the shadow margin on every side.
        var w = (int)Math.Ceiling(ActualWidth * scale);
        var h = (int)Math.Ceiling(ActualHeight * scale);
        var m = (int)Math.Round(ShadowMarginDip * scale);
        var gap = (int)(7 * scale);
        var visW = w - 2 * m;
        var visH = h - 2 * m;

        // Place the visible box above the caret line so the text stays readable; below if no room.
        var visX = approximate ? caret.Left + gap : caret.Left - (int)(12 * scale);
        var visY = caret.Top - gap - visH;
        if (visY < wa.Top + gap) visY = caret.Bottom + gap;
        visX = Math.Clamp(visX, wa.Left + gap, Math.Max(wa.Left + gap, wa.Right - visW - gap));
        visY = Math.Clamp(visY, wa.Top + gap, Math.Max(wa.Top + gap, wa.Bottom - visH - gap));

        // Shell overlays (Start menu, Search) sit in a window band no app can draw over,
        // so slide out of their window rect; if the overlay fills the screen, stay put.
        if (avoid is { } a && visX < a.Right && visX + visW > a.Left && visY < a.Bottom && visY + visH > a.Top)
        {
            var above = a.Top - gap - visH;
            var below = a.Bottom + gap;
            if (above >= wa.Top + gap) visY = above;
            else if (below + visH <= wa.Bottom - gap) visY = below;
        }

        Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, visX - m, visY - m, 0, 0,
            Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }

    private void FadeIn()
    {
        var anim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(130)) { EasingFunction = new QuadraticEase() };
        BeginAnimation(OpacityProperty, anim);
    }

    private void ApplyTheme()
    {
        var dark = IsSystemDarkTheme();
        Resources["SurfaceBrush"] = Frozen(dark ? Color.FromArgb(0xF2, 0x2B, 0x2B, 0x2B) : Color.FromArgb(0xF2, 0xF9, 0xF9, 0xF9));
        Resources["BorderBrush"] = Frozen(dark ? Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x18, 0x00, 0x00, 0x00));
        Resources["ForegroundBrush"] = Frozen(dark ? Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0xE6, 0x00, 0x00, 0x00));
        Resources["SubtleBrush"] = Frozen(dark ? Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x8C, 0x00, 0x00, 0x00));
        Resources["HoverBrush"] = Frozen(dark ? Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x17, 0x00, 0x00, 0x00));
        Resources["AccentBrush"] = Frozen(GetAccentColor());
    }

    private static bool IsSystemDarkTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
    }

    private static Color GetAccentColor()
    {
        try
        {
            var c = new Windows.UI.ViewManagement.UISettings().GetColorValue(Windows.UI.ViewManagement.UIColorType.Accent);
            return Color.FromArgb(c.A, c.R, c.G, c.B);
        }
        catch { return Color.FromRgb(0x00, 0x78, 0xD4); }
    }

    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}

// Row item: one accent variant with its numeric shortcut.
public sealed class OptionVm(string ch, int index) : INotifyPropertyChanged
{
    public string Char { get; } = ch;
    public int Index { get; } = index;
    public string Digit { get; } = (index + 1).ToString();

    private bool _isSelected;
    private bool _isHovered;

    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
    public bool IsHovered { get => _isHovered; set => Set(ref _isHovered, value); }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set(ref bool field, bool value, [CallerMemberName] string? name = null)
    {
        if (field == value) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
