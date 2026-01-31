using System;
using System.Collections.Generic;
using System.Linq;
using LibreHardwareMonitor.Hardware;
using LiteMonitor.src.Core;

namespace LiteMonitor.src.SystemServices
{
    /// <summary>
    /// 核心服务：负责将物理硬件传感器映射为标准 Key (如 CPU.Temp)
    /// </summary>
    public class SensorMap
    {
        // 核心映射字典
        private readonly Dictionary<string, ISensor> _map = new();
        
        // ★★★ [新增] 引入子服务：风扇匹配器 (解耦) ★★★
        private readonly FanMapper _fanMapper = new FanMapper();

        // 高性能缓存
        // CPU 核心传感器对 (用于加权平均计算)
        public class CpuCoreSensors
        {
            public ISensor? Clock;
            public ISensor? Load;
        }
        public List<CpuCoreSensors> CpuCoreCache { get; private set; } = new();
        
        // 显卡硬件缓存 (用于快速定位)
        public IHardware? CachedGpu { get; private set; }
        // ★★★ [新增] 缓存总线传感器 (用于 Zen 5 频率修正) ★★★
        public ISensor? CpuBusSpeedSensor { get; private set; }

        private DateTime _lastMapBuild = DateTime.MinValue;
        
        // ★★★ [优化 1] 移除了全系统传感器指纹相关字段 ★★★
        private readonly object _lock = new object();
        
        // ★★★ [新增] 配置引用 ★★★
        private Settings _cfg;

        // [Fix] 回归 v1.2.9 的简单逻辑
        // 只有在初始化时才需要 build，之后除非手动触发或 10 分钟兜底，否则不需要任何检测。
        // EnsureFresh 的返回值改为 void，因为不需要外部感知是否重建。
        public void EnsureFresh(Computer computer, Settings cfg) 
        {
            if ((DateTime.Now - _lastMapBuild).TotalMinutes > 10)
            {
                Rebuild(computer, cfg);
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _map.Clear();
                CpuCoreCache.Clear();
                CachedGpu = null;
                CpuBusSpeedSensor = null;
                // _lastSensorFingerprint = 0; // 已移除
            }
        }



        public bool TryGetSensor(string key, out ISensor? sensor)
        {
            lock (_lock)
            {
                return _map.TryGetValue(key, out sensor);
            }
        }

        /// <summary>
        /// [新增] 获取内部映射的副本，用于 HardwareValueProvider 的静态化预热
        /// </summary>
        public Dictionary<string, ISensor> GetInternalMap()
        {
            lock (_lock)
            {
                return new Dictionary<string, ISensor>(_map);
            }
        }

