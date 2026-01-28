using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Runtime.InteropServices; // ★★★ 新增：引用用于内存修剪的库
using System.Reflection; // ★★★ 新增：用于反射关闭历史记录
using LibreHardwareMonitor.Hardware;
using LiteMonitor.src.Core;
using System.Linq; 

namespace LiteMonitor.src.SystemServices
{
    public sealed class HardwareMonitor : IDisposable
    {
        public static HardwareMonitor? Instance { get; private set; }
        public event Action? OnValuesUpdated;

        private readonly Settings _cfg;
        private readonly Computer _computer;
        private readonly object _lock = new object();

        // 拆分出的子服务
        private readonly SensorMap _sensorMap;
        private readonly NetworkManager _networkManager;
        private readonly DiskManager _diskManager;
        private readonly FpsCounter _fpsCounter; // <--- 新增
        private readonly DriverInstaller _driverInstaller;
        private readonly HardwareValueProvider _valueProvider;

        // ★★★ [新增] 性能计数器管理器 ★★★
        private readonly PerformanceCounterManager _perfCounterManager;

        private readonly Dictionary<string, float> _lastValidMap = new();
        
        // ★★★ 优化：增加 UI 列表缓存，防止重复分配字符串 ★★★
        private List<string>? _cachedFanList = null; 
        private List<string>? _cachedNetworkList = null; // 网卡列表缓存
        private List<string>? _cachedDiskList = null;    // 硬盘列表缓存
        private List<string>? _cachedMoboTempList = null; // 主板温度列表缓存

        private long _tickCounter = 0; // ★★★ 统一计时器计数 (约 1秒/tick) ★★★
        private double _secondAccumulator = 0; // ★★★ [新增] 秒级累加器，解决非1秒刷新率问题 ★★★
        private long _secondsCounter = 0;      // ★★★ [新增] 真实的秒计数器 (不受刷新率影响) ★★★
        private DateTime _lastTrafficTime = DateTime.Now; // 仅用于网速计算 (需要高精度)
        private DateTime _startTime = DateTime.Now;

        public IComputer ComputerInstance => _computer;// [新增] 允许 UI 层访问原始硬件树（用于硬件信息面板）

        // ★★★ 新增：允许主程序手动触发驱动检查 (用于解决启动弹窗冲突) ★★★
        public Task SmartCheckDriver() => _driverInstaller.SmartCheckDriver();

        // ★★★ 新增：声明 Windows API 用于修剪工作集内存 ★★★
        [DllImport("psapi.dll")]
        private static extern int EmptyWorkingSet(IntPtr hwProc);

        public string GetNetworkIP() => _networkManager.GetCurrentIP();

