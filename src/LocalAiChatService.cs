#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MCP_Godot4
{
    public class AiModelOption
    {
        public string Id { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Provider { get; set; } = "Ollama"; // "Ollama" or "LM Studio"
        public string BaseUrl { get; set; } = "";
        public bool SupportsTools { get; set; } = true;
        public bool IsLoaded { get; set; }

        public override string ToString() => DisplayName;
    }

    public class ChatMessageItemModel
    {
        public string Role { get; set; } = "user"; // "user", "assistant", "tool", "system"
        public string Content { get; set; } = "";
        public string? Thinking { get; set; }
        public string? ToolName { get; set; }
        public string? ToolArgs { get; set; }
        public string? ToolResult { get; set; }
        public bool IsError { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    public class LocalAiChatService
    {
        private static readonly HttpClient Http = new() { Timeout = ResolveChatHttpTimeout() };

        private static TimeSpan ResolveChatHttpTimeout()
        {
            var envStr = Environment.GetEnvironmentVariable("MCP_AI_CHAT_TIMEOUT_SECONDS")
                      ?? Environment.GetEnvironmentVariable("MCP_OLLAMA_TIMEOUT_SECONDS")
                      ?? Environment.GetEnvironmentVariable("MCP_LMSTUDIO_TIMEOUT_SECONDS");
            if (int.TryParse(envStr, out var sec) && sec > 0)
            {
                return TimeSpan.FromSeconds(sec);
            }
            return TimeSpan.FromMinutes(5); // Default to 300s (5 minutes) for local models
        }

        public static void SetTimeout(TimeSpan timeout)
        {
            try
            {
                Http.Timeout = timeout;
            }
            catch { }
        }
        private readonly GodotMcpBridge _mcpBridge;
        private readonly List<object> _rawTools;

        public const string DefaultOllamaUrl = "http://127.0.0.1:11434";
        public const string DefaultLmStudioUrl = "http://127.0.0.1:1234/v1";
        public const string DefaultLmStudioApiV0Url = "http://127.0.0.1:1234/api/v0";

        public LocalAiChatService(GodotMcpBridge mcpBridge)
        {
            _mcpBridge = mcpBridge;
            _rawTools = (List<object>)GodotMcpBridge.GetOpenAiToolDefinitions();
        }

        public async Task<List<AiModelOption>> DiscoverModelsAsync()
        {
            var results = new List<AiModelOption>();

            // 1. Check Ollama (with auto-start fallback if offline)
            for (int attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    var resp = await Http.GetAsync($"{DefaultOllamaUrl}/api/tags", cts.Token).ConfigureAwait(false);
                    if (resp.IsSuccessStatusCode)
                    {
                        string json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                        using var doc = JsonDocument.Parse(json);
                        if (doc.RootElement.TryGetProperty("models", out var modelsArr) && modelsArr.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var m in modelsArr.EnumerateArray())
                            {
                                string name = m.GetProperty("name").GetString() ?? "";
                                if (name.Contains("embed", StringComparison.OrdinalIgnoreCase)) continue;

                                bool hasTools = true;
                                if (m.TryGetProperty("capabilities", out var caps) && caps.ValueKind == JsonValueKind.Array)
                                {
                                    hasTools = false;
                                    foreach (var c in caps.EnumerateArray())
                                    {
                                        if (c.GetString() == "tools") hasTools = true;
                                    }
                                }

                                results.Add(new AiModelOption
                                {
                                    Id = name,
                                    DisplayName = $"[Ollama] {name}" + (hasTools ? " ⚡ Tools" : ""),
                                    Provider = "Ollama",
                                    BaseUrl = DefaultOllamaUrl,
                                    SupportsTools = hasTools,
                                    IsLoaded = true
                                });
                            }
                        }
                        break;
                    }
                }
                catch
                {
                    if (attempt == 0 && TryStartOllamaServer())
                    {
                        await Task.Delay(2500).ConfigureAwait(false);
                        continue;
                    }
                }
                break;
            }

            // 2. Check LM Studio via rich /api/v0/models metadata first, falling back to /v1/models
            var lmStudioModels = new List<AiModelOption>();
            bool v0Success = false;
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                var resp = await Http.GetAsync($"{DefaultLmStudioApiV0Url}/models", cts.Token).ConfigureAwait(false);
                if (resp.IsSuccessStatusCode)
                {
                    string json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("data", out var dataArr) && dataArr.ValueKind == JsonValueKind.Array)
                    {
                        v0Success = true;
                        foreach (var m in dataArr.EnumerateArray())
                        {
                            string id = m.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : "";
                            string type = m.TryGetProperty("type", out var typeEl) ? typeEl.GetString() ?? "llm" : "llm";
                            if (string.IsNullOrEmpty(id) || string.Equals(type, "embeddings", StringComparison.OrdinalIgnoreCase) || id.Contains("embed", StringComparison.OrdinalIgnoreCase))
                                continue;

                            bool isLoaded = m.TryGetProperty("state", out var stEl) && string.Equals(stEl.GetString(), "loaded", StringComparison.OrdinalIgnoreCase);
                            bool hasTools = false;
                            if (m.TryGetProperty("capabilities", out var caps) && caps.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var c in caps.EnumerateArray())
                                {
                                    if (string.Equals(c.GetString(), "tool_use", StringComparison.OrdinalIgnoreCase))
                                        hasTools = true;
                                }
                            }

                            string badge = (isLoaded ? " 🟢" : "") + (hasTools ? " ⚡ Tools" : " (Chat Only)");
                            lmStudioModels.Add(new AiModelOption
                            {
                                Id = id,
                                DisplayName = $"[LM Studio] {id}{badge}",
                                Provider = "LM Studio",
                                BaseUrl = DefaultLmStudioUrl,
                                SupportsTools = hasTools,
                                IsLoaded = isLoaded
                            });
                        }
                    }
                }
            }
            catch { }

            if (!v0Success)
            {
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    var resp = await Http.GetAsync($"{DefaultLmStudioUrl}/models", cts.Token).ConfigureAwait(false);
                    if (resp.IsSuccessStatusCode)
                    {
                        string json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                        using var doc = JsonDocument.Parse(json);
                        if (doc.RootElement.TryGetProperty("data", out var dataArr) && dataArr.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var m in dataArr.EnumerateArray())
                            {
                                string id = m.GetProperty("id").GetString() ?? "";
                                if (string.IsNullOrEmpty(id) || id.Contains("embed", StringComparison.OrdinalIgnoreCase))
                                    continue;

                                bool hasTools = !id.Contains("deepseek-r1", StringComparison.OrdinalIgnoreCase);
                                lmStudioModels.Add(new AiModelOption
                                {
                                    Id = id,
                                    DisplayName = $"[LM Studio] {id}" + (hasTools ? " ⚡ Tools" : " (Chat Only)"),
                                    Provider = "LM Studio",
                                    BaseUrl = DefaultLmStudioUrl,
                                    SupportsTools = hasTools
                                });
                            }
                        }
                    }
                }
                catch { }
            }

            // Sort LM Studio models: tool-capable + loaded first, then qwen3.8-9b-distill, then others
            results.AddRange(lmStudioModels
                .OrderByDescending(m => m.SupportsTools && m.IsLoaded)
                .ThenByDescending(m => m.Id.Contains("qwen3.8-9b", StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(m => m.SupportsTools)
                .ThenByDescending(m => m.IsLoaded));

            return results;
        }

        public static async Task<bool> IsOllamaRunningAsync()
        {
            try
            {
                var resp = await Http.GetAsync($"{DefaultOllamaUrl}/api/tags").ConfigureAwait(false);
                return resp.IsSuccessStatusCode;
            }
            catch { return false; }
        }

        public static bool TryStartOllamaServer()
        {
            try
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string ollamaExe = Path.Combine(localAppData, "Programs", "Ollama", "ollama.exe");
                if (File.Exists(ollamaExe))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = ollamaExe,
                        Arguments = "serve",
                        CreateNoWindow = true,
                        UseShellExecute = false
                    });
                    return true;
                }
            }
            catch { }
            return false;
        }

        /// <summary>
        /// Ejects active models from GPU VRAM / system memory for Ollama and LM Studio.
        /// </summary>
        public async Task<(bool success, string message)> UnloadModelAsync(AiModelOption? model = null, bool unloadAll = false)
        {
            var unloadedList = new List<string>();

            // 1. Ollama Eviction: Send POST /api/generate with keep_alive: 0
            if (unloadAll || model == null || string.Equals(model.Provider, "Ollama", StringComparison.OrdinalIgnoreCase))
            {
                if (!unloadAll && model != null && !string.IsNullOrEmpty(model.Id))
                {
                    try
                    {
                        var directReq = new HttpRequestMessage(HttpMethod.Post, $"{DefaultOllamaUrl}/api/generate")
                        {
                            Content = new StringContent(
                                JsonSerializer.Serialize(new { model = model.Id, keep_alive = 0 }),
                                Encoding.UTF8,
                                "application/json")
                        };
                        using var directCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                        var res = await Http.SendAsync(directReq, directCts.Token).ConfigureAwait(false);
                        if (res.IsSuccessStatusCode)
                        {
                            unloadedList.Add($"[Ollama] {model.Id}");
                        }
                    }
                    catch { }
                }

                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    var psResp = await Http.GetAsync($"{DefaultOllamaUrl}/api/ps", cts.Token).ConfigureAwait(false);
                    if (psResp.IsSuccessStatusCode)
                    {
                        var psJson = await psResp.Content.ReadAsStringAsync().ConfigureAwait(false);
                        using var doc = JsonDocument.Parse(psJson);
                        if (doc.RootElement.TryGetProperty("models", out var modelsArr) && modelsArr.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var m in modelsArr.EnumerateArray())
                            {
                                string name = m.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                                if (!string.IsNullOrEmpty(name))
                                {
                                    if (unloadAll || model == null || string.Equals(model.Id, name, StringComparison.OrdinalIgnoreCase))
                                    {
                                        var unloadReq = new HttpRequestMessage(HttpMethod.Post, $"{DefaultOllamaUrl}/api/generate")
                                        {
                                            Content = new StringContent(
                                                JsonSerializer.Serialize(new { model = name, keep_alive = 0 }),
                                                Encoding.UTF8,
                                                "application/json")
                                        };
                                        using var unlCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                                        var res = await Http.SendAsync(unloadReq, unlCts.Token).ConfigureAwait(false);
                                        if (res.IsSuccessStatusCode)
                                        {
                                            unloadedList.Add($"[Ollama] {name}");
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                catch { }
            }

            // 2. LM Studio Eviction: Run lms unload <model> or lms unload --all
            if (unloadAll || model == null || string.Equals(model.Provider, "LM Studio", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    string? lmsPath = ResolveLmsCliPath();
                    if (!string.IsNullOrEmpty(lmsPath))
                    {
                        string args = (unloadAll || model == null || string.IsNullOrEmpty(model.Id))
                            ? "unload --all"
                            : $"unload \"{model.Id}\"";

                        var psi = new ProcessStartInfo
                        {
                            FileName = lmsPath,
                            Arguments = args,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                        using var proc = Process.Start(psi);
                        if (proc != null)
                        {
                            using var lmsCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                            await proc.WaitForExitAsync(lmsCts.Token).ConfigureAwait(false);
                            unloadedList.Add(model != null && !unloadAll ? $"[LM Studio] {model.Id}" : "[LM Studio] All models");
                        }
                    }
                }
                catch { }
            }

            if (unloadedList.Count > 0)
            {
                return (true, $"✔ Ejected from VRAM: {string.Join(", ", unloadedList)}");
            }
            return (false, "No active models were currently resident in VRAM.");
        }

        public static string? ResolveLmsCliPath()
        {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var candidates = new[]
            {
                Path.Combine(userProfile, ".lmstudio", "bin", "lms.exe"),
                Path.Combine(userProfile, ".cache", "lm-studio", "bin", "lms.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "LM Studio", "resources", "app", "bin", "lms.exe")
            };
            foreach (var c in candidates)
            {
                if (File.Exists(c)) return c;
            }
            var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = Path.Combine(dir.Trim(), "lms.exe");
                if (File.Exists(candidate)) return candidate;
            }
            return null;
        }

        /// <summary>
        /// Normalizes conversation history into a clean, schema-compliant messages list for either
        /// Ollama (/api/chat, where tool_calls[].function.arguments MUST be a JSON object) or
        /// LM Studio (/v1/chat/completions, where tool_calls[].type="function" and arguments MUST be a JSON string).
        /// This allows seamlessly switching between Ollama and LM Studio models mid-conversation without schema errors.
        /// </summary>
        private static List<Dictionary<string, object?>> BuildProviderMessages(
            List<Dictionary<string, object?>> history,
            bool isOllama,
            bool supportsTools)
        {
            var normalized = new List<Dictionary<string, object?>>(history.Count);

            foreach (var msg in history)
            {
                string role = msg.TryGetValue("role", out var rObj) ? rObj?.ToString() ?? "user" : "user";
                string content = msg.TryGetValue("content", out var cObj) ? cObj?.ToString() ?? "" : "";

                // If the target model does not support tools, convert historical tool calls/results into plain text context
                if (!supportsTools)
                {
                    if (role == "tool")
                    {
                        continue; // Skip raw tool role messages for non-tool models
                    }
                    if (role == "assistant" && string.IsNullOrWhiteSpace(content))
                    {
                        continue; // Skip empty tool-calling assistant turns
                    }
                    AppendOrMergeMessage(normalized, new Dictionary<string, object?>
                    {
                        ["role"] = role,
                        ["content"] = content
                    });
                    continue;
                }

                if (role == "assistant" && msg.TryGetValue("tool_calls", out var tcObj) && tcObj != null)
                {
                    var normalizedToolCalls = NormalizeToolCalls(tcObj, isOllama);
                    if (normalizedToolCalls.Count > 0)
                    {
                        var assistantMsg = new Dictionary<string, object?>
                        {
                            ["role"] = "assistant",
                            ["content"] = content,
                            ["tool_calls"] = normalizedToolCalls
                        };
                        normalized.Add(assistantMsg);
                        continue;
                    }
                }

                if (role == "tool")
                {
                    var toolMsg = new Dictionary<string, object?>
                    {
                        ["role"] = "tool",
                        ["content"] = content
                    };
                    if (msg.TryGetValue("tool_call_id", out var idObj) && idObj != null && !string.IsNullOrEmpty(idObj.ToString()))
                    {
                        toolMsg["tool_call_id"] = idObj.ToString();
                    }
                    else if (!isOllama)
                    {
                        toolMsg["tool_call_id"] = "call_godot";
                    }
                    normalized.Add(toolMsg);
                    continue;
                }

                AppendOrMergeMessage(normalized, new Dictionary<string, object?>
                {
                    ["role"] = role,
                    ["content"] = content
                });
            }

            return normalized;
        }

        private static void AppendOrMergeMessage(List<Dictionary<string, object?>> list, Dictionary<string, object?> msg)
        {
            string role = msg["role"]?.ToString() ?? "user";
            if ((role == "user" || role == "assistant") && list.Count > 0)
            {
                var prev = list[^1];
                string prevRole = prev.TryGetValue("role", out var pr) ? pr?.ToString() ?? "" : "";
                bool prevHasTools = prev.ContainsKey("tool_calls");
                if (prevRole == role && !prevHasTools)
                {
                    string prevContent = prev.TryGetValue("content", out var pc) ? pc?.ToString() ?? "" : "";
                    string newContent = msg.TryGetValue("content", out var nc) ? nc?.ToString() ?? "" : "";
                    prev["content"] = string.IsNullOrEmpty(prevContent) ? newContent : $"{prevContent}\n\n{newContent}";
                    return;
                }
            }
            list.Add(msg);
        }

        private static List<Dictionary<string, object?>> NormalizeToolCalls(object tcObj, bool isOllama)
        {
            var result = new List<Dictionary<string, object?>>();
            string json = tcObj is JsonElement je ? je.GetRawText() : JsonSerializer.Serialize(tcObj);

            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return result;

            int idx = 0;
            foreach (var tc in doc.RootElement.EnumerateArray())
            {
                string id = tc.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String
                    ? (idEl.GetString() ?? $"call_{idx}")
                    : $"call_{idx}";
                if (string.IsNullOrEmpty(id)) id = $"call_{idx}";

                string fnName = "";
                string argsRawJson = "{}";

                if (tc.TryGetProperty("function", out var fnEl) && fnEl.ValueKind == JsonValueKind.Object)
                {
                    if (fnEl.TryGetProperty("name", out var nEl) && nEl.ValueKind == JsonValueKind.String)
                        fnName = nEl.GetString() ?? "";

                    if (fnEl.TryGetProperty("arguments", out var aEl))
                    {
                        if (aEl.ValueKind == JsonValueKind.String)
                        {
                            string s = aEl.GetString() ?? "{}";
                            argsRawJson = string.IsNullOrWhiteSpace(s) ? "{}" : s;
                        }
                        else if (aEl.ValueKind == JsonValueKind.Object)
                        {
                            argsRawJson = aEl.GetRawText();
                        }
                    }
                }

                if (string.IsNullOrEmpty(fnName)) continue;

                if (isOllama)
                {
                    // Ollama requires function.arguments to be a JSON object
                    JsonElement argsObj;
                    try
                    {
                        using var argsDoc = JsonDocument.Parse(argsRawJson);
                        argsObj = argsDoc.RootElement.Clone();
                        if (argsObj.ValueKind != JsonValueKind.Object)
                        {
                            using var emptyDoc = JsonDocument.Parse("{}");
                            argsObj = emptyDoc.RootElement.Clone();
                        }
                    }
                    catch
                    {
                        using var emptyDoc = JsonDocument.Parse("{}");
                        argsObj = emptyDoc.RootElement.Clone();
                    }

                    result.Add(new Dictionary<string, object?>
                    {
                        ["id"] = id,
                        ["type"] = "function",
                        ["function"] = new Dictionary<string, object?>
                        {
                            ["name"] = fnName,
                            ["arguments"] = argsObj
                        }
                    });
                }
                else
                {
                    // OpenAI / LM Studio requires tool_calls[].type = "function" and function.arguments to be a JSON string (with NO "index" field)
                    result.Add(new Dictionary<string, object?>
                    {
                        ["id"] = id,
                        ["type"] = "function",
                        ["function"] = new Dictionary<string, object?>
                        {
                            ["name"] = fnName,
                            ["arguments"] = argsRawJson
                        }
                    });
                }
                idx++;
            }

            return result;
        }

        private static void RollbackHistory(List<Dictionary<string, object?>> history, int targetCount)
        {
            if (history.Count > targetCount && targetCount >= 0)
            {
                history.RemoveRange(targetCount, history.Count - targetCount);
            }
        }

        /// <summary>
        /// Executes a single chat round using the specified local model, processing Godot MCP tool calls
        /// up to <c>maxToolLoops</c> (50) iterations and streaming results back through callbacks.
        /// </summary>
        /// <param name="model">The local AI model option selected for this conversation turn.</param>
        /// <param name="messageHistory">Conversation history maintained as normalized message dictionaries.</param>
        /// <param name="userPrompt">The user's input prompt to process.</param>
        /// <param name="onStatusUpdate">Callback invoked with status messages during execution.</param>
        /// <param name="onToolExecuted">Callback invoked when a Godot tool is called: (toolName, argsJson, resultJson, isError).</param>
        /// <param name="onAssistantResponse">Callback invoked with the assistant response: (content, reasoningContent).</param>
        /// <param name="ct">Cancellation token to abort the operation.</param>
        public async Task ExecuteChatRoundAsync(
            AiModelOption model,
            List<Dictionary<string, object?>> messageHistory,
            string userPrompt,
            Action<string> onStatusUpdate,
            Action<string, string, string, bool> onToolExecuted,
            Action<string, string?> onAssistantResponse,
            CancellationToken ct)
        {
            string activeProj = _mcpBridge.Bridge.ConnectedProjectPath
                             ?? GodotProjectManager.ActiveProjectPath
                             ?? Directory.GetCurrentDirectory();
            string systemInstruction =
                "You are Godot Assistant, an expert AI game developer integrated directly into Godot MCP Hub.\n" +
                $"Active Godot Project Directory: \"{activeProj}\"\n" +
                $"Godot Editor Connection: {(_mcpBridge.Bridge.IsConnected ? "Connected" : "Waiting")} on port {_mcpBridge.Bridge.BoundPort}\n\n" +
                "IMPORTANT RULES:\n" +
                $"1. The active project is ALREADY created and located at \"{activeProj}\". NEVER call 'godot_create_project' unless the user explicitly asks to create a brand new project in a different folder.\n" +
                "2. All relative paths or 'res://' paths (like 'res://main.gd' or 'main.tscn') automatically resolve to the Active Godot Project Directory.\n" +
                "3. To create or write a GDScript file, call 'godot_write_script' with file_path (e.g. 'res://main.gd') and gdscript_code.\n" +
                "4. To create a scene with a script attached, call 'godot_create_scene' with scene_file_path='res://main.tscn', root_node_name='Main', root_node_type='Node2D', and script_res_path='res://main.gd'.\n" +
                "5. To modify the currently open scene in the live Godot Editor, use 'godot_get_scene_tree', 'godot_add_node', or 'godot_execute_editor_script'.\n" +
                "6. Call each necessary tool once, inspect the result, and then provide a clear, concise final response to the user.";

            if (messageHistory.Count == 0)
            {
                messageHistory.Add(new Dictionary<string, object?>
                {
                    ["role"] = "system",
                    ["content"] = systemInstruction
                });
            }
            else if (messageHistory[0].TryGetValue("role", out var r) && r?.ToString() == "system")
            {
                messageHistory[0]["content"] = systemInstruction;
            }

            int rollbackCount = messageHistory.Count;

            // Add user message to history
            messageHistory.Add(new Dictionary<string, object?>
            {
                ["role"] = "user",
                ["content"] = userPrompt
            });

            const int maxToolLoops = 50;
            int currentLoop = 0;

            try
            {
                while (currentLoop++ < maxToolLoops && !ct.IsCancellationRequested)
                {
                    onStatusUpdate(currentLoop == 1 ? $"Connecting to {model.Provider} ({model.Id})..." : "Synthesizing tool results...");

                    if (model.Provider == "Ollama")
                    {
                        var providerMessages = BuildProviderMessages(messageHistory, isOllama: true, supportsTools: model.SupportsTools);
                        var reqBody = new Dictionary<string, object?>
                        {
                            ["model"] = model.Id,
                            ["messages"] = providerMessages,
                            ["stream"] = false,
                            ["options"] = new Dictionary<string, object?>
                            {
                                ["num_ctx"] = 16384,
                                ["temperature"] = 0.2
                            }
                        };
                        if (model.SupportsTools)
                        {
                            reqBody["tools"] = _rawTools;
                        }

                        string jsonReq = JsonSerializer.Serialize(reqBody);
                        using var content = new StringContent(jsonReq, Encoding.UTF8, "application/json");

                        var response = await Http.PostAsync($"{model.BaseUrl}/api/chat", content, ct).ConfigureAwait(false);
                        if (!response.IsSuccessStatusCode)
                        {
                            string err = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                            RollbackHistory(messageHistory, rollbackCount);
                            onAssistantResponse($"❌ Ollama Error ({response.StatusCode}): {err}", null);
                            return;
                        }

                        string respJson = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                        using var doc = JsonDocument.Parse(respJson);
                        var root = doc.RootElement;

                        if (!root.TryGetProperty("message", out var msgElem))
                        {
                            RollbackHistory(messageHistory, rollbackCount);
                            onAssistantResponse("Received invalid response from Ollama.", null);
                            return;
                        }

                        string textContent = "";
                        if (msgElem.TryGetProperty("content", out var cElem) && cElem.ValueKind == JsonValueKind.String)
                        {
                            textContent = cElem.GetString() ?? "";
                        }

                        string? thinking = null;
                        if (msgElem.TryGetProperty("thinking", out var thElem) && thElem.ValueKind == JsonValueKind.String)
                        {
                            thinking = thElem.GetString();
                        }

                        // Check for tool calls
                        if (msgElem.TryGetProperty("tool_calls", out var toolCalls) && toolCalls.ValueKind == JsonValueKind.Array && toolCalls.GetArrayLength() > 0)
                        {
                            var normalizedCalls = NormalizeToolCalls(toolCalls, isOllama: false);
                            messageHistory.Add(new Dictionary<string, object?>
                            {
                                ["role"] = "assistant",
                                ["content"] = textContent,
                                ["tool_calls"] = normalizedCalls
                            });

                            int callIdx = 0;
                            foreach (var tc in toolCalls.EnumerateArray())
                            {
                                string toolCallId = tc.TryGetProperty("id", out var idElem) && idElem.ValueKind == JsonValueKind.String
                                    ? (idElem.GetString() ?? $"call_{callIdx}")
                                    : $"call_{callIdx}";
                                if (string.IsNullOrEmpty(toolCallId)) toolCallId = $"call_{callIdx}";

                                string fnName = "";
                                string fnArgs = "{}";

                                if (tc.TryGetProperty("function", out var fnElem))
                                {
                                    if (fnElem.TryGetProperty("name", out var nElem)) fnName = nElem.GetString() ?? "";
                                    if (fnElem.TryGetProperty("arguments", out var aElem))
                                    {
                                        fnArgs = aElem.ValueKind == JsonValueKind.String ? (aElem.GetString() ?? "{}") : aElem.GetRawText();
                                    }
                                }

                                onStatusUpdate($"⚡ Calling Godot tool: {fnName}...");
                                var (resultJson, isError) = await _mcpBridge.ExecuteToolCallAsync(fnName, fnArgs).ConfigureAwait(false);
                                onToolExecuted(fnName, fnArgs, resultJson, isError);

                                messageHistory.Add(new Dictionary<string, object?>
                                {
                                    ["role"] = "tool",
                                    ["tool_call_id"] = toolCallId,
                                    ["content"] = resultJson
                                });
                                callIdx++;
                            }

                            continue;
                        }
                        else
                        {
                            messageHistory.Add(new Dictionary<string, object?>
                            {
                                ["role"] = "assistant",
                                ["content"] = textContent
                            });

                            onAssistantResponse(textContent, thinking);
                            return;
                        }
                    }
                    else // LM Studio (OpenAI endpoint)
                    {
                        var providerMessages = BuildProviderMessages(messageHistory, isOllama: false, supportsTools: model.SupportsTools);
                        var reqBody = new Dictionary<string, object?>
                        {
                            ["model"] = model.Id,
                            ["messages"] = providerMessages,
                            ["stream"] = false
                        };
                        if (model.SupportsTools)
                        {
                            reqBody["tools"] = _rawTools;
                        }

                        string jsonReq = JsonSerializer.Serialize(reqBody);
                        using var content = new StringContent(jsonReq, Encoding.UTF8, "application/json");

                        var response = await Http.PostAsync($"{model.BaseUrl}/chat/completions", content, ct).ConfigureAwait(false);
                        if (!response.IsSuccessStatusCode)
                        {
                            string err = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                            RollbackHistory(messageHistory, rollbackCount);
                            onAssistantResponse($"❌ LM Studio Error ({response.StatusCode}): {err}", null);
                            return;
                        }

                        string respJson = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                        using var doc = JsonDocument.Parse(respJson);
                        var root = doc.RootElement;

                        if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                        {
                            RollbackHistory(messageHistory, rollbackCount);
                            onAssistantResponse("Empty choices returned by LM Studio.", null);
                            return;
                        }

                        var msgElem = choices[0].GetProperty("message");
                        string textContent = "";
                        if (msgElem.TryGetProperty("content", out var cElem) && cElem.ValueKind == JsonValueKind.String)
                        {
                            textContent = cElem.GetString() ?? "";
                        }

                        string? thinking = null;
                        if (msgElem.TryGetProperty("reasoning_content", out var rcElem) && rcElem.ValueKind == JsonValueKind.String)
                        {
                            thinking = rcElem.GetString();
                        }

                        if (msgElem.TryGetProperty("tool_calls", out var toolCalls) && toolCalls.ValueKind == JsonValueKind.Array && toolCalls.GetArrayLength() > 0)
                        {
                            var normalizedCalls = NormalizeToolCalls(toolCalls, isOllama: false);
                            messageHistory.Add(new Dictionary<string, object?>
                            {
                                ["role"] = "assistant",
                                ["content"] = textContent,
                                ["tool_calls"] = normalizedCalls
                            });

                            int callIdx = 0;
                            foreach (var tc in toolCalls.EnumerateArray())
                            {
                                string toolCallId = tc.TryGetProperty("id", out var idElem) && idElem.ValueKind == JsonValueKind.String
                                    ? (idElem.GetString() ?? $"call_{callIdx}")
                                    : $"call_{callIdx}";
                                if (string.IsNullOrEmpty(toolCallId)) toolCallId = $"call_{callIdx}";

                                string fnName = "";
                                string fnArgs = "{}";

                                if (tc.TryGetProperty("function", out var fnElem))
                                {
                                    if (fnElem.TryGetProperty("name", out var nElem)) fnName = nElem.GetString() ?? "";
                                    if (fnElem.TryGetProperty("arguments", out var aElem))
                                    {
                                        fnArgs = aElem.ValueKind == JsonValueKind.String ? (aElem.GetString() ?? "{}") : aElem.GetRawText();
                                    }
                                }

                                onStatusUpdate($"⚡ Calling Godot tool: {fnName}...");
                                var (resultJson, isError) = await _mcpBridge.ExecuteToolCallAsync(fnName, fnArgs).ConfigureAwait(false);
                                onToolExecuted(fnName, fnArgs, resultJson, isError);

                                messageHistory.Add(new Dictionary<string, object?>
                                {
                                    ["role"] = "tool",
                                    ["tool_call_id"] = toolCallId,
                                    ["content"] = resultJson
                                });
                                callIdx++;
                            }

                            continue;
                        }
                        else
                        {
                            messageHistory.Add(new Dictionary<string, object?>
                            {
                                ["role"] = "assistant",
                                ["content"] = textContent
                            });

                            onAssistantResponse(textContent, thinking);
                            return;
                        }
                    }
                }

                if (currentLoop >= maxToolLoops)
                {
                    onAssistantResponse("⚠️ Maximum tool execution iterations reached.", null);
                }
            }
            catch
            {
                RollbackHistory(messageHistory, rollbackCount);
                throw;
            }
        }
    }
}
