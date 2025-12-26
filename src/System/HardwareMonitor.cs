using System.Diagnostics; // 必须添加这个
using LibreHardwareMonitor.Hardware;
using System.Net.NetworkInformation;
using LiteMonitor.src.Core; // 必须引用
using Debug = System.Diagnostics.Debug;

namespace LiteMonitor.src.SystemServices
{
    // 使用 partial 关键字，表示这是类的一部分
    public sealed partial class HardwareMonitor : IDisposable
    {
        public static HardwareMonitor? Instance { get; private set; }
        public event Action? OnValuesUpdated;

        // =======================================================================
        // [字段] 核心资源与锁
        // =======================================================================
        private readonly Settings _cfg;
        private readonly Computer _computer;
        private readonly object _lock = new object();
        
        // 传感器映射字典
        private readonly Dictionary<string, ISensor> _map = new();
        private readonly Dictionary<string, float> _lastValid = new();
        private DateTime _lastMapBuild = DateTime.MinValue;

         // 定义网卡扫描时间标记
        private DateTime _startTime = DateTime.Now;      // 启动时间
        
        private DateTime _lastSlowScan = DateTime.Now;   //  初始值为 Now，强迫程序启动时先等 3 秒再进行慢速全盘扫描，防止卡顿
        
       // --- 流量统计专用字段 ---
        private DateTime _lastTrafficTime = DateTime.Now; // 积分时间戳
        private DateTime _lastTrafficSave = DateTime.Now; // 自动保存时间戳

        // ★★★ [新增] 系统 CPU 计数器 ★★★
        private PerformanceCounter? _cpuPerfCounter;
        private float _lastSystemCpuLoad = 0f;

        // ★★★ [修复] 状态隔离：每个硬件拥有独立的网络状态，不再全局共享 ★★★
        private class NetworkState
        {
            public NetworkInterface? NativeAdapter;
            public long LastNativeUpload;
            public long LastNativeDownload;
            public DateTime LastMatchAttempt = DateTime.MinValue;
            
            // 缓存 LHM 传感器
            public ISensor? CachedUpSensor;
            public ISensor? CachedDownSensor;
        }
        private readonly Dictionary<IHardware, NetworkState> _netStates = new();

        // =======================================================================
        // [缓存] 高性能读取缓存 (避免 LINQ)
        // =======================================================================
        // CPU 核心传感器对 (用于加权平均计算)
        private class CpuCoreSensors
        {
            public ISensor? Clock;
            public ISensor? Load;
        }
        private List<CpuCoreSensors> _cpuCoreCache = new();
        
        // 显卡硬件缓存 (用于快速定位)
        private IHardware? _cachedGpu;
        // ★★★ [新增] 缓存总线传感器 (用于 Zen 5 频率修正) ★★★
        private ISensor? _cpuBusSpeedSensor;

        // 网络/磁盘 智能缓存
        private IHardware? _cachedNetHw;
        private DateTime _lastNetScan = DateTime.MinValue;
        private IHardware? _cachedDiskHw;
        private DateTime _lastDiskScan = DateTime.MinValue;

        // =======================================================================
        // [构造与析构]
        // =======================================================================
        public HardwareMonitor(Settings cfg)
        {
            _cfg = cfg;
            Instance = this;

            _computer = new Computer()
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true,
                IsMemoryEnabled = true,
                IsNetworkEnabled = true,
                IsStorageEnabled = true,
                IsMotherboardEnabled = false,
                IsControllerEnabled = false
            };

            // 异步初始化，防止卡顿 UI
            Task.Run(() =>
            {
                try
                {
                    _computer.Open();
                    BuildSensorMap();
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[HardwareMonitor] Init failed: " + ex.Message);
                }
            });
        }

        public void Dispose() 
        {
            _computer.Close();
            _cpuPerfCounter?.Dispose(); // ★ 新增
            // ★★★ 新增：清理网络状态缓存 ★★★
            _netStates.Clear(); 
            _map.Clear();
        }

        // =======================================================================
        // [生命周期] 定时更新 (终极优化版)
        // =======================================================================
        
