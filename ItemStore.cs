using System;
using System.IO;

namespace Nook
{
    /// <summary>
    /// 把桌面上的东西搬进 Nook 自己的存储目录，让桌面图标真正消失。
    /// 原则：只移动，永不删除。移入时记下原位置，移除条目或删除抽屉时自动搬回去。
    /// </summary>
    internal static class ItemStore
    {
        private const long MaxMoveBytes = 300L * 1024 * 1024;

        public static string Root => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Nook", "items");

        /// <summary>
        /// 存储根目录尽量落在源文件所在的卷上。
        /// 跨卷时 Directory.Move 不可用，同卷才是原子重命名——文件夹也搬得动，且零数据风险。
        /// </summary>
        public static string RootForSource(string sourcePath)
        {
            try
            {
                string sourceVolume = Path.GetPathRoot(sourcePath) ?? "";
                string appDataVolume = Path.GetPathRoot(Root) ?? "";

                if (string.Equals(sourceVolume, appDataVolume, StringComparison.OrdinalIgnoreCase))
                    return Root;

                if (sourceVolume.Length == 0) return Root;

                string alt = Path.Combine(sourceVolume, ".nook-items");
                Directory.CreateDirectory(alt);

                try { File.SetAttributes(alt, FileAttributes.Hidden | FileAttributes.System); } catch { }

                return alt;
            }
            catch
            {
                return Root;
            }
        }

        public static string FolderFor(string drawerId) => FolderFor(drawerId, null);

        public static string FolderFor(string drawerId, string? sourcePath)
        {
            string root = sourcePath == null ? Root : RootForSource(sourcePath);
            string dir = Path.Combine(root, drawerId);
            Directory.CreateDirectory(dir);
            return dir;
        }

        public static bool SameVolume(string a, string b)
        {
            string ra = Path.GetPathRoot(a) ?? "";
            string rb = Path.GetPathRoot(b) ?? "";
            return string.Equals(ra, rb, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>把文件/文件夹搬进抽屉目录。失败时返回 false，调用方保留原引用。</summary>
        public static bool TryMoveIn(string source, string drawerId, out string storedPath)
        {
            storedPath = source;

            try
            {
                bool isDir = Directory.Exists(source);
                bool isFile = File.Exists(source);
                if (!isDir && !isFile) return false;

                if (isFile)
                {
                    long size = new FileInfo(source).Length;
                    if (size > MaxMoveBytes)
                    {
                        Log.Write($"skip move, too big ({size} bytes): {source}");
                        return false;
                    }
                }

                string dir = FolderFor(drawerId, source);
                string name = isDir
                    ? new DirectoryInfo(source).Name
                    : new FileInfo(source).Name;

                string? target = Unique(Path.Combine(dir, name), isDir);
                if (target == null) return false;

                if (isDir)
                {
                    // 目录跨卷不能 Move，明确拒绝而不是先复制再删源
                    if (!SameVolume(source, target))
                    {
                        Log.Write($"skip folder move, cross volume: {source} -> {target}");
                        return false;
                    }

                    Directory.Move(source, target);
                }
                else
                {
                    File.Move(source, target);
                }

                storedPath = target;
                Log.Write($"moved in: {source} -> {target}");
                return true;
            }
            catch (Exception ex)
            {
                Log.Write($"move in failed {source}: {ex.Message}");
                return false;
            }
        }

        /// <summary>把搬进来的东西搬回原位置。</summary>
        public static void MoveBack(ItemData item)
        {
            if (item == null || string.IsNullOrEmpty(item.OriginalPath)) return;

            try
            {
                bool isDir = Directory.Exists(item.Path);
                bool isFile = File.Exists(item.Path);

                if (!isDir && !isFile)
                {
                    // 文件已经不在了，别硬来，只清掉记录
                    Log.Write($"move back skipped, missing: {item.Path}");
                    item.OriginalPath = null;
                    return;
                }

                string parent = Path.GetDirectoryName(item.OriginalPath) ?? "";
                if (!string.IsNullOrEmpty(parent) && !Directory.Exists(parent))
                    Directory.CreateDirectory(parent);

                string? target = Unique(item.OriginalPath, isDir);
                if (target == null) return;

                if (isDir) Directory.Move(item.Path, target);
                else File.Move(item.Path, target);

                Log.Write($"moved back: {item.Path} -> {target}");
                item.Path = target;
                item.OriginalPath = null;
            }
            catch (Exception ex)
            {
                Log.Write($"move back failed {item.Path}: {ex.Message}");
            }
        }

        private static string? Unique(string desired, bool isDir)
        {
            try
            {
                bool taken = isDir ? Directory.Exists(desired) || File.Exists(desired)
                                   : File.Exists(desired) || Directory.Exists(desired);
                if (!taken) return desired;

                string dir = Path.GetDirectoryName(desired) ?? "";
                string stem = isDir ? Path.GetFileName(desired) : Path.GetFileNameWithoutExtension(desired);
                string ext = isDir ? "" : Path.GetExtension(desired);

                for (int i = 2; i < 1000; i++)
                {
                    string candidate = Path.Combine(dir, $"{stem} ({i}){ext}");
                    bool exists = isDir ? Directory.Exists(candidate) || File.Exists(candidate)
                                        : File.Exists(candidate) || Directory.Exists(candidate);
                    if (!exists) return candidate;
                }

                return Path.Combine(dir, $"{stem} ({Guid.NewGuid():N}){ext}");
            }
            catch (Exception ex)
            {
                Log.Write($"unique name failed: {ex.Message}");
                return null;
            }
        }
    }
}
