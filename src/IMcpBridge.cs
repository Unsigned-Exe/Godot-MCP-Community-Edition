using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

namespace MCP_Godot4
{
    /// <summary>
    /// Contract for MCP tool exposition and execution.
    /// </summary>
    public interface IMcpBridge
    {
        string ServerId { get; }
        string DisplayName { get; }
        string ProtocolServerName { get; }

        IReadOnlyList<object> GetToolDefinitions();
        Task<object> ExecuteToolAsync(string toolName, JsonElement? args);
    }
}
