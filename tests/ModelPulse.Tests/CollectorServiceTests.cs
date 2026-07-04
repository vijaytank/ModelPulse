using System;
using System.Collections.Generic;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Xunit;
using Moq;
using FluentAssertions;
using Microsoft.Reactive.Testing;
using ModelPulse.Core.Adapters;
using ModelPulse.Core.Models;
using ModelPulse.Core.Services.Config;
using ModelPulse.Core.Services.Polling;
using ModelPulse.Core.Services.System;

namespace ModelPulse.Tests
{
    public class CollectorServiceTests
    {
        private readonly Mock<IConfigService> _mockConfigService;
        private readonly Mock<SystemTelemetryProvider> _mockSystemProvider;
        private readonly Mock<IRuntimeAdapter> _mockOllamaAdapter;
        private readonly TestScheduler _testScheduler;
        private readonly ModelPulseSettings _settings;

        public CollectorServiceTests()
        {
            _mockConfigService = new Mock<IConfigService>();
            _mockSystemProvider = new Mock<SystemTelemetryProvider>();
            _mockOllamaAdapter = new Mock<IRuntimeAdapter>();
            _testScheduler = new TestScheduler();

            _settings = new ModelPulseSettings
            {
                PollingMode = "default",
                PollingIntervals = new PollingIntervals { IdleMs = 3000, ActiveMs = 1000 },
                Runtimes = new RuntimesConfig
                {
                    Ollama = new RuntimeConfig { Enabled = true, Endpoint = "http://localhost" },
                    LlamaCpp = new RuntimeConfig { Enabled = false, Endpoint = "http://localhost" }
                }
            };

            _mockConfigService.Setup(c => c.CurrentSettings).Returns(_settings);

            _mockSystemProvider.Setup(s => s.GetSystemState()).Returns(new SystemTelemetryState
            {
                CpuPercent = 10,
                RamUsedMb = 2000,
                RamTotalMb = 16000
            });

            _mockOllamaAdapter.Setup(a => a.RuntimeName).Returns("ollama");
            _mockOllamaAdapter.Setup(a => a.IsAvailableAsync()).ReturnsAsync(true);
            _mockOllamaAdapter.Setup(a => a.GetRuntimeSummaryAsync()).ReturnsAsync(new RuntimeSummary { IsAvailable = true });
            _mockOllamaAdapter.Setup(a => a.GetActiveModelsAsync()).ReturnsAsync(new List<ActiveModelInfo>());
            _mockOllamaAdapter.Setup(a => a.GetPerformanceSampleAsync()).ReturnsAsync(new PerformanceSample());
            _mockOllamaAdapter.Setup(a => a.GetUnknownFields()).Returns(new List<UnknownFieldEntry>());
        }

        [Fact]
        public async Task CollectorService_PublishSnapshotsPeriodicInIdleMode()
        {
            // Arrange
            var adapters = new[] { _mockOllamaAdapter.Object };
            var collector = new CollectorService(
                _mockConfigService.Object,
                _mockSystemProvider.Object,
                adapters,
                _testScheduler);

            var snapshotsReceived = new List<CollectorSnapshot>();
            using var sub = collector.Snapshots.Where(s => s.Timestamp != DateTime.MinValue).Subscribe(snapshotsReceived.Add);

            // Act
            await collector.StartAsync();

            // At t = 0, first poll is scheduled but has not run because the virtual scheduler has not advanced
            snapshotsReceived.Should().BeEmpty();

            // Advance by 6 seconds + 10 ticks (should poll at t=0s, t=3s and t=6s)
            _testScheduler.AdvanceBy(TimeSpan.FromSeconds(6).Ticks + 10);

            // Assert: Total of 3 snapshots (t=0s, t=3s, t=6s)
            snapshotsReceived.Should().HaveCount(3);
            snapshotsReceived[1].System.CpuPercent.Should().Be(10);
        }

        [Fact]
        public async Task CollectorService_AdaptivePolling_SwitchesToActiveIntervalOnActiveInference()
        {
            // Arrange
            var adapters = new[] { _mockOllamaAdapter.Object };
            var collector = new CollectorService(
                _mockConfigService.Object,
                _mockSystemProvider.Object,
                adapters,
                _testScheduler);

            var snapshotsReceived = new List<CollectorSnapshot>();
            using var sub = collector.Snapshots.Where(s => s.Timestamp != DateTime.MinValue).Subscribe(snapshotsReceived.Add);

            // Set up active performance sample BEFORE start
            _mockOllamaAdapter.Setup(a => a.GetPerformanceSampleAsync()).ReturnsAsync(new PerformanceSample
            {
                GenerationTokensPerSecond = 25.0 // Active inference detected!
            });

            await collector.StartAsync();
            snapshotsReceived.Should().BeEmpty(); // t=0s, not run yet

            // Advance to t=3s + 10 ticks. Since active is 1s, we should get polls at t=0s, t=1s, t=2s, t=3s
            _testScheduler.AdvanceBy(TimeSpan.FromSeconds(3).Ticks + 10);
            snapshotsReceived.Should().HaveCount(4); // t=0s, 1s, 2s, 3s
        }

