using System;
using System.Collections.Generic;
using System.Threading;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using ModelPulse.Core.Models;
using ModelPulse.Core.Services;
using ModelPulse.Core.Services.Config;
using ModelPulse.Core.Services.Polling;

namespace ModelPulse.UI.ViewModels
{
    /// <summary>
    /// ViewModel bound to the Overlay Window. Subscribes to telemetry snapshots and
    /// propagates values with diff checks to minimize layout passes (see NFR-02).
    /// </summary>
    public class OverlayViewModel : ViewModelBase, IDisposable
    {
        private readonly ICollectorService _collectorService;
        private readonly IConfigService _configService;
        private readonly IDisposable _subscription;
        private readonly Queue<CollectorSnapshot> _historyQueue = new();

        // ─── Bindable Fields ───────────────────────────────────────────
        private bool _isCompactMode = true;
        private double _cpuPercent;
        private double _ramPercent;
        private double _gpuPercent;
        private double _vramPercent;

        private string _cpuText = "0%";
        private string _ramText = "0 / 0 GB";
        private string _gpuText = "Unavailable";
        private string _vramText = "Unavailable";

        private bool _isOllamaActive;
        private bool _isLlamaCppActive;
        private bool _isMultipleRuntimesActive;

        private string _ollamaVersion = "Unknown";
        private string _llamaCppVersion = "Unknown";
        private string _activeModelName = "No Model Loaded";
        private string _modelVramText = "-";
        private string _contextLengthText = "-";
        private string _unloadTimerText = "-";
        private string _throughputText = "0.0 t/s";

        private bool _isOllamaSelected = true;
        private bool _isLlamaCppSelected;

        // 5-Minute Trends
        private string _historyCpuAvg = "-";
        private string _historyRamAvg = "-";
        private string _historyGpuAvg = "-";
        private string _historyTpsAvg = "-";
        private string _historyPressureEvents = "0";

        // Sparkline backing fields (WPF PointCollection for Polyline binding)
        private PointCollection _cpuSparkline = new();
        private PointCollection _ramSparkline = new();
        private PointCollection _gpuSparkline = new();
        private PointCollection _tpsSparkline = new();

        // ─── Public Properties ─────────────────────────────────────────
        public bool IsCompactMode
        {
            get => _isCompactMode;
            set
            {
                if (SetProperty(ref _isCompactMode, value))
                {
                    OnPropertyChanged(nameof(CompactVisibility));
                    OnPropertyChanged(nameof(ExpandedVisibility));
                }
            }
        }

        public Visibility CompactVisibility => _isCompactMode ? Visibility.Visible : Visibility.Collapsed;
        public Visibility ExpandedVisibility => _isCompactMode ? Visibility.Collapsed : Visibility.Visible;

        public double CpuPercent
        {
            get => _cpuPercent;
            set => SetProperty(ref _cpuPercent, value);
        }

        public double RamPercent
        {
            get => _ramPercent;
            set => SetProperty(ref _ramPercent, value);
        }

        public double GpuPercent
        {
            get => _gpuPercent;
            set => SetProperty(ref _gpuPercent, value);
        }

        public double VramPercent
        {
            get => _vramPercent;
            set => SetProperty(ref _vramPercent, value);
        }

        public string CpuText
        {
            get => _cpuText;
            set => SetProperty(ref _cpuText, value);
        }

        public string RamText
        {
            get => _ramText;
            set => SetProperty(ref _ramText, value);
        }

        public string GpuText
        {
            get => _gpuText;
            set => SetProperty(ref _gpuText, value);
        }

        public string VramText
        {
            get => _vramText;
            set => SetProperty(ref _vramText, value);
        }

        public bool IsOllamaActive
        {
            get => _isOllamaActive;
            set => SetProperty(ref _isOllamaActive, value);
        }

        public bool IsLlamaCppActive
        {
            get => _isLlamaCppActive;
            set => SetProperty(ref _isLlamaCppActive, value);
        }

        public bool IsMultipleRuntimesActive
        {
            get => _isMultipleRuntimesActive;
            set => SetProperty(ref _isMultipleRuntimesActive, value);
        }

        public string OllamaVersion
        {
            get => _ollamaVersion;
            set => SetProperty(ref _ollamaVersion, value);
        }

        public string LlamaCppVersion
        {
            get => _llamaCppVersion;
            set => SetProperty(ref _llamaCppVersion, value);
        }

        public string ActiveModelName
        {
            get => _activeModelName;
            set => SetProperty(ref _activeModelName, value);
        }

        public string ModelVramText
        {
            get => _modelVramText;
            set => SetProperty(ref _modelVramText, value);
        }

