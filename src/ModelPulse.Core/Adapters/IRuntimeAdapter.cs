using System.Collections.Generic;
using System.Threading.Tasks;
using ModelPulse.Core.Models;

namespace ModelPulse.Core.Adapters
{
    /// <summary>
    /// Normalized contract all runtime adapters must implement.
    /// Adapters should fail gracefully on all methods — never throw to callers.
    /// Unknown or newly added runtime fields must be captured as UnknownFieldEntry
    /// rather than silently discarded (see FR-08).
    /// </summary>
    public interface IRuntimeAdapter
    {
        /// <summary>Identifies this adapter: "ollama" or "llama.cpp".</summary>
        string RuntimeName { get; }

        /// <summary>
        /// Returns true if the runtime endpoint is reachable and responding.
        /// Must complete within a reasonable timeout and never throw.
        /// </summary>
        Task<bool> IsAvailableAsync();

        /// <summary>
        /// Returns a normalized runtime summary including version info and availability.
        /// Version must be captured as first-class telemetry (see FR-02).
        /// </summary>
        Task<RuntimeSummary> GetRuntimeSummaryAsync();

        /// <summary>
        /// Returns the list of models currently loaded by this runtime.
        /// Returns an empty list if unavailable — never null.
        /// </summary>
        Task<IReadOnlyList<ActiveModelInfo>> GetActiveModelsAsync();

        /// <summary>
        /// Returns the most recent throughput/slot performance sample.
        /// Fields may be null if the runtime did not report them.
        /// </summary>
        Task<PerformanceSample> GetPerformanceSampleAsync();

        /// <summary>
        /// Returns fields observed in the last API response that were not
        /// recognized by this adapter. Used for drift tracking (see FR-08).
        /// </summary>
        IReadOnlyList<UnknownFieldEntry> GetUnknownFields();
    }
}
