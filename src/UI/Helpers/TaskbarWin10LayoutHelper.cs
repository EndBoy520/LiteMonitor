using System;
using System.Drawing;
using System.Runtime.InteropServices;
using static LiteMonitor.src.UI.Helpers.TaskbarWinHelper;

namespace LiteMonitor.src.UI.Helpers
{
    /// <summary>
    /// Win10 任务栏布局调整助手
    /// 职责：实现 Win10 任务栏的"挤占"逻辑，防止自定义窗口覆盖系统图标
    /// 原理：通过调整 MSTaskSwWClass (任务列表) 的位置，腾出左侧空间
    /// </summary>
    public class TaskbarWin10LayoutHelper
    {
        private IntPtr _hReBar = IntPtr.Zero;
        private IntPtr _hMin = IntPtr.Zero;
        private int _lastSqueezedWidth = -1;
        private int _lastUsedWidth = 0;
        private int _lastSqueezedHeight = -1;
        private int _lastUsedHeight = 0;
        private bool _lastAlignLeft = true;
        private bool _lastVertical = false;

        public IntPtr ReBarHandle => _hReBar;

        public bool IsReady => _hReBar != IntPtr.Zero && _hMin != IntPtr.Zero;

        public void Initialize(IntPtr hTaskbar)
        {
            // Win10 结构：Shell_TrayWnd -> ReBarWindow32 -> MSTaskSwWClass
            _hReBar = FindWindowEx(hTaskbar, IntPtr.Zero, "ReBarWindow32", null);
            if (_hReBar == IntPtr.Zero)
                _hReBar = FindWindowEx(hTaskbar, IntPtr.Zero, "WorkerW", null); // 部分旧版或特殊主题

            if (_hReBar != IntPtr.Zero)
            {
                _hMin = FindWindowEx(_hReBar, IntPtr.Zero, "MSTaskSwWClass", null);
                if (_hMin == IntPtr.Zero)
                    _hMin = FindWindowEx(_hReBar, IntPtr.Zero, "MSTaskListWClass", null); // 兼容旧版类名
            }
        }

