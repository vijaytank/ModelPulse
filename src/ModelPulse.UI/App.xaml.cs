using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using ModelPulse.Core.Adapters;
using ModelPulse.Core.Adapters.LlamaCpp;
using ModelPulse.Core.Adapters.Ollama;
using ModelPulse.Core.Models;
using ModelPulse.Core.Services.Config;
using ModelPulse.Core.Services.Polling;
using ModelPulse.Core.Services.System;

using ModelPulse.Core.Services.Alerts;
using ModelPulse.Core.Services.Diagnostics;

namespace ModelPulse.UI
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private ConfigService? _configService;
        private CollectorService? _collectorService;
        private AlertEngine? _alertEngine;
        private IDisposable? _alertSubscription;
        private IDisposable? _snapshotSubscription;
        private TaskbarIcon? _taskbarIcon;
        private MainWindow? _overlayWindow;
        private bool _isPollingPaused;

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 1. Initialize Configuration Service
            _configService = new ConfigService();
            _configService.Load();

            // 2. Resolve enabled adapters
            var adapters = new List<IRuntimeAdapter>();
            var settings = _configService.CurrentSettings;

            if (settings.Runtimes.Ollama.Enabled)
            {
                var client = new HttpClient { BaseAddress = new Uri(settings.Runtimes.Ollama.Endpoint) };
                client.Timeout = TimeSpan.FromSeconds(2);
                adapters.Add(new OllamaAdapter(client));
            }

            if (settings.Runtimes.LlamaCpp.Enabled)
            {
                var client = new HttpClient { BaseAddress = new Uri(settings.Runtimes.LlamaCpp.Endpoint) };
                client.Timeout = TimeSpan.FromSeconds(2);
                adapters.Add(new LlamaCppAdapter(client));
            }

            // 3. Initialize Telemetry Provider, Collector Service, & Alert Engine
            var systemProvider = new SystemTelemetryProvider();
            _collectorService = new CollectorService(_configService, systemProvider, adapters);

            _alertEngine = new AlertEngine(_configService);

            // 4. Initialize Tray Icon first so it is available for alerts
            InitializeTrayIcon();

            // Forward snapshots to Alert Engine
            _snapshotSubscription = _collectorService.Snapshots.Subscribe(snapshot =>
            {
                _alertEngine.EvaluateSnapshot(snapshot);
            });

            // Listen to alerts and present them via system notifications
            _alertSubscription = _alertEngine.Alerts.Subscribe(alert =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    var notificationIcon = alert.Severity switch
                    {
                        AlertSeverity.Critical => NotificationIcon.Error,
                        AlertSeverity.Warning => NotificationIcon.Warning,
                        _ => NotificationIcon.Info
                    };

                    var title = alert.Severity switch
                    {
                        AlertSeverity.Critical => "ModelPulse - CRITICAL",
                        AlertSeverity.Warning => "ModelPulse - WARNING",
                        _ => "ModelPulse - INFO"
                    };

                    try
                    {
                        _taskbarIcon?.ShowNotification(title, alert.Message, notificationIcon);
                    }
                    catch
                    {
                        // Avoid crashes if tray icon has not registered fully yet
                    }
                }));
            });

            // Start background collection loop
            await _collectorService.StartAsync();

            // 5. Show Overlay Window on first launch for visibility
            ShowOverlay();
        }

        private void InitializeTrayIcon()
        {
            _taskbarIcon = new TaskbarIcon
            {
                ToolTipText = "ModelPulse - Local AI Telemetry",
                MenuActivation = PopupActivationMode.LeftOrRightClick
            };

            try
            {
                var resourceUri = new Uri("pack://application:,,,/ModelPulse.UI;component/Resources/tray_icon.ico");
                var resourceInfo = Application.GetResourceStream(resourceUri);
                if (resourceInfo != null)
                {
                    using (var stream = resourceInfo.Stream)
                    {
                        _taskbarIcon.Icon = new System.Drawing.Icon(stream);
                    }
                }
                else
                {
                    _taskbarIcon.Icon = System.Drawing.SystemIcons.Application;
                }
            }
            catch
            {
                _taskbarIcon.Icon = System.Drawing.SystemIcons.Application;
            }

            try
            {
                _taskbarIcon.ForceCreate();
            }
            catch
            {
                // Gracefully degrade in headless/test environments where taskbar is not present
            }
            this.Resources.Add("TrayIcon", _taskbarIcon);

            // Build Context Menu
            var menu = new ContextMenu();

            var showOverlayItem = new MenuItem { Header = "Show Overlay", FontWeight = FontWeights.Bold };
            showOverlayItem.Click += (s, ev) => ShowOverlay();

            var hideOverlayItem = new MenuItem { Header = "Hide Overlay" };
            hideOverlayItem.Click += (s, ev) => HideOverlay();

            var pausePollingItem = new MenuItem { Header = "Pause Polling" };
            pausePollingItem.Click += (s, ev) => TogglePolling(pausePollingItem);

            var refreshItem = new MenuItem { Header = "Refresh Now" };
            refreshItem.Click += (s, ev) => ForceRefresh();

            var settingsItem = new MenuItem { Header = "Open Settings..." };
            settingsItem.Click += (s, ev) => OpenSettings();

            var exportItem = new MenuItem { Header = "Export Diagnostics..." };
            exportItem.Click += (s, ev) => ExportDiagnostics();

            var exitItem = new MenuItem { Header = "Exit ModelPulse" };
            exitItem.Click += (s, ev) => ExitApp();

            menu.Items.Add(showOverlayItem);
            menu.Items.Add(hideOverlayItem);
            menu.Items.Add(new Separator());
            menu.Items.Add(pausePollingItem);
            menu.Items.Add(refreshItem);
            menu.Items.Add(new Separator());
            menu.Items.Add(settingsItem);
            menu.Items.Add(exportItem);
            menu.Items.Add(new Separator());
            menu.Items.Add(exitItem);

            _taskbarIcon.ContextMenu = menu;

            // Double click toggles overlay
            _taskbarIcon.TrayMouseDoubleClick += (s, ev) => ToggleOverlay();
        }

        private void ShowOverlay()
        {
            if (_overlayWindow == null)
            {
                _overlayWindow = new MainWindow(_collectorService!, _configService!);
                if (_taskbarIcon != null)
                {
                    _overlayWindow.RegisterTrayIcon(_taskbarIcon);
                }
            }
            _overlayWindow.Show();
            _overlayWindow.Activate();
        }

        private void HideOverlay()
        {
            _overlayWindow?.Hide();
        }

        private void ToggleOverlay()
        {
            if (_overlayWindow != null && _overlayWindow.IsVisible)
            {
                HideOverlay();
            }
            else
            {
                ShowOverlay();
            }
        }

        private void TogglePolling(MenuItem menuItem)
        {
            if (_isPollingPaused)
            {
                _collectorService?.StartAsync();
                menuItem.Header = "Pause Polling";
                _isPollingPaused = false;
            }
            else
            {
                _collectorService?.StopAsync();
                menuItem.Header = "Resume Polling";
                _isPollingPaused = true;
            }
        }

        private async void ExitApp()
        {
            _snapshotSubscription?.Dispose();
            _alertSubscription?.Dispose();
            _alertEngine?.Dispose();

            if (_collectorService != null)
            {
                await _collectorService.StopAsync();
                _collectorService.Dispose();
            }

            _taskbarIcon?.Dispose();
            _overlayWindow?.ExplicitClose();

            Shutdown();
        }

        /// <summary>
        /// Forces an immediate poll cycle by momentarily shortening the polling interval.
        /// Restores normal adaptive scheduling after 500ms (one fast cycle).
        /// Does NOT restart CollectorService — SetPollingInterval is hot-swappable.
        /// </summary>
        private void ForceRefresh()
        {
            if (_isPollingPaused) return;
            _collectorService?.SetPollingInterval(TimeSpan.FromMilliseconds(100));
            // Restore normal adaptive scheduling after one fast cycle
            Task.Delay(500).ContinueWith(_ =>
                Dispatcher.BeginInvoke(new Action(() =>
                    _collectorService?.SetPollingInterval(TimeSpan.Zero))));
        }

        /// <summary>
        /// Opens the Settings dialog. Passes live-apply callbacks so the
        /// Settings window can update opacity and polling interval without
        /// needing a reference to MainWindow or CollectorService directly.
        /// </summary>
        private void OpenSettings()
        {
            var settingsWindow = new SettingsWindow(
                _configService!,
                newOpacity =>
                {
                    if (_overlayWindow != null)
                        _overlayWindow.Dispatcher.BeginInvoke(new Action(() =>
                            _overlayWindow.Opacity = newOpacity));
                },
                newInterval =>
                {
                    _collectorService?.SetPollingInterval(newInterval);
                });

            settingsWindow.Owner = _overlayWindow;
            settingsWindow.WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner;
            settingsWindow.ShowDialog();
        }

        private void ExportDiagnostics()
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "JSON Files (*.json)|*.json",
                FileName = $"modelpulse-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.json",
                Title = "Export Diagnostics Snapshot"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var snapshot = _collectorService?.LatestSnapshot;
                    var settings = _configService?.CurrentSettings;

                    if (snapshot != null && settings != null)
                    {
                        var json = DiagnosticsExporter.Export(snapshot, settings);
                        System.IO.File.WriteAllText(dialog.FileName, json);
                        MessageBox.Show("Diagnostics exported successfully!", "ModelPulse", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        MessageBox.Show("Error: Telemetry data not yet collected.", "ModelPulse", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Export failed: {ex.Message}", "ModelPulse", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }
}
