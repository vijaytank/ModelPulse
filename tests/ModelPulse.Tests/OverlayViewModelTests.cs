using System;
using System.Collections.Generic;
using System.Reactive.Subjects;
using Xunit;
using Moq;
using FluentAssertions;
using ModelPulse.Core.Models;
using ModelPulse.Core.Services;
using ModelPulse.Core.Services.Config;
using ModelPulse.Core.Services.Polling;
using ModelPulse.UI.ViewModels;

namespace ModelPulse.Tests
{
    public class OverlayViewModelTests
    {
        private readonly Mock<ICollectorService> _mockCollector;
        private readonly Mock<IConfigService> _mockConfig;
        private readonly Subject<CollectorSnapshot> _snapshotsSubject;

        public OverlayViewModelTests()
        {
            _mockCollector = new Mock<ICollectorService>();
            _mockConfig = new Mock<IConfigService>();
            _snapshotsSubject = new Subject<CollectorSnapshot>();

            _mockCollector.Setup(c => c.Snapshots).Returns(_snapshotsSubject);
            _mockConfig.Setup(c => c.CurrentSettings).Returns(new ModelPulseSettings());
        }

        [Fact]
        public void ViewModel_UpdatesProperties_OnNewSnapshot()
        {
            // Arrange
            using var viewModel = new OverlayViewModel(_mockCollector.Object, _mockConfig.Object);

            var snapshot = new CollectorSnapshot
            {
                Timestamp = DateTime.UtcNow,
                System = new SystemTelemetryState
                {
                    CpuPercent = 42,
                    RamUsedMb = 8192,
                    RamTotalMb = 16384,
                    Gpu = new GpuTelemetryState
                    {
                        SourcePath = "NVML",
                        UtilizationPercent = 75,
                        VramUsedMb = 4096,
                        VramTotalMb = 8192
                    }
                }
            };

            // Act
            _snapshotsSubject.OnNext(snapshot);

            // Assert
            viewModel.CpuPercent.Should().Be(42);
            viewModel.CpuText.Should().Be("42%");
            viewModel.RamPercent.Should().Be(50.0);
            viewModel.RamText.Should().Be("8.0 / 16.0 GB");
            viewModel.GpuPercent.Should().Be(75);
            viewModel.GpuText.Should().Be("75%");
            viewModel.VramPercent.Should().Be(50.0);
            viewModel.VramText.Should().Be("4.0 / 8.0 GB");
        }

        [Fact]
        public void ViewModel_FiltersOutRedundantPropertyChangedEvents_OnEqualValues()
        {
            // Arrange
            using var viewModel = new OverlayViewModel(_mockCollector.Object, _mockConfig.Object);

            var snapshot1 = new CollectorSnapshot
            {
                Timestamp = DateTime.UtcNow,
                System = new SystemTelemetryState { CpuPercent = 10 }
            };
            var snapshot2 = new CollectorSnapshot
            {
                Timestamp = DateTime.UtcNow.AddSeconds(1),
                System = new SystemTelemetryState { CpuPercent = 10 } // Same CPU value
            };

            int propertyChangedCount = 0;
            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(OverlayViewModel.CpuPercent))
                {
                    propertyChangedCount++;
                }
            };

            // Act
            _snapshotsSubject.OnNext(snapshot1); // Should trigger change (0 -> 10)
            _snapshotsSubject.OnNext(snapshot2); // Should NOT trigger change (10 -> 10)

            // Assert
            propertyChangedCount.Should().Be(1, "diff check must prevent duplicate PropertyChanged runs");
        }

        [Fact]
        public void ViewModel_CalculatesHistoryStats_Over5Minutes()
        {
            // Arrange
            using var viewModel = new OverlayViewModel(_mockCollector.Object, _mockConfig.Object);

            var now = DateTime.UtcNow;
            var snaps = new List<CollectorSnapshot>
            {
                new() { Timestamp = now.AddMinutes(-4), System = new SystemTelemetryState { CpuPercent = 10, RamUsedMb = 1024 } },
                new() { Timestamp = now.AddMinutes(-2), System = new SystemTelemetryState { CpuPercent = 20, RamUsedMb = 2048 } },
                new() { Timestamp = now.AddMinutes(0), System = new SystemTelemetryState { CpuPercent = 30, RamUsedMb = 3072 } }
            };

            // Act
            foreach (var s in snaps)
            {
                _snapshotsSubject.OnNext(s);
            }

            // Assert: Averages CPU: (10+20+30)/3 = 20%, RAM: (1+2+3)/3 = 2 GB
            viewModel.HistoryCpuAvg.Should().Be("20%");
            viewModel.HistoryRamAvg.Should().Be("2.0 GB");
        }
    }
}
