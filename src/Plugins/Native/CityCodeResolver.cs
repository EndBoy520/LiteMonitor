using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using LiteMonitor.src.Core; // Added for UIUtils
using LiteMonitor.src.SystemServices.InfoService;

namespace LiteMonitor.src.Plugins.Native
{
    public static class CityCodeResolver
    {
        // TODO: 用户需要将生成的 city_code_v8.json 上传到 Cloudflare Pages，并替换此处的 URL
        private const string JSON_DB_URL = "https://litemonitor.cn/update/CityCode.json";
        
        // Memory Cache
        private static Dictionary<string, List<List<string>>> _db = null;
        private static bool _isLoading = false;
        private static readonly object _lock = new object();

        public static async Task<string> ResolveAsync(string province, string city, string district)
        {
            await EnsureDbLoadedAsync();

            if (_db == null) throw new Exception("CityCode DB failed to load");

            // 1. 参数清洗
            province = (province ?? "").Replace("省", "").Replace("市", "").Replace("自治区", "").Replace("壮族", "").Replace("回族", "").Replace("维吾尔", "");
            city = (city ?? "").Trim();
            district = (district ?? "").Trim();

            if (string.IsNullOrEmpty(district) && string.IsNullOrEmpty(city))
            {
                return JsonSerializer.Serialize(new { error = "Empty query" });
            }

            // 2. 查找逻辑
            List<List<string>> candidates = null;
            string targetName = "";

            // 3.1 优先查 District
            if (!string.IsNullOrEmpty(district))
            {
                candidates = Lookup(district);
                targetName = district;
            }

            // 3.2 降级查 City
            if (candidates == null && !string.IsNullOrEmpty(city))
            {
                candidates = Lookup(city);
                targetName = city;
            }

            if (candidates == null)
            {
                return JsonSerializer.Serialize(new { error = "Not Found" });
            }

            // 4. 评分排序
            List<string> bestMatch = candidates[0];
            int maxScore = -1000;

            if (candidates.Count > 1)
            {
                foreach (var item in candidates)
                {
                    // item: [code, name, province]
                    string code = item[0];
                    string name = item[1];
                    string prov = item.Count > 2 ? item[2] : "";

                    int score = 0;
                    if (!string.IsNullOrEmpty(province) && prov.Contains(province)) score += 100;
                    if (name == targetName) score += 50;
                    score -= name.Length;

                    if (score > maxScore)
                    {
                        maxScore = score;
                        bestMatch = item;
                    }
                }
            }

            return JsonSerializer.Serialize(new
            {
                code = bestMatch[0],
                name = bestMatch[1],
                province = bestMatch.Count > 2 ? bestMatch[2] : ""
            });
        }

        private static List<List<string>> Lookup(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (_db.TryGetValue(key, out var res)) return res;

            // 处理后缀
            string root = System.Text.RegularExpressions.Regex.Replace(key, "(自治州|自治县|地区|盟|市|区|县|旗)$", "");
            if (root.Length < 2 && key.Length > 1) root = key;

            if (_db.TryGetValue(root, out var resRoot)) return resRoot;

            return null;
        }

        private static async Task EnsureDbLoadedAsync()
        {
            if (_db != null) return;
            if (_isLoading) 
            {
                // Simple spin wait or just return error to avoid deadlock/complexity in this demo
                await Task.Delay(100); 
                if (_db != null) return;
            }

            _isLoading = true;
            try
            {
                // 使用默认 HttpClient 下载
                using (var client = new HttpClient())
                {
                    // 设置超时
                    client.Timeout = TimeSpan.FromSeconds(10);
                    var json = await client.GetStringAsync(JSON_DB_URL);
                    var rawDb = JsonSerializer.Deserialize<Dictionary<string, List<List<string>>>>(json);

                    // [Optimization] Intern strings in the DB to reduce memory usage
                    // Many cities share the same province name, and many districts share the same city name
                    _db = new Dictionary<string, List<List<string>>>(rawDb.Count);
                    
                    foreach (var kvp in rawDb)
                    {
                        // Key (district name) can also be interned
                        string key = UIUtils.Intern(kvp.Key);
                        var lists = new List<List<string>>(kvp.Value.Count);

                        foreach (var item in kvp.Value)
                        {
                            // item: [code, name, province]
                            var newItem = new List<string>(item.Count);
                            foreach (var str in item)
                            {
                                newItem.Add(UIUtils.Intern(str));
                            }
                            lists.Add(newItem);
                        }
                        _db[key] = lists;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CityCodeResolver] Load DB Failed: {ex.Message}");
            }
            finally
            {
                _isLoading = false;
            }
        }
    }
}
