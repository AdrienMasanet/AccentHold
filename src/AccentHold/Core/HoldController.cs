using System.Windows.Threading;
using AccentHold.UI;

namespace AccentHold.Core;

// State machine driving the press-and-hold flow, fed by the low-level hooks.
// Hook callbacks stay cheap; caret lookup runs on a worker, UI work on the dispatcher.
internal sealed class HoldController : IDisposable
{
    private enum State { Idle, Priming, Pending, Suppressed, Popup }

    private readonly object _gate = new();
    private readonly Dispatcher _dispatcher;
    private readonly Func<AccentPopup> _popup;
    private readonly KeyboardHook _keyboard = new();
    private readonly MouseHook _mouse = new();
    private readonly Native.WinEventProc _winEventProc;
    private nint _winEventHook;

    private State _state = State.Idle;
    private int _primeVk;
    private string[] _variants = [];
    private int _selection = -1;
    private int _lookupToken;
    private readonly HashSet<int> _downKeys = [];
    private readonly HashSet<int> _swallowedDowns = [];

    public bool Enabled { get; set; } = true;

    public HoldController(Dispatcher dispatcher, Func<AccentPopup> popup)
    {
        _dispatcher = dispatcher;
        _popup = popup;
        _winEventProc = OnForegroundChanged;
    }

    public void Install()
    {
        _keyboard.Callback = OnKey;
        _mouse.ButtonDown = OnMouseDown;
        _keyboard.Install();
        _mouse.Install();
        _winEventHook = Native.SetWinEventHook(Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_SYSTEM_FOREGROUND,
            0, _winEventProc, 0, 0, Native.WINEVENT_OUTOFCONTEXT);
    }

    private bool OnKey(int vk, bool isDown)
    {
        lock (_gate)
        {
            var wasDown = _downKeys.Contains(vk);
            if (isDown) _downKeys.Add(vk); else _downKeys.Remove(vk);
            if (!Enabled) return false;

            return _state switch
            {
                State.Idle => HandleIdle(vk, isDown, wasDown),
                State.Priming => HandlePriming(vk, isDown, wasDown),
                State.Pending => HandlePending(vk, isDown, wasDown),
                State.Suppressed => HandleSuppressed(vk, isDown, wasDown),
                State.Popup => HandlePopup(vk, isDown, wasDown),
                _ => false,
            };
        }
    }

    private bool HandleIdle(int vk, bool isDown, bool wasDown)
    {
        if (isDown && !wasDown && TryGetCandidate(vk, out _, out _))
        {
            _primeVk = vk;
            _state = State.Priming;
        }
        return false;
    }

    private bool HandlePriming(int vk, bool isDown, bool wasDown)
    {
        if (isDown && vk == _primeVk && wasDown)
        {
            // First auto-repeat of the held key: swallow it and try to open the popup.
            if (TryGetCandidate(vk, out var ch, out var upper) && AccentMap.TryGetVariants(ch, upper, out var variants))
            {
                _variants = variants;
                _selection = -1;
                _state = State.Pending;
                StartCaretLookup();
                return true;
            }
            _state = State.Idle;
            return false;
        }
        if (isDown)
        {
            _state = State.Idle;
            return HandleIdle(vk, isDown, wasDown);
        }
        if (vk == _primeVk) _state = State.Idle;
        return false;
    }

    private bool HandlePending(int vk, bool isDown, bool wasDown)
    {
        // Lookup in flight: keep swallowing repeats of the held key.
        if (isDown && vk == _primeVk) return true;
        if (isDown)
        {
            _lookupToken++;
            _state = State.Idle;
            return HandleIdle(vk, isDown, wasDown);
        }
        // Key releases pass through; the popup may still appear (macOS keeps it open).
        return false;
    }

    private bool HandleSuppressed(int vk, bool isDown, bool wasDown)
    {
        // No text caret was found: let the key repeat normally until it is released.
        if (!isDown && vk == _primeVk) _state = State.Idle;
        else if (isDown && vk != _primeVk)
        {
            _state = State.Idle;
            return HandleIdle(vk, isDown, wasDown);
        }
        return false;
    }

    private bool HandlePopup(int vk, bool isDown, bool wasDown)
    {
        if (!isDown) return _swallowedDowns.Remove(vk);
        if (vk == _primeVk && wasDown) return true;
        if (IsModifier(vk)) return false;

        var digit = DigitOf(vk);
        if (digit >= 1 && digit <= _variants.Length)
        {
            _swallowedDowns.Add(vk);
            Commit(digit - 1);
            return true;
        }

        switch (vk)
        {
            case Native.VK_LEFT:
                MoveSelection(-1);
                _swallowedDowns.Add(vk);
                return true;
            case Native.VK_RIGHT:
                MoveSelection(+1);
                _swallowedDowns.Add(vk);
                return true;
            case Native.VK_ESCAPE:
                Dismiss();
                _swallowedDowns.Add(vk);
                return true;
            case Native.VK_RETURN or Native.VK_SPACE when _selection >= 0:
                _swallowedDowns.Add(vk);
                Commit(_selection);
                return true;
        }

        // Any other key closes the popup and types normally (macOS behaviour).
        Dismiss();
        return HandleIdle(vk, isDown, wasDown);
    }

