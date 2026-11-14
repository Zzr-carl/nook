using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Input;
using System.Windows.Threading;

namespace Nook
{
    /// <summary>
    /// 顶部边缘轨。平时藏在屏幕外，鼠标推到最顶边才滑下来。
    /// 悬停某根刻度 320ms 展开对应抽屉；鼠标离开面板就收回。
    /// </summary>
    public partial class RailWindow : Window
    {
        private const double BarHeight = 22;
        private const double SlotWidth = 32;
        private const double LeftPad = 16;

        private const int DwellMs = 550;
        private const int AwayCloseMs = 420;
        private const int HideRailMs = 900;

        private readonly DrawerManager _manager;
        private readonly DispatcherTimer _poll;
        private readonly FullscreenWatcher _fullscreen;
        private readonly List<Border> _ticks = new();

        private bool _revealed;
        private int _hoverIndex = -1;
        private int _openIndex = -1;
        private DateTime _hoverSince;
        private DateTime _awaySince = DateTime.MinValue;
        private DateTime _leaveSince = DateTime.MinValue;
        private DateTime _graceUntil = DateTime.MinValue;

        private DrawerWindow? _open;
        private HintWindow? _hint;

        private bool _pressed;
        private bool _dragging;
        private System.Windows.Point _pressAt;
        private DateTime _pressAtClock;
        private int _pressIndex = -1;
        private int _dragFrom = -1;
        private int _dragTarget = -1;

        public RailWindow(DrawerManager manager)
        {
            _manager = manager;
            InitializeComponent();

            _fullscreen = new FullscreenWatcher();
            _fullscreen.Changed += fullscreen => Dispatcher.BeginInvoke(
                new Action(() => OnFullscreenChanged(fullscreen)));

            _poll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(55) };
            _poll.Tick += (_, _) => Poll();

            Loaded += (_, _) => { Rebuild(); PlaceHidden(); _poll.Start(); Reveal(); };

            MouseMove += Rail_MouseMove;
            MouseLeftButtonUp += Rail_MouseUp;
            MouseLeave += (_, _) => { if (!_pressed) DragMarker.Visibility = Visibility.Collapsed; };
            // 松手时系统会先收回捕获，此时不能清状态，否则 MouseUp 拿不到这次点击。
            // 只有"还按着却丢了捕获"（切窗口等）才算中断。
            LostMouseCapture += (_, _) =>
            {
                if (Mouse.LeftButton == MouseButtonState.Pressed) ResetDrag();
            };
            SourceInitialized += (_, _) => WindowStyleHelper.HideFromAltTab(this);
        }

        private Rect Work => SystemParameters.WorkArea;

        private double RailWidth => LeftPad * 2 + _ticks.Count * SlotWidth;

        // ==================== 位置 ====================

        private void PlaceHidden()
        {
            Width = RailWidth;
            Left = Work.Left + (Work.Width - RailWidth) / 2;
            Top = Work.Top - BarHeight;
        }

        private void Reveal()
        {
            if (_revealed) return;
            _revealed = true;
            MoveTo(Work.Top, 190);
        }

        private bool AlwaysVisible => _manager.State.RailAlwaysVisible;

        private void HideRail(bool force = false)
        {
            if (!force && AlwaysVisible) return;
            if (!_revealed) return;
            _revealed = false;
            _leaveSince = DateTime.MinValue;
            Highlight(-1);
            HideHint();
            MoveTo(Work.Top - BarHeight, 240);
        }

        private void MoveTo(double top, int ms)
        {
            var anim = new DoubleAnimation(Top, top, TimeSpan.FromMilliseconds(ms))
            {
                EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
            };

            anim.Completed += (_, _) =>
            {
                BeginAnimation(TopProperty, null);
                Top = top;
            };

            BeginAnimation(TopProperty, anim);
        }

        // ==================== 刻度 ====================

