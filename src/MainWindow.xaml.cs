#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using System.Runtime.InteropServices;

// Disambiguate WPF vs WinForms
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using Orientation = System.Windows.Controls.Orientation;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using FontFamily = System.Windows.Media.FontFamily;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfColor = System.Windows.Media.Color;
using WpfColorConverter = System.Windows.Media.ColorConverter;
using WpfMessageBox = System.Windows.MessageBox;
using WpfOpenFileDialog = Microsoft.Win32.OpenFileDialog;
using FormsFolderBrowserDialog = System.Windows.Forms.FolderBrowserDialog;

namespace MCP_Godot4
{
    public partial class MainWindow : Window
    {
        private const string RegKeyPath = @"Software\UnsignedExe\GodotMCP";
        private const string ThemeValName = "Theme";
        private const string LastProjectValName = "LastProjectPath";
        private const string CustomGodotValName = "CustomGodotPath";

        private readonly GodotBridge _bridge;
        private readonly GodotMcpBridge _mcpBridge;
        private readonly LocalAiChatService _chatService;
        private readonly DispatcherTimer _pollTimer;
        private readonly List<Dictionary<string, object?>> _conversationHistory = new();

        private AiModelOption? _currentModel;
        private CancellationTokenSource? _chatCts;
        private bool _isInitializing = true;
        private bool _isChatBusy = false;

        public MainWindow(GodotBridge bridge)
        {
            _bridge = bridge;
            _mcpBridge = new GodotMcpBridge(_bridge);
            _chatService = new LocalAiChatService(_mcpBridge);

            InitializeComponent();

            LoadThemePreference();
            InitializeWorkspace();

            _pollTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _pollTimer.Tick += OnPollTimerTick;
            _pollTimer.Start();

            // Asynchronously populate local models
            Dispatcher.InvokeAsync(async () =>
            {
                await RefreshLocalModelsAsync();
                _isInitializing = false;
            });
        }

        #region Navigation Views

        private void OnNavTabChanged(object sender, RoutedEventArgs e)
        {
            if (gridChatView == null || gridWorkspaceView == null || gridPublishView == null || gridProSuiteView == null) return;

            if (tabBtnChat.IsChecked == true)
            {
                gridChatView.Visibility = Visibility.Visible;
                gridWorkspaceView.Visibility = Visibility.Collapsed;
                gridPublishView.Visibility = Visibility.Collapsed;
                gridProSuiteView.Visibility = Visibility.Collapsed;
            }
            else if (tabBtnWorkspace.IsChecked == true)
            {
                gridChatView.Visibility = Visibility.Collapsed;
                gridWorkspaceView.Visibility = Visibility.Visible;
                gridPublishView.Visibility = Visibility.Collapsed;
                gridProSuiteView.Visibility = Visibility.Collapsed;
            }
            else if (tabBtnPublish.IsChecked == true)
            {
                gridChatView.Visibility = Visibility.Collapsed;
                gridWorkspaceView.Visibility = Visibility.Collapsed;
                gridPublishView.Visibility = Visibility.Visible;
                gridProSuiteView.Visibility = Visibility.Collapsed;
            }
            else if (tabBtnProSuite.IsChecked == true)
            {
                gridChatView.Visibility = Visibility.Collapsed;
                gridWorkspaceView.Visibility = Visibility.Collapsed;
                gridPublishView.Visibility = Visibility.Collapsed;
                gridProSuiteView.Visibility = Visibility.Visible;
            }
        }

