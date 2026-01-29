using System;
using System.Drawing;
using System.Linq; // 需要 Linq 来查询 Config
using LiteMonitor.src.Core;
using LiteMonitor.src.SystemServices.InfoService; // [New] For Plugin Color Override

namespace LiteMonitor
{
    public enum MetricRenderStyle
    {
        StandardBar, 
        TwoColumn,   
        TextOnly     
    }

    public class MetricItem
    {
        // [新增] 绑定原始配置对象，实现动态 Label
        public MonitorItemConfig BoundConfig { get; set; }

        private string _key = "";
        
        // [Optimization] Cached InfoService lookup keys
        private string _propLabelKey;
        private string _propShortLabelKey;
        private string _dashColorKey;

        public string Key 
        { 
            get => _key;
            set 
            {
                _key = UIUtils.Intern(value);
                // Pre-calculate lookup keys
                _propLabelKey = "PROP.Label." + _key;
                _propShortLabelKey = "PROP.ShortLabel." + _key;
                if (_key.StartsWith("DASH."))
                    _dashColorKey = _key.Substring(5) + ".Color";
                else
                    _dashColorKey = null;
            } 
        }

        private string _label = "";
        public string Label 
        {
            get 
            {
                // [Refactor] 使用统一的 Label 解析器
                string labelResolved = MetricLabelResolver.ResolveLabel(BoundConfig);
                if (!string.IsNullOrEmpty(labelResolved)) return labelResolved;

                return _label;
            }
            set => _label = UIUtils.Intern(value);
        }
        
        private string _shortLabel = "";
        public string ShortLabel 
        {
            get 
            {
                // [Refactor] 使用统一的 Label 解析器
                string shortResolved = MetricLabelResolver.ResolveShortLabel(BoundConfig);
                if (!string.IsNullOrEmpty(shortResolved)) return shortResolved;

                return _shortLabel;
            }
            set => _shortLabel = UIUtils.Intern(value);
        }
        
        public float? Value { get; set; } = null;
        public float DisplayValue { get; set; } = 0f;
        public string TextValue { get; set; } = null;

        // =============================
        // 缓存字段
        // =============================
        private float _cachedDisplayValue = -99999f; 
        private bool _cachedChargingState = false; // [Fix] 缓存充电状态，用于触发图标刷新
        private string _cachedNormalText = "";       // 完整文本 (值+单位)
        private string _cachedHorizontalText = "";   // 完整横屏文本
        
        // ★★★ [新增] 分离缓存 ★★★
        public string CachedValueText { get; private set; } = "";
        public string CachedUnitText { get; private set; } = "";
        public bool HasCustomUnit { get; private set; } = false; // 标记是否使用了自定义单位


        public int CachedColorState { get; private set; } = 0;
        public double CachedPercent { get; private set; } = 0.0;

        public Color GetTextColor(Theme t)
        {
            return UIUtils.GetStateColor(CachedColorState, t, true);
        }

