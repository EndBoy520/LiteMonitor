using LiteMonitor.src.Core;
using System.Drawing.Text;

namespace LiteMonitor
{
    /// <summary>
    /// 任务栏渲染器（仅负责绘制，不再负责布局）
    /// </summary>
    public static class TaskbarRenderer
    {
        //private static readonly Settings _settings = Settings.Load();
        
        // 字体缓存 - 直接初始化，避免每次渲染都创建字体
        private static Font? _cachedFont = null;

        // 浅色主题
        private static readonly Color LABEL_LIGHT = Color.FromArgb(20, 20, 20);
        private static readonly Color SAFE_LIGHT = Color.FromArgb(0x00, 0x80, 0x40);
        private static readonly Color WARN_LIGHT = Color.FromArgb(0xB5, 0x75, 0x00);
        private static readonly Color CRIT_LIGHT = Color.FromArgb(0xC0, 0x30, 0x30);

        // 深色主题
        private static readonly Color LABEL_DARK = Color.White;
        private static readonly Color SAFE_DARK = Color.FromArgb(0x66, 0xFF, 0x99);
        private static readonly Color WARN_DARK = Color.FromArgb(0xFF, 0xD6, 0x66);
        private static readonly Color CRIT_DARK = Color.FromArgb(0xFF, 0x66, 0x66);

        // ★★★ [新增] 自定义颜色缓存 ★★★
        private static bool _useCustom = false;
        private static Color _cLabel, _cSafe, _cWarn, _cCrit;

        // ★★★ [新增] 极简的核心：手动刷新缓存 ★★★
        // 在 UIController 初始化或配置变更时调用它
        public static void ReloadStyle(Settings cfg)
        {
            // 1. 释放旧字体
            _cachedFont?.Dispose();

            // 2. 创建新字体 (使用传入的最新 cfg)
            var style = cfg.TaskbarFontBold ? FontStyle.Bold : FontStyle.Regular;
            _cachedFont = new Font(cfg.TaskbarFontFamily, cfg.TaskbarFontSize, style);

            // ★★★ [新增] 读取自定义颜色配置 ★★★
            _useCustom = cfg.TaskbarCustomStyle;
            if (_useCustom)
            {
                try {
                    _cLabel = ColorTranslator.FromHtml(cfg.TaskbarColorLabel);
                    _cSafe = ColorTranslator.FromHtml(cfg.TaskbarColorSafe);
                    _cWarn = ColorTranslator.FromHtml(cfg.TaskbarColorWarn);
                    _cCrit = ColorTranslator.FromHtml(cfg.TaskbarColorCrit);
                } catch {
                    // 容错：如果解析失败，回退到默认
                    _useCustom = false; 
                }
            }
        }

        public static void Render(Graphics g, List<Column> cols, bool light) // <--- 新的
        {
            // [防空策略] 万一还没人调用 ReloadStyle，就自己兜底初始化一次
            if (_cachedFont == null)
            {
                // 兜底：读磁盘配置（仅第一次）
                ReloadStyle(Settings.Load());
            }
            
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            // 使用传入的 light 参数，避免每次都查询系统主题瓠
            //bool light = IsSystemLight();

            foreach (var col in cols)
            {
                if (col.BoundsTop != Rectangle.Empty && col.Top != null)
                    DrawItem(g, col.Top, col.BoundsTop, light);

                if (col.BoundsBottom != Rectangle.Empty && col.Bottom != null)
                    DrawItem(g, col.Bottom, col.BoundsBottom, light);
            }
        }

        private static void DrawItem(Graphics g, MetricItem item, Rectangle rc, bool light)
        {
            string label = LanguageManager.T($"Short.{item.Key}");
            string value = item.GetFormattedText(true);

            // 直接使用缓存的字体，不再 new Font
            Font font = _cachedFont!;

            Color labelColor, valueColor;

            // ★★★ [修改] 颜色选择逻辑 ★★★
            if (_useCustom)
            {
                // 自定义模式：忽略系统明暗，强制使用自定义色
                labelColor = _cLabel;
                valueColor = PickCustomColor(item.Key, item.DisplayValue);
            }
            else
            {
                // 原有模式
                labelColor = light ? LABEL_LIGHT : LABEL_DARK;
                valueColor = PickColor(item.Key, item.DisplayValue, light);
            }
            // Label 左对齐
            TextRenderer.DrawText(
                g, label, font, rc, labelColor,
                TextFormatFlags.Left |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPadding |
                TextFormatFlags.NoClipping
            );

            // Value 右对齐
            TextRenderer.DrawText(
                g, value, font, rc, valueColor,
                TextFormatFlags.Right |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPadding |
                TextFormatFlags.NoClipping
            );
        }
        // ★★★ [新增] 自定义颜色提取逻辑 ★★★
        private static Color PickCustomColor(string key, double v)
        {
            if (double.IsNaN(v)) return _cLabel;
            int result = UIUtils.GetColorResult(key, v);
            if (result == 2) return _cCrit;
            if (result == 1) return _cWarn;
            return _cSafe;
        }
        private static Color PickColor(string key, double v, bool light)
        {
            if (double.IsNaN(v)) return light ? LABEL_LIGHT : LABEL_DARK;
            
            // 调用核心逻辑
            int result = UIUtils.GetColorResult(key, v); 

            if (result == 2) return light ? CRIT_LIGHT : CRIT_DARK; // 翻译为硬编码的红色
            if (result == 1) return light ? WARN_LIGHT : WARN_DARK; // 翻译为硬编码的黄色
            return light ? SAFE_LIGHT : SAFE_DARK;                   // 翻译为硬编码的绿色
        }
    }
}
