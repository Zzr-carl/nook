using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;

namespace Nook
{
    /// <summary>前台窗口占了屏幕顶部的两种程度。</summary>
    internal enum Occlusion
    {
        /// <summary>桌面或普通窗口，轨道正常显示。</summary>
        None,

        /// <summary>最大化窗口：轨道让位，但鼠标顶边停留可以临时叫出来。</summary>
        Maximized,

        /// <summary>独占全屏或无边框全屏：完全让位，不打扰。</summary>
        Fullscreen
    }

    /// <summary>
    /// 判断前台窗口是否压住了屏幕顶部：独占全屏、无边框全屏、以及最大化窗口。
    /// </summary>
    internal sealed class FullscreenWatcher : IDisposable
    {
        private readonly DispatcherTimer _timer;
        private Occlusion _last = Occlusion.None;

        public event Action<Occlusion>? Changed;

        public Occlusion Mode { get; private set; } = Occlusion.None;

        public FullscreenWatcher()
        {
            // 250ms 一次：进出全屏要跟得上，游戏里手一滑碰到顶边就被误开更糟
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(140) };
            _timer.Tick += (_, _) => Poll();
            _timer.Start();
        }

        private void Poll()
        {
            var now = Detect();
            if (now == _last) return;

            _last = now;
            Mode = now;
            Log.Write($"occlusion={now}");

            Changed?.Invoke(now);
        }

        private static Occlusion Detect()
        {
            // 一、系统告诉我们有独占全屏（游戏最可靠的一条）
            if (SHQueryUserNotificationState(out int quns) == 0 &&
                quns is QUNS_BUSY or QUNS_D3D_FULL_SCREEN or QUNS_PRESENTATION)
                return Occlusion.Fullscreen;

            IntPtr fg = GetForegroundWindow();
            if (fg == IntPtr.Zero) return Occlusion.None;

            GetWindowThreadProcessId(fg, out uint pid);
            if (pid == (uint)Environment.ProcessId) return Occlusion.None;

            var cls = new StringBuilder(128);
            GetClassName(fg, cls, cls.Capacity);

            string name = cls.ToString();
            if (name is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Windows.UI.Core.CoreWindow")
                return Occlusion.None;

            int style = GetWindowLong(fg, GWL_STYLE);

            // 最大化的窗口，标题栏正压在轨道位置上
            if ((style & WS_MAXIMIZE) != 0) return Occlusion.Maximized;

            // 有标题栏的普通窗口：轨道照常出现
            if ((style & WS_CAPTION) == WS_CAPTION) return Occlusion.None;

            // 二、无边框 + 铺满整块屏幕 = 全屏
            if (!GetVisibleRect(fg, out RECT wr)) return Occlusion.None;

            IntPtr mon = MonitorFromWindow(fg, MONITOR_DEFAULTTONEAREST);
            var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (!GetMonitorInfo(mon, ref mi)) return Occlusion.None;

            RECT mr = mi.rcMonitor;

            bool covers = wr.Left <= mr.Left && wr.Top <= mr.Top
                       && wr.Right >= mr.Right && wr.Bottom >= mr.Bottom;

            return covers ? Occlusion.Fullscreen : Occlusion.None;
        }

        /// <summary>DWM 的真实可见边界；GetWindowRect 会把不可见的缩放边也算进去。</summary>
        private static bool GetVisibleRect(IntPtr hwnd, out RECT rect)
        {
            if (DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS,
                    out rect, Marshal.SizeOf<RECT>()) == 0)
                return true;

            return GetWindowRect(hwnd, out rect);
        }

        public void Dispose() => _timer.Stop();

        private const int QUNS_BUSY = 2;
        private const int QUNS_D3D_FULL_SCREEN = 3;
        private const int QUNS_PRESENTATION = 4;
        private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

        private const uint MONITOR_DEFAULTTONEAREST = 2;

        private const int GWL_STYLE = -16;
        private const int WS_MAXIMIZE = 0x01000000;
        private const int WS_CAPTION = 0x00C00000;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        [DllImport("shell32.dll")]
        private static extern int SHQueryUserNotificationState(out int state);

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out RECT value, int size);

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int index);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder name, int count);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
    }
}
