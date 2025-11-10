using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace LiteMonitor
{
    public class UIController : IDisposable
    {
        private readonly Settings _cfg;
        private readonly Form _form;
        private readonly HardwareMonitor _mon;
        private readonly System.Windows.Forms.Timer _timer;
        private bool _dragging = false;
        private UILayout? _layout;
        private bool _layoutDirty = true;

        private List<GroupLayoutInfo> _groups = new();

        public UIController(Settings cfg, Form form)
        {
            _cfg = cfg;
            _form = form;
            _mon = new HardwareMonitor(cfg);
            _mon.OnValuesUpdated += () => _form.Invalidate();

            _timer = new System.Windows.Forms.Timer { Interval = Math.Max(100, _cfg.RefreshMs) };
            _timer.Tick += (_, __) => Tick();
            _timer.Start();

            // 初始化语言与主题
            LanguageManager.Load(cfg.Language);
            ApplyTheme(cfg.Skin);
        }

        // ========== 主题切换 ==========
        public void ApplyTheme(string name)
        {
            ThemeManager.Load(name);
            var t = ThemeManager.Current;

            // 性能模式：关闭动画（必要时你也可以自行把毛玻璃关掉）
            if (t.Behavior.PerformanceMode)
            {
                t.Layout.AnimationSpeed = 1.0;     // 动画全关闭（即时刷新）
            }

            // ✅ 修复点：同步主题宽度或设置的面板宽度
            if (_cfg.PanelWidth > 100)
            {
                t.Layout.Width = _cfg.PanelWidth;
                _form.Width = _cfg.PanelWidth;
            }
            else
            {
                _form.Width = t.Layout.Width;
            }

            // ✅ 修复点：切主题时同步窗体背景色，避免边缘露底色
            _form.BackColor = ThemeManager.ParseColor(t.Color.Background);

            // ✅ 重新创建布局对象
            _layout = new UILayout(t);
            _layoutDirty = true;

            // ✅ 重建硬件项列表
            BuildMetrics();

            // ❌ 原逻辑：仅逐组刷新，无法覆盖边缘
            // foreach (var g in _groups)
            //     _form.Invalidate(g.Bounds, false);

            // ✅ 修复点：改为整窗重绘，避免上/左边缘出现白线
            _form.Invalidate();     // 全部客户区
            _form.Update();         // 立即刷新（确保即时重绘）

            // ✅ 可选触发圆角刷新（防止主题宽度变化时圆角不同步）
            // _form.ApplyRoundedCorners();  // 如你的 MainForm 暴露了此方法可打开此行
        }



        public void SetDragging(bool dragging) => _dragging = dragging;

        private bool _busy = false;

        private async void Tick()
        {
            if (_dragging || _busy) return;
            _busy = true;

            await System.Threading.Tasks.Task.Run(() => _mon.UpdateAll());

            foreach (var g in _groups)
                foreach (var it in g.Items)
                {
                    it.Value = _mon.Get(it.Key);
                    it.TickSmooth(ThemeManager.Current.Layout.AnimationSpeed);
                }

            _form.Invalidate();
            _busy = false;
        }

        // ========== 动态构建分组与项目 ==========
        private void BuildMetrics()
        {
            var t = ThemeManager.Current;
            _groups = new List<GroupLayoutInfo>();

            // === CPU ===
            var cpuItems = new List<MetricItem>();
            if (_cfg.Enabled.CpuLoad)
                cpuItems.Add(new MetricItem { Key = "CPU.Load", Label = LanguageManager.T("Items.CPU.Load") });
            if (_cfg.Enabled.CpuTemp)
                cpuItems.Add(new MetricItem { Key = "CPU.Temp", Label = LanguageManager.T("Items.CPU.Temp") });
            if (cpuItems.Count > 0)
                _groups.Add(new GroupLayoutInfo("CPU", cpuItems));

            // === GPU ===
            var gpuItems = new List<MetricItem>();
            if (_cfg.Enabled.GpuLoad)
                gpuItems.Add(new MetricItem { Key = "GPU.Load", Label = LanguageManager.T("Items.GPU.Load") });
            if (_cfg.Enabled.GpuTemp)
                gpuItems.Add(new MetricItem { Key = "GPU.Temp", Label = LanguageManager.T("Items.GPU.Temp") });
            if (_cfg.Enabled.GpuVram)
                gpuItems.Add(new MetricItem { Key = "GPU.VRAM", Label = LanguageManager.T("Items.GPU.VRAM") });
            if (gpuItems.Count > 0)
                _groups.Add(new GroupLayoutInfo("GPU", gpuItems));

            // === 内存 ===
            var memItems = new List<MetricItem>();
            if (_cfg.Enabled.MemLoad)
                memItems.Add(new MetricItem { Key = "MEM.Load", Label = LanguageManager.T("Items.MEM.Load") });
            if (memItems.Count > 0)
                _groups.Add(new GroupLayoutInfo("MEM", memItems));

            // === 磁盘 ===
            var diskItems = new List<MetricItem>();
            if (_cfg.Enabled.DiskRead)
                diskItems.Add(new MetricItem { Key = "DISK.Read", Label = LanguageManager.T("Items.DISK.Read") });
            if (_cfg.Enabled.DiskWrite)
                diskItems.Add(new MetricItem { Key = "DISK.Write", Label = LanguageManager.T("Items.DISK.Write") });
            if (diskItems.Count > 0)
                _groups.Add(new GroupLayoutInfo("DISK", diskItems));

            // === 网络 ===
            var netItems = new List<MetricItem>();
            if (_cfg.Enabled.NetUp)
                netItems.Add(new MetricItem { Key = "NET.Up", Label = LanguageManager.T("Items.NET.Up") });
            if (_cfg.Enabled.NetDown)
                netItems.Add(new MetricItem { Key = "NET.Down", Label = LanguageManager.T("Items.NET.Down") });
            if (netItems.Count > 0)
                _groups.Add(new GroupLayoutInfo("NET", netItems));

            // === 构建布局 ===
            _layout ??= new UILayout(t);
            if (_layoutDirty)
            {
                _layout.Build(_groups);
                _layoutDirty = false;

                int totalH = 0;
                foreach (var g in _groups) totalH = Math.Max(totalH, g.Bounds.Bottom);
                _form.Height = totalH + t.Layout.Padding;
            }
        }

        // ========== 绘制接口 ==========
        public void Render(Graphics g)
        {
            var t = ThemeManager.Current;
            _layout ??= new UILayout(t);

            if (_layoutDirty)
            {
                _layout.Build(_groups);
                _layoutDirty = false;

                int totalH = 0;
                foreach (var gr in _groups) totalH = Math.Max(totalH, gr.Bounds.Bottom);
                _form.Height = totalH + t.Layout.Padding;
            }

            UIRenderer.Render(g, _groups, t);
        }

        // ========== 清理 ==========
        public void Dispose()
        {
            _timer.Stop();
            _timer.Dispose();
            _mon.Dispose();
        }
    }
}
