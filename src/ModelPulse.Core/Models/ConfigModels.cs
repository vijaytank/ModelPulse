using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ModelPulse.Core.Models
{
    public class PollingIntervals
    {
        [JsonPropertyName("idle_ms")]
        public int IdleMs { get; set; } = 3000;

        [JsonPropertyName("active_ms")]
        public int ActiveMs { get; set; } = 1000;
    }

    public class RuntimeConfig
    {
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; }

        [JsonPropertyName("endpoint")]
        public string Endpoint { get; set; } = string.Empty;
    }

    public class RuntimesConfig
    {
        [JsonPropertyName("ollama")]
        public RuntimeConfig Ollama { get; set; } = new() { Enabled = true, Endpoint = "http://127.0.0.1:11434" };

        [JsonPropertyName("llama_cpp")]
        public RuntimeConfig LlamaCpp { get; set; } = new() { Enabled = false, Endpoint = "http://127.0.0.1:8080" };
    }

    public class AlertsConfig
    {
        [JsonPropertyName("suppressed_types")]
        public List<string> SuppressedTypes { get; set; } = new();

        [JsonPropertyName("cooldown_seconds")]
        public int CooldownSeconds { get; set; } = 60;

        [JsonPropertyName("vram_warning_threshold_percent")]
        public double VramWarningThresholdPercent { get; set; } = 90.0;
    }

    public class UiConfig
    {
        [JsonPropertyName("always_on_top")]
        public bool AlwaysOnTop { get; set; } = true;

        [JsonPropertyName("opacity")]
        public double Opacity { get; set; } = 0.9;

        [JsonPropertyName("launch_overlay_on_startup")]
        public bool LaunchOverlayOnStartup { get; set; } = false;
    }

    public class ModelPulseSettings
    {
        [JsonPropertyName("polling_mode")]
        public string PollingMode { get; set; } = "default";

        [JsonPropertyName("polling_intervals")]
        public PollingIntervals PollingIntervals { get; set; } = new();

        [JsonPropertyName("runtimes")]
        public RuntimesConfig Runtimes { get; set; } = new();

        [JsonPropertyName("alerts")]
        public AlertsConfig Alerts { get; set; } = new();

        [JsonPropertyName("ui")]
        public UiConfig Ui { get; set; } = new();
    }
}
