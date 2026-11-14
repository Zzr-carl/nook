using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Nook
{
    /// <summary>
    /// 游戏卸掉之后，图标源（游戏目录里的 exe/ico）就没了。
    /// Steam 仍会在 appcache\librarycache\&lt;appid&gt;\ 里留一张客户端图标，从这里补。
    /// </summary>
    internal static class SteamIcons
    {
        private static readonly Regex AppIdPattern = new(@"(?:rungameid|install|run)/(\d+)", RegexOptions.IgnoreCase);

        private static string? _steamRoot;
        private static bool _rootProbed;

        public static string? ResolveSource(string shortcutPath)
        {
            try
            {
                string ext = Path.GetExtension(shortcutPath);
                if (!ext.Equals(".url", StringComparison.OrdinalIgnoreCase)) return null;

                string? appId = ExtractAppId(shortcutPath);
                if (appId == null) return null;

                string? root = SteamRoot();
                if (root == null) return null;

                string dir = Path.Combine(root, "appcache", "librarycache", appId);
                if (!Directory.Exists(dir)) return null;

                foreach (string candidate in Rank(dir))
                {
                    if (File.Exists(candidate)) return candidate;
                }
            }
            catch (Exception ex)
            {
                Log.Write($"steam icon failed {shortcutPath}: {ex.Message}");
            }

            return null;
        }

        // 优先 40 位哈希命名的客户端图标，其次 logo，最后立绘/头图
        private static IEnumerable<string> Rank(string dir)
        {
            List<string> files;
            try { files = Directory.GetFiles(dir).ToList(); }
            catch { yield break; }

            foreach (string f in files)
            {
                string name = Path.GetFileNameWithoutExtension(f);
                if (name.Length == 40 && name.All(Uri.IsHexDigit)
                    && Path.GetExtension(f).Equals(".jpg", StringComparison.OrdinalIgnoreCase))
                    yield return f;
            }

            string logo = Path.Combine(dir, "logo.png");
            if (File.Exists(logo)) yield return logo;

            string portrait = Path.Combine(dir, "library_600x900.jpg");
            if (File.Exists(portrait)) yield return portrait;

            string header = Path.Combine(dir, "header.jpg");
            if (File.Exists(header)) yield return header;
        }

        private static string? ExtractAppId(string urlFile)
        {
            foreach (string line in File.ReadLines(urlFile))
            {
                if (!line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase)) continue;

                var m = AppIdPattern.Match(line);
                if (m.Success) return m.Groups[1].Value;
            }

            return null;
        }

        private static string? SteamRoot()
        {
            if (_rootProbed) return _steamRoot;
            _rootProbed = true;

            try
            {
                object? value = Microsoft.Win32.Registry.GetValue(
                    @"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null);

                if (value is string path && path.Length > 0)
                {
                    path = path.Replace('/', '\\').TrimEnd('\\');
                    if (Directory.Exists(path))
                    {
                        _steamRoot = path;
                        return _steamRoot;
                    }
                }
            }
            catch { }

            foreach (string guess in new[]
                     {
                         @"C:\Program Files (x86)\Steam",
                         @"C:\Program Files\Steam",
                         @"D:\Steam", @"E:\Steam", @"E:\steam", @"F:\Steam"
                     })
            {
                if (Directory.Exists(guess))
                {
                    _steamRoot = guess;
                    return _steamRoot;
                }
            }

            return null;
        }
    }
}