        public HardwareMonitor(Settings cfg)
        {
            _cfg = cfg;
            Instance = this;

            // 1. 初始化 Computer
            _computer = new Computer()
            {
                IsCpuEnabled = cfg.IsAnyEnabled(""),
                IsGpuEnabled = true,
                IsMemoryEnabled = true,
                IsNetworkEnabled = true,
                IsStorageEnabled = true,
                IsMotherboardEnabled = true,
                // ★★★ 优化 T0：关闭 PCI 控制器扫描，省下 2 万个对象 (约 8MB) ★★★
                // 除非你需要极底层的 SuperIO 调试，否则不需要开这个
                IsControllerEnabled = true, 

                // 开启电池监控
                IsBatteryEnabled = true,

                // 顺便确保 PSU 也关闭（通常不需要监控电源模块，除非是高端 Corsair 电源）
                IsPsuEnabled = false
            };

            // ★★★ [新增] 1. 初始化计数器管理器 (必须在 ValueProvider 之前) ★★★
            _perfCounterManager = new PerformanceCounterManager();

            // 2. 初始化服务
            _sensorMap = new SensorMap();
            _networkManager = new NetworkManager(_perfCounterManager);
            _diskManager = new DiskManager();
            _driverInstaller = new DriverInstaller(cfg, _computer, ReloadComputerSafe);
            _fpsCounter = new FpsCounter(_driverInstaller); // <--- 新增

            // ★★★ [修改] 2. 将 Manager 注入给 ValueProvider ★★★
            _valueProvider = new HardwareValueProvider(_computer, cfg, _sensorMap, _networkManager, _diskManager, _fpsCounter, _perfCounterManager, _lock, _lastValidMap);

            // 3. 异步启动 (唯一优化：不卡UI)
            Task.Run(async () =>
            {
                try
                {
                    // ★★★ [新增] 启动计数器预热 (不阻塞主 UI) ★★★
                    _perfCounterManager.InitializeAsync();

                    // 这句耗时 4-5 秒，但在执行过程中，硬件会陆续添加到 _computer.Hardware
                    _computer.Open(); 

                    // ★★★ T0+级修复：彻底禁用历史记录，解决 SensorValue[] 飙升 ★★★
                    // 必须在 Open() 之后调用，此时传感器才被创建
                    DisableSensorHistory();

                    // 只有全部扫描完，才建立高速 Map
                    lock (_lock)
                    {
                        // 1. 先进行一次全量更新 (预热)
                        // 这一步至关重要！它确保了随后 Rebuild 时，SensorMap 能读到传感器的数值，
                        // 从而让"智能主板温度识别"等依赖数值的逻辑正常工作。
                        foreach (var hw in _computer.Hardware)
                        {
                            hw.Update();
                        }

                        // 2. 数据有了，再建立映射
                        // 此时 Rebuild 能读到刚才 Update 产生的数值，智能识别逻辑完美生效。
                        _sensorMap.Rebuild(_computer, cfg); 

                        // ★★★ [新增] 3. 静态化预热：将所有传感器对象存入 Provider 缓存 ★★★
                        _valueProvider.PreCacheAllSensors(_sensorMap);
                    }
                    
                    // ★★★ 优化 T1：启动后大扫除 ★★★
                    // 1. 强制 GC：清理初始化过程中(如JSON解析、驱动检查)产生的临时垃圾
                    GC.Collect(2, GCCollectionMode.Forced, true, true);
                    
                    // 2. 修剪物理内存：告诉系统"我很闲"，把不用的物理内存页交换出去
                    // 这会让任务管理器里的数值瞬间变得很漂亮
                    try { EmptyWorkingSet(System.Diagnostics.Process.GetCurrentProcess().Handle); } catch { }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Init Error: {ex.Message}");
                }
            });
        }

        public float? Get(string key) => _valueProvider.GetValue(key);

