using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace LiteMonitor
{
    public static class UIRenderer
    {
        // ========== Brush Cache ==========
        private static readonly Dictionary<int, SolidBrush> _brushCache = new();

        private static SolidBrush GetBrush(Color c)
        {
            int key = c.ToArgb();
            if (_brushCache.TryGetValue(key, out var br)) return br;
            br = new SolidBrush(c);
            _brushCache[key] = br;
            return br;
        }

        private static SolidBrush GetBrush(string colorStr, Theme t)
        {
            var c = ThemeManager.ParseColor(colorStr);
            return GetBrush(c);
        }

        // ========== 主绘制入口 ==========
        public static void Render(Graphics g, List<GroupLayoutInfo> groups, Theme t)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            // 背景
            var bg = GetBrush(t.Color.Background, t);
            int h = groups.Count > 0 ? groups[^1].Bounds.Bottom + t.Layout.Padding : t.Layout.Width;
            g.FillRectangle(bg, new Rectangle(0, 0, t.Layout.Width, h));

            // 顶部标题
            string title = LanguageManager.T("Title");
            if (string.IsNullOrEmpty(title) || title == "Title") title = "LiteMonitor"; // ✅ 默认标题

            int titleHeight = TextRenderer.MeasureText(title, t.FontTitle).Height;
            var titleRect = new Rectangle(
                t.Layout.Padding,
                t.Layout.Padding,
                t.Layout.Width - t.Layout.Padding * 2,
                titleHeight + 4  // ↑ 给一点余量
            );
            TextRenderer.DrawText(
                g,
                title,
                t.FontTitle,
                titleRect,
                ThemeManager.ParseColor(t.Color.TextTitle),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding
            );

            // 各分组
            foreach (var gr in groups)
                DrawGroup(g, gr, t);
        }

        // ========== 分组块绘制 ==========
        private static void DrawGroup(Graphics g, GroupLayoutInfo gr, Theme t)
        {
            int gp = t.Layout.GroupPadding;
            int radius = t.Layout.GroupRadius;

            // 背景块
            var block = new Rectangle(gr.Bounds.X, gr.Bounds.Y, gr.Bounds.Width, gr.Bounds.Height - t.Layout.GroupBottom);
            var brGroup = GetBrush(t.Color.GroupBackground, t);
            using (var path = RoundedRect(block, radius))
                g.FillPath(brGroup, path);

            // 分组标题
            string groupLabel = LanguageManager.T($"Groups.{gr.GroupName}");
            if (string.IsNullOrEmpty(groupLabel)) groupLabel = gr.GroupName;

            int titleH = (int)(t.Font.Group * 2.0);
            int titleY = block.Y - t.Layout.GroupTitleOffset - titleH;
            var titleRect = new Rectangle(block.X + gp, Math.Max(0, titleY),
                                          block.Width - gp * 2, titleH);

            TextRenderer.DrawText(
                g,
                groupLabel,
                t.FontGroup,
                titleRect,
                ThemeManager.ParseColor(t.Color.TextGroup),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding
            );

            // 特殊组绘制
            if (gr.GroupName.Equals("DISK", StringComparison.OrdinalIgnoreCase))
            {
                DrawTwoColsRow(
                    g, gr.Items, t,
                    "DISK", "DISK.Read", "DISK.Write",
                    LanguageManager.T("Items.DISK.Read"),
                    LanguageManager.T("Items.DISK.Write"));
                return;
            }

            if (gr.GroupName.Equals("NET", StringComparison.OrdinalIgnoreCase))
            {
                DrawTwoColsRow(
                    g, gr.Items, t,
                    "NET", "NET.Up", "NET.Down",
                    LanguageManager.T("Items.NET.Up"),
                    LanguageManager.T("Items.NET.Down"));
                return;
            }

            // 普通项绘制
            foreach (var it in gr.Items)
                DrawMetricItem(g, it, t);
        }

        // ========== 单项绘制（带进度条） ==========
        private static void DrawMetricItem(Graphics g, MetricItem it, Theme t)
        {
            if (it.Bounds == Rectangle.Empty) return;

            var inner = new Rectangle(it.Bounds.X + 10, it.Bounds.Y, it.Bounds.Width - 20, it.Bounds.Height);
            int topH = (int)(inner.Height * 0.55);
            var topRect = new Rectangle(inner.X, inner.Y, inner.Width, topH);

            // 文案国际化
            string label = LanguageManager.T($"Items.{it.Key}");
            if (label == $"Items.{it.Key}") label = it.Label; // fallback

            // 左侧标签
            TextRenderer.DrawText(
                g,
                label,
                t.FontItem,
                topRect,
                ThemeManager.ParseColor(t.Color.TextPrimary),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding
            );

            // 数值文本
            string text = BuildValueText(it);
            var (warn, crit) = GetThresholds(it.Key, t);
            Color valueColor = ChooseColor(it.DisplayValue, warn, crit, t, true);

            TextRenderer.DrawText(
                g,
                text,
                t.FontValue,
                topRect,
                valueColor,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding
            );

            // 进度条
            int barH = Math.Max(6, (int)(inner.Height * 0.25));
            int barY = inner.Bottom - barH - 3;
            var barRect = new Rectangle(inner.X, barY, inner.Width, barH);

            var brBg = GetBrush(t.Color.BarBackground, t);
            using (var pathBg = RoundedRect(barRect, barH / 2))
                g.FillPath(brBg, pathBg);

            float v = Math.Clamp(it.DisplayValue, 0f, 100f);
            if (v > 0.001f)
            {
                float displayPercent = Math.Max(5f, v);
                int w = (int)(barRect.Width * (displayPercent / 100f));
                var fgRect = new Rectangle(barRect.X, barRect.Y, w, barRect.Height);
                Color fgColor = ChooseColor(v, warn, crit, t, false);
                var brFg = GetBrush(fgColor);
                using var pathFg = RoundedRect(fgRect, barH / 2);
                g.FillPath(brFg, pathFg);
            }
        }

        // ========== 双列绘制（NET / DISK） ==========
        private static void DrawTwoColsRow(
            Graphics g,
            List<MetricItem> items,
            Theme t,
            string prefix,
            string leftKey,
            string rightKey,
            string leftLabel,
            string rightLabel,
            double verticalOffset = 0.1
        )
        {
            var leftItem = items.FirstOrDefault(i => i.Key.Equals(leftKey, StringComparison.OrdinalIgnoreCase));
            var rightItem = items.FirstOrDefault(i => i.Key.Equals(rightKey, StringComparison.OrdinalIgnoreCase));
            if (leftItem == null && rightItem == null) return;

            var baseRect = (leftItem ?? rightItem)!.Bounds;
            int rowH = baseRect.Height;
            int colW = baseRect.Width / 2;

            var left = new Rectangle(baseRect.X, baseRect.Y, colW, rowH);
            var right = new Rectangle(left.Right, baseRect.Y, colW, rowH);

            int offsetY = (int)(rowH * verticalOffset);

            // 标签
            var leftShift = new Rectangle(left.X, left.Y + offsetY, left.Width, left.Height);
            var rightShift = new Rectangle(right.X, right.Y + offsetY, right.Width, right.Height);

            TextRenderer.DrawText(
                g,
                leftLabel,
                t.FontItem,
                leftShift,
                ThemeManager.ParseColor(t.Color.TextPrimary),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.NoPadding
            );
            TextRenderer.DrawText(
                g,
                rightLabel,
                t.FontItem,
                rightShift,
                ThemeManager.ParseColor(t.Color.TextPrimary),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.NoPadding
            );

            // 数值
            int valueExtraOffset = (int)(rowH * verticalOffset);
            var leftValueRect = new Rectangle(leftShift.X, leftShift.Y + valueExtraOffset, leftShift.Width, leftShift.Height);
            var rightValueRect = new Rectangle(rightShift.X, rightShift.Y + valueExtraOffset, rightShift.Width, rightShift.Height);

            string leftStr = FormatNet(leftItem?.Value);
            string rightStr = FormatNet(rightItem?.Value);

            double leftKBps = (leftItem?.Value ?? 0f) / 1024.0;
            double rightKBps = (rightItem?.Value ?? 0f) / 1024.0;

            var leftColor = ChooseNetColor(leftKBps, t);
            var rightColor = ChooseNetColor(rightKBps, t);

            TextRenderer.DrawText(
                g,
                leftStr,
                t.FontValue,
                leftValueRect,
                leftColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.Bottom | TextFormatFlags.NoPadding
            );
            TextRenderer.DrawText(
                g,
                rightStr,
                t.FontValue,
                rightValueRect,
                rightColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.Bottom | TextFormatFlags.NoPadding
            );
        }

        // ========== 工具函数 ==========
        private static string BuildValueText(MetricItem it)
        {
            string k = it.Key.ToUpperInvariant();
            if (k.Contains("LOAD") || k.Contains("VRAM") || k.Contains("MEM")) return $"{it.DisplayValue:0.0}%";
            if (k.Contains("TEMP")) return $"{it.DisplayValue:0.0}°C";
            return $"{it.DisplayValue:0.0}";
        }

        private static (double warn, double crit) GetThresholds(string key, Theme t)
        {
            string k = key.ToUpperInvariant();
            if (k.Contains("LOAD")) return (t.Thresholds.Load.Warn, t.Thresholds.Load.Crit);
            if (k.Contains("TEMP")) return (t.Thresholds.Temp.Warn, t.Thresholds.Temp.Crit);
            if (k.Contains("VRAM")) return (t.Thresholds.Vram.Warn, t.Thresholds.Vram.Crit);
            if (k.Contains("MEM")) return (t.Thresholds.Mem.Warn, t.Thresholds.Mem.Crit);
            return (t.Thresholds.Load.Warn, t.Thresholds.Load.Crit);
        }

        private static Color ChooseColor(double v, double warn, double crit, Theme t, bool forValue)
        {
            if (double.IsNaN(v)) return ThemeManager.ParseColor(t.Color.TextPrimary);
            if (v < warn) return ThemeManager.ParseColor(forValue ? t.Color.ValueSafe : t.Color.BarLow);
            if (v < crit) return ThemeManager.ParseColor(forValue ? t.Color.ValueWarn : t.Color.BarMid);
            return ThemeManager.ParseColor(forValue ? t.Color.ValueCrit : t.Color.BarHigh);
        }

        private static string FormatNet(float? bytes)
        {
            if (!bytes.HasValue) return "0.0KB/s";
            double kb = bytes.Value / 1024.0;
            return kb >= 1024 ? $"{kb / 1024.0:0.00}MB/s" : $"{kb:0.0}KB/s";
        }

        private static Color ChooseNetColor(double kbps, Theme t)
        {
            if (double.IsNaN(kbps)) return ThemeManager.ParseColor(t.Color.TextPrimary);
            double warn = t.Thresholds.NetKBps.Warn;
            double crit = t.Thresholds.NetKBps.Crit;
            if (kbps < warn) return ThemeManager.ParseColor(t.Color.ValueSafe);
            if (kbps < crit) return ThemeManager.ParseColor(t.Color.ValueWarn);
            return ThemeManager.ParseColor(t.Color.ValueCrit);
        }

        private static GraphicsPath RoundedRect(Rectangle rect, int radius)
        {
            int d = Math.Max(0, radius) * 2;
            var gp = new GraphicsPath();
            if (d <= 0) { gp.AddRectangle(rect); gp.CloseFigure(); return gp; }
            gp.AddArc(rect.X, rect.Y, d, d, 180, 90);
            gp.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            gp.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            gp.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            gp.CloseFigure();
            return gp;
        }
    }
}
