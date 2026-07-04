using System.Windows;
using System.Windows.Input;
using ModelPulse.Core.Models;
using ModelPulse.Core.Services.Config;

namespace ModelPulse.UI
{
    /// <summary>
    /// Settings dialog for ModelPulse.
    /// Reads current settings from IConfigService on open, persists via Save(),
    /// and calls live-apply callbacks for opacity and polling interval so changes
    /// take effect immediately without restarting the app.
    ///
    /// Design contract:
    /// - NO direct reference to MainWindow, CollectorService, or AlertEngine.
    /// - Live changes are propagated via Action callbacks injected by App.xaml.cs.
    /// - Cancelling restores the previous settings state from the service.
    /// </summary>
    public partial class SettingsWindow : Window
    {
        private readonly IConfigService _configService;
        private readonly Action<double>? _onOpacityChanged;
        private readonly Action<TimeSpan>? _onPollingIntervalChanged;

        // Snapshot of settings at open time — used to restore on Cancel
        private readonly ModelPulseSettings _originalSettings;

        // Flags to suppress event handler re-entrancy during initial load
        private bool _loading;

        public SettingsWindow(
            IConfigService configService,
            Action<double>? onOpacityChanged = null,
            Action<TimeSpan>? onPollingIntervalChanged = null)
        {
            InitializeComponent();

            _configService = configService;
            _onOpacityChanged = onOpacityChanged;
            _onPollingIntervalChanged = onPollingIntervalChanged;

            // Deep-copy original settings to restore on Cancel
            var s = _configService.CurrentSettings;
            _originalSettings = DeepCopySettings(s);

            LoadFromSettings(s);

            // Allow dragging the settings window by its background
            MouseLeftButtonDown += (_, e) =>
            {
                if (e.ButtonState == MouseButtonState.Pressed) DragMove();
            };
        }

        // ─── Population ──────────────────────────────────────────────────

        private void LoadFromSettings(ModelPulseSettings s)
        {
            _loading = true;

            // Polling
            switch (s.PollingMode.ToLowerInvariant())
            {
                case "low-power": ModeLowPower.IsChecked = true; break;
                case "custom":    ModeCustom.IsChecked   = true; break;
                default:          ModeDefault.IsChecked  = true; break;
            }
            IdleMsBox.Text   = s.PollingIntervals.IdleMs.ToString();
            ActiveMsBox.Text = s.PollingIntervals.ActiveMs.ToString();

            // Display
            OpacitySlider.Value = Math.Round(s.Ui.Opacity * 100);
            OpacityLabel.Text   = $"{(int)OpacitySlider.Value}%";
            AlwaysOnTopCheck.IsChecked      = s.Ui.AlwaysOnTop;
            LaunchOnStartupCheck.IsChecked  = s.Ui.LaunchOverlayOnStartup;

            // Runtimes
            OllamaEnabledCheck.IsChecked    = s.Runtimes.Ollama.Enabled;
            OllamaEndpointBox.Text          = s.Runtimes.Ollama.Endpoint;
            LlamaCppEnabledCheck.IsChecked  = s.Runtimes.LlamaCpp.Enabled;
            LlamaCppEndpointBox.Text        = s.Runtimes.LlamaCpp.Endpoint;

            // Alerts
            VramThresholdBox.Text = s.Alerts.VramWarningThresholdPercent.ToString("0");
            CooldownBox.Text      = s.Alerts.CooldownSeconds.ToString();

            _loading = false;

            // Sync visibility of custom panel
            UpdateCustomPollingVisibility();
            UpdatePollingSummary();
        }

        // ─── Event Handlers ───────────────────────────────────────────────

        private void PollingMode_Checked(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            UpdateCustomPollingVisibility();
            UpdatePollingSummary();
        }

        private void UpdateCustomPollingVisibility()
        {
            if (CustomPollingPanel == null || PollingSummary == null) return;

            bool isCustom = ModeCustom.IsChecked == true;
            CustomPollingPanel.Visibility = isCustom ? Visibility.Visible : Visibility.Collapsed;
            PollingSummary.Visibility     = isCustom ? Visibility.Collapsed : Visibility.Visible;
        }

        private void UpdatePollingSummary()
        {
            if (PollingSummary == null || _loading) return;

            if (ModeLowPower.IsChecked == true)
                PollingSummary.Text = "Idle: 10 000 ms  |  Active: 3 000 ms";
            else
                PollingSummary.Text = "Idle: 3 000 ms  |  Active: 1 000 ms";
        }

        private void IntervalBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (_loading) return;
            ValidationMessage.Text = string.Empty;
        }

        private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (OpacityLabel == null) return;
            var pct = (int)OpacitySlider.Value;
            OpacityLabel.Text = $"{pct}%";

