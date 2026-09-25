// ZCode 截图监听器：全局热键或命令触发 -> 全屏遮罩框选区域 -> 保存 PNG 到 ~/.zcode/screenshot/shots
// 纯 Win32 消息循环（无 WinForms，省内存）；System.Drawing 仅用于 GDI+ 截屏与 PNG 编码。
// 懒启动（由 capture.ps1 编译并拉起），空闲或 ZCode 进程退出后自动退出；互斥锁保证单实例。
using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;

namespace ZCodeShot
{
    static class Program
    {
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr h, int id);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extra);
        [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
        [DllImport("user32.dll")] static extern ushort RegisterClassW(ref WNDCLASS wc);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr CreateWindowExW(uint exStyle, string cls, string name, uint style,
            int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
        [DllImport("user32.dll")] static extern bool DestroyWindow(IntPtr h);
        [DllImport("user32.dll")] static extern bool SetLayeredWindowAttributes(IntPtr h, uint crKey, byte alpha, uint flags);
        [DllImport("user32.dll")] static extern int GetSystemMetrics(int index);
        [DllImport("user32.dll")] static extern IntPtr LoadCursor(IntPtr h, uint id);
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("user32.dll")] static extern bool OpenClipboard(IntPtr owner);
        [DllImport("user32.dll")] static extern bool EmptyClipboard();
        [DllImport("user32.dll")] static extern IntPtr SetClipboardData(uint format, IntPtr data);
        [DllImport("user32.dll")] static extern bool CloseClipboard();
        [DllImport("gdi32.dll")] static extern IntPtr CreateSolidBrush(uint color);
        [DllImport("gdi32.dll")] static extern IntPtr CreatePen(int style, int width, uint color);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] static extern int SetROP2(IntPtr dc, int mode);
        [DllImport("gdi32.dll")] static extern bool GdiRectangle(IntPtr dc, int l, int t, int r, int b);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandleW(string name);

        delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct WNDCLASS
        {
            public uint style;
            public WndProc lpfnWndProc;
            public int cbClsExtra, cbWndExtra;
            public IntPtr hInstance, hIcon, hCursor, hbrBackground;
            public string lpszMenuName, lpszClassName;
        }
        struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public int ptX, ptY; }
        struct RECT { public int Left, Top, Right, Bottom; }

        static readonly string BaseDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".zcode", "screenshot");
        static readonly string ConfigPath = Path.Combine(BaseDir, "config.json");
        static readonly string ShotsDir = Path.Combine(BaseDir, "shots");
        static readonly string LatestPath = Path.Combine(BaseDir, "latest.txt");
        static readonly string HotkeyErrorPath = Path.Combine(BaseDir, "hotkey-error.log");

        static EventWaitHandle _trigger, _reload;

        const int HotkeyId = 0xB00B;       // 截图并隐藏当前窗口
        const int HotkeyIdNoHide = 0xB00C; // 截图不隐藏窗口
        const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_WIN = 0x8;
        const uint WM_HOTKEY = 0x0312, WM_TIMER = 0x0113, WM_DESTROY = 0x0002,
            WM_KEYDOWN = 0x0100, WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202, WM_MOUSEMOVE = 0x0200;
        const int SW_HIDE = 0, SW_SHOW = 5;
        const int R2_NOTXORPEN = 10;

        static readonly WndProc MsgWndProc = MsgProc;   // 静态引用防止委托被 GC
        static readonly WndProc OverlayWndProc = OverlayProc;
        static IntPtr _msgHwnd, _overlay;
        static IntPtr _overlayBrush;

        static uint _mods = MOD_CONTROL | MOD_ALT;
        static uint _vk = (uint)'A';
        static uint _mods2 = MOD_CONTROL | MOD_ALT | MOD_SHIFT;
        static uint _vk2 = (uint)'A';
        static DateTime _lastActive = DateTime.Now;
        static bool _capturing = false; // 防重入：遮罩开着时忽略新的热键/触发，避免叠出多层遮罩
        static int _zcodeGoneTicks = 0; // ZCode 进程消失的连续检测次数

        // 框选状态
        static Rectangle _result = Rectangle.Empty;
        static Rectangle _lastDrawn = Rectangle.Empty;
        static Point _start, _end;
        static bool _dragging;

        static IntPtr MsgProc(IntPtr h, uint m, IntPtr w, IntPtr l)
        {
            if (m == WM_HOTKEY) { _lastActive = DateTime.Now; DoCapture(w.ToInt32() == HotkeyId); return IntPtr.Zero; }
            if (m == WM_TIMER)
            {
                try
                {
                    if (_reload.WaitOne(0))
                    {
                        UnregisterHotKey(h, HotkeyId);
                        UnregisterHotKey(h, HotkeyIdNoHide);
                        LoadConfig();
                        bool ok1 = RegisterHotKey(h, HotkeyId, _mods, _vk);
                        bool ok2 = RegisterHotKey(h, HotkeyIdNoHide, _mods2, _vk2);
                        LogHotkeyFailure(ok1, ok2);
                    }
                    if (_trigger.WaitOne(0)) { _lastActive = DateTime.Now; DoCapture(true); } // 脚本触发等同默认热键：隐藏窗口
                    else
                    {
                        // 每 5 秒检查一次：ZCode 进程连续 60 秒不存在则退出（空闲计时照常）
                        if (++_zcodeGoneTicks >= 25)
                        {
                            _zcodeGoneTicks = 0;
                            if (!ZCodeRunning()) { _zcodeGoneSeconds++; if (_zcodeGoneSeconds >= 12) { Application_Exit(); } }
                            else _zcodeGoneSeconds = 0;
                        }
                        if ((DateTime.Now - _lastActive).TotalMinutes > IdleMinutes()) Application_Exit();
                    }
                }
                catch { }
                return IntPtr.Zero;
            }
            if (m == WM_DESTROY) return IntPtr.Zero;
            return DefWindowProcW(h, m, w, l);
        }
        static int _zcodeGoneSeconds = 0;

        // 热键注册失败（多半被其他软件占用）不会静默：写日志，capture.ps1 状态 会显示
        static void LogHotkeyFailure(bool ok1, bool ok2)
        {
            if (ok1 && ok2) { try { File.Delete(HotkeyErrorPath); } catch { } return; }
            try
            {
                string msg = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " 热键注册失败: "
                    + (ok1 ? "" : "hotkey ") + (ok2 ? "" : "hotkeyNoHide ") + "(可能被其他程序占用)";
                File.WriteAllText(HotkeyErrorPath, msg);
            }
            catch { }
        }

        static bool ZCodeRunning()        {
            try { return Process.GetProcessesByName("zcode").Length > 0 || Process.GetProcessesByName("ZCode").Length > 0; }
            catch { return true; } // 检测失败时保守处理：不退出
        }

        static void Application_Exit()
        {
            UnregisterHotKey(_msgHwnd, HotkeyId);
            UnregisterHotKey(_msgHwnd, HotkeyIdNoHide);
            DestroyWindow(_msgHwnd);
            Environment.Exit(0);
        }

        // 全屏框选遮罩：拖出矩形后保存，ESC 取消
        static IntPtr OverlayProc(IntPtr h, uint m, IntPtr w, IntPtr l)
        {
            switch (m)
            {
                case WM_KEYDOWN:
                    if (w.ToInt32() == 0x1B) { _result = Rectangle.Empty; DestroyWindow(h); }
                    return IntPtr.Zero;
                case WM_LBUTTONDOWN:
                    _dragging = true;
                    _start = _end = ToPoint(l);
                    return IntPtr.Zero;
                case WM_MOUSEMOVE:
                    if (_dragging)
                    {
                        _end = ToPoint(l);
                        DrawRubber(h); // XOR 擦旧画新
                    }
                    return IntPtr.Zero;
                case WM_LBUTTONUP:
                    if (!_dragging) return IntPtr.Zero;
                    _dragging = false;
                    _end = ToPoint(l);
                    DrawRubber(h); // 擦掉最后一帧
                    _result = Normalize(_start, _end);
                    if (_result.Width < 2 || _result.Height < 2) _result = Rectangle.Empty;
                    ShowWindow(h, SW_HIDE); // 先隐藏遮罩再截屏，避免把遮罩截进去
                    return IntPtr.Zero;
                default:
                    return DefWindowProcW(h, m, w, l);
            }
        }
        static IntPtr DefWindowProc = IntPtr.Zero; // 由 Interop 回填

        [DllImport("user32.dll")] static extern IntPtr DefWindowProcW(IntPtr h, uint m, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] static extern bool GetMessageW(out MSG msg, IntPtr hwnd, uint min, uint max);
        [DllImport("user32.dll")] static extern bool TranslateMessage(ref MSG msg);
        [DllImport("user32.dll")] static extern IntPtr DispatchMessageW(ref MSG msg);
        [DllImport("user32.dll")] static extern IntPtr SetTimer(IntPtr h, UIntPtr id, uint ms, IntPtr proc);

        static Point ToPoint(IntPtr l)
        {
            int x = (short)((long)l & 0xFFFF);
            int y = (short)(((long)l >> 16) & 0xFFFF);
            return new Point(x, y);
        }

        static Rectangle Normalize(Point a, Point b)
        {
            return new Rectangle(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
        }

        // XOR 橡皮筋：NOTXOR 模式下同矩形画两次等于擦除
        static void DrawRubber(IntPtr h)
        {
            var r = Normalize(_start, _end);
            IntPtr dc = GetDC(h);
            if (dc != IntPtr.Zero)
            {
                SetROP2(dc, R2_NOTXORPEN);
                IntPtr pen = CreatePen(0, 2, 0x0000FF00); // lime (0x00BBGGRR)
                IntPtr old = SelectObject(dc, pen);
                if (!_lastDrawn.IsEmpty) GdiRectangle(dc, _lastDrawn.Left, _lastDrawn.Top, _lastDrawn.Right, _lastDrawn.Bottom);
                if (!r.IsEmpty && _dragging) GdiRectangle(dc, r.Left, r.Top, r.Right, r.Bottom);
                SelectObject(dc, old);
                DeleteObject(pen);
                ReleaseDC(h, dc);
            }
            _lastDrawn = _dragging ? r : Rectangle.Empty;
        }

        [STAThread]
        static void Main()
        {
            SetProcessDPIAware();
            Directory.CreateDirectory(ShotsDir);
            bool createdNew;
            using (var mutex = new Mutex(true, "Local\\ZCodeShotMutex", out createdNew))
            {
                if (!createdNew) return; // 已有实例在运行
                _trigger = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\ZCodeShotTrigger");
                _reload = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\ZCodeShotReload");
                LoadConfig();

                _overlayBrush = CreateSolidBrush(0x00000000); // 黑
                var overlayClass = new WNDCLASS
                {
                    lpfnWndProc = OverlayWndProc,
                    hInstance = GetModuleHandleW(null),
                    lpszClassName = "ZCodeShotOverlay",
                    hbrBackground = _overlayBrush,
                    hCursor = LoadCursor(IntPtr.Zero, 32515) // IDC_CROSS
                };
                RegisterClassW(ref overlayClass);

                var msgClass = new WNDCLASS
                {
                    lpfnWndProc = MsgWndProc,
                    hInstance = GetModuleHandleW(null),
                    lpszClassName = "ZCodeShotMsg"
                };
                RegisterClassW(ref msgClass);
                // HWND_MESSAGE：纯消息窗口，不可见、不占任务栏
                _msgHwnd = CreateWindowExW(0, "ZCodeShotMsg", "ZCodeShot", 0, 0, 0, 0, 0,
                    (IntPtr)(-3), IntPtr.Zero, GetModuleHandleW(null), IntPtr.Zero);
                if (_msgHwnd == IntPtr.Zero) return;

                bool ok1 = RegisterHotKey(_msgHwnd, HotkeyId, _mods, _vk);
                bool ok2 = RegisterHotKey(_msgHwnd, HotkeyIdNoHide, _mods2, _vk2);
                LogHotkeyFailure(ok1, ok2);
                SetTimer(_msgHwnd, (UIntPtr)1, 200, IntPtr.Zero);

                MSG msg;
                while (GetMessageW(out msg, IntPtr.Zero, 0, 0))
                {
                    TranslateMessage(ref msg);
                    DispatchMessageW(ref msg);
                }
            }
        }

        static void DoCapture(bool hideWindow)
        {
            if (_capturing) return; // 已有遮罩在框选，忽略重复触发
            _capturing = true;
            try
            {
                _lastActive = DateTime.Now;
                var prevWindow = GetForegroundWindow();
                if (hideWindow && prevWindow != IntPtr.Zero)
                {
                    ShowWindow(prevWindow, SW_HIDE);
                    WaitInvisible(prevWindow, 500); // 等窗口真正不可见，而非固定 sleep
                }
                Bitmap savedBitmap = RunOverlay();
                if (hideWindow && prevWindow != IntPtr.Zero)
                {
                    ShowWindow(prevWindow, SW_SHOW);
                    SetForegroundWindow(prevWindow);
                    WaitForeground(prevWindow, 500); // 等前台切换完成再粘贴
                }
                if (savedBitmap != null)
                {
                    PasteToPreviousWindow(prevWindow, savedBitmap);
                    savedBitmap.Dispose();
                }
            }
            finally
            {
                _capturing = false;
            }
        }

        // 显示遮罩并跑独立消息循环，返回截图（null = 取消/无效选区）
        static Bitmap RunOverlay()
        {
            _result = Rectangle.Empty;
            _lastDrawn = Rectangle.Empty;
            _dragging = false;
            var vs = new Rectangle(GetSystemMetrics(76), GetSystemMetrics(77), GetSystemMetrics(78), GetSystemMetrics(79));
            _overlay = CreateWindowExW(0x80088 /*TOPMOST|TOOLWINDOW|LAYERED*/, "ZCodeShotOverlay", "", 0x80000000 /*WS_POPUP*/,
                vs.X, vs.Y, vs.Width, vs.Height, IntPtr.Zero, IntPtr.Zero, GetModuleHandleW(null), IntPtr.Zero);
            if (_overlay == IntPtr.Zero) return null;
            SetLayeredWindowAttributes(_overlay, 0, 90, 0x2 /*LWA_ALPHA*/);
            ShowWindow(_overlay, SW_SHOW);
            MSG msg;
            while (IsWindow(_overlay) && GetMessageW(out msg, IntPtr.Zero, 0, 0))
            {
                TranslateMessage(ref msg);
                DispatchMessageW(ref msg);
            }
            _overlay = IntPtr.Zero;
            if (_result.IsEmpty) return null;
            Thread.Sleep(120); // 等桌面合成器完成遮罩消失后的重绘
            try { return SaveRegion(_result); }
            catch { return null; }
        }

        [DllImport("user32.dll")] static extern bool IsWindow(IntPtr h);

        static void WaitInvisible(IntPtr h, int timeoutMs)
        {
            for (int waited = 0; IsWindowVisible(h) && waited < timeoutMs; waited += 20) Thread.Sleep(20);
        }

        static void WaitForeground(IntPtr h, int timeoutMs)
        {
            for (int waited = 0; GetForegroundWindow() != h && waited < timeoutMs; waited += 20)
            {
                Thread.Sleep(20);
                SetForegroundWindow(h);
            }
        }

        // 保存 PNG 落盘并记录 latest.txt，返回位图供粘贴（由调用方负责 Dispose）
        public static Bitmap SaveRegion(Rectangle r)
        {
            var bmp = new Bitmap(r.Width, r.Height);
            using (var g = Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(r.X, r.Y, 0, 0, r.Size);
            }
            var file = Path.Combine(ShotsDir, "shot_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png");
            bmp.Save(file, ImageFormat.Png);
            File.WriteAllText(LatestPath, file);
            return bmp;
        }

        // 截图完成后把图片粘贴回原前台窗口（通常是 ZCode 输入框），用户补充文字后自行发送
        static void PasteToPreviousWindow(IntPtr prevHwnd, Bitmap bmp)
        {
            try
            {
                if (prevHwnd == IntPtr.Zero || bmp == null || !AutoInsert()) return;
                SetForegroundWindow(prevHwnd);
                WaitForeground(prevHwnd, 500);
                // ZCode 客户端：键盘焦点未必在输入框上，先点击输入框区域点亮光标再粘贴。
                // 这是基于窗口底边布局的启发式，若粘贴位置不对，可关闭 autoInsert。
                if (IsZCodeWindow(prevHwnd))
                {
                    RECT r;
                    if (GetWindowRect(prevHwnd, out r))
                    {
                        LeftClick((r.Left + r.Right) / 2, r.Bottom - 70);
                        Thread.Sleep(150);
                    }
                }
                SetClipboardBitmap(bmp);
                keybd_event(0x11, 0, 0, UIntPtr.Zero);      // Ctrl down
                keybd_event(0x56, 0, 0, UIntPtr.Zero);      // V down
                keybd_event(0x56, 0, 2, UIntPtr.Zero);      // V up
                keybd_event(0x11, 0, 2, UIntPtr.Zero);      // Ctrl up
            }
            catch { }
        }

        // 原生剪贴板：SetClipboardData(CF_BITMAP)，系统会按需为其他格式合成
        static void SetClipboardBitmap(Bitmap bmp)
        {
            IntPtr hbm = bmp.GetHbitmap();
            if (OpenClipboard(IntPtr.Zero))
            {
                try
                {
                    EmptyClipboard();
                    if (SetClipboardData(2 /*CF_BITMAP*/, hbm) == IntPtr.Zero) DeleteObject(hbm); // 成功后归剪贴板所有，不可再删
                }
                finally { CloseClipboard(); }
            }
            else DeleteObject(hbm);
        }

        static bool IsZCodeWindow(IntPtr hwnd)
        {
            try
            {
                uint pid;
                GetWindowThreadProcessId(hwnd, out pid);
                var proc = Process.GetProcessById((int)pid);
                return proc.ProcessName.ToLowerInvariant().Contains("zcode");
            }
            catch { return false; }
        }

        static void LeftClick(int x, int y)
        {
            SetCursorPos(x, y);
            Thread.Sleep(60);
            mouse_event(2, 0, 0, 0, UIntPtr.Zero); // LEFTDOWN
            mouse_event(4, 0, 0, 0, UIntPtr.Zero); // LEFTUP
        }

        static bool AutoInsert()
        {
            try
            {
                var json = File.ReadAllText(ConfigPath);
                var m = Regex.Match(json, "\"autoInsert\"\\s*:\\s*(true|false)");
                if (m.Success) return m.Groups[1].Value == "true";
            }
            catch { }
            return true;
        }

        static double IdleMinutes()
        {
            var m = MatchNumber("idleMinutes", 30);
            return m > 0 ? m : 30;
        }

        static double MatchNumber(string key, double def)
        {
            try
            {
                var json = File.ReadAllText(ConfigPath);
                var m = Regex.Match(json, "\"" + key + "\"\\s*:\\s*([0-9.]+)");
                if (m.Success) return double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            }
            catch { }
            return def;
        }

        static void LoadConfig()
        {
            try
            {
                var json = File.ReadAllText(ConfigPath);
                var m = Regex.Match(json, "\"hotkey\"\\s*:\\s*\"([^\"]+)\"");
                if (m.Success && ParseHotkey(m.Groups[1].Value, out _mods, out _vk)) { }
                else ParseHotkey("Ctrl+Alt+A", out _mods, out _vk);
                var m2 = Regex.Match(json, "\"hotkeyNoHide\"\\s*:\\s*\"([^\"]+)\"");
                if (m2.Success && ParseHotkey(m2.Groups[1].Value, out _mods2, out _vk2)) { }
                else ParseHotkey("Ctrl+Shift+Alt+A", out _mods2, out _vk2);
            }
            catch
            {
                ParseHotkey("Ctrl+Alt+A", out _mods, out _vk);
                ParseHotkey("Ctrl+Shift+Alt+A", out _mods2, out _vk2);
            }
        }

        static bool ParseHotkey(string text, out uint mods, out uint vk)
        {
            mods = 0;
            vk = 0;
            foreach (var raw in text.Split('+'))
            {
                var p = raw.Trim().ToLowerInvariant();
                if (p == "") continue;
                if (p == "ctrl" || p == "control") mods |= MOD_CONTROL;
                else if (p == "alt") mods |= MOD_ALT;
                else if (p == "shift") mods |= MOD_SHIFT;
                else if (p == "win") mods |= MOD_WIN;
                else if (p.Length == 1 && char.IsLetter(p[0])) vk = (uint)char.ToUpper(p[0]);
                else if (p.Length == 1 && char.IsDigit(p[0])) vk = (uint)p[0];
                else if (p[0] == 'f' && p.Length <= 3)
                {
                    int n;
                    if (int.TryParse(p.Substring(1), out n) && n >= 1 && n <= 24) vk = (uint)(0x70 + n - 1);
                }
            }
            return vk != 0;
        }
    }
}
