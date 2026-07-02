using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using ModelPulse.Core.Adapters.LlamaCpp;
using ModelPulse.Core.Models;

namespace ModelPulse.Core.Adapters.LlamaCpp
{
    /// <summary>
    /// Adapter for the llama.cpp local server runtime.
    /// Queries /health for availability and slot state.
    /// Queries /metrics (Prometheus text format) for throughput counters.
    /// Captures unknown metric names for drift tracking (FR-08).
    /// </summary>
    public class LlamaCppAdapter : IRuntimeAdapter
    {
        public string RuntimeName => "llama.cpp";

        private readonly HttpClient _httpClient;
        private readonly List<UnknownFieldEntry> _unknownFields = new();
        private readonly object _lock = new();

        private static readonly HashSet<string> KnownMetricNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "llamacpp:prompt_tokens_total",
            "llamacpp:tokens_predicted_total",
            "llamacpp:slots_active",
            "llamacpp:kv_cache_usage_ratio",
            "llamacpp:requests_processing",
            "llamacpp:requests_deferred"
        };

        private double? _lastPromptTokens;
        private double? _lastPredictedTokens;
        private DateTime? _lastMetricsTime;

        public LlamaCppAdapter(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        /// <inheritdoc/>
        public async Task<bool> IsAvailableAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync("/health");
                if (!response.IsSuccessStatusCode) return false;

                var content = await response.Content.ReadAsStringAsync();
                var health = LlamaCppParser.ParseHealthResponse(content);
                return health.Status.Equals("ok", StringComparison.OrdinalIgnoreCase)
                    || health.Status.Equals("loading model", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        /// <inheritdoc/>
        public async Task<RuntimeSummary> GetRuntimeSummaryAsync()
        {
            var available = await IsAvailableAsync();

            // llama.cpp does not expose a version API endpoint in the standard build.
            // Version is logged as "Unknown" unless captured from server startup logs.
            return new RuntimeSummary
            {
                RuntimeName = RuntimeName,
                RuntimeVersion = "Unknown",
                IsAvailable = available,
                Endpoint = _httpClient.BaseAddress?.ToString() ?? "http://127.0.0.1:8080",
                Notes = "llama.cpp does not expose a version endpoint; version captured from diagnostics only."
            };
        }

        /// <inheritdoc/>
        public async Task<IReadOnlyList<ActiveModelInfo>> GetActiveModelsAsync()
        {
            // llama.cpp does not expose a running models list via a standard endpoint.
            // Active slot state is returned as part of GetPerformanceSampleAsync.
            return await Task.FromResult(Array.Empty<ActiveModelInfo>());
        }

        /// <inheritdoc/>
        public async Task<PerformanceSample> GetPerformanceSampleAsync()
        {
            var sample = new PerformanceSample { RuntimeName = RuntimeName };

            try
            {
                // Query health for slot state
                var healthResponse = await _httpClient.GetAsync("/health");
                if (healthResponse.IsSuccessStatusCode)
                {
                    var healthContent = await healthResponse.Content.ReadAsStringAsync();
                    var health = LlamaCppParser.ParseHealthResponse(healthContent);
                    sample.IdleSlots = health.SlotsIdle;
                }

                // Query Prometheus metrics for throughput and slot details
                var metricsResponse = await _httpClient.GetAsync("/metrics");
                if (metricsResponse.IsSuccessStatusCode)
                {
                    var metricsContent = await metricsResponse.Content.ReadAsStringAsync();
                    var metrics = ParseMetricsWithDriftDetection(metricsContent);
                    sample.ActiveSlots = metrics.ActiveSlots > 0 ? metrics.ActiveSlots : sample.ActiveSlots;

                    var now = DateTime.UtcNow;
                    lock (_lock)
                    {
                        if (_lastMetricsTime.HasValue)
                        {
                            var elapsedSeconds = (now - _lastMetricsTime.Value).TotalSeconds;
                            if (elapsedSeconds > 0)
                            {
                                if (_lastPromptTokens.HasValue && metrics.PromptTokensTotal >= _lastPromptTokens.Value)
                                {
                                    sample.PromptTokensPerSecond = (metrics.PromptTokensTotal - _lastPromptTokens.Value) / elapsedSeconds;
                                }
                                if (_lastPredictedTokens.HasValue && metrics.TokensPredictedTotal >= _lastPredictedTokens.Value)
                                {
                                    sample.GenerationTokensPerSecond = (metrics.TokensPredictedTotal - _lastPredictedTokens.Value) / elapsedSeconds;
                                }
                            }
                        }

                        _lastPromptTokens = metrics.PromptTokensTotal;
                        _lastPredictedTokens = metrics.TokensPredictedTotal;
                        _lastMetricsTime = now;
                    }
                }
            }
            catch
            {
                // Adapter must not throw — return partial sample
            }

            return sample;
        }

        /// <inheritdoc/>
        public IReadOnlyList<UnknownFieldEntry> GetUnknownFields()
        {
            lock (_lock)
            {
                return _unknownFields.AsReadOnly();
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // Private Parsing
        // ─────────────────────────────────────────────────────────────────

        private LlamaCppMetrics ParseMetricsWithDriftDetection(string rawMetrics)
        {
            var metrics = LlamaCppParser.ParseMetrics(rawMetrics);
            var now = DateTime.UtcNow;

            if (string.IsNullOrWhiteSpace(rawMetrics)) return metrics;

            // Scan for unknown metric names in Prometheus output
            var lines = rawMetrics.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            lock (_lock)
            {
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("#") || !trimmed.StartsWith("llamacpp:")) continue;

                    var parts = trimmed.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 2) continue;

                    var metricName = parts[0];
                    if (KnownMetricNames.Contains(metricName)) continue;

                    // Unknown metric found
                    var existing = _unknownFields.Find(f => f.FieldName == metricName);
                    if (existing != null)
                    {
                        existing.LastSeenAt = now;
                    }
                    else
                    {
                        _unknownFields.Add(new UnknownFieldEntry
                        {
                            RuntimeName = RuntimeName,
                            FieldName = metricName,
                            FirstSeenAt = now,
                            LastSeenAt = now,
                            SampleValue = parts.Length > 1 ? parts[1] : null
                        });
                    }
                }
            }

            return metrics;
        }
    }
}
