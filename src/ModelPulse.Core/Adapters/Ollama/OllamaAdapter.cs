using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.IO;
using System.Text.RegularExpressions;
using ModelPulse.Core.Adapters.Ollama;
using ModelPulse.Core.Models;

namespace ModelPulse.Core.Adapters.Ollama
{
    /// <summary>
    /// Adapter for the Ollama local runtime.
    /// Queries /api/ps for running models.
    /// Implements null-safe parsing throughout — missing optional fields degrade
    /// gracefully rather than throwing or returning misleading values.
    /// Captures unknown fields for drift tracking (FR-08).
    /// </summary>
    public class OllamaAdapter : IRuntimeAdapter
    {
        public string RuntimeName => "ollama";

        private readonly HttpClient _httpClient;
        private readonly List<UnknownFieldEntry> _unknownFields = new();
        private readonly object _lock = new();

        // Known fields in OllamaRunningModel (top-level, not under "details")
        private static readonly HashSet<string> KnownTopLevelFields = new(StringComparer.OrdinalIgnoreCase)
        {
            "name", "model", "size", "digest", "details",
            "expires_at", "size_vram", "processor", "context_length"
        };

        // Known fields inside OllamaModelDetails
        private static readonly HashSet<string> KnownDetailFields = new(StringComparer.OrdinalIgnoreCase)
        {
            "parent_model", "format", "family", "families",
            "parameter_size", "quantization_level", "embedding_length"
        };

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly string _logPath;
        private long _lastLogPosition = 0;
        private double? _lastPromptTps;
        private double? _lastGenTps;
        private static readonly Regex TpsNumberRegex = new(@"([\d\.]+)\s+tokens per second", RegexOptions.Compiled);

        public OllamaAdapter(HttpClient httpClient, string? logPath = null)
        {
            _httpClient = httpClient;
            if (logPath != null)
            {
                _logPath = logPath;
            }
            else
            {
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                _logPath = Path.Combine(localAppData, "Ollama", "server.log");
            }
        }

