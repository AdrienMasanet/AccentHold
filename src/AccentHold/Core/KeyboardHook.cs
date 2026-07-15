using System.Runtime.InteropServices;

namespace AccentHold.Core;

// Thin WH_KEYBOARD_LL wrapper; Callback returns true to swallow the event.
internal sealed class KeyboardHook : IDisposable
{
    private readonly Native.HookProc _proc;
    private nint _handle;

    public Func<int, bool, bool>? Callback { get; set; }

    public KeyboardHook() => _proc = HookProc;

    public void Install()
    {
        if (_handle != 0) return;
        _handle = Native.SetWindowsHookExW(Native.WH_KEYBOARD_LL, _proc, 0, 0);
        if (_handle == 0) throw new InvalidOperationException($"SetWindowsHookEx failed ({Marshal.GetLastWin32Error()})");
    }

    private nint HookProc(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0 && Callback is { } cb)
        {
            var data = Marshal.PtrToStructure<Native.KBDLLHOOKSTRUCT>(lParam);
            // Ignore injected input (including our own) entirely.
            if ((data.flags & Native.LLKHF_INJECTED) == 0)
            {
                var isDown = wParam is Native.WM_KEYDOWN or Native.WM_SYSKEYDOWN;
                var isUp = wParam is Native.WM_KEYUP or Native.WM_SYSKEYUP;
                if ((isDown || isUp) && cb((int)data.vkCode, isDown)) return 1;
            }
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
