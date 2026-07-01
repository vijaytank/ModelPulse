# ModelPulse Requirements

## Overview

This document defines the functional and non-functional requirements for the ModelPulse MVP, a Windows-first, memory-optimized desktop utility focused on Ollama and llama.cpp local runtime monitoring.[cite:5][cite:34][cite:40]

## Functional Requirements

### FR-01 Runtime Detection

The widget must detect supported runtimes independently and show only the runtime panels for services that are reachable and responding on the local machine.[cite:5][cite:40]

### FR-02 Runtime Version Logging

The widget must capture runtime version information for each detected runtime and include it in diagnostics snapshots so version mismatches can be traced during support and debugging. Version data should be treated as first-class telemetry metadata, not as an optional debug-only field.[cite:5][cite:34][cite:40]

### FR-03 User-Configurable Polling

The widget must provide user-configurable polling controls beyond low-power mode. The default configuration should stay conservative, but advanced users must be able to opt into faster refresh rates with a clear warning that higher polling may increase local CPU, memory, and network overhead.

Required polling modes:
- Default mode
- Low-power mode
- Advanced custom mode

Advanced custom mode must allow separate idle and active polling intervals.

### FR-04 Rolling History View

The widget must present a user-facing rolling history view for the last 5 minutes rather than exposing only raw telemetry samples. This view should summarize recent runtime and system behavior in a format that is intuitive for monitoring short development sessions.

The 5-minute view must support:
- CPU trend
- RAM trend
- GPU trend when available
- Last throughput trend for active runtime
- Memory pressure indicators

### FR-05 Per-Runtime Alert Rules

The widget must support per-runtime alert rules so Ollama and llama.cpp warnings remain distinct and do not create overlapping or duplicated alerts. Alert evaluation should be scoped per runtime wherever possible.[cite:5][cite:34][cite:40]

### FR-06 Alert Cooldowns

The widget must enforce cooldowns per alert type so the same warning is not repeatedly shown at short intervals. Cooldowns must be configurable in settings, with conservative defaults.

### FR-07 Diagnostics Snapshot Export

The widget must support diagnostics snapshot export. Each snapshot must include:
- Timestamp
- App version
- Runtime version data
- Detected runtime list
- Current polling mode
- Current polling interval values
- Unknown field log entries when present
- Unsupported telemetry field notes
- Recent alert state summary

### FR-08 Unknown Field Discovery

The widget must log unknown or newly observed runtime fields into diagnostics data rather than silently dropping them. This behavior is required to help track endpoint drift over time.

## Non-Functional Requirements

### NFR-01 Memory Efficiency

The widget must remain lightweight enough for local AI use cases where RAM and VRAM are under pressure. The implementation must use bounded buffers and avoid unbounded telemetry retention.

### NFR-02 Safe Degradation

If a runtime field is missing, renamed, unsupported, or unavailable, the widget must degrade gracefully by showing a clear unavailable state instead of invalid placeholder values such as false zeroes.[cite:23]

### NFR-03 Polling Safety

Faster polling must never be enabled by default. Higher refresh behavior must always be a conscious user opt-in setting.

### NFR-04 UI Clarity

The widget must remain readable when one or more runtimes are active. The expanded overlay must default to a runtime toggle model when multiple runtimes are detected.

### NFR-05 Local-Only Operation

The MVP must operate locally with no cloud sync and no requirement for external telemetry services.
