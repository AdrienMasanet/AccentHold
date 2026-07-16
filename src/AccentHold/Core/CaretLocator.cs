using System.Windows.Automation;
using System.Windows.Automation.Text;
using Accessibility;

namespace AccentHold.Core;

// Finds the foreground app's caret in screen pixels (Win32 -> MSAA -> UIA); false means no text input, so no popup.
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

        var fg = Native.GetForegroundWindow();
        if (fg == 0) return false;
        var tid = Native.GetWindowThreadProcessId(fg, 0);
        var gui = new Native.GUITHREADINFO { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Native.GUITHREADINFO>() };
        Native.GetGUIThreadInfo(tid, ref gui);

        if (TryWin32Caret(gui, ref rectPx)) return true;
        if (TryMsaaCaret(gui.hwndFocus != 0 ? gui.hwndFocus : fg, ref rectPx)) return true;
        return TryUiAutomation(ref rectPx, ref approximate);
    }

    private static bool TryWin32Caret(Native.GUITHREADINFO gui, ref Native.RECT rectPx)
    {
        if (gui.hwndCaret == 0) return false;
        var rc = gui.rcCaret;
        if (rc.Bottom - rc.Top <= 0) return false;
        Native.MapWindowPoints(gui.hwndCaret, 0, ref rc, 2);
        rectPx = rc;
        return true;
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
            var el = AutomationElement.FocusedElement;
            if (el is null) return false;

            if (el.TryGetCurrentPattern(TextPattern.Pattern, out var pattern) && pattern is TextPattern tp)
            {
                var sel = tp.GetSelection();
                if (sel is { Length: > 0 } && TryRangeRect(sel[^1], ref rectPx)) return true;
            }

            // Fallback: known text control in focus, position relative to its bounds.
            var ct = el.Current.ControlType;
            if (ct != ControlType.Edit && ct != ControlType.Document) return false;
            var b = el.Current.BoundingRectangle;
            if (b.IsEmpty) return false;
            rectPx = new Native.RECT { Left = (int)b.Left, Top = (int)b.Top, Right = (int)b.Right, Bottom = (int)b.Bottom };
            approximate = true;
            return true;
        }
        catch { return false; }
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
