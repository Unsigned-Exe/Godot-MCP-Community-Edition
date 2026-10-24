#nullable enable
using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MCP_Godot4
{
    /// <summary>
    /// System tray agent providing dynamic icon badges reflecting Godot Editor connection
    /// and AI tool execution status, balloon notifications, and quick action context menus.
    /// </summary>
    public static class HubTrayAgent
    {
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool DestroyIcon(IntPtr handle);

        private static NotifyIcon? _notifyIcon;
        private static ContextMenuStrip? _contextMenu;
        private static ToolStripMenuItem? _statusHeaderItem;
        private static Icon? _currentIcon;
        private static Action? _openDashboardCallback;
        private static Action? _installAddonCallback;
        private static Action? _launchEditorCallback;
        private static Action? _syncClientsCallback;

        public static void Initialize(
            Action openDashboardCallback,
            Action? installAddonCallback = null,
            Action? launchEditorCallback = null,
            Action? syncClientsCallback = null)
        {
            _openDashboardCallback = openDashboardCallback;
            _installAddonCallback = installAddonCallback;
            _launchEditorCallback = launchEditorCallback;
            _syncClientsCallback = syncClientsCallback;

            AppDomain.CurrentDomain.ProcessExit += (s, e) => Shutdown();

            try
            {
                System.Windows.Forms.Application.EnableVisualStyles();
            }
            catch { }

            _contextMenu = new ContextMenuStrip();

            _statusHeaderItem = new ToolStripMenuItem("Godot MCP Hub: Waiting for Editor...")
            {
                Enabled = false,
                Font = new Font(Control.DefaultFont, FontStyle.Bold)
            };
            _contextMenu.Items.Add(_statusHeaderItem);
            _contextMenu.Items.Add(new ToolStripSeparator());

            var openItem = new ToolStripMenuItem("Open Dashboard", null, (s, e) => _openDashboardCallback?.Invoke());
            _contextMenu.Items.Add(openItem);

            var installItem = new ToolStripMenuItem("Install Addon to Project...", null, (s, e) => _installAddonCallback?.Invoke());
            _contextMenu.Items.Add(installItem);

            var launchItem = new ToolStripMenuItem("Launch Godot Editor", null, (s, e) => _launchEditorCallback?.Invoke());
            _contextMenu.Items.Add(launchItem);

            var syncItem = new ToolStripMenuItem("Sync AI Clients (Claude, LM Studio, Ollama)", null, (s, e) => _syncClientsCallback?.Invoke());
            _contextMenu.Items.Add(syncItem);

            _contextMenu.Items.Add(new ToolStripSeparator());

            var exitItem = new ToolStripMenuItem("Exit", null, (s, e) =>
            {
                Shutdown();
                Environment.Exit(0);
            });
            _contextMenu.Items.Add(exitItem);

            _currentIcon = CreateStatusIcon(Color.FromArgb(33, 150, 243), isBusy: false);
            _notifyIcon = new NotifyIcon
            {
                Text = "Godot MCP Hub (Community Edition)",
                Icon = _currentIcon,
                ContextMenuStrip = _contextMenu,
                Visible = true
            };

            _notifyIcon.DoubleClick += (s, e) => _openDashboardCallback?.Invoke();
            _notifyIcon.MouseClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                {
                    _openDashboardCallback?.Invoke();
                }
            };
        }

        public static void UpdateState(bool isConnected, bool isBusy, string? message = null)
        {
            if (_notifyIcon == null) return;

            Color accentColor;
            string statusText;

            if (isBusy)
            {
                accentColor = Color.FromArgb(255, 193, 7); // Amber
                statusText = "Godot MCP: Executing AI Tool...";
            }
            else if (isConnected)
            {
                accentColor = Color.FromArgb(76, 175, 80); // Green
                statusText = "Godot MCP: Editor Connected (Live)";
            }
            else
            {
                accentColor = Color.FromArgb(33, 150, 243); // Blue
                statusText = "Godot MCP: Waiting for Editor (Port 6505)";
            }

            if (_statusHeaderItem != null)
            {
                _statusHeaderItem.Text = statusText;
            }

            var newIcon = CreateStatusIcon(accentColor, isBusy);
            var oldIcon = _currentIcon;
            _currentIcon = newIcon;

            _notifyIcon.Icon = newIcon;
            _notifyIcon.Text = statusText.Length > 63 ? statusText.Substring(0, 63) : statusText;

            oldIcon?.Dispose();
        }

        public static void NotifyMinimized()
        {
            try
            {
                _notifyIcon?.ShowBalloonTip(
                    2500,
                    "Godot MCP Hub",
                    "Running in system tray. Double-click or click icon to reopen.",
                    ToolTipIcon.Info);
            }
            catch { }
        }

        public static void Shutdown()
        {
            try
            {
                if (_notifyIcon != null)
                {
                    _notifyIcon.Visible = false;
                    _notifyIcon.Dispose();
                    _notifyIcon = null;
                }
                _currentIcon?.Dispose();
                _currentIcon = null;
            }
            catch { }
        }

        private static Icon CreateStatusIcon(Color dotColor, bool isBusy)
        {
            const int size = 32;
            using var bmp = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);

                float s = size / 32f;

                if (isBusy)
                {
                    // Busy state: Radiant rotating gear / star badge
                    float r = 13.5f * s;
                    float cx = size / 2f;
                    float cy = size / 2f;
                    int points = 8;
                    PointF[] pts = new PointF[points * 2];

                    for (int i = 0; i < points * 2; i++)
                    {
                        float rad = (i % 2 == 0) ? r : r * 0.65f;
                        double angle = i * Math.PI / points;
                        pts[i] = new PointF(cx + (float)(rad * Math.Cos(angle)), cy + (float)(rad * Math.Sin(angle)));
                    }

                    using (var bgBrush = new LinearGradientBrush(
                        new PointF(0, 0), new PointF(size, size),
                        Color.FromArgb(255, 235, 59), Color.FromArgb(245, 124, 0)))
                    {
                        g.FillPolygon(bgBrush, pts);
                    }

                    using (var pen = new Pen(Color.FromArgb(220, 78, 42, 0), Math.Max(1f, 1.2f * s)))
                    {
                        g.DrawPolygon(pen, pts);
                    }
                }
                else
                {
                    // Hexagonal Prism Nexus badge with dotColor accent
                    float pad = 2f * s;
                    float r = (size / 2f) - pad;
                    float cx = size / 2f;
                    float cy = size / 2f;

                    PointF[] hex = new PointF[6];
                    for (int i = 0; i < 6; i++)
                    {
                        double angle = (Math.PI / 3.0) * i - (Math.PI / 6.0);
                        hex[i] = new PointF(cx + (float)(r * Math.Cos(angle)), cy + (float)(r * Math.Sin(angle)));
                    }

                    using (var bgBrush = new LinearGradientBrush(
                        new PointF(0, 0), new PointF(size, size),
                        Color.FromArgb(36, 39, 58), Color.FromArgb(14, 15, 24)))
                    {
                        g.FillPolygon(bgBrush, hex);
                    }

                    using (var pen = new Pen(dotColor, Math.Max(1.2f, 1.8f * s)))
                    {
                        g.DrawPolygon(pen, hex);
                    }

                    // Center radiant dot
                    float dotR = 6.5f * s;
                    using (var dotBrush = new SolidBrush(dotColor))
                    {
                        g.FillEllipse(dotBrush, cx - dotR, cy - dotR, dotR * 2, dotR * 2);
                    }

                    // Inner glossy highlight for high contrast depth
                    using (var hiBrush = new SolidBrush(Color.FromArgb(160, 255, 255, 255)))
                    {
                        g.FillEllipse(hiBrush, cx - 2f * s, cy - 4.5f * s, 4f * s, 2.5f * s);
                    }
                }
            }

            IntPtr hIcon = bmp.GetHicon();
            Icon icon = (Icon)Icon.FromHandle(hIcon).Clone();
            DestroyIcon(hIcon);
            return icon;
        }
    }
}
