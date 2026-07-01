using System;
using Xunit;
using FluentAssertions;
using ModelPulse.Core.Adapters.Ollama;
using ModelPulse.Core.Adapters.LlamaCpp;
using ModelPulse.Core.Services.System;

namespace ModelPulse.Tests
{
    public class TelemetryFallbackTests
    {
        [Fact]
        public void OllamaParser_ResilientToMalformedJson()
        {
            // Arrange: JSON missing expected fields entirely
            var malformedJson = "{ \"unexpected_key\": 1234, \"models\": [ { \"name\": \"test_model\" } ] }";

            // Act
            Action act = () =>
            {
                var res = OllamaParser.ParsePsResponse(malformedJson);
                res.Should().NotBeNull();
                res.Models.Should().ContainSingle();
                res.Models[0].Name.Should().Be("test_model");
            };

            // Assert
            act.Should().NotThrow("Ollama parser must handle malformed JSON and schema drifts gracefully");
        }

        [Fact]
        public void LlamaCppParser_ResilientToMalformedJson()
        {
            // Arrange
            var malformedJson = "{ \"invalid_structure\": true }";

            // Act
            Action act = () =>
            {
                var health = LlamaCppParser.ParseHealthResponse(malformedJson);
                health.Should().NotBeNull();
                health.Status.Should().BeEmpty(); // fallback to default empty
            };

            // Assert
            act.Should().NotThrow("LlamaCpp parser must handle missing or empty fields without exception");
        }

        [Fact]
        public void SystemTelemetryProvider_ProbeGpu_NeverThrows()
        {
            // Arrange
            var provider = new SystemTelemetryProvider();

            // Act
            Action act = () =>
            {
                var state = provider.ProbeGpu();
                state.Should().NotBeNull();
                state.SourcePath.Should().NotBeNullOrEmpty();
            };

            // Assert
            act.Should().NotThrow("GPU Probing must fail gracefully and fall back safely without raising runtime errors");
        }
    }
}
