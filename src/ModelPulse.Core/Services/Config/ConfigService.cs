using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using ModelPulse.Core.Models;

namespace ModelPulse.Core.Services.Config
{
    /// <summary>
    /// Configuration service to manage local settings load, save, and validation.
    /// Settings are stored locally in %APPDATA%\ModelPulse\settings.json.
    /// </summary>
    public class ConfigService : IConfigService
    {
        private readonly string? _filePath;
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true
        };

        // Consolidated property to resolve ambiguity errors (CS0229, CS0102)
        public ModelPulseSettings CurrentSettings { get; private set; } = new();

        public ConfigService(string? filePath = null)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                _filePath = Path.Combine(appData, "ModelPulse", "settings.json");
            }
            else
            {
                _filePath = filePath;
            }
        }

        public void Load()
        {
            try
            {
                if (!File.Exists(_filePath))
                {
                    CurrentSettings = new ModelPulseSettings();
                    Save();
                    return;
                }

                var json = File.ReadAllText(_filePath);
                var settings = JsonSerializer.Deserialize<ModelPulseSettings>(json);
                CurrentSettings = settings ?? new ModelPulseSettings();
                ValidateAndEnforceBounds();
            }
            catch (JsonException ex)
            {
                Debug.WriteLine($"[ConfigService] JSON deserialization failed. Using default settings. Error: {ex.Message}");
                CurrentSettings = new ModelPulseSettings();
            }
            catch (IOException ex)
            {
                Debug.WriteLine($"[ConfigService] File I/O error during load. Using default settings. Error: {ex.Message}");
                CurrentSettings = new ModelPulseSettings();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ConfigService] An unexpected error occurred during load. Using default settings. Error: {ex.Message}");
                CurrentSettings = new ModelPulseSettings();
            }
        }

        public void Save()
        {
            try
            {
                var filePath = _filePath ?? throw new InvalidOperationException("Config file path is not initialized.");
                var dir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var json = JsonSerializer.Serialize(CurrentSettings, JsonOptions);
                File.WriteAllText(filePath, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ConfigService] Save error: {ex.Message}");
            }
        }

        public void UpdateSettings(ModelPulseSettings newSettings)
        {
            CurrentSettings = newSettings ?? new ModelPulseSettings();
            ValidateAndEnforceBounds();
            Save();
        }

        private void ApplyEnvironmentVariables()
        {
            if (CurrentSettings == null)
            {
                CurrentSettings = new ModelPulseSettings();
            }
            if (CurrentSettings.PollingIntervals == null)
            {
                CurrentSettings.PollingIntervals = new PollingIntervals();
            }
            if (CurrentSettings.Runtimes == null)
            {
                CurrentSettings.Runtimes = new RuntimesConfig();
            }
            if (CurrentSettings.Runtimes.Ollama == null)
            {
                CurrentSettings.Runtimes.Ollama = new RuntimeConfig { Enabled = true, Endpoint = "http://127.0.0.1:11434" };
            }
            if (CurrentSettings.Runtimes.LlamaCpp == null)
            {
                CurrentSettings.Runtimes.LlamaCpp = new RuntimeConfig { Enabled = false, Endpoint = "http://127.0.0.1:8080" };
            }
            if (CurrentSettings.Alerts == null)
            {
                CurrentSettings.Alerts = new AlertsConfig();
            }
            if (CurrentSettings.Ui == null)
            {
                CurrentSettings.Ui = new UiConfig();
            }

            var envPollingMode = Environment.GetEnvironmentVariable("MODELPULSE_POLLING_MODE");
            if (!string.IsNullOrWhiteSpace(envPollingMode))
            {
                CurrentSettings.PollingMode = envPollingMode.Trim();
            }

            var envIdleMs = Environment.GetEnvironmentVariable("MODELPULSE_POLLING_INTERVAL_IDLE_MS");
            if (!string.IsNullOrWhiteSpace(envIdleMs) && int.TryParse(envIdleMs, out int idleMs))
            {
                CurrentSettings.PollingIntervals.IdleMs = idleMs;
            }

            var envActiveMs = Environment.GetEnvironmentVariable("MODELPULSE_POLLING_INTERVAL_ACTIVE_MS");
            if (!string.IsNullOrWhiteSpace(envActiveMs) && int.TryParse(envActiveMs, out int activeMs))
            {
                CurrentSettings.PollingIntervals.ActiveMs = activeMs;
            }

            var envOllamaEnabled = Environment.GetEnvironmentVariable("MODELPULSE_OLLAMA_ENABLED");
            if (!string.IsNullOrWhiteSpace(envOllamaEnabled) && bool.TryParse(envOllamaEnabled, out bool ollamaEnabled))
            {
                CurrentSettings.Runtimes.Ollama.Enabled = ollamaEnabled;
            }

            var envOllamaEndpoint = Environment.GetEnvironmentVariable("MODELPULSE_OLLAMA_ENDPOINT");
            if (!string.IsNullOrWhiteSpace(envOllamaEndpoint))
            {
                CurrentSettings.Runtimes.Ollama.Endpoint = envOllamaEndpoint.Trim();
            }

            var envLlamaCppEnabled = Environment.GetEnvironmentVariable("MODELPULSE_LLAMACPP_ENABLED");
            if (!string.IsNullOrWhiteSpace(envLlamaCppEnabled) && bool.TryParse(envLlamaCppEnabled, out bool llamaCppEnabled))
            {
                CurrentSettings.Runtimes.LlamaCpp.Enabled = llamaCppEnabled;
            }

            var envLlamaCppEndpoint = Environment.GetEnvironmentVariable("MODELPULSE_LLAMACPP_ENDPOINT");
            if (!string.IsNullOrWhiteSpace(envLlamaCppEndpoint))
            {
                CurrentSettings.Runtimes.LlamaCpp.Endpoint = envLlamaCppEndpoint.Trim();
            }

            var envCooldown = Environment.GetEnvironmentVariable("MODELPULSE_ALERT_COOLDOWN_SECONDS");
            if (!string.IsNullOrWhiteSpace(envCooldown) && int.TryParse(envCooldown, out int cooldown))
            {
                CurrentSettings.Alerts.CooldownSeconds = cooldown;
            }

            var envVramThreshold = Environment.GetEnvironmentVariable("MODELPULSE_ALERT_VRAM_WARNING_THRESHOLD_PERCENT");
            if (!string.IsNullOrWhiteSpace(envVramThreshold) && double.TryParse(envVramThreshold, out double vramThreshold))
            {
                CurrentSettings.Alerts.VramWarningThresholdPercent = vramThreshold;
            }

            var envAlwaysOnTop = Environment.GetEnvironmentVariable("MODELPULSE_UI_ALWAYS_ON_TOP");
            if (!string.IsNullOrWhiteSpace(envAlwaysOnTop) && bool.TryParse(envAlwaysOnTop, out bool alwaysOnTop))
            {
                CurrentSettings.Ui.AlwaysOnTop = alwaysOnTop;
            }

            var envOpacity = Environment.GetEnvironmentVariable("MODELPULSE_UI_OPACITY");
            if (!string.IsNullOrWhiteSpace(envOpacity) && double.TryParse(envOpacity, out double opacity))
            {
                CurrentSettings.Ui.Opacity = opacity;
            }

            var envLaunchOnStartup = Environment.GetEnvironmentVariable("MODELPULSE_UI_LAUNCH_OVERLAY_ON_STARTUP");
            if (!string.IsNullOrWhiteSpace(envLaunchOnStartup) && bool.TryParse(envLaunchOnStartup, out bool launchOnStartup))
            {
                CurrentSettings.Ui.LaunchOverlayOnStartup = launchOnStartup;
            }
        }

        private void ValidateAndEnforceBounds()
        {
            ApplyEnvironmentVariables();

            // Polling mode validation
            if (CurrentSettings.PollingMode != "default" &&
                CurrentSettings.PollingMode != "low-power" &&
                CurrentSettings.PollingMode != "custom")
            {
                CurrentSettings.PollingMode = "default";
            }

            // Polling intervals bounds checking
            if (CurrentSettings.PollingIntervals == null)
            {
                CurrentSettings.PollingIntervals = new PollingIntervals();
            }
            if (CurrentSettings.PollingIntervals.IdleMs < 500)
            {
                CurrentSettings.PollingIntervals.IdleMs = 500;
            }
            if (CurrentSettings.PollingIntervals.ActiveMs < 100)
            {
                CurrentSettings.PollingIntervals.ActiveMs = 100;
            }

            // Runtimes validation
            if (CurrentSettings.Runtimes == null)
            {
                CurrentSettings.Runtimes = new RuntimesConfig();
            }

            // Alerts bounds checking
            if (CurrentSettings.Alerts == null)
            {
                CurrentSettings.Alerts = new AlertsConfig();
            }
            if (CurrentSettings.Alerts.CooldownSeconds < 5)
            {
                CurrentSettings.Alerts.CooldownSeconds = 5;
            }
            if (CurrentSettings.Alerts.VramWarningThresholdPercent < 0 || CurrentSettings.Alerts.VramWarningThresholdPercent > 100.0)
            {
                CurrentSettings.Alerts.VramWarningThresholdPercent = 90.0; // Fallback to default
            }

            // UI bounds checking
            if (CurrentSettings.Ui == null)
            {
                CurrentSettings.Ui = new UiConfig();
            }
            if (CurrentSettings.Ui.Opacity < 0.1)
            {
                CurrentSettings.Ui.Opacity = 0.1;
            }
            else if (CurrentSettings.Ui.Opacity > 1.0)
            {
                CurrentSettings.Ui.Opacity = 1.0;
            }
        }
    }
}