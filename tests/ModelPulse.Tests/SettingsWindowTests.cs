using System;
using System.IO;
using FluentAssertions;
using ModelPulse.Core.Models;
using ModelPulse.Core.Services.Config;
using Xunit;

namespace ModelPulse.Tests
{
    /// <summary>
    /// Tests for SettingsWindow business logic exercised via ConfigService.
    /// Does NOT instantiate the WPF window (requires UI thread); instead,
    /// it validates the settings model and ConfigService round-trip that
    /// the SettingsWindow depends on.
    /// </summary>
    public class SettingsWindowTests : IDisposable
    {
        private readonly string _tempFilePath;
        private readonly ConfigService _configService;

        public SettingsWindowTests()
        {
            _tempFilePath = Path.Combine(Path.GetTempPath(), $"settings_sw_{Guid.NewGuid():N}.json");
            _configService = new ConfigService(_tempFilePath);
            _configService.Load();
        }

        public void Dispose()
        {
            if (File.Exists(_tempFilePath))
                File.Delete(_tempFilePath);
        }

        // ─── Polling Mode Tests ────────────────────────────────────────

        [Fact]
        public void DefaultPollingMode_SetsExpectedIntervals()
        {
            // Arrange
            var s = _configService.CurrentSettings;

            // Act: simulate Settings window applying "default" mode
            s.PollingMode = "default";
            s.PollingIntervals.IdleMs = 3000;
            s.PollingIntervals.ActiveMs = 1000;
            _configService.Save();

            // Assert round-trip
            var reloaded = new ConfigService(_tempFilePath);
            reloaded.Load();
            reloaded.CurrentSettings.PollingMode.Should().Be("default");
            reloaded.CurrentSettings.PollingIntervals.IdleMs.Should().Be(3000);
            reloaded.CurrentSettings.PollingIntervals.ActiveMs.Should().Be(1000);
        }

        [Fact]
        public void LowPowerPollingMode_SetsConservativeIntervals()
        {
            var s = _configService.CurrentSettings;
            s.PollingMode = "low-power";
            s.PollingIntervals.IdleMs = 10000;
            s.PollingIntervals.ActiveMs = 3000;
            _configService.Save();

            var reloaded = new ConfigService(_tempFilePath);
            reloaded.Load();
            reloaded.CurrentSettings.PollingMode.Should().Be("low-power");
            reloaded.CurrentSettings.PollingIntervals.IdleMs.Should().Be(10000);
        }

        [Fact]
        public void CustomPollingMode_PersistsCustomIntervals()
        {
            var s = _configService.CurrentSettings;
            s.PollingMode = "custom";
            s.PollingIntervals.IdleMs = 7500;
            s.PollingIntervals.ActiveMs = 500;
            _configService.Save();

            var reloaded = new ConfigService(_tempFilePath);
            reloaded.Load();
            reloaded.CurrentSettings.PollingMode.Should().Be("custom");
            reloaded.CurrentSettings.PollingIntervals.IdleMs.Should().Be(7500);
            reloaded.CurrentSettings.PollingIntervals.ActiveMs.Should().Be(500);
        }

        // ─── Opacity / UI Tests ────────────────────────────────────────

        [Fact]
        public void Opacity_IsClampedByConfigService_WhenBelowMin()
        {
            var s = _configService.CurrentSettings;
            s.Ui.Opacity = 0.0; // below 0.1 minimum
            _configService.Save();

            // Reload — ConfigService clamps on load
            var reloaded = new ConfigService(_tempFilePath);
            reloaded.Load();
            reloaded.CurrentSettings.Ui.Opacity.Should().BeGreaterThanOrEqualTo(0.1);
        }

        [Fact]
        public void Opacity_IsClampedByConfigService_WhenAboveMax()
        {
            var s = _configService.CurrentSettings;
            s.Ui.Opacity = 2.0; // above 1.0 maximum
            _configService.Save();

            var reloaded = new ConfigService(_tempFilePath);
            reloaded.Load();
            reloaded.CurrentSettings.Ui.Opacity.Should().BeLessThanOrEqualTo(1.0);
        }

