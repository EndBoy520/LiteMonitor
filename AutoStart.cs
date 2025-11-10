using System;
using System.Diagnostics;
using System.IO;

namespace LiteMonitor
{
    /// <summary>
    /// 自启动管理（通过系统自带 schtasks.exe 创建计划任务）
    /// </summary>
    public static class AutoStart
    {
        private const string TaskName = "LiteMonitor_AutoStart";  // 任务名唯一即可

        /// <summary>
        /// 启用或禁用开机自启
        /// </summary>
        public static void Set(bool enabled)
        {
            if (enabled)
                CreateTask();
            else
                DeleteTask();
        }

        /// <summary>
        /// 检查任务是否存在
        /// </summary>
        public static bool Exists()
        {
            return RunSchtasks($"/Query /TN \"{TaskName}\"", out _) == 0;
        }

        /// <summary>
        /// 创建计划任务：登录触发，以最高权限运行
        /// </summary>
        private static void CreateTask()
        {
            string exePath = Process.GetCurrentProcess().MainModule!.FileName!;
            string quotedPath = $"\"{exePath}\"";

            // 先删除旧任务，避免报错
            RunSchtasks($"/Delete /TN \"{TaskName}\" /F", out _);

            // 当前用户名（例如：TUEUR 或 DOMAIN\TUEUR）
            string user = Environment.UserName;

            // /RL HIGHEST 表示最高权限
            // /IT 表示交互式（必须当前登录用户）
            // /SC ONLOGON 表示用户登录时触发
            // 注意：在 Windows 10/11 家庭版无需密码输入
            string args = $"/Create /TN \"{TaskName}\" /TR {quotedPath} /SC ONLOGON /RL HIGHEST /F /IT /RU \"{user}\"";

            int code = RunSchtasks(args, out string output);
            if (code != 0)
            {
                // 某些系统上 /RU 会要求密码；这时去掉 /RU 再试一次
                args = $"/Create /TN \"{TaskName}\" /TR {quotedPath} /SC ONLOGON /RL HIGHEST /F /IT";
                code = RunSchtasks(args, out output);

                if (code != 0)
                    throw new InvalidOperationException($"创建计划任务失败（退出码 {code}）：\n{output}");
            }
        }

        /// <summary>
        /// 删除任务（忽略不存在的情况）
        /// </summary>
        private static void DeleteTask()
        {
            RunSchtasks($"/Delete /TN \"{TaskName}\" /F", out _);
        }

        /// <summary>
        /// 通用 schtasks 执行器
        /// </summary>
        private static int RunSchtasks(string args, out string output)
        {
            using var p = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WorkingDirectory = AppContext.BaseDirectory
                }
            };
            p.Start();
            string stdOut = p.StandardOutput.ReadToEnd();
            string stdErr = p.StandardError.ReadToEnd();
            p.WaitForExit();
            output = (stdOut + "\n" + stdErr).Trim();
            return p.ExitCode;
        }
    }
}
