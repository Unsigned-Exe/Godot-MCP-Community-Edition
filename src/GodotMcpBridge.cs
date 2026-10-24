using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

namespace MCP_Godot4
{
    public class GodotMcpBridge : IMcpBridge
    {
        public string ServerId => "godot-4-7";
        public string DisplayName => "Godot 4 Engine";
        public string ProtocolServerName => "MCP-Godot4";

        private readonly GodotBridge _bridge;
        private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

        public GodotMcpBridge(int websocketPort = 6505)
        {
            _bridge = new GodotBridge(websocketPort);
            _bridge.Start();
        }

        public GodotMcpBridge(GodotBridge bridge)
        {
            _bridge = bridge;
        }

        public GodotBridge Bridge => _bridge;

        public IReadOnlyList<object> GetToolDefinitions() => GetToolDefinitionsInternal();

        public static IReadOnlyList<object> GetOpenAiToolDefinitions()
        {
            var json = JsonSerializer.Serialize(GetToolDefinitionsInternal());
            using var doc = JsonDocument.Parse(json);
            var list = new List<object>();
            foreach (var elem in doc.RootElement.EnumerateArray())
            {
                string name = elem.GetProperty("name").GetString() ?? "";
                string desc = elem.GetProperty("description").GetString() ?? "";
                var inputSchema = elem.GetProperty("inputSchema").Clone();
                list.Add(new
                {
                    type = "function",
                    function = new
                    {
                        name = name,
                        description = desc,
                        parameters = inputSchema
                    }
                });
            }
            return list;
        }

        public async Task<(string resultJson, bool isError)> ExecuteToolCallAsync(string toolName, string? argumentsJson)
        {
            JsonElement? element = null;
            if (!string.IsNullOrWhiteSpace(argumentsJson))
            {
                try
                {
                    using var doc = JsonDocument.Parse(argumentsJson);
                    element = doc.RootElement.Clone();
                }
                catch { }
            }

            var dict = await DispatchToolAsync(toolName, element, _bridge).ConfigureAwait(false);
            bool isError = false;
            if (dict.TryGetValue("success", out var sObj) && sObj is bool b)
            {
                isError = !b;
            }
            string serialized = JsonSerializer.Serialize(dict, new JsonSerializerOptions { WriteIndented = false });
            return (serialized, isError);
        }

        public async Task<object> ExecuteToolAsync(string toolName, JsonElement? args)
        {
            var dict = await DispatchToolAsync(toolName, args, _bridge).ConfigureAwait(false);
            bool success = true;
            if (dict.TryGetValue("success", out var sObj) && sObj is bool b) success = b;
            string serialized = JsonSerializer.Serialize(dict, new JsonSerializerOptions { WriteIndented = false });
            return new
            {
                content = new[]
                {
                    new { type = "text", text = serialized }
                },
                isError = !success
            };
        }

