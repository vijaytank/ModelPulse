using System;
using System.Collections.Generic;

namespace ModelPulse.Core.Models
{
    // ─────────────────────────────────────────────
    // GPU & System Hardware Telemetry
    // ─────────────────────────────────────────────

    /// <summary>
    /// Represents the GPU telemetry state resolved via NVML → DXGI → WMI fallback chain.
    /// SourcePath indicates which provider resolved the data.
    /// </summary>
    public class GpuTelemetryState
    {
        public string Name { get; set; } = "Unknown GPU";
        public double UtilizationPercent { get; set; }
        public double VramUsedMb { get; set; }
        public double VramTotalMb { get; set; }
        public double TemperatureC { get; set; }
        /// <summary>"NVML", "DXGI", "WMI", or "Unavailable".</summary>
        public string SourcePath { get; set; } = "Unavailable";
    }

    /// <summary>
    /// CPU, RAM, and GPU system telemetry collected each polling cycle.
    /// Also tracks the widget's own resource footprint.
    /// </summary>
    public class SystemTelemetryState
    {
        public double CpuPercent { get; set; }
        public double RamUsedMb { get; set; }
        public double RamTotalMb { get; set; }
        /// <summary>ModelPulse process working set (self-monitoring).</summary>
        public double WidgetRamMb { get; set; }
        public GpuTelemetryState Gpu { get; set; } = new();
    }

    // ─────────────────────────────────────────────
    // Runtime Adapter Normalized Domain Models
    // ─────────────────────────────────────────────

    /// <summary>
    /// Normalized runtime availability metadata emitted by each adapter.
    /// Includes version information for drift tracking and diagnostics.
    /// </summary>
    public class RuntimeSummary
    {
        /// <summary>"ollama" or "llama.cpp"</summary>
        public string RuntimeName { get; set; } = string.Empty;
        public string RuntimeVersion { get; set; } = "Unknown";
        public bool IsAvailable { get; set; }
        public string Endpoint { get; set; } = string.Empty;
        public string? Notes { get; set; }
    }

    /// <summary>
    /// Represents a single active model loaded by a runtime.
    /// </summary>
    public class ActiveModelInfo
    {
        public string RuntimeName { get; set; } = string.Empty;
        public string ModelName { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public long? SizeVramBytes { get; set; }
        public long? ContextLength { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public string? QuantizationLevel { get; set; }
        public string? ParameterSize { get; set; }
        public string? Family { get; set; }
    }

    /// <summary>
    /// A throughput/performance sample from an active runtime.
    /// Fields may be null if the runtime did not report them in this cycle.
    /// </summary>
    public class PerformanceSample
    {
        public string RuntimeName { get; set; } = string.Empty;
        public double? PromptTokensPerSecond { get; set; }
        public double? GenerationTokensPerSecond { get; set; }
        public double? LoadDurationMs { get; set; }
        public double? TotalDurationMs { get; set; }
        public int? ActiveSlots { get; set; }
        public int? IdleSlots { get; set; }
    }

    // ─────────────────────────────────────────────
    // Collector Snapshot (Top-Level State Object)
    // ─────────────────────────────────────────────

    /// <summary>
    /// Immutable snapshot produced by the Collector Core at the end of each polling cycle.
    /// Published via BehaviorSubject&lt;CollectorSnapshot&gt; to all three subscribers.
    /// </summary>
    public class CollectorSnapshot
    {
        public DateTime Timestamp { get; init; } = DateTime.UtcNow;
        public SystemTelemetryState System { get; init; } = new();
        public IReadOnlyList<RuntimeSummary> Runtimes { get; init; } = Array.Empty<RuntimeSummary>();
        public IReadOnlyList<ActiveModelInfo> ActiveModels { get; init; } = Array.Empty<ActiveModelInfo>();
        public IReadOnlyList<PerformanceSample> Performance { get; init; } = Array.Empty<PerformanceSample>();
        public IReadOnlyList<UnknownFieldEntry> UnknownFields { get; init; } = Array.Empty<UnknownFieldEntry>();
        /// <summary>Current polling mode active at snapshot time.</summary>
        public string PollingMode { get; init; } = "default";
    }

    // ─────────────────────────────────────────────
    // Alert System Models
    // ─────────────────────────────────────────────

    /// <summary>Alert severity tiers.</summary>
    public enum AlertSeverity { Info, Warning, Critical }

    /// <summary>
    /// Describes an alert event evaluated by the Alert Engine.
    /// </summary>
    public class Alert
    {
        public string AlertId { get; set; } = Guid.NewGuid().ToString("N")[..8];
        /// <summary>Which runtime triggered this alert, or null for system-wide alerts.</summary>
        public string? RuntimeName { get; set; }
        /// <summary>"memory_pressure", "disconnected", "performance_drop"</summary>
        public string AlertType { get; set; } = string.Empty;
        public AlertSeverity Severity { get; set; }
        public int CooldownSeconds { get; set; } = 60;
        public DateTime? LastTriggeredAt { get; set; }
        public bool Suppressed { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    // ─────────────────────────────────────────────
    // Unknown Field Drift Tracking
    // ─────────────────────────────────────────────

    /// <summary>
    /// Records a previously unseen field discovered in a runtime API response.
    /// Used for endpoint drift detection and diagnostics snapshot export.
    /// </summary>
    public class UnknownFieldEntry
    {
        public string RuntimeName { get; set; } = string.Empty;
        public string FieldName { get; set; } = string.Empty;
        public DateTime FirstSeenAt { get; set; }
        public DateTime LastSeenAt { get; set; }
        /// <summary>Truncated sample value for debugging. Never used for computation.</summary>
        public string? SampleValue { get; set; }
    }

    // ─────────────────────────────────────────────
    // Rolling History Model
    // ─────────────────────────────────────────────

    /// <summary>
    /// 5-minute rolling summary produced by the HistoryAggregator.
    /// Displayed in the "Last 5 Minutes" section of the expanded overlay.
    /// </summary>
    public class RollingHistorySummary
    {
        public int HistoryWindowMinutes { get; init; } = 5;
        public double? CpuAvg5m { get; set; }
        public double? RamAvg5m { get; set; }
        public double? GpuAvg5m { get; set; }
        public double? GenTpsAvg5m { get; set; }
        public double? PromptTpsAvg5m { get; set; }
        public int PressureEvents5m { get; set; }
    }
}