        public void Rebuild()
        {
            Ticks.Children.Clear();
            _ticks.Clear();

            _hoverIndex = -1;
            _openIndex = -1;

            foreach (var drawer in _manager.State.Drawers)
            {
                var tick = new Border
                {
                    Width = 5,
                    Height = 13,
                    CornerRadius = new CornerRadius(0, 0, 2, 2),
                    Background = new SolidColorBrush(Color.FromArgb(0x38, 0xF4, 0xF5, 0xF7)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Top,
                    IsHitTestVisible = false
                };

                // 3px 的线太细抓不住，外面套一个整格的透明热区来接鼠标
                var cell = new Border
                {
                    Width = SlotWidth,
                    Height = BarHeight,
                    Background = Brushes.Transparent,
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Child = tick
                };

                int slot = _ticks.Count;
                cell.MouseLeftButtonDown += (_, e) => BeginTickPress(slot, e);

                Ticks.Children.Add(cell);
                _ticks.Add(tick);
            }

            if (_openIndex >= _manager.State.Drawers.Count) ClosePanelNow();

            double w = RailWidth;
            Width = w;
            Height = BarHeight;
            Left = Work.Left + (Work.Width - w) / 2;
            if (!_revealed) Top = Work.Top - BarHeight;
        }

        private void Highlight(int index)
        {
            for (int i = 0; i < _ticks.Count; i++)
            {
                bool on = i == index || i == _openIndex;

                _ticks[i].Height = on ? 22 : 13;
                _ticks[i].Background = on
                    ? new SolidColorBrush(Color.FromArgb(0xFF, 0xE9, 0xB8, 0x72))
                    : new SolidColorBrush(Color.FromArgb(0x38, 0xF4, 0xF5, 0xF7));

                _ticks[i].Effect = on
                    ? new System.Windows.Media.Effects.DropShadowEffect
                    {
                        Color = Color.FromRgb(0xE9, 0xB8, 0x72),
                        BlurRadius = 14,
                        ShadowDepth = 0,
                        Opacity = 0.55
                    }
                    : null;
            }
        }

        // ==================== 主循环 ====================

        private void Poll()
        {
            if (_fullscreen.IsFullscreen)
            {
                if (_revealed) HideRail(true);
                ClosePanelNow();
                return;
            }

            if (_pressed || _dragging)
            {
                // 松手事件万一丢了，不能把状态卡死在这里。
                // 400ms 缓冲：正常松手的 MouseUp 早已到达，不会和它抢。
                if (Mouse.LeftButton == MouseButtonState.Released &&
                    (DateTime.Now - _pressAtClock).TotalMilliseconds > 400) ResetDrag();

                _leaveSince = DateTime.MinValue;
                return;
            }

            var p = CursorDip();
            bool atTopEdge = p.Y <= 2;

            if (!_revealed)
            {
                if (atTopEdge || AlwaysVisible) Reveal();
                return;
            }

            bool overRail = IsOverRail(p);
            bool overPanel = IsOverPanel(p);

            if (overRail || overPanel) _leaveSince = DateTime.MinValue;
            else if (_leaveSince == DateTime.MinValue) _leaveSince = DateTime.Now;

            // 贴到最顶边只唤出轨；往下一丁点再开始计时，避免"只看一眼"就弹面板
            int index = (overRail && p.Y >= 5) ? IndexAt(p.X) : -1;

            if (index != _hoverIndex)
            {
                _hoverIndex = index;
                _hoverSince = DateTime.Now;
                Highlight(index);

                if (index >= 0 && _open == null) ShowHint(index);
                else HideHint();
            }

            if (index >= 0 && App.HoverToOpen &&
                (DateTime.Now - _hoverSince).TotalMilliseconds >= DwellMs)
            {
                OpenIndex(index);
            }

            if (_open != null && _leaveSince != DateTime.MinValue &&
                (DateTime.Now - _leaveSince).TotalMilliseconds >= AwayCloseMs &&
                DateTime.Now >= _graceUntil && !_open.IsPinned)
            {
                ClosePanelNow();
                Highlight(_hoverIndex);
            }

            if (!AlwaysVisible && !overRail && !overPanel && _leaveSince != DateTime.MinValue &&
                (DateTime.Now - _leaveSince).TotalMilliseconds >= HideRailMs)
            {
                HideRail();
            }
        }

        private bool IsOverRail(Point p) =>
            _revealed && p.X >= Left && p.X <= Left + Width && p.Y >= Top && p.Y <= Top + BarHeight;

        private bool IsOverPanel(Point p)
        {
            if (_open == null) return false;

            return p.X >= _open.Left && p.X <= _open.Left + _open.Width
                && p.Y >= _open.Top && p.Y <= _open.Top + _open.Height;
        }

        private int IndexAt(double x)
        {
            double local = x - Left - LeftPad;
            if (local < 0) return -1;

            int i = (int)(local / SlotWidth);
            return i >= 0 && i < _ticks.Count ? i : -1;
        }

        private double TickCenter(int index) =>
            Left + LeftPad + index * SlotWidth + SlotWidth / 2;

        private Point CursorDip()
        {
            var raw = System.Windows.Forms.Cursor.Position;
            double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
            return new Point(raw.X / scale, raw.Y / scale);
        }

        // ==================== 面板 ====================

        internal DrawerWindow? OpenPanel => _open;

        internal async void OpenIndex(int index)
        {
            if (index < 0 || index >= _manager.State.Drawers.Count) return;
            if (_openIndex == index && _open != null) return;

            if (_open != null)
            {
                _open.ClosePanelNow();
                _open = null;
            }

            var data = _manager.State.Drawers[index];
            var window = _manager.GetOrCreatePanel(data);

            HideHint();

            _open = window;
            _openIndex = index;

            // 不是悬停开的（比如从托盘点抽屉），先给 2.5 秒，否则下一秒就被判"离得远"收掉
            if (!IsOverRail(CursorDip()))
            {
                _leaveSince = DateTime.MinValue;
                _graceUntil = DateTime.Now.AddMilliseconds(2500);
            }

            Highlight(index);

            try
            {
                await window.OpenPanelAsync(TickCenter(index), Work.Top + BarHeight);
            }
            catch (Exception ex)
            {
                Log.Write("open panel failed: " + ex);
            }
        }

        private void ClosePanelNow()
        {
            _open?.ClosePanelNow();
            _open = null;
            _openIndex = -1;
        }

        private void ShowHint(int index)
        {
            if (index < 0 || index >= _manager.State.Drawers.Count) { HideHint(); return; }

            _hint ??= new HintWindow();
            _hint.ShowFor(_manager.State.Drawers[index], TickCenter(index), Work.Top + BarHeight + 8);
        }

        private void HideHint() => _hint?.HideHint();

        // ==================== 点击 / 拖动排序 ====================

        private void BeginTickPress(int index, MouseButtonEventArgs e)
        {
            if (_manager.State.Drawers.Count == 0) return;

            _pressed = true;
            _pressIndex = index;
            _pressAt = e.GetPosition(this);
            _pressAtClock = DateTime.Now;

            CaptureMouse();
            e.Handled = true;
        }

        private void Rail_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_pressed) return;

