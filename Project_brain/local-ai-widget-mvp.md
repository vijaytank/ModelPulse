# ModelPulse MVP Design

## Overview

This document defines the MVP for a lightweight Windows desktop widget that monitors local AI runtime activity without forcing the user to keep Task Manager or terminal commands open. The MVP is intentionally local-first, memory-optimized, and limited to Ollama and llama.cpp because both runtimes expose usable local monitoring surfaces and are strong fits for offline development workflows.[cite:5][cite:34][cite:40]

The goal is a small always-on-top overlay plus a tray agent that shows system pressure, active local models, runtime status, and throughput data such as tokens per second. Ollama can expose running model details through `/api/ps`, including model name, memory-related fields, unload timing, and context-related metadata, while llama.cpp server exposes health and monitoring-oriented endpoints such as `/health` and `/metrics`.[cite:5][cite:34][cite:40]

## Product Goals

The MVP should solve four core problems:

- Reduce context switching by keeping model and system status visible at all times.
- Prevent system slowdowns by making the widget itself lightweight and memory-aware.
- Provide actionable local runtime telemetry for Ollama and llama.cpp first.
- Create an adapter architecture that can later support cloud and hybrid AI providers without redesigning the product.[cite:5][cite:34][cite:40]

## Scope

### In scope for MVP

- Windows desktop overlay that can stay pinned on screen.
- System tray app for background polling and quick controls.
- System telemetry: CPU, RAM, GPU utilization, and GPU memory usage.
- Ollama integration: service status, running models, model size, VRAM-related size, unload timer, and context length where available.[cite:5]
- Ollama request performance capture: prompt tokens, generated tokens, prompt duration, eval duration, and derived tokens per second when available from generate/chat responses.[cite:17][cite:19]
- llama.cpp server integration: server health, metrics endpoint support, throughput metrics, and active slot state where exposed by the runtime.[cite:34][cite:37][cite:40]
- Rolling recent history with strict memory limits.
- Local-only storage for settings, with no cloud sync.

### Out of scope for MVP

- Claude, Copilot, Codex, GitHub, NIM, or other cloud/provider integrations.
- Full conversation token accounting across third-party clients.
- Long-term analytics database.
- Cross-device sync.
- Heavy dashboard views that require a browser-based shell.

## Runtime Detection Rules

The MVP must be runtime-conditional rather than provider-cluttered. It should only show telemetry panels for runtimes that are actually installed, reachable, and responding on the local machine.[cite:5][cite:40]

Rules:
- If Ollama is detected, show only the Ollama runtime panel and Ollama-specific fields.[cite:5]
- If llama.cpp server is detected, show only the llama.cpp runtime panel and llama.cpp-specific fields.[cite:34][cite:40]
- If both are detected, show both in separate cards or sections; never merge their metrics into a single runtime view.[cite:5][cite:34][cite:40]
- If neither is detected, show system telemetry only plus a clear `No local runtime detected` state.[cite:5][cite:40]
- Poll only the adapters that are currently detected and enabled to reduce unnecessary memory and CPU overhead.

This behavior keeps the UI clean, prevents empty runtime sections, and supports the memory-first goal of the widget by avoiding needless polling for unavailable providers.

## Core Screens

### 1. Compact overlay

A small always-on-top widget pinned to any screen edge.

Fields:
- CPU percent
- RAM used / available
- GPU percent
- GPU VRAM used
- Active runtime badge: Ollama, llama.cpp, or both
- Active model count
- Last generation t/s
- Busy/idle status

Behavior:
- Click to expand.
- Auto-collapse after inactivity.
- Low refresh rate while idle, higher refresh rate during inference.
- Show runtime sections conditionally based on detected adapters only.

### 2. Expanded overlay

Shows richer runtime details without opening a full app.

Sections:
- System status
- Ollama runtime panel
- llama.cpp runtime panel
- Recent session metrics
- Alerts