        public void UpdateAll()
        {
            try
            {
                // 1. 统一心跳计数 (假设 UpdateAll 约 1秒调用一次)
                _tickCounter++;

                // 2. 计算精确时间差 (仅用于网速计算)
                DateTime now = DateTime.Now;
                double timeDelta = (now - _lastTrafficTime).TotalSeconds;
                _lastTrafficTime = now;
                if (timeDelta > 5.0) timeDelta = 0; // 防止休眠唤醒后的数据突刺

                // ★★★ [智能处理] 刷新率自适应逻辑 ★★★
                // 无论 UpdateAll 是 300ms 调一次还是 2s 调一次，
                // 我们都将时间累加，直到凑满 1秒，才增加 _secondsCounter。
                // 这样后续的 % 60, % 600 逻辑就是基于"真实时间"，而非"调用次数"。
                _secondAccumulator += timeDelta;
                while (_secondAccumulator >= 1.0)
                {
                    _secondAccumulator -= 1.0;
                    _secondsCounter++;
                }

                // === [优化开始] 精细化判断更新需求 ===
        
                // 1. 获取计数器状态
                bool useCounter = _cfg.UseWinPerCounters && _perfCounterManager.IsInitialized;
                
                // ★★★ [优化] 全量更新判断 ★★★
                // 如果开启了 WebServer，则需要强制更新所有硬件，因为网页端可能会查看主界面未开启的项目
                bool forceAll = _cfg.WebServerEnabled;

                // 2. CPU: 总是需要 (因为 LHM 要读温度)
                bool needCpu = forceAll || _cfg.IsAnyEnabled("CPU");

                // 3. 显卡: 总是需要
                bool needGpu = forceAll || _cfg.IsAnyEnabled("GPU");

                // 4. ★★★ [优化 1] 内存: 如果走了计数器，就不需要 LHM 轮询了 ★★★
                bool needMem = forceAll || (_cfg.IsAnyEnabled("MEM") && !useCounter);

                // 5. 网络: 保持不变
                bool needNet = forceAll || _cfg.IsAnyEnabled("NET") || _cfg.IsAnyEnabled("DATA");

                // 6. ★★★ [优化 2] 磁盘: 智能判断 ★★★
                // 只有当 (没开计数器 OR 指定了特定盘 OR 需要看温度) 时，才需要 LHM 介入
                // 如果用户只看总速且开了计数器，这里 needDisk 会变成 false -> 彻底省去 IO
                bool needDiskTemp = _cfg.IsAnyEnabled("DISK.Temp");
                bool hasSpecificDisk = !string.IsNullOrEmpty(_cfg.PreferredDisk);
                bool needDiskSpeed = _cfg.IsAnyEnabled("DISK") && (!useCounter || hasSpecificDisk);
                
                bool needDisk = forceAll || needDiskSpeed || needDiskTemp;
                // 7. 电池: 只有在开启时才更新
                bool needBat = forceAll || _cfg.IsAnyEnabled("BAT");

                // 判断主板更新需求
                bool needMobo = forceAll || 
                _cfg.IsAnyEnabled("MOBO") || 
                _cfg.IsAnyEnabled("CPU.Fan") || 
                _cfg.IsAnyEnabled("CPU.Pump") || 
                _cfg.IsAnyEnabled("CASE.Fan");

                // ★★★ 使用 _secondsCounter (真实秒) 替代 Tick (调用次数) ★★★
                // 3秒一次 (慢速扫描)
                bool isSlowScanTick = (_secondsCounter % 3 == 0); 
                // 10秒一次 (磁盘后台扫描)
                bool needDiskBgScan = (_secondsCounter % 10 == 0);

                lock (_lock)
                {
                    // 你原来的代码已经使用了 foreach，非常高效，不需要修改！
                    foreach (var hw in _computer.Hardware)
                    {
                        if (hw.HardwareType == HardwareType.Cpu && needCpu) { hw.Update(); continue; }
                        if ((hw.HardwareType == HardwareType.GpuNvidia || hw.HardwareType == HardwareType.GpuAmd || hw.HardwareType == HardwareType.GpuIntel) && needGpu) { hw.Update(); continue; }
                        if (hw.HardwareType == HardwareType.Memory && needMem) { hw.Update(); continue; }
                        if (hw.HardwareType == HardwareType.Battery && needBat) { hw.Update(); continue; }

                        if (hw.HardwareType == HardwareType.Network && needNet)
                        {
                            _networkManager.ProcessUpdate(hw, _cfg, timeDelta, isSlowScanTick);
                            continue;
                        }
                        if (hw.HardwareType == HardwareType.Storage && needDisk)
                        {
                            _diskManager.ProcessUpdate(hw, _cfg, isSlowScanTick, needDiskBgScan);
                            continue;
                        }
                        
                        // 递归更新主板 (Motherboard / SuperIO)
                        if ((hw.HardwareType == HardwareType.Motherboard || hw.HardwareType == HardwareType.SuperIO|| hw.HardwareType == HardwareType.Cooler) && needMobo)
                        {
                             // ★★★ [优化] 降低更新频率：主板传感器每 3 秒更新一次，减少 I/O 阻塞 ★★★
                             if (isSlowScanTick || forceAll)
                             {
                                 UpdateWithSubHardware(hw);
                             }
                             continue;
                        }
                    }
                }

                _valueProvider.OnUpdateTickStarted();

                // ★★★ 任务错峰执行 (Task Staggering) ★★★
                // 此时使用的是 _secondsCounter (真实秒)，无论刷新率是多少，这里都是精确的 60秒 执行一次
                
                // 1. 流量保存: 每 60 秒 (Offset 5s: 避开整点)
                if (_secondsCounter % 60 == 5)
                {
                    TrafficLogger.Save();
                }

                // 2. 内存软清理: 每 60 秒 (Offset 30s: 避开流量保存)
                if (_secondsCounter % 60 == 30)
                {
                    GC.Collect(2, GCCollectionMode.Optimized); 
                }

                // 3. 内存硬清理: 每 300 秒 (5分钟) (Offset 45s)
                if (_secondsCounter % 300 == 45)
                {
                    try 
                    {
                        using var proc = System.Diagnostics.Process.GetCurrentProcess();
                        // 阈值降低到 30MB，确保即使占用不高也能保持极致轻量
                        if (proc.WorkingSet64 > 30 * 1024 * 1024) 
                        {
                            EmptyWorkingSet(proc.Handle);
                        }
                    } 
                    catch { }
                }

                OnValuesUpdated?.Invoke();
            }
            catch { }
        }

