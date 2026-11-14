using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Nook
{
    public sealed class ItemData
    {
        public string Path { get; set; } = "";

        /// <summary>搬进抽屉存储前的原始位置。非空表示文件已被搬进抽屉。</summary>
        public string? OriginalPath { get; set; }

        /// <summary>搬走前抓下来的图标 PNG。</summary>
        public string? IconCache { get; set; }
    }

    public sealed class DrawerData
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "抽屉";
        public double Left { get; set; } = double.NaN;
        public double Top { get; set; } = double.NaN;
        public double Width { get; set; } = 680;
        public double Height { get; set; } = 500;
        public List<ItemData> Items { get; set; } = new();

        /// <summary>图标尺寸：small / medium / large</summary>
        public string IconSize { get; set; } = "medium";

        /// <summary>固定：展开后不自动收起，且始终置顶</summary>
        public bool Pinned { get; set; }
    }

    public sealed class AppState
    {
        public List<DrawerData> Drawers { get; set; } = new();
        public bool AlwaysOnTop { get; set; } = true;

        /// <summary>悬停刻度即展开（关闭后只能点击展开）</summary>
        public bool HoverToOpen { get; set; } = true;

        /// <summary>顶部轨常驻，不自动收起</summary>
        public bool RailAlwaysVisible { get; set; }

        /// <summary>鼠标离开就收起面板；关掉后要点桌面才收</summary>
        public bool CloseOnLeave { get; set; } = true;
    }

    public static class Store
    {
        private static readonly string Dir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Nook");

        private static readonly string FilePath = Path.Combine(Dir, "drawers.json");

        private static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = true
        };

        public static AppState Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var state = JsonSerializer.Deserialize<AppState>(File.ReadAllText(FilePath), Options);
                    if (state != null) return state;
                }
            }
            catch (Exception ex)
            {
                Log.Write("load failed: " + ex.Message);
            }

            return new AppState();
        }

        public static string ToJson(AppState state) => JsonSerializer.Serialize(state, Options);

        public static AppState? FromJson(string json)
        {
            try { return JsonSerializer.Deserialize<AppState>(json, Options); }
            catch (Exception ex) { Log.Write("parse json failed: " + ex.Message); return null; }
        }

        public static void Save(AppState state)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(state, Options));
                File.Move(tmp, FilePath, true);
            }
            catch (Exception ex)
            {
                Log.Write("save failed: " + ex.Message);
            }
        }
    }
}
