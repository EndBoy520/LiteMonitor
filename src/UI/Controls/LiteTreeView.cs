using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using LibreHardwareMonitor.Hardware;
using LiteMonitor.src.Core;

namespace LiteMonitor.src.UI.Controls
{
    public class LiteTreeView : TreeView
    {
        private static readonly Brush _selectBgBrush = new SolidBrush(Color.FromArgb(204, 232, 255)); 
        private static readonly Brush _hoverBrush = new SolidBrush(Color.FromArgb(250, 250, 250)); 
        private static readonly Pen _linePen = new Pen(Color.FromArgb(240, 240, 240)); 
        private static readonly Brush _chevronBrush = new SolidBrush(Color.Gray); 

        private Font _baseFont;
        private Font _boldFont;

        // 布局参数
        public int ColValueWidth { get; set; } = 70;  
        public int ColMaxWidth { get; set; } = 70;
        public int RightMargin { get; set; } = 6;    
        public int IconWidth { get; set; } = 20;      

        public LiteTreeView()
        {
            // ★★★ 修复1：移除 AllPaintingInWmPaint，防止黑屏 ★★★
            // 只保留基本的双缓冲设置
            this.SetStyle(
                ControlStyles.OptimizedDoubleBuffer | 
                ControlStyles.ResizeRedraw, true);

            this.DrawMode = TreeViewDrawMode.OwnerDrawText; 
            this.ShowLines = false;
            this.ShowPlusMinus = false; 
            this.FullRowSelect = true;
            this.BorderStyle = BorderStyle.None;
            this.BackColor = Color.White;
            this.ItemHeight = UIUtils.S(28); 

            _baseFont = new Font("Microsoft YaHei UI", 9f);
            _boldFont = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold);
            this.Font = _baseFont;
        }