            var p = e.GetPosition(this);

            if (!_dragging)
            {
                // 按下瞬间可能收到"上一个位置"的残留移动事件，先挡掉 120ms
                if ((DateTime.Now - _pressAtClock).TotalMilliseconds < 120) return;

                // 动过 5px 才算拖，否则一直等松手，当点击处理
                if (Math.Abs(p.X - _pressAt.X) < 5) return;

                _dragging = true;
                _dragFrom = _pressIndex;
                HideHint();
                _dragTarget = _pressIndex;

                ClosePanelNow();
                ShowMarker(_dragFrom);
                Log.Write($"drag begin idx={_dragFrom} dx={p.X - _pressAt.X:F1} pressX={_pressAt.X:F1} nowX={p.X:F1}");
            }

            int target = Math.Clamp(IndexAt(Left + p.X), 0, _manager.State.Drawers.Count - 1);
            if (target == _dragTarget) return;

            _dragTarget = target;
            ShowMarker(target);
        }

        private void Rail_MouseUp(object sender, MouseButtonEventArgs e)
        {
            Log.Write($"mouseup pressed={_pressed} dragging={_dragging} pressIdx={_pressIndex}");

            if (!_pressed) return;

            bool wasDragging = _dragging;
            int from = _dragFrom;
            int to = _dragTarget;
            int clicked = _pressIndex;

            ResetDrag();
            e.Handled = true;

            if (wasDragging)
            {
                Log.Write($"drag end {from}->{to}");

                if (from >= 0 && to >= 0 && from != to &&
                    from < _manager.State.Drawers.Count)
                {
                    _manager.MoveDrawer(_manager.State.Drawers[from], to - from);
                }
            }
            else if (clicked >= 0 && clicked < _manager.State.Drawers.Count)
            {
                // 没拖动 = 单击，直接展开这个抽屉
                OpenIndex(clicked);
            }
        }

        private void ResetDrag()
        {
            _pressed = false;
            _dragging = false;
            _pressIndex = -1;
            _dragFrom = _dragTarget = -1;

            if (IsMouseCaptured) ReleaseMouseCapture();
            DragMarker.Visibility = Visibility.Collapsed;
        }

        private void ShowMarker(int index)
        {
            DragMarker.Margin = new Thickness(LeftPad + index * SlotWidth - 1, 0, 0, 0);
            DragMarker.Visibility = Visibility.Visible;
        }

        /// <summary>抽屉换位后，把正开着的面板移到新刻度下方。</summary>
        public void Reanchor(DrawerData data)
        {
            if (_open == null) return;

            int index = _manager.State.Drawers.IndexOf(data);
            if (index < 0) { ClosePanelNow(); return; }

            _openIndex = index;
            _open.GlideTo(TickCenter(index));
        }

        public void CloseOpenPanel()
        {
            if (_open == null) return;
            _open.ClosePanelNow();
            _open = null;
            _openIndex = -1;
            Highlight(_hoverIndex);
        }

        public async void OpenFor(DrawerData data)
        {
            int index = _manager.State.Drawers.IndexOf(data);
            if (index < 0) return;

            Reveal();
            await System.Threading.Tasks.Task.Delay(200);
            OpenIndex(index);
        }

        private void OnFullscreenChanged(bool fullscreen)
        {
            if (!fullscreen) return;

            HideRail(true);
            ClosePanelNow();
        }

        public void SavePng(string path)
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
                (int)Math.Ceiling(ActualWidth * dpi.DpiScaleX),
                (int)Math.Ceiling(ActualHeight * dpi.DpiScaleY),
                96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY,
                PixelFormats.Pbgra32);

            bitmap.Render(Root);

            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using var fs = System.IO.File.Create(path);
            encoder.Save(fs);
        }

        public bool IsRevealed => _revealed;

        public void ShowRail() => Reveal();

        /// <summary>已经在运行时被再次启动：给出可见回应，而不是悄无声息。</summary>
        public void Pulse()
        {
            Reveal();

            foreach (var tick in _ticks)
                tick.Background = new SolidColorBrush(Color.FromArgb(0xFF, 0xE9, 0xB8, 0x72));

            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(650) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                Highlight(_hoverIndex);
            };
            timer.Start();
        }

        /// <summary>切换常驻后立即生效。</summary>
        public void ApplyAlwaysVisible()
        {
            if (AlwaysVisible) Reveal();
        }

        public void HideRailNow() => HideRail();
    }
}
