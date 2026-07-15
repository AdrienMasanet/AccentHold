using System.Runtime.InteropServices;

namespace AccentHold.Core;

// WH_MOUSE_LL wrapper reporting button-down events only; never swallows input.
internal sealed class MouseHook : IDisposable
{
    private readonly Native.HookProc _proc;
    private nint _handle;

    public Action<int, int>? ButtonDown { get; set; }

    public MouseHook() => _proc = HookProc;

    public void Install()
    {
        if (_handle != 0) return;
        _handle = Native.SetWindowsHookExW(Native.WH_MOUSE_LL, _proc, 0, 0);
        if (_handle == 0) throw new InvalidOperationException($"SetWindowsHookEx failed ({Marshal.GetLastWin32Error()})");
    }

    private nint HookProc(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0 && ButtonDown is { } cb &&
            wParam is Native.WM_LBUTTONDOWN or Native.WM_RBUTTONDOWN or Native.WM_MBUTTONDOWN)
        {
            var data = Marshal.PtrToStructure<Native.MSLLHOOKSTRUCT>(lParam);
            cb(data.pt.X, data.pt.Y);
        }
        return Native.CallNextHookEx(_handle, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_handle == 0) return;
        Native.UnhookWindowsHookEx(_handle);
        _handle = 0;
    }
}