        private static async Task<Dictionary<string, object?>> DispatchToolAsync(string toolName, JsonElement? args, GodotBridge bridge)
        {
            return toolName switch
            {
                "godot_get_status" => new Dictionary<string, object?>
                {
                    ["success"] = true,
                    ["connected_to_editor"] = bridge.IsConnected,
                    ["godot_executable"] = GodotDiscovery.Detect().ExecutablePath,
                    ["active_project_directory"] = GodotProjectManager.ActiveProjectPath ?? "",
                    ["websocket_port"] = bridge.BoundPort,
                    ["message"] = bridge.IsConnected
                        ? $"Godot 4 Editor is connected via WebSocket! Active project: '{GodotProjectManager.ActiveProjectPath}'."
                        : $"Godot Editor not connected. Waiting on port {bridge.BoundPort}."
                },

                "godot_create_project" => GodotProjectManager.CreateProject(
                    GetString(args, "project_directory", "projectDirectory") ?? "",
                    GetString(args, "project_name", "projectName") ?? "NewProject"),

                "godot_launch_editor" => GodotProjectManager.LaunchEditor(
                    GetString(args, "project_directory", "projectDirectory") ?? ""),

                "godot_run_project" => GodotProjectManager.RunProject(
                    GetString(args, "project_directory", "projectDirectory") ?? ""),

                "godot_check_syntax" => GodotProjectManager.CheckSyntax(
                    GetString(args, "project_directory", "projectDirectory") ?? ""),

                "godot_read_file" => GodotProjectManager.ReadFile(
                    GetString(args, "file_path", "filePath") ?? ""),

                "godot_write_file" => GodotProjectManager.WriteFile(
                    GetString(args, "file_path", "filePath") ?? "",
                    GetString(args, "content", "content") ?? "",
                    overwrite: true),

                "godot_delete_file" => GodotProjectManager.DeleteFile(
                    GetString(args, "file_path", "filePath") ?? "",
                    GetBool(args, "remove_imports", "removeImports", true)),

                "godot_list_project_files" => GodotProjectManager.ListFiles(
                    GetString(args, "project_directory", "projectDirectory") ?? "",
                    GetString(args, "extension_filter", "extensionFilter")),

                "godot_write_script" => GodotProjectManager.WriteFile(
                    GetString(args, "file_path", "filePath") ?? "",
                    GetString(args, "gdscript_code", "gdscriptCode") ?? "",
                    overwrite: true),

                "godot_edit_script_snippet" => GodotProjectManager.EditScriptSnippet(
                    GetString(args, "file_path", "filePath") ?? "",
                    GetString(args, "target_snippet", "targetSnippet") ?? "",
                    GetString(args, "replacement_snippet", "replacementSnippet") ?? ""),

                "godot_append_to_script" => GodotProjectManager.AppendToScript(
                    GetString(args, "file_path", "filePath") ?? "",
                    GetString(args, "code_to_append", "codeToAppend") ?? ""),

                "godot_create_scene" => GodotProjectManager.CreateSceneTscn(
                    GetString(args, "scene_file_path", "sceneFilePath") ?? "",
                    GetString(args, "root_node_name", "rootNodeName") ?? "Main",
                    GetString(args, "root_node_type", "rootNodeType") ?? "Node2D",
                    GetString(args, "script_res_path", "scriptResPath")),

                // Live Editor Commands via WebSocket
                "godot_execute_editor_script" => await bridge.CallGodotAsync("execute_editor_script", new Dictionary<string, object?>
                {
                    ["code"] = GetString(args, "gdscript_code", "gdscriptCode") ?? "",
                    ["allow_unsafe_editor_io"] = GetBool(args, "allow_unsafe_editor_io", "allowUnsafeEditorIo", true)
                }).ConfigureAwait(false),

                "godot_get_scene_tree" => await bridge.CallGodotAsync("get_scene_tree", new()).ConfigureAwait(false),

                "godot_add_node" => await bridge.CallGodotAsync("add_node", new Dictionary<string, object?>
                {
                    ["type"] = GetString(args, "type", "type") ?? "Node",
                    ["name"] = GetString(args, "name", "name") ?? "",
                    ["parent_path"] = GetString(args, "parent_path", "parentPath") ?? "."
                }).ConfigureAwait(false),

                "godot_delete_node" => await bridge.CallGodotAsync("delete_node", new Dictionary<string, object?>
                {
                    ["node_path"] = GetString(args, "node_path", "nodePath") ?? ""
                }).ConfigureAwait(false),

                "godot_update_node_property" => await bridge.CallGodotAsync("update_property", new Dictionary<string, object?>
                {
                    ["node_path"] = GetString(args, "node_path", "nodePath") ?? "",
                    ["property"] = GetString(args, "property", "property") ?? "",
                    ["value"] = GetString(args, "value", "value") ?? ""
                }).ConfigureAwait(false),

                "godot_save_active_scene" => await bridge.CallGodotAsync("save_scene", new()).ConfigureAwait(false),

                "godot_open_scene" => await bridge.CallGodotAsync("open_scene", new Dictionary<string, object?>
                {
                    ["path"] = GetString(args, "scene_res_path", "sceneResPath") ?? ""
                }).ConfigureAwait(false),

                "godot_play_current_scene" => await bridge.CallGodotAsync("play_scene", new()).ConfigureAwait(false),

                "godot_stop_running_scene" => await bridge.CallGodotAsync("stop_scene", new()).ConfigureAwait(false),

                "godot_get_editor_errors" => await bridge.CallGodotAsync("get_editor_errors", new Dictionary<string, object?>
                {
                    ["max_lines"] = GetInt(args, "max_lines", "maxLines", 50)
                }).ConfigureAwait(false),

                "godot_get_editor_screenshot" => await bridge.CallGodotAsync("get_editor_screenshot", new()).ConfigureAwait(false),

                "godot_call_editor_command" => await bridge.CallGodotAsync(
                    GetString(args, "method", "method") ?? "get_project_info",
                    new()).ConfigureAwait(false),

                _ => new Dictionary<string, object?>
                {
                    ["success"] = false,
                    ["error"] = $"Unknown tool '{toolName}'"
                }
            };
        }

