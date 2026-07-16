using System.Windows.Threading;
using AccentHold.UI;

namespace AccentHold.Core;

// Press-and-hold state machine; hook callbacks stay cheap, caret lookup runs on a worker, UI on the dispatcher.
internal sealed class HoldController : IDisposable
{
    private enum State { Idle, Priming, Pending, Suppressed, Popup }

    private readonly object _gate = new();
    private readonly Dispatcher _dispatcher;
    private readonly Func<AccentPopup> _popup;
    private readonly Settings _settings;
    private readonly KeyboardHook _keyboard = new();
    private readonly MouseHook _mouse = new();
    private readonly Native.WinEventProc _winEventProc;
    private readonly System.Threading.Timer _holdTimer;
    private nint _winEventHook;

    private AccentTable _table;
    private State _state = State.Idle;
    private int _primeVk;
    private bool _primeUpper;
    private string[] _variants = [];
    private int _selection = -1;
    private int _holdToken;
    private readonly HashSet<int> _downKeys = [];
    private readonly HashSet<int> _swallowedDowns = [];

    public bool Enabled { get; set; } = true;

    public HoldController(Dispatcher dispatcher, Func<AccentPopup> popup, Settings settings)
    {
        _dispatcher = dispatcher;
        _popup = popup;
        _settings = settings;
        _table = new AccentTable(settings.AccentOverrides);
        _winEventProc = OnForegroundChanged;
        _holdTimer = new System.Threading.Timer(OnHoldElapsed);
        settings.Changed += () => { lock (_gate) _table = new AccentTable(_settings.AccentOverrides); };
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
                State.Idle => HandleIdle(vk, isDown),
                State.Priming => HandlePriming(vk, isDown),
                State.Pending => HandlePending(vk, isDown),
                State.Suppressed => HandleSuppressed(vk, isDown),
                State.Popup => HandlePopup(vk, isDown, wasDown),
                _ => false,
            };
        }
    }

    // Idle: the first press of an accentable key types normally and arms the hold timer.
    private bool HandleIdle(int vk, bool isDown)
    {
        if (isDown && TryGetCandidate(vk, out var ch, out var upper) && _table.TryGetVariants(ch, upper, out var variants))
        {
            _primeVk = vk;
            _primeUpper = upper;
            _variants = variants;
            _selection = -1;
            _state = State.Priming;
            _holdToken++;
            _holdTimer.Change(_settings.HoldDelayMs, System.Threading.Timeout.Infinite);
        }
        return false;
    }

    // Priming: swallow the held key's auto-repeats until the timer fires or the key is released.
    private bool HandlePriming(int vk, bool isDown)
    {
        if (isDown && vk == _primeVk) return true;
        if (isDown) { CancelHold(); return HandleIdle(vk, isDown); }
        if (vk == _primeVk) CancelHold();
        return false;
    }

    // Pending: caret lookup in flight; keep swallowing repeats, let other keys cancel.
    private bool HandlePending(int vk, bool isDown)
    {
        if (isDown && vk == _primeVk) return true;
        if (isDown) { _holdToken++; _state = State.Idle; return HandleIdle(vk, isDown); }
        return false;
    }

    // Suppressed: no caret was found; let the key repeat normally until released.
    private bool HandleSuppressed(int vk, bool isDown)
    {
        if (!isDown && vk == _primeVk) _state = State.Idle;
        else if (isDown && vk != _primeVk) { _state = State.Idle; return HandleIdle(vk, isDown); }
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
        return HandleIdle(vk, isDown);
    }

    private void CancelHold()
    {
        _holdToken++;
        _holdTimer.Change(System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
        _state = State.Idle;
    }

    // Timer thread: hold delay reached, look up the caret and decide whether to open the popup.
    private void OnHoldElapsed(object? _)
    {
        int token;
        string[] variants;
        double scale;
        lock (_gate)
        {
            if (_state != State.Priming || !_downKeys.Contains(_primeVk)) return;
            _state = State.Pending;
            token = _holdToken;
            variants = _variants;
            scale = _settings.Scale;
        }

        var found = CaretLocator.TryLocate(out var rect, out var approximate);
        Native.RECT? avoid = CaretLocator.TryGetShellOverlayRect(out var overlayRect) ? overlayRect : null;
        lock (_gate)
        {
            if (_state != State.Pending || token != _holdToken) return;
            if (!found)
            {
                _state = _downKeys.Contains(_primeVk) ? State.Suppressed : State.Idle;
                return;
            }
            _state = State.Popup;
            _swallowedDowns.Clear();
            _dispatcher.BeginInvoke(() => _popup().ShowAt(rect, approximate, variants, scale, avoid, OnOptionClicked));
        }
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
                    CancelHold();
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
            else if (_state != State.Idle) CancelHold();
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
        if (!_table.Contains(ch)) return false;
        upper = AnyDown(Native.VK_SHIFT, Native.VK_LSHIFT, Native.VK_RSHIFT) ^ Native.IsCapsLockOn();
        return true;
    }

    private bool AnyDown(params ReadOnlySpan<int> vks)
    {
        foreach (var vk in vks) if (_downKeys.Contains(vk)) return true;
        return false;
    }

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
        _holdTimer.Dispose();
        if (_winEventHook != 0)
        {
            Native.UnhookWinEvent(_winEventHook);
            _winEventHook = 0;
        }
    }
}
