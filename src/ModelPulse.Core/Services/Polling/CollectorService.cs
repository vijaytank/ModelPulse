using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reactive.Concurrency;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using ModelPulse.Core.Adapters;
using ModelPulse.Core.Models;
using ModelPulse.Core.Services.Config;
using ModelPulse.Core.Services.System;

namespace ModelPulse.Core.Services.Polling
{
    /// <summary>
    /// Background collector service that polls runtime adapters and system
    /// telemetry, publishing immutable CollectorSnapshots on a BehaviorSubject stream.
    /// Implements adaptive polling rates and exponential backoff on network failure.
    /// Polling is scheduled recursively on an IScheduler to support deterministic virtual-time testing.
    /// </summary>
    public class CollectorService : ICollectorService
    {
        private readonly IConfigService _configService;
        private readonly SystemTelemetryProvider _systemProvider;
        private readonly IEnumerable<IRuntimeAdapter> _adapters;
        private readonly IScheduler _scheduler;
        private readonly BehaviorSubject<CollectorSnapshot> _subject;

        private CancellationTokenSource? _cts;
        private IDisposable? _loopDisposable;
        private int _consecutiveFailures;
        private TimeSpan? _overrideInterval;
        private readonly object _lock = new();

        public IObservable<CollectorSnapshot> Snapshots => _subject;

        public CollectorSnapshot LatestSnapshot => _subject.Value;

        public CollectorService(
            IConfigService configService,
            SystemTelemetryProvider systemProvider,
            IEnumerable<IRuntimeAdapter> adapters,
            IScheduler? scheduler = null)
        {
            _configService = configService;
            _systemProvider = systemProvider;
            _adapters = adapters;
            _scheduler = scheduler ?? TaskPoolScheduler.Default;
            _subject = new BehaviorSubject<CollectorSnapshot>(new CollectorSnapshot { Timestamp = DateTime.MinValue });
        }

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                if (_cts != null) return Task.CompletedTask;

                _cts = new CancellationTokenSource();
                _consecutiveFailures = 0;

                ScheduleNext(TimeSpan.Zero, _cts.Token);
            }
            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            lock (_lock)
            {
                if (_cts != null)
                {
                    _cts.Cancel();
                    _cts.Dispose();
                    _cts = null;
                }

                if (_loopDisposable != null)
                {
                    _loopDisposable.Dispose();
                    _loopDisposable = null;
                }
            }
            return Task.CompletedTask;
        }

        public void SetPollingInterval(TimeSpan interval)
        {
            lock (_lock)
            {
                if (interval <= TimeSpan.Zero)
                {
                    _overrideInterval = null;
                }
                else
                {
                    _overrideInterval = interval;
                }
            }
        }

        /// <inheritdoc/>
        public void ClearPollingOverride()
        {
            lock (_lock)
            {
                _overrideInterval = null;
            }
        }

        private void ScheduleNext(TimeSpan delay, CancellationToken ct)
        {
            lock (_lock)
            {
                if (ct.IsCancellationRequested) return;

                _loopDisposable = _scheduler.Schedule(delay, (Action<TimeSpan> recurse) =>
                {
                    if (ct.IsCancellationRequested) return;

                    try
                    {
                        // Run the poll cycle synchronously on this scheduler thread
                        PollCycleAsync().GetAwaiter().GetResult();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[CollectorService] Loop exception: {ex.Message}");
                    }

                    if (ct.IsCancellationRequested) return;

                    TimeSpan nextDelay;
                    lock (_lock)
                    {
                        nextDelay = ComputeNextDelay();
                    }

                    recurse(nextDelay);
                });
            }
        }

        private async Task PollCycleAsync()
        {
            var systemState = _systemProvider.GetSystemState();
            var runtimes = new List<RuntimeSummary>();
            var activeModels = new List<ActiveModelInfo>();
            var performance = new List<PerformanceSample>();
            var unknownFields = new List<UnknownFieldEntry>();

            bool anyRuntimeActive = false;

            foreach (var adapter in _adapters)
            {
                try
                {
                    var isAvailable = await adapter.IsAvailableAsync();
                    var summary = await adapter.GetRuntimeSummaryAsync();
                    if (summary != null)
                    {
                        runtimes.Add(summary);
                    }
                    else
                    {
                        Debug.WriteLine($"[CollectorService] Warning: Adapter {adapter.GetType().Name} returned null summary. Skipping data collection for this adapter.");
                    }

                    if (isAvailable)
                    {
                        anyRuntimeActive = true;

                        var models = await adapter.GetActiveModelsAsync();
                        if (models != null && models.Any())
                        {
                            activeModels.AddRange(models);
                        }
                        else
                        {
                            Debug.WriteLine($"[CollectorService] Warning: Adapter {adapter.GetType().Name} returned no active models.");
                        }

                        var sample = await adapter.GetPerformanceSampleAsync();
                        if (sample != null)
                        {
                            performance.Add(sample);
                        }
                        else
                        {
                            Debug.WriteLine($"[CollectorService] Warning: Adapter {adapter.GetType().Name} returned null performance sample.");
                        }

                        var fields = adapter.GetUnknownFields();
                        if (fields != null && fields.Any())
                        {
                            unknownFields.AddRange(fields);
                        }
                        else
                        {
                            Debug.WriteLine($"[CollectorService] Warning: Adapter {adapter.GetType().Name} returned no unknown fields.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[CollectorService] Exception while polling adapter {adapter.GetType().Name}: {ex.Message}");
                }
            }

            lock (_lock)
            {
                if (anyRuntimeActive)
                {
                    _consecutiveFailures = 0;
                }
                else
                {
                    _consecutiveFailures++;
                }
            }

            var snapshot = new CollectorSnapshot
            {
                Timestamp = _scheduler.Now.UtcDateTime,
                System = systemState,
                Runtimes = runtimes,
                ActiveModels = activeModels,
                Performance = performance,
                UnknownFields = unknownFields,
                PollingMode = _configService.CurrentSettings.PollingMode
            };

            _subject.OnNext(snapshot);
        }

        private TimeSpan ComputeNextDelay()
        {
            if (_overrideInterval.HasValue)
            {
                return _overrideInterval.Value;
            }

            var settings = _configService.CurrentSettings;
            var baseIdleMs = settings.PollingIntervals.IdleMs;
            var baseActiveMs = settings.PollingIntervals.ActiveMs;

            // 1. Check for exponential backoff if no runtimes are available
            if (_consecutiveFailures > 0)
            {
                var factor = Math.Pow(2, _consecutiveFailures);
                var backoffMs = baseIdleMs * factor;
                if (backoffMs > 30000)
                {
                    backoffMs = 30000; // Cap at 30 seconds
                }
                return TimeSpan.FromMilliseconds(backoffMs);
            }

            // 2. Check for active inference to scale polling interval
            bool inActiveInference = false;
            var latestSnapshot = _subject.Value;
            if (latestSnapshot != null)
            {
                foreach (var p in latestSnapshot.Performance)
                {
                    if (p.GenerationTokensPerSecond > 0 || p.ActiveSlots > 0)
                    {
                        inActiveInference = true;
                        break;
                    }
                }
            }

            var delayMs = inActiveInference ? baseActiveMs : baseIdleMs;
            return TimeSpan.FromMilliseconds(delayMs);
        }

        public void Dispose()
        {
            _subject.Dispose();
            _cts?.Dispose();
            _loopDisposable?.Dispose();
        }
    }
}
