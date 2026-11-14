using System;
using System.IO;

namespace Nook
{
    /// <summary>
    /// 从快捷方式里挖出真正承载图标的文件。
    /// .url 读 IconFile=；.lnk 走 WScript.Shell 拿 IconLocation / TargetPath。
    /// 这样就不依赖 shell 对快捷方式所在位置的推断——文件被搬走后图标也不会变白。
    /// </summary>
    internal static class ShortcutIcon
    {
        public static string? ResolveSource(string shortcutPath)
        {
            try
            {
                string ext = Path.GetExtension(shortcutPath);

                if (ext.Equals(".url", StringComparison.OrdinalIgnoreCase))
                {
                    // 先看 IconFile，失效了再从 Steam 缓存里补
                    return FromUrl(shortcutPath) ?? SteamIcons.ResolveSource(shortcutPath);
                }

                if (ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase)) return FromLnk(shortcutPath);
            }
            catch (Exception ex)
            {
                Log.Write($"icon resolve failed {shortcutPath}: {ex.Message}");
            }

            return null;
        }

        private static string? FromUrl(string path)
        {
            foreach (string line in File.ReadLines(path))
            {
                if (!line.StartsWith("IconFile=", StringComparison.OrdinalIgnoreCase)) continue;

                string value = line.Substring("IconFile=".Length).Trim().Trim('"');
                if (value.Length == 0) return null;

                value = Environment.ExpandEnvironmentVariables(value);
                if (!Path.IsPathRooted(value))
                {
                    string dir = Path.GetDirectoryName(path) ?? "";
                    value = Path.GetFullPath(Path.Combine(dir, value));
                }

                return File.Exists(value) ? value : null;
            }

            return null;
        }

        private static string? FromLnk(string path)
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return null;

            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic link = shell.CreateShortcut(path);

            string iconLocation = (string)link.IconLocation;
            if (!string.IsNullOrWhiteSpace(iconLocation))
            {
                string candidate = iconLocation.Split(',')[0].Trim().Trim('"');
                if (candidate.Length > 0)
                {
                    candidate = Environment.ExpandEnvironmentVariables(candidate);
                    if (File.Exists(candidate)) return candidate;
                }
            }

            string target = (string)link.TargetPath;
            if (!string.IsNullOrWhiteSpace(target))
            {
                target = Environment.ExpandEnvironmentVariables(target);
                if (File.Exists(target)) return target;
            }

            return null;
        }
    }
}
