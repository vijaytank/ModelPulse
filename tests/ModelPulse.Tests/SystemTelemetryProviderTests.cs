using FluentAssertions;
using ModelPulse.Core.Services.System;
using Xunit;

namespace ModelPulse.Tests
{
    /// <summary>
    /// Integration-style tests for SystemTelemetryProvider.
    /// These run on the local developer machine against real hardware.
    /// </summary>
    public class SystemTelemetryProviderTests
    {
        [Fact]
        public void ProbeGpu_ReturnsNonUnavailableSourcePath_OnMachineWithGpu()
        {
            // Arrange
            var provider = new SystemTelemetryProvider();

            // Act
            var state = provider.ProbeGpu();

            // Assert: on a machine with any GPU, at least DXGI should succeed
            state.SourcePath.Should().NotBe("Unavailable",
                "this machine has a GPU and at least DXGI should resolve telemetry");
        }

        [Fact]
        public void ProbeGpu_NeverReturnsNull()
        {
            // Arrange
            var provider = new SystemTelemetryProvider();

            // Act
            var state = provider.ProbeGpu();

            // Assert
            state.Should().NotBeNull();
            state.SourcePath.Should().NotBeNullOrEmpty();
        }

        [Fact]
        public void ProbeGpu_VramTotalMb_IsPositiveWhenGpuResolves()
        {
            // Arrange
            var provider = new SystemTelemetryProvider();

            // Act
            var state = provider.ProbeGpu();

            // Assert: if a real GPU was found, VRAM total should be > 0
            if (state.SourcePath != "Unavailable")
            {
                state.VramTotalMb.Should().BeGreaterThan(0,
                    "any detected GPU should report non-zero total VRAM");
            }
        }

        [Fact]
        public void GetSystemState_CpuPercent_IsInValidRange()
        {
            // Arrange
            var provider = new SystemTelemetryProvider();

            // Act
            var state = provider.GetSystemState();

            // Assert: CPU % must always be in 0–100 range
            state.CpuPercent.Should().BeInRange(0, 100);
            state.RamUsedMb.Should().BeGreaterThan(0);
            state.RamTotalMb.Should().BeGreaterThan(0);
            state.WidgetRamMb.Should().BeGreaterThan(0,
                "widget should report its own process working set");
        }
    }
}
