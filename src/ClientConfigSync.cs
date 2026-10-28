#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Win32;

namespace MCP_Godot4
{
    /// <summary>
    /// Streamlined MCP configuration synchronizer for Ollama, LM Studio, and Claude Desktop.
    /// Injects the portable GodotMCP server into each assistant's configuration file.
    /// </summary>
    public static class ClientConfigSync
    {
        public const string ClientClaude = "Claude";
        public const string ClientLmStudio = "LMStudio";
        public const string ClientOllama = "Ollama";

        public static readonly IReadOnlyList<string> SupportedClients = new[]
        {
            ClientClaude,
            ClientLmStudio,
            ClientOllama
        };

        private static readonly Dictionary<string, string> ConfigPaths = new(StringComparer.OrdinalIgnoreCase)
        {
            [ClientClaude] = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Claude", "claude_desktop_config.json"),

            [ClientLmStudio] = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".lmstudio", "mcp.json"),

            [ClientOllama] = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".ollama", "mcp.json")
        };

        private static readonly string LegacyLmStudioConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".cache", "lm-studio", "mcp.json");

        public static string GetConfigPath(string client)
        {
            if (ConfigPaths.TryGetValue(client, out var path)) return path;
            return "";
        }

        public static bool IsClientInstalled(string client)
        {
            string path = GetConfigPath(client);
            if (string.IsNullOrEmpty(path)) return false;

            // Installed if config file exists or parent application folder exists
            string? dir = Path.GetDirectoryName(path);
            return File.Exists(path) || (dir != null && Directory.Exists(dir));
        }

        public static bool IsServerRegistered(string client, string serverKey = "godot-4-7")
        {
            try
            {
                string path = GetConfigPath(client);
                if (!File.Exists(path)) return false;

                string json = File.ReadAllText(path);
                var doc = JsonNode.Parse(json);
                if (doc == null) return false;

                var servers = doc["mcpServers"] as JsonObject;
                return servers != null && servers.ContainsKey(serverKey);
            }
            catch
            {
                return false;
            }
        }

        public static Dictionary<string, string> GetConfiguredEnvironment()
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\GodotMCP\Env", false);
                if (key != null)
                {
                    foreach (var valName in key.GetValueNames())
                    {
                        var val = key.GetValue(valName)?.ToString();
                        if (val != null)
                        {
                            dict[valName] = val;
                        }
                    }
                }
            }
            catch { }

            // If empty, also check MCP-Suite registry if available
            if (dict.Count == 0)
            {
                try
                {
                    using var suiteKey = Registry.CurrentUser.OpenSubKey(@"Software\MCP-Suite\Servers\godot-4-7\Env", false);
                    if (suiteKey != null)
                    {
                        foreach (var valName in suiteKey.GetValueNames())
                        {
                            var val = suiteKey.GetValue(valName)?.ToString();
                            if (val != null)
                            {
                                dict[valName] = val;
                            }
                        }
                    }
                }
                catch { }
            }

            return dict;
        }

        public static void SaveConfiguredEnvironment(IReadOnlyDictionary<string, string> env)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(@"Software\GodotMCP\Env", true);
                if (key != null)
                {
                    foreach (var name in key.GetValueNames())
                    {
                        if (!env.ContainsKey(name))
                        {
                            key.DeleteValue(name, false);
                        }
                    }

                    foreach (var kvp in env)
                    {
                        if (!string.IsNullOrWhiteSpace(kvp.Key))
                        {
                            key.SetValue(kvp.Key, kvp.Value ?? "");
                        }
                    }
                }
            }
            catch { }
        }

        public static bool RegisterGodotServer(string client, string? customExePath = null, IReadOnlyDictionary<string, string>? envVars = null)
        {
            try
            {
                string path = GetConfigPath(client);
                if (string.IsNullOrEmpty(path)) return false;

                string? dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                string exePath = customExePath ?? Environment.ProcessPath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "GodotMCP.exe");

                JsonObject root;
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    root = JsonNode.Parse(json) as JsonObject ?? new JsonObject();
                }
                else
                {
                    root = new JsonObject();
                }

                if (root["mcpServers"] is not JsonObject mcpServers)
                {
                    mcpServers = new JsonObject();
                    root["mcpServers"] = mcpServers;
                }

                // Register godot-4-7 tool server
                var godotEntry = new JsonObject
                {
                    ["command"] = exePath,
                    ["args"] = new JsonArray { "--server" }
                };

                // Inject environment variables if specified or configured
                var effectiveEnv = envVars ?? GetConfiguredEnvironment();
                if (effectiveEnv != null && effectiveEnv.Count > 0)
                {
                    var envObj = new JsonObject();
                    foreach (var (k, v) in effectiveEnv)
                    {
                        if (!string.IsNullOrWhiteSpace(k))
                        {
                            envObj[k.Trim()] = v ?? "";
                        }
                    }
                    if (envObj.Count > 0)
                    {
                        godotEntry["env"] = envObj;
                    }
                }

                mcpServers["godot-4-7"] = godotEntry;

                var opts = new JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(path, root.ToJsonString(opts));

                // Also sync legacy LM Studio cache path if applicable
                if (string.Equals(client, ClientLmStudio, StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        string? legacyDir = Path.GetDirectoryName(LegacyLmStudioConfigPath);
                        if (!string.IsNullOrEmpty(legacyDir) && Directory.Exists(legacyDir))
                        {
                            File.WriteAllText(LegacyLmStudioConfigPath, root.ToJsonString(opts));
                        }
                    }
                    catch { }
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool UnregisterGodotServer(string client, string serverKey = "godot-4-7")
        {
            try
            {
                string path = GetConfigPath(client);
                if (!File.Exists(path)) return false;

                string json = File.ReadAllText(path);
                var root = JsonNode.Parse(json) as JsonObject;
                if (root == null) return false;

                if (root["mcpServers"] is JsonObject mcpServers && mcpServers.ContainsKey(serverKey))
                {
                    mcpServers.Remove(serverKey);
                    var opts = new JsonSerializerOptions { WriteIndented = true };
                    File.WriteAllText(path, root.ToJsonString(opts));
                    return true;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }
    }
}
