using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MCP_Godot4
{
    /// <summary>
    /// High-performance WebSocket bridge to a Godot Editor plugin using JSON-RPC 2.0.
    /// Listens for WebSocket connections from the editor and dispatches commands.
    /// </summary>
    public class GodotBridge : IDisposable
    {
        private HttpListener? _listener;
        private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pendingRequests = new();
        private readonly object _lockObj = new();
        private int _nextId;
        private WebSocket? _activeSocket;
        private CancellationTokenSource? _cts;
        private bool _disposed;

        public bool IsConnected => _activeSocket != null && _activeSocket.State == WebSocketState.Open;
        public int BoundPort { get; private set; }
        public string? ConnectedProjectPath { get; private set; }
        public string? ConnectedProjectName { get; private set; }

        public GodotBridge(int initialPort = 6505)
        {
            BoundPort = BindAvailablePort(initialPort);
        }

        private int BindAvailablePort(int startPort)
        {
            for (int p = startPort; p < startPort + 10; p++)
            {
                try
                {
                    var listener = new HttpListener();
                    listener.Prefixes.Add($"http://127.0.0.1:{p}/");
                    listener.Start();
                    _listener = listener;
                    return p;
                }
                catch
                {
                    _listener?.Close();
                    _listener = null;
                }
            }

            // Fallback to initial port
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://127.0.0.1:{startPort}/");
            try { _listener.Start(); } catch { }
            return startPort;
        }

        public void Start()
        {
            if (_disposed) return;
            _cts = new CancellationTokenSource();

            Task.Run(ListenLoopAsync);
        }

        private async Task ListenLoopAsync()
        {
            while (!_disposed && _listener != null && _listener.IsListening)
            {
                try
                {
                    var context = await _listener.GetContextAsync().ConfigureAwait(false);

                    if (context.Request.IsWebSocketRequest)
                    {
                        var wsContext = await context.AcceptWebSocketAsync(subProtocol: null).ConfigureAwait(false);
                        lock (_lockObj)
                        {
                            try { _activeSocket?.Dispose(); } catch { }
                            _activeSocket = wsContext.WebSocket;
                        }

                        _ = Task.Run(() => ReceiveLoopAsync(wsContext.WebSocket));
                        _ = Task.Run(QueryConnectedProjectInfoAsync);
                    }
                    else
                    {
                        context.Response.StatusCode = 400;
                        context.Response.Close();
                    }
                }
                catch when (_disposed)
                {
                    break;
                }
                catch (Exception)
                {
                    await Task.Delay(500).ConfigureAwait(false);
                }
            }
        }

        private async Task QueryConnectedProjectInfoAsync()
        {
            try
            {
                await Task.Delay(250).ConfigureAwait(false);
                if (!IsConnected) return;

                var resp = await CallGodotAsync("get_project_info", new(), 3000).ConfigureAwait(false);
                if (resp.TryGetValue("result", out var rObj) && rObj is JsonElement elem && elem.ValueKind == JsonValueKind.Object)
                {
                    if (elem.TryGetProperty("project_path", out var pp) && pp.ValueKind == JsonValueKind.String)
                    {
                        string rawPath = pp.GetString() ?? "";
                        if (!string.IsNullOrWhiteSpace(rawPath))
                        {
                            string cleanPath = Path.GetFullPath(rawPath.TrimEnd('/', '\\'));
                            if (Directory.Exists(cleanPath))
                            {
                                ConnectedProjectPath = cleanPath;
                                GodotProjectManager.ActiveProjectPath = cleanPath;
                            }
                        }
                    }

                    if (elem.TryGetProperty("project_name", out var pn) && pn.ValueKind == JsonValueKind.String)
                    {
                        ConnectedProjectName = pn.GetString();
                    }
                }
            }
            catch { }
        }

        private async Task ReceiveLoopAsync(WebSocket socket)
        {
            var buffer = new byte[16384];
            using var ms = new MemoryStream();

            while (!_disposed && socket.State == WebSocketState.Open)
            {
                try
                {
                    WebSocketReceiveResult result;
                    ms.SetLength(0);

                    do
                    {
                        result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None).ConfigureAwait(false);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed", CancellationToken.None).ConfigureAwait(false);
                            return;
                        }

                        ms.Write(buffer, 0, result.Count);
                    }
                    while (!result.EndOfMessage);

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        var json = Encoding.UTF8.GetString(ms.ToArray());
                        ProcessIncomingMessage(json);
                    }
                }
                catch
                {
                    break;
                }
            }
        }

        private void ProcessIncomingMessage(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("id", out var idProp) && idProp.TryGetInt32(out int id))
                {
                    if (_pendingRequests.TryRemove(id, out var tcs))
                    {
                        tcs.TrySetResult(doc.RootElement.Clone());
                    }
                }
            }
            catch { }
        }

        public async Task<Dictionary<string, object?>> CallGodotAsync(
            string method,
            Dictionary<string, object?>? parameters = null,
            int timeoutMs = 8000)
        {
            if (!IsConnected || _activeSocket == null)
            {
                return new Dictionary<string, object?>
                {
                    ["success"] = false,
                    ["error"] = $"Godot Editor plugin is not connected. Make sure Godot is running and 'godot_mcp' plugin is enabled on port {BoundPort}."
                };
            }

            int reqId = Interlocked.Increment(ref _nextId);
            var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingRequests[reqId] = tcs;

            var payload = new Dictionary<string, object?>
            {
                ["jsonrpc"] = "2.0",
                ["id"] = reqId,
                ["method"] = method,
                ["params"] = parameters ?? new()
            };

            var jsonBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));

            try
            {
                await _activeSocket.SendAsync(
                    new ArraySegment<byte>(jsonBytes),
                    WebSocketMessageType.Text,
                    true,
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _pendingRequests.TryRemove(reqId, out _);
                return new Dictionary<string, object?>
                {
                    ["success"] = false,
                    ["error"] = $"Failed to transmit message to Godot: {ex.Message}"
                };
            }

            using var timeoutCts = new CancellationTokenSource(timeoutMs);

            try
            {
                using (timeoutCts.Token.Register(() => tcs.TrySetCanceled()))
                {
                    var response = await tcs.Task.ConfigureAwait(false);

                    var res = new Dictionary<string, object?> { ["success"] = true };

                    if (response.TryGetProperty("result", out var resultProp))
                    {
                        res["result"] = JsonSerializer.Deserialize<object?>(resultProp.GetRawText());
                    }
                    else if (response.TryGetProperty("error", out var errorProp))
                    {
                        res["success"] = false;
                        res["error"] = JsonSerializer.Deserialize<object?>(errorProp.GetRawText());
                    }
                    else
                    {
                        res["data"] = JsonSerializer.Deserialize<object?>(response.GetRawText());
                    }

                    return res;
                }
            }
            catch (OperationCanceledException)
            {
                _pendingRequests.TryRemove(reqId, out _);
                return new Dictionary<string, object?>
                {
                    ["success"] = false,
                    ["error"] = $"Timed out waiting for response from Godot ({timeoutMs}ms)."
                };
            }
            catch (Exception ex)
            {
                _pendingRequests.TryRemove(reqId, out _);
                return new Dictionary<string, object?>
                {
                    ["success"] = false,
                    ["error"] = $"Error communicating with Godot: {ex.Message}"
                };
            }
        }

        public void Stop()
        {
            if (_disposed) return;
            _disposed = true;

            try { _cts?.Cancel(); } catch { }
            try { _activeSocket?.Dispose(); } catch { }
            try { _listener?.Stop(); _listener?.Close(); } catch { }

            foreach (var tcs in _pendingRequests.Values)
            {
                tcs.TrySetCanceled();
            }
            _pendingRequests.Clear();
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