        public void UpdateAll()
        {
            try
            {
                DateTime now = DateTime.Now;
                double timeDelta = (now - _lastTrafficTime).TotalSeconds;
                _lastTrafficTime = now;
                if (timeDelta > 5.0) timeDelta = 0;

                // ★★★ 核心改动：使用 List 判断是否需要更新 ★★★
                // 只要列表中有任意一个 CPU 相关的项开启 (无论是面板还是任务栏)，就需要更新 CPU
                bool needCpu = _cfg.IsAnyEnabled("CPU");
                bool needGpu = _cfg.IsAnyEnabled("GPU");
                bool needMem = _cfg.IsAnyEnabled("MEM");
                bool needNet = _cfg.IsAnyEnabled("NET") || _cfg.IsAnyEnabled("DATA");
                bool needDisk = _cfg.IsAnyEnabled("DISK");

                bool isStartupPhase = (DateTime.Now - _startTime).TotalSeconds < 3;
                bool isSlowScanTick = (DateTime.Now - _lastSlowScan).TotalSeconds > 3;

                foreach (var hw in _computer.Hardware)
                {
                    // CPU / GPU / Memory
                    if (hw.HardwareType == HardwareType.Cpu) { if (needCpu) hw.Update(); continue; }
                    if (hw.HardwareType == HardwareType.GpuNvidia || hw.HardwareType == HardwareType.GpuAmd || hw.HardwareType == HardwareType.GpuIntel) { if (needGpu) hw.Update(); continue; }
                    if (hw.HardwareType == HardwareType.Memory) { if (needMem) hw.Update(); continue; }

                    // Network
                    if (hw.HardwareType == HardwareType.Network)
                    {
                        if (needNet) 
                        {
                            bool isTarget = (_cachedNetHw != null && hw == _cachedNetHw) || 
                                            (hw.Name == _cfg.LastAutoNetwork) ||
                                            (hw.Name == _cfg.PreferredNetwork);

                            if (isTarget)
                            {
                                hw.Update(); 
                                AccumulateTraffic(hw, timeDelta);
                            }
                            else if (isStartupPhase || IsVirtualNetwork(hw.Name))
                            {
                                continue;    
                            }
                            else if (isSlowScanTick)
                            {
                                hw.Update(); 
                            }
                        }
                        continue;
                    }

                    // Storage
                    if (hw.HardwareType == HardwareType.Storage)
                    {
                        if (needDisk) 
                        {
                            bool isTarget = (_cachedDiskHw != null && hw == _cachedDiskHw) || 
                                            (hw.Name == _cfg.LastAutoDisk) || 
                                            (hw.Name == _cfg.PreferredDisk);

                            if (isTarget)
                            {
                                hw.Update(); 
                            }
                            else if (isStartupPhase) 
                            {
                                continue;
                            }
                            else if (isSlowScanTick) 
                            {
                                hw.Update();
                            }
                        }
                        continue;
                    }
                }
                
                if (isSlowScanTick) _lastSlowScan = DateTime.Now;

                // ★★★ [新增] 更新系统 CPU 计数器 ★★★
                if (_cfg.UseSystemCpuLoad)
                {
                    if (_cpuPerfCounter == null)
                    {
                        try 
                        {
                            // 初始化计数器："Processor" 是类别，"% Processor Time" 是计数器名，"_Total" 是实例名
                            _cpuPerfCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                            _cpuPerfCounter.NextValue(); // 第一次调用通常返回 0，用于建立基准
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine("Init CPU Counter failed: " + ex.Message);
                        }
                    }

                    if (_cpuPerfCounter != null)
                    {
                        // NextValue 获取的是“上一次调用到现在的平均值”，非常适合 1秒1次的 UpdateAll
                        _lastSystemCpuLoad = _cpuPerfCounter.NextValue();
                    }
                }
                else
                {
                    // 如果用户关闭了该功能，释放计数器以节省资源
                    if (_cpuPerfCounter != null)
                    {
                        _cpuPerfCounter.Dispose();
                        _cpuPerfCounter = null;
                    }
                }

                // 流量自动保存
                if ((DateTime.Now - _lastTrafficSave).TotalSeconds > 60)
                {
                    TrafficLogger.Save();
                    _lastTrafficSave = DateTime.Now;
                }

                OnValuesUpdated?.Invoke();
            }
            catch { }
        }

