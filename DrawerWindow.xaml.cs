using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Nook
{
    public partial class DrawerWindow : Window
    {
        private const string ItemFormat = "NookItemIndex";

        private readonly ObservableCollection<DrawerItem> _items = new();
        private readonly DrawerItem _addTile = new("", isAddTile: true);
        private readonly DrawerData _data;
        private readonly DrawerManager _manager;

        private bool _expanded;
        private bool _opening;
        private bool _renaming;
        private DrawerItem? _dragCandidate;
        private Point _dragStart;

        private readonly DispatcherTimer _collapseWatch;
        private bool _pendingCollapse;
        private bool _draggingOut;
        private bool _internalDrop;
        private DateTime _dragEndedAt = DateTime.MinValue;
        private DispatcherTimer? _glassWatch;

        public DrawerWindow(DrawerData data, DrawerManager manager)
        {
            _data = data;
            _manager = manager;

            InitializeComponent();
            ItemsHost.ItemsSource = _items;

            // 任何"没经过 OpenPanelAsync 就被显示"的情况，立刻收回并记录
            IsVisibleChanged += (_, _) =>
            {
                if (!IsVisible || _expanded || _opening) return;

                Log.Write($"guard: panel shown unexpectedly, hiding ({_data.Name})");
                Dispatcher.BeginInvoke(new Action(Hide));
            };

            GlassShell.SizeChanged += (_, _) => UpdateClip();
            SizeChanged += (_, _) => UpdateClip();

            AllowDrop = true;
            Drop += OnDrop;
            DragOver += OnDragOver;

            // 拖拽时窗口会被判为"失活"，这里不能立刻收，否则拖到一半面板就没了
            _collapseWatch = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(70) };
            _collapseWatch.Tick += (_, _) =>
            {
                if (Mouse.LeftButton == MouseButtonState.Pressed) return;

                _collapseWatch.Stop();
                if (_pendingCollapse && _expanded && !SuppressAutoCollapse)
                    _ = CollapseAsync();

                _pendingCollapse = false;
            };

            DragEnter += (_, e) =>
            {
                // 有东西拖进来了，别收
                _pendingCollapse = false;
                _collapseWatch.Stop();

                if (!e.Data.GetDataPresent(ItemFormat))
                    DropHint.Visibility = Visibility.Visible;
            };

            DragLeave += (_, _) =>
            {
                DropHint.Visibility = Visibility.Collapsed;
                InsertMarker.Visibility = Visibility.Collapsed;
                ArmCollapseWhenReleased();
            };

            Deactivated += (_, _) =>
            {
                if (!_expanded || SuppressAutoCollapse || _renaming) return;

                // 固定住的不自动收起
                if (_data.Pinned) return;

                // 正在把条目拖出去，松手前别收
                if (_draggingOut || (DateTime.Now - _dragEndedAt).TotalMilliseconds < 400) return;

                // 左键还按着，多半是在往抽屉里拖文件，等松手再决定
                if (Mouse.LeftButton == MouseButtonState.Pressed)
                {
                    ArmCollapseWhenReleased();
                    return;
                }

                _ = CollapseAsync("deactivated");
            };

            PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape && _expanded && !_renaming)
                {
                    _ = CollapseAsync("esc");
                    e.Handled = true;
                }
            };

            SourceInitialized += (_, _) => WindowStyleHelper.HideFromAltTab(this);
        }

        private void ArmCollapseWhenReleased()
        {
            if (!_expanded || SuppressAutoCollapse) return;
            if (_draggingOut || (DateTime.Now - _dragEndedAt).TotalMilliseconds < 400) return;
            _pendingCollapse = true;
            _collapseWatch.Stop();
            _collapseWatch.Start();
        }

        public DrawerData Data => _data;

        public bool IsPinned => _data.Pinned;

        /// <summary>固定 + 全局置顶，任一为真就置顶。</summary>
        public void ApplyTopmost() => Topmost = _data.Pinned || _manager.State.AlwaysOnTop;

        public void TogglePinned()
        {
            _data.Pinned = !_data.Pinned;
            _manager.Save();

            ApplyTopmost();
            ApplyPinnedVisual();

            Log.Write($"pinned={_data.Pinned} ({_data.Name})");

            // 刚取消固定时，若已经失焦就顺手收起来
            if (!_data.Pinned && _expanded && !IsActive) _ = CollapseAsync("unpin");
        }

        private void ApplyPinnedVisual()
        {
            AccentBar.Height = _data.Pinned ? 24 : 15;
            AccentBar.Width = _data.Pinned ? 3.5 : 3;

            HairlineBorder.BorderBrush = _data.Pinned
                ? new SolidColorBrush(Color.FromArgb(0x4D, App.AccentColor.R, App.AccentColor.G, App.AccentColor.B))
                : (Brush)FindResource("Hairline");
        }

        /// <summary>换主题色后刷新渐变端点和固定色。DynamicResource 部分会自动跟。</summary>
        public void RefreshAccent()
        {
            var c = App.AccentColor;
            AccentStopTop.Color = Color.FromRgb(c.R, c.G, c.B);
            AccentStopBottom.Color = Color.FromArgb(0x80, c.R, c.G, c.B);
            ApplyPinnedVisual();
        }

        public bool IsExpanded => _expanded;

        public bool SuppressAutoCollapse { get; set; }

        // ==================== 启动 ====================

        /// <summary>面板模式：窗口不常驻，由顶部轨按需打开。</summary>
        public void InitializePanel()
        {
            // 没打开之前待在屏幕外，万一被意外显示也看不见
            Width = 680;
            Height = 500;
            Left = -6000;
            Top = -6000;

            ApplyTopmost();
            RefreshAccent();

            SourceInitialized += (_, _) =>
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                Log.Write("exclude from capture: " + Native.HideFromCapture(hwnd));
            };

            // 开着的时候持续刷新背景，桌面变了玻璃也跟着变
            if (_glassWatch == null)
            {
                _glassWatch = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(110) };
                _glassWatch.Tick += (_, _) => RefreshGlassAsync();
            }
        }

        /// <summary>锚定的刻度中心：换图标大小后仍对齐同一根刻度。</summary>
        private double _anchorX;

        /// <summary>按当前图标尺寸和条目数算出面板该多宽多高。</summary>
        private (double w, double h) MeasurePanel()
        {
            var box = SystemParameters.WorkArea;
            var m = CardMetrics.For(_data.IconSize);

            // 列数按条目数长：三列起步、八列封顶，且尽量不留"末行只有一个"的孤儿格
            int total = _data.Items.Count + 1;

            int cols = 8, rows = (int)Math.Ceiling(total / 8.0), waste = cols * rows - total;
            for (int c = 3; c <= 8; c++)
            {
                int r = (int)Math.Ceiling(total / (double)c);
                if (r > 3) continue;

                int w2 = c * r - total;
                if (w2 < waste) { cols = c; rows = r; waste = w2; }
            }

            double w = 24 + cols * (m.CardWidth + 2);
            double h = Math.Clamp(66 + 12 + rows * (m.CardHeight + 2) + 10, 180, box.Height - 140);
            return (w, h);
        }

        /// <summary>换位时把面板平移到新的刻度下方。</summary>
        internal void GlideTo(double centerX)
        {
            var box = SystemParameters.WorkArea;
            _anchorX = centerX;

            double left = Math.Max(box.Left + 12, Math.Min(centerX - Width / 2, box.Right - 12 - Width));

            var anim = new DoubleAnimation(Left, left, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
            };

            anim.Completed += (_, _) =>
            {
                BeginAnimation(LeftProperty, null);
                Left = left;
            };

            BeginAnimation(LeftProperty, anim);
        }

        /// <summary>图标大小变了之后，窗口要跟着重新量一遍，否则内容会被旧尺寸裁掉。</summary>
        private void ResizeToContent()
        {
            if (!_expanded) return;

            var box = SystemParameters.WorkArea;
            var (w, h) = MeasurePanel();

            Width = w;
            Height = h;
            Left = Math.Max(box.Left + 12, Math.Min(_anchorX - w / 2, box.Right - 12 - w));
            Top = Math.Min(Top, box.Bottom - h - 12);

            // 展开动画留下的裁剪矩形是旧尺寸，必须换掉
            RootGrid.Clip = new RectangleGeometry(new Rect(0, 0, w, h));
            UpdateClip();
        }

        /// <summary>从轨的下沿垂落展开。centerX 是刻度中心，top 是轨的下沿。</summary>
        public async Task OpenPanelAsync(double centerX, double top)
        {
            if (_expanded) return;

            var box = SystemParameters.WorkArea;
            _anchorX = centerX;
            var openClock = System.Diagnostics.Stopwatch.StartNew();

            var (w, h) = MeasurePanel();

            double left = Math.Max(box.Left + 12, Math.Min(centerX - w / 2, box.Right - 12 - w));
            double t = Math.Min(top, box.Bottom - h - 12);

            RootGrid.Opacity = 0;
            Left = left;
            Top = t;
            Width = w;
            Height = h;

            // 先抓背景再显示窗口：窗口一显示，抓到的就是它自己（或正在消失的预览卡）
            var clock2 = System.Diagnostics.Stopwatch.StartNew();
            CaptureBackdropNow();
            Log.Write($"backdrop: {_data.Name} {clock2.ElapsedMilliseconds}ms");

            _opening = true;
            Show();
            UpdateLayout();

            _expanded = true;
            _opening = false;
            UpdateClip();
            ExpandedLayer.Visibility = Visibility.Visible;
            ExpandedLayer.Opacity = 1;
            ExpandedLayer.RenderTransform = null;
            LoadItems(loadIcons: false);

            var geo = new RectangleGeometry(new Rect(0, 0, w, 0));
            RootGrid.Clip = geo;

            _glassWatch?.Start();

            // 图标和动画并行：命中缓存的已经贴上了，未命中的在这 300ms 里补完
            var icons = LoadIconsAsync();

            await Task.WhenAll(
                AnimateClip(geo, new Rect(0, 0, w, h), 300),
                Animate(RootGrid, UIElement.OpacityProperty, 1, 170));

            await icons;

            Log.Write($"open: {_data.Name} 动画 {openClock.ElapsedMilliseconds}ms");

            RootGrid.Clip = null;
            WindowStyleHelper.SetTopmost(this);
        }

        public async void ClosePanelNow()
        {
            if (!_expanded) return;
            _expanded = false;

            try
            {
                double w = Width;
                double h = Height;

                var geo = new RectangleGeometry(new Rect(0, 0, w, h));
                RootGrid.Clip = geo;

                await Task.WhenAll(
                    AnimateClip(geo, new Rect(0, 0, w, 0), 170),
                    Animate(RootGrid, UIElement.OpacityProperty, 0, 170));
            }
            catch { }

            RootGrid.Clip = null;
            ExpandedLayer.Visibility = Visibility.Collapsed;
            Hide();
        }

        private static Task AnimateClip(RectangleGeometry geo, Rect to, int ms)
        {
            var tcs = new TaskCompletionSource();

            var anim = new RectAnimation(geo.Rect, to, TimeSpan.FromMilliseconds(ms))
            {
                EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
            };

            anim.Completed += (_, _) =>
            {
                geo.BeginAnimation(RectangleGeometry.RectProperty, null);
                geo.Rect = to;
                tcs.TrySetResult();
            };

            geo.BeginAnimation(RectangleGeometry.RectProperty, anim);
            return tcs.Task;
        }

        private int _glassSignature;

        private void CaptureBackdropNow()
        {
            int x = PixelX(), y = PixelY(), w = PixelW(), h = PixelH();

            var shot = Glass.Capture(x, y, w, h, out int sig);
            if (shot == null) return;

            if (sig == _glassSignature) return;   // 桌面没动静，别白贴一张

            _glassSignature = sig;
            Backdrop.Source = shot;
        }

        private bool _glassBusy;
        private int _glassSamples;

        /// <summary>抓屏和模糊丢到后台线程，别占 UI 线程；结果回 UI 线程贴图。</summary>
        private async void RefreshGlassAsync()
        {
            if (!_expanded || _glassBusy || !IsVisible) return;

            _glassBusy = true;
            try
            {
                int x = PixelX(), y = PixelY(), w = PixelW(), h = PixelH();

                var sw = System.Diagnostics.Stopwatch.StartNew();
                var result = await Task.Run(() =>
                {
                    var img = Glass.Capture(x, y, w, h, out int sig);
                    return (img, sig);
                });
                sw.Stop();

                // 按单次耗时自动调间隔：太快会吃 CPU，太慢会看出延迟
                int ms = (int)Math.Clamp(sw.ElapsedMilliseconds * 4, 45, 260);
                if (_glassWatch != null && Math.Abs(_glassWatch.Interval.TotalMilliseconds - ms) > 20)
                    _glassWatch.Interval = TimeSpan.FromMilliseconds(ms);

                if (result.img != null && _expanded && result.sig != _glassSignature)
                {
                    _glassSignature = result.sig;
                    Backdrop.Source = result.img;
                }
            }
            catch { }
            finally { _glassBusy = false; }
        }

        private void UpdateClip()
        {
            double r = _expanded ? 26 : 23;
            double w = GlassShell.ActualWidth;
            double h = GlassShell.ActualHeight;
            if (w <= 0 || h <= 0) return;

            GlassShell.Clip = new RectangleGeometry(new Rect(0, 0, w, h), r, r);
            GlassShell.CornerRadius = new CornerRadius(r);
            HairlineBorder.CornerRadius = new CornerRadius(r);
        }

        private int PixelX() => (int)Math.Round(Left * VisualTreeHelper.GetDpi(this).DpiScaleX);
        private int PixelY() => (int)Math.Round(Top * VisualTreeHelper.GetDpi(this).DpiScaleY);
        private int PixelW() => (int)Math.Round(Width * VisualTreeHelper.GetDpi(this).DpiScaleX);
        private int PixelH() => (int)Math.Round(Height * VisualTreeHelper.GetDpi(this).DpiScaleY);

        public void SavePng(string path)
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            var bitmap = new RenderTargetBitmap(
                (int)Math.Ceiling(ActualWidth * dpi.DpiScaleX),
                (int)Math.Ceiling(ActualHeight * dpi.DpiScaleY),
                96 * dpi.DpiScaleX,
                96 * dpi.DpiScaleY,
                PixelFormats.Pbgra32);

            bitmap.Render(RootGrid);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));

            using var stream = File.Create(path);
            encoder.Save(stream);
        }

        // ==================== 展开 / 收起 ====================

        // ==================== 收起 ====================

        private Task CollapseAsync(string reason = "")
        {
            if (!_expanded) return Task.CompletedTask;

            Log.Write($"collapse [{reason}] {_data.Name}");
            ClosePanelNow();
            return Task.CompletedTask;
        }

        private static Task Animate(DependencyObject target, DependencyProperty prop, double to, int ms)
        {
            var tcs = new TaskCompletionSource();

            double from;
            try { from = Convert.ToDouble(target.GetValue(prop)); }
            catch { from = 0; }

            var anim = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(ms))
            {
                EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
            };

            anim.Completed += (_, _) =>
            {
                if (target is IAnimatable a) a.BeginAnimation(prop, null);
                target.SetValue(prop, to);
                tcs.TrySetResult();
            };

            if (target is IAnimatable animatable) animatable.BeginAnimation(prop, anim);
            else tcs.TrySetResult();

            return tcs.Task;
        }

        // ==================== 内容 ====================

        private void LoadItems(bool loadIcons = true)
        {
            MigrateDesktopEntries();

            _items.Clear();

            foreach (var item in _data.Items)
            {
                if (string.IsNullOrWhiteSpace(item.Path)) continue;

                var card = new DrawerItem(item.Path, iconCache: item.IconCache,
                    metrics: CardMetrics.For(_data.IconSize));

                var cached = IconMemory.Get(IconMemory.KeyFor(item.IconCache, item.Path));
                if (cached != null) card.Icon = cached;

                _items.Add(card);
            }

            _addTile.MetricsOverride(CardMetrics.For(_data.IconSize));
            _items.Add(_addTile);

            UpdateLabels();
            if (loadIcons) _ = LoadIconsAsync();
        }

        /// <summary>
        /// 老数据（只存引用）自动补搬：桌面上已收进抽屉的项目，把文件真正搬进来。
        /// 搬过之后 OriginalPath 非空，不会重复尝试。
        /// </summary>
        private void MigrateDesktopEntries()
        {
            bool changed = false;

            foreach (var entry in _data.Items.ToArray())
            {
                if (!string.IsNullOrEmpty(entry.OriginalPath)) continue;
                if (!File.Exists(entry.Path) && !Directory.Exists(entry.Path)) continue;
                if (!DesktopIcon.IsOnDesktop(entry.Path)) continue;

                if (ItemStore.TryMoveIn(entry.Path, _data.Id, out string stored))
                {
                    entry.OriginalPath = entry.Path;
                    entry.Path = stored;
                    changed = true;
                }
            }

            if (changed)
            {
                _manager.Save();
                Log.Write($"migrated desktop entries for {_data.Name}");
            }
        }

        private async Task LoadIconsAsync()
        {
            var snapshot = _items.Where(i => !i.IsAddTile).ToArray();
            if (snapshot.Length == 0) return;

            var clock = System.Diagnostics.Stopwatch.StartNew();
            int misses = 0;

            await Task.Run(() =>
            {
                var batch = new List<(DrawerItem item, ImageSource src)>(8);

                void Flush()
                {
                    if (batch.Count == 0) return;
                    var copy = batch.ToArray();
                    batch.Clear();

                    Application.Current?.Dispatcher.Invoke(() =>
                    {
                        foreach (var (item, src) in copy) item.Icon = src;
                    });
                }

                foreach (var item in snapshot)
                {
                    ImageSource? src;
                    try
                    {
                        string key = IconMemory.KeyFor(item.IconCachePath, item.Path);
                        src = IconMemory.Get(key);

                        if (src == null)
                        {
                            // 1) 搬走前的快照最可靠；2) 解析快捷方式真实图标源；3) 退回 shell
                            src = IconSnapshot.Load(item.IconCachePath);

                            if (src == null)
                            {
                                misses++;
                                string? source = ShortcutIcon.ResolveSource(item.Path);
                                src = source != null
                                    ? IconProvider.Get(source) ?? IconProvider.Get(item.Path)
                                    : IconProvider.Get(item.Path);
                            }

                            IconMemory.Put(key, src);
                        }
                    }
                    catch { src = null; }

                    if (src != null) batch.Add((item, src));
                    if (batch.Count >= 8) Flush();
                }

                Flush();
            });

            clock.Stop();
            Log.Write($"icons: {_data.Name} {snapshot.Length} 个 用时 {clock.ElapsedMilliseconds}ms 未命中 {misses}");
        }

        private void UpdateLabels()
        {
            TitleText.Text = _data.Name;

            int count = _data.Items.Count;
            SubTitleText.Text = count == 0 ? "拖入程序，或点 ＋" : $"{count} 项";
        }

        private void AddPaths(IEnumerable<string> paths, bool moveFromDesktop = true)
        {
            int added = 0;
            var stuck = new List<string>();

            foreach (string raw in paths)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;

                string path = raw.Trim().Replace('/', '\\');
                bool isShell = ShellItem.IsShellPath(path);

                bool exists = isShell || File.Exists(path) || Directory.Exists(path);
                if (!exists) continue;

                if (_data.Items.Any(i => string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase)))
                    continue;

                var entry = new ItemData { Path = path };

                // 趁文件还在原位，先把图标抓下来
                entry.IconCache = IconSnapshot.Capture(path, _data.Id);

                if (moveFromDesktop && DesktopIcon.IsOnDesktop(path))
                {
                    if (ItemStore.TryMoveIn(path, _data.Id, out string stored))
                    {
                        entry.OriginalPath = path;
                        entry.Path = stored;
                    }
                    else
                    {
                        // 多半是程序正在运行，文件被锁，搬不动
                        stuck.Add(Path.GetFileName(path));
                    }
                }

                _data.Items.Add(entry);
                added++;
            }

            if (added == 0) return;

            _manager.Save();
            LoadItems();

            if (stuck.Count > 0)
            {
                MessageBox.Show(this,
                    "这些项目搬不进抽屉（程序可能正在运行，文件被占用）：\n\n"
                    + string.Join("\n", stuck)
                    + "\n\n它们已作为快捷引用保留在抽屉里，桌面图标暂时还在。关掉程序后右键该项目选「从桌面收进抽屉」即可。",
                    "Nook", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void RemoveItem(DrawerItem item)
        {
            if (item.IsAddTile) return;

            var target = _data.Items.FirstOrDefault(
                i => string.Equals(i.Path, item.Path, StringComparison.OrdinalIgnoreCase));

            if (target != null)
            {
                ItemStore.MoveBack(target);
                IconSnapshot.Delete(target.IconCache);
                _data.Items.Remove(target);
            }

            _manager.Save();
            LoadItems();
        }

        /// <summary>把一个只被引用的桌面项目真正搬进抽屉。</summary>
        private void TakeInFromDesktop(DrawerItem item)
        {
            var target = _data.Items.FirstOrDefault(
                i => string.Equals(i.Path, item.Path, StringComparison.OrdinalIgnoreCase));
            if (target == null || !string.IsNullOrEmpty(target.OriginalPath)) return;

            if (!DesktopIcon.IsOnDesktop(target.Path)) return;

            if (ItemStore.TryMoveIn(target.Path, _data.Id, out string stored))
            {
                target.OriginalPath = target.Path;
                target.Path = stored;
                _manager.Save();
                LoadItems();
            }
        }

        private void RestoreToDesktop(DrawerItem item)
        {
            var target = _data.Items.FirstOrDefault(
                i => string.Equals(i.Path, item.Path, StringComparison.OrdinalIgnoreCase));
            if (target == null) return;

            // 还原 = 文件搬回桌面 + 条目离开抽屉。留着条目会被自动迁移再次搬回来。
            ItemStore.MoveBack(target);
            _data.Items.Remove(target);

            Log.Write($"restore to desktop: {target.Path}");
            _manager.Save();
            LoadItems();
        }

        internal void SetIconSize(string size)
        {
            if (_data.IconSize == size) return;

            _data.IconSize = size;
            _manager.Save();

            ResizeToContent();
            LoadItems();
            UpdateLayout();

            Log.Write($"icon size: {size} ({_data.Name}) -> {Width:F0}x{Height:F0}");
        }

        private void RecaptureIcon(DrawerItem item)
        {
            var entry = _data.Items.FirstOrDefault(
                i => string.Equals(i.Path, item.Path, StringComparison.OrdinalIgnoreCase));
            if (entry == null) return;

            IconSnapshot.Delete(entry.IconCache);
            entry.IconCache = IconSnapshot.Capture(entry.Path, _data.Id);
            IconMemory.Clear();

            Log.Write($"icon recaptured: {entry.Path}");
            _manager.Save();
            LoadItems();
        }

        /// <summary>趁图标源还在，把还没快照的条目补抓一遍。</summary>
        private void SnapshotIcons()
        {
            int captured = 0;

            foreach (var entry in _data.Items)
            {
                if (!string.IsNullOrEmpty(entry.IconCache) && File.Exists(entry.IconCache)) continue;

                string? png = IconSnapshot.Capture(entry.Path, _data.Id);
                if (png != null)
                {
                    entry.IconCache = png;
                    captured++;
                }
            }

            if (captured == 0) return;

            Log.Write($"icon snapshot: {captured} item(s) in {_data.Name}");
            _manager.Save();
            LoadItems();
        }

        public void ReloadIcons() => LoadItems();

        private void RestoreAllInDrawer()
        {
            int restored = 0;

            foreach (var entry in _data.Items.ToArray())
            {
                if (string.IsNullOrEmpty(entry.OriginalPath)) continue;

                ItemStore.MoveBack(entry);
                _data.Items.Remove(entry);
                restored++;
            }

            if (restored == 0) return;

            Log.Write($"restore all: {restored} item(s) from {_data.Name}");
            _manager.Save();
            LoadItems();
        }

        private void TakeAllFromDesktop()
        {
            foreach (var entry in _data.Items.ToArray())
            {
                if (!string.IsNullOrEmpty(entry.OriginalPath)) continue;
                if (!DesktopIcon.IsOnDesktop(entry.Path)) continue;

                if (ItemStore.TryMoveIn(entry.Path, _data.Id, out string stored))
                {
                    entry.OriginalPath = entry.Path;
                    entry.Path = stored;
                }
            }

            _manager.Save();
            LoadItems();
        }

        private void AddViaDialog()
        {
            var dialog = new OpenFileDialog
            {
                Title = "选择要放进抽屉的程序",
                Multiselect = true,
                Filter = "程序与快捷方式|*.lnk;*.exe;*.bat;*.cmd;*.url;*.appref-ms|所有文件|*.*"
            };

            if (dialog.ShowDialog(this) == true) AddPaths(dialog.FileNames);
        }

        // ==================== 菜单 ====================

        private void Panel_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            var menu = NewMenu();

            menu.Items.Add(NewItem("重命名", BeginRename));
            menu.Items.Add(NewItem("添加程序…", AddViaDialog));
            menu.Items.Add(new Separator { Style = (Style)FindResource("NookSeparator") });
            menu.Items.Add(NewItem("新建抽屉", () => _manager.CreateDrawer()));
            menu.Items.Add(PinItem());
            menu.Items.Add(NewItem("删除此抽屉", ConfirmDelete));
            menu.Items.Add(new Separator { Style = (Style)FindResource("NookSeparator") });
            menu.Items.Add(NewItem("退出 Nook", () => Application.Current.Shutdown()));

            ShowMenu(menu);
            e.Handled = true;
        }

        private void MenuBtn_Click(object sender, MouseButtonEventArgs e)
        {
            var menu = NewMenu();
            menu.Items.Add(NewItem("重命名", BeginRename));
            menu.Items.Add(NewItem("添加程序…", AddViaDialog));
            menu.Items.Add(new Separator { Style = (Style)FindResource("NookSeparator") });
            int index = _manager.State.Drawers.IndexOf(_data);
            int total = _manager.State.Drawers.Count;

            var left = NewItem("在轨道上左移", () => _manager.MoveDrawer(_data, -1));
            left.IsEnabled = index > 0;

            var right = NewItem("在轨道上右移", () => _manager.MoveDrawer(_data, 1));
            right.IsEnabled = index >= 0 && index < total - 1;

            menu.Items.Add(left);
            menu.Items.Add(right);
            menu.Items.Add(new Separator { Style = (Style)FindResource("NookSeparator") });
            menu.Items.Add(NewItem("新建抽屉", () => _manager.CreateDrawer()));
            menu.Items.Add(NewItem("删除此抽屉", ConfirmDelete));
            menu.Items.Add(new Separator { Style = (Style)FindResource("NookSeparator") });
            menu.Items.Add(NewItem("把桌面项目都收进来", TakeAllFromDesktop));
            menu.Items.Add(NewItem("补抓缺失图标", SnapshotIcons));
            menu.Items.Add(new Separator { Style = (Style)FindResource("NookSeparator") });
            menu.Items.Add(SizeItem("小图标", "small"));
            menu.Items.Add(SizeItem("中图标", "medium"));
            menu.Items.Add(SizeItem("大图标", "large"));
            menu.Items.Add(NewItem("全部还原到桌面", RestoreAllInDrawer));
            menu.Items.Add(new Separator { Style = (Style)FindResource("NookSeparator") });
            menu.Items.Add(NewItem("收起", () => _ = CollapseAsync("menu")));

            ShowMenu(menu);
            e.Handled = true;
        }

        private void ShowMenu(ContextMenu menu)
        {
            menu.PlacementTarget = this;
            menu.Placement = PlacementMode.MousePoint;

            menu.Opened += (_, _) => SuppressAutoCollapse = true;
            menu.Closed += (_, _) => SuppressAutoCollapse = false;
            menu.IsOpen = true;
        }

        private ContextMenu NewMenu() => new() { Style = (Style)FindResource("NookMenu") };

        private MenuItem PinItem()
        {
            var item = new MenuItem
            {
                Header = _data.Pinned ? "取消固定" : "固定",
                IsCheckable = true,
                IsChecked = _data.Pinned
            };

            item.Click += (_, _) => TogglePinned();
            return item;
        }

        private MenuItem SizeItem(string header, string size)
        {
            var item = new MenuItem
            {
                Header = header,
                IsCheckable = true,
                IsChecked = _data.IconSize == size
            };

            item.Click += (_, _) => SetIconSize(size);
            return item;
        }

        private static MenuItem NewItem(string header, Action action)
        {
            var mi = new MenuItem { Header = header };
            mi.Click += (_, _) => action();
            return mi;
        }

        private void ConfirmDelete()
        {
            var result = MessageBox.Show(this,
                $"删除抽屉「{_data.Name}」？\n\n抽屉里的程序快捷方式不会被删除，只是从这个抽屉里移除。",
                "Nook", MessageBoxButton.OKCancel, MessageBoxImage.Question);

            if (result == MessageBoxResult.OK)
                _manager.DeleteDrawer(_data);
        }

        // ==================== 交互：卡片 ====================

        private void Card_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.DataContext is not DrawerItem item) return;

            if (item.IsAddTile)
            {
                AddViaDialog();
                e.Handled = true;
                return;
            }

            Log.Write($"card down {item.Name} click={e.ClickCount}");

            if (e.ClickCount == 2)
            {
                OpenItem(item);
                e.Handled = true;
                return;
            }

            SelectOnly(item);
            _dragCandidate = item;
            _dragStart = e.GetPosition(this);
            e.Handled = true;
        }

        private void Card_MouseMove(object sender, MouseEventArgs e)
        {
            if (_dragCandidate == null || e.LeftButton != MouseButtonState.Pressed) return;

            var p = e.GetPosition(this);
            if (Math.Abs(p.X - _dragStart.X) < 8 && Math.Abs(p.Y - _dragStart.Y) < 8) return;

            var item = _dragCandidate;
            _dragCandidate = null;

            int index = _items.IndexOf(item);
            if (index < 0) return;

            _draggingOut = true;
            _internalDrop = false;
            DropHint.Visibility = Visibility.Collapsed;

            var data = new DataObject();
            data.SetData(ItemFormat, index);
            data.SetData(DataFormats.FileDrop, new[] { item.Path });

            DragDropEffects effect = DragDropEffects.None;
            try
            {
                effect = DragDrop.DoDragDrop((DependencyObject)sender, data,
                    DragDropEffects.Move | DragDropEffects.Copy);
            }
            catch { }
            finally
            {
                _draggingOut = false;
                _dragEndedAt = DateTime.Now;
                InsertMarker.Visibility = Visibility.Collapsed;
            }

            // 拖到抽屉外面并且真的被"移动"走了 → 条目离开抽屉，保持一一对应
            if (!_internalDrop && effect == DragDropEffects.Move)
            {
                var tracked = _data.Items.FirstOrDefault(
                    i => string.Equals(i.Path, item.Path, StringComparison.OrdinalIgnoreCase));

                if (tracked != null)
                {
                    _data.Items.Remove(tracked);
                    Log.Write($"dragged out: {tracked.Path}");
                    _manager.Save();
                    LoadItems();
                }
            }
        }

        private void Card_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => _dragCandidate = null;

        private void Card_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.DataContext is not DrawerItem item) return;
            if (item.IsAddTile) { e.Handled = true; return; }

            SelectOnly(item);

            var menu = NewMenu();
            menu.Items.Add(NewItem("打开", () => OpenItem(item)));

            if (!ShellItem.IsShellPath(item.Path))
                menu.Items.Add(NewItem("在资源管理器中显示", () => RevealItem(item)));

            var entryData = _data.Items.FirstOrDefault(
                i => string.Equals(i.Path, item.Path, StringComparison.OrdinalIgnoreCase));

            if (entryData != null && !string.IsNullOrEmpty(entryData.OriginalPath))
                menu.Items.Add(NewItem("还原到桌面（移出抽屉）", () => RestoreToDesktop(item)));
            else if (DesktopIcon.IsOnDesktop(item.Path))
                menu.Items.Add(NewItem("从桌面收进抽屉", () => TakeInFromDesktop(item)));

            menu.Items.Add(new Separator { Style = (Style)FindResource("NookSeparator") });
            menu.Items.Add(NewItem("重新抓取图标", () => RecaptureIcon(item)));
            menu.Items.Add(NewItem("从抽屉移除", () => RemoveItem(item)));

            menu.PlacementTarget = fe;
            ShowMenu(menu);
            e.Handled = true;
        }

        private void SelectOnly(DrawerItem item)
        {
            foreach (var i in _items) i.IsSelected = ReferenceEquals(i, item);
        }

        private void OpenItem(DrawerItem item)
        {
            if (item.IsAddTile) { AddViaDialog(); return; }

            string path = item.Path;
            string ext = Path.GetExtension(path).ToLowerInvariant();

            try
            {
                if (ShellItem.IsShellPath(path))
                {
                    ShellItem.Open(path);
                    Log.Write($"open shell: {item.Name} | {path}");
                }
                else
                {
                    var proc = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                    Log.Write($"open: {item.Name} | {ext} | " +
                              (proc != null ? $"pid={proc.Id}" : "无进程句柄"));

                    // 有的程序会"启动即退出"（缺依赖、需要提权被拒、重复实例），这里抓出来
                    if (proc != null) _ = WatchLaunch(proc, item.Name, path);
                }

                _ = CollapseAsync();
            }
            catch (Exception ex)
            {
                Log.Write($"open FAILED: {item.Name} | {ext} | {path} | {ex.GetType().Name}: {ex.Message}");
                TryFallback(item, path, ext);
            }
        }

        /// <summary>启动失败时，退回原始位置再试一次（搬动可能弄坏了相对路径）。</summary>
        private void TryFallback(DrawerItem item, string path, string ext)
        {
            var entry = _data.Items.FirstOrDefault(
                i => string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase));

            string? original = entry?.OriginalPath;
            if (string.IsNullOrEmpty(original) || !File.Exists(original)) return;

            try
            {
                Process.Start(new ProcessStartInfo(original) { UseShellExecute = true });
                Log.Write($"open fallback OK: {item.Name} | {original}");
            }
            catch (Exception ex)
            {
                Log.Write($"open fallback FAILED: {item.Name} | {original} | {ex.Message}");
            }
        }

        private static async Task WatchLaunch(Process proc, string name, string path)
        {
            try
            {
                bool exited = await Task.Run(() => proc.WaitForExit(1800));
                if (!exited) return;

                int code = -1;
                try { code = proc.ExitCode; } catch { }

                Log.Write($"open 秒退: {name} | code={code} | {path}");
            }
            catch { }
        }

        private static void RevealItem(DrawerItem item)
        {
            try { Process.Start("explorer.exe", $"/select,\"{item.Path}\""); }
            catch { }
        }

        // ==================== 拖入 ====================

        private void OnDragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(ItemFormat))
            {
                e.Effects = DragDropEffects.Move;

                var p = e.GetPosition(ItemsHost);
                int insertAt = ResolveInsertIndex(p);
                ShowInsertMarker(p, insertAt);
                DropHint.Visibility = Visibility.Collapsed;
            }
            else if (e.Data.GetDataPresent(DataFormats.FileDrop) || ShellDrop.HasIdList(e.Data))
            {
                e.Effects = DragDropEffects.Copy;
                InsertMarker.Visibility = Visibility.Collapsed;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }

            e.Handled = true;
        }

        private int ResolveInsertIndex(Point p)
        {
            for (int i = 0; i < _items.Count; i++)
            {
                if (ItemsHost.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement fe) continue;

                var origin = fe.TranslatePoint(new Point(0, 0), ItemsHost);
                var rect = new Rect(origin, fe.RenderSize);

                if (p.Y < rect.Top) return i;
                if (rect.Contains(p))
                    return p.X >= rect.Left + rect.Width / 2 ? i + 1 : i;
            }

            return _items.Count;
        }

        private void ShowInsertMarker(Point p, int insertAt)
        {
            int anchor = insertAt >= _items.Count ? _items.Count - 1 : insertAt;
            if (anchor < 0) { InsertMarker.Visibility = Visibility.Collapsed; return; }

            if (ItemsHost.ItemContainerGenerator.ContainerFromIndex(anchor) is not FrameworkElement fe)
            {
                InsertMarker.Visibility = Visibility.Collapsed;
                return;
            }

            var origin = fe.TranslatePoint(new Point(0, 0), ItemsHost);
            double x = insertAt > anchor ? origin.X + fe.RenderSize.Width : origin.X;

            Canvas.SetLeft(InsertMarker, x - 1.5);
            Canvas.SetTop(InsertMarker, origin.Y + 12);
            InsertMarker.Visibility = Visibility.Visible;
        }

        private void ReorderTo(int fromIndex, int insertAt)
        {
            if (fromIndex < 0 || fromIndex >= _data.Items.Count) return;

            insertAt = Math.Min(insertAt, _data.Items.Count);
            if (insertAt == fromIndex || insertAt == fromIndex + 1) return;

            var item = _data.Items[fromIndex];
            _data.Items.RemoveAt(fromIndex);
            if (insertAt > fromIndex) insertAt--;

            insertAt = Math.Clamp(insertAt, 0, _data.Items.Count);
            _data.Items.Insert(insertAt, item);

            Log.Write($"reorder: {fromIndex} -> {insertAt} ({item.Path})");
            _manager.Save();
            LoadItems();
        }

        private void OnDrop(object sender, DragEventArgs e)
        {
            DropHint.Visibility = Visibility.Collapsed;
            _pendingCollapse = false;
            _collapseWatch.Stop();

            if (e.Data.GetDataPresent(ItemFormat))
            {
                _internalDrop = true;
                InsertMarker.Visibility = Visibility.Collapsed;

                if (e.Data.GetData(ItemFormat) is int fromIndex)
                {
                    int insertAt = ResolveInsertIndex(e.GetPosition(ItemsHost));

                    // 此刻还嵌套在 DoDragDrop 的消息循环里，改数据源要等它退出
                    Dispatcher.BeginInvoke(new Action(() => ReorderTo(fromIndex, insertAt)));
                }

                return;
            }

            try { Log.Write("drop formats: " + string.Join(" | ", e.Data.GetFormats())); } catch { }

            var incoming = new List<string>();

            if (e.Data.GetDataPresent(DataFormats.FileDrop)
                && e.Data.GetData(DataFormats.FileDrop) is string[] files)
                incoming.AddRange(files);

            // 虚拟对象（我的电脑 / 回收站）通常只给 IDList
            incoming.AddRange(ShellDrop.Read(e.Data));

            if (incoming.Count == 0) return;

            AddPaths(incoming);
        }

        // ==================== 重命名 ====================

        private void TitleText_Click(object sender, MouseButtonEventArgs e)
        {
            BeginRename();
            e.Handled = true;
        }

        private void BeginRename()
        {
            if (!_expanded) return;

            _renaming = true;
            TitleEditor.Text = _data.Name;
            TitleText.Visibility = Visibility.Collapsed;
            TitleEditor.Visibility = Visibility.Visible;
            TitleEditor.Focus();
            TitleEditor.SelectAll();
        }

        private void CommitRename()
        {
            if (!_renaming) return;
            _renaming = false;

            string name = TitleEditor.Text.Trim();
            if (!string.IsNullOrEmpty(name) && name != _data.Name)
            {
                _data.Name = name;
                _manager.Save();
            }

            TitleEditor.Visibility = Visibility.Collapsed;
            TitleText.Visibility = Visibility.Visible;
            UpdateLabels();
        }

        private void CancelRename()
        {
            _renaming = false;
            TitleEditor.Visibility = Visibility.Collapsed;
            TitleText.Visibility = Visibility.Visible;
        }

        private void TitleEditor_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) { CommitRename(); e.Handled = true; }
            else if (e.Key == Key.Escape) { CancelRename(); e.Handled = true; }
        }

        private void TitleEditor_LostFocus(object sender, RoutedEventArgs e) => CommitRename();
    }

    internal static class WindowStyleHelper
    {
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int index);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int index, int value);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter,
            int x, int y, int cx, int cy, uint flags);

        private static readonly IntPtr HWND_TOPMOST = new(-1);
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOACTIVATE = 0x0010;

        /// <summary>压到置顶层的最上面，但不抢焦点。</summary>
        public static void SetTopmost(Window window)
        {
            var helper = new System.Windows.Interop.WindowInteropHelper(window);
            if (helper.Handle == IntPtr.Zero) return;

            SetWindowPos(helper.Handle, HWND_TOPMOST, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        public static void HideFromAltTab(Window window)
        {
            var helper = new System.Windows.Interop.WindowInteropHelper(window);
            int style = GetWindowLong(helper.Handle, GWL_EXSTYLE);
            SetWindowLong(helper.Handle, GWL_EXSTYLE, style | WS_EX_TOOLWINDOW);
        }
    }
}
