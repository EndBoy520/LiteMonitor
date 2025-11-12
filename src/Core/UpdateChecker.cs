using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LiteMonitor
{
    public static class UpdateChecker
    {
        private const string VersionUrl = "https://raw.githubusercontent.com/Diorser/LiteMonitor/master/resources/version.json";
        private const string ReleasePage = "https://github.com/Diorser/LiteMonitor/releases/latest";

        public static async Task CheckAsync(bool showMessage = false)
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
                var json = await http.GetStringAsync(VersionUrl);

                using var doc = JsonDocument.Parse(json);
                string? latest = doc.RootElement.GetProperty("version").GetString();

                // 当前版本（读取 <Version>）
                string current = Application.ProductVersion ?? "0.0.0";

                // 去掉 +哈希 后缀
                latest = Normalize(latest);
                current = Normalize(current);

                if (IsNewer(latest, current))
                {
                    if (MessageBox.Show(
                        $"发现新版本：{latest}\n当前版本：{current}\n是否前往下载？",
                        "LiteMonitor 更新",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Information) == DialogResult.Yes)
                    {
                        Process.Start(new ProcessStartInfo(ReleasePage) { UseShellExecute = true });
                    }
                }
                else if (showMessage)
                {
                    MessageBox.Show($"当前已是最新版：{current}", "LiteMonitor", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                if (showMessage)
                    MessageBox.Show("检查更新失败。\n" + ex.Message, "LiteMonitor", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                else
                    Debug.WriteLine("[UpdateChecker] " + ex.Message);
            }
        }

        private static string Normalize(string? version)
        {
            if (string.IsNullOrWhiteSpace(version)) return "0.0.0";
            int plus = version.IndexOf('+');
            return plus >= 0 ? version.Substring(0, plus) : version;
        }

        private static bool IsNewer(string latest, string current)
        {
            if (Version.TryParse(latest, out var v1) && Version.TryParse(current, out var v2))
                return v1 > v2;
            return false;
        }
    }
}
