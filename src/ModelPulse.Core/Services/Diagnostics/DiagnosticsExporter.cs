using System;
using System.Collections.Generic;
using System.Text.Json;
using ModelPulse.Core.Models;

namespace ModelPulse.Core.Services.Diagnostics
{
    /// <summary>
    /// Exporter to package configuration and active telemetry snapshots
    /// into a pretty-printed diagnostic JSON format.
    /// </summary>
    public static class DiagnosticsExporter
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true
        };

        /// <summary>
        /// Serializes settings, snapshots, active runtime configurations,
        /// and drift logging records into a diagnostic JSON report payload.
        /// </summary>
        public static string Export(CollectorSnapshot snapshot, ModelPulseSettings settings)
        {
            if (snapshot == null || settings == null)
            {
                return string.Empty;
            }

            var runtimesList = new List<object>();
            foreach (var r in snapshot.Runtimes)
            {
                runtimesList.Add(new
                {
                    name = r.RuntimeName,
                    version = r.RuntimeVersion,
                    is_available = r.IsAvailable
                });
            }

            var unknownFieldsList = new List<object>();
            foreach (var u in snapshot.UnknownFields)
            {
                unknownFieldsList.Add(new
                {
                    runtime = u.RuntimeName,
                    field_name = u.FieldName,
                    first_seen = u.FirstSeenAt,
                    last_seen = u.LastSeenAt,
                    sample_value = u.SampleValue
                });
            }

            var payload = new
            {
                exported_at = DateTime.UtcNow,
                polling_mode = settings.PollingMode,
                idle_ms = settings.PollingIntervals.IdleMs,
                active_ms = settings.PollingIntervals.ActiveMs,
                runtimes = runtimesList,
                unknown_fields = unknownFieldsList,
                system = new
                {
                    cpu_percent = snapshot.System.CpuPercent,
                    ram_used_mb = snapshot.System.RamUsedMb,
                    ram_total_mb = snapshot.System.RamTotalMb,
                    gpu_name = snapshot.System.Gpu.Name,
                    gpu_source = snapshot.System.Gpu.SourcePath,
                    gpu_utilization = snapshot.System.Gpu.UtilizationPercent,
                    vram_used_mb = snapshot.System.Gpu.VramUsedMb,
                    vram_total_mb = snapshot.System.Gpu.VramTotalMb
                }
            };

            return JsonSerializer.Serialize(payload, JsonOptions);
        }
    }
}