        // 递归更新子硬件，确保 SuperIO 刷新
        private void UpdateWithSubHardware(IHardware hw)
        {
            hw.Update();
            foreach (var sub in hw.SubHardware) 
            {
                UpdateWithSubHardware(sub);
            }
        }

        public void CleanMemory(Action<int>? onProgress = null)
        {
            // 1. 自身 GC (0-5%)
            onProgress?.Invoke(0);
            GC.Collect(2, GCCollectionMode.Forced, true, true);
            onProgress?.Invoke(5);

            // 2. 全局清理 (5-100%)
            try 
            {
                var procs = System.Diagnostics.Process.GetProcesses();
                int total = procs.Length;
                int current = 0;

                // 为了防止除以零
                if (total == 0) 
                {
                    onProgress?.Invoke(100);
                    return;
                }

                foreach (var proc in procs)
                {
                    try
                    {
                        using (proc)
                        {
                            if (!proc.HasExited) EmptyWorkingSet(proc.Handle);
                        }
                    }
                    catch 
                    {
                        // 忽略无权限访问的系统进程 (如 System, Registry 等)
                    }
                    finally
                    {
                        current++;
                        // 映射进度到 5-100% 范围
                        // Progress = 5 + (current / total * 95)
                        int p = 5 + (int)((double)current / total * 95);
                        if (p > 100) p = 100;
                        onProgress?.Invoke(p);
                    }
                }
            }
            catch { }
            
            onProgress?.Invoke(100);
        }

        private void ReloadComputerSafe()
        {
            try
            {
                lock (_lock)
                {
                    // 1. 清理业务缓存
                    _networkManager.ClearCache();
                    _diskManager.ClearCache();
                    _sensorMap.Clear();
                    
                    // ★★★ 新增：清理 Provider 对象缓存，防止持有死对象 ★★★
                    _valueProvider.ClearCache();

                    // ★★★ 新增：清理 UI 列表缓存 ★★★
                    _cachedFanList = null;
                    _cachedNetworkList = null;
                    _cachedDiskList = null;
                    _cachedMoboTempList = null;

                    // 2. ★★★ 清理字符串池 (配合 UIUtils 的新功能) ★★★
                    UIUtils.ClearStringPool();

                    // 3. 关闭硬件服务 (使用 Visitor 模式触发 LHM 内部清理)
                    _computer.Accept(new HardwareVisitor(h => { }));
                    _computer.Close();
                    
                    _computer.Open();

                    // ★★★ 核心修复：重置后再次禁用历史记录 ★★★
                    DisableSensorHistory();
                }
                _sensorMap.Rebuild(_computer, _cfg); 
                _valueProvider.PreCacheAllSensors(_sensorMap); // ★ 重新预热
                
                // 4. ★★★ 优化 T1：重置后再次修剪内存 ★★★
                GC.Collect();
                try { EmptyWorkingSet(System.Diagnostics.Process.GetCurrentProcess().Handle); } catch { }
            }
            catch { }
        }

        // =========================================================
        // ★★★ 核心修复：禁用所有传感器的历史记录 ★★★
        // 这将阻止 LibreHardwareMonitor 在内存中保留 24 小时的数据缓存
        // =========================================================
        private void DisableSensorHistory()
        {
            try
            {
                // 使用 Visitor 模式遍历整棵硬件树
                _computer.Accept(new DisableHistoryVisitor());
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MemoryFix] Failed to disable history: {ex.Message}");
            }
        }

        /// <summary>
        /// 专用访问器：将所有 Sensor 的 ValuesTimeWindow 设为 0
        /// </summary>
        private class DisableHistoryVisitor : IVisitor
        {
            public void VisitComputer(IComputer computer) { computer.Traverse(this); }
            
            public void VisitHardware(IHardware hardware) 
            { 
                // 递归遍历子硬件
                foreach (var sub in hardware.SubHardware) sub.Accept(this);
                
                // 遍历当前硬件的传感器
                foreach (var sensor in hardware.Sensors) VisitSensor(sensor);
            }

