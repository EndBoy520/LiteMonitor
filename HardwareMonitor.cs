using System;
using System.Collections.Generic;
using System.Linq;
using LibreHardwareMonitor.Hardware;

namespace LiteMonitor
{
    public sealed class HardwareMonitor : IDisposable
    {
        private readonly Computer _computer;
        private readonly Dictionary<string, ISensor> _map = new();
        private readonly Dictionary<string, float> _lastValid = new();
        private DateTime _lastMapBuild = DateTime.MinValue;

        public event Action? OnValuesUpdated;

        public HardwareMonitor(Settings cfg)
        {
            _computer = new Computer()
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true,
                IsMemoryEnabled = true,
                IsNetworkEnabled = true,
                IsStorageEnabled = true,   
                IsMotherboardEnabled = false,// 关闭非必要模块
                IsControllerEnabled = false
            };
            // 异步打开硬件，避免UI阻塞
            Task.Run(() =>
            {
                try
                {
                    _computer.Open();
                    BuildSensorMap();
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[HardwareMonitor] init failed: " + ex.Message);
                }
            });
        }

        private void BuildSensorMap()
        {
            _map.Clear();
            foreach (var hw in _computer.Hardware)
                RegisterHardware(hw);
            _lastMapBuild = DateTime.Now;
        }

        private void RegisterHardware(IHardware hw)
        {
            hw.Update(); // 直接刷新当前硬件
            foreach (var s in hw.Sensors)
            {
                string? key = NormalizeKey(hw, s);
                if (!string.IsNullOrEmpty(key) && !_map.ContainsKey(key))
                    _map[key] = s;
            }
            // ✅ 递归子硬件（原本由 Visitor 完成）
            foreach (var sub in hw.SubHardware)
                RegisterHardware(sub);
        }

        private static string? NormalizeKey(IHardware hw, ISensor s)
        {
            // 所有名称统一转小写，避免大小写不一致
            string name = s.Name.ToLower();
            var type = hw.HardwareType;

            // ========================= 🧠 CPU =========================
            if (type == HardwareType.Cpu)
            {
                // ---- CPU 总体负载 ----
                // Intel/AMD 均有 “CPU Total” 字段，最可靠
                if (s.SensorType == SensorType.Load && name.Contains("total"))
                    return "CPU.Load";

                // ---- CPU 温度 ----
                // 优先级：core average（最平滑）> package（旧平台兜底）
                // 不再包含 core max，避免瞬时抖动
                if (s.SensorType == SensorType.Temperature)
                {
                    if (name.Contains("average") || name.Contains("core average"))
                        return "CPU.Temp"; // ✅ 首选
                    if (name.Contains("package") || name.Contains("tctl"))
                        return "CPU.Temp"; // ✅ 兜底（AMD/旧Intel）
                }
            }

            // ========================= 🎮 GPU =========================
            if (type is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel)
            {
                // ---- 温度 ----
                // 优先 GPU Core；次选 Hotspot；排除 Memory Junction（部分显卡异常 255℃）
                if (s.SensorType == SensorType.Temperature &&
                    (name.Contains("core") || name.Contains("hotspot")))
                    return "GPU.Temp";

                // ---- 负载 ----
                // “GPU Core” 或 “GPU” 表示整体核心负载率
                if (s.SensorType == SensorType.Load &&
                    (name.Contains("core") || name.Contains("gpu")))
                    return "GPU.Load";

                // ---- 显存 ----
                // 一般有 D3D Dedicated / GPU Memory Used / GPU Memory Total
                if (s.SensorType == SensorType.SmallData)
                {
                    if ((name.Contains("dedicated") || name.Contains("memory")) && name.Contains("used"))
                        return "GPU.VRAM.Used";
                    if ((name.Contains("dedicated") || name.Contains("memory")) && name.Contains("total"))
                        return "GPU.VRAM.Total";
                }

                // ---- 显存负载率（部分显卡有 “GPU Memory” Load 字段）----
                if (s.SensorType == SensorType.Load && name.Contains("memory"))
                    return "GPU.VRAM.Load";
            }

            // ========================= 💾 Memory =========================
            if (type == HardwareType.Memory)
            {
                // Load: Memory -> 百分比（推荐）
                if (s.SensorType == SensorType.Load && name.Contains("memory"))
                    return "MEM.Load";
            }


            // ========================= 💽 Disk =========================
            if (type == HardwareType.Storage)
            {
                if (s.SensorType == SensorType.Throughput)
                {
                    if (name.Contains("read")) return "DISK.Read";
                    if (name.Contains("write")) return "DISK.Write";
                }
            }


            // ========================= 🌐 Network =========================
            if (type == HardwareType.Network && s.SensorType == SensorType.Throughput)
            {
                // Throughput: Upload/Download Speed（单位：Bytes/s）
                if (name.Contains("upload") || name.Contains("up") || name.Contains("sent"))
                    return "NET.Up";
                if (name.Contains("download") || name.Contains("down") || name.Contains("received"))
                    return "NET.Down";
            }

            // ========================= 🧩 兼容性扩展（未来支持） =========================
            // 你可以在此添加 Storage / Fan / Power 等映射
            // if (type == HardwareType.Storage && name.Contains("load")) return "DISK.Load";
            // if (type == HardwareType.Fan && name.Contains("fan")) return "FAN.Speed";

            return null;
        }

        private void EnsureMapFresh()
        {
            if ((DateTime.Now - _lastMapBuild).TotalMinutes > 10)
                BuildSensorMap();
        }

        public float? Get(string key)
        {
            EnsureMapFresh();
            if (key == "GPU.VRAM")
            {
                float? used = Get("GPU.VRAM.Used");
                float? total = Get("GPU.VRAM.Total");
                if (used.HasValue && total.HasValue && total > 0)
                {
                    // 字节转 MB
                    if (total > 1024 * 1024 * 10)
                    {
                        used /= 1024f * 1024f;
                        total /= 1024f * 1024f;
                    }
                    return used / total * 100f;
                }
                if (_map.TryGetValue("GPU.VRAM.Load", out var s) && s.Value.HasValue)
                    return s.Value;
            }

            if (_map.TryGetValue(key, out var sensor))
            {
                var val = sensor.Value;
                if (val.HasValue && !float.IsNaN(val.Value))
                {
                    _lastValid[key] = val.Value;
                    return val.Value;
                }
                if (_lastValid.TryGetValue(key, out var last))
                    return last;
            }
            return null;
        }

        public void UpdateAll()
        {
            try
            {
                foreach (var hw in _computer.Hardware)
                {
                    if (hw.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel or HardwareType.Cpu)
                        hw.Update();  // 高频数据：CPU/GPU
                    else if ((DateTime.Now - _lastMapBuild).TotalSeconds > 3)
                        hw.Update();  // 低频数据：内存、网卡
                }
                OnValuesUpdated?.Invoke();
            }
            catch { }
        }


        public void Dispose() => _computer.Close();
    }
}
