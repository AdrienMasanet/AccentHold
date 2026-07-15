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
public partial class AccentPopup : Window
{
    // Read by the low-level mouse hook thread to detect clicks outside the popup.
    public static nint InstanceHwnd;

    // Transparent shadow padding around the visible border (must match the Border margin in XAML).
    private const int ShadowMarginDip = 12;

    private readonly ObservableCollection<OptionVm> _options = [];
    private Action<int>? _onClick;
    private nint _hwnd;

    public AccentPopup()
    {
        InitializeComponent();
        Items.ItemsSource = _options;
        new WindowInteropHelper(this).EnsureHandle();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        InstanceHwnd = _hwnd;

        // Never steal focus from the app being typed in, and stay out of Alt+Tab.
        var ex = Native.GetWindowLongPtrW(_hwnd, Native.GWL_EXSTYLE);
        Native.SetWindowLongPtrW(_hwnd, Native.GWL_EXSTYLE, ex | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW);
    }

    internal void ShowAt(Native.RECT caretPx, bool approximate, IReadOnlyList<string> variants, double scale, Action<int> onClick)
    {
        _onClick = onClick;
        ApplyTheme();
        Items.LayoutTransform = scale is > 0.99 and < 1.01 ? null : new ScaleTransform(scale, scale);

        _options.Clear();
        for (var i = 0; i < variants.Count; i++) _options.Add(new OptionVm(variants[i], i));

        BeginAnimation(OpacityProperty, null);
        Opacity = 0;
        if (!IsVisible) Show();
        // Two passes: moving onto a different-DPI monitor can re-scale the content.
        UpdateLayout();
        Position(caretPx, approximate);
        UpdateLayout();
        Position(caretPx, approximate);
        FadeIn();
    }

    public void SetSelection(int index)
    {
        for (var i = 0; i < _options.Count; i++) _options[i].IsSelected = i == index;
    }

    public void HideNow()
    {
        if (!IsVisible) return;
        BeginAnimation(OpacityProperty, null);
        Hide();
    }

    private void Position(Native.RECT caret, bool approximate)
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

        Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, visX - m, visY - m, 0, 0,
            Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
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

    private void OnOptionEnter(object sender, System.Windows.Input.MouseEventArgs e) { if (VmOf(sender) is { } vm) vm.IsHovered = true; }

    private void OnOptionLeave(object sender, System.Windows.Input.MouseEventArgs e) { if (VmOf(sender) is { } vm) vm.IsHovered = false; }

    private void OnOptionClick(object sender, System.Windows.Input.MouseButtonEventArgs e) { if (VmOf(sender) is { } vm) _onClick?.Invoke(vm.Index); }

    // DataContext can momentarily be the disconnected-item placeholder while the list refreshes.
    private static OptionVm? VmOf(object sender) => (sender as FrameworkElement)?.DataContext as OptionVm;
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
