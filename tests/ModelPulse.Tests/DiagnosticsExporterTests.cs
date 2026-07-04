using System;
using System.Collections.Generic;
using System.Text.Json;
using Xunit;
using FluentAssertions;
using ModelPulse.Core.Models;
using ModelPulse.Core.Services.Diagnostics;

namespace ModelPulse.Tests
{
    public class DiagnosticsExporterTests
    {
        [Fact]
        public void Export_ReturnsValidJson_WithSnapshotAndSettings()
        {
            // Arrange
            var settings = new ModelPulseSettings
            {
                PollingMode = "custom",
                PollingIntervals = new PollingIntervals { IdleMs = 5000 }
            };

            var snapshot = new CollectorSnapshot
            {
                Timestamp = DateTime.UtcNow,
                System = new SystemTelemetryState { CpuPercent = 15 },
                Runtimes = new List<RuntimeSummary>
                {
                    new() { RuntimeName = "ollama", RuntimeVersion = "0.1.48", IsAvailable = true }
                },
                UnknownFields = new List<UnknownFieldEntry>
                {
                    new() { RuntimeName = "ollama", FieldName = "experimental_field", SampleValue = "true" }
                }
            };

            // Act
            var jsonString = DiagnosticsExporter.Export(snapshot, settings);

            // Assert
            jsonString.Should().NotBeNullOrEmpty();

            // Check deserialization correctness
            var jsonDoc = JsonDocument.Parse(jsonString);
            var root = jsonDoc.RootElement;

            root.GetProperty("exported_at").GetDateTime().Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
            root.GetProperty("polling_mode").GetString().Should().Be("custom");
            root.GetProperty("idle_ms").GetInt32().Should().Be(5000);

            var runtimes = root.GetProperty("runtimes");
            runtimes[0].GetProperty("name").GetString().Should().Be("ollama");
            runtimes[0].GetProperty("version").GetString().Should().Be("0.1.48");

            var unknownFields = root.GetProperty("unknown_fields");
            unknownFields[0].GetProperty("field_name").GetString().Should().Be("experimental_field");
        }

        [Fact]
        public void Export_ScrubsNonWhitelistedUnknownFields()
        {
            // Arrange
            var settings = new ModelPulseSettings();
            var snapshot = new CollectorSnapshot
            {
                Timestamp = DateTime.UtcNow,
                UnknownFields = new List<UnknownFieldEntry>
                {
                    // "status" is whitelisted -> should NOT be scrubbed
                    new() { RuntimeName = "ollama", FieldName = "status", SampleValue = "running" },
                    // "secret_path" is NOT whitelisted -> should be scrubbed
                    new() { RuntimeName = "ollama", FieldName = "secret_path", SampleValue = "C:\\Users\\user\\secret.json" }
                }
            };

            // Act
            var jsonString = DiagnosticsExporter.Export(snapshot, settings);

            // Assert
            jsonString.Should().NotBeNullOrEmpty();
            var jsonDoc = JsonDocument.Parse(jsonString);
            var root = jsonDoc.RootElement;
            var unknownFields = root.GetProperty("unknown_fields");

            unknownFields.GetArrayLength().Should().Be(2);

            // status check
            var f1 = unknownFields[0];
            f1.GetProperty("field_name").GetString().Should().Be("status");
            f1.GetProperty("sample_value").GetString().Should().Be("running");

            // secret_path check
            var f2 = unknownFields[1];
            f2.GetProperty("field_name").GetString().Should().Be("secret_path");
            f2.GetProperty("sample_value").GetString().Should().Be("[SCRUBBED]");
        }
    }
}
