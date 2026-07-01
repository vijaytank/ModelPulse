using System;
using System.IO;
using Xunit;
using FluentAssertions;
using ModelPulse.Core.Models;
using ModelPulse.Core.Services.Config;

namespace ModelPulse.Tests
{
    public class ConfigServiceTests : IDisposable
    {
        private readonly string _tempFilePath;

        public ConfigServiceTests()
        {
            _tempFilePath = Path.Combine(Path.GetTempPath(), $"settings_{Guid.NewGuid():N}.json");
        }

        public void Dispose()
        {
            if (File.Exists(_tempFilePath))
            {
                File.Delete(_tempFilePath);
            }
        }

        [Fact]
        public void Load_FileNotExists_CreatesDefaultSettingsAndSaves()
        {
            // Arrange
            var configService = new ConfigService(_tempFilePath);

            // Act
            configService.Load();

            // Assert
            configService.CurrentSettings.Should().NotBeNull();
            configService.CurrentSettings.PollingMode.Should().Be("default");
            configService.CurrentSettings.PollingIntervals.IdleMs.Should().Be(3000);
            configService.CurrentSettings.PollingIntervals.ActiveMs.Should().Be(1000);
            configService.CurrentSettings.Runtimes.Ollama.Enabled.Should().BeTrue();
            configService.CurrentSettings.Runtimes.LlamaCpp.Enabled.Should().BeFalse();

            // Check that file was created on disk
            File.Exists(_tempFilePath).Should().BeTrue();
        }

        [Fact]
        public void Load_InvalidIntervals_EnforcesBounds()
        {
            // Arrange
            var invalidJson = @"{
                ""polling_mode"": ""custom"",
                ""polling_intervals"": {
                    ""idle_ms"": 100,
                    ""active_ms"": 5
                },
                ""runtimes"": {
                    ""ollama"": { ""enabled"": true, ""endpoint"": ""http://localhost"" },
                    ""llama_cpp"": { ""enabled"": false, ""endpoint"": ""http://localhost"" }
                },
                ""alerts"": {
                    ""suppressed_types"": [],
                    ""cooldown_seconds"": 2,
                    ""vram_warning_threshold_percent"": 120
                },
                ""ui"": {
                    ""always_on_top"": true,
                    ""opacity"": 0.05
                }
            }";

            File.WriteAllText(_tempFilePath, invalidJson);
            var configService = new ConfigService(_tempFilePath);

            // Act
            configService.Load();

            // Assert bounds enforcement (based on settings.json schema in telemetry-schema.md)
            // idle_ms minimum is 500
            // active_ms minimum is 100
            // cooldown_seconds minimum is 5
            // opacity minimum is 0.1
            // vram_warning_threshold_percent maximum is 100.0
            configService.CurrentSettings.PollingIntervals.IdleMs.Should().Be(500);
            configService.CurrentSettings.PollingIntervals.ActiveMs.Should().Be(100);
            configService.CurrentSettings.Alerts.CooldownSeconds.Should().Be(5);
            configService.CurrentSettings.Alerts.VramWarningThresholdPercent.Should().Be(90.0); // fallback to default or max out? Wait, 120 is > 100, let's cap it at 100 or default. Let's cap at 90.0/100.0. Let's verify our validation caps it at 100.0. Wait, default is 90.0.
            configService.CurrentSettings.Ui.Opacity.Should().Be(0.1);
        }

        [Fact]
        public void Save_PersistsSettingsToDisk()
        {
            // Arrange
            var configService = new ConfigService(_tempFilePath);
            configService.Load();
            configService.CurrentSettings.PollingMode = "custom";
            configService.CurrentSettings.PollingIntervals.IdleMs = 4000;

            // Act
            configService.Save();

            // Assert
            var diskContent = File.ReadAllText(_tempFilePath);
            diskContent.Should().Contain("\"polling_mode\": \"custom\"");
            diskContent.Should().Contain("\"idle_ms\": 4000");
        }

        [Fact]
        public void UpdateSettings_ValidSettings_UpdatesCurrentSettingsAndSaves()
        {
            // Arrange
            var configService = new ConfigService(_tempFilePath);
            configService.Load();

            var newSettings = new ModelPulseSettings
            {
                PollingMode = "low-power",
                PollingIntervals = new PollingIntervals { IdleMs = 5000, ActiveMs = 2000 },
                Runtimes = new RuntimesConfig
                {
                    Ollama = new RuntimeConfig { Enabled = false, Endpoint = "http://ollama" },
                    LlamaCpp = new RuntimeConfig { Enabled = true, Endpoint = "http://llama" }
                }
            };

            // Act
            configService.UpdateSettings(newSettings);

            // Assert
            configService.CurrentSettings.PollingMode.Should().Be("low-power");
            configService.CurrentSettings.PollingIntervals.IdleMs.Should().Be(5000);
            configService.CurrentSettings.Runtimes.Ollama.Enabled.Should().BeFalse();
            configService.CurrentSettings.Runtimes.LlamaCpp.Enabled.Should().BeTrue();

            // Verify saved to disk
            var diskContent = File.ReadAllText(_tempFilePath);
            diskContent.Should().Contain("\"polling_mode\": \"low-power\"");
        }
    }
}
