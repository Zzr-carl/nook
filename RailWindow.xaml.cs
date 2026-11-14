using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Nook
{
    /// <summary>
    /// 顶部边缘轨。平时藏在屏幕外，鼠标推到最顶边才滑下来。
    /// 悬停某根刻度 320ms 展开对应抽屉；鼠标离开面板就收回。
    /// </summary>
    public partial class RailWindow : Window
    {
        private const double BarHeight = 15;
        private const double SlotWidth = 17;
        private const double LeftPad = 12;

        private const int DwellMs = 320;
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

            foreach (var drawer in _manager.State.Drawers)
            {
                var tick = new Border
                {
                    Width = 3,
                    Height = 9,
                    Margin = new Thickness(7, 0, 7, 0),
                    CornerRadius = new CornerRadius(0, 0, 1.5, 1.5),
                    Background = new SolidColorBrush(Color.FromArgb(0x38, 0xF4, 0xF5, 0xF7))
                };

                Ticks.Children.Add(tick);
                _ticks.Add(tick);
            }

            if (_openIndex >= _manager.State.Drawers.Count) ClosePanelNow();

            double w = RailWidth;
            Width = w;
            Left = Work.Left + (Work.Width - w) / 2;
            if (!_revealed) Top = Work.Top - BarHeight;
        }

        private void Highlight(int index)
        {
            for (int i = 0; i < _ticks.Count; i++)
            {
                bool on = i == index || i == _openIndex;

                _ticks[i].Height = on ? 15 : 9;
                _ticks[i].Background = on
                    ? new SolidColorBrush(Color.FromArgb(0xFF, 0xE9, 0xB8, 0x72))
                    : new SolidColorBrush(Color.FromArgb(0x38, 0xF4, 0xF5, 0xF7));
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
