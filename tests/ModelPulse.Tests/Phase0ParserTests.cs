using System;
using Xunit;
using FluentAssertions;
using ModelPulse.Core.Adapters;
using ModelPulse.Core.Adapters.Ollama;
using ModelPulse.Core.Adapters.LlamaCpp;

namespace ModelPulse.Tests
{
    public class Phase0ParserTests
    {
        [Fact]
        public void ParseOllamaPsResponse_ValidJson_ReturnsParsedModel()
        {
            // Arrange
            var json = @"{
                ""models"": [
                    {
                        ""name"": ""gemma2:27b"",
                        ""model"": ""gemma2:27b"",
                        ""size"": 16223467657,
                        ""digest"": ""sha256:12345"",
                        ""details"": {
                            ""parent_model"": """",
                            ""format"": ""gguf"",
                            ""family"": ""gemma2"",
                            ""families"": [""gemma2""],
                            ""parameter_size"": ""27B"",
                            ""quantization_level"": ""Q4_K_M""
                        },
                        ""expires_at"": ""2026-07-02T15:20:00Z"",
                        ""size_vram"": 16223467657
                    }
                ]
            }";

            // Act
            var response = OllamaParser.ParsePsResponse(json);

            // Assert
            response.Should().NotBeNull();
            response.Models.Should().HaveCount(1);
            var model = response.Models[0];
            model.Name.Should().Be("gemma2:27b");
            model.Size.Should().Be(16223467657);
            model.SizeVram.Should().Be(16223467657);
            model.ExpiresAt!.Value.ToUniversalTime().Should().BeCloseTo(DateTime.Parse("2026-07-02T15:20:00Z").ToUniversalTime(), TimeSpan.FromSeconds(1));
            model.Details.Family.Should().Be("gemma2");
        }

        [Fact]
        public void ParseLlamaCppHealthResponse_ValidJson_ReturnsParsedHealth()
        {
            // Arrange
            var json = @"{
                ""status"": ""ok"",
                ""slots_idle"": 4,
                ""slots_processing"": 1
            }";

            // Act
            var response = LlamaCppParser.ParseHealthResponse(json);

            // Assert
            response.Should().NotBeNull();
            response.Status.Should().Be("ok");
            response.SlotsIdle.Should().Be(4);
            response.SlotsProcessing.Should().Be(1);
        }

        [Fact]
        public void ParseLlamaCppMetrics_PrometheusFormat_ReturnsParsedMetrics()
        {
            // Arrange
            var rawMetrics = @"
# HELP llamacpp:prompt_tokens_total Number of prompt tokens processed.
# TYPE llamacpp:prompt_tokens_total counter
llamacpp:prompt_tokens_total 4827
# HELP llamacpp:tokens_predicted_total Number of tokens predicted.
# TYPE llamacpp:tokens_predicted_total counter
llamacpp:tokens_predicted_total 9272
# HELP llamacpp:slots_active Number of active slots.
# TYPE llamacpp:slots_active gauge
llamacpp:slots_active 2
";

            // Act
            var metrics = LlamaCppParser.ParseMetrics(rawMetrics);

            // Assert
            metrics.Should().NotBeNull();
            metrics.PromptTokensTotal.Should().Be(4827);
            metrics.TokensPredictedTotal.Should().Be(9272);
            metrics.ActiveSlots.Should().Be(2);
        }
    }
}
