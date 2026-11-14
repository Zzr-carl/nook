using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;

namespace Nook
{
    public partial class App : Application
    {
        /// <summary>悬停刻度是否直接展开（托盘的开关改它）</summary>
        public static bool HoverToOpen { get; set; } = true;

        private Mutex? _mutex;
        private EventWaitHandle? _activate;
        private DrawerManager? _manager;
        private TrayHost? _tray;

        protected override void OnStartup(StartupEventArgs e)
        {
            _mutex = new Mutex(true, "Nook.SingleInstance.v1", out bool isFirst);
            if (!isFirst)
            {
                // 已经有实例在跑：把它叫醒，别让用户对着"双击没反应"发愣
                try
                {
                    if (EventWaitHandle.TryOpenExisting("Nook.Activate.v1", out var ev))
                    {
                        ev.Set();
                        ev.Dispose();
                    }
                }
                catch { }

                Shutdown();
                return;
            }

            _activate = new EventWaitHandle(false, EventResetMode.AutoReset, "Nook.Activate.v1");
            ListenForActivate();

            base.OnStartup(e);

            Log.Write("=== start ===");

            _manager = new DrawerManager();
            _tray = new TrayHost(_manager);
            _manager.Tray = _tray;
            _manager.Start();

            int idx = Array.IndexOf(e.Args, "--capture");
            if (idx >= 0)
            {
                string outPath = idx + 1 < e.Args.Length
                    ? e.Args[idx + 1]
                    : Path.Combine(Path.GetTempPath(), "nook-shot.png");
                string? which = idx + 2 < e.Args.Length ? e.Args[idx + 2] : null;
                string? size = idx + 3 < e.Args.Length ? e.Args[idx + 3] : null;
                _ = CaptureAsync(outPath, which, size);
            }

            if (e.Args.Contains("--selftest")) _ = SelfTestAsync();

            if (e.Args.Contains("--uicheck")) _ = UiCheckAsync();

            int ro = Array.IndexOf(e.Args, "--reorder");
            if (ro >= 0 && ro + 2 < e.Args.Length)
            {
                var target = _manager.State.Drawers.FirstOrDefault(d => d.Name == e.Args[ro + 1]);
                bool ok = target != null && _manager.MoveDrawer(target, int.Parse(e.Args[ro + 2]));
                Log.Write($"reorder {e.Args[ro + 1]} {e.Args[ro + 2]}: ok={ok}");
                Log.Write("order: " + string.Join(" | ", _manager.State.Drawers.Select(d => d.Name)));
                Shutdown();
                return;
            }

            int pf = Array.IndexOf(e.Args, "--perf");
            if (pf >= 0 && pf + 1 < e.Args.Length) _ = PerfAsync(e.Args[pf + 1]);

            int ai = Array.IndexOf(e.Args, "--autostart");
            if (ai >= 0 && ai + 1 < e.Args.Length)
            {
                bool on = e.Args[ai + 1] == "on";
                bool ok = Nook.Startup.SetEnabled(on);
                Log.Write($"autostart set {on}: ok={ok} now={Nook.Startup.IsEnabled}");
                Shutdown();
                return;
            }

            int bi = Array.IndexOf(e.Args, "--backup");
            if (bi >= 0 && bi + 1 < e.Args.Length) { _manager.BackupTo(e.Args[bi + 1]); Log.Write("backup done"); Shutdown(); return; }

            int rs = Array.IndexOf(e.Args, "--restore");
            if (rs >= 0 && rs + 1 < e.Args.Length)
            {
                bool ok = _manager.RestoreFrom(e.Args[rs + 1]);
                Log.Write("restore result: " + ok);
                Shutdown();
                return;
            }

            int ri = Array.IndexOf(e.Args, "--railshot");
            if (ri >= 0 && ri + 1 < e.Args.Length) _ = RailShotAsync(e.Args[ri + 1]);

            int si = Array.IndexOf(e.Args, "--sicon");
            if (si >= 0 && si + 2 < e.Args.Length) _ = ProbeShellAsync(e.Args[si + 1], e.Args[si + 2]);

            int pi = Array.IndexOf(e.Args, "--picon");
            if (pi >= 0 && pi + 2 < e.Args.Length) _ = ProbeIconAsync(e.Args[pi + 1], e.Args[pi + 2]);

            int di = Array.IndexOf(e.Args, "--icondiag");
            if (di >= 0 && di + 1 < e.Args.Length) _ = IconDiagAsync(e.Args[di + 1]);
        }