        /// <summary>
        /// 尝试调整任务栏布局，为 LiteMonitor 腾出空间
        /// </summary>
        /// <param name="hTaskbar">主任务栏句柄 (Win10下未使用，仅保留接口一致性)</param>
        /// <param name="w">LiteMonitor 宽度</param>
        /// <param name="h">LiteMonitor 高度</param>
        /// <param name="alignLeft">是否居左/居顶 (true=左/顶, false=右/底)</param>
        /// <param name="x">输出：LiteMonitor 应该放置的 X 坐标 (相对于 ReBar 父窗口)</param>
        /// <param name="y">输出：LiteMonitor 应该放置的 Y 坐标 (相对于 ReBar 父窗口)</param>
        /// <returns>是否成功执行了调整计算</returns>
        public bool TryAdjustLayout(IntPtr hTaskbar, int w, int h, bool alignLeft, out int x, out int y)
        {
            x = 0; y = 0;
            if (!IsReady) return false;

            if (!GetWindowRect(_hMin, out RECT rMin) || !GetWindowRect(_hReBar, out RECT rBar))
                return false;

            Rectangle rcMin = Rectangle.FromLTRB(rMin.left, rMin.top, rMin.right, rMin.bottom);
            Rectangle rcBar = Rectangle.FromLTRB(rBar.left, rBar.top, rBar.right, rBar.bottom);

            bool isVertical = rcBar.Height > rcBar.Width;

            // 如果方向改变，重置状态
            if (isVertical != _lastVertical)
            {
                _lastSqueezedWidth = -1;
                _lastSqueezedHeight = -1;
                _lastVertical = isVertical;
            }

            if (isVertical)
            {
                // ================== 垂直模式 ==================
                int currentRelTop = rcMin.Top - rcBar.Top;
                int originalRelTop = currentRelTop;
                int originalHeight = rcMin.Height;
                bool isSqueezed = (rcMin.Height == _lastSqueezedHeight);

                if (isSqueezed)
                {
                    originalHeight = rcMin.Height + _lastUsedHeight;
                    if (_lastAlignLeft) // 之前是顶部挤占
                        originalRelTop = currentRelTop - _lastUsedHeight;
                }

                bool needAdjust = !isSqueezed || (h != _lastUsedHeight) || (alignLeft != _lastAlignLeft);

                int targetHeight = originalHeight;
                int targetRelTop = originalRelTop;
                
                // 计算 LiteMonitor 位置 & 目标任务栏高度
                if (alignLeft)
                {
                    // 居顶模式：Monitor 在顶部，任务列表下移
                    targetRelTop += h;
                    targetHeight = originalHeight - h;
                    y = originalRelTop;
                }
                else
                {
                    // 居底模式：Monitor 在底部 (ReBar 底部)，任务列表在上方
                    // 注意：这里我们基于 ReBar 的高度来定位，而不是依赖任务列表的当前高度
                    y = rcBar.Height - h;
                    
                    // 任务列表的最大允许高度 = Monitor 的起始 Y - 任务列表的起始 Y
                    int maxTaskListHeight = y - originalRelTop;
                    
                    // 总是将任务列表限制在这个高度，防止重叠
                    // (即使任务列表原本很短，强制设大通常也无害，或者我们可以取 Min)
                    // 但为了能正确 Restore，我们假设任务列表原本是填满的（或应该填满）
                    targetHeight = maxTaskListHeight;
                }

                if (needAdjust)
                {
                    if (targetHeight > 0)
                    {
                        MoveWindow(_hMin, 0, targetRelTop, rcMin.Width, targetHeight, true);
                        _lastSqueezedHeight = targetHeight;
                        // 记录为了恢复而需要增加的高度 Delta
                        _lastUsedHeight = originalHeight - targetHeight; 
                        _lastAlignLeft = alignLeft;
                    }
                }
                else
                {
                    _lastSqueezedHeight = rcMin.Height;
                }

                // 计算 LiteMonitor X (水平居中)
                x = (rcBar.Width - w) / 2;
            }
            else
            {
                // ================== 水平模式 ==================
                int currentRelLeft = rcMin.Left - rcBar.Left;
                int originalRelLeft = currentRelLeft;
                int originalWidth = rcMin.Width;
                bool isSqueezed = (rcMin.Width == _lastSqueezedWidth);

                if (isSqueezed)
                {
                    originalWidth = rcMin.Width + _lastUsedWidth;
                    if (_lastAlignLeft) // 之前是左侧挤占
                        originalRelLeft = currentRelLeft - _lastUsedWidth;
                }

                bool needAdjust = !isSqueezed || (w != _lastUsedWidth) || (alignLeft != _lastAlignLeft);

                int targetWidth = originalWidth;
                int targetRelLeft = originalRelLeft;

                // 计算 LiteMonitor 位置 & 目标任务栏宽度
                if (alignLeft)
                {
                    // 居左模式：Monitor 在左侧，任务列表右移
                    targetRelLeft += w;
                    targetWidth = originalWidth - w;
                    x = originalRelLeft;
                }
                else
                {
                    // 居右模式：Monitor 在右侧 (ReBar 右侧)
                    x = rcBar.Width - w;
                    
                    // 任务列表最大宽度
                    int maxTaskListWidth = x - originalRelLeft;
                    targetWidth = maxTaskListWidth;
                }

                if (needAdjust)
                {
                    if (targetWidth > 0)
                    {
                        MoveWindow(_hMin, targetRelLeft, 0, targetWidth, rcMin.Height, true);
                        _lastSqueezedWidth = targetWidth;
                        // 记录 Delta
                        _lastUsedWidth = originalWidth - targetWidth;
                        _lastAlignLeft = alignLeft;
                    }
                }
                else
                {
                    _lastSqueezedWidth = rcMin.Width;
                }

                // 计算 LiteMonitor Y (垂直居中)
                y = (rcBar.Height - h) / 2;
            }

            return true;
        }

        public void Restore()
        {
            if (IsReady)
            {
                if (GetWindowRect(_hMin, out RECT rMin) && GetWindowRect(_hReBar, out RECT rBar))
                {
                    Rectangle rcMin = Rectangle.FromLTRB(rMin.left, rMin.top, rMin.right, rMin.bottom);
                    Rectangle rcBar = Rectangle.FromLTRB(rBar.left, rBar.top, rBar.right, rBar.bottom);

                    // 恢复水平挤占
                    if (_lastSqueezedWidth != -1)
                    {
                        int currentRelLeft = rcMin.Left - rcBar.Left;
                        int originalRelLeft = currentRelLeft;
                        if (_lastAlignLeft) originalRelLeft -= _lastUsedWidth;
                        int originalWidth = rcMin.Width + _lastUsedWidth;

                        if (originalWidth > 0 && originalRelLeft >= 0)
                            MoveWindow(_hMin, originalRelLeft, 0, originalWidth, rcMin.Height, true);
                        _lastSqueezedWidth = -1;
                    }

                    // 恢复垂直挤占
                    if (_lastSqueezedHeight != -1)
                    {
                        int currentRelTop = rcMin.Top - rcBar.Top;
                        int originalRelTop = currentRelTop;
                        if (_lastAlignLeft) originalRelTop -= _lastUsedHeight;
                        int originalHeight = rcMin.Height + _lastUsedHeight;

                        if (originalHeight > 0 && originalRelTop >= 0)
                            MoveWindow(_hMin, 0, originalRelTop, rcMin.Width, originalHeight, true);
                        _lastSqueezedHeight = -1;
                    }
                }
            }
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);
    }
}
