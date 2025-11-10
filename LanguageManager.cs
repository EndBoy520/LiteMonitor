using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace LiteMonitor
{
    public static class LanguageManager
    {
        public static string CurrentLang { get; private set; } = "zh";
        private static Dictionary<string, string> _texts = new();

        private static string LangDir
        {
            get
            {
                var dir = Path.Combine(AppContext.BaseDirectory, "lang");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                return dir;
            }
        }

        public static void Load(string langCode)
        {
            try
            {
                var path = Path.Combine(LangDir, $"{langCode}.json");
                if (!File.Exists(path))
                {
                    Console.WriteLine($"[LanguageManager] Missing lang file: {langCode}.json");
                    return;
                }
                var json = File.ReadAllText(path);
                using var doc = JsonDocument.Parse(json);
                _texts = Flatten(doc.RootElement);
                CurrentLang = langCode;
                Console.WriteLine($"[LanguageManager] Loaded language: {langCode}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LanguageManager] Load failed: {ex.Message}");
            }
        }

        public static string T(string key)
        {
            if (_texts.TryGetValue(key, out var val)) return val;
            int dot = key.IndexOf('.');
            return dot >= 0 ? key[(dot + 1)..] : key;   // ✅ 去掉前缀
        }

        private static Dictionary<string, string> Flatten(JsonElement element, string prefix = "")
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in element.EnumerateObject())
            {
                string fullKey = string.IsNullOrEmpty(prefix) ? prop.Name : $"{prefix}.{prop.Name}";
                if (prop.Value.ValueKind == JsonValueKind.Object)
                {
                    foreach (var kv in Flatten(prop.Value, fullKey))
                        dict[kv.Key] = kv.Value;
                }
                else
                {
                    dict[fullKey] = prop.Value.GetString() ?? "";
                }
            }
            return dict;
        }
    }
}
