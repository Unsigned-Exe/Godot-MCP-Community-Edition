using System;
using System.Diagnostics;
using System.IO;

namespace MCP_Godot4
{
    /// <summary>
    /// Result of a Godot Engine detection attempt.
    /// </summary>
    public class GodotDetectionResult
    {
        public bool IsFound { get; set; }
        public string ExecutablePath { get; set; } = "";
        public string Version { get; set; } = "";
    }

    /// <summary>
    /// Discovers the Godot Engine executable and retrieves its version.
    /// </summary>
    public static class GodotDiscovery
    {
        private const string VersionCommand = "--version";
        private const int TimeoutSeconds = 2;

        public static GodotDetectionResult Detect(string? customPath = null)
        {
            try
            {
                // 1. Custom path provided by caller
                if (!string.IsNullOrEmpty(customPath) && File.Exists(customPath))
                {
                    return ValidateAndDetect(customPath);
                }

                // 2. Config file
                var config = Config.Load();
                if (!string.IsNullOrEmpty(config.GodotPath) && File.Exists(config.GodotPath))
                {
                    return ValidateAndDetect(config.GodotPath);
                }

                // 3. Environment variable GODOT_PATH
                var envPath = Environment.GetEnvironmentVariable("GODOT_PATH");
                if (!string.IsNullOrEmpty(envPath) && File.Exists(envPath))
                {
                    return ValidateAndDetect(envPath);
                }

                // 4. Standard Windows installation paths
                var standardPaths = new[]
                {
                    @"C:\Apps\Godot\Godot_v4.7-stable_win64_console.exe",
                    @"C:\Apps\Godot\godot.exe",
                    @"C:\Apps\Godot\Godot_v4.7-stable_win64.exe",
                    @"C:\Program Files\Godot\godot.exe",
                    @"C:\Program Files (x86)\Steam\steamapps\common\Godot Engine\godot.exe"
                };

                foreach (var path in standardPaths)
                {
                    if (File.Exists(path))
                    {
                        return ValidateAndDetect(path);
                    }
                }

                // 5. Scan PATH environment variable for godot.exe or godot4.exe
                var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
                var pathSegments = pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

                foreach (var segment in pathSegments)
                {
                    try
                    {
                        var candidate1 = Path.Combine(segment, "godot.exe");
                        if (File.Exists(candidate1)) return ValidateAndDetect(candidate1);

                        var candidate2 = Path.Combine(segment, "godot4.exe");
                        if (File.Exists(candidate2)) return ValidateAndDetect(candidate2);
                    }
                    catch
                    {
                        continue;
                    }
                }

                return new GodotDetectionResult();
            }
            catch
            {
                return new GodotDetectionResult();
            }
        }

        private static GodotDetectionResult ValidateAndDetect(string executablePath)
        {
            var version = GetVersionFromProcess(executablePath);
            return new GodotDetectionResult
            {
                IsFound = true,
                ExecutablePath = executablePath,
                Version = version ?? ""
            };
        }

        private static string? GetVersionFromProcess(string executablePath)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = executablePath,
                    Arguments = VersionCommand,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                if (process == null) return null;

                if (!process.WaitForExit(TimeoutSeconds * 1000))
                {
                    try { process.Kill(); } catch { }
                    return null;
                }

                if (process.ExitCode != 0) return null;

                var output = process.StandardOutput.ReadToEnd().Trim();
                return output;
            }
            catch
            {
                return null;
            }
        }
    }
}
