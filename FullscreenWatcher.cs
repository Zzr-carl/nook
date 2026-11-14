using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;

namespace Nook
{
    /// <summary>
    /// 检测前台窗口是否占满整个显示器（无边框全屏游戏）。
    /// 命中时所有抽屉窗口让位，避免遮挡和误触。
    /// </summary>
    internal sealed class FullscreenWatcher : IDisposable
    {
        private readonly DispatcherTimer _timer;
        private bool _last;

        public event Action<bool>? Changed;

        public bool IsFullscreen { get; private set; }

        public FullscreenWatcher()
        {
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
            _timer.Tick += (_, _) => Poll();
            _timer.Start();
        }

        private void Poll()
        {
            bool now = Detect();
            if (now == _last) return;

            _last = now;
            IsFullscreen = now;
            Log.Write($"fullscreen={now}");

            Changed?.Invoke(now);
        }

        private static bool Detect()
        {
            IntPtr fg = GetForegroundWindow();
            if (fg == IntPtr.Zero) return false;

            GetWindowThreadProcessId(fg, out uint pid);
            if (pid == (uint)Environment.ProcessId) return false;

            var cls = new StringBuilder(128);
            GetClassName(fg, cls, cls.Capacity);

            string name = cls.ToString();
            if (name is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Windows.UI.Core.CoreWindow") return false;

            // 有标题栏或处于最大化状态的，都不是独占全屏
            // （任务栏自动隐藏时，最大化窗口的矩形也会盖满整块屏幕，只靠矩形判断会误判）
            int style = GetWindowLong(fg, GWL_STYLE);
            if ((style & WS_MAXIMIZE) != 0) return false;
            if ((style & WS_CAPTION) == WS_CAPTION) return false;

            if (!GetWindowRect(fg, out RECT wr)) return false;

            IntPtr mon = MonitorFromWindow(fg, MONITOR_DEFAULTTONEAREST);
            var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (!GetMonitorInfo(mon, ref mi)) return false;

            RECT mr = mi.rcMonitor;

            return wr.Left <= mr.Left && wr.Top <= mr.Top
                && wr.Right >= mr.Right && wr.Bottom >= mr.Bottom;
        }

        public void Dispose() => _timer.Stop();

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
