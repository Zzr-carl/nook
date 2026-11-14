using System;
using Microsoft.Win32;

namespace Nook
{
    /// <summary>开机自启：只写当前用户的 Run 项，不动系统级设置，也不需要管理员权限。</summary>
    internal static class Startup
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "Nook";

        public static bool IsEnabled
        {
            get
            {
                try
                {
                    using var key = Registry.CurrentUser.OpenSubKey(RunKey);
                    return key?.GetValue(ValueName) is string s && s.Length > 0;
                }
                catch { return false; }
            }
        }

        public static bool SetEnabled(bool on)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                                ?? Registry.CurrentUser.CreateSubKey(RunKey);
                if (key == null) return false;

                if (on)
                {
                    string exe = Environment.ProcessPath ?? "";
                    if (exe.Length == 0) return false;

                    key.SetValue(ValueName, "\"" + exe + "\"");
                    Log.Write("autostart on: " + exe);
                }
                else
                {
                    key.DeleteValue(ValueName, throwOnMissingValue: false);
                    Log.Write("autostart off");
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.Write("autostart failed: " + ex.Message);
                return false;
            }
        }
    }
}
