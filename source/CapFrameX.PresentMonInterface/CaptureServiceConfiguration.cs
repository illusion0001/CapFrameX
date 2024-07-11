using CapFrameX.Capture.Contracts;
using System;
using System.IO;

namespace CapFrameX.PresentMonInterface
{
    public static class CaptureServiceConfiguration
    {
        public static string PresentMonAppName = "PresentMon-2.2.0-x64";

        public static IServiceStartInfo GetServiceStartInfo(string arguments)
        {
            var startInfo = new PresentMonStartInfo
            {
                FileName = Path.Combine("PresentMon", PresentMonAppName + ".exe"),
                Arguments = arguments,
                CreateNoWindow = true,
                RunWithAdminRights = true,
                RedirectStandardOutput = false,
                UseShellExecute = false
            };

            return startInfo;
        }

        public static string GetCaptureFilename(string processName, string cpuName, string gpuName)
        {
            DateTime now = DateTime.Now;
            string dateTimeFormat = $"{now.Year}-{now.Month:d2}-{now.Day:d2}-{now.Hour:d2}-{now.Minute:d2}-{now.Second:d2}";
            string customProfileFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), @"CapFrameX\Profile.txt");
            string filename = $"CapFrameX-{processName}.exe-{dateTimeFormat}.json";
            if (File.Exists(customProfileFilePath) && cpuName != null && gpuName != null)
            {
                string[] lines = File.ReadAllLines(customProfileFilePath);
                string DataLine = $"{(lines.Length > 0 ? lines[0] : "")}";
                filename = $"{DataLine}-{cpuName}-{gpuName}-CapFrameX-{processName}.exe-{dateTimeFormat}.json";
            }
            return filename.Replace(" ", "-");
        }
    }
}
