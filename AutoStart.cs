using Microsoft.Win32;

namespace XboxBattery;

/// <summary>开机自启（写入当前用户 Run 项，不需要管理员权限）。</summary>
internal static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "XboxBattery";

    public static bool IsEnabled()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey, false);
            return key?.GetValue(ValueName) is string value && value.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    public static void Set(bool enabled)
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey, true);
        if (key is null)
        {
            return;
        }

        if (enabled)
        {
            string? path = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(path))
            {
                key.SetValue(ValueName, $"\"{path}\"");
            }
        }
        else
        {
            key.DeleteValue(ValueName, false);
        }
    }
}