        private void OnOpenAssetLibWeb(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://godotengine.org/asset-library/asset",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show($"Unable to open Godot Asset Library portal: {ex.Message}", "Browser Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void OnValidateAssetLibPackage(object sender, RoutedEventArgs e)
        {
            string projectPath = txtProjectPath?.Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(projectPath) || !Directory.Exists(projectPath))
            {
                txtAssetLibStatus.Text = "⚠️ Please select a valid Godot project path in the 'Godot Workspace' tab first.";
                return;
            }

            string pluginCfg = Path.Combine(projectPath, "addons", "godot_mcp", "plugin.cfg");
            if (!File.Exists(pluginCfg))
            {
                txtAssetLibStatus.Text = "⚠️ No 'plugin.cfg' found in addons/godot_mcp. Click '⚡ Install Addon in Project' in the Workspace tab.";
                return;
            }

            txtAssetLibStatus.Text = "✔ Addon structure validated! Ready for Godot Asset Library submission. (Archive packaging ready).";
            WpfMessageBox.Show(
                "Godot AssetLib Pre-Flight Check Passed!\n\n" +
                "• Addon ID: godot_mcp\n" +
                "• Compatibility: Godot 4.x\n" +
                "• Manifest: plugin.cfg verified\n" +
                "• Repository: " + txtPublishRepoUrl.Text + "\n\n" +
                "You can now submit this repository URL to the Godot Asset Library portal.",
                "AssetLib Validation Success",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void OnProPlatformCardClick(object sender, MouseButtonEventArgs e)
        {
            var res = WpfMessageBox.Show(
                "Storefront Publishing Pipelines (itch.io Butler, SteamCmd, GitHub Releases, Epic BPT, GOG) are an exclusive feature of MCP Suite Pro.\n\n" +
                "Would you like to open unsignedexe.com to explore MCP Suite Pro?",
                "MCP Suite Pro Feature",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (res == MessageBoxResult.Yes)
            {
                OnExploreProSuite(sender, e);
            }
        }

        #endregion

        #region Workspace Setup & Polling

        private void InitializeWorkspace()
        {
            // 1. Detect Godot executable
            string? customGodot = GetRegistryString(CustomGodotValName);
            var detection = GodotDiscovery.Detect(customGodot);
            if (detection.IsFound)
            {
                txtGodotPath.Text = detection.ExecutablePath;
                txtFooterLog.Text = $"Detected {detection.Version} at: {detection.ExecutablePath}";
            }
            else
            {
                txtFooterLog.Text = "Godot 4 executable not automatically detected. Click Browse to select your godot.exe.";
            }

            // 2. Load last opened project path
            string? lastProject = GetRegistryString(LastProjectValName);
            if (!string.IsNullOrEmpty(lastProject) && Directory.Exists(lastProject))
            {
                txtProjectPath.Text = lastProject;
            }
            else
            {
                string defaultProj = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "GodotProjects");
                if (Directory.Exists(defaultProj)) txtProjectPath.Text = defaultProj;
            }

            if (!string.IsNullOrWhiteSpace(txtProjectPath.Text))
            {
                GodotProjectManager.ActiveProjectPath = txtProjectPath.Text.Trim();
            }

            // 3. Check assistant installed states
            chkClaude.IsChecked = ClientConfigSync.IsClientInstalled(ClientConfigSync.ClientClaude);
            chkLmStudio.IsChecked = ClientConfigSync.IsClientInstalled(ClientConfigSync.ClientLmStudio);
            chkOllama.IsChecked = ClientConfigSync.IsClientInstalled(ClientConfigSync.ClientOllama);

            txtPort.Text = _bridge.BoundPort.ToString();
            UpdateConnectionBadge();
        }

        private void OnPollTimerTick(object? sender, EventArgs e)
        {
            UpdateConnectionBadge();
        }

        private void UpdateConnectionBadge()
        {
            bool connected = _bridge.IsConnected;
            if (connected)
            {
                if (!string.IsNullOrEmpty(_bridge.ConnectedProjectPath) &&
                    !string.Equals(txtProjectPath.Text, _bridge.ConnectedProjectPath, StringComparison.OrdinalIgnoreCase))
                {
                    txtProjectPath.Text = _bridge.ConnectedProjectPath;
                    GodotProjectManager.ActiveProjectPath = _bridge.ConnectedProjectPath;
                    SetRegistryString(LastProjectValName, _bridge.ConnectedProjectPath);
                }

                string projLabel = !string.IsNullOrWhiteSpace(_bridge.ConnectedProjectName)
                    ? $" — {_bridge.ConnectedProjectName}"
                    : "";

                borderStatus.Background = new SolidColorBrush((WpfColor)WpfColorConverter.ConvertFromString("#E8F5E9"));
                borderStatus.BorderBrush = new SolidColorBrush((WpfColor)WpfColorConverter.ConvertFromString("#81C784"));
                ellipseStatusDot.Fill = new SolidColorBrush((WpfColor)WpfColorConverter.ConvertFromString("#4CAF50"));
                txtStatusBadge.Text = $"Editor: Connected{projLabel} (Port {_bridge.BoundPort})";
                txtStatusBadge.Foreground = new SolidColorBrush((WpfColor)WpfColorConverter.ConvertFromString("#2E7D32"));
            }
            else
            {
                borderStatus.Background = new SolidColorBrush((WpfColor)WpfColorConverter.ConvertFromString("#E3F2FD"));
                borderStatus.BorderBrush = new SolidColorBrush((WpfColor)WpfColorConverter.ConvertFromString("#90CAF9"));
                ellipseStatusDot.Fill = new SolidColorBrush((WpfColor)WpfColorConverter.ConvertFromString("#2196F3"));
                txtStatusBadge.Text = $"Editor: Waiting on Port {_bridge.BoundPort}";
                txtStatusBadge.Foreground = new SolidColorBrush((WpfColor)WpfColorConverter.ConvertFromString("#1565C0"));
            }

            HubTrayAgent.UpdateState(connected, isBusy: _isChatBusy);
        }

        #endregion

        #region Local AI Chat System

        private async Task RefreshLocalModelsAsync()
        {
            txtProviderStatus.Text = "Scanning local AI models...";
            ellipseProviderDot.Fill = new SolidColorBrush((WpfColor)WpfColorConverter.ConvertFromString("#FF9500"));

            var models = await _chatService.DiscoverModelsAsync();
            cbModels.Items.Clear();

            if (models.Count > 0)
            {
                foreach (var m in models)
                {
                    cbModels.Items.Add(m);
                }
                cbModels.SelectedIndex = 0;
                _currentModel = models[0];

                txtProviderStatus.Text = $"{_currentModel.Provider} Ready ({models.Count} model{(models.Count > 1 ? "s" : "")} detected)";
                ellipseProviderDot.Fill = new SolidColorBrush((WpfColor)WpfColorConverter.ConvertFromString("#34C759"));
            }
            else
            {
                // No running models detected - check if Ollama server can be started
                bool started = LocalAiChatService.TryStartOllamaServer();
                if (started)
                {
                    txtProviderStatus.Text = "Starting Ollama server... Please click Refresh in 3s.";
                    ellipseProviderDot.Fill = new SolidColorBrush((WpfColor)WpfColorConverter.ConvertFromString("#FF9500"));
                }
                else
                {
                    txtProviderStatus.Text = "No local models found (Start Ollama or LM Studio)";
                    ellipseProviderDot.Fill = new SolidColorBrush((WpfColor)WpfColorConverter.ConvertFromString("#FF3B30"));
                }
            }
        }

        private void OnModelSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cbModels.SelectedItem is AiModelOption model)
            {
                _currentModel = model;
                txtProviderStatus.Text = $"{model.Provider} Online ({model.Id})";
                ellipseProviderDot.Fill = new SolidColorBrush((WpfColor)WpfColorConverter.ConvertFromString("#34C759"));
            }
        }