        /// <inheritdoc/>
        public async Task<bool> IsAvailableAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync("/api/ps");
                return response.IsSuccessStatusCode;
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
                    var response = await _httpClient.GetAsync("/api/version");
                    if (response.IsSuccessStatusCode)
                    {
                        var content = await response.Content.ReadAsStringAsync();
                        var doc = JsonDocument.Parse(content);
                        if (doc.RootElement.TryGetProperty("version", out var versionProp))
                        {
                            version = versionProp.GetString() ?? "Unknown";
                        }
                    }
                }
                catch { }
            }

            return new RuntimeSummary
            {
                RuntimeName = RuntimeName,
                RuntimeVersion = version,
                IsAvailable = available,
                Endpoint = _httpClient.BaseAddress?.ToString() ?? "http://127.0.0.1:11434"
            };
        }

        /// <inheritdoc/>
        public async Task<IReadOnlyList<ActiveModelInfo>> GetActiveModelsAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync("/api/ps");
                if (!response.IsSuccessStatusCode)
                    return Array.Empty<ActiveModelInfo>();

                var content = await response.Content.ReadAsStringAsync();
                return ParseActiveModels(content);
            }
            catch
            {
                return Array.Empty<ActiveModelInfo>();
            }
        }

        /// <inheritdoc/>
        public async Task<PerformanceSample> GetPerformanceSampleAsync()
        {
            var sample = new PerformanceSample { RuntimeName = RuntimeName };

            try
            {
                if (File.Exists(_logPath))
                {
                    var fileInfo = new FileInfo(_logPath);
                    lock (_lock)
                    {
                        // Handle log rotation or first run
                        if (_lastLogPosition == 0 || fileInfo.Length < _lastLogPosition)
                        {
                            _lastLogPosition = Math.Max(0, fileInfo.Length - 4096); // start near the end (last 4KB) to avoid reading huge historic logs
                        }

                        if (fileInfo.Length > _lastLogPosition)
                        {
                            using var stream = new FileStream(_logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                            stream.Position = _lastLogPosition;
                            using var reader = new StreamReader(stream);

                            string? line;
                            while ((line = reader.ReadLine()) != null)
                            {
                                if (line.Contains("prompt eval time ="))
                                {
                                    var match = TpsNumberRegex.Match(line);
                                    if (match.Success && double.TryParse(match.Groups[1].Value, out var val))
                                    {
                                        if (val < 9999.0)
                                        {
                                            _lastPromptTps = val;
                                        }
                                    }
                                }
                                else if (line.Contains("eval time ="))
                                {
                                    var match = TpsNumberRegex.Match(line);
                                    if (match.Success && double.TryParse(match.Groups[1].Value, out var val))
                                    {
                                        if (val < 9999.0)
                                        {
                                            _lastGenTps = val;
                                        }
                                    }
                                }
                            }
                            _lastLogPosition = stream.Position;
                        }

                        sample.PromptTokensPerSecond = _lastPromptTps;
                        sample.GenerationTokensPerSecond = _lastGenTps;
                    }
                }
            }
            catch
            {
                // Degrade gracefully
            }

            return await Task.FromResult(sample);
        }

        /// <summary>
        /// Derives a PerformanceSample from raw Ollama response token counters.
        /// Called by the CollectorCore when a chat/generate response is captured.
        /// </summary>
        public PerformanceSample DerivePerformanceSample(
            long promptEvalCount,
            long promptEvalDurationNs,
            long evalCount,
            long evalDurationNs)
        {
            double? promptTps = promptEvalDurationNs > 0
                ? promptEvalCount / (promptEvalDurationNs / 1e9)
                : null;

            double? genTps = evalDurationNs > 0
                ? evalCount / (evalDurationNs / 1e9)
                : null;

            return new PerformanceSample
            {
                RuntimeName = RuntimeName,
                PromptTokensPerSecond = promptTps,
                GenerationTokensPerSecond = genTps
            };
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

        private List<ActiveModelInfo> ParseActiveModels(string json)
        {
            var results = new List<ActiveModelInfo>();
            if (string.IsNullOrWhiteSpace(json)) return results;

            try
            {
                var doc = JsonNode.Parse(json);
                var modelsArray = doc?["models"]?.AsArray();
                if (modelsArray == null) return results;

                foreach (var modelNode in modelsArray)
                {
                    if (modelNode is not JsonObject modelObj) continue;

                    // --- Drift detection: scan top-level fields ---
                    ScanForUnknownFields(modelObj, KnownTopLevelFields, "models[]");

                    // --- Parse details sub-object ---
                    var detailsObj = modelObj["details"]?.AsObject();
                    if (detailsObj != null)
                        ScanForUnknownFields(detailsObj, KnownDetailFields, "models[].details");

                    // --- Map to ActiveModelInfo ---
                    var info = new ActiveModelInfo
                    {
                        RuntimeName = RuntimeName,
                        ModelName = modelObj["name"]?.GetValue<string>() ?? string.Empty,
                        SizeBytes = modelObj["size"]?.GetValue<long>() ?? 0,
                        SizeVramBytes = GetNullableLong(modelObj, "size_vram"),
                        ExpiresAt = GetNullableDateTime(modelObj, "expires_at"),
                        ContextLength = GetNullableLong(modelObj, "context_length"),
                        QuantizationLevel = detailsObj?["quantization_level"]?.GetValue<string>(),
                        ParameterSize = detailsObj?["parameter_size"]?.GetValue<string>(),
                        Family = detailsObj?["family"]?.GetValue<string>()
                    };

                    results.Add(info);
                }
            }
            catch (JsonException ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OllamaAdapter/Parse] {ex.Message}");
            }

            return results;
        }

        private void ScanForUnknownFields(JsonObject obj, HashSet<string> knownFields, string context)
        {
            var now = DateTime.UtcNow;
            lock (_lock)
            {
                foreach (var kvp in obj)
                {
                    if (knownFields.Contains(kvp.Key)) continue;

                    // Check if we've already recorded this field
                    var existing = _unknownFields.Find(f => f.FieldName == kvp.Key);
                    if (existing != null)
                    {
                        existing.LastSeenAt = now;
                    }
                    else
                    {
                        _unknownFields.Add(new UnknownFieldEntry
                        {
                            RuntimeName = RuntimeName,
                            FieldName = kvp.Key,
                            FirstSeenAt = now,
                            LastSeenAt = now,
                            SampleValue = kvp.Value?.ToJsonString()?.Trim('"')
                        });
                    }
                }
            }
        }

        private static long? GetNullableLong(JsonNode? node, string key)
        {
            try { return node?[key]?.GetValue<long>(); } catch { return null; }
        }

        private static DateTime? GetNullableDateTime(JsonNode? node, string key)
        {
            try
            {
                var str = node?[key]?.GetValue<string>();
                if (string.IsNullOrEmpty(str)) return null;
                return DateTime.Parse(str, null, System.Globalization.DateTimeStyles.RoundtripKind);
            }
            catch { return null; }
        }
    }
}
