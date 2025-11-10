using System;
using System.Drawing;

namespace LiteMonitor
{
    public class MetricItem
    {
        public string Key { get; set; } = "";
        public string Label { get; set; } = "";
        public Rectangle Bounds { get; set; } = Rectangle.Empty;

        public float? Value { get; set; } = null;
        public float DisplayValue { get; set; } = 0f;

        public void TickSmooth(double speed)
        {
            if (!Value.HasValue) return;
            float target = Value.Value;
            float diff = Math.Abs(target - DisplayValue);

            if (diff < 0.05f) return; // ºöÂÔÎ¢Ð¡¶¶¶¯
            if (diff > 15f || speed >= 0.9)
                DisplayValue = target;
            else
                DisplayValue += (float)((target - DisplayValue) * speed);
        }


    }
}