When more than one runtime is active, the expanded overlay should default to a runtime toggle so users focus on one runtime at a time, with collapsible sections available as an optional alternate view. This keeps the expanded overlay readable while still keeping runtime data separate.

### 3. Tray popup

Quick control surface from the notification area.

Actions:
- Show/hide overlay
- Switch compact/expanded mode
- Pause polling
- Open settings
- Exit

## Telemetry Model

### System telemetry

Collect from Windows-native counters or vendor APIs.

Required:
- CPU usage
- RAM usage
- GPU engine usage
- GPU VRAM usage
- Widget process memory and CPU footprint

Nice to have later:
- Per-process GPU usage
- Disk activity
- Network usage

GPU telemetry must be vendor-tolerant. If a given Windows counter or vendor API cannot provide VRAM usage, the widget should show `Unavailable` or `Not exposed by driver/runtime` instead of misleading zero values. 

The fallback order for GPU telemetry collection is:
1. **NVML (NVIDIA Management Library):** Query directly via NVML API for NVIDIA GPU hardware (most accurate for temperature, engine load, and physical/virtual VRAM).
2. **DXGI (DirectX Graphics Infrastructure):** Query via DXGI adapter interface (Windows native fallback, provides adapter-wide VRAM allocation metrics).
3. **WMI (Windows Management Instrumentation) / Performance Counters:** Query general system counters if NVML and DXGI are unreachable.
4. **Unavailable State:** If all queries fail or return invalid/unsupported signals, show `Unavailable` instead of misleading zero values.

The follow-on `telemetry-schema.md` includes a compatibility matrix mapping which GPU fields are reliable by vendor and collection path.

### Ollama telemetry

Poll the local Ollama API.

Primary endpoint:
- `GET /api/ps` for running models and runtime fields.[cite:5]

Capture where available:
- Model name
- Model size
- `size_vram`
- `expires_at`
- Context length
- Processor/runtime hints if exposed

Request-level telemetry:
- `prompt_eval_count`
- `prompt_eval_duration`
- `eval_count`
- `eval_duration`
- `total_duration`
- `load_duration`

Derived fields:
- Prompt t/s = `prompt_eval_count / (prompt_eval_duration / 1e9)` when duration exists.[cite:17][cite:19]
- Generation t/s = `eval_count / (eval_duration / 1e9)` when duration exists.[cite:17][cite:19]
- Last response latency
- Rolling average t/s

### llama.cpp telemetry

Poll the local llama.cpp server only if enabled by the user.

Primary endpoints:
- `/health` for server availability.[cite:40]
- `/metrics` for throughput and monitoring counters.[cite:34][cite:37]
- Slot inspection where available for active slot or KV-cache-related state.[cite:33][cite:44]

Capture where available:
- Server up/down
- Prompt tokens per second
- Predicted/generated tokens per second
- Active slot count
- Busy/idle state
- Queue or slot occupancy indicators

## Memory Optimization Rules

The MVP must be designed to avoid becoming a resource problem itself.

### Hard constraints

- Target idle RAM footprint: as low as practical; no browser-heavy shell for v1.
- Use bounded in-memory buffers only.
- No unbounded logs in RAM.
- No model loading, prompt replay, or tokenization inside the widget.
- Poll adaptively instead of using fixed high-frequency refresh.

### Polling strategy

- Idle mode: 2 to 5 second polling.
- Active inference mode: 500 ms to 1 second polling.
- Error mode: exponential backoff when runtime is unavailable.
- User override: allow manual low-power mode.
- Advanced polling controls should be optional and opt-in, so power users can choose higher refresh rates only when they explicitly accept the extra resource cost.
- Where possible, batch Ollama and llama.cpp requests within the same polling cycle to reduce HTTP churn when more than one adapter is active.
- If future runtime hooks or event-driven notifications become available, prefer them over pure high-frequency polling.

### Buffer strategy

