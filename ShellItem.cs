using System;
using System.Runtime.InteropServices;

namespace Nook
{
    /// <summary>
    /// 「我的电脑」「回收站」这类虚拟对象没有文件路径，只有 ::{GUID} 形式的 shell 名称。
    /// 这里负责识别它们并取出显示名，图标则复用 IShellItemImageFactory。
    /// </summary>
    internal static class ShellItem
    {
        public const string RecycleBin = "::{645FF040-5081-101B-9F08-00AA002F954E}";
        public const string ThisPc = "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}";

        public static bool IsShellPath(string path) =>
            path.StartsWith("::", StringComparison.Ordinal);

        public static string? GetDisplayName(string parsingName)
        {
            object? item = null;
            IntPtr namePtr = IntPtr.Zero;

            try
            {
                Guid iid = typeof(Native.IShellItem).GUID;
                Native.SHCreateItemFromParsingName(parsingName, IntPtr.Zero, iid, out item);

                if (item is not Native.IShellItem shellItem) return null;

                shellItem.GetDisplayName(Native.SIGDN_NORMALDISPLAY, out namePtr);
                if (namePtr == IntPtr.Zero) return null;

                return Marshal.PtrToStringUni(namePtr);
            }
            catch
            {
                return null;
            }
            finally
            {
                if (namePtr != IntPtr.Zero) Native.CoTaskMemFree(namePtr);
                if (item != null)
                {
                    try { Marshal.ReleaseComObject(item); } catch { }
                }
            }
        }

        /// <summary>打开虚拟对象。ShellExecute 认 ::{GUID}，失败再退回 explorer 的 shell: 别名。</summary>
        public static bool Open(string path)
        {
            if (!IsShellPath(path)) return false;

            try
            {
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
                return true;
            }
            catch { }

            string? alias = path.ToUpperInvariant() switch
            {
                RecycleBin => "shell:RecycleBinFolder",
                ThisPc => "shell:MyComputerFolder",
                _ => null
            };

            if (alias == null) return false;

            try
            {
                System.Diagnostics.Process.Start("explorer.exe", alias);
                return true;
            }
            catch { return false; }
        }
    }
}
