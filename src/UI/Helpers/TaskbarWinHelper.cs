using Microsoft.Win32;
using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using LiteMonitor.src.Core;

namespace LiteMonitor.src.UI.Helpers
{
    /// <summary>
    /// 任务栏窗口底层助手 (Windows Helper)
    /// 职责：Win32 API、句柄查找、挂载逻辑、样式与图层设置
    /// </summary>
    public class TaskbarWinHelper
    {
        private readonly Form _form;

        // ★★★ 性能优化缓存 ★★★
        private Rectangle _lastWindowRect = Rectangle.Empty;
        private Rectangle _cachedResult = Rectangle.Empty;
        private bool _isCacheValid = false;

        // Win10 布局助手 (A/B 方案支持)
        private TaskbarWin10LayoutHelper? _win10Helper;

        // [Optimization] 静态缓存系统版本检测结果，供全局复用，避免重复调用系统 API
        private static readonly bool _isWin11 = Environment.OSVersion.Version.Major == 10 && Environment.OSVersion.Version.Build >= 22000;

        public bool IsWin10Mode => !_isWin11 && _win10Helper != null && _win10Helper.IsReady;
        public TaskbarWin10LayoutHelper Win10Helper => _win10Helper;

        public TaskbarWinHelper(Form form)
        {
            _form = form;
        }

        // =================================================================
        // 样式与图层
        // =================================================================
        public void ApplyLayeredStyle(Color transparentKey, bool clickThrough)
        {
            _form.BackColor = transparentKey;
            
            if (_form.IsHandleCreated)
            {
                uint colorKey = (uint)(transparentKey.R | (transparentKey.G << 8) | (transparentKey.B << 16));
                SetLayeredWindowAttributes(_form.Handle, colorKey, 0, LWA_COLORKEY);
            }

            int exStyle = GetWindowLong(_form.Handle, GWL_EXSTYLE);
            if (clickThrough) exStyle |= WS_EX_TRANSPARENT; 
            else exStyle &= ~WS_EX_TRANSPARENT; 
            SetWindowLong(_form.Handle, GWL_EXSTYLE, exStyle);
            
            _form.Invalidate();
        }

