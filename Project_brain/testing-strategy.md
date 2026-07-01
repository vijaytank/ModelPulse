# ModelPulse Testing Strategy

## Overview

This document outlines the testing strategy for the ModelPulse MVP. Given that ModelPulse is a background utility designed to run alongside memory-heavy AI workloads, the testing strategy focuses heavily on:
1. **Robust Exception Handling & Safe Degradation:** Ensuring the app never crashes due to changing runtime API shapes (drift) or network disconnects.
2. **Deterministic Time-Based Logic:** Using Reactive Test Schedulers to verify polling rates, alert cooldowns, and moving averages without resorting to fragile `Thread.Sleep` calls.
3. **Resource Footprint Integrity:** Confirming memory bounds and CPU usage remain within budget.

---

## Test Framework & Tools

- **Unit Testing Framework:** `xUnit`
- **Mocking Library:** `Moq`
- **Reactive Extensions Testing:** `Microsoft.Reactive.Testing` (provides `TestScheduler` to manipulate virtual time).
- **Assertions Library:** `FluentAssertions`

---

## Testing Layers

### 1. Unit Tests (`ModelPulse.Tests`)

Unit tests target isolated business logic without hitting actual Windows performance counters or local network ports.

#### A. Adapter Logic & Drift Tolerance
- **Goal:** Ensure adapters parse raw runtime endpoints without throwing exceptions even if response shapes differ.
- **Approach:**
  - Mock HTTP responses using a custom `HttpMessageHandler` mock injected into the adapter's `HttpClient`.
  - Test case matrices including:
    - Standard payload (successful parsing).
    - Malformed JSON payload (graceful degradation, returning partial or `Unavailable` states).
    - Payload with missing/null parameters (e.g., missing `prompt_eval_duration`).
    - Payload containing unknown/newly added fields (assert that the adapter fires a drift event and populates the diagnostics buffer).

#### B. Reactive State Stream & Polling
- **Goal:** Validate adaptive polling state machine transition (idle interval vs. active inference interval) and backoff rules.
- **Approach:**
  - Inject a virtual `IScheduler` into the `CollectorCore`.
  - Using `TestScheduler`, simulate elapsed time and verify that the polling interval scales down to `active_ms` when an adapter reports inference activity, and falls back to `idle_ms` on inactivity.
  - Simulate consecutive HTTP errors and verify exponential backoff delay execution.

#### C. Alert Engine & Cooldowns
- **Goal:** Verify that alerts trigger on correct thresholds, respect per-runtime boundaries, and honor configured cooldowns.
- **Approach:**
  - Feed simulated telemetry streams to `AlertEngine`.
  - Assert that an alert triggers immediately upon crossing the CPU/VRAM pressure threshold.
  - Simulate a second crossing within the cooldown window and verify no additional notification is emitted.
  - Advance virtual time past the cooldown window and assert that a new alert is successfully dispatched.

#### D. Moving History Aggregator
- **Goal:** Ensure raw sample points are aggregated correctly into the last-5-minutes summary models without memory leaks or unbounded growth.
- **Approach:**
  - Feed a stream of telemetry samples over simulated time.
  - Assert that average calculation is correct and older points are dropped, keeping the internal queue bounded to the target size.

---

## Integration & System Tests

Integration tests run on the local target developer machine to confirm Windows API bindings and network reachability.

### 1. GPU Telemetry Integration
- Verify NVML DLL load state and ensure P/Invoke calls resolve successfully on NVIDIA hardware.
- Verify DXGI QueryVideoMemoryInfo returns valid non-zero values on active displays.
- Confirm WMI queries complete without blocking or causing WMI provider host spikes.

### 2. Live Runtime Loop Tests
- Run integration suites against active Ollama and legacy/llama.cpp local services (if installed) to assert real-world payload compatibility.

---

## UI Thread Safety Validation

WPF applications throw `InvalidOperationException` if objects created on background threads are modified directly on the UI thread.
- **Verification Rule:** All ViewModel binding updates must be explicitly routed via `Dispatcher.BeginInvoke(DispatcherPriority.Background, ...)` — never written directly from a background thread.
- **Test cases:**
  - Telemetry updates emitted from background collector tasks must update views without producing cross-thread dispatch failures.
  - Tray icon context menu click actions must not block the telemetry loop or lead to deadlock states.
