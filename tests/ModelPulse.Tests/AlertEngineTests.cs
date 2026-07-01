using System;
using System.Collections.Generic;
using System.Reactive.Concurrency;
using Xunit;
using Moq;
using FluentAssertions;
using Microsoft.Reactive.Testing;
using ModelPulse.Core.Models;
using ModelPulse.Core.Services.Config;
using ModelPulse.Core.Services.Alerts;

namespace ModelPulse.Tests
{
    public class AlertEngineTests
    {
        private readonly Mock<IConfigService> _mockConfig;
        private readonly ModelPulseSettings _settings;
        private readonly TestScheduler _testScheduler;

        public AlertEngineTests()
        {
            _mockConfig = new Mock<IConfigService>();
            _testScheduler = new TestScheduler();

            // Advance scheduler initially so it's not at DateTime.MinValue (t=0)
            _testScheduler.AdvanceBy(TimeSpan.FromDays(10).Ticks);

            _settings = new ModelPulseSettings
            {
                Alerts = new AlertsConfig
                {
                    CooldownSeconds = 10, // 10 seconds for fast tests
                    VramWarningThresholdPercent = 90.0
                }
            };
            _mockConfig.Setup(c => c.CurrentSettings).Returns(_settings);
        }

        [Fact]
        public void AlertEngine_VramPressure_FiresAlertCorrectly()
        {
            // Arrange
            using var alertEngine = new AlertEngine(_mockConfig.Object, _testScheduler);
            var alertsFired = new List<Alert>();
            using var sub = alertEngine.Alerts.Subscribe(alertsFired.Add);

            var snapshot = new CollectorSnapshot
            {
                Timestamp = _testScheduler.Now.UtcDateTime,
                System = new SystemTelemetryState
                {
                    Gpu = new GpuTelemetryState
                    {
                        SourcePath = "NVML",
                        VramUsedMb = 9200,
                        VramTotalMb = 10000 // 92% VRAM utilization (exceeds 90% threshold)
                    }
                }
            };

            // Act
            alertEngine.EvaluateSnapshot(snapshot);

            // Assert
            alertsFired.Should().ContainSingle();
            var alert = alertsFired[0];
            alert.AlertType.Should().Be("memory_pressure");
            alert.Severity.Should().Be(AlertSeverity.Warning);
            alert.Message.Should().Contain("VRAM");
        }

        [Fact]
        public void AlertEngine_VramPressure_RespectsCooldown()
        {
            // Arrange
            using var alertEngine = new AlertEngine(_mockConfig.Object, _testScheduler);
            var alertsFired = new List<Alert>();
            using var sub = alertEngine.Alerts.Subscribe(alertsFired.Add);

            var snap1 = new CollectorSnapshot
            {
                Timestamp = _testScheduler.Now.UtcDateTime,
                System = new SystemTelemetryState
                {
                    Gpu = new GpuTelemetryState { SourcePath = "NVML", VramUsedMb = 9200, VramTotalMb = 10000 }
                }
            };

            var snap2 = new CollectorSnapshot
            {
                Timestamp = _testScheduler.Now.UtcDateTime.AddSeconds(2), // 2 seconds later (within 10s cooldown)
                System = new SystemTelemetryState
                {
                    Gpu = new GpuTelemetryState { SourcePath = "NVML", VramUsedMb = 9500, VramTotalMb = 10000 }
                }
            };

            var snap3 = new CollectorSnapshot
            {
                Timestamp = _testScheduler.Now.UtcDateTime.AddSeconds(12), // 12 seconds later (exceeds 10s cooldown)
                System = new SystemTelemetryState
                {
                    Gpu = new GpuTelemetryState { SourcePath = "NVML", VramUsedMb = 9500, VramTotalMb = 10000 }
                }
            };

            // Act & Assert
            // 1. First trigger
            alertEngine.EvaluateSnapshot(snap1);
            alertsFired.Should().HaveCount(1);

            // 2. Immediate second trigger (should be suppressed by cooldown)
            _testScheduler.AdvanceBy(TimeSpan.FromSeconds(2).Ticks);
            alertEngine.EvaluateSnapshot(snap2);
            alertsFired.Should().HaveCount(1, "alert must be suppressed during cooldown");

            // 3. Trigger after cooldown elapsed
            _testScheduler.AdvanceBy(TimeSpan.FromSeconds(10).Ticks);
            alertEngine.EvaluateSnapshot(snap3);
            alertsFired.Should().HaveCount(2, "alert must fire again after cooldown elapsed");
        }

        [Fact]
        public void AlertEngine_DisconnectedRuntime_FiresOnStateChange()
        {
            // Arrange
            using var alertEngine = new AlertEngine(_mockConfig.Object, _testScheduler);
            var alertsFired = new List<Alert>();
            using var sub = alertEngine.Alerts.Subscribe(alertsFired.Add);

            var snap1 = new CollectorSnapshot
            {
                Timestamp = _testScheduler.Now.UtcDateTime,
                Runtimes = new List<RuntimeSummary>
                {
                    new() { RuntimeName = "ollama", IsAvailable = true }
                }
            };

            var snap2 = new CollectorSnapshot
            {
                Timestamp = _testScheduler.Now.UtcDateTime.AddSeconds(3),
                Runtimes = new List<RuntimeSummary>
                {
                    new() { RuntimeName = "ollama", IsAvailable = false } // Transition to disconnected!
                }
            };

            // Act
            alertEngine.EvaluateSnapshot(snap1);
            alertsFired.Should().BeEmpty(); // Ollama was available, no alert

            _testScheduler.AdvanceBy(TimeSpan.FromSeconds(3).Ticks);
            alertEngine.EvaluateSnapshot(snap2);

            // Assert
            alertsFired.Should().ContainSingle();
            alertsFired[0].AlertType.Should().Be("disconnected");
            alertsFired[0].RuntimeName.Should().Be("ollama");
        }
    }
}
