# ModelPulse Milestones & Timeline

This document lays out the execution roadmap for ModelPulse. The total estimated duration to complete the MVP is **approximately 15–17 active development days (~3 weeks)**.

---

## Milestone 1: Core Telemetry & Discovery Spike [COMPLETED]
*Estimated Duration: 4 Days*

- **Phase 0 Spike (1 Day) [COMPLETED]:** Validate local Ollama (`/api/ps`) and llama.cpp (`/health`) endpoint structure, confirm GPU performance counter path viability, and pass the Go/No-Go Gate.
- **Core Models (1 Day) [COMPLETED]:** Set up the .NET 10 solution (`ModelPulse.sln` / `ModelPulse.slnx`) structure, C# project templates, interfaces (`IRuntimeAdapter`), and model schemas.
- **System & Runtime Adapters (2 Days) [COMPLETED]:**
  - Implement NVIDIA P/Invoke NVML client and DXGI/WMI fallbacks.
  - Implement `OllamaAdapter` and `LlamaCppAdapter` with schema parsing and drift logging.

---

## Milestone 2: Reactive Collector & Adaptive Polling [COMPLETED]
*Estimated Duration: 3 Days*

- **Polling Core (1 Day) [COMPLETED]:** Build the timer-based collector service using `Task.Delay` and async HTTP loop. Implement adaptive rate scaling based on inference status (busy/idle).
- **Rx.NET State Stream (1 Day) [COMPLETED]:** Implement `IObservable<TelemetryState>` pipeline to push unified telemetry data from background threads.
- **Settings Store (1 Day) [COMPLETED]:** Create local JSON configuration load/save service (`settings.json`). Add advanced custom interval controls.

---

## Milestone 3: Desktop UI & Overlay Baseline [COMPLETED]
*Estimated Duration: 4 Days*

- **Tray Agent Setup (1 Day) [COMPLETED]:** Implement WPF NotifyIcon application startup, tray context menu (show/hide controls, manual pause, settings trigger), and background lifecycle.
- **Compact Overlay View (1.5 Days) [COMPLETED]:** Design borderless, transparent, always-on-top WPF window. Pinned to edge with auto-snap. Bind to Rx.NET stream via ViewModel dispatcher.
- **Expanded Overlay View (1.5 Days) [COMPLETED]:** Design the detailed toggle panel for Ollama/llama.cpp, adding the rolling 5-minute trend summaries and active model list.

---

## Milestone 4: Alert Engine & Diagnostics [COMPLETED]
*Estimated Duration: 3 Days*

- **Alert Engine Implementation (1.5 Days) [COMPLETED]:** Write rule evaluation engine for CPU/RAM/VRAM pressure and disconnected runtimes. Implement distinct per-runtime queues and thread-safe alert cooldowns.
- **Diagnostics Exporter (1.5 Days) [COMPLETED]:** Build snapshot serializer writing timestamped configuration state, adapter version info, and observed unknown fields to JSON for easy debugging.

---

## Milestone 5: Stabilization & Verification
*Estimated Duration: 3 Days*

- **Drift & Fallback Testing (1 Day):** Test with mock endpoints sending malformed, missing, or renamed JSON fields. Verify fallback to DXGI when NVML path is blocked.
- **Performance Profiling (1 Day):** Profile Memory and CPU footprints to ensure ModelPulse runs under target budget (RAM < 15MB, idle CPU < 0.1%).
- **Packaging & Launch (1 Day):** Configure portable `.zip` build release and verify first-run installer actions.