       // [修复版] 智能匹配 (支持 "以太网" 这种连接名)
       // ★★★ [修复] 增加 state 参数，只操作当前硬件的状态
        private void MatchNativeNetworkAdapter(string lhmName, NetworkState state)
        {
            if (state.NativeAdapter != null) return;
            // 限制匹配频率，避免频繁调用 (10秒内仅匹配一次)
            if ((DateTime.Now - state.LastMatchAttempt).TotalSeconds < 10) return;
            state.LastMatchAttempt = DateTime.Now;

            try
            {
                var nics = NetworkInterface.GetAllNetworkInterfaces();
                var lhmTokens = SplitTokens(lhmName); // 预分词

                foreach (var nic in nics)
                {
                    // -------------------------------------------------------
                    // 1. 匹配连接名称 (Connection Name) -> 解决 "以太网/WLAN" 问题
                    // -------------------------------------------------------
                    // 如果 LHM 返回的是 "以太网"，而 nic.Name 也是 "以太网"，直接命中！
                    if (nic.Name.Equals(lhmName, StringComparison.OrdinalIgnoreCase))
                    {
                        SetNativeAdapter(nic, state);
                        Debug.WriteLine($"[匹配成功] 策略:连接名 | LHM:{lhmName} == Native:{nic.Name}");
                        return;
                    }

                    // -------------------------------------------------------
                    // 2. 匹配硬件描述 (Interface Description) -> 解决 "Realtek..." 问题
                    // -------------------------------------------------------
                    if (nic.Description.Equals(lhmName, StringComparison.OrdinalIgnoreCase))
                    {
                        SetNativeAdapter(nic, state);
                        Debug.WriteLine($"[匹配成功] 策略:硬件名 | LHM:{lhmName} == Native:{nic.Description}");
                        return;
                    }

                    // -------------------------------------------------------
                    // 3. 模糊分词匹配 (Fuzzy) -> 解决 "Intel(R) #2" 这种微小差异
                    // -------------------------------------------------------
                    // 只有当名字里包含英文单词时才进行分词匹配，防止 "以太网" 这种短词误判
                    if (lhmTokens.Count > 0 && lhmName.Length > 5) 
                    {
                        var nicTokens = SplitTokens(nic.Description);
                        int matchCount = lhmTokens.Intersect(nicTokens, StringComparer.OrdinalIgnoreCase).Count();
                        
                        if (matchCount > 2 && (double)matchCount / lhmTokens.Count > 0.6)
                        {
                            SetNativeAdapter(nic, state);
                            Debug.WriteLine($"[匹配成功] 策略:模糊 | LHM:{lhmName} ≈ Native:{nic.Description}");
                            return;
                        }
                    }
                }
            }
            catch { state.NativeAdapter = null; }
        }

        // 分词辅助
        private List<string> SplitTokens(string input)
        {
            return input.Split(new[] { ' ', '(', ')', '[', ']', '-', '_', '#' }, StringSplitOptions.RemoveEmptyEntries).ToList();
        }

        // ★★★ [修复] 增加 state 参数
        private void SetNativeAdapter(NetworkInterface nic, NetworkState state)
        {
            state.NativeAdapter = nic;
            // 初始化基准值，防止首次匹配时产生巨大增量
            try
            {
                var stats = nic.GetIPStatistics();
                state.LastNativeUpload = stats.BytesSent;
                state.LastNativeDownload = stats.BytesReceived;
            }
            catch
            {
                state.NativeAdapter = null;
            }
        }

