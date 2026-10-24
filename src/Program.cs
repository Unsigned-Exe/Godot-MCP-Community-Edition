#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using WpfApplication = System.Windows.Application;

namespace MCP_Godot4
{
    public static class Program
    {
        private const string AppVersion = "1.0.0";

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(int dwProcessId);
        private const int ATTACH_PARENT_PROCESS = -1;

        [STAThread]
        public static int Main(string[] args)
        {
            // Parse CLI flags
            int port = 6505;
            string? installAddonTarget = null;
            bool isServer = args.Contains("--server");
            bool isTray = args.Contains("--tray");
            bool isHelp = args.Contains("--help") || args.Contains("-h") || args.Contains("-?") || args.Contains("/?");
            bool isVersion = args.Contains("--version") || args.Contains("-v");

            for (int i = 0; i < args.Length; i++)
            {
                var arg = args[i];

                if (arg is "--port" or "-p" && i + 1 < args.Length)
                {
                    if (int.TryParse(args[++i], out int p) && p > 0 && p < 65536)
                    {
                        port = p;
                    }
                }

                if (arg is "--install-addon" && i + 1 < args.Length)
                {
                    installAddonTarget = args[++i];
                }
            }

            // If CLI query or stdio server mode, attach parent console for stdio communication
            if (isHelp || isVersion || !string.IsNullOrEmpty(installAddonTarget) || isServer)
            {
                if (AttachConsole(ATTACH_PARENT_PROCESS))
                {
                    try
                    {
                        var stdOut = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
                        Console.SetOut(stdOut);
                        var stdErr = new StreamWriter(Console.OpenStandardError()) { AutoFlush = true };
                        Console.SetError(stdErr);
                    }
                    catch { }
                }
            }

            if (isHelp)
            {
                PrintHelp();
                return 0;
            }

            if (isVersion)
            {
                Console.WriteLine($"Godot-MCP v{AppVersion} (Community Edition - Unsigned Exe Studios)");
                return 0;
            }

            // Automated addon installation into a Godot project
            if (!string.IsNullOrEmpty(installAddonTarget))
            {
                return InstallAddon(installAddonTarget);
            }

            // HEADLESS STDIO SERVER MODE (Called by Claude, LM Studio, Ollama)
            if (isServer)
            {
                using var cts = new CancellationTokenSource();
                Console.CancelKeyPress += (s, e) =>
                {
                    e.Cancel = true;
                    cts.Cancel();
                };

                var bridge = new GodotMcpBridge(port);
                GodotMcpServer.RunAsync(bridge, cts.Token).GetAwaiter().GetResult();
                return 0;
            }

            // INTERACTIVE WPF GUI HUB (Default on double-click)
            MainWindow? mainWindow = null;

            // Enforce single instance across GUI and system tray modes
            using var singleLock = SingleInstanceLock.TryAcquire("GodotMCP", isSecondarySilent: isTray, onShowRequested: () =>
            {
                mainWindow?.RestoreAndActivate();
            });

            if (singleLock == null)
            {
                // Another instance is already active; secondary exits immediately.
                return 0;
            }

            // Register process exit handler for graceful tray cleanup
            AppDomain.CurrentDomain.ProcessExit += (s, e) => HubTrayAgent.Shutdown();

            var godotBridge = new GodotBridge(port);
            godotBridge.Start();

            var wpfApp = new WpfApplication();
            wpfApp.ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;

            HubTrayAgent.Initialize(
                openDashboardCallback: () =>
                {
                    mainWindow?.RestoreAndActivate();
                },
                installAddonCallback: () =>
                {
                    mainWindow?.RestoreAndActivate();
                },
                launchEditorCallback: () =>
                {
                    wpfApp.Dispatcher.Invoke(() =>
                    {
                        var detection = GodotDiscovery.Detect();
                        if (detection.IsFound)
                        {
                            try
                            {
                                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                                {
                                    FileName = detection.ExecutablePath,
                                    UseShellExecute = true
                                });
                            }
                            catch { }
                        }
                    });
                },
                syncClientsCallback: () =>
                {
                    wpfApp.Dispatcher.Invoke(() =>
                    {
                        string currentExe = Environment.ProcessPath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "GodotMCP.exe");
                        ClientConfigSync.RegisterGodotServer(ClientConfigSync.ClientClaude, currentExe);
                        ClientConfigSync.RegisterGodotServer(ClientConfigSync.ClientLmStudio, currentExe);
                        ClientConfigSync.RegisterGodotServer(ClientConfigSync.ClientOllama, currentExe);
                        System.Windows.MessageBox.Show(
                            "✔ Synchronized Godot MCP to Claude, LM Studio, and Ollama!",
                            "AI Clients Synced",
                            System.Windows.MessageBoxButton.OK,
                            System.Windows.MessageBoxImage.Information);
                    });
                });

