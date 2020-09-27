using Microsoft.WindowsAPICodePack.Shell;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace CapFrameX.Updater
{
    class Program
    {
        private const string FROMTEMPPARAMNAME = "from-temp";
        private static Downloader _downloader = new Downloader();
        static async Task Main(string[] args)
        {
            try
            {
                var parameters = BuildParameters(args);
                if (parameters.TryGetValue(FROMTEMPPARAMNAME, out var fromTemp) && fromTemp == "true")
                {
                    StartFromTmpFolder(parameters);
                }
                Console.WriteLine("Welcome to CapFrameX Updater");

                var installationType = parameters["type"];
                Version version = parameters.TryGetValue("version", out var versionString) ? new Version(versionString) : new Version(await _downloader.DetermineLatestVersion());

                Console.WriteLine($"Downloading CapFrameX {installationType} v{version}");

                await Run(version, installationType, parameters.TryGetValue("cx-directory", out var originalExecutingDirectory) ? originalExecutingDirectory : Environment.CurrentDirectory);

                Console.WriteLine("Done");
            }
            catch (Exception exc)
            {
                Console.Error.WriteLine(exc);
            }
            finally
            {
                Console.WriteLine("Press any Key to close.");
                Console.ReadKey(); // keep console open
            }

            async Task Run(Version version, string installationType, string executingDirectory)
            {
                var file = await _downloader.DownloadVersion(version, installationType);

                var downloadPath = Path.Combine(KnownFolders.Downloads.Path, file.Filename);
                File.WriteAllBytes(downloadPath, file.Content);

                if (installationType.Equals("setup", StringComparison.OrdinalIgnoreCase))
                {
                    if (downloadPath.EndsWith("zip"))
                    {
                        var extractionPath = Path.Combine(Path.GetTempPath(), "CapframeX_Updater", "releases");
                        ZipFile.ExtractToDirectory(downloadPath, extractionPath);
                        Process.Start(Directory.GetFiles(extractionPath).First());
                    }
                    else
                    {
                        Process.Start(downloadPath);
                    }
                }
                else if (installationType.Equals("portable", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var proc in Process.GetProcesses().Where(p => p.ProcessName.Equals("capframex", StringComparison.OrdinalIgnoreCase) || p.ProcessName.IndexOf("presentmon", StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        Console.WriteLine($"Killing Process {proc.ProcessName}");
                        proc.Kill();
                    }
                    await Task.Delay(3000);
                    using (ZipArchive archive = ZipFile.Open(downloadPath, ZipArchiveMode.Read))
                    {
                        Console.WriteLine($"Extracting to {executingDirectory}");
                        var foldername = file.Filename.Replace(".zip", string.Empty);
                        foreach (var entry in archive.Entries)
                        {
                            var targetFilename = Path.Combine(executingDirectory, entry.FullName.Replace($"{foldername}/", string.Empty));
                            if (entry.FullName.EndsWith("/"))
                            {
                                Directory.CreateDirectory(targetFilename);
                                continue;
                            }
                            Console.WriteLine($"Extracting to {targetFilename}");
                            entry.ExtractToFile(targetFilename, true);
                        }
                    }
                }

                File.Delete(downloadPath);
            }
        }

        private static Dictionary<string, string> BuildParameters(string[] args)
        {
            var argLine = string.Join(" ", args);
            var splitRegex = new Regex(@"-(?<param>[\w-]+)\s(?<value>[\w\d.-\\:\/]+)");
            var matches = splitRegex.Matches(argLine).Cast<Match>();
            return matches.ToDictionary(m => m.Groups["param"].Value, m => m.Groups["value"].Value);
        }

        private static void StartFromTmpFolder(Dictionary<string, string> parameters)
        {
            var executingDirectory = Environment.CurrentDirectory;
            var tmpPathTarget = Path.Combine(Path.GetTempPath(), "CX_Updater", "updater");
            Directory.CreateDirectory(tmpPathTarget);
            foreach (var filePath in Directory.GetFiles(executingDirectory))
            {
                var fileInfo = new FileInfo(filePath);
                File.Copy(fileInfo.FullName, Path.Combine(tmpPathTarget, fileInfo.Name), true);
            }

            var parametersLine = string.Join(" ", parameters.Where(kvp => !kvp.Key.Equals(FROMTEMPPARAMNAME)).Select(kvp => $"-{kvp.Key} {kvp.Value}"));
            var newProcessFilename = Environment.GetCommandLineArgs()[0].Replace(executingDirectory, tmpPathTarget);
            var process = Process.Start(newProcessFilename, parametersLine);
            Environment.Exit(0);
        }
    }
}