        public string ContextLengthText
        {
            get => _contextLengthText;
            set => SetProperty(ref _contextLengthText, value);
        }

        public string UnloadTimerText
        {
            get => _unloadTimerText;
            set => SetProperty(ref _unloadTimerText, value);
        }

        public string ThroughputText
        {
            get => _throughputText;
            set => SetProperty(ref _throughputText, value);
        }

        public bool IsOllamaSelected
        {
            get => _isOllamaSelected;
            set
            {
                if (SetProperty(ref _isOllamaSelected, value) && value)
                {
                    IsLlamaCppSelected = false;
                }
            }
        }

        public bool IsLlamaCppSelected
        {
            get => _isLlamaCppSelected;
            set
            {
                if (SetProperty(ref _isLlamaCppSelected, value) && value)
                {
                    IsOllamaSelected = false;
                }
            }
        }

        public string HistoryCpuAvg
        {
            get => _historyCpuAvg;
            set => SetProperty(ref _historyCpuAvg, value);
        }

        public string HistoryRamAvg
        {
            get => _historyRamAvg;
            set => SetProperty(ref _historyRamAvg, value);
        }

        public string HistoryGpuAvg
        {
            get => _historyGpuAvg;
            set => SetProperty(ref _historyGpuAvg, value);
        }

        public string HistoryTpsAvg
        {
            get => _historyTpsAvg;
            set => SetProperty(ref _historyTpsAvg, value);
        }

        public string HistoryPressureEvents
        {
            get => _historyPressureEvents;
            set => SetProperty(ref _historyPressureEvents, value);
        }

        // ─── Sparkline Properties ──────────────────────────────────────
        /// <summary>Normalized CPU sparkline points for last-5-min Polyline binding.</summary>
        public PointCollection CpuSparklinePoints
        {
            get => _cpuSparkline;
            private set { _cpuSparkline = value; OnPropertyChanged(nameof(CpuSparklinePoints)); }
        }

        /// <summary>Normalized RAM sparkline points for last-5-min Polyline binding.</summary>
        public PointCollection RamSparklinePoints
        {
            get => _ramSparkline;
            private set { _ramSparkline = value; OnPropertyChanged(nameof(RamSparklinePoints)); }
        }

        /// <summary>Normalized GPU sparkline points for last-5-min Polyline binding.</summary>
        public PointCollection GpuSparklinePoints
        {
            get => _gpuSparkline;
            private set { _gpuSparkline = value; OnPropertyChanged(nameof(GpuSparklinePoints)); }
        }

        /// <summary>Normalized t/s sparkline points for last-5-min Polyline binding.</summary>
        public PointCollection TpsSparklinePoints
        {
            get => _tpsSparkline;
            private set { _tpsSparkline = value; OnPropertyChanged(nameof(TpsSparklinePoints)); }
        }

        // ─── Constructor & Setup ───────────────────────────────────────
        public OverlayViewModel(ICollectorService collectorService, IConfigService configService)
        {
            _collectorService = collectorService;
            _configService = configService;

            var isWpf = Application.Current != null;

            // Subscribe to collector snapshots, ensuring we dispatch UI updates safely
            _subscription = _collectorService.Snapshots
                .Subscribe(snapshot =>
                {
                    if (isWpf && Application.Current != null)
                    {
                        Application.Current.Dispatcher.BeginInvoke(new Action(() => UpdateFromSnapshot(snapshot)));
                    }
                    else
                    {
                        UpdateFromSnapshot(snapshot);
                    }
                });
        }