        [Fact]
        public async Task CollectorService_AdaptivePolling_ReturnsToIdleIntervalOnInactivity()
        {
            // Arrange
            var adapters = new[] { _mockOllamaAdapter.Object };
            var collector = new CollectorService(
                _mockConfigService.Object,
                _mockSystemProvider.Object,
                adapters,
                _testScheduler);

            var snapshotsReceived = new List<CollectorSnapshot>();
            using var sub = collector.Snapshots.Where(s => s.Timestamp != DateTime.MinValue).Subscribe(snapshotsReceived.Add);

            await collector.StartAsync(); // t=0s

            // Make active immediately
            _mockOllamaAdapter.Setup(a => a.GetPerformanceSampleAsync()).ReturnsAsync(new PerformanceSample
            {
                GenerationTokensPerSecond = 15.0
            });

            // Advance by 2 seconds (polls at t=0s, t=1s, t=2s)
            _testScheduler.AdvanceBy(TimeSpan.FromSeconds(2).Ticks + 10);
            snapshotsReceived.Should().HaveCount(3); // t=0, 1, 2

            // Now make it idle again
            _mockOllamaAdapter.Setup(a => a.GetPerformanceSampleAsync()).ReturnsAsync(new PerformanceSample());

            // Next poll at t=3s will see it is idle.
            // After t=3s (which is still polled 1s after t=2s), the next poll should be scheduled 3s later (t=6s).
            _testScheduler.AdvanceBy(TimeSpan.FromSeconds(1).Ticks + 10); // t=3s (detects idle)
            snapshotsReceived.Should().HaveCount(4);

            // Advance by 2 seconds (t=5s) -> should NOT poll
            _testScheduler.AdvanceBy(TimeSpan.FromSeconds(2).Ticks + 10);
            snapshotsReceived.Should().HaveCount(4);

            // Advance by 1 more second (t=6s) -> should poll
            _testScheduler.AdvanceBy(TimeSpan.FromSeconds(1).Ticks + 10);
            snapshotsReceived.Should().HaveCount(5); // t=0, 1, 2, 3, 6
        }

        [Fact]
        public async Task CollectorService_ConsecutiveFailures_AppliesExponentialBackoff()
        {
            // Arrange
            var adapters = new[] { _mockOllamaAdapter.Object };
            var collector = new CollectorService(
                _mockConfigService.Object,
                _mockSystemProvider.Object,
                adapters,
                _testScheduler);

            var snapshotsReceived = new List<CollectorSnapshot>();
            using var sub = collector.Snapshots.Where(s => s.Timestamp != DateTime.MinValue).Subscribe(snapshotsReceived.Add);

            // Make adapter unavailable
            _mockOllamaAdapter.Setup(a => a.IsAvailableAsync()).ReturnsAsync(false);
            _mockOllamaAdapter.Setup(a => a.GetRuntimeSummaryAsync()).ReturnsAsync(new RuntimeSummary { IsAvailable = false });

            await collector.StartAsync(); // t=0s (Failure 1)

            // Let's verify subsequent delays.
            // Failure 1: base delay (3s) * 2^1 = 6s. Next poll should be at t=6s.
            _testScheduler.AdvanceBy(TimeSpan.FromSeconds(5).Ticks + 10);
            snapshotsReceived.Should().HaveCount(1); // Poll at t=0 has run

            _testScheduler.AdvanceBy(TimeSpan.FromSeconds(1).Ticks + 10);
            snapshotsReceived.Should().HaveCount(2); // Polls at t=6s. (Failure 2)

            // Failure 2: base delay (3s) * 2^2 = 12s. Next poll should be at t=18s.
            _testScheduler.AdvanceBy(TimeSpan.FromSeconds(11).Ticks + 10);
            snapshotsReceived.Should().HaveCount(2); // Still 2

            _testScheduler.AdvanceBy(TimeSpan.FromSeconds(1).Ticks + 10);
            snapshotsReceived.Should().HaveCount(3); // Polls at t=18s. (Failure 3)

            // Now make it available again. Next poll will reset the delay.
            _mockOllamaAdapter.Setup(a => a.IsAvailableAsync()).ReturnsAsync(true);
            _mockOllamaAdapter.Setup(a => a.GetRuntimeSummaryAsync()).ReturnsAsync(new RuntimeSummary { IsAvailable = true });

            // Failure 3: base delay (3s) * 2^3 = 24s. Next poll should be at t=42s.
            _testScheduler.AdvanceBy(TimeSpan.FromSeconds(24).Ticks + 10);
            snapshotsReceived.Should().HaveCount(4); // Polls at t=42s (Succeeds!)

            // Success resets backoff! Next poll should occur at standard 3s interval (t=45s).
            _testScheduler.AdvanceBy(TimeSpan.FromSeconds(2).Ticks + 10);
            snapshotsReceived.Should().HaveCount(4); // t=44s (no poll)

            _testScheduler.AdvanceBy(TimeSpan.FromSeconds(1).Ticks + 10);
            snapshotsReceived.Should().HaveCount(5); // t=45s (polls!)
        }

