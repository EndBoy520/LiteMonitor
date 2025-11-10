using System;
using System.Drawing;
using System.IO;
using System.Text.Json;

namespace LiteMonitor
{
    public class Settings
    {
        // ====== 主题 / 行为基础 ======
        public string Skin { get; set; } = "DarkNeo_Modern";
        public int PanelWidth { get; set; } = 240;   // ← 默认值与主题默认宽度一致
        public bool TopMost { get; set; } = true;
        public bool AutoStart { get; set; } = false;
        public int RefreshMs { get; set; } = 300;
        public Point Position { get; set; } = new Point(-1, -1);

        // ====== 新增：用户偏好（从主题里迁移出来的 & 新功能需要） ======
        public double Opacity { get; set; } = 0.95;   // ← 窗口透明度（原 theme.window.opacity）
        public string Language { get; set; } = "zh";  // ← 语言：zh / en（对应 zh.json / en.json）
        public bool ClickThrough { get; set; } = false; // ← 鼠标穿透
        public bool AutoHide { get; set; } = false;     // ← 靠边自动隐藏

        // ====== 显示项（整组/子项开关）======
        public EnabledSet Enabled { get; set; } = new();

        private static string FilePath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");

        public static Settings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var json = File.ReadAllText(FilePath);
                    var s = JsonSerializer.Deserialize<Settings>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                    return s ?? new Settings();
                }
            }
            catch { }
            return new Settings();
        }

        public void Save()
        {
            try
            {
                var json = JsonSerializer.Serialize(this, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                File.WriteAllText(FilePath, json);
            }
            catch { }
        }
    }

    public class EnabledSet
    {
        public bool CpuLoad { get; set; } = true;
        public bool CpuTemp { get; set; } = true;

        public bool GpuLoad { get; set; } = true;
        public bool GpuTemp { get; set; } = true;
        public bool GpuVram { get; set; } = true;

        public bool MemLoad { get; set; } = true;

        // ★ 磁盘与网络：用于“整组隐藏”判断（DISK / NET）
        public bool DiskRead { get; set; } = true;
        public bool DiskWrite { get; set; } = true;

        public bool NetUp { get; set; } = true;
        public bool NetDown { get; set; } = true;
    }
}