- Keep only 60 to 120 recent samples per metric.
- Present recent behavior as a simple user-facing `last 5 minutes` view rather than exposing only raw samples.
- Aggregate older data into min/avg/max snapshots or discard it.
- Persist only compact settings, not raw telemetry streams.

### UI strategy

- Text-first UI with tiny sparkline support only if it stays cheap.
- No webview-based charts in MVP.
- No animations except small state transitions.
- Render only visible sections; avoid hidden heavy panels.

## MVP Data Schema

| Field | Source | Type | Priority | Notes |
|---|---|---|---|---|
| cpu_percent | Windows system counters | exact | High | Overlay top row |
| ram_used_mb | Windows system counters | exact | High | Overlay top row |
| gpu_percent | Windows or vendor API | exact | High | Overlay top row |
| vram_used_mb | Windows or vendor API | exact | High | Overlay top row |
| ollama_up | Ollama API | exact | High | From local service response [cite:5] |
| ollama_models_running | Ollama API | exact | High | Count from `/api/ps` [cite:5] |
| model_name | Ollama API / llama.cpp config | exact | High | Primary runtime row [cite:5] |
| model_size | Ollama API | exact | Medium | Useful for loaded model summary [cite:5] |
| size_vram | Ollama API | exact | High | Key memory signal [cite:5] |
| context_length | Ollama API | exact | High | Important for local testing [cite:5] |
| expires_at | Ollama API | exact | Medium | Helps show unload timing [cite:5] |
| prompt_tps | Ollama / llama.cpp metrics | derived/exact | High | Based on prompt counters [cite:17][cite:19][cite:37] |
| gen_tps | Ollama / llama.cpp metrics | derived/exact | High | Main performance number [cite:17][cite:19][cite:37] |
| load_duration | Ollama response | exact | Medium | Useful when model reloads [cite:17][cite:19] |
| total_duration | Ollama response | exact | Medium | Request latency [cite:17][cite:19] |
| llama_server_up | llama.cpp `/health` | exact | High | Runtime availability [cite:40] |
| llama_active_slots | llama.cpp slot data | exact/optional | Medium | Depends on runtime exposure [cite:33][cite:44] |
| widget_ram_mb | self telemetry | exact | High | Ensures the app stays lightweight |

## Architecture

### Components

1. **Collector service**
   - Background process.
   - Polls Windows counters and local runtime endpoints.
   - Maintains bounded recent samples.
   - Emits normalized state updates.

2. **Runtime adapters**
   - `OllamaAdapter`
   - `LlamaCppAdapter`
   - Future adapters implement a shared interface.

3. **Overlay UI**
   - Always-on-top mini widget.
   - Compact and expanded modes.
   - Reads normalized state only.

4. **Tray controller**
   - Manages startup, quick actions, mode switching, and settings.
   - Provides diagnostics snapshot export.
   - Includes runtime version logging in snapshots so mismatches can be traced later.

5. **Alert engine**
   - Memory pressure alerts
   - Runtime disconnected alerts
   - Performance drop alerts
   - Severity model: info, warning, critical
   - User controls for suppressing noisy alert types
   - Cooldown rules per alert type to avoid repeated spam
   - Per-runtime alert rules so Ollama and llama.cpp warnings stay distinct

### Normalized adapter contract

Each runtime adapter should provide:

- `IsAvailable()`
- `GetRuntimeSummary()`
- `GetActiveModels()`
- `GetPerformanceSample()`
- `GetMemorySignals()`
- `GetWarnings()`

Adapters should also support automated field discovery behavior for diagnostics: unknown or newly added runtime fields should be logged into snapshots or debug logs rather than silently discarded. This helps track endpoint drift without breaking the UI.

## Suggested Free Secure Windows Stack

For a Windows-first MVP that stays memory-efficient, the recommended stack is:

- **Language/runtime:** .NET 8
- **UI:** WinUI 3 or WPF for a lightweight native overlay
- **Tray integration:** native Windows notification area support
- **HTTP polling:** built-in .NET HTTP client
- **System metrics:** Windows performance counters, WMI, DXGI, or vendor libraries depending on GPU path
- **Config:** local JSON file only
- **Packaging:** MSIX or portable signed build later

This stack is appropriate because it avoids the memory overhead often associated with browser-shell desktop apps while staying free and secure on Windows.

## Development Plan

### Phase 0: discovery and spike

Deliverables:
- Confirm Ollama endpoint behavior on Windows.
- Confirm llama.cpp server endpoint behavior with at least one local test setup.
- Benchmark several polling intervals.
- Establish memory budget for the widget process.

Tasks:
- Build tiny probe scripts for `/api/ps`, `/health`, and `/metrics`.[cite:5][cite:34][cite:40]
- Record which fields are exact, missing, or runtime-specific.
- Record runtime version information for snapshots and compatibility tracking.
- Decide minimum useful refresh rate.

**Go/No-Go Exit Criteria (Phase 0 Gate):**
Before moving to Phase 1, the following must be met:
1. Probe scripts successfully query local Ollama (`/api/ps`) and llama.cpp (`/health`) endpoints and return valid JSON.
2. The schemas returned are parsed successfully without throwing exceptions in a test C# program.
3. Polling overhead is confirmed: idle CPU usage of probe script must be < 0.1%, and RAM footprint under 15MB.
4. Any undocumented or changed fields are captured and logged. If endpoints are unreachable or schemas differ fundamentally, Phase 1 is blocked until the adapter parser rules are updated.

### Phase 1: collector core

Deliverables:
- Normalized telemetry state model.
- Background collector service.
- Adaptive polling scheduler.
- Self-monitoring of widget RAM and CPU.

Tasks:
- Implement system metrics collection.
- Implement bounded ring buffers.
- Add runtime availability detection.
- Add structured error handling and backoff.

### Phase 2: Ollama adapter

Deliverables:
- Running models view.
- Derived t/s calculations.
- Basic alerting for missing runtime or memory pressure.

Tasks:
- Parse `/api/ps`.[cite:5]
- Capture performance metrics from generate/chat-compatible responses when available.[cite:17][cite:19]
- Compute prompt and generation t/s.[cite:17][cite:19]
- Handle missing metrics safely.[cite:23]
- Use null-safe parsing throughout so omitted Ollama fields degrade gracefully instead of breaking UI state.[cite:23]
- Log unknown or newly seen Ollama fields into diagnostics output for drift tracking.

### Phase 3: llama.cpp adapter

Deliverables:
- Health status panel.
- Throughput fields from `/metrics`.
- Slot/occupancy display when supported.

Tasks:
- Parse `/health`.[cite:40]
- Parse `/metrics` and map relevant counters.[cite:34][cite:37]
- Support optional slot inspection.[cite:33][cite:44]
- Log unknown or newly seen llama.cpp metric names or fields into diagnostics output for drift tracking.

### Phase 4: overlay UI

Deliverables:
- Compact overlay.
- Expanded overlay.
- Tray popup and settings.

Tasks:
- Build text-first widget.
- Add always-on-top and snap behavior.
- Add manual compact mode and low-power mode.
- Add theme and transparency settings.

### Phase 5: stabilization

Deliverables:
- Stress-tested MVP.
- Portable package.
- Initial documentation.

Tasks:
- Test during long Ollama sessions.
- Test under low RAM and low VRAM conditions.
- Validate widget stays below its memory budget.
- Validate endpoint disconnect and recovery behavior.

## Acceptance Criteria

The MVP is successful when all of the following are true:

- Widget launches on Windows and remains visible as overlay or tray utility.
- Idle resource consumption remains small relative to the host machine.
- Runtime panels appear only for detected local runtimes, and unavailable runtimes remain hidden.[cite:5][cite:40]
- Ollama runtime status is visible without opening terminal or Task Manager.[cite:5]
- Ollama running models and memory-related fields are displayed correctly.[cite:5]
- Last prompt t/s and generation t/s are displayed when metrics are available.[cite:17][cite:19]
- llama.cpp server health and metrics can be shown when the server is enabled.[cite:34][cite:40]
- Polling slows down automatically when runtimes are idle.
- Runtime outages do not freeze the UI.
- Unsupported GPU metrics are shown as unavailable rather than incorrect zero values.
- Diagnostics snapshot export works for troubleshooting runtime mismatch cases.
- Diagnostics snapshots include runtime version data and unknown-field logging where available.
- When multiple runtimes are active, the expanded overlay defaults to a runtime toggle view rather than an overloaded all-at-once panel.

## Risks and Mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| Runtime version drift | High | Build adapters with schema validation, version-tolerant parsing, graceful degradation, and unknown-field logging so field changes do not break the widget and new telemetry is easier to discover. |
| Runtime metrics differ by version | Medium | Keep adapter parsing version-tolerant and mark unsupported fields as optional. |
| Missing eval fields in some Ollama responses | Medium | Null-safe parsing and fallback labels such as `n/a`.[cite:23] |
| High polling rate adds CPU overhead | High | Adaptive polling, backoff, low-power mode, batched requests, and optional user opt-in for higher refresh rates only. |
| GPU telemetry varies by vendor | High | Start with common Windows counters, add vendor-specific enrichments later, and show `Unavailable` rather than `0` when VRAM usage is not exposed. |
| Compact overlay becomes cluttered when multiple runtimes are active | Medium | Use collapsible sections, compact summaries, or a toggle so the overlay stays readable. |
| Alert fatigue from noisy notifications | Medium | Use severity tiers such as info, warning, and critical; add cooldowns and allow per-alert-type suppression. |
| Feature creep from cloud providers | High | Freeze MVP to Ollama and llama.cpp only. |

## MVP Backlog

### Must-have
- Native Windows overlay
- Tray agent
- System metrics
- Ollama adapter
- llama.cpp adapter
- Adaptive polling
- Self-memory telemetry
- Basic alerts

### Should-have
- Tiny sparklines
- Export diagnostics snapshot
- Manual refresh button
- Top-most opacity slider

A minimal diagnostics snapshot export should be introduced early, even in the MVP cycle, because it will help debug runtime mismatches, endpoint drift, and unsupported fields across user machines.

### Later
- Cloud provider adapters
- Session history export
- Request timeline
- Multi-machine monitoring
- Plug-in SDK

## Planning Documents

All planning documents have been created:

- `requirements.md` — functional and non-functional requirements
- `architecture.md` — modules, threading model, state publication, solution structure
- `ui-spec.md` — compact and expanded widget layouts
- `telemetry-schema.md` — normalized fields, adapter mapping, settings schema
- `milestones.md` — milestone delivery plan with estimates
- `risk-register.md` — operational and technical risk tracking
- `testing-strategy.md` — unit, integration, and UI thread safety test plan

## Immediate Recommendation

Build the MVP as a native Windows local-first utility focused on Ollama and llama.cpp only. That provides enough official and practical runtime data to deliver meaningful value quickly while keeping the codebase, memory usage, and support burden under control.[cite:5][cite:17][cite:34][cite:40]


## Pre-Development Checks

Before implementation starts, the following blockers or decision points should be resolved because they affect architecture, UI behavior, and packaging choices.

### Blockers and open decisions

1. **Windows UI stack choice**
   - The MVP needs both a reliable always-on-top overlay and a tray presence.
   - WPF on .NET supports tray-icon patterns well through `NotifyIcon`-style approaches and related desktop guidance, while WinUI 3 tray support is less direct and commonly requires workarounds or alternate components.[cite:80][cite:81][cite:91][cite:93]
   - WinUI 3 also has some practical overlay/topmost caveats in community and issue discussions, which increases implementation risk for a lightweight utility focused on stability.[cite:82][cite:84][cite:92]
   - **Recommendation:** choose WPF on .NET 8 for v1 unless there is a strong product reason to prefer WinUI 3.