        private async void OnRefreshModelsClicked(object sender, RoutedEventArgs e)
        {
            await RefreshLocalModelsAsync();
        }

        private async void OnUnloadModelClicked(object sender, RoutedEventArgs e)
        {
            if (btnUnloadModel != null) btnUnloadModel.IsEnabled = false;
            if (btnPurgeAllVram != null) btnPurgeAllVram.IsEnabled = false;
            txtProviderStatus.Text = "Ejecting model from VRAM...";
            ellipseProviderDot.Fill = new SolidColorBrush((WpfColor)WpfColorConverter.ConvertFromString("#FF9500"));

            try
            {
                var result = await _chatService.UnloadModelAsync(_currentModel, unloadAll: false);
                txtProviderStatus.Text = result.message;
                ellipseProviderDot.Fill = new SolidColorBrush((WpfColor)WpfColorConverter.ConvertFromString(result.success ? "#34C759" : "#8E8E93"));
                await RefreshLocalModelsAsync();
            }
            catch (Exception ex)
            {
                txtProviderStatus.Text = $"Error: {ex.Message}";
                ellipseProviderDot.Fill = new SolidColorBrush((WpfColor)WpfColorConverter.ConvertFromString("#FF3B30"));
            }
            finally
            {
                if (btnUnloadModel != null) btnUnloadModel.IsEnabled = true;
                if (btnPurgeAllVram != null) btnPurgeAllVram.IsEnabled = true;
            }
        }

