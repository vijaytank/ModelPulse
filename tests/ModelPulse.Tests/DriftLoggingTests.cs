using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using ModelPulse.Core.Adapters.Ollama;
using Xunit;

namespace ModelPulse.Tests
{
    /// <summary>
    /// Validates that adapters correctly detect and log unknown/newly added
    /// runtime API fields rather than silently discarding them (FR-08).
    /// </summary>
    public class DriftLoggingTests
    {
        [Fact]
        public async Task OllamaAdapter_UnknownFieldInResponse_IsLoggedAsDriftEntry()
        {
            // Arrange: Ollama response includes a field unknown to this adapter
            var psJson = @"{
                ""models"": [{
                    ""name"": ""llama3:latest"",
                    ""model"": ""llama3:latest"",
                    ""size"": 4000000000,
                    ""digest"": ""sha256:xyz"",
                    ""details"": {
                        ""format"": ""gguf"",
                        ""family"": ""llama"",
                        ""families"": [""llama""],
                        ""parameter_size"": ""8B"",
                        ""quantization_level"": ""Q4_K_M""
                    },
                    ""size_vram"": 3800000000,
                    ""new_field_added_in_future_ollama_version"": ""some_value""
                }]
            }";

            var handler = new MockHttpHandler(HttpStatusCode.OK, psJson, "application/json");
            var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:11434") };
            var adapter = new OllamaAdapter(httpClient);

            // Act
            _ = await adapter.GetActiveModelsAsync();
            var unknownFields = adapter.GetUnknownFields();

            // Assert: the unknown field must be captured, not dropped
            unknownFields.Should().ContainSingle(
                f => f.FieldName == "new_field_added_in_future_ollama_version",
                "unknown fields must be logged for drift tracking per FR-08");
        }

        [Fact]
        public async Task OllamaAdapter_KnownField_DoesNotProduceDriftEntry()
        {
            // Arrange: all fields are recognized
            var psJson = @"{
                ""models"": [{
                    ""name"": ""llama3:latest"",
                    ""model"": ""llama3:latest"",
                    ""size"": 4000000000,
                    ""digest"": ""sha256:xyz"",
                    ""details"": {
                        ""format"": ""gguf"",
                        ""family"": ""llama"",
                        ""families"": [""llama""],
                        ""parameter_size"": ""8B"",
                        ""quantization_level"": ""Q4_K_M""
                    },
                    ""size_vram"": 3800000000
                }]
            }";

            var handler = new MockHttpHandler(HttpStatusCode.OK, psJson, "application/json");
            var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:11434") };
            var adapter = new OllamaAdapter(httpClient);

            // Act
            _ = await adapter.GetActiveModelsAsync();
            var unknownFields = adapter.GetUnknownFields();

            // Assert: no spurious drift entries for known fields
            unknownFields.Should().BeEmpty("no unknown fields were present in this response");
        }

        [Fact]
        public async Task OllamaAdapter_UnknownField_CapturesSampleValue()
        {
            // Arrange
            var psJson = @"{
                ""models"": [{
                    ""name"": ""phi3:mini"",
                    ""model"": ""phi3:mini"",
                    ""size"": 2000000000,
                    ""digest"": ""sha256:abc"",
                    ""details"": { ""format"": ""gguf"", ""family"": ""phi"", ""families"": [""phi""], ""parameter_size"": ""3.8B"", ""quantization_level"": ""Q4_0"" },
                    ""size_vram"": 1900000000,
                    ""experimental_kv_cache_type"": ""q8_0""
                }]
            }";

            var handler = new MockHttpHandler(HttpStatusCode.OK, psJson, "application/json");
            var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:11434") };
            var adapter = new OllamaAdapter(httpClient);

            // Act
            _ = await adapter.GetActiveModelsAsync();
            var unknownFields = adapter.GetUnknownFields();

            // Assert: sample value was captured for debugging
            unknownFields.Should().ContainSingle();
            var entry = unknownFields[0];
            entry.FieldName.Should().Be("experimental_kv_cache_type");
            entry.SampleValue.Should().Be("q8_0");
            entry.FirstSeenAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        }
    }
}
