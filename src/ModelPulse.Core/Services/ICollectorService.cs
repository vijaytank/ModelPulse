using System;
using System.Threading;
using System.Threading.Tasks;
using ModelPulse.Core.Models;

namespace ModelPulse.Core.Services
{
    /// <summary>
    /// Background collector service that polls runtime adapters and system
    /// telemetry on a configurable interval, publishing immutable CollectorSnapshots
    /// to a single BehaviorSubject observable stream.
    /// </summary>
    public interface ICollectorService : IDisposable
    {
        /// <summary>
        /// Observable stream of immutable CollectorSnapshots.
        /// A BehaviorSubject is used so late subscribers (e.g., UI after restore
        /// from minimize) immediately receive the most recent state.
        /// </summary>
        IObservable<CollectorSnapshot> Snapshots { get; }

        /// <summary>Gets the latest published snapshot.</summary>
        CollectorSnapshot LatestSnapshot { get; }

        /// <summary>Starts the background polling loop.</summary>
        Task StartAsync(CancellationToken cancellationToken = default);

        /// <summary>Signals the polling loop to stop and awaits its completion.</summary>
        Task StopAsync();

        /// <summary>
        /// Sets the active polling interval. The collector will apply it
        /// on the next cycle without restarting the service.
        /// </summary>
        void SetPollingInterval(TimeSpan interval);

        /// <summary>
        /// Clears any active polling interval override and resumes normal
        /// adaptive scheduling. Call this after a forced refresh burst to
        /// prevent the 0ms tight-loop caused by SetPollingInterval(TimeSpan.Zero).
        /// </summary>
        void ClearPollingOverride();
    }
}