        /// <summary>
        /// Regression test for the ForceRefresh CPU tight-loop bug (Bug #1).
        ///
        /// Root cause: SetPollingInterval(TimeSpan.Zero) sets _overrideInterval to
        /// TimeSpan.Zero (HasValue = true), so ComputeNextDelay() returns 0ms on every
        /// cycle — an infinite tight-loop consuming CPU.
        ///
        /// This test verifies that ClearPollingOverride() sets the override to null
        /// and that subsequent scheduling resumes at the adaptive idle interval (3s),
        /// NOT at 0ms.
        ///
        /// Note on virtual-time mechanics: when ClearPollingOverride() is called, the
        /// scheduler has already queued the next callback at the burst interval (100ms).
        /// That one in-flight cycle fires, then adaptive scheduling takes over.
        /// We advance 200ms to let the in-flight fire, clear the baseline, then verify
        /// 3s idle cadence from that point forward.
        /// </summary>
        [Fact]
        public async Task CollectorService_ClearPollingOverride_ResumesAdaptiveScheduling()
        {
            // Arrange
            var adapters = new[] { _mockOllamaAdapter.Object };
            var collector = new CollectorService(
                _mockConfigService.Object,
                _mockSystemProvider.Object,
                adapters,
                _testScheduler);

            var snapshotsReceived = new List<CollectorSnapshot>();
            using var sub = collector.Snapshots
                .Where(s => s.Timestamp != DateTime.MinValue)
                .Subscribe(snapshotsReceived.Add);

            await collector.StartAsync();

            // Simulate ForceRefresh: set a 100ms burst interval
            collector.SetPollingInterval(TimeSpan.FromMilliseconds(100));

            // Advance 500ms — polls should happen every 100ms
            _testScheduler.AdvanceBy(TimeSpan.FromMilliseconds(500).Ticks + 10);
            snapshotsReceived.Count.Should().BeGreaterThan(3, "burst should produce multiple rapid polls");

            // Clear override. The scheduler already has the next 100ms burst callback queued.
            // Let that one in-flight cycle fire (advance 200ms), then snapshot the baseline count.
            // After the in-flight poll completes, ComputeNextDelay() will see _overrideInterval==null
            // and schedule the NEXT poll 3s (idle) later from that point.
            collector.ClearPollingOverride();
            _testScheduler.AdvanceBy(TimeSpan.FromMilliseconds(200).Ticks + 10);
            var baselineCount = snapshotsReceived.Count;

            // From here on, adaptive idle=3s should be in effect.
            // Advance 2.8 seconds — NOT enough to cross the 3s boundary. Count must stay the same.
            _testScheduler.AdvanceBy(TimeSpan.FromMilliseconds(2800).Ticks);
            snapshotsReceived.Count.Should().Be(baselineCount,
                "adaptive idle interval is 3s; no new poll should fire in 2.8s after ClearPollingOverride");

            // Advance a further 400ms (total ~3.2s from baseline) → exactly one new poll
            _testScheduler.AdvanceBy(TimeSpan.FromMilliseconds(400).Ticks);
            snapshotsReceived.Count.Should().Be(baselineCount + 1,
                "exactly one new poll should fire after the 3s adaptive idle boundary");
        }

        /// <summary>
        /// Verifies that after ClearPollingOverride(), a subsequent SetPollingInterval
        /// (non-zero) correctly establishes a new explicit cadence.
        /// </summary>
        [Fact]
        public async Task CollectorService_SetPollingIntervalAfterClear_Works()
        {
            var adapters = new[] { _mockOllamaAdapter.Object };
            var collector = new CollectorService(
                _mockConfigService.Object,
                _mockSystemProvider.Object,
                adapters,
                _testScheduler);

            var snapshotsReceived = new List<CollectorSnapshot>();
            using var sub = collector.Snapshots
                .Where(s => s.Timestamp != DateTime.MinValue)
                .Subscribe(snapshotsReceived.Add);

            await collector.StartAsync();

            // Set, then immediately clear (no time advance — tests the state transition only)
            collector.SetPollingInterval(TimeSpan.FromMilliseconds(500));
            collector.ClearPollingOverride();

            // Immediately set a 1s explicit interval.
            // The first poll fires at t=0 (already scheduled by StartAsync),
            // then at t=1s, t=2s, etc.
            collector.SetPollingInterval(TimeSpan.FromSeconds(1));

            // Let t=0 poll fire, then clear baseline
            _testScheduler.AdvanceBy(10);
            snapshotsReceived.Clear();

            // Advance 2s + 10 ticks — should see polls at t+1s and t+2s (2 polls)
            _testScheduler.AdvanceBy(TimeSpan.FromSeconds(2).Ticks + 10);
            snapshotsReceived.Should().HaveCount(2,
                "after ClearPollingOverride + new SetPollingInterval(1s), should poll every 1s");
        }

