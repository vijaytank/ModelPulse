using System;
using System.Collections.Generic;
using System.Reactive.Concurrency;
using System.Reactive.Subjects;
using ModelPulse.Core.Models;
using ModelPulse.Core.Services.Config;

namespace ModelPulse.Core.Services.Alerts
{
    /// <summary>
    /// Alert evaluation engine. Subscribes to CollectorSnapshots and emits Alert events.
    /// Supports per-runtime scoping, cooldown rules, and severity levels.
    /// Thread-safe and supports virtual-time scheduler for deterministic testing.
    /// </summary>
    public class AlertEngine : IAlertEngine
    {
        private readonly IConfigService _configService;
        private readonly IScheduler _scheduler;
        private readonly Subject<Alert> _subject = new();
        
        private readonly Dictionary<string, DateTime> _lastTriggered = new();
        private readonly Dictionary<string, bool> _lastAvailability = new();
        private readonly HashSet<string> _suppressedTypes = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _lock = new();

        public IObservable<Alert> Alerts => _subject;

        public AlertEngine(IConfigService configService, IScheduler? scheduler = null)
        {
            _configService = configService;
            _scheduler = scheduler ?? TaskPoolScheduler.Default;
        }

        public void EvaluateSnapshot(CollectorSnapshot snapshot)
        {
            if (snapshot == null || snapshot.Timestamp == DateTime.MinValue) return;

            var now = _scheduler.Now.UtcDateTime;
            var settings = _configService.CurrentSettings;
            var cooldown = settings.Alerts.CooldownSeconds;

            lock (_lock)
            {
                // 1. Evaluate System RAM Memory Pressure
                if (snapshot.System.RamTotalMb > 0)
                {
                    var ramPct = (snapshot.System.RamUsedMb / snapshot.System.RamTotalMb) * 100.0;
                    if (ramPct >= 95.0)
                    {
                        TriggerAlert("system", "memory_pressure", AlertSeverity.Critical, cooldown, now, 
                            $"System RAM pressure is critical: {ramPct:F0}% utilized.");
                    }
                }

                // 2. Evaluate GPU VRAM Memory Pressure
                var gpu = snapshot.System.Gpu;
                if (gpu.SourcePath != "Unavailable" && gpu.VramTotalMb > 0)
                {
                    var vramPct = (gpu.VramUsedMb / gpu.VramTotalMb) * 100.0;
                    var threshold = settings.Alerts.VramWarningThresholdPercent;
                    if (vramPct >= threshold)
                    {
                        var severity = vramPct >= 98.0 ? AlertSeverity.Critical : AlertSeverity.Warning;
                        TriggerAlert("system", "memory_pressure", severity, cooldown, now, 
                            $"GPU VRAM pressure is high: {vramPct:F0}% utilized ({gpu.VramUsedMb:F0}/{gpu.VramTotalMb:F0} MB).");
                    }
                }

                // 3. Evaluate Runtime Disconnection alerts
                foreach (var runtime in snapshot.Runtimes)
                {
                    var name = runtime.RuntimeName.ToLowerInvariant();
                    
                    // Check if availability transitioned from true -> false
                    if (_lastAvailability.TryGetValue(name, out var lastAvailable))
                    {
                        if (lastAvailable && !runtime.IsAvailable)
                        {
                            TriggerAlert(name, "disconnected", AlertSeverity.Critical, cooldown, now, 
                                $"Runtime '{runtime.RuntimeName}' has disconnected or become unreachable.");
                        }
                    }
                    
                    _lastAvailability[name] = runtime.IsAvailable;
                }
            }
        }

        private void TriggerAlert(string runtime, string alertType, AlertSeverity severity, int cooldownSeconds, DateTime now, string message)
        {
            // Suppressed globally by type?
            if (_suppressedTypes.Contains(alertType)) return;

            // Unique key for the cooldown tracker (e.g. system_memory_pressure, ollama_disconnected)
            var key = $"{runtime}_{alertType}";

            if (_lastTriggered.TryGetValue(key, out var lastTime))
            {
                if ((now - lastTime).TotalSeconds < cooldownSeconds)
                {
                    return; // Suppressed by cooldown timer
                }
            }

            var alert = new Alert
            {
                RuntimeName = runtime == "system" ? null : runtime,
                AlertType = alertType,
                Severity = severity,
                CooldownSeconds = cooldownSeconds,
                LastTriggeredAt = now,
                Message = message
            };

            _lastTriggered[key] = now;
            _subject.OnNext(alert);
        }

        public void SuppressAlertType(string alertType)
        {
            lock (_lock)
            {
                _suppressedTypes.Add(alertType);
            }
        }

        public void UnsuppressAlertType(string alertType)
        {
            lock (_lock)
            {
                _suppressedTypes.Remove(alertType);
            }
        }

        public void Dispose()
        {
            _subject.Dispose();
        }
    }
}