        // ★★★ 修复2：启用 WS_EX_COMPOSITED (终极防闪烁) ★★★
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x02000000; // WS_EX_COMPOSITED (双缓冲合成)
                return cp;
            }
        }

        // ★★★ 修复3：彻底删除 WndProc 方法 (让系统正常擦除背景，解决底部黑屏) ★★★

        public void InvalidateSensorValue(TreeNode node)
        {
            if (node == null || node.Bounds.Height <= 0) return;
            
            // 计算需要重绘的右侧区域宽度
            // 包含：Value列 + Max列 + 右边距 + 你在 OnDrawNode 增加的间距 (25 + 20)
            int refreshWidth = UIUtils.S(ColValueWidth + ColMaxWidth + RightMargin + 25 + 20); 
            int safeWidth = this.ClientSize.Width;

            Rectangle dirtyRect = new Rectangle(safeWidth - refreshWidth, node.Bounds.Y, refreshWidth, node.Bounds.Height);
            this.Invalidate(dirtyRect);
        }

        protected override void OnDrawNode(DrawTreeNodeEventArgs e)
        {
            if (e.Bounds.Height <= 0 || e.Bounds.Width <= 0) return;

            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            int w = this.ClientSize.Width; 
            Rectangle fullRow = new Rectangle(0, e.Bounds.Y, w, this.ItemHeight);

            // 绘制背景
            if ((e.State & TreeNodeStates.Selected) != 0) g.FillRectangle(_selectBgBrush, fullRow);
            else if ((e.State & TreeNodeStates.Hot) != 0) g.FillRectangle(_hoverBrush, fullRow);
            else g.FillRectangle(Brushes.White, fullRow);

            // 分割线
            g.DrawLine(_linePen, 0, fullRow.Bottom - 1, w, fullRow.Bottom - 1);

            // --- 坐标计算 (左侧图标) ---
            // 基础缩进量
            int baseIndent = e.Node.Level * UIUtils.S(20);
            // 图标区域 (在文本之前)
            Rectangle chevronRect = new Rectangle(baseIndent + UIUtils.S(5), fullRow.Y, UIUtils.S(IconWidth), fullRow.Height);

            // --- 坐标计算 (右侧数值) ---
            int xBase = w - UIUtils.S(RightMargin); 
            
            // Max 列区域
            // 同步修改：使用 25 的间距
            int xMax = xBase - UIUtils.S(25) - UIUtils.S(ColMaxWidth);
            Rectangle maxRect = new Rectangle(xMax, fullRow.Y, UIUtils.S(ColMaxWidth), fullRow.Height);

            // Value 列区域 (在 Max 左侧)
            // 同步修改：使用 20 的间距
            int xValue = xMax - UIUtils.S(20) - UIUtils.S(ColValueWidth);
            Rectangle valRect = new Rectangle(xValue, fullRow.Y, UIUtils.S(ColValueWidth), fullRow.Height);

            // 3. 绘制折叠图标 (如果有子节点，画在左侧)
            if (e.Node.Nodes.Count > 0)
            {
                DrawChevron(g, chevronRect, e.Node.IsExpanded);
            }

            // 4. 绘制数值 (仅传感器)
            if (e.Node.Tag is ISensor sensor)
            {
                // Max (灰色) - SingleLine
                string maxStr = FormatValue(sensor.Max, sensor.SensorType);
                TextRenderer.DrawText(g, maxStr, _baseFont, maxRect, Color.Gray, 
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Right | TextFormatFlags.SingleLine);

                // Value (彩色) - SingleLine
                string valStr = FormatValue(sensor.Value, sensor.SensorType);
                Color valColor = GetColorByType(sensor.SensorType);
                TextRenderer.DrawText(g, valStr, _baseFont, valRect, valColor, 
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Right | TextFormatFlags.SingleLine);
            }

            // 5. 绘制文本
            Color txtColor;
            Font font;

            if (e.Node.Tag is IHardware) 
            {
                font = _boldFont;
                txtColor = Color.Black;
            }
            else if (e.Node.Tag is ISensor)
            {
                font = _baseFont;
                txtColor = Color.FromArgb(00, 00, 00); 
            }
            else 
            {
                font = _boldFont;
                txtColor = Color.FromArgb(30, 30, 30); 
            }

            // 文本起始位置在图标之后
            int textStartX = chevronRect.Right + UIUtils.S(5);
            // 文本宽度截止到 Value 列之前
            int textWidth = xValue - textStartX - UIUtils.S(10); 
            Rectangle textRect = new Rectangle(textStartX, fullRow.Y, textWidth, fullRow.Height);
            
            TextRenderer.DrawText(g, e.Node.Text, font, textRect, txtColor, 
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }

        private void DrawChevron(Graphics g, Rectangle rect, bool expanded)
        {
            int cx = rect.X + rect.Width / 2;
            int cy = rect.Y + rect.Height / 2;
            int size = UIUtils.S(4);

            using (Pen p = new Pen(_chevronBrush, 1.5f))
            {
                if (expanded) // V
                {
                    g.DrawLine(p, cx - size, cy - 2, cx, cy + 3);
                    g.DrawLine(p, cx, cy + 3, cx + size, cy - 2);
                }
                else // >
                {
                    g.DrawLine(p, cx - 2, cy - size, cx + 2, cy);
                    g.DrawLine(p, cx + 2, cy, cx - 2, cy + size);
                }
            }
        }

        private Color GetColorByType(SensorType type)
        {
            switch (type) {
                case SensorType.Temperature: return Color.FromArgb(200, 60, 0); 
                case SensorType.Load: return Color.FromArgb(0, 100, 0); 
                case SensorType.Power: return Color.Purple;
                case SensorType.Clock: return Color.DarkBlue;
                default: return Color.Black;
            }
        }

        private string FormatValue(float? val, SensorType type)
        {
            if (!val.HasValue) return "-";
            float v = val.Value;
            switch (type)
            {
                case SensorType.Voltage: return $"{v:F3} V";
                case SensorType.Clock: return v >= 1000 ? $"{v/1000:F1} GHz" : $"{v:F0} MHz";
                case SensorType.Temperature: return $"{v:F0} °C";
                case SensorType.Load: return $"{v:F1} %";
                case SensorType.Fan: return $"{v:F0} RPM";
                case SensorType.Power: return $"{v:F1} W";
                case SensorType.Data: return $"{v:F1} GB";
                case SensorType.SmallData: return $"{v:F0} MB";
                case SensorType.Throughput: return UIUtils.FormatDataSize(v, "/s");
                default: return $"{v:F1}";
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            var node = this.GetNodeAt(e.X, e.Y);
            if (node != null && node.Nodes.Count > 0)
            {
                // 计算左侧图标的有效点击区域
                int baseIndent = node.Level * UIUtils.S(20);
                // 给图标左右各加一点缓冲区域方便点击
                int clickAreaStart = baseIndent;
                int clickAreaEnd = baseIndent + UIUtils.S(IconWidth) + UIUtils.S(15);

                // 如果点击了左侧图标区域
                if (e.X >= clickAreaStart && e.X <= clickAreaEnd) 
                {
                    if (node.IsExpanded) node.Collapse(); else node.Expand();
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _baseFont?.Dispose(); _boldFont?.Dispose(); }
            base.Dispose(disposing);
        }
    }
}