        private void UpdateFromSnapshot(CollectorSnapshot snapshot)
        {
            if (snapshot == null || snapshot.Timestamp == DateTime.MinValue) return;

            // 1. System Telemetry Update
            CpuPercent = snapshot.System.CpuPercent;
            CpuText = $"{CpuPercent:F0}%";

            double ramUsedGb = snapshot.System.RamUsedMb / 1024.0;
            double ramTotalGb = snapshot.System.RamTotalMb / 1024.0;
            RamPercent = snapshot.System.RamTotalMb > 0 ? (snapshot.System.RamUsedMb / snapshot.System.RamTotalMb) * 100.0 : 0;
            RamText = $"{ramUsedGb:F1} / {ramTotalGb:F1} GB";

            if (snapshot.System.Gpu.SourcePath != "Unavailable")
            {
                GpuPercent = snapshot.System.Gpu.UtilizationPercent;
                GpuText = $"{GpuPercent:F0}%";

                double vramUsedGb = snapshot.System.Gpu.VramUsedMb / 1024.0;
                double vramTotalGb = snapshot.System.Gpu.VramTotalMb / 1024.0;
                VramPercent = snapshot.System.Gpu.VramTotalMb > 0 ? (snapshot.System.Gpu.VramUsedMb / snapshot.System.Gpu.VramTotalMb) * 100.0 : 0;
                VramText = $"{vramUsedGb:F1} / {vramTotalGb:F1} GB";
            }
            else
            {
                GpuPercent = 0;
                GpuText = "Unavailable";
                VramPercent = 0;
                VramText = "Unavailable";
            }

            // 2. Runtimes availability
            var ollama = snapshot.Runtimes.FirstOrDefault(r => r.RuntimeName == "ollama");
            var llamacpp = snapshot.Runtimes.FirstOrDefault(r => r.RuntimeName == "llama.cpp");

            IsOllamaActive = ollama?.IsAvailable ?? false;
            IsLlamaCppActive = llamacpp?.IsAvailable ?? false;
            IsMultipleRuntimesActive = IsOllamaActive && IsLlamaCppActive;

            OllamaVersion = ollama?.RuntimeVersion ?? "Unknown";
            LlamaCppVersion = llamacpp?.RuntimeVersion ?? "Unknown";

            // If only LlamaCpp is active, default selected to it
            if (IsLlamaCppActive && !IsOllamaActive)
            {
                IsLlamaCppSelected = true;
            }
            else if (IsOllamaActive && !IsLlamaCppActive)
            {
                IsOllamaSelected = true;
            }

            // 3. Model Telemetry & Throughput Update
            if (IsOllamaSelected && IsOllamaActive)
            {
                var activeModel = snapshot.ActiveModels.FirstOrDefault(m => m.RuntimeName == "ollama");
                var perf = snapshot.Performance.FirstOrDefault(p => p.RuntimeName == "ollama");

                ActiveModelName = activeModel?.ModelName ?? "No Model Loaded";

                if (activeModel != null)
                {
                    double vramMb = (activeModel.SizeVramBytes ?? 0) / (1024.0 * 1024.0);
                    ModelVramText = $"{vramMb:F0} MB VRAM";
                    ContextLengthText = activeModel.ContextLength.HasValue ? $"{activeModel.ContextLength.Value}" : "-";
                    UnloadTimerText = activeModel.ExpiresAt.HasValue ? $"{Math.Max(0, (activeModel.ExpiresAt.Value - DateTime.UtcNow).TotalMinutes):F0} min" : "-";
                }
                else
                {
                    ModelVramText = "-";
                    ContextLengthText = "-";
                    UnloadTimerText = "-";
                }

                ThroughputText = perf?.GenerationTokensPerSecond.HasValue == true ? $"{perf.GenerationTokensPerSecond.Value:F1} t/s" : "0.0 t/s";
            }
            else if (IsLlamaCppSelected && IsLlamaCppActive)
            {
                // Llama.cpp single-model status mapping
                var perf = snapshot.Performance.FirstOrDefault(p => p.RuntimeName == "llama.cpp");

                ActiveModelName = IsLlamaCppActive ? "Active Server Model" : "No Model Loaded";
                ModelVramText = "-";
                ContextLengthText = "-";
                UnloadTimerText = "-";
                ThroughputText = perf?.GenerationTokensPerSecond.HasValue == true ? $"{perf.GenerationTokensPerSecond.Value:F1} t/s" : "0.0 t/s";
            }
            else
            {
                ActiveModelName = "No Model Loaded";
                ModelVramText = "-";
                ContextLengthText = "-";
                UnloadTimerText = "-";
                ThroughputText = "0.0 t/s";
            }

            // 4. Rolling 5-Minute History calculations
            _historyQueue.Enqueue(snapshot);
            var cutoff = snapshot.Timestamp.AddMinutes(-5);
            while (_historyQueue.Count > 0 && _historyQueue.Peek().Timestamp < cutoff)
            {
                _historyQueue.Dequeue();
            }

            CalculateHistoryStats();
        }

