using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace Nook
{
    public sealed class DrawerManager
    {
        private readonly Dictionary<string, DrawerWindow> _panels = new();

        public AppState State { get; private set; } = new();

        public TrayHost? Tray { get; set; }

        public RailWindow? Rail { get; private set; }

        public IEnumerable<DrawerWindow> Panels => _panels.Values;

        public void Start()
        {
            State = Store.Load();
            App.HoverToOpen = State.HoverToOpen;

            if (State.Drawers.Count == 0)
            {
                State.Drawers.Add(NewData("常用"));
                Store.Save(State);
            }

            Rail = new RailWindow(this);
            Rail.Show();

            Tray?.Rebuild();
        }

        public void Save() => Store.Save(State);

        private static DrawerData NewData(string name) => new()
        {
            Name = name,
            Width = 680,
            Height = 500
        };

        /// <summary>面板按需创建，避免九个窗口常驻内存。</summary>
        public DrawerWindow GetOrCreatePanel(DrawerData data)
        {
            if (_panels.TryGetValue(data.Id, out var existing)) return existing;

            var window = new DrawerWindow(data, this);
            _panels[data.Id] = window;
            window.InitializePanel();

            return window;
        }

        public void CreateDrawer()
        {
            var data = NewData($"抽屉 {State.Drawers.Count + 1}");
            State.Drawers.Add(data);
            Save();

            Rail?.Rebuild();
            Rail?.ShowRail();
            Tray?.Rebuild();

            _ = System.Threading.Tasks.Task.Delay(240)
                .ContinueWith(_ => Application.Current.Dispatcher.Invoke(() => OpenDrawer(data)));
        }

        public void DeleteDrawer(DrawerData data)
        {
            if (_panels.TryGetValue(data.Id, out var window))
            {
                window.ClosePanelNow();
                _panels.Remove(data.Id);
                try { window.Close(); } catch { }
            }

            foreach (var item in data.Items)
            {
                ItemStore.MoveBack(item);
                IconSnapshot.Delete(item.IconCache);
            }

            State.Drawers.RemoveAll(d => d.Id == data.Id);

            if (State.Drawers.Count == 0)
            {
                var fresh = NewData("常用");
                State.Drawers.Add(fresh);
                GetOrCreatePanel(fresh);
            }

            Save();
            Rail?.Rebuild();
            Tray?.Rebuild();
        }

        public string? BackupTo(string path)
        {
            try { return BackupService.Create(State, path); }
            catch (Exception ex) { Log.Write("backup failed: " + ex.Message); return null; }
        }

        public bool RestoreFrom(string zipPath)
        {
            var restored = BackupService.Restore(zipPath);
            if (restored == null) return false;

            Reload(restored);
            return true;
        }

        private void Reload(AppState state)
        {
            foreach (var w in _panels.Values)
            {
                try { w.ClosePanelNow(); w.Close(); } catch { }
            }

            _panels.Clear();
            State = state;
            App.HoverToOpen = state.HoverToOpen;

            Rail?.Rebuild();
            Tray?.Rebuild();
        }

        public void OpenDrawer(DrawerData data) => Rail?.OpenFor(data);

        public void ShowRail() => Rail?.ShowRail();

        public void CloseAllPanels() => Rail?.CloseOpenPanel();

        public void SetRailAlwaysVisible(bool on)
        {
            State.RailAlwaysVisible = on;
            Save();
            Rail?.ApplyAlwaysVisible();
        }

        public void SetHoverToOpen(bool on)
        {
            App.HoverToOpen = on;
            State.HoverToOpen = on;
            Save();
        }

        public void SetTopmostAll(bool on)
        {
            State.AlwaysOnTop = on;
            foreach (var w in _panels.Values) w.ApplyTopmost();
            Save();
        }

        public void SnapshotAllIcons()
        {
            int captured = 0;

            foreach (var drawer in State.Drawers)
            {
                foreach (var item in drawer.Items)
                {
                    if (!string.IsNullOrEmpty(item.IconCache) && System.IO.File.Exists(item.IconCache)) continue;

                    string? png = IconSnapshot.Capture(item.Path, drawer.Id);
                    if (png != null)
                    {
                        item.IconCache = png;
                        captured++;
                    }
                }
            }

            if (captured == 0) return;

            Log.Write($"icon snapshot (all): {captured}");
            Save();
            foreach (var w in _panels.Values) w.ReloadIcons();
        }

        public void RestoreAllToDesktop()
        {
            int restored = 0;

            foreach (var drawer in State.Drawers)
            {
                foreach (var item in drawer.Items.ToArray())
                {
                    if (string.IsNullOrEmpty(item.OriginalPath)) continue;

                    ItemStore.MoveBack(item);
                    drawer.Items.Remove(item);
                    restored++;
                }
            }

            if (restored == 0) return;

            Log.Write($"restore all (global): {restored}");
            Save();

            foreach (var w in _panels.Values) w.ReloadIcons();
            Tray?.Rebuild();
        }
    }
}
