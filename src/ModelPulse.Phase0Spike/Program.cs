using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ModelPulse.Core.Adapters;
using ModelPulse.Core.Adapters.Ollama;
using ModelPulse.Core.Adapters.LlamaCpp;
using ModelPulse.Core.Services.Config;
using ModelPulse.Core.Services.Polling;
using ModelPulse.Core.Services.System;
using ModelPulse.Core.Services.Alerts;

namespace ModelPulse.Phase0Spike
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("==================================================");
            Console.WriteLine("        MODELPULSE STABILIZATION PROFILER         ");
            Console.WriteLine("==================================================");

            // 1. Initialize Core Services
            var configService = new ConfigService();
            configService.Load();

            var systemProvider = new SystemTelemetryProvider();
            var alertEngine = new AlertEngine(configService);

            // Setup mock Ollama/Llama.cpp adapters (using mock endpoints)
            using var mockServer = new MockHttpServer("http://127.0.0.1:8080/");
            mockServer.Start();
            Console.WriteLine("[INFO] Started Mock llama.cpp server on http://127.0.0.1:8080/");

            using var httpClient = new HttpClient();
            var adapters = new IRuntimeAdapter[]
            {
                new LlamaCppAdapter(new HttpClient { BaseAddress = new Uri("http://127.0.0.1:8080/") })
            };

            var collector = new CollectorService(configService, systemProvider, adapters);

            // Connect AlertEngine to collector snapshots stream
            using var subscription = collector.Snapshots.Subscribe(snapshot =>
            {
                alertEngine.EvaluateSnapshot(snapshot);
            });

            // 2. Run Warm-Up Cycle
            Console.WriteLine("[INFO] Running warm-up cycle...");
            await collector.StartAsync();
            await Task.Delay(2000); // Allow first polls to fire

            Console.WriteLine("[INFO] Warm-up complete. Performing Garbage Collection...");
            GC.Collect(2, GCCollectionMode.Forced, true, true);
            GC.WaitForPendingFinalizers();

            // 3. Run 60-Second Footprint Validation Profiling
            Console.WriteLine("\n[INFO] Starting 60-second stabilization profile loop (1-second intervals)...");

            var process = Process.GetCurrentProcess();
            var startCpuTime = process.TotalProcessorTime;
            var stopwatch = Stopwatch.StartNew();

            // Loop for 60 seconds
            for (int i = 0; i < 60; i++)
            {
                await Task.Delay(1000);
            }

            stopwatch.Stop();
            await collector.StopAsync();

            // Force final GC collection to measure cleaned footprint
            GC.Collect(2, GCCollectionMode.Forced, true, true);
            GC.WaitForPendingFinalizers();

            process.Refresh();
            var endCpuTime = process.TotalProcessorTime;

            var cpuUsedMs = (endCpuTime - startCpuTime).TotalMilliseconds;
            var elapsedMs = stopwatch.Elapsed.TotalMilliseconds;
            var cpuPercent = (cpuUsedMs / (Environment.ProcessorCount * elapsedMs)) * 100.0;

            var managedRamMb = GC.GetTotalMemory(true) / (1024.0 * 1024.0);
            var privateRamMb = process.PrivateMemorySize64 / (1024.0 * 1024.0);
            var workingSetRamMb = process.WorkingSet64 / (1024.0 * 1024.0);

            Console.WriteLine("\n--- [RESOURCE FOOTPRINT BENCHMARK] ---");
            Console.WriteLine($"Profile Loop Duration: {stopwatch.Elapsed.TotalSeconds:F1}s");
            Console.WriteLine($"Average CPU Usage: {cpuPercent:F6}%");
            Console.WriteLine($"App Managed Memory: {managedRamMb:F2} MB");
            Console.WriteLine($"Process Private Memory: {privateRamMb:F2} MB");
            Console.WriteLine($"Process Working Set: {workingSetRamMb:F2} MB");

            Console.WriteLine("\n==================================================");
            Console.WriteLine("        STABILIZATION GATE EXIT CHECK             ");
            Console.WriteLine("==================================================");

            bool gatePass = true;
            if (cpuPercent >= 0.1)
            {
                Console.WriteLine("[FAIL] CPU footprint exceeds 0.1% target.");
                gatePass = false;
            }
            else
            {
                Console.WriteLine("[PASS] CPU footprint is under 0.1% target.");
            }

            if (managedRamMb >= 15.0)
            {
                Console.WriteLine("[FAIL] RAM footprint (App Managed Memory) exceeds 15MB target.");
                gatePass = false;
            }
            else
            {
                Console.WriteLine("[PASS] RAM footprint (App Managed Memory) is under 15MB target.");
            }

            if (gatePass)
            {
                Console.WriteLine("[GO/NO-GO STATUS] GATE PASSED (GO)!");
            }
            else
            {
                Console.WriteLine("[GO/NO-GO STATUS] GATE FAILED (NO-GO)!");
            }
            Console.WriteLine("==================================================");
        }
    }
}