2. **Tray plus overlay interaction model**
   - Decide whether the app starts hidden in tray, launches overlay immediately, or restores the last-used mode.
   - This affects startup behavior, shutdown behavior, and whether the app feels lightweight or intrusive.
   - **Recommendation:** start in tray by default and restore overlay only if the user enabled `Launch overlay on startup`.

3. **GPU telemetry provider chain**
   - A fallback order must be decided early, because GPU usage and VRAM fields vary by vendor and driver exposure.
   - **Recommendation:** define a provider chain such as Windows counters first, then vendor-specific enrichment when available, and show `Unavailable` when a field is not exposed.

4. **Runtime version detection contract**
   - The widget should explicitly capture Ollama and llama.cpp version information in diagnostics snapshots to support drift analysis.
   - **Recommendation:** make runtime version capture a first-class telemetry field rather than a debug-only extra.

5. **Multi-runtime UX rule**
   - The spec now prefers a runtime toggle when both runtimes are active, but that should be locked before UI implementation to avoid rework.
   - **Recommendation:** adopt `single runtime shown by default, toggle to switch` as the default expanded overlay behavior.

6. **Alert policy defaults**
   - Cooldowns, per-runtime rules, and severity tiers are now in scope, but default thresholds still need to be chosen.
   - **Recommendation:** keep alerts conservative in MVP, with most notifications disabled by default except critical failures.

### Not blockers, but important early tasks

- Validate actual Ollama fields on the target Windows version and installed runtime versions.[cite:5]
- Validate actual llama.cpp `/health` and `/metrics` behavior on the server build you intend to support.[cite:34][cite:40]
- Benchmark memory footprint of the collector process and overlay separately.
- Decide whether diagnostics snapshots are JSON-only or JSON plus a human-readable text summary.

## Recommended Tech Stack

The best v1 stack should optimize for low memory usage, native Windows behavior, simple deployment, and secure local-only operation.

### Preferred stack

| Layer | Recommended choice | Why |
|---|---|---|
| Desktop runtime | .NET 8 | Free, mature, strong Windows desktop tooling |
| UI framework | WPF | Better-established tray patterns and lower implementation risk for this kind of utility.[cite:80][cite:85][cite:91][cite:93] |
| Tray icon | WPF-compatible NotifyIcon approach | Reliable tray integration for background utility behavior.[cite:85][cite:91][cite:93] |
| Overlay window | Borderless WPF topmost window | Simpler and lower risk than forcing WinUI 3 into tray-plus-overlay duties.[cite:86][cite:92] |
| HTTP client | Built-in .NET `HttpClient` | No extra dependency needed |
| Config storage | Local JSON file | Small, transparent, easy to debug |
| Logging | Structured local file logging | Needed for diagnostics snapshots and drift tracking |
| Packaging | Portable build first, installer later | Faster MVP iteration |

### Why not Electron for MVP

A browser-shell stack would likely increase idle memory use more than needed for a compact telemetry widget, which conflicts with the product's memory-first goal. A native .NET desktop stack is a better fit for a utility that must stay resident while local models already consume RAM and VRAM.

### Why not WinUI 3 for MVP

WinUI 3 is attractive visually, but tray integration is not as straightforward, and always-on-top overlay behavior has more edge-case risk in available issue discussions and community workarounds.[cite:81][cite:82][cite:84][cite:88][cite:92] It can still be a future option, but WPF is the safer stack for getting the MVP shipped.

## Final Pre-Build Recommendation

The MVP can move into development after locking these three choices: WPF on .NET 8 as the UI stack, conservative default alerting, and a defined GPU telemetry provider chain. With those decisions fixed, there are no major blockers left beyond normal runtime-version validation and telemetry testing on real Windows machines.[cite:80][cite:85][cite:91][cite:93]