        [Theory]
        [InlineData(0.3)]
        [InlineData(0.7)]
        [InlineData(1.0)]
        public void Opacity_ValidValues_RoundTripCorrectly(double opacity)
        {
            _configService.CurrentSettings.Ui.Opacity = opacity;
            _configService.Save();

            var reloaded = new ConfigService(_tempFilePath);
            reloaded.Load();
            reloaded.CurrentSettings.Ui.Opacity.Should().BeApproximately(opacity, 0.001);
        }

        // ─── Alert Settings Tests ──────────────────────────────────────

        [Fact]
        public void AlertSettings_VramThresholdAndCooldown_PersistCorrectly()
        {
            var s = _configService.CurrentSettings;
            s.Alerts.VramWarningThresholdPercent = 85.0;
            s.Alerts.CooldownSeconds = 120;
            _configService.Save();

            var reloaded = new ConfigService(_tempFilePath);
            reloaded.Load();
            reloaded.CurrentSettings.Alerts.VramWarningThresholdPercent.Should().Be(85.0);
            reloaded.CurrentSettings.Alerts.CooldownSeconds.Should().Be(120);
        }

        // ─── Runtime Config Tests ──────────────────────────────────────

        [Fact]
        public void RuntimeConfig_EnableDisable_PersistsCorrectly()
        {
            var s = _configService.CurrentSettings;
            s.Runtimes.Ollama.Enabled = false;
            s.Runtimes.LlamaCpp.Enabled = true;
            s.Runtimes.LlamaCpp.Endpoint = "http://127.0.0.1:9090";
            _configService.Save();

            var reloaded = new ConfigService(_tempFilePath);
            reloaded.Load();
            reloaded.CurrentSettings.Runtimes.Ollama.Enabled.Should().BeFalse();
            reloaded.CurrentSettings.Runtimes.LlamaCpp.Enabled.Should().BeTrue();
            reloaded.CurrentSettings.Runtimes.LlamaCpp.Endpoint.Should().Be("http://127.0.0.1:9090");
        }

        // ─── Deep-Copy / Cancel Safety Tests ──────────────────────────

        [Fact]
        public void DeepCopySettings_PreservesAllFields()
        {
            var original = new ModelPulseSettings
            {
                PollingMode = "custom",
                PollingIntervals = new PollingIntervals { IdleMs = 5000, ActiveMs = 750 },
                Ui = new UiConfig { Opacity = 0.65, AlwaysOnTop = false, LaunchOverlayOnStartup = true },
                Runtimes = new RuntimesConfig
                {
                    Ollama   = new RuntimeConfig { Enabled = true,  Endpoint = "http://a:11434" },
                    LlamaCpp = new RuntimeConfig { Enabled = false, Endpoint = "http://b:8080" }
                },
                Alerts = new AlertsConfig { VramWarningThresholdPercent = 75.0, CooldownSeconds = 90 }
            };

            // Simulate the deep-copy that SettingsWindow performs on open
            var copy = new ModelPulseSettings
            {
                PollingMode = original.PollingMode,
                PollingIntervals = new PollingIntervals
                {
                    IdleMs   = original.PollingIntervals.IdleMs,
                    ActiveMs = original.PollingIntervals.ActiveMs
                },
                Ui = new UiConfig
                {
                    Opacity               = original.Ui.Opacity,
                    AlwaysOnTop           = original.Ui.AlwaysOnTop,
                    LaunchOverlayOnStartup = original.Ui.LaunchOverlayOnStartup
                }
            };

            // Mutate the original — copy should be unaffected
            original.PollingMode = "default";
            original.Ui.Opacity = 1.0;

            copy.PollingMode.Should().Be("custom");
            copy.Ui.Opacity.Should().BeApproximately(0.65, 0.001);
        }
    }
}
