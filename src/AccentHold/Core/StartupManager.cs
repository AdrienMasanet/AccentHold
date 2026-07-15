using System.Diagnostics;
using Microsoft.Win32;

namespace AccentHold.Core;

// Toggles run-at-logon. The installer may create an elevated scheduled task ("admin" mode);
// otherwise a plain HKCU Run entry is used. This reflects and switches whichever is in place.
internal static class StartupManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "AccentHold";
    private const string TaskName = "AccentHold";

    public static bool IsEnabled() => TaskExists() || RunKeyExists();

    public static void SetEnabled(bool enabled)
    {
        // Prefer the elevated logon task when the installer created one (needs elevation to change).
        if (TaskExists())
        {
            try { Schtasks(enabled ? $"/Change /TN \"{TaskName}\" /ENABLE" : $"/Change /TN \"{TaskName}\" /DISABLE"); }
            catch { }
            return;
        }
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue(ValueName, $"\"{Environment.ProcessPath}\"");
        else key.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    private static bool RunKeyExists()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string;
    }

    private static bool TaskExists() => Schtasks($"/Query /TN \"{TaskName}\"") == 0;

    private static int Schtasks(string args)
    {
        using var p = Process.Start(new ProcessStartInfo("schtasks.exe", args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        });
        p!.WaitForExit();
        return p.ExitCode;
    }
}
