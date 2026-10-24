# itch.io Storefront Setup & Metadata Guide
**Project**: Godot-MCP Community Edition  
**Target URL**: `https://<your-username>.itch.io/godot-mcp`

---

## 1. Project Configuration Fields

| Setting | Value / Selection |
| :--- | :--- |
| **Title** | `Godot-MCP Community Edition` |
| **Short line / Tagline** | `Open-Source Model Context Protocol (MCP) Desktop Hub & Editor Integration for Godot Engine 4.x` |
| **Classification** | **Tools** |
| **Kind of project** | **Downloadable** |
| **Release status** | **Released** |
| **Pricing** | **$0 or donate** *(Pay-What-You-Want)* |
| **Suggested Donation** | `$5.00` |
| **Platforms** | Check **Windows** |
| **Tags** | `godot-engine`, `godot-4`, `tools`, `development-tools`, `ai-tools`, `mcp`, `open-source`, `productivity`, `c-sharp`, `gdscript` |
| **Community** | **Comments** enabled (or link to GitHub Discussions) |

---

## 2. Storefront Description (Copy & Paste Ready)

```markdown
# 🤖 Godot-MCP (Community Edition)
### Bring Local & Cloud AI Assistants Directly into the Godot 4 Editor

**Godot-MCP** is an open-source, portable desktop GUI hub and Model Context Protocol (MCP) server that connects AI assistants (**Claude Desktop / Code**, **LM Studio**, **Ollama**, **Antigravity**) directly into your active **Godot Engine 4.x** projects.

With a single click, sync configurations, inspect active scene trees in real time, modify nodes, and automate editor workflows without manual context switching.

---

### ✨ Key Features

- **🎮 Dual-Mode Desktop Binary**: Double-click to launch the modern WPF dashboard with Light/Dark themes and 1-click AI assistant sync; or run headlessly via CLI as a silent stdio JSON-RPC 2.0 server.
- **⚡ 1-Click AI Client Sync**: Automatically configures `claude_desktop_config.json`, LM Studio's `mcp.json`, and Ollama's `mcp.json`.
- **🔌 Zero-Configuration WebSocket Bridge**: Low-latency bidirectional communication between the desktop hub and the active Godot 4 editor over local loopback (`ws://127.0.0.1:6505`).
- **🛡 Dedicated VRAM Purge (Nuclear Option)**: Permanent toolbar controls to eject models or completely flush GPU VRAM across Ollama and LM Studio (`lms unload --all`).
- **🚀 Pre-Flight Godot AssetLib Packager**: Validates `plugin.cfg` syntax and automatically compiles a clean `godot_mcp_assetlib.zip` distribution archive.
- **📦 100% Standalone & Portable**: Self-contained .NET 8 binary. No runtime installation, Python setups, or external dependencies required.

---

### 📥 What's in the Download?

- `GodotMCP.exe` — Self-contained Windows desktop hub & stdio bridge executable.
- `addons/godot_mcp/` — Complete, ready-to-use Godot 4.x editor addon (copy into your Godot project's root).
- `Godot_MCP_Manual.pdf` — Complete user and architecture manual.
- `LICENSE` & `DISCLAIMER.md` — Legal terms and hardware safety disclaimers.

---

### ⚠️ First-Time Launch & Windows SmartScreen Notice

Because Godot-MCP is an open-source, community-distributed binary without an expensive corporate Code Signing Certificate, Windows Defender SmartScreen may display an alert:

1. Click **"More info"** on the blue popup.
2. Click **"Run anyway"**.

Godot-MCP is 100% open-source under the MIT license. You can inspect the complete source code or build it directly from source on [GitHub](https://github.com/Unsigned-Exe/Godot-MCP-Community-Edition).

#### Verified SHA-256 Checksums
- `GodotMCP-v1.0.0-windows-64.zip`: `47548384f7939f48f17582a24c7ed35d3f3fdf1f86ffa7e27ba7dfff2b189d3b`
- `GodotMCP.exe`: `8879f354613ff1b88aacc153484eab7de18c8dbce56ee5e86a492304603cb4b6`

---

### 🔒 Network & Privacy Guarantee

- **Local Loopback Only**: Godot-MCP binds strictly to `127.0.0.1:6505`. It never exposes ports to external networks or `0.0.0.0`.
- **Zero Telemetry**: No analytics, telemetry pings, tracking beacons, or diagnostic reports are compiled into the application.
- **Your Code Stays Local**: Your scene data and GDScript files remain on your workstation and are only shared with the AI models you explicitly configure.

---

### ⚖️ Legal & Trademark Disclaimers

- **Godot Engine**: Godot-MCP is an independent, open-source community tool. It is not affiliated with, sponsored by, or endorsed by the Godot Engine team, W4 Games, or Juan Linietsky. "Godot Engine" is a registered trademark of Juan Linietsky and Ariel Manzur.
- **License**: Released under the permissive [MIT License](https://opensource.org/licenses/MIT). Copyright © 2026 Unsigned Exe Studios.
- **Hardware & Thermals**: Local AI model inference depends on your local GPU configuration (LM Studio, Ollama). Ensure adequate hardware cooling when running heavy local LLM workloads alongside the Godot Editor.
```

---

## 3. Uploading Files via Web Interface

Under the **Downloadable files** section on itch.io:
1. Click **Upload files**.
2. Select `dist/GodotMCP-v1.0.0-windows-64.zip` (66.7 MB).
3. Check the **Windows** platform icon next to the file.
4. Set Display Name: `Godot-MCP v1.0.0 (Windows x64 Standalone Bundle)`.
