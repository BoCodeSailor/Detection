using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ClassLibrary1.Model
{
    internal class Logger
    {
        private static readonly string LogFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "RevitSoftCollision.log");
        public static void ClearLog()
        {
            try
            {
                // 如果文件存在，则清空文件内容
                if (File.Exists(LogFilePath))
                {
                    File.WriteAllText(LogFilePath, string.Empty);
                }
            }
            catch (Exception ex)
            {
                // 可选：写入失败时输出到调试窗口
                System.Diagnostics.Debug.WriteLine("[Logger ERROR] " + ex.Message);
            }
        }
        public static void Log(string message)
        {
            try
            {
                using (StreamWriter writer = File.AppendText(LogFilePath))
                {
                    writer.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} - {message}");
                }
            }
            catch (Exception ex)
            {
                // 可选：写入失败时输出到调试窗口
                System.Diagnostics.Debug.WriteLine("[Logger ERROR] " + ex.Message);
            }
        }

        public static void LogSeparator(string title = "")
        {
            Log("--------------------------------------------------------");
            if (!string.IsNullOrEmpty(title))
            {
                Log(">>> " + title);
            }
        }
    }
}
