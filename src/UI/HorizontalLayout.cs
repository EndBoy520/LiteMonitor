using LiteMonitor.src.Core;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace LiteMonitor
{
    public enum LayoutMode
    {
        Horizontal,
        Taskbar
    }

    public class HorizontalLayout
    {
        private readonly Theme _t;
        private readonly LayoutMode _mode;
        private readonly Settings _settings;

        private readonly int _padding;
        private int _rowH;

        // DPI
        private readonly float _dpiScale;

        public int PanelWidth { get; private set; }

        public HorizontalLayout(Theme t, int initialWidth, LayoutMode mode, Settings? settings = null)
        {
            _t = t;
            _mode = mode;
            _settings = settings ?? Settings.Load();

            using (var g = Graphics.FromHwnd(IntPtr.Zero))
            {
                _dpiScale = g.DpiX / 96f;
            }

            _padding = t.Layout.Padding;

            if (mode == LayoutMode.Horizontal)
                _rowH = Math.Max(t.FontItem.Height, t.FontValue.Height);
            else
                _rowH = 0; // 任务栏模式稍后根据 taskbarHeight 决定

            PanelWidth = initialWidth;
        }

        /// <summary>
        /// Build：横屏/任务栏共用布局
        /// </summary>
        public int Build(List<Column> cols, int taskbarHeight = 32)
        {
            if (cols == null || cols.Count == 0) return 0;
            
            var s = _settings.GetStyle();
            int pad = _padding;
            int padV = _padding / 2;
            bool isTaskbarSingle = (_mode == LayoutMode.Taskbar && _settings.TaskbarSingleLine);

            if (_mode == LayoutMode.Taskbar)
            {
                padV = 0;
                _rowH = isTaskbarSingle ? taskbarHeight : taskbarHeight / 2;
            }

            int totalWidth = pad * 2;
            float dpi = _dpiScale;

            using (var g = Graphics.FromHwnd(IntPtr.Zero))
            {
                foreach (var col in cols)
                {
                    // 分别计算 Top 和 Bottom 的所需宽度，然后取最大值
                    int widthTop = 0;
                    int widthBottom = 0;



                    // 执行测量
                    widthTop = MeasureMetricItem(g, col.Top, s);
                    widthBottom = MeasureMetricItem(g, col.Bottom, s);

                    // ★★★ 核心修复：列宽取上下两者的最大值 ★★★
                    // 这样即使 IP 在下面，列宽也会被 IP 撑大；
                    // 同时上面的普通项也能利用这个宽度正常显示（虽然左右会有空余，但不会重叠）
                    col.ColumnWidth = Math.Max(widthTop, widthBottom);
                    
                    totalWidth += col.ColumnWidth;
                }
            }


             // 组间距逻辑
            int gapBase = (_mode == LayoutMode.Taskbar) ? s.Gap : 12; 
            int gap = (int)Math.Round(gapBase * dpi); 

            if (cols.Count > 1) totalWidth += (cols.Count - 1) * gap;
            PanelWidth = totalWidth;
            
            // ===== 设置列 Bounds =====
            int x = pad;

            foreach (var col in cols)
            {
                int colHeight = isTaskbarSingle ? _rowH : _rowH * 2;
                col.Bounds = new Rectangle(x, padV, col.ColumnWidth, colHeight);

                if (_mode == LayoutMode.Taskbar)
                {
                    int fixOffset = 1; 
                    
                    if (isTaskbarSingle) {
                        col.BoundsTop = new Rectangle(x, col.Bounds.Y + fixOffset, col.ColumnWidth, colHeight);
                        col.BoundsBottom = Rectangle.Empty;
                    } else {
                        // 双行模式
                        col.BoundsTop = new Rectangle(x, col.Bounds.Y + s.VOff + fixOffset, col.ColumnWidth, _rowH - s.VOff);
                        col.BoundsBottom = new Rectangle(x, col.Bounds.Y + _rowH - s.VOff + fixOffset, col.ColumnWidth, _rowH);
                    }
                }
                else
                {
                    // 横屏模式
                    col.BoundsTop = new Rectangle(col.Bounds.X, col.Bounds.Y, col.Bounds.Width, _rowH);
                    col.BoundsBottom = new Rectangle(col.Bounds.X, col.Bounds.Y + _rowH, col.Bounds.Width, _rowH);
                }
                
                // [补充修正] 如果是 NET.IP 混合列，我们需要告诉 Renderer 不要画 Label 区域，而是全宽显示
                // 但由于 Renderer 是根据 (LabelRect, ValueRect) 绘图的，而 HorizontalLayout 不负责计算具体的 LabelRect
                // 所以我们依赖 TaskbarRenderer 的逻辑：它会看 Label 是否为空。
                // 只要列宽足够（ColumnWidth 够大），TaskbarRenderer 右对齐 Value 时就不会出问题。

                x += col.ColumnWidth + gap;
            }

            return padV * 2 + (isTaskbarSingle ? _rowH : _rowH * 2);
        }

        private int MeasureMetricItem(Graphics g, MetricItem item, Settings.TBStyle s)
        {
            if (item == null) return 0;

            float dpi = _dpiScale;

            // [通用逻辑] 如果隐藏标签 (ShortLabel 为空 或 " ")，则只计算文本宽
            if (string.IsNullOrEmpty(item.ShortLabel) || item.ShortLabel == " ")
            {
                // 对于 Dashboard/IP 类，直接使用当前文本作为测量依据
                string valText = item.TextValue ?? item.GetFormattedText(true);
                if (string.IsNullOrEmpty(valText)) return 0;

                Font valFont;
                bool disposeFont = false;

                if (_mode == LayoutMode.Taskbar)
                {
                    valFont = new Font(s.Font, s.Size, s.Bold ? FontStyle.Bold : FontStyle.Regular);
                    disposeFont = true;
                }
                else
                {
                    valFont = _t.FontItem;
                }

                try
                {
                    int w = TextRenderer.MeasureText(g, valText, valFont,
                        new Size(int.MaxValue, int.MaxValue),
                        TextFormatFlags.NoPadding).Width;
                    
                    // 纯文本项建议稍微加一点点左右 padding，防止紧贴
                    return w + 4;
                }
                finally
                {
                    if (disposeFont) valFont.Dispose();
                }
            }
            else
            {
                // [普通逻辑] 标签 + 数值 + 间距
                // 1. Label
                string label = item.ShortLabel;
                Font labelFont, valueFont;
                bool disposeFont = false;

                if (_mode == LayoutMode.Taskbar)
                {
                    var fs = s.Bold ? FontStyle.Bold : FontStyle.Regular;
                    var f = new Font(s.Font, s.Size, fs);
                    labelFont = f; valueFont = f;
                    disposeFont = true;
                }
                else
                {
                    labelFont = _t.FontItem;
                    valueFont = _t.FontValue;
                }

                try
                {
                    int wLabel = TextRenderer.MeasureText(g, label, labelFont,
                        new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding).Width;

                    // 2. Value (使用样本值估算 或 真实值)
                    string sample = GenerateSampleText(item);

                    int wValue = TextRenderer.MeasureText(g, sample, valueFont,
                        new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding).Width;

                    // 3. Padding
                    int paddingX = _rowH;
                    if (_mode == LayoutMode.Taskbar) paddingX = (int)Math.Round(s.Inner * dpi);

                    return wLabel + wValue + paddingX;
                }
                finally
                {
                    if (disposeFont)
                    {
                        labelFont.Dispose();
                        // valueFont is same reference as labelFont in Taskbar mode
                    }
                }
            }
        }

        private string GenerateSampleText(MetricItem item)
        {
            // 1. 运行时动态文本 (DASH/IP) 直接返回真实值
            if (!string.IsNullOrEmpty(item.TextValue)) return item.TextValue;

            // 2. 估算数值宽度
            var type = MetricUtils.GetType(item.Key);
            string val = type switch
            {
                MetricType.Frequency => "9.9",// GHz
                MetricType.Voltage => "1.25",// V
                MetricType.RPM => "9999",// RPM
                MetricType.Memory when _settings.MemoryDisplayMode == 1 => "99.9",// GB
                MetricType.DataSpeed => "9.9",// MB/s   
                _ => "999" // 覆盖 Power, Percent(100), Temp(100), DataSize(999), FPS(999) 等
            };

            // [Fix] 横屏模式(Panel)与任务栏模式(Taskbar)均属于水平布局，
            // 且 HorizontalRenderer 渲染时使用了 GetFormattedText(true) 即 UnitTaskbar 配置，
            // 因此测量时必须统一使用 Horizontal/Taskbar 的逻辑（紧凑单位、去除 /s 等）。
            bool isHorizontal = _mode == LayoutMode.Taskbar || _mode == LayoutMode.Horizontal;

            // 3. 确定基础单位 (用于占位或替换 {u})
            // 使用重构后的 GetDefaultUnit，根据模式自动获取正确的单位（包含空格）
            string rawUnit = type switch
            {
                MetricType.DataSpeed or MetricType.DataSize => "MB",
                _ => MetricUtils.GetDefaultUnit(item.Key, isHorizontal ? MetricUtils.UnitContext.Taskbar : MetricUtils.UnitContext.Panel)
                                .Replace("{u}", "") // 移除占位符
            };

            // 4. 获取最终显示单位 (处理自定义格式和 Auto 模式)
            string userFmt = isHorizontal ? item.BoundConfig?.UnitTaskbar : item.BoundConfig?.UnitPanel;
            string unit = MetricUtils.GetDisplayUnit(item.Key, rawUnit, userFmt);

            // [Fix] 任务栏/横屏模式下，GetDisplayUnit 可能会强制加上 /s (针对 DataSpeed)，
            // 但通常空间紧凑，不显示 /s，这里需要修正以避免列宽过宽。
            if (isHorizontal && type == MetricType.DataSpeed && unit.EndsWith("/s"))
            {
                unit = unit.Substring(0, unit.Length - 2);
            }

            // 5. 电池充电图标
            if (MetricUtils.IsBatteryCharging && item.Key.StartsWith("BAT", StringComparison.OrdinalIgnoreCase))
                unit += "⚡";

            return val + unit;
        }

        private string GetMaxValueSample(Column col, bool isTop)
        {
            var item = isTop ? col.Top : col.Bottom;
            if (item == null) return "";

            // 动态文本仅关注长度变化
            if (!string.IsNullOrEmpty(item.TextValue))
                return new string('0', item.TextValue.Length);

            return GenerateSampleText(item);
        }

        // [通用方案] 获取当前布局的签名是否变化 (用于检测是否需要重绘)
        // 拼接所有列的 MaxValueSample，如果签名变了，说明列宽可能需要调整
        public string GetLayoutIsChage(List<Column> cols)
        {
            if (cols == null || cols.Count == 0) return "";
            
            // 简单哈希或拼接。为了性能，这里只取前几列或关键特征，或者简单地拼接长度
            // 鉴于列数不多，StringBuilder 拼接所有 Sample 长度即可
            // 或者直接用 unchecked 加法算一个 HashCode
            unchecked
            {
                int hash = 17;
                foreach (var col in cols)
                {
                    string sTop = GetMaxValueSample(col, true);
                    string sBot = GetMaxValueSample(col, false);
                    hash = hash * 31 + sTop.Length; // 关注长度变化即可 (因为主要是宽度撑开)
                    hash = hash * 31 + sTop.GetHashCode(); // 加上内容哈希更保险
                    hash = hash * 31 + sBot.Length;
                    hash = hash * 31 + sBot.GetHashCode();
                }
                return hash.ToString();
            }
        }
    }

    public class Column
    {
        public MetricItem? Top;
        public MetricItem? Bottom;

        public int ColumnWidth;
        public Rectangle Bounds = Rectangle.Empty;

        // ★★ B 方案新增：上下行布局由 Layout 计算，不再由 Renderer 处理
        public Rectangle BoundsTop = Rectangle.Empty;
        public Rectangle BoundsBottom = Rectangle.Empty;
    }
}
