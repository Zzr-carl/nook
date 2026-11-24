using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Drawing = System.Drawing;
using WinForms = System.Windows.Forms;

namespace Nook
{
    public sealed class TrayHost : IDisposable
    {
        private readonly DrawerManager _manager;
        private readonly WinForms.NotifyIcon _icon;
        private readonly ContextMenu _menu;
        private Window? _anchor;
        private AccentWindow? _accentWin;
        private bool _disposed;

        public TrayHost(DrawerManager manager)
        {
            _manager = manager;

            _menu = new ContextMenu
            {
                Style = (Style)Application.Current.FindResource("NookMenu")
            };
            _menu.Opened += (_, _) => Rebuild();

            _icon = new WinForms.NotifyIcon
            {
                Icon = BuildIcon(),
                Visible = true,
                Text = "Nook"
            };
            _icon.MouseUp += OnTrayMouseUp;
        }

        /// <summary>菜单第一次打开时才建锚点窗口——开机时省下 300ms。</summary>
        private void EnsureAnchor()
        {
            if (_anchor != null) return;

            // 没有父级的 ContextMenu 用 IsOpen 弹出时，二级子菜单打不开（拿不到菜单焦点）
            _anchor = new Window
            {
                Width = 1,
                Height = 1,
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                ShowActivated = false,
                Background = System.Windows.Media.Brushes.Transparent,
                Left = -300,
                Top = -300
            };
            _anchor.Show();
            _menu.PlacementTarget = _anchor;
        }

        /// <summary>主题色窗口单例：已开着就带到前台。</summary>
        public void ShowAccentWindow()
        {
            if (_accentWin == null || !_accentWin.IsLoaded)
                _accentWin = new AccentWindow(_manager);

            _accentWin.Show();
            _accentWin.Activate();
        }

        private void OnTrayMouseUp(object? sender, WinForms.MouseEventArgs e)
        {
            if (e.Button == WinForms.MouseButtons.Left)
            {
                _manager.ShowRail();
                return;
            }

            if (e.Button == WinForms.MouseButtons.Right)
            {
                Rebuild();
                EnsureAnchor();
                _menu.Placement = PlacementMode.MousePoint;
                _menu.IsOpen = true;
            }
        }

        public void Rebuild()
        {
            _menu.Items.Clear();

            var show = new MenuItem { Header = "显示顶部抽屉轨" };
            show.Click += (_, _) => _manager.ShowRail();
            _menu.Items.Add(show);

            _menu.Items.Add(Separator());

            foreach (var drawer in _manager.State.Drawers)
            {
                var item = new MenuItem { Header = $"{drawer.Name}  ·  {drawer.Items.Count}" };
                item.Click += (_, _) => _manager.OpenDrawer(drawer);
                _menu.Items.Add(item);
            }

            _menu.Items.Add(Separator());

            var create = new MenuItem { Header = "新建抽屉" };
            create.Click += (_, _) => _manager.CreateDrawer();
            _menu.Items.Add(create);

            var restore = new MenuItem { Header = "把抽屉里的文件还原到桌面" };
            restore.Click += (_, _) => _manager.RestoreAllToDesktop();
            _menu.Items.Add(restore);

            _menu.Items.Add(Separator());

            var backup = new MenuItem { Header = "备份抽屉…" };
            backup.Click += (_, _) => BackupDrawers();
            _menu.Items.Add(backup);

            var load = new MenuItem { Header = "从备份恢复…" };
            load.Click += (_, _) => RestoreDrawers();
            _menu.Items.Add(load);

            _menu.Items.Add(Separator());

            var autoRun = new MenuItem
            {
                Header = "开机自启",
                IsCheckable = true,
                IsChecked = Nook.Startup.IsEnabled
            };
            autoRun.Click += (_, _) =>
            {
                if (!Nook.Startup.SetEnabled(!Nook.Startup.IsEnabled))
                    MessageBox.Show("写注册表失败，可能被安全软件拦了。", "Nook");
            };
            _menu.Items.Add(autoRun);

            var accent = new MenuItem { Header = "主题色…" };
            accent.Click += (_, _) => ShowAccentWindow();
            _menu.Items.Add(accent);

            var closeOnLeave = new MenuItem
            {
                Header = "移开鼠标就收起",
                IsCheckable = true,
                IsChecked = _manager.State.CloseOnLeave
            };
            closeOnLeave.Click += (_, _) => _manager.SetCloseOnLeave(!_manager.State.CloseOnLeave);
            _menu.Items.Add(closeOnLeave);

            var keep = new MenuItem
            {
                Header = "始终显示顶部轨",
                IsCheckable = true,
                IsChecked = _manager.State.RailAlwaysVisible
            };
            keep.Click += (_, _) => _manager.SetRailAlwaysVisible(!_manager.State.RailAlwaysVisible);
            _menu.Items.Add(keep);

            var hover = new MenuItem
            {
                Header = "悬停即展开",
                IsCheckable = true,
                IsChecked = App.HoverToOpen
            };
            hover.Click += (_, _) => _manager.SetHoverToOpen(!App.HoverToOpen);
            _menu.Items.Add(hover);

            var pin = new MenuItem
            {
                Header = "始终置顶",
                IsCheckable = true,
                IsChecked = _manager.State.AlwaysOnTop
            };
            pin.Click += (_, _) => _manager.SetTopmostAll(!_manager.State.AlwaysOnTop);
            _menu.Items.Add(pin);

            _menu.Items.Add(Separator());

            var quit = new MenuItem { Header = "退出 Nook" };
            quit.Click += (_, _) => Application.Current.Shutdown();
            _menu.Items.Add(quit);
        }

