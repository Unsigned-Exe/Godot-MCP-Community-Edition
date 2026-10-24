using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MCP_Godot4
{
    /// <summary>
    /// Lightweight, high-performance stdio server implementing the Model Context Protocol (MCP)
    /// for Godot Engine 4.x.
    /// </summary>
    public static class GodotMcpServer
    {
        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public static async Task RunAsync(GodotMcpBridge bridge, CancellationToken cancellationToken = default)
        {
            using var reader = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
            using var writer = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };

            while (!cancellationToken.IsCancellationRequested)
            {
                string? line;
                try
                {
                    line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                if (line == null) break;
                if (string.IsNullOrWhiteSpace(line)) continue;

                if (line.Length > 16 * 1024 * 1024)
                {
                    string err = SerializeResponse(null, null, new { code = -32600, message = "Request payload exceeds maximum allowed size (16MB)." });
                    await writer.WriteLineAsync(err).ConfigureAwait(false);
                    continue;
                }

                string? responseJson = await HandleMessageAsync(line, bridge).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(responseJson))
                {
                    await writer.WriteLineAsync(responseJson).ConfigureAwait(false);
                }
            }
        }

        private static async Task<string?> HandleMessageAsync(string rawJson, GodotMcpBridge bridge)
        {
            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(rawJson);
            }
            catch (JsonException ex)
            {
                return SerializeResponse(null, null, new { code = -32700, message = $"Parse error: {ex.Message}" });
            }

            using (doc)
            {
                var root = doc.RootElement;
                object? id = null;
                if (root.TryGetProperty("id", out var idProp))
                {
                    if (idProp.ValueKind == JsonValueKind.Number && idProp.TryGetInt64(out long l)) id = l;
                    else if (idProp.ValueKind == JsonValueKind.String) id = idProp.GetString();
                }

                if (!root.TryGetProperty("method", out var methodProp))
                {
                    return SerializeResponse(id, null, new { code = -32600, message = "Invalid Request: missing 'method'" });
                }

                string method = methodProp.GetString() ?? "";
                JsonElement? paramsElem = root.TryGetProperty("params", out var p) ? p : null;

                try
                {
                    switch (method)
                    {
                        case "initialize":
                            return SerializeResponse(id, new
                            {
                                protocolVersion = "2024-11-05",
                                capabilities = new { tools = new { listChanged = false } },
                                serverInfo = new { name = "Godot-MCP", version = "1.0.0" }
                            }, null);

                        case "notifications/initialized":
                            return null;

                        case "ping":
                            return SerializeResponse(id, new { }, null);

                        case "tools/list":
                            return SerializeResponse(id, new
                            {
                                tools = bridge.GetToolDefinitions()
                            }, null);

                        case "tools/call":
                            {
                                if (paramsElem == null || !paramsElem.Value.TryGetProperty("name", out var toolNameProp))
                                {
                                    return SerializeResponse(id, null, new { code = -32602, message = "Missing 'name' in tool call parameters." });
                                }

                                string toolName = toolNameProp.GetString() ?? "";
                                JsonElement? arguments = paramsElem.Value.TryGetProperty("arguments", out var args) ? args : null;

                                var toolResult = await bridge.ExecuteToolAsync(toolName, arguments).ConfigureAwait(false);
                                return SerializeResponse(id, toolResult, null);
                            }

                        default:
                            return SerializeResponse(id, null, new { code = -32601, message = $"Method '{method}' not found." });
                    }
                }
                catch (Exception ex)
                {
                    return SerializeResponse(id, null, new { code = -32603, message = $"Internal server error: {ex.Message}" });
                }
            }
        }

        private static string SerializeResponse(object? id, object? result, object? error)
        {
            var res = new Dictionary<string, object?> { ["jsonrpc"] = "2.0" };
            if (id != null) res["id"] = id;
            if (error != null) res["error"] = error;
            else res["result"] = result;
            return JsonSerializer.Serialize(res, JsonOpts);
        }
    }
}
