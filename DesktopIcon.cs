using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Nook
{
    /// <summary>
    /// 通过 Hidden 属性让桌面图标"消失"，文件本身原地不动。
    /// 这是可逆的——移除抽屉条目时会自动恢复。
    /// </summary>
    internal static class DesktopIcon
    {
        private const int SHCNE_ATTRIBUTES = 0x00000800;
        private const uint SHCNF_PATHW = 0x0005;
        private const uint SHCNF_FLUSH = 0x1000;

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);

        public static bool IsOnDesktop(string path)
        {
            try
            {
                string dir = (Path.GetDirectoryName(path) ?? "").TrimEnd(Path.DirectorySeparatorChar);

                string user = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
                    .TrimEnd(Path.DirectorySeparatorChar);
                string common = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)
                    .TrimEnd(Path.DirectorySeparatorChar);

                return string.Equals(dir, user, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(dir, common, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        /// <summary>隐藏桌面图标。返回 true 表示由本次操作改为隐藏。</summary>
        public static bool Hide(string path)
        {
            try
            {
                if (!File.Exists(path)) return false;
                if (!IsOnDesktop(path)) return false;

                var attrs = File.GetAttributes(path);
                if ((attrs & FileAttributes.Hidden) != 0) return false;

                File.SetAttributes(path, attrs | FileAttributes.Hidden);
                Notify(path);
                return true;
            }
            catch (Exception ex)
            {
                Log.Write($"hide failed {path}: {ex.Message}");
                return false;
            }
        }

        /// <summary>恢复桌面图标。</summary>
        public static void Show(string path)
        {
            try
            {
                if (!File.Exists(path)) return;

                var attrs = File.GetAttributes(path);
                if ((attrs & FileAttributes.Hidden) == 0) return;

                File.SetAttributes(path, attrs & ~FileAttributes.Hidden);
                Notify(path);
            }
            catch (Exception ex)
            {
                Log.Write($"show failed {path}: {ex.Message}");
            }
        }

        public static bool IsHidden(string path)
        {
            try
            {
                if (!File.Exists(path)) return false;
                return (File.GetAttributes(path) & FileAttributes.Hidden) != 0;
            }
            catch { return false; }
        }

        private static void Notify(string path)
        {
            IntPtr ptr = Marshal.StringToHGlobalUni(path);
            try
            {
                SHChangeNotify(SHCNE_ATTRIBUTES, SHCNF_PATHW | SHCNF_FLUSH, ptr, IntPtr.Zero);
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }
    }
}