        [Fact]
        public async Task CollectorService_AdapterSoftHealthCheck_ResilientToAdapterException()
        {
            // Arrange: Create a broken adapter that throws exceptions on everything,
            // and a healthy adapter that returns successfully.
            var mockBrokenAdapter = new Mock<IRuntimeAdapter>();
            mockBrokenAdapter.Setup(a => a.RuntimeName).Returns("broken-runtime");
            mockBrokenAdapter.Setup(a => a.IsAvailableAsync()).ThrowsAsync(new InvalidOperationException("Adapter crashed!"));

            var mockHealthyAdapter = new Mock<IRuntimeAdapter>();
            mockHealthyAdapter.Setup(a => a.RuntimeName).Returns("healthy-runtime");
            mockHealthyAdapter.Setup(a => a.IsAvailableAsync()).ReturnsAsync(true);
            mockHealthyAdapter.Setup(a => a.GetRuntimeSummaryAsync()).ReturnsAsync(new RuntimeSummary
            {
                RuntimeName = "healthy-runtime",
                IsAvailable = true,
                RuntimeVersion = "2.0.0"
            });
            mockHealthyAdapter.Setup(a => a.GetActiveModelsAsync()).ReturnsAsync(new List<ActiveModelInfo>());
            mockHealthyAdapter.Setup(a => a.GetPerformanceSampleAsync()).ReturnsAsync(new PerformanceSample());
            mockHealthyAdapter.Setup(a => a.GetUnknownFields()).Returns(new List<UnknownFieldEntry>());

            var adapters = new[] { mockBrokenAdapter.Object, mockHealthyAdapter.Object };

            var collector = new CollectorService(
                _mockConfigService.Object,
                _mockSystemProvider.Object,
                adapters,
                _testScheduler);

            var snapshotsReceived = new List<CollectorSnapshot>();
            using var sub = collector.Snapshots
                .Where(s => s.Timestamp != DateTime.MinValue)
                .Subscribe(snapshotsReceived.Add);

            // Act
            await collector.StartAsync();

            // Advance by 1 tick to let the t=0 poll fire
            _testScheduler.AdvanceBy(10);

            // Assert: We should have received a snapshot
            snapshotsReceived.Should().ContainSingle();
            var snap = snapshotsReceived[0];

            // The broken adapter should not have stopped the healthy adapter's info from being collected
            snap.Runtimes.Should().ContainSingle(r => r.RuntimeName == "healthy-runtime");
            snap.Runtimes.Should().NotContain(r => r.RuntimeName == "broken-runtime");
        }

        [Fact]
        public async Task CollectorService_SetPollingIntervalZero_ClearsOverrideAndResumesAdaptiveScheduling()
        {
            // Arrange
            var adapters = new[] { _mockOllamaAdapter.Object };
            var collector = new CollectorService(
                _mockConfigService.Object,
                _mockSystemProvider.Object,
                adapters,
                _testScheduler);

            var snapshotsReceived = new List<CollectorSnapshot>();
            using var sub = collector.Snapshots
                .Where(s => s.Timestamp != DateTime.MinValue)
                .Subscribe(snapshotsReceived.Add);

            await collector.StartAsync();

            // Set polling interval override
            collector.SetPollingInterval(TimeSpan.FromMilliseconds(500));

            // Act: Call SetPollingInterval with TimeSpan.Zero
            collector.SetPollingInterval(TimeSpan.Zero);

            // Let t=0 poll fire, then clear baseline
            _testScheduler.AdvanceBy(10);
            snapshotsReceived.Clear();

            // Assert: Standard adaptive scheduling (idle = 3s) should be in effect
            // Advance by 2.8 seconds - should NOT poll
            _testScheduler.AdvanceBy(TimeSpan.FromMilliseconds(2800).Ticks);
            snapshotsReceived.Should().BeEmpty();

            // Advance by a further 400ms (crossing the 3s adaptive boundary) -> should poll once
            _testScheduler.AdvanceBy(TimeSpan.FromMilliseconds(400).Ticks);
            snapshotsReceived.Should().ContainSingle();
        }
    }
}