        // =======================================================================
        // [核心] 构建传感器映射与缓存 (最复杂的构建逻辑)
        // =======================================================================
        public void Rebuild(Computer computer, Settings cfg) // ★ 签名修改
        {
            _cfg = cfg;
            // 1. 准备临时容器 (线程安全)
            var newMap = new Dictionary<string, ISensor>(StringComparer.OrdinalIgnoreCase); // 使用忽略大小写的比较器，减少字符串重复
            var newCpuCache = new List<CpuCoreSensors>();
            IHardware? newGpu = null;
            ISensor? newBusSensor = null; // 临时变量
            
            // ★★★ [新增] 临时列表：用于智能匹配 ★★★
            // 注意：这里不再在递归中直接处理风扇匹配，而是收集起来统一交给 ScanAndMapFans 处理
            // 但为了保持原代码结构，我们依然用candidates收集主板相关数据
            var candidatesMoboTemps = new List<ISensor>(capacity: 10); // 预设容量，减少扩容开销

            // 局部递归函数
            void RegisterTo(IHardware hw)
            {
                //hw.Update();

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
                            s.Name.EndsWith(clock.Name, StringComparison.OrdinalIgnoreCase)); 
                            
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
                    
                    // ★★★ [新增] GPU 风扇映射 ★★★
                    var fan = hw.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Fan);
                    if (fan == null) fan = hw.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Control);
                    if (fan != null) newMap["GPU.Fan"] = fan;
                }
                
                // ★★★ [新增] 收集主板/SuperIO 传感器 ★★★
                if (hw.HardwareType == HardwareType.Motherboard || hw.HardwareType == HardwareType.SuperIO)
                {
                    foreach (var s in hw.Sensors)
                    {
                        // 风扇数据由后续 ScanAndMapFans 全局扫描，这里只收集温度备用
                        if (s.SensorType == SensorType.Temperature) candidatesMoboTemps.Add(s);
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
            var ordered = computer.Hardware.OrderBy(h => GetHwPriority(h));
            foreach (var hw in ordered) RegisterTo(hw);
            
            // ============================================
            // ★★★ [新增] 智能匹配逻辑 ★★★
            // ============================================
            
            // A. 主板温度 (策略：System > Motherboard > Chipset > MaxValue)
            if (!newMap.ContainsKey("MOBO.Temp") && candidatesMoboTemps.Count > 0)
            {
                // 1. 优先匹配标准名称 (System, Motherboard)
                var best = candidatesMoboTemps.FirstOrDefault(x => Has(x.Name, "System"));
                if (best == null) best = candidatesMoboTemps.FirstOrDefault(x => Has(x.Name, "Motherboard"));
                
                // 2. 其次匹配芯片组 (Chipset)
                if (best == null) best = candidatesMoboTemps.FirstOrDefault(x => Has(x.Name, "Chipset") || Has(x.Name, "PCH"));
                
                // 3. 兜底策略：找有效值中最大的 (假设热点即关键点，且排除 0 和 200+ 异常值)
                if (best == null)
                {
                    // ★★★ 优化策略：优先寻找"合理范围"(15-68度)内的最大值，这通常是 System/Motherboard 温度 ★★★
                    // 只有当找不到合理值时，才去寻找更宽范围(0-95度)的最大值(可能是 VRM 或 Chipset)
                    // 修复：用户反馈 Asus Z790 主板 Temperature #5 经常跳到 100+，导致误报。
                    
                    ISensor? bestSafe = null;
                    float maxSafe = -999f;
                    
                    ISensor? bestFallback = null;
                    float maxFallback = -999f;

                    foreach (var t in candidatesMoboTemps)
                    {
                        if (!t.Value.HasValue) continue;
                        float v = t.Value.Value;
                        
                        // 1. 收集宽范围 (0 - 95)，严格过滤掉 >95 的异常值/虚假值 (原逻辑是 110，太宽了)
                        if (v > 0 && v < 95 && v > maxFallback)
                        {
                            maxFallback = v;
                            bestFallback = t;
                        }
                        
                        // 2. 收集合理范围 (15 - 68)
                        // 68度通常是主板/系统温度的合理上限，超过这个值可能是 Chipset 或 VRM
                        if (v >= 15 && v <= 68 && v > maxSafe)
                        {
                            maxSafe = v;
                            bestSafe = t;
                        }
                    }
                    
                    // 优先使用合理范围的传感器
                    best = bestSafe ?? bestFallback;
                }
                
                if (best != null) newMap["MOBO.Temp"] = best;
            }

            // B. 风扇匹配 (调用全新智能算法)
            // ★★★ 修改：委托给 FanMapper 专用类处理 ★★★
            _fanMapper.ScanAndMapFans(computer, cfg, newMap);

            // 2. 原子交换数据 (加锁)
            lock (_lock)
            {
                _map.Clear();
                foreach (var kv in newMap) _map[kv.Key] = kv.Value;
                
                CpuCoreCache = newCpuCache;
                CachedGpu = newGpu;
                CpuBusSpeedSensor = newBusSensor; // ★ 更新 Bus Sensor 缓存
                
                _lastMapBuild = DateTime.Now;
                // ★★★ [优化 3] 指纹记录已移除 ★★★
            }
        }

        // ===========================================================
        // [重要] 传感器名称标准化映射 (原 Logic.cs 中的 NormalizeKey)
        // ===========================================================
        private string? NormalizeKey(IHardware hw, ISensor s)
        {
            string name = s.Name;
            var type = hw.HardwareType;

            // --- CPU ---
            if (type == HardwareType.Cpu)
            {
                // 新代码：增加 "package" 支持，防止某些 CPU 把总负载叫 "CPU Package"
                if (s.SensorType == SensorType.Load)
                {
                    if (Has(name, "total") || Has(name, "package")) 
                        return "CPU.Load";
                }
                // [深度优化后的温度匹配逻辑]
                if (s.SensorType == SensorType.Temperature)
                {
                    // 1. 黄金标准：包含这些词的通常就是我们要的
                    if (Has(name, "package") ||  // Intel/AMD 标准
                        Has(name, "average") ||  // LHM 聚合数据
                        Has(name, "tctl") ||     // AMD 风扇控制温度 (最准)
                        Has(name, "tdie") ||     // AMD 核心硅片温度
                        Has(name, "ccd") ||       // AMD 核心板
                        Has(name, "cores"))     // 通用核心温度
                    {
                        return "CPU.Temp";
                    }

                    // 2. 银牌标准：通用名称兜底 (修复 AMD 7840HS 等移动端 CPU)
                    // 必须严格排除干扰项 (如 SOC, VRM, Pump 等)
                    if ((Has(name, "cpu") || Has(name, "core")) && 
                        !Has(name, "soc") &&     // 排除核显/片上系统
                        !Has(name, "vrm") &&     // 排除供电
                        !Has(name, "fan") &&     // 排除风扇(虽类型不同，但防名字干扰)
                        !Has(name, "pump") &&    // 排除水泵
                        !Has(name, "liquid") &&  // 排除水冷液
                        !Has(name, "coolant") && // 排除冷却液
                        !Has(name, "distance"))  // 排除 "Distance to TjMax"
                    {
                        return "CPU.Temp";
                    }
                }
                if (s.SensorType == SensorType.Power && (Has(name, "package") || Has(name, "cores"))) return "CPU.Power";
                // [New] CPU Voltage
                if (s.SensorType == SensorType.Voltage && (Has(name, "core") || Has(name, "cpu") || Has(name, "vcore") || Has(name, "vid"))) 
                {
                    // Exclude distractions
                    if (!Has(name, "soc") && !Has(name, "gt") && !Has(name, "sa") && !Has(name, "aux")) 
                        return "CPU.Voltage";
                }
            }

            // --- GPU ---
            if (type is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel)
            {
                if (s.SensorType == SensorType.Load && (Has(name, "core") || Has(name, "d3d 3d"))) return "GPU.Load";
                if (s.SensorType == SensorType.Temperature && (Has(name, "core") || Has(name, "hot spot") || Has(name, "soc") || Has(name, "vr"))) return "GPU.Temp";
                
                // VRAM
                if (s.SensorType == SensorType.SmallData && (Has(name, "memory") || Has(name, "dedicated")))
                {
                    if (Has(name, "used")) return "GPU.VRAM.Used";
                    if (Has(name, "total")) return "GPU.VRAM.Total";
                }
                if (s.SensorType == SensorType.Load && Has(name, "memory")) return "GPU.VRAM.Load";
            }

            // --- Memory ---
            if (type == HardwareType.Memory) 
            {
                if (Has(hw.Name, "virtual")) return null;
                // 1. 负载 (保持不变)
                if (s.SensorType == SensorType.Load && Has(name, "memory")) return "MEM.Load";
                
                // 2. ★ 增强版匹配：同时接受 Data 和 SmallData
                if (s.SensorType == SensorType.Data || s.SensorType == SensorType.SmallData)
                {
                    if (Has(name, "used")) return "MEM.Used";
                    if (Has(name, "available")) return "MEM.Available";
                }
            }

            // --- Battery ---
            if (type == HardwareType.Battery)
            {
                if (s.SensorType == SensorType.Level)
                {
                    // Fix: 过滤掉电池损耗/健康度数据 (通常也标记为 Level 类型)
                    // 用户反馈 #199: Battery level shows Degradation level
                    if (Has(name, "Degradation") || Has(name, "Wear")) return null;
                    
                    // ★★★ 优先选择包含 "Charge" 的传感器 ★★★
                    if (Has(name, "Charge")) return "BAT.Percent";
                    
                    // 其他作为备选 (Weak)
                    return "BAT.Percent";
                }
                if (s.SensorType == SensorType.Power)
                {
                    // ★★★ [Simplified] 移除基于充电状态的硬过滤，防止在特殊状态(如充满插电)下找不到传感器导致数据卡死 ★★★
                    // 无论当前是充电还是放电，只要是 Power 类型的电池传感器，都进行映射。
                    // 具体的数值正负号和归零逻辑交由 BatteryService 处理。
                    return "BAT.Power";
                }
                if (s.SensorType == SensorType.Voltage) return "BAT.Voltage";
                if (s.SensorType == SensorType.Current) return "BAT.Current";
            }

            return null;
        }

        // 公共辅助方法 - 优化字符串比较，减少内存分配
        public static bool Has(string source, string sub)
        {
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(sub)) return false;
            return source.AsSpan().Contains(sub.AsSpan(), StringComparison.OrdinalIgnoreCase); // 使用Span避免内存分配
        }

        private bool IsGenericGpuName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return true;
            // 常见核显名称: "AMD Radeon(TM) Graphics", "Intel(R) UHD Graphics"
            if (name.Equals("AMD Radeon(TM) Graphics", StringComparison.OrdinalIgnoreCase)) return true;
            if (name.Contains("UHD Graphics", StringComparison.OrdinalIgnoreCase)) return true;
            if (name.Contains("Iris", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private int GetHwPriority(IHardware hw)
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
    }
}