        /// <summary>等第二个实例来敲门：闪一下轨 + 托盘气泡，告诉用户它在哪。</summary>
        private void ListenForActivate()
        {
            var thread = new Thread(() =>
            {
                while (true)
                {
                    try { _activate?.WaitOne(); }
                    catch { return; }

                    Log.Write("activate: 已有实例，唤出轨并提示");

                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        _manager?.Rail?.Pulse();
                        _tray?.Say("Nook 已经在运行，把鼠标移到屏幕最顶端就能拉下抽屉。");
                    }));
                }
            })
            { IsBackground = true, Name = "Nook.Activate" };

            thread.Start();
        }

        // 同一个进程里连续展开两次，比较"冷"和"热"的图标加载耗时
        private async Task PerfAsync(string drawerName)
        {
            await Task.Delay(700);

            var data = _manager?.State.Drawers.FirstOrDefault(d => d.Name == drawerName)
                       ?? _manager?.State.Drawers.FirstOrDefault();
            if (data == null) { Shutdown(); return; }

            var window = _manager!.GetOrCreatePanel(data);
            var box = SystemParameters.WorkArea;

            await window.OpenPanelAsync(box.Left + box.Width / 2, box.Top + 15);
            await Task.Delay(900);

            window.ClosePanelNow();
            await Task.Delay(300);

            await window.OpenPanelAsync(box.Left + box.Width / 2, box.Top + 15);
            await Task.Delay(900);

            Log.Write("perf 完成");
            Shutdown();
        }

        // 不靠鼠标，直接驱动"展开 → 锚定 → 收回"，把几何数据打到日志里
        private async Task UiCheckAsync()
        {
            await Task.Delay(800);

            var rail = _manager?.Rail;
            if (rail == null) { Log.Write("uicheck: rail NULL"); Shutdown(); return; }

            rail.ShowRail();
            await Task.Delay(600);
            Log.Write($"uicheck rail: left={rail.Left:F0} top={rail.Top:F0} w={rail.Width:F0} vis={rail.IsVisible}");

            rail.OpenIndex(2);
            await Task.Delay(300);

            var panel = rail.OpenPanel;
            if (panel == null)
            {
                Log.Write("uicheck FAIL: panel NULL");
            }
            else
            {
                Log.Write($"uicheck panel: left={panel.Left:F0} top={panel.Top:F0} w={panel.Width:F0} h={panel.Height:F0} vis={panel.IsVisible} anchoredTop={rail.Top + rail.ActualHeight:F0}");
            }

            // 鼠标不在附近，420ms 后应自动收回
            await Task.Delay(3200);
            Log.Write($"uicheck auto-close: vis={panel?.IsVisible}");

            rail.CloseOpenPanel();
            await Task.Delay(300);
            Log.Write($"uicheck after close: vis={panel?.IsVisible}");
            Shutdown();
        }

        // 自渲染：不截屏、不抢鼠标，直接把窗口视觉树渲成 PNG
        private async Task CaptureAsync(string outPath, string? drawerName = null, string? size = null)
        {
            await Task.Delay(700);

            var target = drawerName == null
                ? _manager?.State.Drawers.FirstOrDefault()
                : _manager?.State.Drawers.FirstOrDefault(d => d.Name == drawerName)
                  ?? _manager?.State.Drawers.FirstOrDefault();

            var window = target == null ? null : _manager?.GetOrCreatePanel(target);
            if (window == null) { Shutdown(); return; }

            var box = SystemParameters.WorkArea;
            await window.OpenPanelAsync(box.Left + box.Width / 2, box.Top + 15);
            await Task.Delay(600);

            if (size != null)
            {
                string before = $"{window.Width:F0}x{window.Height:F0}";
                window.SetIconSize(size);
                await Task.Delay(400);
                Log.Write($"capture size {size}: {before} -> {window.Width:F0}x{window.Height:F0} left={window.Left:F0}");
            }

            try
            {
                window.SavePng(outPath);
                Log.Write("capture saved: " + outPath);
            }
            catch (Exception ex)
            {
                Log.Write("capture failed: " + ex);
            }

            Shutdown();
        }

        private async Task RailShotAsync(string outPath)
        {
            await Task.Delay(900);

            try
            {
                _manager?.Rail?.ShowRail();
                await Task.Delay(400);
                _manager?.Rail?.SavePng(outPath);
                Log.Write("railshot saved: " + outPath);
            }
            catch (Exception ex)
            {
                Log.Write("railshot failed: " + ex);
            }

            Shutdown();
        }

        private async Task ProbeShellAsync(string shellPath, string outPng)
        {
            await Task.Delay(250);

            try
            {
                string name = ShellItem.GetDisplayName(shellPath) ?? "(取名失败)";
                var img = IconProvider.Get(shellPath);

                if (img == null)
                {
                    Log.Write($"sicon NULL | name={name} | {shellPath}");
                }
                else
                {
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(img));
                    using (var fs = File.Create(outPng)) encoder.Save(fs);
                    Log.Write($"sicon {img.PixelWidth}x{img.PixelHeight} | name={name} | {shellPath}");
                }
            }
            catch (Exception ex)
            {
                Log.Write($"sicon EX {ex.Message} | {shellPath}");
            }

            Shutdown();
        }

        // 只测一个路径的图标，用来验证"文件在原位时能否拿到缓存图标"
        private async Task ProbeIconAsync(string path, string outPng)
        {
            await Task.Delay(250);

            try
            {
                var img = IconProvider.Get(path);
                if (img == null) Log.Write($"picon NULL | {path}");
                else
                {
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(img));
                    using (var fs = File.Create(outPng)) encoder.Save(fs);
                    Log.Write($"picon {img.PixelWidth}x{img.PixelHeight} | {path}");
                }
            }
            catch (Exception ex)
            {
                Log.Write($"picon EX {ex.Message} | {path}");
            }

            Shutdown();
        }

        // 把抽屉里每个条目的图标导出成 PNG，用来肉眼核对有没有变白
        private async Task IconDiagAsync(string outDir)
        {
            await Task.Delay(300);

            try
            {
                Directory.CreateDirectory(outDir);
                int i = 0;

                foreach (var drawer in _manager!.State.Drawers)
                {
                    foreach (var item in drawer.Items)
                    {
                        string name = Path.GetFileNameWithoutExtension(item.Path);
                        try
                        {
                            string? source = ShortcutIcon.ResolveSource(item.Path);
                            var img = (source != null ? IconProvider.Get(source) : null)
                                      ?? IconProvider.Get(item.Path);


                            if (img == null)
                            {
                                Log.Write($"icondiag {drawer.Name}/{name} | NULL");
                            }
                            else
                            {
                                string safe = string.Concat(name.Split(Path.GetInvalidFileNameChars()));
                                string file = Path.Combine(outDir, $"{i:D2}_{safe}.png");

                                var encoder = new PngBitmapEncoder();
                                encoder.Frames.Add(BitmapFrame.Create(img));
                                using (var fs = File.Create(file)) encoder.Save(fs);

                                Log.Write($"icondiag {drawer.Name}/{name} | {img.PixelWidth}x{img.PixelHeight} | src={source ?? "(none)"}");
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Write($"icondiag {name} | EX {ex}");
                        }

                        i++;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Write("icondiag failed: " + ex);
            }

            Shutdown();
        }

        // 端到端验证"搬进抽屉 / 搬回桌面"这条最容易出事的路
        private async Task SelfTestAsync()
        {
            await Task.Delay(400);

            string desk = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string source = Path.Combine(desk, "NookSelfTest.lnk");

            try
            {
                string? template = Directory.EnumerateFiles(desk, "*.lnk").FirstOrDefault();
                if (template == null)
                {
                    Log.Write("selftest: no template shortcut on desktop");
                    Shutdown();
                    return;
                }

                File.Copy(template, source, true);
                Log.Write($"selftest: created {source}");

                var entry = new ItemData { Path = source };

                if (!ItemStore.TryMoveIn(source, "selftest", out string stored))
                {
                    Log.Write("selftest FAIL: move in returned false");
                }
                else
                {
                    bool sourceGone = !File.Exists(source);
                    bool storedThere = File.Exists(stored);
                    Log.Write($"selftest: moved -> {stored} | sourceGone={sourceGone} storedThere={storedThere}");

                    entry.OriginalPath = source;
                    entry.Path = stored;

                    ItemStore.MoveBack(entry);

                    bool backHome = File.Exists(source);
                    bool storeClean = !File.Exists(stored);
                    Log.Write($"selftest: moved back -> {entry.Path} | backHome={backHome} storeClean={storeClean}");
                    Log.Write(sourceGone && storedThere && backHome && storeClean
                        ? "selftest PASS"
                        : "selftest FAIL: state mismatch");
                }

                if (File.Exists(source)) File.Delete(source);

                // 第二阶段：文件夹（跨卷曾是致命点）
                string dirTest = Path.Combine(desk, "NookSelfTestDir");
                try
                {
                    if (Directory.Exists(dirTest)) Directory.Delete(dirTest, true);
                    Directory.CreateDirectory(dirTest);
                    File.WriteAllText(Path.Combine(dirTest, "inner.txt"), "hello");

                    var dirEntry = new ItemData { Path = dirTest };

                    if (!ItemStore.TryMoveIn(dirTest, "selftest", out string dirStored))
                    {
                        Log.Write("selftest2 FAIL: folder move in returned false");
                    }
                    else
                    {
                        bool gone = !Directory.Exists(dirTest);
                        bool there = Directory.Exists(dirStored);
                        bool inner = File.Exists(Path.Combine(dirStored, "inner.txt"));
                        Log.Write($"selftest2: folder -> {dirStored} | gone={gone} there={there} inner={inner}");

                        dirEntry.OriginalPath = dirTest;
                        dirEntry.Path = dirStored;
                        ItemStore.MoveBack(dirEntry);

                        bool back = Directory.Exists(dirTest) && File.Exists(Path.Combine(dirTest, "inner.txt"));
                        Log.Write($"selftest2: folder moved back | back={back}");
                        Log.Write(gone && there && inner && back ? "selftest2 PASS" : "selftest2 FAIL: state mismatch");
                    }
                }
                catch (Exception ex)
                {
                    Log.Write("selftest2 FAIL: " + ex.Message);
                }
                finally
                {
                    try { if (Directory.Exists(dirTest)) Directory.Delete(dirTest, true); } catch { }
                }

                // 清理测试用的存储目录
                string folder = ItemStore.FolderFor("selftest");
                try { if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any()) Directory.Delete(folder); } catch { }

                Log.Write("selftest: cleaned up");
            }
            catch (Exception ex)
            {
                Log.Write("selftest FAIL: " + ex);
                try { if (File.Exists(source)) File.Delete(source); } catch { }
            }

            Shutdown();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _tray?.Dispose();
            _mutex?.Dispose();
            base.OnExit(e);
        }
    }
}