            public void VisitSensor(ISensor sensor) 
            { 
                // ★ 关键：通过反射设置 ValuesTimeWindow = TimeSpan.Zero
                // 因为 ISensor 接口通常不暴露这个属性，它属于具体的 Sensor 类
                try
                {
                    var prop = sensor.GetType().GetProperty("ValuesTimeWindow");
                    if (prop != null && prop.CanWrite)
                    {
                        prop.SetValue(sensor, TimeSpan.Zero);
                    }
                }
                catch { }
            }
            
            public void VisitParameter(IParameter parameter) { }
        }

        public void Dispose()
        {
            // ★★★ 核心修复：加锁！防止与正在运行的 UpdateAll() 冲突 ★★★
            lock (_lock)
            {
                try 
                {
                    // 将 Close 操作放入锁内，确保此时 UpdateAll 不在运行
                    _computer.Close(); 
                }
                catch (Exception ex)
                {
                    // 吞掉关闭时的异常，防止弹窗报错困扰用户
                    System.Diagnostics.Debug.WriteLine($"Dispose Error: {ex.Message}");
                }
            }

            _computer.Close();
            _valueProvider.Dispose();
            _perfCounterManager.Dispose(); // ★★★ [新增] 释放计数器资源 ★★★
            _fpsCounter.Dispose(); // <--- 新增
            _networkManager.ClearCache();
            _diskManager.ClearCache(); // 漏掉的，补上
        }
        
