using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LiteMonitor
{
    // ========== 主题配置类结构 ==========
    public class WindowConfig
    {
        public double Opacity { get; set; } = 0.95;
    }

    public class LayoutConfig
    {
        public int Width { get; set; } = 220;
        public int RowHeight { get; set; } = 40;
        public int Padding { get; set; } = 12;

        public int CornerRadius { get; set; } = 12;
        public int GroupRadius { get; set; } = 10;
        public int GroupPadding { get; set; } = 8;

        public int GroupSpacing { get; set; } = 14;
        public int GroupBottom { get; set; } = 6;
        public int ItemGap { get; set; } = 6;

        public int GroupTitleOffset { get; set; } = 6;
        public string GroupTitleAlign { get; set; } = "left";

        public double AnimationSpeed { get; set; } = 0.18;
    }

    public class FontConfig
    {
        public string Family { get; set; } = "Microsoft YaHei UI";
        public string ValueFamily { get; set; } = "Consolas";
        public double Title { get; set; } = 11.5;
        public double Group { get; set; } = 10.5;
        public double Item { get; set; } = 10.0;
        public double Value { get; set; } = 10.5;
        public bool Bold { get; set; } = true;
        public double Scale { get; set; } = 1.0;
    }

    public class ThresholdSet
    {
        public double Warn { get; set; } = 70;
        public double Crit { get; set; } = 90;
    }

    public class ThresholdConfig
    {
        public ThresholdSet Load { get; set; } = new() { Warn = 65, Crit = 85 };
        public ThresholdSet Temp { get; set; } = new() { Warn = 50, Crit = 70 };
        public ThresholdSet Vram { get; set; } = new() { Warn = 65, Crit = 85 };
        public ThresholdSet Mem { get; set; } = new() { Warn = 65, Crit = 85 };
        public ThresholdSet NetKBps { get; set; } = new() { Warn = 2048, Crit = 8192 };
    }

    public class BehaviorConfig
    {
        public bool PauseOnDrag { get; set; } = true;
        public bool ShowShadow { get; set; } = true;
        public bool ShowBorder { get; set; } = true;

        public List<string> GroupOrder { get; set; } = new() { "CPU", "GPU", "MEM", "DISK", "NET" };
        public List<string> DisplayOrder { get; set; } = new() { "Load", "Temp", "VRAM", "MEM", "Read", "Write", "Up", "Down" };
        public bool PerformanceMode { get; set; } = false;
    }

    // 扁平颜色结构（避免复杂嵌套）
    public class ColorConfig
    {
        public string Background { get; set; } = "#202225";
        public string Border { get; set; } = "#333333";
        public string Shadow { get; set; } = "rgba(0,0,0,0.35)";

        public string TextTitle { get; set; } = "#FFFFFF";
        public string TextGroup { get; set; } = "#B0B0B0";
        public string TextPrimary { get; set; } = "#EAEAEA";
        public string TextSecondary { get; set; } = "#888888";

        public string ValueSafe { get; set; } = "#66FF99";
        public string ValueWarn { get; set; } = "#FFD666";
        public string ValueCrit { get; set; } = "#FF6666";

        public string BarBackground { get; set; } = "#1C1C1C";
        public string BarLow { get; set; } = "#00C853";
        public string BarMid { get; set; } = "#FFAB00";
        public string BarHigh { get; set; } = "#D50000";

        public string GroupBackground { get; set; } = "#2B2D31";
        public string GroupSeparator { get; set; } = "#3A3A3A";
    }

    public class ExtensionConfig
    {
        public List<string> CustomMetrics { get; set; } = new(); // 例如: ["Fan.Speed"]
    }

    // ========== Theme 主对象 ==========
    public class Theme
    {
        public string Name { get; set; } = "Default";
        public int Version { get; set; } = 3;

        public WindowConfig Window { get; set; } = new();
        public LayoutConfig Layout { get; set; } = new();
        public FontConfig Font { get; set; } = new();
        public BehaviorConfig Behavior { get; set; } = new();
        public ThresholdConfig Thresholds { get; set; } = new();
        public ColorConfig Color { get; set; } = new();
        public ExtensionConfig Extensions { get; set; } = new();

        // 运行时字体
        [JsonIgnore] public Font FontTitle;
        [JsonIgnore] public Font FontGroup;
        [JsonIgnore] public Font FontItem;
        [JsonIgnore] public Font FontValue;

        public void BuildFonts()
        {
            var style = Font.Bold ? FontStyle.Bold : FontStyle.Regular;
            FontTitle = new Font(Font.Family, (float)(Font.Title * Font.Scale), style);
            FontGroup = new Font(Font.Family, (float)(Font.Group * Font.Scale), style);
            FontItem = new Font(Font.Family, (float)(Font.Item * Font.Scale), style);
            FontValue = new Font(Font.ValueFamily, (float)(Font.Value * Font.Scale), style);
        }
    }

    // ========== Theme 管理器 ==========
    public static class ThemeManager
    {
        public static Theme Current { get; private set; } = new();

        public static string ThemeDir
        {
            get
            {
                var dir = Path.Combine(AppContext.BaseDirectory, "themes");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                return dir;
            }
        }

        public static IEnumerable<string> GetAvailableThemes()
        {
            try
            {
                return Directory.EnumerateFiles(ThemeDir, "*.json")
                                .Select(f => Path.GetFileNameWithoutExtension(f))
                                .OrderBy(n => n)
                                .ToList();
            }
            catch { return Enumerable.Empty<string>(); }
        }

        public static Theme Load(string name)
        {
            try
            {
                var path = Path.Combine(ThemeDir, $"{name}.json");
                if (!File.Exists(path)) throw new FileNotFoundException("Theme json not found", path);

                var json = File.ReadAllText(path);
                var theme = JsonSerializer.Deserialize<Theme>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    IgnoreReadOnlyProperties = true,
                    AllowTrailingCommas = true
                });
                if (theme == null) throw new Exception("Theme parse failed.");

                theme.BuildFonts();
                Current = theme;
                Console.WriteLine($"[ThemeManager] Loaded theme: {theme.Name} (v{theme.Version})");
                return theme;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ThemeManager] Load error: {ex.Message}");
                var fallback = new Theme();
                fallback.BuildFonts();
                Current = fallback;
                return fallback;
            }
        }

        // 通用颜色解析函数
        public static Color ParseColor(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return Color.White;
            s = s.Trim();

            if (s.StartsWith("rgba", StringComparison.OrdinalIgnoreCase))
            {
                var nums = s.Replace("rgba", "", StringComparison.OrdinalIgnoreCase)
                            .Trim('(', ')')
                            .Split(',', StringSplitOptions.RemoveEmptyEntries);
                if (nums.Length >= 4 &&
                    int.TryParse(nums[0], out int r) &&
                    int.TryParse(nums[1], out int g) &&
                    int.TryParse(nums[2], out int b) &&
                    float.TryParse(nums[3], out float a))
                {
                    return Color.FromArgb((int)(a * 255), r, g, b);
                }
            }

            if (s.StartsWith("#")) s = s[1..];
            if (s.Length == 6)
                return Color.FromArgb(255,
                    Convert.ToInt32(s[..2], 16),
                    Convert.ToInt32(s.Substring(2, 2), 16),
                    Convert.ToInt32(s.Substring(4, 2), 16));

            if (s.Length == 8)
                return Color.FromArgb(
                    Convert.ToInt32(s[..2], 16),
                    Convert.ToInt32(s.Substring(2, 2), 16),
                    Convert.ToInt32(s.Substring(4, 2), 16),
                    Convert.ToInt32(s.Substring(6, 2), 16));

            return Color.White;
        }
    }
}
