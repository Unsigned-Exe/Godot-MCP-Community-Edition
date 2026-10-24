using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace MCP_Godot4
{
    /// <summary>
    /// Crash-resilient project and disk manager for Godot 4.
    /// Manages project creation, script authoring, scene generation, and engine launching.
    /// </summary>
    public static class GodotProjectManager
    {
        public static string? ActiveProjectPath { get; set; }

        public static string ResolveProjectPath(string? projectDir)
        {
            if (!string.IsNullOrWhiteSpace(projectDir))
            {
                if (Directory.Exists(projectDir)) return Path.GetFullPath(projectDir);

                if (!string.IsNullOrEmpty(ActiveProjectPath))
                {
                    string candidate = Path.Combine(ActiveProjectPath, projectDir.TrimStart('/', '\\'));
                    if (Directory.Exists(candidate)) return Path.GetFullPath(candidate);
                }
            }

            if (!string.IsNullOrEmpty(ActiveProjectPath) && Directory.Exists(ActiveProjectPath))
            {
                return Path.GetFullPath(ActiveProjectPath);
            }

            return !string.IsNullOrWhiteSpace(projectDir) ? Path.GetFullPath(projectDir) : Directory.GetCurrentDirectory();
        }

        public static string ResolveFilePath(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return "";

            // Normalize Godot res:// URI
            if (filePath.StartsWith("res://", StringComparison.OrdinalIgnoreCase))
            {
                string rel = filePath.Substring(6).TrimStart('/', '\\');
                if (!string.IsNullOrEmpty(ActiveProjectPath))
                {
                    return Path.GetFullPath(Path.Combine(ActiveProjectPath, rel));
                }
                return Path.GetFullPath(rel);
            }

            if (Path.IsPathRooted(filePath))
            {
                return filePath;
            }

            if (!string.IsNullOrEmpty(ActiveProjectPath))
            {
                return Path.GetFullPath(Path.Combine(ActiveProjectPath, filePath));
            }

            return Path.GetFullPath(filePath);
        }

        public static Dictionary<string, object?> CreateProject(string projectDir, string projectName)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(projectName))
                    return new Dictionary<string, object?> { ["success"] = false, ["error"] = "Project name cannot be empty." };

                var dir = ResolveProjectPath(projectDir);
                Directory.CreateDirectory(dir);

                var godotPath = Path.Combine(dir, "project.godot");
                var content = $@"; Engine configuration file.
; It's best edited using the editor UI and not directly.
config_version=5

[application]

config/name=""{projectName}""
config/features=PackedStringArray(""4.3"", ""Forward Plus"")
";

                File.WriteAllText(godotPath, content, Encoding.UTF8);

                return new Dictionary<string, object?>
                {
                    ["success"] = true,
                    ["message"] = $"Project created successfully at: {dir}",
                    ["projectDir"] = dir,
                    ["godotPath"] = godotPath
                };
            }
            catch (Exception ex)
            {
                return new Dictionary<string, object?>
                {
                    ["success"] = false,
                    ["error"] = $"Failed to create project: {ex.Message}"
                };
            }
        }

        public static Dictionary<string, object?> LaunchEditor(string projectDir)
        {
            try
            {
                var detection = GodotDiscovery.Detect();
                if (!detection.IsFound || string.IsNullOrEmpty(detection.ExecutablePath))
                {
                    return new Dictionary<string, object?>
                    {
                        ["success"] = false,
                        ["error"] = "Godot executable was not found. Please configure path in Setup / Config."
                    };
                }

                var fullDir = ResolveProjectPath(projectDir);
                var psi = new ProcessStartInfo
                {
                    FileName = detection.ExecutablePath,
                    Arguments = $"-e --path \"{fullDir}\"",
                    UseShellExecute = true
                };

                using var proc = Process.Start(psi);
                return new Dictionary<string, object?>
                {
                    ["success"] = true,
                    ["message"] = $"Launched Godot editor for '{fullDir}'",
                    ["pid"] = proc?.Id
                };
            }
            catch (Exception ex)
            {
                return new Dictionary<string, object?>
                {
                    ["success"] = false,
                    ["error"] = $"Failed to launch editor: {ex.Message}"
                };
            }
        }

        public static Dictionary<string, object?> RunProject(string projectDir)
        {
            try
            {
                var detection = GodotDiscovery.Detect();
                if (!detection.IsFound || string.IsNullOrEmpty(detection.ExecutablePath))
                {
                    return new Dictionary<string, object?>
                    {
                        ["success"] = false,
                        ["error"] = "Godot executable was not found."
                    };
                }

                var fullDir = ResolveProjectPath(projectDir);
                var psi = new ProcessStartInfo
                {
                    FileName = detection.ExecutablePath,
                    Arguments = $"--path \"{fullDir}\" -w -t --position 200,200",
                    UseShellExecute = true
                };

                using var proc = Process.Start(psi);
                return new Dictionary<string, object?>
                {
                    ["success"] = true,
                    ["message"] = $"Launched game project for '{fullDir}'",
                    ["pid"] = proc?.Id
                };
            }
            catch (Exception ex)
            {
                return new Dictionary<string, object?>
                {
                    ["success"] = false,
                    ["error"] = $"Failed to run project: {ex.Message}"
                };
            }
        }

        public static Dictionary<string, object?> CheckSyntax(string projectDir)
        {
            try
            {
                var detection = GodotDiscovery.Detect();
                if (!detection.IsFound || string.IsNullOrEmpty(detection.ExecutablePath))
                {
                    return new Dictionary<string, object?>
                    {
                        ["success"] = false,
                        ["error"] = "Godot executable was not found."
                    };
                }

                var fullDir = ResolveProjectPath(projectDir);
                var psi = new ProcessStartInfo
                {
                    FileName = detection.ExecutablePath,
                    Arguments = $"--headless --check-only --path \"{fullDir}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(psi);
                if (proc == null)
                {
                    return new Dictionary<string, object?> { ["success"] = false, ["error"] = "Failed to start process." };
                }

                if (!proc.WaitForExit(8000))
                {
                    try { proc.Kill(); } catch { }
                    return new Dictionary<string, object?> { ["success"] = false, ["error"] = "Syntax check timed out after 8s." };
                }

                string stdout = proc.StandardOutput.ReadToEnd();
                string stderr = proc.StandardError.ReadToEnd();
                string combined = $"{stdout}\n{stderr}".Trim();

                bool hasErrors = proc.ExitCode != 0 || combined.Contains("SCRIPT ERROR", StringComparison.OrdinalIgnoreCase);

                return new Dictionary<string, object?>
                {
                    ["success"] = !hasErrors,
                    ["exitCode"] = proc.ExitCode,
                    ["hasErrors"] = hasErrors,
                    ["output"] = combined
                };
            }
            catch (Exception ex)
            {
                return new Dictionary<string, object?>
                {
                    ["success"] = false,
                    ["error"] = $"Syntax check failed: {ex.Message}"
                };
            }
        }

        public static Dictionary<string, object?> ReadFile(string filePath)
        {
            try
            {
                filePath = ResolveFilePath(filePath);
                if (!File.Exists(filePath))
                    return new Dictionary<string, object?> { ["success"] = false, ["error"] = $"File not found: {filePath}" };

                var content = File.ReadAllText(filePath, Encoding.UTF8);
                var lines = content.Split('\n').Length;

                return new Dictionary<string, object?>
                {
                    ["success"] = true,
                    ["content"] = content,
                    ["lineCount"] = lines,
                    ["sizeBytes"] = new FileInfo(filePath).Length
                };
            }
            catch (Exception ex)
            {
                return new Dictionary<string, object?>
                {
                    ["success"] = false,
                    ["error"] = $"Failed to read file: {ex.Message}"
                };
            }
        }

        public static Dictionary<string, object?> WriteFile(string filePath, string content, bool overwrite = true)
        {
            try
            {
                filePath = ResolveFilePath(filePath);
                if (!overwrite && File.Exists(filePath))
                    return new Dictionary<string, object?> { ["success"] = false, ["error"] = $"File already exists: {filePath}" };

                var dir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                File.WriteAllText(filePath, content, Encoding.UTF8);

                return new Dictionary<string, object?>
                {
                    ["success"] = true,
                    ["message"] = $"File written successfully: {filePath}",
                    ["sizeBytes"] = new FileInfo(filePath).Length
                };
            }
            catch (Exception ex)
            {
                return new Dictionary<string, object?>
                {
                    ["success"] = false,
                    ["error"] = $"Failed to write file: {ex.Message}"
                };
            }
        }

        public static Dictionary<string, object?> DeleteFile(string filePath, bool removeImports = true)
        {
            try
            {
                filePath = ResolveFilePath(filePath);
                if (!File.Exists(filePath) && !Directory.Exists(filePath))
                    return new Dictionary<string, object?> { ["success"] = false, ["error"] = $"Path not found: {filePath}" };

                if (File.Exists(filePath))
                {
                    File.Delete(filePath);

                    if (removeImports)
                    {
                        var importFile = $"{filePath}.import";
                        var uidFile = $"{filePath}.uid";
                        if (File.Exists(importFile)) File.Delete(importFile);
                        if (File.Exists(uidFile)) File.Delete(uidFile);
                    }
                }
                else if (Directory.Exists(filePath))
                {
                    Directory.Delete(filePath, recursive: true);
                }

                return new Dictionary<string, object?>
                {
                    ["success"] = true,
                    ["message"] = $"Deleted: {filePath}"
                };
            }
            catch (Exception ex)
            {
                return new Dictionary<string, object?>
                {
                    ["success"] = false,
                    ["error"] = $"Failed to delete: {ex.Message}"
                };
            }
        }

        public static Dictionary<string, object?> ListFiles(string projectDir, string? extensionFilter = null)
        {
            try
            {
                var baseDir = ResolveProjectPath(projectDir);
                if (!Directory.Exists(baseDir))
                    return new Dictionary<string, object?> { ["success"] = false, ["error"] = $"Directory not found: {baseDir}" };

                var files = new List<string>();

                foreach (var path in Directory.EnumerateFiles(baseDir, "*.*", SearchOption.AllDirectories))
                {
                    string relPath = Path.GetRelativePath(baseDir, path).Replace('\\', '/');

                    // Skip internal engine cache, git, and addons directories so AI context stays lean and focused on user game files
                    if (relPath.StartsWith(".godot/", StringComparison.OrdinalIgnoreCase) ||
                        relPath.StartsWith(".git/", StringComparison.OrdinalIgnoreCase) ||
                        relPath.StartsWith("addons/", StringComparison.OrdinalIgnoreCase) ||
                        relPath.StartsWith("."))
                    {
                        continue;
                    }

                    var fi = new FileInfo(path);
                    var ext = fi.Extension.TrimStart('.').ToLowerInvariant();

                    // Skip Godot auto-generated metadata files (.uid, .import)
                    if (ext is "uid" or "import")
                        continue;

                    if (!string.IsNullOrEmpty(extensionFilter))
                    {
                        var filter = extensionFilter.TrimStart('.').ToLowerInvariant();
                        if (!ext.Equals(filter, StringComparison.OrdinalIgnoreCase))
                            continue;
                    }

                    files.Add($"res://{relPath}");
                }

                return new Dictionary<string, object?>
                {
                    ["success"] = true,
                    ["projectDir"] = baseDir,
                    ["totalFiles"] = files.Count,
                    ["files"] = files
                };
            }
            catch (Exception ex)
            {
                return new Dictionary<string, object?>
                {
                    ["success"] = false,
                    ["error"] = $"Failed to list files: {ex.Message}"
                };
            }
        }

        public static Dictionary<string, object?> EditScriptSnippet(string filePath, string targetSnippet, string replacementSnippet)
        {
            try
            {
                filePath = ResolveFilePath(filePath);
                if (!File.Exists(filePath))
                    return new Dictionary<string, object?> { ["success"] = false, ["error"] = $"File not found: {filePath}" };

                var text = File.ReadAllText(filePath, Encoding.UTF8);
                if (!text.Contains(targetSnippet))
                {
                    return new Dictionary<string, object?>
                    {
                        ["success"] = false,
                        ["error"] = "Target snippet not found in the file."
                    };
                }

                var updated = text.Replace(targetSnippet, replacementSnippet);
                File.WriteAllText(filePath, updated, Encoding.UTF8);

                return new Dictionary<string, object?>
                {
                    ["success"] = true,
                    ["message"] = $"Successfully edited snippet in {filePath}"
                };
            }
            catch (Exception ex)
            {
                return new Dictionary<string, object?>
                {
                    ["success"] = false,
                    ["error"] = $"Failed to edit script: {ex.Message}"
                };
            }
        }

        public static Dictionary<string, object?> AppendToScript(string filePath, string codeToAppend)
        {
            try
            {
                filePath = ResolveFilePath(filePath);
                if (!File.Exists(filePath))
                    return new Dictionary<string, object?> { ["success"] = false, ["error"] = $"File not found: {filePath}" };

                using var sw = File.AppendText(filePath);
                sw.WriteLine();
                sw.WriteLine(codeToAppend);

                return new Dictionary<string, object?>
                {
                    ["success"] = true,
                    ["message"] = $"Successfully appended code to {filePath}"
                };
            }
            catch (Exception ex)
            {
                return new Dictionary<string, object?>
                {
                    ["success"] = false,
                    ["error"] = $"Failed to append to script: {ex.Message}"
                };
            }
        }

        public static Dictionary<string, object?> CreateSceneTscn(string sceneFilePath, string rootNodeName = "Main", string rootNodeType = "Node2D", string? scriptResPath = null)
        {
            try
            {
                sceneFilePath = ResolveFilePath(sceneFilePath);
                var dir = Path.GetDirectoryName(sceneFilePath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                var sb = new StringBuilder();
                sb.AppendLine("[gd_scene format=3 uid=\"uid://temp\"]");
                sb.AppendLine();

                if (!string.IsNullOrEmpty(scriptResPath))
                {
                    if (!scriptResPath.StartsWith("res://", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!string.IsNullOrEmpty(ActiveProjectPath) && scriptResPath.StartsWith(ActiveProjectPath, StringComparison.OrdinalIgnoreCase))
                        {
                            scriptResPath = "res://" + Path.GetRelativePath(ActiveProjectPath, scriptResPath).Replace('\\', '/');
                        }
                        else
                        {
                            scriptResPath = "res://" + scriptResPath.TrimStart('/', '\\').Replace('\\', '/');
                        }
                    }
                    sb.AppendLine($"[ext_resource type=\"Script\" path=\"{scriptResPath}\" id=\"1_script\"]");
                    sb.AppendLine();
                    sb.AppendLine($"[node name=\"{rootNodeName}\" type=\"{rootNodeType}\"]");
                    sb.AppendLine("script = ExtResource(\"1_script\")");
                }
                else
                {
                    sb.AppendLine($"[node name=\"{rootNodeName}\" type=\"{rootNodeType}\"]");
                }

                File.WriteAllText(sceneFilePath, sb.ToString(), Encoding.UTF8);

                return new Dictionary<string, object?>
                {
                    ["success"] = true,
                    ["message"] = $"Created scene: {sceneFilePath}",
                    ["rootNode"] = rootNodeName,
                    ["rootType"] = rootNodeType
                };
            }
            catch (Exception ex)
            {
                return new Dictionary<string, object?>
                {
                    ["success"] = false,
                    ["error"] = $"Failed to create scene: {ex.Message}"
                };
            }
        }
    }
}
