# ModelPulse Telemetry and Config Schema

## Overview

This document defines the normalized telemetry and configuration model for the ModelPulse MVP. It focuses on consistent runtime metadata, configurable polling, rolling history windows, runtime-aware alerting, and local settings representation.[cite:5][cite:34][cite:40]

---

## Settings Configuration Schema (`settings.json`)

The local settings are persisted in a JSON file at `%APPDATA%\ModelPulse\settings.json`.

```json
{
  "$schema": "http://json-schema.org/draft-07/schema#",
  "title": "ModelPulseSettings",
  "type": "object",
  "properties": {
    "polling_mode": {
      "type": "string",
      "enum": ["default", "low-power", "custom"],
      "default": "default"
    },
    "polling_intervals": {
      "type": "object",
      "properties": {
        "idle_ms": {
          "type": "integer",
          "minimum": 500,
          "default": 3000
        },
        "active_ms": {
          "type": "integer",
          "minimum": 100,
          "default": 1000
        }
      },
      "required": ["idle_ms", "active_ms"]
    },
    "runtimes": {
      "type": "object",
      "properties": {
        "ollama": {
          "type": "object",
          "properties": {
            "enabled": { "type": "boolean", "default": true },
            "endpoint": { "type": "string", "default": "http://127.0.0.1:11434" }
          },
          "required": ["enabled", "endpoint"]
        },
        "llama_cpp": {
          "type": "object",
          "properties": {
            "enabled": { "type": "boolean", "default": false },
            "endpoint": { "type": "string", "default": "http://127.0.0.1:8080" }
          },
          "required": ["enabled", "endpoint"]
        }
      },
      "required": ["ollama", "llama_cpp"]
    },
    "alerts": {
      "type": "object",
      "properties": {
        "suppressed_types": {
          "type": "array",
          "items": { "type": "string" },
          "default": []
        },
        "cooldown_seconds": {
          "type": "integer",
          "minimum": 5,
          "default": 60
        },
        "vram_warning_threshold_percent": {
          "type": "number",
          "minimum": 0,
          "maximum": 100,
          "default": 90.0
        }
      },
      "required": ["suppressed_types", "cooldown_seconds", "vram_warning_threshold_percent"]
    },
    "ui": {
      "type": "object",
      "properties": {
        "always_on_top": { "type": "boolean", "default": true },
        "opacity": { "type": "number", "minimum": 0.1, "maximum": 1.0, "default": 0.9 },
        "launch_overlay_on_startup": { "type": "boolean", "default": false }
      },
      "required": ["always_on_top", "opacity", "launch_overlay_on_startup"]
    }
  },
  "required": ["polling_mode", "polling_intervals", "runtimes", "alerts", "ui"]
}
```

---

## Snapshot Metadata

Every diagnostics snapshot must include the following metadata:

| Field | Type | Required | Notes |
|---|---|---|---|
| snapshot_timestamp | datetime | Yes | UTC timestamp |
| app_version | string | Yes | Widget version |
| os_version | string | Yes | Windows version |
| polling_mode | string | Yes | default, low-power, custom |
| polling_idle_ms | integer | Yes | Current idle interval |
| polling_active_ms | integer | Yes | Current active interval |
| detected_runtimes | array | Yes | Runtime names detected |
| unknown_fields_present | boolean | Yes | Any unmapped fields discovered |

## Runtime Metadata

Each detected runtime must contribute a normalized metadata object.

| Field | Type | Required | Notes |
|---|---|---|---|
| runtime_name | string | Yes | `ollama` or `llama.cpp` |
| runtime_version | string | Yes | Version captured for diagnostics tracing |
| runtime_available | boolean | Yes | Reachability status |
| runtime_endpoint | string | Optional | Local endpoint used |
| runtime_notes | string | Optional | Unsupported or special behavior |

## Polling Settings

The telemetry layer must expose the current polling configuration so diagnostics can explain observed behavior.