        private async void OnUnloadAllModelsClicked(object sender, RoutedEventArgs e)
        {
            if (btnUnloadModel != null) btnUnloadModel.IsEnabled = false;
            if (btnPurgeAllVram != null) btnPurgeAllVram.IsEnabled = false;
            txtProviderStatus.Text = "Purging ALL models from VRAM...";
            ellipseProviderDot.Fill = new SolidColorBrush((WpfColor)WpfColorConverter.ConvertFromString("#FF9500"));

            try
            {
                var result = await _chatService.UnloadModelAsync(null, unloadAll: true);
                txtProviderStatus.Text = result.message;
                ellipseProviderDot.Fill = new SolidColorBrush((WpfColor)WpfColorConverter.ConvertFromString(result.success ? "#34C759" : "#8E8E93"));
                await RefreshLocalModelsAsync();
            }
            catch (Exception ex)
            {
                txtProviderStatus.Text = $"Error: {ex.Message}";
                ellipseProviderDot.Fill = new SolidColorBrush((WpfColor)WpfColorConverter.ConvertFromString("#FF3B30"));
            }
            finally
            {
                if (btnUnloadModel != null) btnUnloadModel.IsEnabled = true;
                if (btnPurgeAllVram != null) btnPurgeAllVram.IsEnabled = true;
            }
        }

        private void OnClearChatClicked(object sender, RoutedEventArgs e)
        {
            pnlChatHistory.Children.Clear();
            if (borderWelcome != null)
            {
                pnlChatHistory.Children.Add(borderWelcome);
            }
            _conversationHistory.Clear();
            txtChatBusyStatus.Text = "";
            txtFooterLog.Text = "Conversation history cleared.";
        }

        private void OnQuickChipClicked(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string prompt)
            {
                txtChatInput.Text = prompt;
                SubmitChatPrompt();
            }
        }

