using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Nook
{
    /// <summary>把文件路径变成冻结的 BitmapSource，可跨线程使用。</summary>
    internal static class IconProvider
    {
        private static readonly Dictionary<string, (long stamp, BitmapSource? img)> Cache = new(StringComparer.OrdinalIgnoreCase);

        public static BitmapSource? Get(string path)
        {
            path = path.Replace('/', '\\');
            long stamp = 0;
            try
            {
                var info = new FileInfo(path);
                if (info.Exists) stamp = info.LastWriteTimeUtc.Ticks;
                else
                {
                    var di = new DirectoryInfo(path);
                    if (di.Exists) stamp = di.LastWriteTimeUtc.Ticks;
                }
            }
            catch { }

            if (Cache.TryGetValue(path, out var hit) && hit.stamp == stamp && hit.img != null)
                return hit.img;

            BitmapSource? img = TryLoadImageFile(path) ?? TryShellImage(path) ?? TryLegacyIcon(path);

            Cache[path] = (stamp, img);
            return img;
        }

        public static void ClearCache() => Cache.Clear();

        /// <summary>
        /// 把位图复制成不带解码器背书的纯 BitmapSource。
        /// 必须在解码所在的线程调用——跨线程调会触发 CheckIfSiteOfOrigin 的线程校验。
        /// </summary>
        private static BitmapSource Detach(BitmapSource src)
        {
            int w = src.PixelWidth;
            int h = src.PixelHeight;
            if (w <= 0 || h <= 0) return src;

            int stride = w * 4;
            byte[] buffer = new byte[stride * h];

            BitmapSource readable = src.Format == PixelFormats.Bgra32
                ? src
                : new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);

            readable.CopyPixels(buffer, stride, 0);

            BitmapSource copy = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, buffer, stride);
            copy.Freeze();
            return copy;
        }

        // .ico / .png / .jpg 直接解码，别让 shell 返回缩略图
        private static BitmapSource? TryLoadImageFile(string path)
        {
            if (ShellItem.IsShellPath(path)) return null;

            string ext = Path.GetExtension(path);

            // Windows Installer 的 ProductIcon 其实是 ICO，只是没扩展名 —— 交给解码器嗅探
            bool direct = ext.Length == 0
                       || ext.Equals(".ico", StringComparison.OrdinalIgnoreCase)
                       || ext.Equals(".png", StringComparison.OrdinalIgnoreCase)
                       || ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
                       || ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
                       || ext.Equals(".bmp", StringComparison.OrdinalIgnoreCase);

            if (!direct) return null;

            try
            {
                var decoder = BitmapDecoder.Create(
                    new Uri(path), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);

                if (decoder.Frames.Count == 0) return null;

                // 帧顺序不可靠，按像素面积挑最大的一张
                BitmapSource best = decoder.Frames[0];
                foreach (var f in decoder.Frames)
                {
                    if (f.PixelWidth * f.PixelHeight > best.PixelWidth * best.PixelHeight) best = f;
                }

                best.Freeze();
                return Detach(best);
            }
            catch
            {
                return null;
            }
        }

        // 走 shell 的图片工厂：拿到 64px 位图，图片文件还能拿到真实缩略图
        private static BitmapSource? TryShellImage(string path)
        {
            object? item = null;
            IntPtr hbmp = IntPtr.Zero;
            try
            {
                Guid iid = typeof(Native.IShellItemImageFactory).GUID;
                Native.SHCreateItemFromParsingName(path, IntPtr.Zero, iid, out item);
                if (item is not Native.IShellItemImageFactory factory) return null;

                int hr = factory.GetImage(new Native.NativeSize(64, 64), Native.SIIGBF_BIGGERSIZEOK, out hbmp);
                if (hr != 0 || hbmp == IntPtr.Zero) return null;

                BitmapSource src = Imaging.CreateBitmapSourceFromHBitmap(
                    hbmp, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                src.Freeze();
                return src;
            }
            catch { return null; }
            finally
            {
                if (hbmp != IntPtr.Zero) Native.DeleteObject(hbmp);
                if (item != null)
                {
                    try { Marshal.ReleaseComObject(item); } catch { }
                }
            }
        }

        // 兜底：老 API，固定 32px
        private static BitmapSource? TryLegacyIcon(string path)
        {
            IntPtr hIcon = IntPtr.Zero;
            try
            {
                var shfi = new Native.SHFILEINFO();
                IntPtr res = Native.SHGetFileInfo(
                    path, 0, ref shfi, (uint)Marshal.SizeOf<Native.SHFILEINFO>(),
                    Native.SHGFI_ICON | Native.SHGFI_LARGEICON);

                if (res == IntPtr.Zero || shfi.hIcon == IntPtr.Zero) return null;
                hIcon = shfi.hIcon;

                BitmapSource src = Imaging.CreateBitmapSourceFromHIcon(
                    hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                src.Freeze();
                return src;
            }
            catch { return null; }
            finally
            {
                if (hIcon != IntPtr.Zero) DestroyIcon(hIcon);
            }
        }

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr hIcon);
    }
}

namespace Nook
{
    /// <summary>解码过的图标留在内存里，展开第二次起不再碰磁盘和 shell。</summary>
    internal static class IconMemory
    {
        private static readonly Dictionary<string, ImageSource> Map = new(StringComparer.OrdinalIgnoreCase);
        private static readonly object Gate = new();

        public static string KeyFor(string? snapshotPath, string itemPath) =>
            snapshotPath is { Length: > 0 } ? "snap:" + snapshotPath : "item:" + itemPath;

        public static ImageSource? Get(string key)
        {
            lock (Gate) return Map.TryGetValue(key, out var src) ? src : null;
        }

        public static void Put(string key, ImageSource? src)
        {
            if (src == null) return;
            lock (Gate) Map[key] = src;
        }

        public static void Clear()
        {
            lock (Gate) Map.Clear();
        }
    }
}
