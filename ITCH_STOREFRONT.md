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

## 2. Storefront Description (HTML - Copy & Paste Ready for itch.io)

```html
<h1>🤖 Godot-MCP (Community Edition)</h1>
<h3>Bring Local & Cloud AI Assistants Directly into the Godot 4 Editor</h3>

<p><strong>Godot-MCP</strong> is an open-source, portable desktop GUI hub and Model Context Protocol (MCP) server that connects AI assistants (<strong>Claude Desktop</strong>, <strong>LM Studio</strong>, and <strong>Ollama</strong>) directly into your active <strong>Godot Engine 4.x</strong> projects.</p>

<p>With a single click, sync client configurations, inspect active scene trees in real time, modify nodes, and automate editor workflows without manual context switching.</p>

<hr />

<h3>🛠 Architecture &amp; Development Disclosure</h3>
<blockquote>
  <p><strong>Development Methodology:</strong> Human-Directed Agentic Co-Engineering. Conceived, directed, and architected by an experienced human developer (Unsigned Exe Studios); iteratively drafted, synthesized, and refined in tight pair-programming collaboration with cloud and local AI models.</p>
  <p><strong>Human Oversight &amp; QA:</strong> Every end-user subsystem—from the .NET 8 WebSocket bridge and single-instance mutex lifecycle to the GDScript EditorPlugin and 1-click client configuration synchronizer—was guided by human design constraints, tested live against real Godot 4 editor sessions, manually debugged, and verified via automated test suites.</p>
  <p><strong>Code Quality &amp; Safety:</strong> Fully typed C# and GDScript with zero external telemetry, zero unmetered network calls, and strict local loopback (<code>127.0.0.1</code>) process isolation.</p>
  <p><strong>License &amp; Philosophy:</strong> 100% free, portable, and open-source under the MIT License. Built with modern agentic tooling, held to strict engineering standards, and made freely available for the Godot community.</p>
</blockquote>

<hr />

<h3>✨ Key Features</h3>
<ul>
  <li><strong>🎮 Dual-Mode Desktop Binary:</strong> Double-click to launch the modern WPF dashboard with Light/Dark themes and 1-click AI assistant sync; or run headlessly via CLI as a silent stdio JSON-RPC 2.0 server.</li>
  <li><strong>⚡ 1-Click AI Client Sync:</strong> Automatically configures <code>claude_desktop_config.json</code>, LM Studio's <code>mcp.json</code>, and Ollama's <code>mcp.json</code>.</li>
  <li><strong>🔌 Zero-Configuration WebSocket Bridge:</strong> Low-latency bidirectional communication between the desktop hub and the active Godot 4 editor over local loopback (<code>ws://127.0.0.1:6505</code>).</li>
  <li><strong>🛡 Dedicated VRAM Purge (Nuclear Option):</strong> Permanent toolbar controls to eject models or completely flush GPU VRAM across Ollama and LM Studio (<code>lms unload --all</code>).</li>
  <li><strong>🚀 Pre-Flight Godot AssetLib Packager:</strong> Validates <code>plugin.cfg</code> syntax and automatically compiles a clean <code>godot_mcp_assetlib.zip</code> distribution archive.</li>
  <li><strong>📦 100% Standalone &amp; Portable:</strong> Self-contained .NET 8 binary. No runtime installation, Python setups, or external dependencies required.</li>
</ul>

<hr />

<h3>📥 What's in the Download?</h3>
<ul>
  <li><code>GodotMCP.exe</code> — Self-contained Windows desktop hub &amp; stdio bridge executable.</li>
  <li><code>addons/godot_mcp/</code> — Complete, ready-to-use Godot 4.x editor addon (copy into your Godot project's root).</li>
  <li><code>Godot_MCP_Manual.pdf</code> — Complete user and architecture manual.</li>
  <li><code>LICENSE</code> &amp; <code>DISCLAIMER.md</code> — Permissive MIT legal terms and hardware safety notices.</li>
</ul>

<hr />

<h3>⚠️ First-Time Launch &amp; Windows SmartScreen Notice</h3>
<p>Because Godot-MCP is an open-source, community-distributed binary without an expensive corporate Code Signing Certificate, Windows Defender SmartScreen may display an informational banner:</p>
<ol>
  <li>Click <strong>"More info"</strong> on the popup window.</li>
  <li>Click <strong>"Run anyway"</strong>.</li>
</ol>
<p>Godot-MCP is 100% open-source under the MIT license. You can inspect the complete source code or compile it directly from source on <a href="https://github.com/Unsigned-Exe/Godot-MCP-Community-Edition" target="_blank" rel="noopener">GitHub</a>.</p>

<h4>Verified SHA-256 Checksums</h4>
<ul>
  <li><code>GodotMCP-v1.0.0-windows-64.zip</code>: <code>47548384f7939f48f17582a24c7ed35d3f3fdf1f86ffa7e27ba7dfff2b189d3b</code></li>
  <li><code>GodotMCP.exe</code>: <code>8879f354613ff1b88aacc153484eab7de18c8dbce56ee5e86a492304603cb4b6</code></li>
</ul>

<hr />

<h3>🔒 Network &amp; Privacy Guarantee</h3>
<ul>
  <li><strong>Local Loopback Only:</strong> Godot-MCP binds strictly to <code>127.0.0.1:6505</code>. It never exposes ports to external networks or <code>0.0.0.0</code>.</li>
  <li><strong>Zero Telemetry:</strong> No analytics, telemetry pings, tracking beacons, or diagnostic reports are compiled into the application.</li>
  <li><strong>Your Code Stays Local:</strong> Your scene data and GDScript files remain exclusively on your workstation and are only shared with the AI models you explicitly configure.</li>
</ul>

<hr />

<h3>⚖️ Legal &amp; Trademark Disclaimers</h3>
<ul>
  <li><strong>Godot Engine:</strong> Godot-MCP is an independent, open-source community tool. It is not affiliated with, sponsored by, or endorsed by the Godot Engine team, W4 Games, or Juan Linietsky. "Godot Engine" is a registered trademark of Juan Linietsky and Ariel Manzur.</li>
  <li><strong>License:</strong> Released under the permissive <a href="https://opensource.org/licenses/MIT" target="_blank" rel="noopener">MIT License</a>. Copyright &copy; 2026 Unsigned Exe Studios.</li>
  <li><strong>Hardware &amp; Thermals:</strong> Local AI model inference depends on your local GPU configuration (LM Studio, Ollama). Ensure adequate hardware cooling when running heavy local LLM workloads alongside the Godot Editor.</li>
</ul>
```

---

## 3. Uploading Files via Web Interface

Under the **Downloadable files** section on itch.io:
1. Click **Upload files**.
2. Select `dist/GodotMCP-v1.0.0-windows-64.zip` (66.7 MB).
3. Check the **Windows** platform icon next to the file.
4. Set Display Name: `Godot-MCP v1.0.0 (Windows x64 Standalone Bundle)`.
