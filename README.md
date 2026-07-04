# 🔬 ModelPulse

### *The always-on AI runtime monitor for local developers*

**A native Windows desktop overlay and tray agent that keeps your AI workload telemetry visible — without sacrificing the resources your models need.**

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com)
[![Platform](https://img.shields.io/badge/Platform-Windows-0078D6?logo=windows&logoColor=white)](https://www.microsoft.com/windows)
[![UI](https://img.shields.io/badge/UI-WPF-512BD4?logo=dotnet&logoColor=white)](https://learn.microsoft.com/en-us/dotnet/desktop/wpf)
[![Status](https://img.shields.io/badge/Status-Active%20Development-brightgreen)](https://github.com/vijaytank/ModelPulse)
[![License](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

[![Ollama](https://img.shields.io/badge/Supports-Ollama-black?logo=llm&logoColor=white)](https://ollama.com)
[![llama.cpp](https://img.shields.io/badge/Supports-llama.cpp-blue)](https://github.com/ggerganov/llama.cpp)
[![GPU](https://img.shields.io/badge/GPU-NVIDIA%20%7C%20AMD%20%7C%20Intel-76B900?logo=nvidia&logoColor=white)](https://developer.nvidia.com/nvml)
[![Tests](https://img.shields.io/badge/Tests-xUnit%20%7C%20FluentAssertions-success)](https://xunit.net)

> 🧠 **Running local LLMs is awesome. Monitoring them shouldn't require keeping Task Manager open.**

---

## 📌 What Is ModelPulse?

**ModelPulse** is a lightweight, native Windows utility purpose-built for developers running local AI inference with tools like **Ollama** and **llama.cpp**. It lives in your system tray and projects a compact, always-on-top overlay showing exactly what your hardware and local models are doing — in real time.

It is designed with one guiding principle:  
> **The monitor should never compete with the models it monitors.**

ModelPulse targets an idle CPU overhead under **0.1%** and a managed memory footprint under **15 MB** — leaving your RAM and VRAM available for what actually matters: running your local AI workload.

---

## ❓ The Problem

Running large language models locally is increasingly common, but monitoring those workloads remains surprisingly cumbersome.

When a developer runs `ollama run gemma3` or launches a llama.cpp server, they have no clean way to see:

| What they want to know | What they have to do today |
|---|---|
| Is my model still loaded in VRAM? | Open a terminal, run `ollama ps` |
| What is my current VRAM usage? | Open Task Manager or `nvidia-smi` |
| Is my model generating tokens right now? | Check llama.cpp stdout logs |
| Is my system under memory pressure? | Alt-Tab to Performance Monitor |
| Did my model get unloaded automatically? | Poll the terminal again |

Every one of those actions interrupts the development workflow. And browser-based dashboards that could replace this consume additional RAM your models need.

---

## 💡 The Solution

ModelPulse replaces all of that context-switching with a **tiny, always-on-top native overlay** that surfaces:

- 🖥 **CPU, RAM, GPU utilization** — live system health at a glance
- 🤖 **Active model name, VRAM footprint, and context length** — from Ollama and llama.cpp
- ⚡ **Token throughput** (t/s) — prompt and generation speed per active session
- ⏱ **Model unload timer** — when Ollama will automatically evict a model from VRAM
- 🔴 **Memory pressure alerts** — with configurable cooldowns to prevent notification fatigue
- 📈 **5-minute rolling trend** — see how your system has been behaving during a session

It runs entirely **locally**, stores only a compact JSON settings file, and never phones home.

---

## ✨ Feature Highlights

### 🖼 Two Display Modes

<table>
<tr>
<td width="50%">

**Compact Overlay**

A minimal, edge-pinned strip showing the most important signals. Click to expand, auto-collapses during inactivity.

- CPU usage %
- RAM used / available
- GPU utilization %
- VRAM used (MB)
- Active runtime badge
- Live t/s (tokens per second)
- Busy / idle status indicator

</td>
<td width="50%">

**Expanded Overlay**

Full model and session detail panel. Shown on click or via tray. Hides automatically when not in focus.

- Loaded model name and size
- VRAM footprint and context length
- Model unload timer (`expires_at`)
- Prompt throughput and generation throughput
- Per-runtime sections (Ollama | llama.cpp)
- Rolling 5-minute session averages
- Runtime version for diagnostics

</td>
</tr>
</table>

When both Ollama and llama.cpp are running simultaneously, the expanded overlay defaults to a **runtime toggle view** — keeping the panel readable while still separating the two data streams.

---

### 🎮 Multi-Vendor GPU Telemetry

ModelPulse implements a strict hardware query fallback chain, requiring zero third-party wrapper libraries:

```
NVML (nvml.dll) ──▶ DXGI (IDXGIAdapter3) ──▶ WMI Win32_VideoController ──▶ "Unavailable"
```

| Provider | Target | Data Available |
|---|---|---|
| **NVML** | NVIDIA GeForce / Quadro / Tesla | GPU load %, VRAM used/total, temperature |
| **DXGI** | AMD, Intel, NVIDIA (all DirectX) | VRAM budget and current allocation |
| **WMI / PDH** | Integrated, legacy, any card | Basic card info, estimated VRAM |
| **Degraded** | No provider succeeded | Displays `Unavailable` — never fake zeroes |

GPU metrics that cannot be reliably queried are shown as `Unavailable` rather than displaying misleading zero values.

---

### ⚡ Adaptive Polling Engine

ModelPulse automatically scales its query frequency based on what your runtimes are doing:

| State | Polling Interval | Behavior |
|---|---|---|
| **Active Inference** | 500ms – 1s | High-frequency updates while a model is generating |
| **Idle** | 2s – 5s | Low-frequency background queries |
| **Unreachable** | Exponential backoff | Automatic backoff when a runtime endpoint is offline |
| **Low-Power Mode** | User-configured | Optional override for battery-constrained machines |

Requests to Ollama and llama.cpp are batched within the same polling cycle to reduce HTTP overhead.

---

### 🔔 Intelligent Alert System

Alerts are scoped per runtime, so an Ollama event never pollutes a llama.cpp notification.

- **Memory pressure** — warns when VRAM or system RAM crosses a configured threshold
- **Runtime disconnected** — notifies when an enabled runtime becomes unreachable
- **Performance drop** — flags significant reductions in token throughput
- **Severity tiers** — `info`, `warning`, `critical` — critical alerts surface prominently, info stays quiet
- **Configurable cooldowns** — prevents notification fatigue by enforcing minimum repeat intervals per alert type

---

### 🧰 Diagnostics Snapshot Export

On demand, ModelPulse can serialize a full snapshot to JSON, capturing:
- Runtime version metadata (Ollama, llama.cpp)
- Current polling configuration and mode
- Detected runtime list
- Unknown or newly added API fields seen since startup
- Recent alert summary

This makes debugging runtime version mismatches and adapter drift significantly easier.

---

## 🏗 Architecture Overview

See the full architectural specification in [`Project_brain/architecture.md`](Project_brain/architecture.md).

```
                ┌─────────────────────────────────┐
                │          Collector Core          │
                │  (Background thread polling)     │
                └────────────┬────────────────────┘
                             │ BehaviorSubject<CollectorSnapshot>
           ┌─────────────────┼─────────────────────┐
           ▼                 ▼                      ▼
    ┌─────────────┐  ┌──────────────┐  ┌──────────────────────┐
    │  History    │  │ Alert Engine │  │     UI Mediator       │
    │  Aggregator │  │  (Cooldowns) │  │  (Sample + Dispatch)  │
    │ (5-min avg) │  │ (Per-runtime)│  │   Dispatcher.Invoke   │
    └─────────────┘  └──────────────┘  └──────────────────────┘
                                                │
                                    ┌───────────▼───────────┐
                                    │   WPF ViewModel Layer │
                                    │   (Diff-based updates)│
                                    └───────────────────────┘
```

All background subscribers process on `TaskPoolScheduler`. The WPF UI thread is never blocked by I/O or polling. ViewModel updates are diffed before raising `PropertyChanged` to minimize layout recalculations.

---

## 📋 Telemetry Data Model

See the full field definitions and compatibility matrix in [`Project_brain/telemetry-schema.md`](Project_brain/telemetry-schema.md).

### System Telemetry (Always Active)

| Field | Source | Notes |
|---|---|---|
| `cpu_percent` | Windows performance counters | Always displayed |
| `ram_used_mb` | Windows memory API | Always displayed |
| `gpu_percent` | NVML → DXGI → WMI | Shows `Unavailable` if not queryable |
| `vram_used_mb` | NVML → DXGI → WMI | Shows `Unavailable` if not queryable |
| `widget_ram_mb` | Self-telemetry | Ensures ModelPulse stays within budget |

### Ollama Runtime Fields (Conditional)

| Field | Endpoint | Notes |
|---|---|---|
| `model_name` | `/api/ps` | Primary model identifier |
| `size_vram` | `/api/ps` | VRAM footprint of loaded model |
| `context_length` | `/api/ps` | Current context window |
| `expires_at` | `/api/ps` | Automatic eviction timer |
| `prompt_tps` | Derived | `prompt_eval_count / (prompt_eval_duration / 1e9)` |
| `gen_tps` | Derived | `eval_count / (eval_duration / 1e9)` |

### llama.cpp Runtime Fields (Conditional)

| Field | Endpoint | Notes |
|---|---|---|
| `llama_server_up` | `/health` | Server reachability |
| `slots_idle` | `/health` | Available processing slots |
| `slots_processing` | `/health` | Active inference slots |
| `prompt_tokens_total` | `/metrics` | Prometheus counter |
| `tokens_predicted_total` | `/metrics` | Prometheus counter |

---

## 🗂 Project Documentation

All planning documents live in [`Project_brain/`](Project_brain/):

| Document | Description |
|---|---|
| [`local-ai-widget-mvp.md`](Project_brain/local-ai-widget-mvp.md) | Full MVP design, product goals, and acceptance criteria |
| [`architecture.md`](Project_brain/architecture.md) | Threading model, state publication pipeline, solution structure |
| [`requirements.md`](Project_brain/requirements.md) | Functional and non-functional requirements |
| [`telemetry-schema.md`](Project_brain/telemetry-schema.md) | Normalized telemetry schema, settings JSON schema, alert state model |
| [`ui-spec.md`](Project_brain/ui-spec.md) | Compact and expanded overlay layout specifications |
| [`milestones.md`](Project_brain/milestones.md) | Delivery milestones and development timeline |
| [`testing-strategy.md`](Project_brain/testing-strategy.md) | Unit, integration, reactive test, and UI thread safety strategy |
| [`risk-register.md`](Project_brain/risk-register.md) | Risk tracking and mitigation strategies |

---

## 🛠 Technology Stack

| Layer | Choice | Reason |
|---|---|---|
| **Runtime** | .NET 10 | Free, mature, first-class Windows desktop tooling |
| **UI Framework** | WPF | Best-established tray patterns; lower risk than WinUI 3 for overlay + tray utilities |
| **Tray Integration** | WPF NotifyIcon | Reliable background utility lifecycle management |
| **Overlay Window** | Borderless WPF `Topmost` window | Avoids WinUI 3 overlay edge cases |
| **State Stream** | `System.Reactive` (Rx.NET) | `BehaviorSubject` holds latest state for late subscribers |
| **HTTP Client** | Built-in `System.Net.Http.HttpClient` | No extra dependencies |
| **Config Storage** | Local JSON — `%APPDATA%\ModelPulse\settings.json` | Simple, transparent, user-accessible |
| **GPU Telemetry** | P/Invoke NVML, COM DXGI, WMI | Zero wrapper dependencies, native accuracy |
| **Testing** | xUnit, Moq, FluentAssertions, Microsoft.Reactive.Testing | Standard .NET test stack with virtual-time scheduler support |

---

## ⚙️ Configuration Defaults

ModelPulse comes configured with sensible, developer-friendly defaults out of the box. These settings are persisted locally in `%APPDATA%\ModelPulse\settings.json` and can be customized via the UI Settings window or overridden at runtime using environment variables:

*   **Local AI Endpoints**:
    *   **Ollama API**: Defaults to `http://127.0.0.1:11434` (Enabled by default).
    *   **llama.cpp API**: Defaults to `http://127.0.0.1:8080` (Disabled by default).
*   **Operational Cooldowns**:
    *   **Alert Cooldown**: Defaults to `60` seconds. Prevents redundant alert notifications (e.g. system RAM/VRAM pressure spikes or connection drops) from spamming the system tray.
*   **UI Startup State**:
    *   **Compact Mode**: Defaults to `true` (The overlay launches in a space-saving, edge-pinned compact mode).
    *   **Show on App Launch**: Defaults to `false` (`LaunchOverlayOnStartup = false`). The application launches silently into the system tray and does not automatically pop up the overlay window until clicked or requested.

---

## 🚀 Getting Started

### Prerequisites

- Windows 10/11 (64-bit)
- .NET SDK 10.0 or later
- Ollama and/or llama.cpp installed and running locally *(optional for initial build)*

### Build & Run

```powershell
# Clone the repository
git clone https://github.com/vijaytank/ModelPulse.git
cd ModelPulse

# Build the entire solution
dotnet build

# Run the test suite
dotnet test

# Run the UI (once available)
dotnet run --project src/ModelPulse.UI/ModelPulse.UI.csproj
```

---

## 🤝 Contributing

Contributions are welcome. Please open an issue first to discuss significant changes. The project follows a test-first development approach — any code change must include a corresponding test update.

See [`Project_brain/testing-strategy.md`](Project_brain/testing-strategy.md) for test standards.

---

## ⚠️ MVP Scope

The MVP is intentionally scoped to **Ollama and llama.cpp only**. Cloud providers (Claude, Copilot, OpenAI, NIM) and cross-device sync are explicitly out of scope for the initial release. This focus allows delivering meaningful, reliable, locally-sourced telemetry without inflating the project scope.

---

<div align="center">

**Built for local AI developers — running locally, monitoring locally, keeping resources local.**

*ModelPulse — Keep your models visible, keep your machine fast.*

</div>