        // ========================================================================
        // ★★★ 新增：智能命名方法 (把 SuperIO 芯片名替换为主板名) ★★★
        // ========================================================================
        public static string GenerateSmartName(ISensor sensor, IHardware hardware)
        {
            string hwName = hardware.Name;
            // 如果是 SuperIO (如 ITE IT8688E)，尝试偷梁换柱用主板名
            if (hardware.HardwareType == HardwareType.SuperIO)
            {
                var mobo = Instance?._computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Motherboard);
                if (mobo != null) hwName = mobo.Name;
            }
            // 返回标准格式：Fan #1 [ASUS Z790-P]
            return $"{sensor.Name} [{hwName}]";
        }

        // ★★★ 优化：使用缓存 + Intern，防止生成重复字符串 ★★★
        public static List<string> ListAllNetworks() 
        {
            if (Instance == null) return new List<string>();
            // 修复：如果缓存有数据，返回副本 (.ToList()) 避免污染缓存
            if (Instance._cachedNetworkList != null && Instance._cachedNetworkList.Count > 0) 
                return Instance._cachedNetworkList.ToList();

            var list = Instance._computer.Hardware
                .Where(h => h.HardwareType == HardwareType.Network)
                .Select(h => h.Name)
                .Distinct()
                .ToList();
            
            // 修复：只有搜到硬件才存入缓存，防止启动时的空列表被永久缓存
            if (list.Count > 0) Instance._cachedNetworkList = list;
            // ★★★ 修复：缓存生成 -> 必须返回副本！否则 UI 会污染缓存 ★★★
            return list.ToList();
        }

        // ★★★ 优化：使用缓存 + Intern，防止生成重复字符串 ★★★
        public static List<string> ListAllDisks() 
        {
            if (Instance == null) return new List<string>();
            // 修复：返回副本
            if (Instance._cachedDiskList != null && Instance._cachedDiskList.Count > 0) 
                return Instance._cachedDiskList.ToList();

            var list = Instance._computer.Hardware
                .Where(h => h.HardwareType == HardwareType.Storage)
                .Select(h => h.Name)
                .Distinct()
                .ToList();

            if (list.Count > 0) Instance._cachedDiskList = list;
            // ★★★ 修复：返回副本 ★★★
            return list.ToList();
        }
        
       // 列出所有风扇 (黑名单机制：排除干扰项，允许 USB/Cooler)
        public static List<string> ListAllFans()
        {
            if (Instance == null) return new List<string>();
            // 修复：返回副本
            if (Instance._cachedFanList != null && Instance._cachedFanList.Count > 0) 
                return Instance._cachedFanList.ToList(); 
            
            var list = new List<string>();

            // 辅助递归函数
            void ScanHardware(IHardware hw)
            {
                // 黑名单：与 SensorMap 保持一致
                // 坚决排除 显卡、CPU、硬盘、内存、网卡
                bool isExcluded = hw.HardwareType == HardwareType.GpuNvidia ||
                                  hw.HardwareType == HardwareType.GpuAmd ||
                                  hw.HardwareType == HardwareType.GpuIntel ||
                                  hw.HardwareType == HardwareType.Cpu ||
                                  hw.HardwareType == HardwareType.Storage ||
                                  hw.HardwareType == HardwareType.Memory ||
                                  hw.HardwareType == HardwareType.Network;

                // 只要不在黑名单里，都扫描！
                if (!isExcluded)
                {
                    foreach (var s in hw.Sensors)
                    {
                        // 只列出 Fan 类型 (转速)
                        if (s.SensorType == SensorType.Fan)
                        {
                            // ★★★ 修复：调用统一的 SmartName 方法 ★★★
                            list.Add(GenerateSmartName(s, hw));
                        }
                    }
                }

                // 递归扫描子硬件
                foreach (var sub in hw.SubHardware)
                {
                    ScanHardware(sub);
                }
            }

            // 开始扫描根节点
            foreach (var hw in Instance._computer.Hardware)
            {
                ScanHardware(hw);
            }
            
            // 排序并去重
            list.Sort(); 
            var final = list.Distinct().ToList();
            // 存入缓存
            if (final.Count > 0) Instance._cachedFanList = final;
            
            // ★★★ 核心修复点：必须返回 final.ToList() ★★★
            // 之前这里直接返回 final，导致 UI 层插入 "Auto" 时直接修改了缓存对象，引发"多个自动"Bug
            return final.ToList();
        }

        // 列出所有适合作为"主板/系统温度"的传感器
        public static List<string> ListAllMoboTemps()
        {
            if (Instance == null) return new List<string>();
            // 修复：返回副本
            if (Instance._cachedMoboTempList != null && Instance._cachedMoboTempList.Count > 0) 
                return Instance._cachedMoboTempList.ToList();

            var list = new List<string>();

            void ScanHardware(IHardware hw)
            {
                // 排除逻辑：只想要主板上的传感器，排除 CPU核心、显卡、硬盘、内存条、网卡
                bool isExcluded = hw.HardwareType == HardwareType.Cpu ||
                                  hw.HardwareType == HardwareType.GpuNvidia ||
                                  hw.HardwareType == HardwareType.GpuAmd ||
                                  hw.HardwareType == HardwareType.GpuIntel ||
                                  hw.HardwareType == HardwareType.Storage ||
                                  hw.HardwareType == HardwareType.Memory ||
                                  hw.HardwareType == HardwareType.Network;

                if (!isExcluded)
                {
                    foreach (var s in hw.Sensors)
                    {
                        if (s.SensorType == SensorType.Temperature)
                        {
                            // ★★★ 修复：调用统一的 SmartName 方法 ★★★
                            list.Add(GenerateSmartName(s, hw));
                        }
                    }
                }

                foreach (var sub in hw.SubHardware) ScanHardware(sub);
            }

            foreach (var hw in Instance._computer.Hardware) ScanHardware(hw);

            list.Sort();
            var final = list.Distinct().ToList();
            if (final.Count > 0) Instance._cachedMoboTempList = final;
            
            // ★★★ 修复：返回副本 ★★★
            return final.ToList();
        }

        private static IEnumerable<ISensor> GetAllSensors(IHardware hw, SensorType type)
        {
            foreach (var s in hw.Sensors) if (s.SensorType == type) yield return s;
            foreach (var sub in hw.SubHardware) 
                foreach (var s in GetAllSensors(sub, type)) yield return s;
        }

        // 内部 Visitor 类，用于触发 LHM 清理逻辑
        private class HardwareVisitor : IVisitor
        {
            private Action<IHardware> _action;
            public HardwareVisitor(Action<IHardware> action) { _action = action; }
            public void VisitComputer(IComputer computer) { computer.Traverse(this); }
            public void VisitHardware(IHardware hardware) { 
                _action(hardware); 
                foreach (var sub in hardware.SubHardware) sub.Accept(this); 
            }
            public void VisitParameter(IParameter parameter) { }
            public void VisitSensor(ISensor sensor) { }
        }
    }
}