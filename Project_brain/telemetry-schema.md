# Local AI Widget Telemetry Schema

## Overview

This document defines the normalized telemetry model for the Local AI Widget MVP. It focuses on consistent runtime metadata, configurable polling, rolling history windows, and runtime-aware alerting.[cite:5][cite:34][cite:40]

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

## GPU Compatibility Tracking

The follow-on schema implementation should maintain a compatibility matrix for GPU telemetry by vendor and collection path.

| Vendor/Path | GPU Utilization | VRAM Used | Process-Level GPU | Notes |
|---|---|---|---|---|
| Windows generic counters | Varies | Varies | Limited | Baseline fallback |
| NVIDIA path | Often strong | Often strong | Varies | Use when available |
| AMD path | Varies | Varies | Varies | Driver-dependent |
| Intel path | Varies | Varies | Varies | Driver-dependent |

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
