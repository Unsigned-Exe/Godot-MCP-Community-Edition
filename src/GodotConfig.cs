using System;
using System.IO;
using System.Text.Json;

namespace MCP_Godot4
{
    /// <summary>
    /// Configuration manager for Godot Engine integration.
    /// </summary>
    public class Config
    {
        public string? GodotPath { get; set; }
        public int WebSocketPort { get; set; } = 6505;
        public bool AutoStartTray { get; set; } = true;

        private static readonly JsonSerializerOptions s_options = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public static string GetConfigFilePath()
        {
            var exePath = AppDomain.CurrentDomain.BaseDirectory;
            return Path.Combine(exePath, "config.json");
        }

        public static Config Load()
        {
            try
            {
                var path = GetConfigFilePath();
                if (!File.Exists(path))
                    return new Config();

                var json = File.ReadAllText(path);
                var config = JsonSerializer.Deserialize<Config>(json, s_options) ?? new Config();

                if (string.IsNullOrEmpty(config.GodotPath)) config.GodotPath = null;
                if (config.WebSocketPort <= 0) config.WebSocketPort = 6505;

                return config;
            }
            catch
            {
                return new Config();
            }
        }

        public void Save()
        {
            try
            {
                var path = GetConfigFilePath();
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, JsonSerializer.Serialize(this, s_options));
            }
            catch
            {
                // Silently fail - never throw
            }
        }
    }
}