        // [终极版] 智能流量累加 (原生精准 + LHM保底)
        // ★★★ [修复] 重写逻辑，使用 NetworkState
        private void AccumulateTraffic(IHardware hw, double seconds)
        {
            // 1. 获取或创建当前硬件的独立状态
            if (!_netStates.TryGetValue(hw, out var state))
            {
                state = new NetworkState();
                _netStates[hw] = state;
            }

            long finalUp = 0;
            long finalDown = 0;

            // -----------------------------------------------------
            // A. 先算一个 LHM 的估算值 (作为保底/校验)
            // -----------------------------------------------------
            // 如果还没缓存传感器，先找一下
            if (state.CachedUpSensor == null || state.CachedDownSensor == null)
            {
                foreach (var s in hw.Sensors)
                {
                    if (s.SensorType != SensorType.Throughput) continue;
                    if (_upKW.Any(k => Has(s.Name, k))) state.CachedUpSensor ??= s;
                    if (_downKW.Any(k => Has(s.Name, k))) state.CachedDownSensor ??= s;
                }
            }

            // ★★★ 直接使用缓存对象，跳过循环和字符串匹配 ★★★
            long lhmUpDelta = (long)((state.CachedUpSensor?.Value ?? 0) * seconds);
            long lhmDownDelta = (long)((state.CachedDownSensor?.Value ?? 0) * seconds);

            // -----------------------------------------------------
            // B. 尝试获取 原生精准值
            // -----------------------------------------------------
            MatchNativeNetworkAdapter(hw.Name, state);
            
            bool nativeValid = false;
            long nativeUpDelta = 0;
            long nativeDownDelta = 0;

            if (state.NativeAdapter != null)
            {
                try
                {
                    // 使用 GetIPStatistics 以支持 IPv6
                    var stats = state.NativeAdapter.GetIPStatistics();
                    long currUp = stats.BytesSent;
                    long currDown = stats.BytesReceived;

                    // 计算增量 (处理溢出或重置)
                    if (currUp >= state.LastNativeUpload) nativeUpDelta = currUp - state.LastNativeUpload;
                    if (currDown >= state.LastNativeDownload) nativeDownDelta = currDown - state.LastNativeDownload;

                    state.LastNativeUpload = currUp;
                    state.LastNativeDownload = currDown;
                    nativeValid = true;
                }
                catch 
                {
                    state.NativeAdapter = null; // 读失败了，扔掉
                }
            }

            // -----------------------------------------------------
            // C. 决策时刻：到底信谁？
            // -----------------------------------------------------
            if (nativeValid)
            {
                // 防呆检查：
                // 如果原生读数是 0 (没流量)，但 LHM 显示速度很快 (> 50KB/s)
                // 说明我们匹配错网卡了！(匹配到了一个同名的闲置网卡)
                if ((nativeUpDelta + nativeDownDelta == 0) && (lhmUpDelta + lhmDownDelta > 51200))
                {
                    // 放弃原生，使用 LHM 保底
                    finalUp = lhmUpDelta;
                    finalDown = lhmDownDelta;
                    
                    // 既然匹配错了，就清空，下次重新找
                    state.NativeAdapter = null; 
                }
                else
                {
                    // 正常情况，原生优先 (精准)
                    finalUp = nativeUpDelta;
                    finalDown = nativeDownDelta;
                }
            }
            else
            {
                // 没有原生对象，只能用 LHM
                finalUp = lhmUpDelta;
                finalDown = lhmDownDelta;
            }

            // -----------------------------------------------------
            // D. 存入数据
            // -----------------------------------------------------
            // ★★★ [新增] 安全阀：单次增量超过 10GB (10737418240 字节) 视为异常丢弃 ★★★
            // 防止基准值错位导致统计爆炸
            if (finalUp > 10737418240L || finalDown > 10737418240L) return;

            if (finalUp > 0 || finalDown > 0)
            {
                _cfg.SessionUploadBytes += finalUp;
                _cfg.SessionDownloadBytes += finalDown;
                TrafficLogger.AddTraffic(finalUp, finalDown);
            }
        }

        // [新增] 辅助方法：复用 Logic.cs 中的关键字判断是否为虚拟网卡
        private bool IsVirtualNetwork(string name)
        {
            // _virtualNicKW 定义在 Logic.cs 中，因为是 partial class 所以可以直接访问
            foreach (var k in _virtualNicKW)
            {
                if (name.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0) 
                    return true;
            }
            return false;
        }