        private static string? GetString(JsonElement? args, string snakeKey, string camelKey, string? defaultValue = null)
        {
            if (args == null || args.Value.ValueKind != JsonValueKind.Object) return defaultValue;
            if (args.Value.TryGetProperty(snakeKey, out var p1) && p1.ValueKind == JsonValueKind.String) return p1.GetString();
            if (args.Value.TryGetProperty(camelKey, out var p2) && p2.ValueKind == JsonValueKind.String) return p2.GetString();
            return defaultValue;
        }

        private static bool GetBool(JsonElement? args, string snakeKey, string camelKey, bool defaultValue = false)
        {
            if (args == null || args.Value.ValueKind != JsonValueKind.Object) return defaultValue;
            if (args.Value.TryGetProperty(snakeKey, out var p1) && (p1.ValueKind == JsonValueKind.True || p1.ValueKind == JsonValueKind.False)) return p1.GetBoolean();
            if (args.Value.TryGetProperty(camelKey, out var p2) && (p2.ValueKind == JsonValueKind.True || p2.ValueKind == JsonValueKind.False)) return p2.GetBoolean();
            return defaultValue;
        }

        private static int GetInt(JsonElement? args, string snakeKey, string camelKey, int defaultValue = 0)
        {
            if (args == null || args.Value.ValueKind != JsonValueKind.Object) return defaultValue;
            if (args.Value.TryGetProperty(snakeKey, out var p1) && p1.TryGetInt32(out int v1)) return v1;
            if (args.Value.TryGetProperty(camelKey, out var p2) && p2.TryGetInt32(out int v2)) return v2;
            return defaultValue;
        }

