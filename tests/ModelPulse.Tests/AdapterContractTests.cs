using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using ModelPulse.Core.Adapters.Ollama;
using ModelPulse.Core.Adapters.LlamaCpp;
using ModelPulse.Core.Models;
using Xunit;

namespace ModelPulse.Tests
{
    public class AdapterContractTests
    {
        // ─── Ollama Adapter ────────────────────────────────────────────

        [Fact]
        public async Task OllamaAdapter_GetActiveModels_ParsesRunningModels()
        {
            // Arrange: mock Ollama /api/ps response with one running model
            var psJson = @"{
                ""models"": [{
                    ""name"": ""gemma2:27b"",
                    ""model"": ""gemma2:27b"",
                    ""size"": 16223467657,
                    ""digest"": ""sha256:abc"",
                    ""details"": {
                        ""parent_model"": """",
                        ""format"": ""gguf"",
                        ""family"": ""gemma2"",
                        ""families"": [""gemma2""],
                        ""parameter_size"": ""27B"",
                        ""quantization_level"": ""Q4_K_M""
                    },
                    ""expires_at"": ""2026-07-02T15:20:00Z"",
                    ""size_vram"": 14000000000,
                    ""context_length"": 131072
                }]
            }";

            var handler = new MockHttpHandler(HttpStatusCode.OK, psJson, "application/json");
            var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:11434") };
            var adapter = new OllamaAdapter(httpClient);

            // Act
            var models = await adapter.GetActiveModelsAsync();

            // Assert
            models.Should().HaveCount(1);
            var model = models[0];
            model.ModelName.Should().Be("gemma2:27b");
            model.SizeVramBytes.Should().Be(14000000000);
            model.ContextLength.Should().Be(131072);
            model.Family.Should().Be("gemma2");
            model.QuantizationLevel.Should().Be("Q4_K_M");
            model.ExpiresAt.Should().NotBeNull();
        }

        [Fact]
        public async Task OllamaAdapter_IsAvailable_ReturnsFalseOnHttpError()
        {
            // Arrange
            var handler = new MockHttpHandler(HttpStatusCode.ServiceUnavailable, "", "text/plain");
            var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:11434") };
            var adapter = new OllamaAdapter(httpClient);

            // Act
            var available = await adapter.IsAvailableAsync();

            // Assert
            available.Should().BeFalse();
        }

        [Fact]
        public async Task OllamaAdapter_GetRuntimeSummary_QueriesVersionApi()
        {
            // Arrange
            var versionJson = @"{ ""version"": ""0.31.1"" }";
            
            var handler = new RoutedMockHttpHandler(new Dictionary<string, (HttpStatusCode, string, string)>
            {
                { "/api/ps", (HttpStatusCode.OK, @"{ ""models"": [] }", "application/json") },
                { "/api/version", (HttpStatusCode.OK, versionJson, "application/json") }
            });

            var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:11434") };
            var adapter = new OllamaAdapter(httpClient);

            // Act
            var summary = await adapter.GetRuntimeSummaryAsync();

            // Assert
            summary.Should().NotBeNull();
            summary.RuntimeVersion.Should().Be("0.31.1");
            summary.IsAvailable.Should().BeTrue();
        }

        [Fact]
        public async Task OllamaAdapter_GetActiveModels_HandlesEmptyModelsList()
        {
            // Arrange: Ollama is up but no models are loaded
            var psJson = @"{ ""models"": [] }";
            var handler = new MockHttpHandler(HttpStatusCode.OK, psJson, "application/json");
            var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:11434") };
            var adapter = new OllamaAdapter(httpClient);

            // Act
            var models = await adapter.GetActiveModelsAsync();

            // Assert — must return empty list, never null
            models.Should().NotBeNull();
            models.Should().BeEmpty();
        }

        [Fact]
        public async Task OllamaAdapter_GetPerformanceSample_DerivesTokensPerSecond()
        {
            // Arrange: A performance sample with prompt and eval counters
            // OllamaAdapter derives t/s from those counters internally
            var handler = new MockHttpHandler(HttpStatusCode.OK, @"{ ""models"": [] }", "application/json");
            var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:11434") };
            var adapter = new OllamaAdapter(httpClient);

            // Simulate feeding performance data (eval_count + eval_duration)
            var promptCount = 50;
            var promptDurationNs = 1_000_000_000L; // 1 second
            var evalCount = 200;
            var evalDurationNs = 4_000_000_000L;   // 4 seconds

            var sample = adapter.DerivePerformanceSample(
                promptEvalCount: promptCount,
                promptEvalDurationNs: promptDurationNs,
                evalCount: evalCount,
                evalDurationNs: evalDurationNs);

            // Assert
            sample.PromptTokensPerSecond.Should().BeApproximately(50.0, 0.01);
            sample.GenerationTokensPerSecond.Should().BeApproximately(50.0, 0.01);
        }

        [Fact]
        public async Task OllamaAdapter_GetPerformanceSample_ParsesServerLog()
        {
            // Arrange: Create a temporary file to mock Ollama server.log
            var tempFile = Path.GetTempFileName();
            try
            {
                var initialLogLines = @"
slot print_timing: id  0 | task 0 | prompt eval time =     100.00 ms /   100 tokens (    1.00 ms per token,  1000.00 tokens per second)
slot print_timing: id  0 | task 0 |        eval time =     500.00 ms /    25 tokens (   20.00 ms per token,    50.00 tokens per second)
";
                await File.WriteAllTextAsync(tempFile, initialLogLines);

                var httpClient = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:11434") };
                // Initialize adapter pointing to our mock temp file
                var adapter = new OllamaAdapter(httpClient, tempFile);

                // Act
                var sample = await adapter.GetPerformanceSampleAsync();

                // Assert
                sample.Should().NotBeNull();
                sample.PromptTokensPerSecond.Should().Be(1000.0);
                sample.GenerationTokensPerSecond.Should().Be(50.0);

                // Append a new generation result to the log
                var newLogLines = @"
slot print_timing: id  0 | task 1 | prompt eval time =     200.00 ms /   100 tokens (    2.00 ms per token,   500.00 tokens per second)
slot print_timing: id  0 | task 1 |        eval time =    1000.00 ms /    80 tokens (   12.50 ms per token,    80.00 tokens per second)
";
                await File.AppendAllTextAsync(tempFile, newLogLines);

                // Act again
                var sample2 = await adapter.GetPerformanceSampleAsync();

                // Assert second parse updates correctly
                sample2.PromptTokensPerSecond.Should().Be(500.0);
                sample2.GenerationTokensPerSecond.Should().Be(80.0);
            }
            finally
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
            }
        }

        // ─── llama.cpp Adapter ─────────────────────────────────────────

        [Fact]
        public async Task LlamaCppAdapter_IsAvailable_ReturnsTrueOnOkStatus()
        {
            // Arrange
            var json = @"{ ""status"": ""ok"", ""slots_idle"": 4, ""slots_processing"": 0 }";
            var handler = new MockHttpHandler(HttpStatusCode.OK, json, "application/json");
            var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:8080") };
            var adapter = new LlamaCppAdapter(httpClient);

            // Act
            var available = await adapter.IsAvailableAsync();

            // Assert
            available.Should().BeTrue();
        }

        [Fact]
        public async Task LlamaCppAdapter_IsAvailable_ReturnsFalseOnConnectionRefused()
        {
            // Arrange: simulate network error (no mock handler — use invalid address)
            var httpClient = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:19999") };
            httpClient.Timeout = TimeSpan.FromMilliseconds(500);
            var adapter = new LlamaCppAdapter(httpClient);

            // Act
            var available = await adapter.IsAvailableAsync();

            // Assert
            available.Should().BeFalse();
        }

        [Fact]
        public async Task LlamaCppAdapter_GetPerformanceSample_ParsesPrometheusMetrics()
        {
            // Arrange
            var healthJson = @"{ ""status"": ""ok"", ""slots_idle"": 3, ""slots_processing"": 1 }";
            var metricsText = @"
llamacpp:prompt_tokens_total 4827
llamacpp:tokens_predicted_total 9272
llamacpp:slots_active 2
";
            var handler = new RoutedMockHttpHandler(new Dictionary<string, (HttpStatusCode, string, string)>
            {
                { "/health", (HttpStatusCode.OK, healthJson, "application/json") },
                { "/metrics", (HttpStatusCode.OK, metricsText, "text/plain") }
            });

            var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:8080") };
            var adapter = new LlamaCppAdapter(httpClient);

            // Act
            var sample = await adapter.GetPerformanceSampleAsync();

            // Assert
            sample.Should().NotBeNull();
            sample.ActiveSlots.Should().Be(2);
            sample.IdleSlots.Should().Be(3);
        }

        [Fact]
        public async Task LlamaCppAdapter_GetPerformanceSample_CalculatesThroughputOverTime()
        {
            // Arrange
            var healthJson = @"{ ""status"": ""ok"", ""slots_idle"": 3, ""slots_processing"": 1 }";
            
            var metricsCallCount = 0;
            var handler = new DynamicMockHttpHandler(req =>
            {
                var path = req.RequestUri?.AbsolutePath ?? "/";
                if (path == "/health")
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(healthJson, Encoding.UTF8, "application/json")
                    };
                }
                else if (path == "/metrics")
                {
                    metricsCallCount++;
                    // Return first metrics state on first call, and second metrics state (increased values) on second call
                    var metricsText = metricsCallCount == 1
                        ? "llamacpp:prompt_tokens_total 1000\nllamacpp:tokens_predicted_total 2000\nllamacpp:slots_active 1\n"
                        : "llamacpp:prompt_tokens_total 1050\nllamacpp:tokens_predicted_total 2100\nllamacpp:slots_active 1\n";
                    
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(metricsText, Encoding.UTF8, "text/plain")
                    };
                }
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });

            var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:8080") };
            var adapter = new LlamaCppAdapter(httpClient);

            // Act: Call 1
            var sample1 = await adapter.GetPerformanceSampleAsync();

            // Wait a short duration
            await Task.Delay(100);

            // Act: Call 2
            var sample2 = await adapter.GetPerformanceSampleAsync();

            // Assert
            sample1.PromptTokensPerSecond.Should().BeNull();
            sample1.GenerationTokensPerSecond.Should().BeNull();

            sample2.PromptTokensPerSecond.Should().NotBeNull();
            sample2.GenerationTokensPerSecond.Should().NotBeNull();

            sample2.PromptTokensPerSecond!.Value.Should().BeGreaterThan(0);
            sample2.GenerationTokensPerSecond!.Value.Should().BeGreaterThan(0);
        }

        [Fact]
        public async Task LlamaCppAdapter_GetRuntimeSummary_ParsesBuildInfoFromProps()
        {
            // Arrange
            var healthJson = @"{ ""status"": ""ok"" }";
            var propsJson = @"{ ""build_info"": ""b9639-ef8268fee"" }";
            var handler = new RoutedMockHttpHandler(new Dictionary<string, (HttpStatusCode, string, string)>
            {
                { "/health", (HttpStatusCode.OK, healthJson, "application/json") },
                { "/props", (HttpStatusCode.OK, propsJson, "application/json") }
            });
            var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:8080") };
            var adapter = new LlamaCppAdapter(httpClient);

            // Act
            var summary = await adapter.GetRuntimeSummaryAsync();

            // Assert
            summary.Should().NotBeNull();
            summary.RuntimeVersion.Should().Be("b9639-ef8268fee");
        }

        [Fact]
        public async Task LlamaCppAdapter_GetActiveModels_ParsesLoadedModel()
        {
            // Arrange
            var modelsJson = @"{ ""data"": [ { ""id"": ""gemma-4-e4b-it-q4-k-m"", ""status"": { ""value"": ""loaded"" }, ""meta"": { ""size"": 4961343656, ""n_ctx"": 131072 } } ] }";
            var handler = new RoutedMockHttpHandler(new Dictionary<string, (HttpStatusCode, string, string)>
            {
                { "/v1/models", (HttpStatusCode.OK, modelsJson, "application/json") }
            });
            var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:8080") };
            var adapter = new LlamaCppAdapter(httpClient);

            // Act
            var activeModels = await adapter.GetActiveModelsAsync();

            // Assert
            activeModels.Should().NotBeNull();
            activeModels.Should().ContainSingle();
            var model = activeModels[0];
            model.ModelName.Should().Be("gemma-4-e4b-it-q4-k-m");
            model.SizeVramBytes.Should().Be(4961343656);
            model.ContextLength.Should().Be(131072);
        }

        [Fact]
        public async Task LlamaCppAdapter_GetPerformanceSample_FallsBackToSlotsStats()
        {
            // Arrange
            var healthJson = @"{ ""status"": ""ok"" }";
            var modelsJson = @"{ ""data"": [ { ""id"": ""gemma-4-e4b-it-q4-k-m"", ""status"": { ""value"": ""loaded"" } } ] }";
            var slotsJson1 = @"[
                { ""id"": 0, ""is_processing"": false },
                { ""id"": 1, ""is_processing"": true, ""id_task"": 10, ""n_prompt_tokens_processed"": 100, ""next_token"": [ { ""n_decoded"": 10 } ] }
            ]";
            var slotsJson2 = @"[
                { ""id"": 0, ""is_processing"": false },
                { ""id"": 1, ""is_processing"": true, ""id_task"": 10, ""n_prompt_tokens_processed"": 100, ""next_token"": [ { ""n_decoded"": 35 } ] }
            ]";

            int callCount = 0;
            var handler = new DynamicMockHttpHandler(req =>
            {
                var path = req.RequestUri?.PathAndQuery ?? "/";
                if (path.StartsWith("/health"))
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(healthJson, Encoding.UTF8, "application/json") };
                if (path.StartsWith("/v1/models"))
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(modelsJson, Encoding.UTF8, "application/json") };
                if (path.StartsWith("/metrics"))
                    return new HttpResponseMessage(HttpStatusCode.NotFound); // Metrics not supported
                if (path.StartsWith("/slots"))
                {
                    callCount++;
                    var json = callCount == 1 ? slotsJson1 : slotsJson2;
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
                }
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });

            var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:8080") };
            var adapter = new LlamaCppAdapter(httpClient);

            // Act
            var sample1 = await adapter.GetPerformanceSampleAsync();
            await Task.Delay(100);
            var sample2 = await adapter.GetPerformanceSampleAsync();

            // Assert
            sample1.ActiveSlots.Should().Be(1);
            sample1.IdleSlots.Should().Be(1);
            sample1.GenerationTokensPerSecond.Should().BeNull();

            sample2.ActiveSlots.Should().Be(1);
            sample2.IdleSlots.Should().Be(1);
            sample2.GenerationTokensPerSecond.Should().NotBeNull();
            sample2.GenerationTokensPerSecond!.Value.Should().BeGreaterThan(0);
        }
    }

    internal class DynamicMockHttpHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public DynamicMockHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_handler(request));
        }
    }

    // ─── Test Helpers ──────────────────────────────────────────────────────

    internal class MockHttpHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _body;
        private readonly string _contentType;

        public MockHttpHandler(HttpStatusCode statusCode, string body, string contentType)
        {
            _statusCode = statusCode;
            _body = body;
            _contentType = contentType;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_body, Encoding.UTF8, _contentType)
            };
            return Task.FromResult(response);
        }
    }

    internal class RoutedMockHttpHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, (HttpStatusCode Status, string Body, string ContentType)> _routes;

        public RoutedMockHttpHandler(Dictionary<string, (HttpStatusCode, string, string)> routes)
            => _routes = routes;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? "/";
            if (_routes.TryGetValue(path, out var route))
            {
                var response = new HttpResponseMessage(route.Status)
                {
                    Content = new StringContent(route.Body, Encoding.UTF8, route.ContentType)
                };
                return Task.FromResult(response);
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