            // Live-preview: immediately apply opacity to the overlay
            if (!_loading)
            {
                _onOpacityChanged?.Invoke(pct / 100.0);
            }
        }

        private void ResetPollingDefaults_Click(object sender, RoutedEventArgs e)
        {
            IdleMsBox.Text   = "3000";
            ActiveMsBox.Text = "1000";
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Cancel();

        private void Cancel_Click(object sender, RoutedEventArgs e) => Cancel();

        private void Cancel()
        {
            // Restore live opacity to original value
            _onOpacityChanged?.Invoke(_originalSettings.Ui.Opacity);
            Close();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (!TryApplyToSettings(out var errorMsg))
            {
                ValidationMessage.Text = errorMsg;
                return;
            }

            _configService.Save();

            // Apply polling interval live if in custom mode
            if (ModeCustom.IsChecked == true &&
                int.TryParse(IdleMsBox.Text, out int idleMs) &&
                int.TryParse(ActiveMsBox.Text, out int activeMs))
            {
                // Apply idle interval as the live override; adaptive logic will
                // handle the active/idle switch on the next cycle.
                _onPollingIntervalChanged?.Invoke(TimeSpan.Zero); // let adaptive resume
            }
            else
            {
                _onPollingIntervalChanged?.Invoke(TimeSpan.Zero); // clear any manual override
            }

            Close();
        }

        // ─── Validation & Apply ───────────────────────────────────────────

        private bool TryApplyToSettings(out string errorMessage)
        {
            errorMessage = string.Empty;
            var s = _configService.CurrentSettings;

            // --- Polling mode ---
            if (ModeDefault.IsChecked == true)
            {
                s.PollingMode = "default";
                s.PollingIntervals.IdleMs   = 3000;
                s.PollingIntervals.ActiveMs = 1000;
            }
            else if (ModeLowPower.IsChecked == true)
            {
                s.PollingMode = "low-power";
                s.PollingIntervals.IdleMs   = 10000;
                s.PollingIntervals.ActiveMs = 3000;
            }
            else // Custom
            {
                if (!int.TryParse(IdleMsBox.Text, out int idleMs) || idleMs < 500 || idleMs > 60000)
                {
                    errorMessage = "Idle interval must be 500 – 60 000 ms.";
                    return false;
                }
                if (!int.TryParse(ActiveMsBox.Text, out int activeMs) || activeMs < 100 || activeMs > 10000)
                {
                    errorMessage = "Active interval must be 100 – 10 000 ms.";
                    return false;
                }
                s.PollingMode = "custom";
                s.PollingIntervals.IdleMs   = idleMs;
                s.PollingIntervals.ActiveMs = activeMs;
            }

            // --- Display ---
            s.Ui.Opacity               = OpacitySlider.Value / 100.0;
            s.Ui.AlwaysOnTop           = AlwaysOnTopCheck.IsChecked == true;
            s.Ui.LaunchOverlayOnStartup = LaunchOnStartupCheck.IsChecked == true;

            // --- Runtimes ---
            s.Runtimes.Ollama.Enabled   = OllamaEnabledCheck.IsChecked == true;
            s.Runtimes.Ollama.Endpoint  = OllamaEndpointBox.Text.Trim();
            s.Runtimes.LlamaCpp.Enabled  = LlamaCppEnabledCheck.IsChecked == true;
            s.Runtimes.LlamaCpp.Endpoint = LlamaCppEndpointBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(s.Runtimes.Ollama.Endpoint))
            {
                errorMessage = "Ollama endpoint cannot be empty.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(s.Runtimes.LlamaCpp.Endpoint))
            {
                errorMessage = "llama.cpp endpoint cannot be empty.";
                return false;
            }

            // --- Alerts ---
            if (!double.TryParse(VramThresholdBox.Text, out double vramPct) || vramPct < 50 || vramPct > 100)
            {
                errorMessage = "VRAM threshold must be 50 – 100 %.";
                return false;
            }
            if (!int.TryParse(CooldownBox.Text, out int cooldown) || cooldown < 10 || cooldown > 3600)
            {
                errorMessage = "Alert cooldown must be 10 – 3 600 seconds.";
                return false;
            }
            s.Alerts.VramWarningThresholdPercent = vramPct;
            s.Alerts.CooldownSeconds             = cooldown;

            return true;
        }

        // ─── Helpers ─────────────────────────────────────────────────────

        /// <summary>
        /// Creates a shallow-field copy of settings so Cancel can restore.
        /// Only primitive/value-type fields need deep copy here.
        /// </summary>
        private static ModelPulseSettings DeepCopySettings(ModelPulseSettings src) =>
            new()
            {
                PollingMode = src.PollingMode,
                PollingIntervals = new PollingIntervals
                {
                    IdleMs   = src.PollingIntervals.IdleMs,
                    ActiveMs = src.PollingIntervals.ActiveMs
                },
                Ui = new UiConfig
                {
                    Opacity               = src.Ui.Opacity,
                    AlwaysOnTop           = src.Ui.AlwaysOnTop,
                    LaunchOverlayOnStartup = src.Ui.LaunchOverlayOnStartup
                },
                Runtimes = new RuntimesConfig
                {
                    Ollama  = new RuntimeConfig { Enabled = src.Runtimes.Ollama.Enabled,  Endpoint = src.Runtimes.Ollama.Endpoint },
                    LlamaCpp = new RuntimeConfig { Enabled = src.Runtimes.LlamaCpp.Enabled, Endpoint = src.Runtimes.LlamaCpp.Endpoint }
                },
                Alerts = new AlertsConfig
                {
                    VramWarningThresholdPercent = src.Alerts.VramWarningThresholdPercent,
                    CooldownSeconds             = src.Alerts.CooldownSeconds
                }
            };
    }
}