        private void OnChatInputPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                if (System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Shift))
                {
                    // Allow multi-line input on Shift+Enter
                    return;
                }

                e.Handled = true;
                SubmitChatPrompt();
            }
        }

        private void OnSendChatClicked(object sender, RoutedEventArgs e)
        {
            SubmitChatPrompt();
        }

        private async void SubmitChatPrompt()
        {
            if (_isChatBusy) return;

            string prompt = txtChatInput.Text.Trim();
            if (string.IsNullOrWhiteSpace(prompt)) return;

            if (_currentModel == null)
            {
                WpfMessageBox.Show(
                    "No local AI model is selected or available.\nPlease ensure Ollama (port 11434) or LM Studio (port 1234) is running, then click 'Refresh'.",
                    "Model Required",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            txtChatInput.Text = "";
            if (!string.IsNullOrWhiteSpace(txtProjectPath.Text))
            {
                GodotProjectManager.ActiveProjectPath = txtProjectPath.Text.Trim();
            }

            _isChatBusy = true;
            btnSendChat.IsEnabled = false;
            pbChatProgress.Visibility = Visibility.Visible;
            txtChatBusyStatus.Text = $"Prompting {_currentModel.Id}...";

            // Add user message bubble
            AddUserMessageBubble(prompt);

            _chatCts = new CancellationTokenSource();

            try
            {
                await _chatService.ExecuteChatRoundAsync(
                    _currentModel,
                    _conversationHistory,
                    prompt,
                    onStatusUpdate: status =>
                    {
                        Dispatcher.Invoke(() =>
                        {
                            txtChatBusyStatus.Text = status;
                            txtFooterLog.Text = status;
                        });
                    },
                    onToolExecuted: (toolName, args, result, isError) =>
                    {
                        Dispatcher.Invoke(() =>
                        {
                            AddToolExecutionCard(toolName, args, result, isError);
                        });
                    },
                    onAssistantResponse: (content, thinking) =>
                    {
                        Dispatcher.Invoke(() =>
                        {
                            AddAssistantMessageBubble(content, thinking);
                        });
                    },
                    _chatCts.Token);
            }
            catch (Exception ex)
            {
                AddAssistantMessageBubble($"❌ Error connecting to {_currentModel.Provider}: {ex.Message}", null);
            }
            finally
            {
                _isChatBusy = false;
                btnSendChat.IsEnabled = true;
                pbChatProgress.Visibility = Visibility.Collapsed;
                txtChatBusyStatus.Text = "";
                scrollChat.ScrollToEnd();
            }
        }

        private void AddUserMessageBubble(string text)
        {
            var bubble = new Border
            {
                HorizontalAlignment = HorizontalAlignment.Right,
                Background = (Brush)FindResource("ThemeAccent"),
                CornerRadius = new CornerRadius(12, 12, 2, 12),
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(60, 4, 0, 8),
                MaxWidth = 720
            };

            var tb = new TextBlock
            {
                Text = text,
                Foreground = Brushes.White,
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 18
            };

            bubble.Child = tb;
            pnlChatHistory.Children.Add(bubble);
            scrollChat.ScrollToEnd();
        }

        private void AddAssistantMessageBubble(string text, string? thinking)
        {
            var container = new Border
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                Background = (Brush)FindResource("ThemeCardBackground"),
                BorderBrush = (Brush)FindResource("ThemeBorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12, 12, 12, 2),
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(0, 4, 60, 8),
                MaxWidth = 780
            };

            var stack = new StackPanel();

            // Header: Assistant name & timestamp
            var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            header.Children.Add(new TextBlock
            {
                Text = "🤖 Godot Assistant",
                FontWeight = FontWeights.Bold,
                FontSize = 11,
                Foreground = (Brush)FindResource("ThemeAccent"),
                Margin = new Thickness(0, 0, 8, 0)
            });
            header.Children.Add(new TextBlock
            {
                Text = DateTime.Now.ToString("h:mm tt"),
                FontSize = 10,
                Foreground = (Brush)FindResource("ThemeSubText"),
                VerticalAlignment = VerticalAlignment.Center
            });
            stack.Children.Add(header);

            // Optional Thinking Expander
            if (!string.IsNullOrWhiteSpace(thinking))
            {
                var expander = new Expander
                {
                    Header = "💭 Model Reasoning Trace",
                    FontSize = 11,
                    Foreground = (Brush)FindResource("ThemeSubText"),
                    Margin = new Thickness(0, 0, 0, 8)
                };

                var thinkTb = new TextBox
                {
                    Text = thinking,
                    IsReadOnly = true,
                    TextWrapping = TextWrapping.Wrap,
                    Background = (Brush)FindResource("ThemeInputBackground"),
                    Foreground = (Brush)FindResource("ThemeSubText"),
                    BorderThickness = new Thickness(0),
                    Padding = new Thickness(8),
                    FontSize = 11,
                    FontFamily = new FontFamily("Consolas, Courier New, monospace"),
                    MaxHeight = 150,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto
                };

                expander.Content = thinkTb;
                stack.Children.Add(expander);
            }

            // Assistant Response Text
            var contentTb = new TextBox
            {
                Text = text,
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                Background = Brushes.Transparent,
                Foreground = (Brush)FindResource("ThemeForeground"),
                BorderThickness = new Thickness(0),
                FontSize = 13
            };
            stack.Children.Add(contentTb);

            container.Child = stack;
            pnlChatHistory.Children.Add(container);
            scrollChat.ScrollToEnd();
        }

        private void AddToolExecutionCard(string toolName, string args, string result, bool isError)
        {
            var card = new Border
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = (Brush)FindResource("ThemeInputBackground"),
                BorderBrush = isError ? (Brush)FindResource("ThemeWarning") : (Brush)FindResource("ThemeBorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 4, 0, 6)
            };

            var stack = new StackPanel();

            // Header row with icon and status
            var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var titleStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            titleStack.Children.Add(new TextBlock
            {
                Text = isError ? "⚠️" : "⚡",
                FontSize = 12,
                Margin = new Thickness(0, 0, 6, 0)
            });
            titleStack.Children.Add(new TextBlock
            {
                Text = $"Godot Tool: {toolName}",
                FontWeight = FontWeights.SemiBold,
                FontSize = 12,
                Foreground = (Brush)FindResource("ThemeForeground"),
                Margin = new Thickness(0, 0, 8, 0)
            });

            var badge = new Border
            {
                Background = isError ? new SolidColorBrush((WpfColor)WpfColorConverter.ConvertFromString("#FFEBEE")) : new SolidColorBrush((WpfColor)WpfColorConverter.ConvertFromString("#E8F5E9")),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2, 6, 2),
                VerticalAlignment = VerticalAlignment.Center
            };
            badge.Child = new TextBlock
            {
                Text = isError ? "FAILED" : "SUCCESS",
                FontSize = 9,
                FontWeight = FontWeights.Bold,
                Foreground = isError ? new SolidColorBrush((WpfColor)WpfColorConverter.ConvertFromString("#C62828")) : new SolidColorBrush((WpfColor)WpfColorConverter.ConvertFromString("#2E7D32"))
            };
            titleStack.Children.Add(badge);
            header.Children.Add(titleStack);

            stack.Children.Add(header);

            // Collapsible details expander
            var expander = new Expander
            {
                Header = "View Tool Arguments & Result",
                FontSize = 10,
                Foreground = (Brush)FindResource("ThemeSubText"),
                Margin = new Thickness(0, 4, 0, 0)
            };

            var detailsStack = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };

            detailsStack.Children.Add(new TextBlock { Text = "Arguments:", FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("ThemeSubText"), Margin = new Thickness(0, 0, 0, 2) });
            detailsStack.Children.Add(new TextBox
            {
                Text = args,
                IsReadOnly = true,
                Background = (Brush)FindResource("ThemeCardBackground"),
                Foreground = (Brush)FindResource("ThemeForeground"),
                BorderBrush = (Brush)FindResource("ThemeBorderBrush"),
                FontFamily = new FontFamily("Consolas, Courier New, monospace"),
                FontSize = 10,
                Padding = new Thickness(6),
                MaxHeight = 80,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Margin = new Thickness(0, 0, 0, 6)
            });

            detailsStack.Children.Add(new TextBlock { Text = "Response:", FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("ThemeSubText"), Margin = new Thickness(0, 0, 0, 2) });
            detailsStack.Children.Add(new TextBox
            {
                Text = result,
                IsReadOnly = true,
                Background = (Brush)FindResource("ThemeCardBackground"),
                Foreground = (Brush)FindResource("ThemeForeground"),
                BorderBrush = (Brush)FindResource("ThemeBorderBrush"),
                FontFamily = new FontFamily("Consolas, Courier New, monospace"),
                FontSize = 10,
                Padding = new Thickness(6),
                MaxHeight = 120,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            });

            expander.Content = detailsStack;
            stack.Children.Add(expander);

            card.Child = stack;
            pnlChatHistory.Children.Add(card);
            scrollChat.ScrollToEnd();
        }

        #endregion

        #region Theme Management

        private void LoadThemePreference()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegKeyPath, false);
                string savedTheme = key?.GetValue(ThemeValName) as string ?? "Light";
                bool isLight = savedTheme.Equals("Light", StringComparison.OrdinalIgnoreCase);

                if (isLight)
                {
                    rbLight.IsChecked = true;
                    ApplyTheme(true);
                }
                else
                {
                    rbDark.IsChecked = true;
                    ApplyTheme(false);
                }
            }
            catch
            {
                ApplyTheme(true);
            }
        }

        private void SaveThemePreference(bool isLight)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(RegKeyPath, true);
                key?.SetValue(ThemeValName, isLight ? "Light" : "Dark");
            }
            catch { }
        }

        private void OnThemeChanged(object sender, RoutedEventArgs e)
        {
            if (_isInitializing || rbLight == null || rbDark == null) return;
            bool isLight = rbLight.IsChecked == true;
            ApplyTheme(isLight);
            SaveThemePreference(isLight);
        }

        private void ApplyTheme(bool isLight)
        {
            if (isLight)
            {
                Resources["ThemeBackground"] = new SolidColorBrush((WpfColor)WpfColorConverter.ConvertFromString("#F5F5F7"));
                Resources["ThemeForeground"] = new SolidColorBrush((WpfColor)WpfColorConverter.ConvertFromString("#1C1C1E"));
                Resources["ThemeSubText"] = new SolidColorBrush((WpfColor)WpfColorConverter.ConvertFromString("#636366"));
                Resources["ThemeCardBackground"] = new SolidColorBrush((WpfColor)WpfColorConverter.ConvertFromString("#FFFFFF"));
                Resources["ThemeBorderBrush"] = new SolidColorBrush((WpfColor)WpfColorConverter.ConvertFromString("#E5E5EA"));
                Resources["ThemeInputBackground"] = new SolidColorBrush((WpfColor)WpfColorConverter.ConvertFromString("#F2F2F7"));
                Resources["ThemeButtonBackground"] = new SolidColorBrush((WpfColor)WpfColorConverter.ConvertFromString("#E5E5EA"));
                Resources["ThemeButtonHover"] = new SolidColorBrush((WpfColor)WpfColorConverter.ConvertFromString("#D1D1D6"));
            }
            else
            {
                Resources["ThemeBackground"] = new SolidColorBrush((WpfColor)WpfColorConverter.ConvertFromString("#1E1E1E"));
                Resources["ThemeForeground"] = new SolidColorBrush((WpfColor)WpfColorConverter.ConvertFromString("#F0F0F0"));
                Resources["ThemeSubText"] = new SolidColorBrush((WpfColor)WpfColorConverter.ConvertFromString("#AAAAAA"));
                Resources["ThemeCardBackground"] = new SolidColorBrush((WpfColor)WpfColorConverter.ConvertFromString("#252526"));
                Resources["ThemeBorderBrush"] = new SolidColorBrush((WpfColor)WpfColorConverter.ConvertFromString("#3E3E42"));
                Resources["ThemeInputBackground"] = new SolidColorBrush((WpfColor)WpfColorConverter.ConvertFromString("#2D2D30"));
                Resources["ThemeButtonBackground"] = new SolidColorBrush((WpfColor)WpfColorConverter.ConvertFromString("#333337"));
                Resources["ThemeButtonHover"] = new SolidColorBrush((WpfColor)WpfColorConverter.ConvertFromString("#3E3E40"));
            }
        }

        #endregion

        #region Actions & Event Handlers (Godot Workspace)

        private void OnBrowseGodotExe(object sender, RoutedEventArgs e)
        {
            var dlg = new WpfOpenFileDialog
            {
                Title = "Select Godot Engine 4.x Executable",
                Filter = "Godot Executable (*.exe)|*.exe|All Files (*.*)|*.*"
            };

            if (dlg.ShowDialog() == true)
            {
                txtGodotPath.Text = dlg.FileName;
                SetRegistryString(CustomGodotValName, dlg.FileName);
                txtFooterLog.Text = $"Custom Godot executable selected: {dlg.FileName}";
            }
        }

        private void OnBrowseProjectDir(object sender, RoutedEventArgs e)
        {
            using var dlg = new FormsFolderBrowserDialog
            {
                Description = "Select Godot 4 Project Directory (containing project.godot)",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = true
            };

            if (Directory.Exists(txtProjectPath.Text))
            {
                dlg.SelectedPath = txtProjectPath.Text;
            }

            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                txtProjectPath.Text = dlg.SelectedPath;
                SetRegistryString(LastProjectValName, dlg.SelectedPath);
                txtFooterLog.Text = $"Project directory selected: {dlg.SelectedPath}";
            }
        }

        private void OnInstallAddon(object sender, RoutedEventArgs e)
        {
            string projectDir = txtProjectPath.Text.Trim();
            if (string.IsNullOrEmpty(projectDir) || !Directory.Exists(projectDir))
            {
                WpfMessageBox.Show(
                    "Please select a valid Godot project directory first.",
                    "Project Required",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string sourceAddon = Path.Combine(baseDir, "addons", "godot_mcp");

                if (!Directory.Exists(sourceAddon))
                {
                    string fallbackAddon = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "addons", "godot_mcp"));
                    if (Directory.Exists(fallbackAddon)) sourceAddon = fallbackAddon;
                }

                if (!Directory.Exists(sourceAddon))
                {
                    WpfMessageBox.Show(
                        $"Could not locate plugin files to copy.\nLooked at: {sourceAddon}",
                        "Plugin Source Missing",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return;
                }

                string destAddon = Path.Combine(projectDir, "addons", "godot_mcp");
                CopyDirectory(sourceAddon, destAddon);

                txtFooterLog.Text = $"✔ Plugin installed to {destAddon}";
                WpfMessageBox.Show(
                    "Godot MCP Plugin successfully installed into project!\n\nNext step:\nOpen Project -> Project Settings -> Plugins in Godot and check 'Enable'.",
                    "Addon Installed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show($"Failed to install addon: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnLaunchEditor(object sender, RoutedEventArgs e)
        {
            string projectDir = txtProjectPath.Text.Trim();
            string exePath = txtGodotPath.Text.Trim();

            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
            {
                var detection = GodotDiscovery.Detect();
                if (detection.IsFound) exePath = detection.ExecutablePath;
                else
                {
                    WpfMessageBox.Show("Please select a valid Godot executable first.", "Godot Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = true
                };

                if (!string.IsNullOrEmpty(projectDir) && Directory.Exists(projectDir))
                {
                    psi.Arguments = $"--editor --path \"{projectDir}\"";
                }

                Process.Start(psi);
                txtFooterLog.Text = $"Launched Godot Editor: {exePath}";
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show($"Failed to launch Godot: {ex.Message}", "Launch Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnCheckSyntax(object sender, RoutedEventArgs e)
        {
            string projectDir = txtProjectPath.Text.Trim();
            if (string.IsNullOrEmpty(projectDir) || !Directory.Exists(projectDir))
            {
                WpfMessageBox.Show("Please select a Godot project directory to check.", "Project Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            txtFooterLog.Text = "Running headless GDScript syntax check...";
            var result = GodotProjectManager.CheckSyntax(projectDir);

            bool success = result.TryGetValue("success", out var s) && s is bool b && b;
            string stdout = result.TryGetValue("stdout", out var outVal) ? outVal?.ToString() ?? "" : "";
            string stderr = result.TryGetValue("stderr", out var errVal) ? errVal?.ToString() ?? "" : "";

            if (success)
            {
                WpfMessageBox.Show($"Syntax check passed cleanly!\n\n{stdout}", "Syntax Clean", MessageBoxButton.OK, MessageBoxImage.Information);
                txtFooterLog.Text = "Syntax check passed cleanly.";
            }
            else
            {
                WpfMessageBox.Show($"Syntax check reported issues:\n\n{stderr}\n{stdout}", "Syntax Issues Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                txtFooterLog.Text = "Syntax check reported issues.";
            }
        }

        private void OnOpenProjectFolder(object sender, RoutedEventArgs e)
        {
            string projectDir = txtProjectPath.Text.Trim();
            if (!string.IsNullOrEmpty(projectDir) && Directory.Exists(projectDir))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = projectDir,
                    UseShellExecute = true
                });
            }
        }

        private void OnSyncAssistants(object sender, RoutedEventArgs e)
        {
            string currentExe = Environment.ProcessPath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "GodotMCP.exe");
            var synced = new List<string>();

            if (chkClaude.IsChecked == true)
            {
                if (ClientConfigSync.RegisterGodotServer(ClientConfigSync.ClientClaude, currentExe))
                    synced.Add("Claude Desktop");
            }

            if (chkLmStudio.IsChecked == true)
            {
                if (ClientConfigSync.RegisterGodotServer(ClientConfigSync.ClientLmStudio, currentExe))
                    synced.Add("LM Studio");
            }

            if (chkOllama.IsChecked == true)
            {
                if (ClientConfigSync.RegisterGodotServer(ClientConfigSync.ClientOllama, currentExe))
                    synced.Add("Ollama");
            }

            if (synced.Count > 0)
            {
                string list = string.Join(", ", synced);
                txtFooterLog.Text = $"✔ Registered Godot MCP server with: {list}";
                WpfMessageBox.Show(
                    $"Godot MCP server registered successfully with:\n\n• {string.Join("\n• ", synced)}\n\nExecutable target:\n{currentExe}",
                    "Sync Completed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            else
            {
                WpfMessageBox.Show("No assistant configs were selected or updated.", "Sync Info", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void OnExploreProSuite(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://unsignedexe.com",
                    UseShellExecute = true
                });
            }
            catch
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "https://github.com/NoizeMaykr",
                        UseShellExecute = true
                    });
                }
                catch { }
            }
        }

        #endregion

        #region Window & Tray Integration

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        private const int SW_RESTORE = 9;

        public void RestoreAndActivate()
        {
            Dispatcher.Invoke(() =>
            {
                if (!IsVisible)
                {
                    Show();
                }

                if (WindowState == WindowState.Minimized)
                {
                    WindowState = WindowState.Normal;
                }

                Activate();
                Focus();

                var helper = new System.Windows.Interop.WindowInteropHelper(this);
                if (helper.Handle != IntPtr.Zero)
                {
                    ShowWindow(helper.Handle, SW_RESTORE);
                    SetForegroundWindow(helper.Handle);
                }
            });
        }

        protected override void OnStateChanged(EventArgs e)
        {
            base.OnStateChanged(e);
            if (WindowState == WindowState.Minimized)
            {
                Hide();
                HubTrayAgent.NotifyMinimized();
            }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = true;
            Hide();
            HubTrayAgent.NotifyMinimized();
        }

        #endregion

        #region Registry & File Helpers

        private static string? GetRegistryString(string valueName)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegKeyPath, false);
                return key?.GetValue(valueName) as string;
            }
            catch { return null; }
        }

        private static void SetRegistryString(string valueName, string value)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(RegKeyPath, true);
                key?.SetValue(valueName, value);
            }
            catch { }
        }

        private static void CopyDirectory(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);
            foreach (var file in Directory.GetFiles(sourceDir))
            {
                string destFile = Path.Combine(destDir, Path.GetFileName(file));
                File.Copy(file, destFile, true);
            }

            foreach (var subDir in Directory.GetDirectories(sourceDir))
            {
                string destSub = Path.Combine(destDir, Path.GetFileName(subDir));
                CopyDirectory(subDir, destSub);
            }
        }

        #endregion
    }
}
