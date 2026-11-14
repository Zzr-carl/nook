using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Nook
{
    /// <summary>
    /// 抽屉备份：drawers.json + 全部图标快照，打成一个 zip。
    /// 恢复前会自动把当前状态再存一份，防止覆盖掉还没备份的新改动。
    /// </summary>
    internal static class BackupService
    {
        private const string StateEntry = "drawers.json";
        private const string IconFolder = "icons/";
        private const string IndexEntry = "icons/index.txt";
        private const string ManifestEntry = "manifest.txt";

        public static string AutoDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Nook", "backups");

        public static string SuggestName() => $"nook-{DateTime.Now:yyyyMMdd-HHmm}.zip";

        public static string Create(AppState state, string targetPath)
        {
            string? dir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            if (File.Exists(targetPath)) File.Delete(targetPath);

            using var zip = ZipFile.Open(targetPath, ZipArchiveMode.Create);

            var stateEntry = zip.CreateEntry(StateEntry);
            using (var stream = stateEntry.Open())
            {
                byte[] bytes = Encoding.UTF8.GetBytes(Store.ToJson(state));
                stream.Write(bytes, 0, bytes.Length);
            }

            var icons = new List<string>();
            foreach (string? icon in state.Drawers
                         .SelectMany(d => d.Items)
                         .Select(i => i.IconCache)
                         .Where(p => !string.IsNullOrEmpty(p))
                         .Distinct())
            {
                if (!File.Exists(icon)) continue;
                icons.Add(icon!);
            }

            var index = new StringBuilder();
            foreach (string icon in icons)
            {
                string key = Sha1(icon) + ".png";
                zip.CreateEntryFromFile(icon, IconFolder + key, CompressionLevel.Optimal);
                index.AppendLine($"{key}|{icon}");
            }

            var indexEntry = zip.CreateEntry(IndexEntry);
            using (var stream = indexEntry.Open())
            {
                byte[] bytes = Encoding.UTF8.GetBytes(index.ToString());
                stream.Write(bytes, 0, bytes.Length);
            }

            int itemCount = state.Drawers.Sum(d => d.Items.Count);
            var manifest = zip.CreateEntry(ManifestEntry);
            using (var stream = manifest.Open())
            {
                string text = $"Nook backup\ncreated={DateTime.Now:yyyy-MM-dd HH:mm:ss}\n"
                            + $"drawers={state.Drawers.Count}\nitems={itemCount}\nicons={icons.Count}\n";
                byte[] bytes = Encoding.UTF8.GetBytes(text);
                stream.Write(bytes, 0, bytes.Length);
            }

            Log.Write($"backup created: {targetPath} | drawers={state.Drawers.Count} items={itemCount} icons={icons.Count}");
            return targetPath;
        }

        /// <summary>恢复。返回恢复后的状态；失败返回 null。</summary>
        public static AppState? Restore(string zipPath)
        {
            try
            {
                using var zip = ZipFile.OpenRead(zipPath);

                var stateEntry = zip.GetEntry(StateEntry);
                if (stateEntry == null)
                {
                    Log.Write("restore failed: drawers.json missing in archive");
                    return null;
                }

                string json;
                using (var reader = new StreamReader(stateEntry.Open(), Encoding.UTF8))
                    json = reader.ReadToEnd();

                var state = Store.FromJson(json);
                if (state == null) return null;

                // 先给现状留一份，万一这个是错的备份还能退回去
                Directory.CreateDirectory(AutoDir);
                string safety = Path.Combine(AutoDir, $"before-restore-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
                try { Create(Store.Load(), safety); } catch (Exception ex) { Log.Write("safety backup failed: " + ex.Message); }

                // 还原图标快照
                var indexEntry = zip.GetEntry(IndexEntry);
                if (indexEntry != null)
                {
                    using var reader = new StreamReader(indexEntry.Open(), Encoding.UTF8);
                    string? line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        string[] parts = line.Split('|', 2);
                        if (parts.Length != 2) continue;

                        var entry = zip.GetEntry(IconFolder + parts[0]);
                        if (entry == null) continue;

                        try
                        {
                            string dir = Path.GetDirectoryName(parts[1]) ?? "";
                            if (dir.Length > 0) Directory.CreateDirectory(dir);
                            entry.ExtractToFile(parts[1], true);
                        }
                        catch (Exception ex)
                        {
                            Log.Write($"icon restore skipped {parts[1]}: {ex.Message}");
                        }
                    }
                }

                Store.Save(state);
                Log.Write($"restore done: {zipPath} | drawers={state.Drawers.Count}");
                return state;
            }
            catch (Exception ex)
            {
                Log.Write("restore failed: " + ex);
                return null;
            }
        }

        public static string? ReadManifest(string zipPath)
        {
            try
            {
                using var zip = ZipFile.OpenRead(zipPath);
                var entry = zip.GetEntry(ManifestEntry);
                if (entry == null) return null;

                using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
                return reader.ReadToEnd().Trim();
            }
            catch { return null; }
        }

        private static string Sha1(string text)
        {
            byte[] hash = SHA1.HashData(Encoding.UTF8.GetBytes(text.ToLowerInvariant()));
            return Convert.ToHexString(hash)[..12].ToLowerInvariant();
        }
    }
}