        // =======================================================================
        // [核心] 构建传感器映射与缓存 (最复杂的构建逻辑)
        // =======================================================================
        private void BuildSensorMap()
        {
            // 1. 准备临时容器 (线程安全)
            var newMap = new Dictionary<string, ISensor>();
            var newCpuCache = new List<CpuCoreSensors>();
            IHardware? newGpu = null;
            ISensor? newBusSensor = null; // 临时变量

            // 局部递归函数
            void RegisterTo(IHardware hw)
            {
                hw.Update();

                // --- 填充 CPU 缓存 (用于加权平均) ---
                if (hw.HardwareType == HardwareType.Cpu)
                {
                    // ★★★ [新增] 查找并缓存 Bus Speed 传感器 ★★★
                    // 优先查找名为 "Bus Speed" 的时钟传感器
                    if (newBusSensor == null)
                    {
                        newBusSensor = hw.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Clock && s.Name.Contains("Bus Speed"));
                    }

                    // 查找所有核心频率 (排除总线频率)
                    var clocks = hw.Sensors.Where(s => s.SensorType == SensorType.Clock && Has(s.Name, "core") && !Has(s.Name, "bus"));
                    
                    
                    foreach (var clock in clocks)
                    {
                        // ★★★ 修复：AMD 负载叫 "CPU Core #1"，频率叫 "Core #1"，不相等。
                        // 改用 EndsWith 匹配，既支持 Intel (名字一样) 也支持 AMD (带前缀)
                        var load = hw.Sensors.FirstOrDefault(s => 
                            s.SensorType == SensorType.Load && 
                            s.Name.EndsWith(clock.Name, StringComparison.OrdinalIgnoreCase)); // <--- 修改了这里
                            
                        newCpuCache.Add(new CpuCoreSensors { Clock = clock, Load = load });
                    }
                }

                // --- 填充 GPU 缓存 (优化版：智能选择独显) ---
                if (hw.HardwareType == HardwareType.GpuNvidia || 
                    hw.HardwareType == HardwareType.GpuAmd || 
                    hw.HardwareType == HardwareType.GpuIntel)
                {
                    // 如果还没找到显卡，直接用当前这个
                    if (newGpu == null)
                    {
                        newGpu = hw;
                    }
                    else
                    {
                        // 如果已经找到了一个显卡，但它是“弱鸡”核显，而当前这个是“强力”独显，则替换！
                        // 判断逻辑：
                        // 1. 旧的是 generic (如 "AMD Radeon(TM) Graphics"), 新的是具体型号 (如 "Intel Arc B580")
                        // 2. 旧的是 Intel 核显，新的是 Nvidia/AMD 独显
                        // 3. 特别针对 B580: 如果新卡名字包含 "Arc"，绝对优先
                        
                        bool oldIsGeneric = IsGenericGpuName(newGpu.Name);
                        bool newIsSpecific = !IsGenericGpuName(hw.Name);
                        bool newIsArc = hw.Name.Contains("Arc", StringComparison.OrdinalIgnoreCase);
                        bool oldIsArc = newGpu.Name.Contains("Arc", StringComparison.OrdinalIgnoreCase);

                        // 优先选 Arc，其次选非通用名称的卡
                        if ((!oldIsArc && newIsArc) || (oldIsGeneric && newIsSpecific))
                        {
                            newGpu = hw;
                        }
                    }
                }

                // --- 普通传感器映射 ---
                foreach (var s in hw.Sensors)
                {
                    string? key = NormalizeKey(hw, s); // 调用 Logic 文件中的方法
                    if (!string.IsNullOrEmpty(key) && !newMap.ContainsKey(key))
                        newMap[key] = s;
                }

                foreach (var sub in hw.SubHardware) RegisterTo(sub);
            }

            // 按优先级排序并注册
            var ordered = _computer.Hardware.OrderBy(h => GetHwPriority(h));
            foreach (var hw in ordered) RegisterTo(hw);

            // 2. 原子交换数据 (加锁)
            lock (_lock)
            {
                _map.Clear();
                foreach (var kv in newMap) _map[kv.Key] = kv.Value;
                
                _cpuCoreCache = newCpuCache;
                _cachedGpu = newGpu;
                _cpuBusSpeedSensor = newBusSensor; // ★ 更新 Bus Sensor 缓存
                _lastMapBuild = DateTime.Now;
            }
        }

        // [新增] 辅助方法：判断是否为通用核显名称
        private bool IsGenericGpuName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return true;
            // 常见核显名称: "AMD Radeon(TM) Graphics", "Intel(R) UHD Graphics"
            if (name.Equals("AMD Radeon(TM) Graphics", StringComparison.OrdinalIgnoreCase)) return true;
            if (name.Contains("UHD Graphics", StringComparison.OrdinalIgnoreCase)) return true;
            if (name.Contains("Iris", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
        private static int GetHwPriority(IHardware hw)
        {
            // 如果是 Intel Arc，提到最高优先级
            if (hw.HardwareType == HardwareType.GpuIntel && 
                hw.Name.Contains("Arc", StringComparison.OrdinalIgnoreCase))
                return 0;

            return hw.HardwareType switch
            {
                HardwareType.GpuNvidia => 0,
                HardwareType.GpuAmd => 1,
                HardwareType.GpuIntel => 2,
                _ => 3
            };
        }

        private void EnsureMapFresh()
        {
            if ((DateTime.Now - _lastMapBuild).TotalMinutes > 10)
                BuildSensorMap();
        }
    }
}