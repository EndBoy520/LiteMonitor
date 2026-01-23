using LiteMonitor.src.Core;
using LiteMonitor.src.UI.Helpers;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using static LiteMonitor.src.UI.Helpers.TaskbarWinHelper;

namespace LiteMonitor
{
    public class TaskbarForm : Form
    {
        private readonly Settings _cfg;
        private readonly UIController _ui;
        private readonly MainForm _mainForm;
        private readonly System.Windows.Forms.Timer _timer = new();

        // ★★★ 双助手架构 ★★★
        private readonly TaskbarWinHelper _winHelper;
        private readonly TaskbarBizHelper _bizHelper;
        
        private HorizontalLayout _layout;
        private List<Column>? _cols;
        private ContextMenuStrip? _currentMenu;
        private DateTime _lastFindHandleTime = DateTime.MinValue;

        // 公开属性
        public string TargetDevice { get; private set; } = "";
        
        private const int WM_RBUTTONUP = 0x0205;
        private bool _isWin11;

        public TaskbarForm(Settings cfg, UIController ui, MainForm mainForm)
        {
            _cfg = cfg;
            _ui = ui;
            _mainForm = mainForm;
            TargetDevice = _cfg.TaskbarMonitorDevice;

            _isWin11 = Environment.OSVersion.Version >= new Version(10, 0, 22000);

            // 初始化组件
            _winHelper = new TaskbarWinHelper(this);
            _bizHelper = new TaskbarBizHelper(this, _cfg, _winHelper);

            // 窗体属性
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            ControlBox = false;
            TopMost = false;
            DoubleBuffered = true;

            ReloadLayout();

            _bizHelper.CheckTheme(true);
            _bizHelper.FindHandles();
            
            _bizHelper.AttachToTaskbar();
            _winHelper.ApplyLayeredStyle(_bizHelper.TransparentKey, _cfg.TaskbarClickThrough);

            _timer.Interval = Math.Max(_cfg.RefreshMs, 60);
            _timer.Tick += (_, __) => Tick();
            _timer.Start();

            Tick();
        }

        public void ReloadLayout()
        {
            _layout = new HorizontalLayout(ThemeManager.Current, 300, LayoutMode.Taskbar, _cfg);
            _winHelper.ApplyLayeredStyle(_bizHelper.TransparentKey, _cfg.TaskbarClickThrough);
            _bizHelper.CheckTheme(true);

            if (_cols != null && _cols.Count > 0)
            {
                _layout.Build(_cols, _bizHelper.Height);
                Width = _layout.PanelWidth;
                _bizHelper.UpdatePlacement(Width);
            }
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _timer.Stop();
                _timer.Dispose();
                _currentMenu?.Dispose();
            }
            base.Dispose(disposing);
        }

        protected override void WndProc(ref Message m)
        {
            if (!_isWin11 && m.Msg == WM_RBUTTONUP)
            {
                this.BeginInvoke(new Action(ShowContextMenu));
                return; 
            }
            base.WndProc(ref m);
        }

        private void ShowContextMenu()
        {
            if (_currentMenu != null)
            {
                _currentMenu.Dispose();
                _currentMenu = null;
            }

            _currentMenu = MenuManager.Build(_mainForm, _cfg, _ui, "Taskbar");
            
            TaskbarWinHelper.ActivateWindow(this.Handle);
            _currentMenu.Show(Cursor.Position);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Right)
            {
                ShowContextMenu();
            }
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (e.Button == MouseButtons.Left)
            {
                _bizHelper.HandleDoubleClick(_mainForm, _ui);
            }
        }

        private void Tick()
        {
            // [Fix] 周期性检查句柄，防止 Explorer 重启后句柄失效
            // 优化：仅在重试期或句柄无效时调用 FindHandles，且限制调用频率
            bool isHandleInvalid = !_bizHelper.IsTaskbarValid();
            
            // 如果处于重试期，或者句柄无效且距离上次查找超过2秒(防止无Explorer时高频空转)
            if (isHandleInvalid && (DateTime.Now - _lastFindHandleTime).TotalSeconds > 2)
            {
                _bizHelper.FindHandles();
                _lastFindHandleTime = DateTime.Now;
            }

            if (Environment.TickCount % 5000 < _cfg.RefreshMs) _bizHelper.CheckTheme();

            _cols = _ui.GetTaskbarColumns();
            if (_cols == null || _cols.Count == 0) return;
            
            _bizHelper.UpdateTaskbarRect(); 
            
            if (_bizHelper.IsVertical())
            {
                _bizHelper.BuildVerticalLayout(_cols);
            }
            else
            {
                _layout.Build(_cols, _bizHelper.Height);
                Width = _layout.PanelWidth;
                Height = _bizHelper.Height;
            }
            
            _bizHelper.UpdatePlacement(Width);
            Invalidate();
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(_bizHelper.TransparentKey);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (_cols == null) return;
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            TaskbarRenderer.Render(g, _cols, _bizHelper.LastIsLightTheme);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_LAYERED | WS_EX_TOOLWINDOW;
                if (_cfg != null && _cfg.TaskbarClickThrough)
                {
                    cp.ExStyle |= WS_EX_TRANSPARENT;
                }
                return cp;
            }
        }
    }
}
