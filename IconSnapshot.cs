using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Nook
{
    /// <summary>
    /// 在文件还待在原位时把图标抓成 PNG 存下来。
    /// 之后无论文件被搬走、目标盘掉线、快捷方式被改，抽屉里的图标都不会变白。
    /// </summary>
    internal static class IconSnapshot
    {
        public static string PathFor(string drawerId, string key)
        {
            string hash = Hash(key);
            return Path.Combine(ItemStore.FolderFor(drawerId, key), $"icon_{hash}.png");
        }

        /// <summary>抓取并落盘。失败返回 null，调用方退回实时解析。</summary>
        public static string? Capture(string sourcePath, string drawerId)
        {
            try
            {
                // 快捷方式的图标可能来自目标程序，IconLocation 也可能是旧路径 —— 先解析再取
                string? resolved = ShortcutIcon.ResolveSource(sourcePath);
                var img = (resolved != null ? IconProvider.Get(resolved) : null)
                          ?? IconProvider.Get(sourcePath);

                if (img == null) return null;

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(img));

                string target = PathFor(drawerId, sourcePath);
                using (var fs = File.Create(target)) encoder.Save(fs);

                Log.Write($"icon snapshot: {sourcePath} -> {Path.GetFileName(target)}");
                return target;
            }
            catch (Exception ex)
            {
                Log.Write($"icon snapshot failed {sourcePath}: {ex.Message}");
                return null;
            }
        }

        public static ImageSource? Load(string? pngPath)
        {
            if (string.IsNullOrEmpty(pngPath) || !File.Exists(pngPath)) return null;

            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                bitmap.UriSource = new Uri(pngPath);
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch
            {
                return null;
            }
        }

        public static void Delete(string? pngPath)
        {
            try
            {
                if (!string.IsNullOrEmpty(pngPath) && File.Exists(pngPath)) File.Delete(pngPath);
            }
            catch { }
        }

        private static string Hash(string text)
        {
            byte[] bytes = SHA1.HashData(Encoding.UTF8.GetBytes(text.ToLowerInvariant()));
            return Convert.ToHexString(bytes)[..10].ToLowerInvariant();
        }
    }
}
