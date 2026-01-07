using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using LiteMonitor.src.Core;
namespace LiteMonitor
{
    public class Settings
    {
        // ====== 基础设置 ======
        public string Skin { get; set; } = "DarkFlat_Classic";
        public bool TopMost { get; set; } = true;
        public bool AutoStart { get; set; } = false;
        public int RefreshMs { get; set; } = 1000;
        public double AnimationSpeed { get; set; } = 0.35;
        public Point Position { get; set; } = new Point(-1, -1);

        // ====== 界面与行为 ======
        public bool HorizontalMode { get; set; } = false;
        public double Opacity { get; set; } = 0.85;
        public string Language { get; set; } = "";
        public bool ClickThrough { get; set; } = false;
        public bool AutoHide { get; set; } = true;
        public bool ClampToScreen { get; set; } = true;
        public int PanelWidth { get; set; } = 240;
        public double UIScale { get; set; } = 1.0;

        // ====== 硬件相关 ======
        public string PreferredNetwork { get; set; } = "";
        public string LastAutoNetwork { get; set; } = "";
        public string PreferredDisk { get; set; } = "";
        public string LastAutoDisk { get; set; } = "";
        
        // 主窗体所在的屏幕设备名 (用于记忆上次位置)
        public string ScreenDevice { get; set; } = "";

        // ====== 任务栏 ======
        public bool ShowTaskbar { get; set; } = false;
        // ★★★ 新增：横条模式是否跟随任务栏布局？ ★★★
        public bool HorizontalFollowsTaskbar { get; set; } = false;
        public bool HideMainForm { get; set; } = false;
        public bool HideTrayIcon { get; set; } = false;
        public bool TaskbarAlignLeft { get; set; } = true;
        public string TaskbarFontFamily { get; set; } = "Microsoft YaHei UI";
        public float TaskbarFontSize { get; set; } = 10f;
        public bool TaskbarFontBold { get; set; } = true;
        
        // ★★★ 新增：指定任务栏显示的屏幕设备名 ("" = 自动/主屏) ★★★
        public string TaskbarMonitorDevice { get; set; } = "";

        // 任务栏行为配置
        public bool TaskbarClickThrough { get; set; } = false;     // 鼠标穿透
        public bool TaskbarSingleLine { get; set; } = false;// 单行显示
        public int TaskbarManualOffset { get; set; } = 0;// 手动偏移量 (像素)

        // ====== 任务栏：高级自定义外观 ======
        public bool TaskbarCustomStyle { get; set; } = false; // 总开关
        public string TaskbarColorLabel { get; set; } = "#141414"; // 标签颜色
        public string TaskbarColorSafe { get; set; } = "#008040";  // 正常 (淡绿)
        public string TaskbarColorWarn { get; set; } = "#B57500";  // 警告 (金黄)
        public string TaskbarColorCrit { get; set; } = "#C03030";  // 严重 (橙红)
        public string TaskbarColorBg { get; set; } = "#D2D2D2";    // 防杂边背景色 (透明键)

        // 双击动作配置
        public int MainFormDoubleClickAction { get; set; } = 0;
        public int TaskbarDoubleClickAction { get; set; } = 0;

        // 内存/显存显示模式
        public int MemoryDisplayMode { get; set; } = 0;

        // ★ 2. 运行时缓存：存储探测到的总容量 (GB)
        [JsonIgnore] public static float DetectedRamTotalGB { get; set; } = 0;
        [JsonIgnore] public static float DetectedGpuVramTotalGB { get; set; } = 0;

        public bool UseSystemCpuLoad { get; set; } = false; 
        
        // ====== 记录与报警 ======
        public float RecordedMaxCpuPower { get; set; } = 65.0f;
        public float RecordedMaxCpuClock { get; set; } = 4200.0f;
        public float RecordedMaxGpuPower { get; set; } = 100.0f;
        public float RecordedMaxGpuClock { get; set; } = 1800.0f;
        public bool MaxLimitTipShown { get; set; } = false;
        
        public bool AlertTempEnabled { get; set; } = true;
        public int AlertTempThreshold { get; set; } = 80;
        
        public ThresholdsSet Thresholds { get; set; } = new ThresholdsSet();

        [JsonIgnore] public DateTime LastAlertTime { get; set; } = DateTime.MinValue;
        [JsonIgnore] public long SessionUploadBytes { get; set; } = 0;
        [JsonIgnore] public long SessionDownloadBytes { get; set; } = 0;
        [JsonIgnore] private DateTime _lastAutoSave = DateTime.MinValue;

        public Dictionary<string, string> GroupAliases { get; set; } = new Dictionary<string, string>();
        public List<MonitorItemConfig> MonitorItems { get; set; } = new List<MonitorItemConfig>();

        public bool IsAnyEnabled(string keyPrefix)
        {
            return MonitorItems.Any(x => x.Key.StartsWith(keyPrefix, StringComparison.OrdinalIgnoreCase) && (x.VisibleInPanel || x.VisibleInTaskbar));
        }

        public void SyncToLanguage()
        {
            LanguageManager.ClearOverrides();
            if (GroupAliases != null)
            {
                foreach (var kv in GroupAliases)
                    LanguageManager.SetOverride("Groups." + kv.Key, kv.Value);
            }
            if (MonitorItems != null)
            {
                foreach (var item in MonitorItems)
                {
                    if (!string.IsNullOrEmpty(item.UserLabel))
                        LanguageManager.SetOverride("Items." + item.Key, item.UserLabel);
                    if (!string.IsNullOrEmpty(item.TaskbarLabel))
                        LanguageManager.SetOverride("Short." + item.Key, item.TaskbarLabel);
                }
            }
        }