            mainWindow = new MainWindow(godotBridge);
            wpfApp.MainWindow = mainWindow;

            if (!isTray)
            {
                mainWindow.Show();
                mainWindow.Activate();
            }
            else
            {
                mainWindow.WindowState = System.Windows.WindowState.Minimized;
                mainWindow.Hide();
                HubTrayAgent.NotifyMinimized();
            }

            wpfApp.Run();
            HubTrayAgent.Shutdown();
            godotBridge.Stop();
            return 0;
        }

        private static void PrintHelp()
        {
            Console.WriteLine($@"
===================================================================
 Godot-MCP — Model Context Protocol Server for Godot Engine 4.x
 Version {AppVersion} | Community Edition | Unsigned Exe Studios
===================================================================

A lightweight, portable GUI hub and stdio server providing 25+ live
Godot 4 editor tools for AI assistants (Claude, LM Studio, Ollama).

USAGE:
  GodotMCP.exe [options]

OPTIONS:
  -h, --help                 Display this help information.
  -v, --version              Display application version.
  -p, --port <port>          Specify WebSocket port for Godot Editor (default: 6505).
  --server                   Run headlessly as stdio MCP server for AI clients.
  --tray                     Start minimized to system tray.
  --install-addon <path>     Installs the godot_mcp plugin into a project directory.

STDIO MCP CONFIGURATION (Claude / LM Studio / Ollama):

  {{
    ""mcpServers"": {{
      ""godot-4-7"": {{
        ""command"": ""C:\\path\\to\\GodotMCP.exe"",
        ""args"": [""--server""]
      }}
    }}
  }}
");
        }

        private static int InstallAddon(string targetProjectPath)
        {
            try
            {
                var fullProject = Path.GetFullPath(targetProjectPath);
                var projectGodot = Path.Combine(fullProject, "project.godot");

                if (!File.Exists(projectGodot) && !Directory.Exists(fullProject))
                {
                    Console.Error.WriteLine($"[Error] Target path '{fullProject}' is not a valid Godot project directory.");
                    return 1;
                }

                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var sourceAddonDir = Path.Combine(baseDir, "addons", "godot_mcp");

                if (!Directory.Exists(sourceAddonDir))
                {
                    sourceAddonDir = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "addons", "godot_mcp"));
                }

                if (!Directory.Exists(sourceAddonDir))
                {
                    Console.Error.WriteLine($"[Error] Could not find bundled addons/godot_mcp at '{sourceAddonDir}'.");
                    return 1;
                }

                var destAddonDir = Path.Combine(fullProject, "addons", "godot_mcp");
                Directory.CreateDirectory(destAddonDir);

                CopyDirectory(sourceAddonDir, destAddonDir);
                Console.WriteLine($"[Success] Installed 'godot_mcp' plugin into: {destAddonDir}");
                Console.WriteLine($"Please enable the plugin under Project -> Project Settings -> Plugins in the Godot Editor.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[Error] Failed to install addon: {ex.Message}");
                return 1;
            }
        }

        private static void CopyDirectory(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);
            foreach (var file in Directory.GetFiles(sourceDir))
            {
                var destFile = Path.Combine(destDir, Path.GetFileName(file));
                File.Copy(file, destFile, true);
            }

            foreach (var subDir in Directory.GetDirectories(sourceDir))
            {
                var destSub = Path.Combine(destDir, Path.GetFileName(subDir));
                CopyDirectory(subDir, destSub);
            }
        }
    }
}