        private static IReadOnlyList<object> GetToolDefinitionsInternal()
        {
            return new object[]
            {
                new { name = "godot_get_status", description = "Checks connection to Godot Editor plugin, returns executable path, active project directory, and WebSocket port.", inputSchema = new { type = "object", properties = new { } } },
                new { name = "godot_create_project", description = "Creates a brand new Godot 4 project directory. Do NOT use if a project already exists.", inputSchema = new { type = "object", properties = new { project_directory = new { type = "string" }, project_name = new { type = "string" } }, required = new[] { "project_directory", "project_name" } } },
                new { name = "godot_launch_editor", description = "Launches the Godot 4 Editor for the active project directory.", inputSchema = new { type = "object", properties = new { project_directory = new { type = "string" } } } },
                new { name = "godot_run_project", description = "Launches and plays the game project.", inputSchema = new { type = "object", properties = new { project_directory = new { type = "string" } } } },
                new { name = "godot_check_syntax", description = "Performs headless GDScript syntax checks on the project.", inputSchema = new { type = "object", properties = new { project_directory = new { type = "string" } } } },
                new { name = "godot_read_file", description = "Reads text file content from the Godot project (supports res:// or relative path).", inputSchema = new { type = "object", properties = new { file_path = new { type = "string" } }, required = new[] { "file_path" } } },
                new { name = "godot_write_file", description = "Writes or overwrites a file in the Godot project (supports res:// or relative path).", inputSchema = new { type = "object", properties = new { file_path = new { type = "string" }, content = new { type = "string" }, overwrite = new { type = "boolean" } }, required = new[] { "file_path", "content" } } },
                new { name = "godot_delete_file", description = "Deletes a file and associated .import/.uid files.", inputSchema = new { type = "object", properties = new { file_path = new { type = "string" }, remove_imports = new { type = "boolean" } }, required = new[] { "file_path" } } },
                new { name = "godot_list_project_files", description = "Lists project files in the active Godot project, ignoring .godot cache folder.", inputSchema = new { type = "object", properties = new { project_directory = new { type = "string" }, extension_filter = new { type = "string" } } } },
                new { name = "godot_write_script", description = "Creates or updates a GDScript (.gd) file in the active project (e.g. file_path='res://main.gd').", inputSchema = new { type = "object", properties = new { file_path = new { type = "string" }, gdscript_code = new { type = "string" }, overwrite = new { type = "boolean" } }, required = new[] { "file_path", "gdscript_code" } } },
                new { name = "godot_edit_script_snippet", description = "Replaces a code snippet in an existing GDScript file.", inputSchema = new { type = "object", properties = new { file_path = new { type = "string" }, target_snippet = new { type = "string" }, replacement_snippet = new { type = "string" } }, required = new[] { "file_path", "target_snippet", "replacement_snippet" } } },
                new { name = "godot_append_to_script", description = "Appends code to the end of a GDScript file.", inputSchema = new { type = "object", properties = new { file_path = new { type = "string" }, code_to_append = new { type = "string" } }, required = new[] { "file_path", "code_to_append" } } },
                new { name = "godot_create_scene", description = "Creates a new .tscn text scene file in the active project (e.g. scene_file_path='res://main.tscn', script_res_path='res://main.gd').", inputSchema = new { type = "object", properties = new { scene_file_path = new { type = "string" }, root_node_name = new { type = "string" }, root_node_type = new { type = "string" }, script_res_path = new { type = "string" } }, required = new[] { "scene_file_path" } } },
                new { name = "godot_execute_editor_script", description = "Executes arbitrary GDScript code live in the running Godot Editor.", inputSchema = new { type = "object", properties = new { gdscript_code = new { type = "string" }, allow_unsafe_editor_io = new { type = "boolean" } }, required = new[] { "gdscript_code" } } },
                new { name = "godot_get_scene_tree", description = "Fetches node hierarchy of active scene in Godot Editor.", inputSchema = new { type = "object", properties = new { } } },
                new { name = "godot_add_node", description = "Adds a node to the currently edited scene.", inputSchema = new { type = "object", properties = new { type = new { type = "string" }, name = new { type = "string" }, parent_path = new { type = "string" } }, required = new[] { "type" } } },
                new { name = "godot_delete_node", description = "Deletes a node from currently edited scene.", inputSchema = new { type = "object", properties = new { node_path = new { type = "string" } }, required = new[] { "node_path" } } },
                new { name = "godot_update_node_property", description = "Modifies a property on a node live in the editor.", inputSchema = new { type = "object", properties = new { node_path = new { type = "string" }, property = new { type = "string" }, value = new { type = "string" } }, required = new[] { "node_path", "property", "value" } } },
                new { name = "godot_save_active_scene", description = "Saves active scene in Godot editor.", inputSchema = new { type = "object", properties = new { } } },
                new { name = "godot_open_scene", description = "Opens a scene file in Godot editor tab.", inputSchema = new { type = "object", properties = new { scene_res_path = new { type = "string" } }, required = new[] { "scene_res_path" } } },
                new { name = "godot_play_current_scene", description = "Plays active scene in editor.", inputSchema = new { type = "object", properties = new { } } },
                new { name = "godot_stop_running_scene", description = "Stops currently playing scene in editor.", inputSchema = new { type = "object", properties = new { } } },
                new { name = "godot_get_editor_errors", description = "Reads latest errors and debugger output from Godot Editor panel.", inputSchema = new { type = "object", properties = new { max_lines = new { type = "integer" } } } },
                new { name = "godot_get_editor_screenshot", description = "Captures screenshot of Godot editor viewport.", inputSchema = new { type = "object", properties = new { } } },
                new { name = "godot_call_editor_command", description = "Calls an arbitrary godot_mcp plugin command.", inputSchema = new { type = "object", properties = new { method = new { type = "string" } } } }
            };
        }
    }
}