        private void CalculateHistoryStats()
        {
            if (_historyQueue.Count == 0) return;

            double cpuSum = 0;
            double ramSum = 0;
            double gpuSum = 0;
            double tpsSum = 0;
            int gpuCount = 0;
            int tpsCount = 0;
            int pressureEvents = 0;

            var threshold = _configService.CurrentSettings.Alerts.VramWarningThresholdPercent;

            foreach (var snap in _historyQueue)
            {
                cpuSum += snap.System.CpuPercent;
                ramSum += snap.System.RamUsedMb;

                if (snap.System.Gpu.SourcePath != "Unavailable")
                {
                    gpuSum += snap.System.Gpu.UtilizationPercent;
                    gpuCount++;

                    // Count VRAM pressure event
                    if (snap.System.Gpu.VramTotalMb > 0)
                    {
                        var pct = (snap.System.Gpu.VramUsedMb / snap.System.Gpu.VramTotalMb) * 100.0;
                        if (pct >= threshold)
                        {
                            pressureEvents++;
                        }
                    }
                }

                // RAM pressure event (custom fixed threshold of 95%)
                if (snap.System.RamTotalMb > 0)
                {
                    var pct = (snap.System.RamUsedMb / snap.System.RamTotalMb) * 100.0;
                    if (pct >= 95.0)
                    {
                        pressureEvents++;
                    }
                }

                foreach (var p in snap.Performance)
                {
                    if (p.GenerationTokensPerSecond.HasValue)
                    {
                        tpsSum += p.GenerationTokensPerSecond.Value;
                        tpsCount++;
                    }
                }
            }

            HistoryCpuAvg = $"{cpuSum / _historyQueue.Count:F0}%";
            HistoryRamAvg = $"{(ramSum / _historyQueue.Count) / 1024.0:F1} GB";
            HistoryGpuAvg = gpuCount > 0 ? $"{gpuSum / gpuCount:F0}%" : "Unavailable";
            HistoryTpsAvg = tpsCount > 0 ? $"{tpsSum / tpsCount:F1} t/s" : "0.0 t/s";
            HistoryPressureEvents = $"{pressureEvents}";

            // Rebuild sparkline point collections from the current queue
            RebuildSparklines();
        }

        /// <summary>
        /// Rebuilds WPF PointCollection objects from the 5-minute history queue.
        /// X is the sample index (0..N-1), Y is normalized to the canvas height (0=top, 40=bottom).
        /// Uses a fixed canvas height of 40px to match the XAML Canvas.Height.
        /// Max values are computed per-rebuild for natural auto-scaling.
        /// </summary>
        private void RebuildSparklines()
        {
            const double canvasHeight = 40.0;
            const double canvasWidth  = 360.0; // approximate expanded overlay width

            var samples = _historyQueue.ToArray();
            int count = samples.Length;
            if (count == 0) return;

            // Compute max values for normalization (avoid divide-by-zero)
            double maxCpu = Math.Max(1.0, samples.Max(s => s.System.CpuPercent));
            double maxRam = Math.Max(1.0, samples.Max(s => s.System.RamUsedMb));
            double maxGpu = Math.Max(1.0, samples.Where(s => s.System.Gpu.SourcePath != "Unavailable")
                                                  .Select(s => s.System.Gpu.UtilizationPercent)
                                                  .DefaultIfEmpty(1.0).Max());
            double maxTps = Math.Max(0.1, samples.SelectMany(s => s.Performance)
                                                   .Where(p => p.GenerationTokensPerSecond.HasValue)
                                                   .Select(p => p.GenerationTokensPerSecond!.Value)
                                                   .DefaultIfEmpty(0.1).Max());

            var cpuPts  = new PointCollection(count);
            var ramPts  = new PointCollection(count);
            var gpuPts  = new PointCollection(count);
            var tpsPts  = new PointCollection(count);

            for (int i = 0; i < count; i++)
            {
                double x = count == 1 ? 0 : (i / (double)(count - 1)) * canvasWidth;

                // Y: invert so high value = near top (low Y in WPF coords)
                double cpuY  = canvasHeight - (samples[i].System.CpuPercent / maxCpu) * canvasHeight;
                double ramY  = canvasHeight - (samples[i].System.RamUsedMb   / maxRam) * canvasHeight;

                double gpuUtil = samples[i].System.Gpu.SourcePath != "Unavailable"
                    ? samples[i].System.Gpu.UtilizationPercent : 0;
                double gpuY  = canvasHeight - (gpuUtil / maxGpu) * canvasHeight;

                var perfSample = samples[i].Performance.FirstOrDefault(p => p.GenerationTokensPerSecond.HasValue);
                double tpsVal = perfSample?.GenerationTokensPerSecond ?? 0;
                double tpsY  = canvasHeight - (tpsVal / maxTps) * canvasHeight;

                cpuPts.Add(new Point(x, cpuY));
                ramPts.Add(new Point(x, ramY));
                gpuPts.Add(new Point(x, gpuY));
                tpsPts.Add(new Point(x, tpsY));
            }

            // Freeze for performance (read-only after creation is safe for WPF bindings)
            cpuPts.Freeze();
            ramPts.Freeze();
            gpuPts.Freeze();
            tpsPts.Freeze();

            CpuSparklinePoints = cpuPts;
            RamSparklinePoints = ramPts;
            GpuSparklinePoints = gpuPts;
            TpsSparklinePoints = tpsPts;
        }

        public void Dispose()
        {
            _subscription.Dispose();
        }
    }
}