        public bool IsSystemLightTheme()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                if (key != null)
                {
                    object? val = key.GetValue("SystemUsesLightTheme");
                    if (val is int i) return i == 1;
                }
            }
            catch { }
            return false;
        }

        // =================================================================
        // 挂载逻辑
        // =================================================================
        public void AttachToTaskbar(IntPtr taskbarHandle)
        {
            IntPtr targetParent = taskbarHandle;

            // Win10 兼容模式：初始化布局助手
            if (!_isWin11)
            {
                _win10Helper ??= new TaskbarWin10LayoutHelper();
                _win10Helper.Initialize(taskbarHandle);

                // Win10下，为了与系统图标平级且正确遮挡，我们将父窗口设为 ReBarWindow32
                if (_win10Helper.IsReady && _win10Helper.ReBarHandle != IntPtr.Zero)
                {
                    targetParent = _win10Helper.ReBarHandle;
                }
            }

            SetParent(_form.Handle, targetParent);

            int style = GetWindowLong(_form.Handle, GWL_STYLE);
            style &= (int)~0x80000000; 
            style |= WS_CHILD | WS_VISIBLE | WS_CLIPSIBLINGS;
            SetWindowLong(_form.Handle, GWL_STYLE, style);
        }

        public void SetPosition(IntPtr taskbarHandle, int left, int top, int w, int h, bool alignLeft = true)
        {
            IntPtr currentParent = GetParent(_form.Handle);
            // Win10下，父窗口可能是 ReBarWindow32
            IntPtr expectedParent = taskbarHandle;
            if (!_isWin11 && _win10Helper != null && _win10Helper.IsReady)
                expectedParent = _win10Helper.ReBarHandle;

            bool isAttached = (currentParent == expectedParent);

            if (!isAttached)
            {
                AttachToTaskbar(taskbarHandle);
                currentParent = GetParent(_form.Handle);
                isAttached = currentParent == expectedParent;
            }

            // =================================================================
            // Win10 原生任务栏挤占逻辑 (A/B 方案之 B)
            // =================================================================
            if (!_isWin11 && _win10Helper != null && _win10Helper.IsReady)
            {
                if (_win10Helper.TryAdjustLayout(taskbarHandle, w, h, alignLeft, out int win10X, out int win10Y))
                {
                    SetWindowPos(_form.Handle, IntPtr.Zero, win10X, win10Y, w, h, SWP_NOZORDER | SWP_NOACTIVATE);
                    return;
                }
            }

            int finalX = left;
            int finalY = top;
            
            if (isAttached)
            {
                POINT pt = new POINT { X = left, Y = top };
                ScreenToClient(taskbarHandle, ref pt);
                finalX = pt.X;
                finalY = pt.Y;
                SetWindowPos(_form.Handle, IntPtr.Zero, finalX, finalY, w, h, SWP_NOZORDER | SWP_NOACTIVATE);
            }
            else
            {
                IntPtr HWND_TOPMOST = (IntPtr)(-1);
                SetWindowPos(_form.Handle, HWND_TOPMOST, finalX, finalY, w, h, SWP_NOACTIVATE);
            }
        }

        public void RestoreTaskbar()
        {
            _win10Helper?.Restore();
        }

        // =================================================================
        // 句柄与信息获取
        // =================================================================
        public (IntPtr hTaskbar, IntPtr hTray) FindHandles(string targetDevice)
        {
            Screen target = Screen.PrimaryScreen;
            if (!string.IsNullOrEmpty(targetDevice))
            {
                target = Screen.AllScreens.FirstOrDefault(s => s.DeviceName == targetDevice) ?? Screen.PrimaryScreen;
            }

            if (target.Primary)
            {
                IntPtr hTaskbar = FindWindow("Shell_TrayWnd", null);
                IntPtr hTray = FindWindowEx(hTaskbar, IntPtr.Zero, "TrayNotifyWnd", null);
                return (hTaskbar, hTray);
            }
            else
            {
                IntPtr hTaskbar = FindSecondaryTaskbar(target);
                return (hTaskbar, IntPtr.Zero);
            }
        }

        private IntPtr FindSecondaryTaskbar(Screen screen)
        {
            IntPtr hWnd = IntPtr.Zero;
            while ((hWnd = FindWindowEx(IntPtr.Zero, hWnd, "Shell_SecondaryTrayWnd", null)) != IntPtr.Zero)
            {
                GetWindowRect(hWnd, out RECT rect);
                Rectangle r = Rectangle.FromLTRB(rect.left, rect.top, rect.right, rect.bottom);
                if (screen.Bounds.Contains(r.Location) || screen.Bounds.IntersectsWith(r))
                    return hWnd;
            }
            return FindWindow("Shell_TrayWnd", null);
        }

        public Rectangle GetTaskbarRect(IntPtr hTaskbar, string targetDevice)
        {
            if (hTaskbar == IntPtr.Zero) return Rectangle.Empty;

            // 1. 获取物理矩形
            if (!GetWindowRect(hTaskbar, out RECT r)) return Rectangle.Empty;
            var rectPhys = Rectangle.FromLTRB(r.left, r.top, r.right, r.bottom);

            // 缓存检查
            if (_isCacheValid && rectPhys == _lastWindowRect) return _cachedResult;

            Rectangle finalRect = rectPhys;

            // =========================================================================
            // [SIMPLIFIED FIX] 极简稳定方案：基于 WorkingArea 和 强制高度修正 (DPI适配版)
            // =========================================================================
            try
            {
                Screen screen = null;
                if (!string.IsNullOrEmpty(targetDevice)) 
                    screen = Screen.AllScreens.FirstOrDefault(s => s.DeviceName == targetDevice);
                if (screen == null) 
                    screen = Screen.FromHandle(hTaskbar);

                if (screen != null)
                {
                    Rectangle workArea = screen.WorkingArea;
                    Rectangle screenBounds = screen.Bounds;
                    int reservedBottom = screenBounds.Bottom - workArea.Bottom;

                    // 场景 A: 锚定模式 (Win10/11 正常桌面固定)
                    // 如果 WorkingArea 被挤压 (>2px)，直接信赖 WorkingArea。
                    if (reservedBottom > 2) 
                    {
                        finalRect = new Rectangle(rectPhys.Left, workArea.Bottom, rectPhys.Width, reservedBottom);
                    }
                    // 场景 B: 悬浮模式 (自动隐藏 / 平板模式)
                    else
                    {
                        // 针对 Win11 的强制修正 (复用静态 _isWin11 字段)
                        if (_isWin11)
                        {
                            // 计算当前 DPI 下的标准任务栏高度 (Win11 标准为 48px @ 96DPI)
                            int dpi = GetTaskbarDpi();
                            int standardHeight = (int)Math.Round(48.0 * dpi / 96.0);

                            // 判定是否处于"弹出/展开"状态
                            // 如果物理高度 > 标准高度的 80%，说明它不是收起状态(收起状态通常很窄)
                            // 此时由于 API 可能返回幽灵高度(60px+)，我们需要强制将其修正为标准高度
                            if (rectPhys.Height > (standardHeight * 0.8))
                            {
                                finalRect = new Rectangle(
                                    rectPhys.Left, 
                                    rectPhys.Bottom - standardHeight, // 紧贴物理底边
                                    rectPhys.Width, 
                                    standardHeight);
                            }
                        }
                    }
                }
            }
            catch { }

            _lastWindowRect = rectPhys;
            _cachedResult = finalRect;
            _isCacheValid = true;

            return _cachedResult;
        }

        public bool GetWindowRectWrapper(IntPtr hWnd, out Rectangle rect)
        {
            if (GetWindowRect(hWnd, out RECT r))
            {
                rect = Rectangle.FromLTRB(r.left, r.top, r.right, r.bottom);
                return true;
            }
            rect = Rectangle.Empty;
            return false;
        }

        public static bool IsCenterAligned()
        {
            if (Environment.OSVersion.Version.Major < 10 || Environment.OSVersion.Version.Build < 22000) 
                return false;
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced");
                return ((int)(key?.GetValue("TaskbarAl", 1) ?? 1)) == 1;
            }
            catch { return false; }
        }

        public static int GetTaskbarDpi()
        {
            IntPtr taskbar = FindWindow("Shell_TrayWnd", null);
            if (taskbar != IntPtr.Zero)
            {
                try { return (int)GetDpiForWindow(taskbar); } catch { }
            }
            return 96;
        }

        public static int GetWidgetsWidth()
        {
            int dpi = GetTaskbarDpi();
            // [Optimization] 复用静态缓存的 Win11 判断
            if (_isWin11)
            {
                string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string pkg = Path.Combine(local, "Packages");
                bool hasWidgetPkg = false;
                try { hasWidgetPkg = Directory.GetDirectories(pkg, "MicrosoftWindows.Client.WebExperience*").Any(); } catch {}
                
                if (!hasWidgetPkg) return 0;

                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced");
                if (key == null) return 0;

                object? val = key.GetValue("TaskbarDa");
                if (val is int i && i != 0) return 150 * dpi / 96;
            }
            return 0;
        }

        // -------------------------------------------------------------
        // Win32 API
        // -------------------------------------------------------------
        [DllImport("user32.dll")] public static extern IntPtr FindWindow(string cls, string? name);
        [DllImport("user32.dll")] public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string? name);
        [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr hWnd, int idx);
        [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr hWnd, int idx, int value);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
        [DllImport("user32.dll")] public static extern bool ScreenToClient(IntPtr hWnd, ref POINT pt);
        [DllImport("user32.dll")] public static extern IntPtr SetParent(IntPtr child, IntPtr parent);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] public static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);
        [DllImport("user32.dll", ExactSpelling = true, CharSet = CharSet.Auto)] public static extern IntPtr GetParent(IntPtr hWnd);
        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)] public static extern uint RegisterWindowMessage(string lpString);
        [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hWnd);
        [DllImport("shell32.dll")] private static extern uint SHAppBarMessage(uint msg, ref APPBARDATA pData);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool IsWindow(IntPtr hWnd);
        [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

        public const int GWL_STYLE = -16;
        public const int GWL_EXSTYLE = -20;
        public const int WS_CHILD = 0x40000000;
        public const int WS_VISIBLE = 0x10000000;
        public const int WS_CLIPSIBLINGS = 0x04000000;
        public const int WS_EX_LAYERED = 0x80000;
        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const uint LWA_COLORKEY = 0x00000001;
        public const uint SWP_NOZORDER = 0x0004;
        public const uint SWP_NOACTIVATE = 0x0010;
        public const int WS_EX_TRANSPARENT = 0x00000020;
        public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
        private const uint ABM_GETTASKBARPOS = 5;

        public struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int left, top, right, bottom; }
        [StructLayout(LayoutKind.Sequential)]
        private struct APPBARDATA
        {
            public int cbSize;
            public IntPtr hWnd;
            public uint uCallbackMessage;
            public uint uEdge;
            public RECT rc;
            public int lParam;
        }

        public static void ActivateWindow(IntPtr handle) => SetForegroundWindow(handle);
    }
}