    private void StartCaretLookup()
    {
        var token = ++_lookupToken;
        var variants = _variants;
        Task.Run(() =>
        {
            var found = CaretLocator.TryLocate(out var rect, out var approximate);
            lock (_gate)
            {
                if (_state != State.Pending || token != _lookupToken) return;
                if (!found)
                {
                    // No text input in focus: stand down until the key is released.
                    _state = _downKeys.Contains(_primeVk) ? State.Suppressed : State.Idle;
                    return;
                }
                _state = State.Popup;
                _swallowedDowns.Clear();
                _dispatcher.BeginInvoke(() => _popup().ShowAt(rect, approximate, variants, OnOptionClicked));
            }
        });
    }

    private void MoveSelection(int delta)
    {
        var n = _variants.Length;
        _selection = _selection < 0
            ? (delta > 0 ? 0 : n - 1)
            : (_selection + delta + n) % n;
        var sel = _selection;
        _dispatcher.BeginInvoke(() => _popup().SetSelection(sel));
    }

    private void Commit(int index)
    {
        var text = _variants[index];
        _state = State.Idle;
        _dispatcher.BeginInvoke(() =>
        {
            _popup().HideNow();
            TextInjector.ReplaceLastChar(text);
        });
    }

    private void Dismiss()
    {
        _state = State.Idle;
        _dispatcher.BeginInvoke(() => _popup().HideNow());
    }

    private void OnOptionClicked(int index)
    {
        lock (_gate)
        {
            if (_state == State.Popup && index >= 0 && index < _variants.Length) Commit(index);
        }
    }

    private void OnMouseDown(int x, int y)
    {
        lock (_gate)
        {
            switch (_state)
            {
                case State.Priming or State.Pending:
                    _lookupToken++;
                    _state = State.Idle;
                    break;
                case State.Popup:
                    var hit = Native.WindowFromPoint(new Native.POINT { X = x, Y = y });
                    if (hit != AccentPopup.InstanceHwnd) Dismiss();
                    break;
            }
        }
    }

    private void OnForegroundChanged(nint hook, uint evt, nint hwnd, int idObject, int idChild, uint tid, uint time)
    {
        lock (_gate)
        {
            if (_state == State.Popup) Dismiss();
            else if (_state != State.Idle)
            {
                _lookupToken++;
                _state = State.Idle;
            }
        }
    }

    private bool TryGetCandidate(int vk, out char ch, out bool upper)
    {
        ch = default;
        upper = false;
        if (AnyDown(Native.VK_CONTROL, Native.VK_LCONTROL, Native.VK_RCONTROL,
                    Native.VK_MENU, Native.VK_LMENU, Native.VK_RMENU,
                    Native.VK_LWIN, Native.VK_RWIN)) return false;
        var mapped = Native.MapVirtualKeyExW((uint)vk, Native.MAPVK_VK_TO_CHAR, Native.GetForegroundKeyboardLayout());
        // High bit set means dead key; zero means the key produces no character.
        if (mapped == 0 || (mapped & 0x80000000) != 0) return false;
        ch = char.ToLowerInvariant((char)mapped);
        if (!AccentMap.Contains(ch)) return false;
        upper = AnyDown(Native.VK_SHIFT, Native.VK_LSHIFT, Native.VK_RSHIFT) ^ Native.IsCapsLockOn();
        return true;
    }

    private bool AnyDown(params int[] vks) => vks.Any(_downKeys.Contains);

    private static int DigitOf(int vk) => vk switch
    {
        >= 0x31 and <= 0x39 => vk - 0x30,
        > Native.VK_NUMPAD0 and <= Native.VK_NUMPAD0 + 9 => vk - Native.VK_NUMPAD0,
        _ => 0,
    };

    private static bool IsModifier(int vk) => vk is
        Native.VK_SHIFT or Native.VK_CONTROL or Native.VK_MENU or Native.VK_CAPITAL or
        Native.VK_LSHIFT or Native.VK_RSHIFT or Native.VK_LCONTROL or Native.VK_RCONTROL or
        Native.VK_LMENU or Native.VK_RMENU or Native.VK_LWIN or Native.VK_RWIN;

    public void Dispose()
    {
        _keyboard.Dispose();
        _mouse.Dispose();
        if (_winEventHook != 0)
        {
            Native.UnhookWinEvent(_winEventHook);
            _winEventHook = 0;
        }
    }
}