        public void UpdateMaxRecord(string key, float val)
        {
            bool changed = false;
            if (val <= 0 || float.IsNaN(val) || float.IsInfinity(val)) return;
            
            if (key.Contains("Clock") && val > 10000) return; 
            if (key.Contains("Power") && val > 1000) return;

            if (key == "CPU.Power" && val > RecordedMaxCpuPower) { RecordedMaxCpuPower = val; changed = true; }
            else if (key == "CPU.Clock" && val > RecordedMaxCpuClock) { RecordedMaxCpuClock = val; changed = true; }
            else if (key == "GPU.Power" && val > RecordedMaxGpuPower) { RecordedMaxGpuPower = val; changed = true; }
            else if (key == "GPU.Clock" && val > RecordedMaxGpuClock) { RecordedMaxGpuClock = val; changed = true; }

            if (changed && (DateTime.Now - _lastAutoSave).TotalSeconds > 30)
            {
                Save();
                _lastAutoSave = DateTime.Now;
            }
        }

        private static string FilePath => Path.Combine(AppContext.BaseDirectory, "settings.json");

        public static Settings Load()
        {
            Settings s = new Settings();
            try
            {
                if (File.Exists(FilePath))
                {
                    var json = File.ReadAllText(FilePath);
                    s = JsonSerializer.Deserialize<Settings>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    }) ?? new Settings();
                }
            }
            catch { }
            if (s.GroupAliases == null) s.GroupAliases = new Dictionary<string, string>();
            if (s.MonitorItems == null || s.MonitorItems.Count == 0) s.InitDefaultItems();
            // ★★★ 新增：旧版本数据兼容 ★★★
            // 如果所有项的 TaskbarSortIndex 都是 0（说明是旧配置），则初始化为 SortIndex
            if (s.MonitorItems.All(x => x.TaskbarSortIndex == 0))
            {
                foreach (var item in s.MonitorItems) item.TaskbarSortIndex = item.SortIndex;
            }
            s.SyncToLanguage();
            return s;
        }

        public void Save()
        {
            try
            {
                var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(FilePath, json);
            }
            catch { }
        }

        private void InitDefaultItems()
        {
            MonitorItems = new List<MonitorItemConfig>
            {
                new MonitorItemConfig { Key = "CPU.Load",  SortIndex = 0, VisibleInPanel = true, VisibleInTaskbar = true },
                new MonitorItemConfig { Key = "CPU.Temp",  SortIndex = 1, VisibleInPanel = true, VisibleInTaskbar = true },
                new MonitorItemConfig { Key = "CPU.Clock", SortIndex = 2, VisibleInPanel = false },
                new MonitorItemConfig { Key = "CPU.Power", SortIndex = 3, VisibleInPanel = false },
                new MonitorItemConfig { Key = "GPU.Load",  SortIndex = 10, VisibleInPanel = true, VisibleInTaskbar = true },
                new MonitorItemConfig { Key = "GPU.Temp",  SortIndex = 11, VisibleInPanel = true },
                new MonitorItemConfig { Key = "GPU.Clock", SortIndex = 12, VisibleInPanel = false },
                new MonitorItemConfig { Key = "GPU.Power", SortIndex = 13, VisibleInPanel = false },
                new MonitorItemConfig { Key = "GPU.VRAM",  SortIndex = 14, VisibleInPanel = true },
                new MonitorItemConfig { Key = "MEM.Load",  SortIndex = 20, VisibleInPanel = true, VisibleInTaskbar = true },
                new MonitorItemConfig { Key = "DISK.Read", SortIndex = 30, VisibleInPanel = true },
                new MonitorItemConfig { Key = "DISK.Write",SortIndex = 31, VisibleInPanel = true },
                new MonitorItemConfig { Key = "NET.Up",    SortIndex = 40, VisibleInPanel = true, VisibleInTaskbar = true },
                new MonitorItemConfig { Key = "NET.Down",  SortIndex = 41, VisibleInPanel = true, VisibleInTaskbar = true },
                new MonitorItemConfig { Key = "DATA.DayUp",  SortIndex = 50, VisibleInPanel = true },
                new MonitorItemConfig { Key = "DATA.DayDown",SortIndex = 51, VisibleInPanel = true }
            };
        }
    }

    public class MonitorItemConfig
    {
        public string Key { get; set; } = "";
        public string UserLabel { get; set; } = ""; 
        public string TaskbarLabel { get; set; } = "";
        public bool VisibleInPanel { get; set; } = true;
        public bool VisibleInTaskbar { get; set; } = false;
        public int SortIndex { get; set; } = 0;
        // ★★★ 新增：任务栏独立排序索引 ★★★
        public int TaskbarSortIndex { get; set; } = 0;
    }

    public class ThresholdsSet
    {
        public ValueRange Load { get; set; } = new ValueRange { Warn = 60, Crit = 85 };
        public ValueRange Temp { get; set; } = new ValueRange { Warn = 50, Crit = 70 };
        public ValueRange DiskIOMB { get; set; } = new ValueRange { Warn = 2, Crit = 8 };
        public ValueRange NetUpMB { get; set; } = new ValueRange { Warn = 1, Crit = 2 };
        public ValueRange NetDownMB { get; set; } = new ValueRange { Warn = 2, Crit = 8 };
        public ValueRange DataUpMB { get; set; } = new ValueRange { Warn = 512, Crit = 1024 };
        public ValueRange DataDownMB { get; set; } = new ValueRange { Warn = 2048, Crit = 5096 };
    }

    public class ValueRange
    {
        public double Warn { get; set; } = 0;
        public double Crit { get; set; } = 0;
    }
}