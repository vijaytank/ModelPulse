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
                        ""quantization_level"": ""Q4_K_M"",
                        ""context_length"": 131072
                    },
                    ""expires_at"": ""2026-07-02T15:20:00Z"",
                    ""size_vram"": 14000000000
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
