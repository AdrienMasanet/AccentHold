using System.Windows.Automation;
using System.Windows.Automation.Text;
using Accessibility;

namespace AccentHold.Core;

// Locates the text caret in the foreground app (screen pixels).
// Strategy: Win32/MSAA caret on the foreground thread -> UI Automation (text selection,
// then the focused element's real window, which may belong to another thread or even
// another process in WebView2/Electron apps) -> focused text control bounds.
// Returning false means "no active text input", in which case no popup is shown.
internal static class CaretLocator
{
    private static Guid _iidIAccessible = new("618736E0-3C3D-11CF-810C-00AA00389B71");

    // Shell experiences (Start menu, Search) render in a window band above anything an
    // application can reach, so a popup overlapping them would be invisible. Their window
    // rect is reported so the popup can be placed just outside it and stay visible.
    private static readonly HashSet<string> ShellOverlayProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "SearchHost", "SearchApp", "SearchUI", "StartMenuExperienceHost", "ShellExperienceHost", "LockApp",
    };

    public static bool TryGetShellOverlayRect(out Native.RECT rect)
    {
        rect = default;
        try
        {
            var fg = Native.GetForegroundWindow();
            if (fg == 0) return false;
            Native.GetWindowThreadProcessId(fg, out uint pid);
            if (pid == 0) return false;
            using var p = System.Diagnostics.Process.GetProcessById((int)pid);
            return ShellOverlayProcesses.Contains(p.ProcessName) && Native.GetWindowRect(fg, out rect);
        }
        catch { return false; }
    }

    public static bool TryLocate(out Native.RECT rectPx, out bool approximate)
    {
        rectPx = default;
        approximate = false;
        if (Native.GetForegroundWindow() == 0) return false;

        // Fast path: a classic Win32 caret owned by the foreground thread.
        if (TryThreadCaret(0, ref rectPx, out var focusHwnd, depth: 0)) return true;
        // UIA pass; touching the focused element also wakes Chromium's lazy accessibility.
        if (TryUiAutomation(ref rectPx, ref approximate)) return true;
        // WinUI-hosted web views (WhatsApp and friends) expose their text world under the
        // focused Win32 window, disconnected from the UIA focus chain.
        if (TryFocusWindowText(focusHwnd, ref rectPx)) return true;
        // Chromium (browsers, WebView2 apps, Electron) builds its accessibility tree
        // asynchronously after first contact; give it one beat and try again.
        Thread.Sleep(90);
        if (TryUiAutomation(ref rectPx, ref approximate)) return true;
        return TryFocusWindowText(focusHwnd, ref rectPx);
    }

    // Pre-touches UIA for the foreground app so Chromium-based apps have their
    // accessibility tree ready by the time the user holds a key.
    public static void WarmUp()
    {
        try
        {
            _ = AutomationElement.FocusedElement;
            var gui = new Native.GUITHREADINFO { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Native.GUITHREADINFO>() };
            if (Native.GetGUIThreadInfo(0, ref gui) && gui.hwndFocus != 0)
                _ = AutomationElement.FromHandle(gui.hwndFocus).Current.ControlType;
        }
        catch { }
    }

    // Win32 caret and MSAA caret for one thread (0 = foreground thread); follows the
    // focus window onto another thread once (WebView2 renderers live out of process).
    private static bool TryThreadCaret(uint tid, ref Native.RECT rectPx, out nint focusHwnd, int depth)
    {
        focusHwnd = 0;
        var gui = new Native.GUITHREADINFO { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Native.GUITHREADINFO>() };
        if (!Native.GetGUIThreadInfo(tid, ref gui)) return false;
        focusHwnd = gui.hwndFocus;
        if (gui.hwndCaret != 0)
        {
            var rc = gui.rcCaret;
            if (rc.Bottom - rc.Top > 0)
            {
                Native.MapWindowPoints(gui.hwndCaret, 0, ref rc, 2);
                rectPx = rc;
                return true;
            }
        }
        if (gui.hwndFocus != 0 && depth < 2)
        {
            var focusTid = Native.GetWindowThreadProcessId(gui.hwndFocus, 0);
            if (focusTid != 0 && focusTid != tid && TryThreadCaret(focusTid, ref rectPx, out _, depth + 1))
                return true;
        }
        return TryMsaaCaret(gui.hwndFocus != 0 ? gui.hwndFocus : Native.GetForegroundWindow(), ref rectPx);
    }

    // A WinUI input-site or WebView2 window hides the web content's accessibility under
    // its own HWND: ask the first document/edit there where its caret selection is.
    private static bool TryFocusWindowText(nint hwnd, ref Native.RECT rectPx)
    {
        try
        {
            if (hwnd == 0) return false;
            var root = AutomationElement.FromHandle(hwnd);
            var editor = root.FindFirst(TreeScope.Subtree, new OrCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Document),
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit)));
            if (editor is null) return false;
            if (editor.TryGetCurrentPattern(TextPattern.Pattern, out var pattern) && pattern is TextPattern tp)
            {
                var sel = tp.GetSelection();
                if (sel is { Length: > 0 } && TryRangeRect(sel[^1], ref rectPx)) return true;
            }
            return false;
        }
        catch { return false; }
    }

    private static bool TryMsaaCaret(nint hwnd, ref Native.RECT rectPx)
    {
        try
        {
            if (hwnd == 0) return false;
            Native.AccessibleObjectFromWindow(hwnd, Native.OBJID_CARET, ref _iidIAccessible, out var obj);
            if (obj is not IAccessible acc) return false;
            acc.accLocation(out var l, out var t, out var w, out var h, 0);
            if (h <= 0) return false;
            rectPx = new Native.RECT { Left = l, Top = t, Right = l + Math.Max(w, 1), Bottom = t + h };
            return true;
        }
        catch { return false; }
    }

    private static bool TryUiAutomation(ref Native.RECT rectPx, ref bool approximate)
    {
        try
        {
            var focused = AutomationElement.FocusedElement;
            if (focused is null) return false;

            // Web apps often focus a leaf node; the text pattern and the control type
            // that identify an editor may sit a few ancestors up.
            AutomationElement? textControl = null;
            var el = focused;
            for (var depth = 0; el is not null && depth < 6; depth++)
            {
                if (el.TryGetCurrentPattern(TextPattern.Pattern, out var pattern) && pattern is TextPattern tp)
                {
                    var sel = tp.GetSelection();
                    if (sel is { Length: > 0 } && TryRangeRect(sel[^1], ref rectPx)) return true;
                    textControl ??= el;
                }
                else if (textControl is null)
                {
                    var ct = el.Current.ControlType;
                    if (ct == ControlType.Edit || ct == ControlType.Document) textControl = el;
                }
                el = TreeWalker.ControlViewWalker.GetParent(el);
            }

            // The caret may live on another thread or process (WebView2, Electron);
            // chase it through the focused element's real window handle.
            var hwnd = NativeWindowOf(focused);
            if (hwnd != 0)
            {
                var tid = Native.GetWindowThreadProcessId(hwnd, 0);
                if (tid != 0 && TryThreadCaret(tid, ref rectPx, out _, depth: 1)) return true;
                if (TryMsaaCaret(hwnd, ref rectPx)) return true;
            }

            // Last resort: a recognizable text control's bounds, placed approximately.
            if (textControl is null) return false;
            var b = textControl.Current.BoundingRectangle;
            if (b.IsEmpty) return false;
            rectPx = new Native.RECT { Left = (int)b.Left, Top = (int)b.Top, Right = (int)b.Right, Bottom = (int)b.Bottom };
            approximate = true;
            return true;
        }
        catch { return false; }
    }

    private static nint NativeWindowOf(AutomationElement? el)
    {
        try
        {
            for (var depth = 0; el is not null && depth < 8; depth++)
            {
                var h = el.Current.NativeWindowHandle;
                if (h != 0) return h;
                el = TreeWalker.ControlViewWalker.GetParent(el);
            }
        }
        catch { }
        return 0;
    }

    private static bool TryRangeRect(TextPatternRange range, ref Native.RECT rectPx)
    {
        var rects = range.GetBoundingRectangles();
        if (rects.Length == 0)
        {
            // Degenerate caret range: widen to the enclosing character to get a rect.
            range = range.Clone();
            range.ExpandToEnclosingUnit(TextUnit.Character);
            rects = range.GetBoundingRectangles();
        }
        if (rects.Length == 0) return false;
        var r = rects[^1];
        if (r.Height <= 0) return false;
        rectPx = new Native.RECT { Left = (int)r.Left, Top = (int)r.Top, Right = (int)r.Right, Bottom = (int)r.Bottom };
        return true;
    }
}
