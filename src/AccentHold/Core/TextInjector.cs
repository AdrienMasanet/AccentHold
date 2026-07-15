using System.Runtime.InteropServices;

namespace AccentHold.Core;

// Replaces the character typed at hold-start with the chosen accented one via SendInput.
internal static class TextInjector
{
    public static void ReplaceLastChar(string replacement)
    {
        var inputs = new List<Native.INPUT>(2 + replacement.Length * 2)
        {
            Key(Native.VK_BACK, up: false),
            Key(Native.VK_BACK, up: true),
        };
        foreach (var ch in replacement)
        {
            inputs.Add(Unicode(ch, up: false));
            inputs.Add(Unicode(ch, up: true));
        }
        var arr = inputs.ToArray();
        Native.SendInput((uint)arr.Length, arr, Marshal.SizeOf<Native.INPUT>());
    }

    private static Native.INPUT Key(int vk, bool up) => new()
    {
        type = Native.INPUT_KEYBOARD,
        u = new Native.InputUnion
        {
            ki = new Native.KEYBDINPUT
            {
                wVk = (ushort)vk,
                dwFlags = up ? Native.KEYEVENTF_KEYUP : 0,
                dwExtraInfo = Native.InjectionTag,
            }
        }
    };

    private static Native.INPUT Unicode(char ch, bool up) => new()
    {
        type = Native.INPUT_KEYBOARD,
        u = new Native.InputUnion
        {
            ki = new Native.KEYBDINPUT
            {
                wScan = ch,
                dwFlags = Native.KEYEVENTF_UNICODE | (up ? Native.KEYEVENTF_KEYUP : 0),
                dwExtraInfo = Native.InjectionTag,
            }
        }
    };
}
