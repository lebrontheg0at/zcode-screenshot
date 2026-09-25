// ZCode 截图监听器：全局热键或命令触发 -> 全屏遮罩框选区域 -> 保存 PNG 到 ~/.zcode/screenshot/shots
// 懒启动（由 capture.ps1 编译并拉起），空闲自动退出；互斥锁保证单实例
using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

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
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extra);

        struct RECT { public int Left, Top, Right, Bottom; }

        const int HotkeyId = 0xB00B;       // 截图并隐藏当前窗口
        const int HotkeyIdNoHide = 0xB00C; // 截图不隐藏窗口
        const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_WIN = 0x8;

        static readonly string BaseDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".zcode", "screenshot");
        static readonly string ConfigPath = Path.Combine(BaseDir, "config.json");
        static readonly string ShotsDir = Path.Combine(BaseDir, "shots");
        static readonly string LatestPath = Path.Combine(BaseDir, "latest.txt");

        static EventWaitHandle _trigger, _reload;
        static MsgForm _form;
        static uint _mods = MOD_CONTROL | MOD_ALT;
        static uint _vk = (uint)'A';
        static uint _mods2 = MOD_CONTROL | MOD_ALT | MOD_SHIFT;
        static uint _vk2 = (uint)'A';
        static DateTime _lastActive = DateTime.Now;
        static bool _capturing = false; // 防重入：遮罩开着时忽略新的热键/触发，避免叠出多层遮罩

        // 隐藏消息窗：接收 WM_HOTKEY，WParam 区分是哪个热键
        class MsgForm : Form
        {
            public Action<int> OnHotkey;
            protected override void WndProc(ref Message m)
            {
                if (m.Msg == 0x0312) { if (OnHotkey != null) OnHotkey(m.WParam.ToInt32()); return; } // WM_HOTKEY
                base.WndProc(ref m);
            }
        }

        // 全屏框选遮罩：拖出矩形后保存，ESC 取消
        class CaptureForm : Form
        {
            public Rectangle Result = Rectangle.Empty;
            public Bitmap SavedBitmap = null;
            Point _start, _end;
            bool _dragging;

            public CaptureForm()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
                FormBorderStyle = FormBorderStyle.None;
                StartPosition = FormStartPosition.Manual;
                var vs = SystemInformation.VirtualScreen;
                Bounds = new Rectangle(vs.X, vs.Y, vs.Width, vs.Height);
                ShowInTaskbar = false;
                TopMost = true;
                KeyPreview = true;
                Cursor = Cursors.Cross;
                BackColor = Color.Black;
                Opacity = 0.35;
                KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); };
                MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) { _dragging = true; _start = e.Location; _end = e.Location; } };
                MouseMove += (s, e) => { if (_dragging) { _end = e.Location; Invalidate(); } };
                MouseUp += (s, e) =>
                {
                    if (!_dragging) return;
                    _dragging = false;
                    Result = Normalize(_start, _end);
                    if (Result.Width < 2 || Result.Height < 2) { Result = Rectangle.Empty; Close(); return; }
                    Hide(); // 先隐藏遮罩再截屏，避免把遮罩截进去
                    Application.DoEvents();
                    Thread.Sleep(120);
                    try { SavedBitmap = Program.SaveRegion(Result); } catch { }
                    Close();
                };
                Paint += (s, e) =>
                {
                    if (!_dragging) return;
                    var r = Normalize(_start, _end);
                    using (var p = new Pen(Color.Lime, 2)) e.Graphics.DrawRectangle(p, r);
                };
            }

            protected override CreateParams CreateParams
            {
                get { var cp = base.CreateParams; cp.ExStyle |= 0x80; return cp; } // WS_EX_TOOLWINDOW：不进 Alt-Tab
            }

            static Rectangle Normalize(Point a, Point b)
            {
                return new Rectangle(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
            }
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
                Application.EnableVisualStyles();
                _form = new MsgForm();
                var handle = _form.Handle; // 强制创建句柄，注册热键用
                _form.OnHotkey = id => DoCapture(id == HotkeyId); // HotkeyId 隐藏窗口，HotkeyIdNoHide 不隐藏
                RegisterHotKey(_form.Handle, HotkeyId, _mods, _vk);
                RegisterHotKey(_form.Handle, HotkeyIdNoHide, _mods2, _vk2);
                var timer = new System.Windows.Forms.Timer { Interval = 200 };
                timer.Tick += (s, e) =>
                {
                    try
                    {
                        if (_reload.WaitOne(0))
                        {
                            UnregisterHotKey(_form.Handle, HotkeyId);
                            UnregisterHotKey(_form.Handle, HotkeyIdNoHide);
                            LoadConfig();
                            RegisterHotKey(_form.Handle, HotkeyId, _mods, _vk);
                            RegisterHotKey(_form.Handle, HotkeyIdNoHide, _mods2, _vk2);
                        }
                        if (_trigger.WaitOne(0)) { DoCapture(true); } // 脚本触发等同默认热键：隐藏窗口
                        else if ((DateTime.Now - _lastActive).TotalMinutes > IdleMinutes()) Application.Exit();
                    }
                    catch { }
                };
                timer.Start();
                Application.Run(_form);
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
                if (hideWindow && prevWindow != IntPtr.Zero) ShowWindow(prevWindow, 0); // SW_HIDE
                Bitmap savedBitmap = null;
                using (var overlay = new CaptureForm())
                {
                    overlay.ShowDialog();
                    savedBitmap = overlay.SavedBitmap;
                }
                if (hideWindow && prevWindow != IntPtr.Zero)
                {
                    ShowWindow(prevWindow, 5); // SW_SHOW：恢复刚隐藏的窗口
                    SetForegroundWindow(prevWindow);
                    Application.DoEvents();
                    Thread.Sleep(150);
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
                Application.DoEvents();
                Thread.Sleep(200);
                // ZCode 客户端：键盘焦点未必在输入框上，先点击输入框区域点亮光标再粘贴
                if (IsZCodeWindow(prevHwnd))
                {
                    RECT r;
                    if (GetWindowRect(prevHwnd, out r))
                    {
                        LeftClick((r.Left + r.Right) / 2, r.Bottom - 70);
                        Thread.Sleep(150);
                    }
                }
                Clipboard.SetImage(bmp);
                SendKeys.SendWait("^v");
            }
            catch { }
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
