using FluentAssertions;
using ModelPulse.Core.Models;
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

        [Fact]
        public void SystemTelemetryProvider_WmiCaching_ReturnsCachedDataWithoutRequerying()
        {
            // Arrange
            var provider = new TestTelemetryProvider();

            // Act
            var result1 = provider.CallCachedWmi();
            var result2 = provider.CallCachedWmi();

            // Assert
            provider.WmiQueryCount.Should().Be(1, "WMI query should only be executed once and cached");
            result1.Should().NotBeNull();
            result1!.Name.Should().Be("Test GPU");
            result1.Should().BeSameAs(result2);
        }

        private class TestTelemetryProvider : SystemTelemetryProvider
        {
            public int WmiQueryCount { get; private set; }

            public GpuTelemetryState? CallCachedWmi()
            {
                return GetCachedData("WmiGpuState", () =>
                {
                    WmiQueryCount++;
                    return new GpuTelemetryState { Name = "Test GPU", SourcePath = "WMI" };
                });
            }
        }
    }
}
