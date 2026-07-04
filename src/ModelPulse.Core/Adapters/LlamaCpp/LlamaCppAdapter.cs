using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Linq;
using System.Diagnostics;
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

        // Slots tracking for fallback telemetry (e.g. when /metrics is not supported)
        private readonly Dictionary<int, (int id_task, int n_decoded, int n_prompt)> _lastSlotTasks = new();
        private double _sessionGeneratedTokens;
        private double _sessionPromptTokens;
        private double _lastSessionGeneratedTokens;
        private double _lastSessionPromptTokens;

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
            string version = "Unknown";

            if (available)
            {
                try
                {
                    var response = await _httpClient.GetAsync("/props");
                    if (response.IsSuccessStatusCode)
                    {
                        var content = await response.Content.ReadAsStringAsync();
                        using var doc = JsonDocument.Parse(content);
                        if (doc.RootElement.TryGetProperty("build_info", out var buildInfoProp))
                        {
                            var buildInfo = buildInfoProp.GetString();
                            if (!string.IsNullOrEmpty(buildInfo))
                            {
                                version = buildInfo;
                            }
                        }
                    }
                }
                catch
                {
                    // Ignore, fallback to Unknown
                }
            }

            return new RuntimeSummary
            {
                RuntimeName = RuntimeName,
                RuntimeVersion = version,
                IsAvailable = available,
                Endpoint = _httpClient.BaseAddress?.ToString() ?? "http://127.0.0.1:8080",
                Notes = "llama.cpp details resolved via /props and OpenAI /v1/models endpoints."
            };
        }

        /// <inheritdoc/>
        public async Task<IReadOnlyList<ActiveModelInfo>> GetActiveModelsAsync()
        {
            var activeModels = new List<ActiveModelInfo>();
            try
            {
                var response = await _httpClient.GetAsync("/v1/models");
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(content);
                    if (doc.RootElement.TryGetProperty("data", out var dataProp) && dataProp.ValueKind == JsonValueKind.Array)
                    {
                        var array = dataProp.EnumerateArray().ToList();
                        string? loadedModelId = null;
                        long? modelSizeBytes = null;
                        int? contextLength = null;

                        // 1. Look for status.value == "loaded"
                        foreach (var modelElement in array)
                        {
                            if (modelElement.TryGetProperty("status", out var statusProp) &&
                                statusProp.TryGetProperty("value", out var valueProp) &&
                                valueProp.GetString() == "loaded")
                            {
                                loadedModelId = modelElement.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
                                if (modelElement.TryGetProperty("meta", out var metaProp))
                                {
                                    if (metaProp.TryGetProperty("size", out var sizeProp) && sizeProp.ValueKind == JsonValueKind.Number)
                                        modelSizeBytes = sizeProp.GetInt64();
                                    if (metaProp.TryGetProperty("n_ctx", out var ctxProp) && ctxProp.ValueKind == JsonValueKind.Number)
                                        contextLength = ctxProp.GetInt32();
                                }
                                break;
                            }
                        }

                        // 2. If not found and there is exactly 1 model in router, assume it's loaded (single-model fallback)
                        if (string.IsNullOrEmpty(loadedModelId) && array.Count == 1)
                        {
                            var modelElement = array[0];
                            loadedModelId = modelElement.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
                            if (modelElement.TryGetProperty("meta", out var metaProp))
                            {
                                if (metaProp.TryGetProperty("size", out var sizeProp) && sizeProp.ValueKind == JsonValueKind.Number)
                                    modelSizeBytes = sizeProp.GetInt64();
                                if (metaProp.TryGetProperty("n_ctx", out var ctxProp) && ctxProp.ValueKind == JsonValueKind.Number)
                                    contextLength = ctxProp.GetInt32();
                            }
                        }

                        if (!string.IsNullOrEmpty(loadedModelId))
                        {
                            activeModels.Add(new ActiveModelInfo
                            {
                                RuntimeName = RuntimeName,
                                ModelName = loadedModelId,
                                SizeVramBytes = modelSizeBytes,
                                ContextLength = contextLength
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LlamaCppAdapter] Error fetching active models: {ex.Message}");
            }
            return activeModels;
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

                // Query Prometheus metrics for throughput and slot details if available
                bool metricsSupported = false;
                LlamaCppMetrics metrics = new();
                try
                {
                    var metricsResponse = await _httpClient.GetAsync("/metrics");
                    if (metricsResponse.IsSuccessStatusCode)
                    {
                        var metricsContent = await metricsResponse.Content.ReadAsStringAsync();
                        metrics = ParseMetricsWithDriftDetection(metricsContent);
                        sample.ActiveSlots = metrics.ActiveSlots > 0 ? metrics.ActiveSlots : sample.ActiveSlots;
                        metricsSupported = true;
                    }
                }
                catch
                {
                    // Metrics endpoint not supported or failed
                }

                // Fallback to slots endpoint for active/idle slots and token calculations if metrics not supported
                try
                {
                    var activeModels = await GetActiveModelsAsync();
                    var loadedModelName = activeModels.FirstOrDefault()?.ModelName;
                    var slotsUrl = !string.IsNullOrEmpty(loadedModelName) ? $"/slots?model={Uri.EscapeDataString(loadedModelName)}" : "/slots";
                    var slotsResponse = await _httpClient.GetAsync(slotsUrl);
                    if (slotsResponse.IsSuccessStatusCode)
                    {
                        var slotsContent = await slotsResponse.Content.ReadAsStringAsync();
                        using var slotsDoc = JsonDocument.Parse(slotsContent);
                        if (slotsDoc.RootElement.ValueKind == JsonValueKind.Array)
                        {
                            int active = 0;
                            int idle = 0;

                            foreach (var slotElement in slotsDoc.RootElement.EnumerateArray())
                            {
                                bool isProcessing = slotElement.TryGetProperty("is_processing", out var ipProp) && ipProp.GetBoolean();
                                if (isProcessing)
                                    active++;
                                else
                                    idle++;

                                int slotId = slotElement.TryGetProperty("id", out var idProp) ? idProp.GetInt32() : -1;
                                if (slotId >= 0)
                                {
                                    int idTask = slotElement.TryGetProperty("id_task", out var taskProp) ? taskProp.GetInt32() : -1;
                                    int nPrompt = slotElement.TryGetProperty("n_prompt_tokens_processed", out var ppProp) ? ppProp.GetInt32() : 0;
                                    int nDecoded = 0;
                                    if (slotElement.TryGetProperty("next_token", out var ntProp) && ntProp.ValueKind == JsonValueKind.Array && ntProp.GetArrayLength() > 0)
                                    {
                                        var firstToken = ntProp[0];
                                        if (firstToken.TryGetProperty("n_decoded", out var ndProp))
                                        {
                                            nDecoded = ndProp.GetInt32();
                                        }
                                    }

                                    if (idTask >= 0)
                                    {
                                        lock (_lock)
                                        {
                                            if (!_lastSlotTasks.TryGetValue(slotId, out var lastState) || lastState.id_task != idTask)
                                            {
                                                _sessionGeneratedTokens += nDecoded;
                                                _sessionPromptTokens += nPrompt;
                                            }
                                            else
                                            {
                                                int deltaDecoded = nDecoded - lastState.n_decoded;
                                                if (deltaDecoded > 0)
                                                    _sessionGeneratedTokens += deltaDecoded;

                                                int deltaPrompt = nPrompt - lastState.n_prompt;
                                                if (deltaPrompt > 0)
                                                    _sessionPromptTokens += deltaPrompt;
                                            }

                                            _lastSlotTasks[slotId] = (idTask, nDecoded, nPrompt);
                                        }
                                    }
                                }
                            }

                            // If we don't have slots counts from /health, populate them from slots
                            if (!sample.IdleSlots.HasValue)
                            {
                                sample.IdleSlots = idle;
                            }
                            sample.ActiveSlots = active;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[LlamaCppAdapter] Fallback slots telemetry error: {ex.Message}");
                }

                // Throughput speed calculations (token rates)
                var now = DateTime.UtcNow;
                lock (_lock)
                {
                    if (_lastMetricsTime.HasValue)
                    {
                        var elapsedSeconds = (now - _lastMetricsTime.Value).TotalSeconds;
                        if (elapsedSeconds > 0)
                        {
                            if (metricsSupported)
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
                            else
                            {
                                sample.PromptTokensPerSecond = (_sessionPromptTokens - _lastSessionPromptTokens) / elapsedSeconds;
                                sample.GenerationTokensPerSecond = (_sessionGeneratedTokens - _lastSessionGeneratedTokens) / elapsedSeconds;
                            }
                        }
                    }

                    if (metricsSupported)
                    {
                        _lastPromptTokens = metrics.PromptTokensTotal;
                        _lastPredictedTokens = metrics.TokensPredictedTotal;
                    }
                    _lastSessionPromptTokens = _sessionPromptTokens;
                    _lastSessionGeneratedTokens = _sessionGeneratedTokens;
                    _lastMetricsTime = now;
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