        private void BackupDrawers()
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "备份抽屉",
                FileName = BackupService.SuggestName(),
                DefaultExt = ".zip",
                Filter = "Nook 备份|*.zip",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };

            if (dialog.ShowDialog() != true) return;

            // 先把还没快照的图标补抓一遍，备份才完整
            _manager.SnapshotAllIcons();

            string? path = _manager.BackupTo(dialog.FileName);
            Notify(path == null ? "备份失败，详情见日志" : "备份完成\n" + path,
                   path == null ? WinForms.ToolTipIcon.Error : WinForms.ToolTipIcon.Info);
        }

        private void RestoreDrawers()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "从备份恢复",
                Filter = "Nook 备份|*.zip",
                InitialDirectory = Directory.Exists(BackupService.AutoDir)
                    ? BackupService.AutoDir
                    : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };

            if (dialog.ShowDialog() != true) return;

            string? manifest = BackupService.ReadManifest(dialog.FileName);
            string info = manifest ?? "（无法读取清单）";

            var ok = MessageBox.Show(
                $"即将用这个备份替换当前的抽屉配置：\n\n{info}\n\n当前状态会先自动另存一份。继续？",
                "Nook", MessageBoxButton.OKCancel, MessageBoxImage.Question);

            if (ok != MessageBoxResult.OK) return;

            bool done = _manager.RestoreFrom(dialog.FileName);
            Notify(done ? "恢复完成" : "恢复失败，详情见日志",
                   done ? WinForms.ToolTipIcon.Info : WinForms.ToolTipIcon.Error);
        }

        public void Say(string text) => Notify(text, WinForms.ToolTipIcon.Info);

        private void Notify(string text, WinForms.ToolTipIcon icon)
        {
            try
            {
                _icon.BalloonTipTitle = "Nook";
                _icon.BalloonTipText = text;
                _icon.BalloonTipIcon = icon;
                _icon.ShowBalloonTip(4000);
            }
            catch { }
        }

        private static Separator Separator() =>
            new() { Style = (Style)Application.Current.FindResource("NookSeparator") };

        /// <summary>托盘图标：优先用打包进去的 nook.ico，挑跟系统小图标最接近的尺寸。</summary>
        private static Drawing.Icon BuildIcon()
        {
            try
            {
                var uri = new Uri("pack://application:,,,/Assets/nook.ico");
                using var stream = System.Windows.Application.GetResourceStream(uri)?.Stream;
                if (stream != null)
                {
                    int want = WinForms.SystemInformation.SmallIconSize.Width;
                    return new Drawing.Icon(stream, want, want);
                }
            }
            catch (Exception ex)
            {
                Log.Write("tray icon load failed: " + ex.Message);
            }

            // 兜底：临时画一个方块，总比没有图标强
            using var bmp = new Drawing.Bitmap(32, 32);
            using (var g = Drawing.Graphics.FromImage(bmp))
            {
                g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(Drawing.Color.Transparent);
                using var path = RoundedRect(new Drawing.Rectangle(3, 6, 26, 20), 6);
                using var body = new Drawing.SolidBrush(Drawing.Color.FromArgb(255, 244, 245, 247));
                g.FillPath(body, path);
            }

            return Drawing.Icon.FromHandle(bmp.GetHicon());
        }

        private static Drawing.Drawing2D.GraphicsPath RoundedRect(Drawing.Rectangle r, int radius)
        {
            int d = radius * 2;
            var p = new Drawing.Drawing2D.GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _icon.Visible = false;
            _icon.Dispose();
        }
    }
}
