using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Runtime.InteropServices; // [Added] For P/Invoke
using LiteMonitor.src.SystemServices;
using LiteMonitor.src.Core;

namespace LiteMonitor.src.SystemServices.InfoService
{
    /// <summary>
    /// 系统信息服务 (单例)
    /// 负责管理 HOST, IP, Time 等看板数据的获取与缓存
    /// 原 DashboardService
    /// </summary>
    public class InfoService
    {
        #region Singleton
        private static InfoService _instance;
        public static InfoService Instance => _instance ??= new InfoService();
        private InfoService() { Initialize(); }
        #endregion

        // === Constants ===
        private const string KEY_HOST = "HOST";
        private const string KEY_IP   = "IP";
        private const string KEY_TIME = "Time";
        private const string KEY_UPTIME = "Uptime";

        // Default Values (User Friendly)
        private const string DEFAULT_IP   = "0.0.0.0";


        // Update Intervals (For IP/Host only, NOT for Uptime/Time)
        private const int INTERVAL_SLOW = 60000; // 1 min (Stable state: IP found)
        private const int INTERVAL_FAST = 2000;  // 2 sec (Retry state: IP missing)

        // === State ===
        private readonly Dictionary<string, string> _data = new();
        private readonly object _lock = new();
        
        private long _lastUpdateTick = 0;
        private int _currentInterval = INTERVAL_FAST;

        // [Optimization] Cache time strings to avoid allocs every tick
        private string _lastTimeStr = "";
        private int _lastSecond = -1;
        private string _lastUptimeStr = "";
        private int _lastUptimeMinute = -1;

        // [Fix] 使用 QueryUnbiasedInterruptTime 排除休眠时间，解决"开机一天"的问题
        [DllImport("kernel32.dll")]
        private static extern bool QueryUnbiasedInterruptTime(out ulong UnbiasedTime);

        /// <summary>
        /// 初始化默认值并启动首次更新
        /// </summary>
        private void Initialize()
        {
            lock (_lock)
            {
                _data[KEY_HOST] = Environment.MachineName; // Hostname always available
                _data[KEY_IP]   = DEFAULT_IP;
                _data[KEY_TIME] = DateTime.Now.ToString("ddd HH:mm:ss"); // ★★★ 立即赋值当前时间，不再使用 00:00:00 默认值 ★★★
            }
            
            // [Fix] Calculate Uptime immediately so it's ready for first render
            UpdateTimeInfo();

            // Trigger first async update
            UpdateData();
        }

        /// <summary>
        /// 线程安全地获取数据
        /// </summary>
        public string GetValue(string key)
        {
            lock (_lock)
            {
                return _data.TryGetValue(key, out var val) ? val : "";
            }
        }

        /// <summary>
        /// 外部注入通用数据 (例如插件抓取的数据)
        /// </summary>
        public void InjectValue(string key, string value)
        {
            if (string.IsNullOrEmpty(key)) return;
            // [Optimization] Intern keys and values from external injections
            SetData(UIUtils.Intern(key), value);
        }

        /// <summary>
        /// 外部注入 IP (例如从配置缓存恢复)
        /// </summary>
        public void InjectIP(string ip)
        {
            if (string.IsNullOrEmpty(ip) || ip == DEFAULT_IP) return;

            SetData(KEY_IP, ip);
            // If valid IP injected, switch to slow update immediately
            _currentInterval = INTERVAL_SLOW;
        }

        public void RemoveDataByPrefix(string prefix)
        {
            if (string.IsNullOrEmpty(prefix)) return;
            lock (_lock)
            {
                var keysToRemove = _data.Keys.Where(k => k.StartsWith(prefix)).ToList();
                foreach (var k in keysToRemove)
                {
                    _data.Remove(k);
                }
            }
        }

        /// <summary>
        /// 主循环调用 (建议每帧或定时器调用)
        /// </summary>
        public void Update()
        {
            // 1. High Frequency: Time (Every tick)
            UpdateTimeInfo();

            // 2. Low Frequency: Network/Host (Based on interval)
            long now = Environment.TickCount64;
            if (now - _lastUpdateTick > _currentInterval)
            {
                _lastUpdateTick = now;
                UpdateData();
            }
        }

        private void UpdateTimeInfo()
        {
            var now = DateTime.Now;
            
            bool isNewSecond = now.Second != _lastSecond;

            // [Optimization] Only format time if second changed
            if (isNewSecond)
            {
                _lastSecond = now.Second;
                _lastTimeStr = now.ToString("ddd HH:mm:ss");
                SetData(KEY_TIME, _lastTimeStr);
            }

            // Uptime
            // [Optimization] Update every minute, hide seconds
            // [Fix] 改用 QueryUnbiasedInterruptTime (不含休眠) 替代 Environment.TickCount64 (含休眠)
            TimeSpan ts;
            try 
            {
                if (QueryUnbiasedInterruptTime(out ulong ticks))
                {
                    ts = TimeSpan.FromTicks((long)ticks);
                }
                else
                {
                    // Fallback (虽然理论上不会失败)
                    ts = TimeSpan.FromMilliseconds(Environment.TickCount64);
                }
            }
            catch 
            {
                 ts = TimeSpan.FromMilliseconds(Environment.TickCount64);
            }
            
            if (now.Minute != _lastUptimeMinute || string.IsNullOrEmpty(_lastUptimeStr))
            {
                _lastUptimeMinute = now.Minute;

                if (ts.TotalDays < 1)
                {
                    _lastUptimeStr = LanguageManager.CurrentLang == "zh"
                        ? $"{ts.Hours}时 {ts.Minutes}分"
                        : $"{ts.Hours}h {ts.Minutes}m";
                }
                else
                {
                    _lastUptimeStr = LanguageManager.CurrentLang == "zh"
                        ? $"{(int)ts.TotalDays}天 {ts.Hours}时 {ts.Minutes}分"
                        : $"{(int)ts.TotalDays}d {ts.Hours}h {ts.Minutes}m";
                }
                
                SetData(KEY_UPTIME, _lastUptimeStr);
            }
        }

        private void UpdateData()
        {
            // [Optimization] Host info is static, only set once or if changed
            string currentHost = Environment.MachineName;
            if (GetValue(KEY_HOST) != currentHost)
            {
                SetData(KEY_HOST, currentHost);
            }

            // IP info is potentially slow, run async
            Task.Run(UpdateIPInfo);
        }

        private void UpdateIPInfo()
        {
            try
            {
                // Delegate to HardwareMonitor's NetworkManager (handles caching & multi-interface logic)
                string ip = HardwareMonitor.Instance?.GetNetworkIP();

                // Validate IP
                if (!string.IsNullOrEmpty(ip) && ip != DEFAULT_IP)
                {
                    // [Optimization] Only update if changed
                    if (GetValue(KEY_IP) != ip)
                    {
                        SetData(KEY_IP, ip);
                    }
                    _currentInterval = INTERVAL_SLOW; // Success -> Relax
                }
                else
                {
                    // Failed -> Retry fast
                    _currentInterval = INTERVAL_FAST;
                }
            }
            catch
            {
                _currentInterval = INTERVAL_FAST;
            }
        }

        private void SetData(string key, string value)
        {
            lock (_lock)
            {
                // [Optimization] Intern values to reduce duplicates (e.g. "KB/s", "luffy-pc")
                // [Fix] Do NOT intern dynamic time strings (Time, Uptime) as they change constantly and pollute the pool
                if (key == KEY_TIME || key == KEY_UPTIME)
                {
                    _data[key] = value;
                }
                else
                {
                    _data[key] = UIUtils.Intern(value);
                }
            }
        }
    }
}