| Field | Type | Required | Notes |
|---|---|---|---|
| polling_mode | string | Yes | default, low-power, custom |
| polling_idle_ms | integer | Yes | Effective idle interval |
| polling_active_ms | integer | Yes | Effective active interval |
| polling_user_override | boolean | Yes | Whether user customized polling |
| polling_batching_enabled | boolean | Yes | Whether runtime requests are batched |

## Rolling History Model

The user-facing history layer should show a last-5-minutes view instead of raw sample dumps.

| Field | Type | Required | Notes |
|---|---|---|---|
| history_window_minutes | integer | Yes | Fixed at 5 for MVP |
| cpu_avg_5m | number | Optional | Summary metric |
| ram_avg_5m | number | Optional | Summary metric |
| gpu_avg_5m | number | Optional | Summary metric |
| gen_tps_avg_5m | number | Optional | Runtime throughput trend |
| prompt_tps_avg_5m | number | Optional | Runtime throughput trend |
| pressure_events_5m | integer | Optional | Count of pressure alerts |

## Unknown Field Discovery

Adapters must capture unmapped fields in a structured format.

| Field | Type | Required | Notes |
|---|---|---|---|
| runtime_name | string | Yes | Source runtime |
| field_name | string | Yes | Unmapped field name |
| first_seen_at | datetime | Yes | First observation time |
| last_seen_at | datetime | Yes | Most recent observation time |
| sample_value | string | Optional | Truncated sample for debugging |

---

## GPU Fallback Hierarchy and Compatibility

To retrieve GPU and VRAM metrics, ModelPulse follows a strict fallback chain to ensure compatibility across hardware and minimize drivers issues:

### Fallback Hierarchy:
1. **NVIDIA Management Library (NVML):**
   - *Target:* NVIDIA GeForce / Quadro / Tesla GPUs.
   - *Mechanism:* P/Invoke into `nvml.dll`.
   - *Data Extracted:* Accurate GPU engine load (%), VRAM Allocated (bytes), Total VRAM (bytes), and temperature (°C).
2. **DXGI (DirectX Graphics Infrastructure):**
   - *Target:* Generic fallback for all DirectX-compatible GPUs (Intel, AMD, NVIDIA).
   - *Mechanism:* Query `IDXGIAdapter3::QueryVideoMemoryInfo`.
   - *Data Extracted:* VRAM Allocated (bytes), Total VRAM (bytes). (Note: Does not provide GPU core engine utilization %).
3. **Windows Performance Counters (PDH) / WMI:**
   - *Target:* CPU-integrated graphics or legacy hardware.
   - *Mechanism:* Query WMI class `Win32_VideoController` or PDH counter paths.
   - *Data Extracted:* Base card description, approximate VRAM fallback.
4. **Degraded State:**
   - If all paths fail, report `Unavailable` instead of mock or fake zeroes.

### Compatibility Matrix:

| Vendor/Path | Primary Path | GPU Load % | VRAM Usage | Reliability | Notes |
|---|---|---|---|---|---|
| **NVIDIA** | NVML | ✅ Yes | ✅ Yes | High | Directly queried via driver dll |
| **AMD** | DXGI | ❌ No | ✅ Yes | Medium | GPU Load requires legacy ADL API (deferred) |
| **Intel** | DXGI | ❌ No | ✅ Yes | Medium | Core utilization is unavailable via DXGI |
| **Generic** | PDH / WMI | ⚠️ Varies | ⚠️ Varies | Low | CPU overhead is higher; fallback only |

---

## Alert State Model

Alerts must be runtime-aware and cooldown-aware.

| Field | Type | Required | Notes |
|---|---|---|---|
| alert_id | string | Yes | Unique event key |
| runtime_name | string | Optional | Runtime-specific if applicable |
| alert_type | string | Yes | memory_pressure, disconnected, performance_drop |
| severity | string | Yes | info, warning, critical |
| cooldown_seconds | integer | Yes | Minimum repeat gap |
| last_triggered_at | datetime | Optional | Last emitted time |
| suppressed | boolean | Yes | User or policy suppression |