        public string GetFormattedText(bool isHorizontal)
        {
            // [Debug & Fix] 1. Always update color state for Plugin Items FIRST
            if (_dashColorKey != null) // Use cached check
            {
                string colorVal = InfoService.Instance.GetValue(_dashColorKey);
                
                if (!string.IsNullOrEmpty(colorVal))
                {
                    if (int.TryParse(colorVal, out int state)) 
                    {
                        CachedColorState = state;
                    }
                }
                else
                {
                    CachedColorState = 0; // Default Safe if no color override
                }
            }

            // 2. Load Config (Optimized)
            var cfg = BoundConfig;
            if (cfg == null)
            {
                 // Fallback: This should rarely happen if initialized correctly
                 cfg = Settings.Load().MonitorItems.FirstOrDefault(x => x.Key == Key);
            }
            
            string userFormat = isHorizontal ? cfg?.UnitTaskbar : cfg?.UnitPanel;
            HasCustomUnit = !string.IsNullOrEmpty(userFormat);

            // 3. Return TextValue (Plugin/Dashboard items)
           if (TextValue != null) 
            {
                // [Optimization] 统一使用 MetricUtils 处理单位逻辑
                // 1. 获取默认单位 (自动处理：插件查InfoService，硬件查表)
                var ctx = isHorizontal ? MetricUtils.UnitContext.Taskbar : MetricUtils.UnitContext.Panel;
                string defUnit = MetricUtils.GetUnitStr(Key, null, ctx);

                // 2. 确定最终单位 (自动处理：用户设为Null时用默认，否则用自定义)
                string finalUnit = MetricUtils.GetDisplayUnit(Key, defUnit, userFormat);

                // 3. 智能拼接：如果文本里还没包含这个单位，就拼上去
                if (!string.IsNullOrEmpty(finalUnit) && !TextValue.EndsWith(finalUnit))
                {
                    return TextValue + finalUnit;
                }

                return TextValue;
            }

            // 4. Numeric Value Processing (Hardware items)
            // [Fix] 增加充电状态检查：如果数值变了 OR (是电池相关项 AND 充电状态变了) -> 强制刷新
            bool isBat = Key.StartsWith("BAT", StringComparison.OrdinalIgnoreCase);
            bool chargingChanged = isBat && (_cachedChargingState != MetricUtils.IsBatteryCharging);

            if (Math.Abs(DisplayValue - _cachedDisplayValue) > 0.05f || chargingChanged)
            {
                _cachedDisplayValue = DisplayValue;
                if (isBat) _cachedChargingState = MetricUtils.IsBatteryCharging;

                // [Refactor] 使用新的原子函数分别构建普通和紧凑文本
                
                // 1. 标准模式 (Panel)
                string valNormal = MetricUtils.GetValueStr(Key, DisplayValue, false);
                string unitNormal = MetricUtils.GetUnitStr(Key, DisplayValue, MetricUtils.UnitContext.Panel);
                
                CachedValueText = valNormal;
                CachedUnitText = MetricUtils.GetDisplayUnit(Key, unitNormal, userFormat);
                _cachedNormalText = CachedValueText + CachedUnitText;

                // 2. 紧凑模式 (Taskbar/Horizontal)
                if (HasCustomUnit)
                {
                    // 自定义单位模式下，不做数值压缩，保持与 Panel 一致
                    _cachedHorizontalText = _cachedNormalText;
                }
                else
                {
                    // 自动模式：启用数值压缩 (Compact=true) 和 紧凑单位 (Taskbar Context)
                    string valCompact = MetricUtils.GetValueStr(Key, DisplayValue, true);
                    string unitCompact = MetricUtils.GetUnitStr(Key, DisplayValue, MetricUtils.UnitContext.Taskbar);
                    
                    // 确保单位正确注入 (虽然 Auto 模式下 GetDisplayUnit 通常直接返回 unitCompact)
                    string finalUnitCompact = MetricUtils.GetDisplayUnit(Key, unitCompact, null);
                    
                    _cachedHorizontalText = valCompact + finalUnitCompact;
                }

                // Only calculate color if NOT a plugin item (already handled above)
                if (!Key.StartsWith("DASH."))
                {
                    CachedColorState = MetricUtils.GetState(Key, DisplayValue);
                }
                
                CachedPercent = MetricUtils.GetProgressValue(Key, DisplayValue);
            }
            return isHorizontal ? _cachedHorizontalText : _cachedNormalText;
        }

        public MetricRenderStyle Style { get; set; } = MetricRenderStyle.StandardBar;
        public Rectangle Bounds { get; set; } = Rectangle.Empty;

        public Rectangle LabelRect;   
        public Rectangle ValueRect;   
        public Rectangle BarRect;     
        public Rectangle BackRect;    

        public void TickSmooth(double speed)
        {
            if (!Value.HasValue) return;
            float target = Value.Value;
            float diff = Math.Abs(target - DisplayValue);
            if (diff < 0.05f) return;
            if (diff > 15f || speed >= 0.9) DisplayValue = target;
            else DisplayValue += (float)((target - DisplayValue) * speed);
        }
    }
}