using System;
using ModelPulse.Core.Models;

namespace ModelPulse.Core.Services
{
    /// <summary>
    /// Alert evaluation engine. Subscribes to CollectorSnapshots on a background
    /// thread and emits Alert events when thresholds are crossed.
    ///
    /// Evaluation rules:
    /// - Per-runtime: Ollama and llama.cpp alert queues are kept separate (see FR-05).
    /// - Cooldown-aware: Same alert type will not re-fire within its cooldown window (see FR-06).
    /// - Severity-tiered: info, warning, critical (see NFR-04).
    /// </summary>
    public interface IAlertEngine : IDisposable
    {
        /// <summary>
        /// Observable stream of fired alerts.
        /// Only emits when a threshold is crossed AND the cooldown for that
        /// alert type has elapsed.
        /// </summary>
        IObservable<Alert> Alerts { get; }

        /// <summary>
        /// Evaluates the latest snapshot for threshold violations.
        /// Called by the Collector Core subscriber on a background thread.
        /// Must never block the calling thread.
        /// </summary>
        void EvaluateSnapshot(CollectorSnapshot snapshot);

        /// <summary>
        /// Suppresses all future alerts of the given type.
        /// Can be toggled by user settings (see FR-06).
        /// </summary>
        void SuppressAlertType(string alertType);

        /// <summary>Restores alerts of the given type after user re-enables them.</summary>
        void UnsuppressAlertType(string alertType);
    }
}
