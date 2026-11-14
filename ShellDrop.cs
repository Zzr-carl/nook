using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;

namespace Nook
{
    /// <summary>
    /// Explorer 拖出"我的电脑/回收站"这类虚拟对象时，可能只给 Shell IDList 而不给标准文件列表。
    /// 这里把 IDList 还原成 ::{GUID} 形式，剩下交给 ShellItem。
    /// </summary>
    internal static class ShellDrop
    {
        private const string IdListFormat = "Shell IDList Array";
        private const uint SIGDN_DESKTOPABSOLUTEPARSING = 0x80028000;

        [DllImport("shell32.dll")]
        private static extern IntPtr ILCombine(IntPtr pidl1, IntPtr pidl2);

        [DllImport("shell32.dll")]
        private static extern void ILFree(IntPtr pidl);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHGetNameFromIDList(IntPtr pidl, uint sigdnName, out IntPtr ppszName);

        public static bool HasIdList(IDataObject data)
        {
            try { return data.GetDataPresent(IdListFormat); }
            catch { return false; }
        }

        public static List<string> Read(IDataObject data)
        {
            var result = new List<string>();

            byte[]? bytes = null;
            try
            {
                if (data.GetData(IdListFormat) is System.IO.MemoryStream stream)
                    bytes = stream.ToArray();
            }
            catch (Exception ex)
            {
                Log.Write("idlist read failed: " + ex.Message);
                return result;
            }

            if (bytes == null || bytes.Length < 12) return result;

            int cidl = BitConverter.ToInt32(bytes, 0);
            if (cidl <= 0 || cidl > 256) return result;

            // CIDA 布局：cidl，随后 cidl+1 个偏移（父目录一个 + 每项各一个）
            var offsets = new List<int>();
            for (int i = 0; i <= cidl; i++)
            {
                int at = 4 + i * 4;
                if (at + 4 > bytes.Length) break;
                offsets.Add(BitConverter.ToInt32(bytes, at));
            }

            if (offsets.Count < 2) return result;

            GCHandle handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try
            {
                IntPtr basePtr = handle.AddrOfPinnedObject();

                if (!InRange(offsets[0], bytes.Length)) return result;
                IntPtr parent = basePtr + offsets[0];

                for (int i = 1; i < offsets.Count; i++)
                {
                    if (!InRange(offsets[i], bytes.Length)) continue;

                    IntPtr combined = ILCombine(parent, basePtr + offsets[i]);
                    if (combined == IntPtr.Zero) continue;

                    IntPtr namePtr = IntPtr.Zero;
                    try
                    {
                        if (SHGetNameFromIDList(combined, SIGDN_DESKTOPABSOLUTEPARSING, out namePtr) != 0) continue;
                        if (namePtr == IntPtr.Zero) continue;

                        string? name = Marshal.PtrToStringUni(namePtr);
                        if (!string.IsNullOrWhiteSpace(name)) result.Add(name);
                    }
                    finally
                    {
                        if (namePtr != IntPtr.Zero) Native.CoTaskMemFree(namePtr);
                        ILFree(combined);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Write("idlist parse failed: " + ex.Message);
            }
            finally
            {
                handle.Free();
            }

            return result;
        }

        private static bool InRange(int offset, int length) => offset > 0 && offset < length - 2;
    }
}
