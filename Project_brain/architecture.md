# ModelPulse Architecture

## Overview

This document describes the ModelPulse architecture, with special focus on threading, state publication, solution/namespace structure, runtime version logging, configurable polling, rolling 5-minute history, and per-runtime alert handling.[cite:5][cite:34][cite:40]

## Modules

### 1. Collector Core
- Schedules polling cycles based on current active/idle modes.
- Batches runtime requests in one cycle where possible.
- Maintains bounded sample buffers in memory.
- Publishes normalized state.

### 2. Runtime Adapters
- Ollama adapter
- llama.cpp adapter
- Each adapter emits runtime version metadata and unknown field discovery events.

### 3. History Aggregator
- Converts raw bounded samples into a rolling 5-minute summary model.
- Produces UI-friendly trend summaries.

### 4. Alert Engine
- Evaluates runtime-specific conditions.
- Applies severity levels (info, warning, critical).
- Applies cooldowns per alert type.
- Keeps Ollama and llama.cpp alert state separate.

### 5. Diagnostics Exporter
- Serializes current state and recent history into a JSON snapshot.
- Includes runtime versions, polling configuration, and unknown fields.

---

## Technical Architecture Details

### Threading Model
To ensure ModelPulse remains responsive and does not lag the desktop UI:
- **Background Polling & Network IO:** Polling of GPU/CPU counters, Ollama API, and llama.cpp endpoints runs entirely on background threads via `Task.Run` and asynchronous I/O (`HttpClient.SendAsync`).
- **WPF UI Thread:** The UI thread is dedicated solely to layout rendering and handling user interactions. No synchronous I/O or polling loops run on the UI thread.
- **Cross-Thread Safety:** Background telemetry data is marshaled to the UI thread using WPF's `Dispatcher` or Rx schedulers.

### State Publication Model
ModelPulse uses a **Reactive Push Model** via Reactive Extensions (Rx.NET / `System.Reactive`).

#### Publisher (Collector Core)
- Builds immutable `CollectorSnapshot` objects (timestamp + system stats + runtime[]) after each polling cycle.
- Publishes via a single `BehaviorSubject<CollectorSnapshot>` — one producer, simple `OnNext(snapshot)` call per cycle.
- `BehaviorSubject` is chosen over `Subject` or `Publish().RefCount()` because it:
  - Holds the latest value so **late subscribers** (UI restore after minimize, diagnostics snapshot export) get current state immediately.
  - Eliminates the complexity of `Connect`/`RefCount` for a known, small subscriber set.

#### Subscribers (Three, Fixed)
| Subscriber | Thread | Rx operators applied |
|---|---|---|
| **HistoryAggregator** | `TaskPoolScheduler` (background) | `.Sample()` or `.Buffer()` to accumulate rolling 5-min windows; computes `cpu_avg_5m`, `gen_tps_avg_5m`, etc. off the UI thread. |
| **AlertEngine** | `TaskPoolScheduler` (background) | `.DistinctUntilChanged()` to skip unchanged snapshots; evaluates per-runtime rules and cooldown timers without touching the UI thread. |
| **UI Mediator** | `TaskPoolScheduler` → `Dispatcher` | `.Sample(TimeSpan)` (active: 500 ms, idle: 2–5 s) then `.ObserveOn(TaskPoolScheduler.Default)` for any pre-processing, then `.Subscribe(s => Dispatcher.BeginInvoke(DispatcherPriority.Background, () => viewModel.ApplySnapshot(s)))`. |

#### ApplySnapshot Contract
- `ApplySnapshot(CollectorSnapshot)` runs on the WPF UI thread (marshaled by `Dispatcher.BeginInvoke`).
- It **diffs** incoming values against current ViewModel properties and calls `PropertyChanged` **only for fields that changed** to minimise layout recalculations.
- For runtime lists, updates target `ObservableCollection<RuntimeViewModel>` with minimal add/remove/update operations (no full-list replacement).
- `BeginInvoke` is preferred over `Invoke` to keep the Dispatcher non-blocking; `DispatcherPriority.Background` ensures urgent render/input tasks are not starved.

#### Why Not Alternatives
- **Dispatcher.Invoke per metric:** Causes many cross-thread calls per cycle → UI thread contention and stuttering under high-frequency polling.
- **Collector writing directly to ViewModels:** Tight coupling; collector must know about UI threading and ViewModel internals.
- **Publish().RefCount():** Unnecessary for a fixed, small subscriber set; `BehaviorSubject` is simpler and sufficient.

---

## Solution and Namespace Structure

The project is structured as a .NET 8 solution (`ModelPulse.sln`):

```
ModelPulse/
├── ModelPulse.sln
├── src/
│   ├── ModelPulse.Core/
│   │   ├── Adapters/          # Adapter interfaces (IRuntimeAdapter)
│   │   ├── Models/            # TelemetryState, Alert, Config schemas
│   │   └── Services/          # ICollector, IAlertEngine interfaces
│   ├── ModelPulse.Adapters/
│   │   ├── Ollama/            # OllamaAdapter implementation
│   │   ├── LlamaCpp/          # LlamaCppAdapter implementation
│   │   └── System/            # SystemTelemetryProvider (NVML/DXGI/WMI)
│   ├── ModelPulse.Services/
│   │   ├── Polling/           # Background Collector service
│   │   ├── History/           # Rolling 5-minute aggregator
│   │   ├── Alerts/            # Alert Engine with cooldown timers
│   │   └── Config/            # JSON Local Settings store
│   └── ModelPulse.UI/
│       ├── App.xaml
│       ├── MainWindow.xaml    # Main Overlay view
│       ├── TrayIcon.cs        # Notification tray manager
│       ├── ViewModels/        # MVVM properties bound to UI
│       └── Resources/         # XAML Styles, themes, and icons
└── tests/
    └── ModelPulse.Tests/      # xUnit and Moq suite for core/services
```

---

## Data Flow

1. Collector polls system metrics and active runtime adapters on a background timer.[cite:5][cite:34][cite:40]
2. Adapters normalize runtime state and emit version metadata.
3. Unknown fields are sent to diagnostics buffers.
4. History aggregator computes last-5-minutes summaries.
5. Alert engine evaluates per-runtime rule sets.
6. `BehaviorSubject.OnNext(snapshot)` emits the immutable snapshot to all three subscribers concurrently.
   - **HistoryAggregator** accumulates rolling 5-min summaries on a background thread.
   - **AlertEngine** evaluates per-runtime thresholds and cooldown rules on a background thread.
   - **UI Mediator** samples/throttles the stream on a background thread, then calls `Dispatcher.BeginInvoke(DispatcherPriority.Background, () => viewModel.ApplySnapshot(snapshot))`.
7. `ApplySnapshot` diffs the snapshot against current ViewModel state, raises `PropertyChanged` only for changed properties, and performs minimal `ObservableCollection` mutations — the WPF Overlay re-renders only the affected bindings.
8. Diagnostics exporter writes a snapshot to disk on demand.
