using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using ModelPulse.Core.Services;
using ModelPulse.Core.Services.Config;
using ModelPulse.UI.ViewModels;
using H.NotifyIcon;
using H.NotifyIcon.Core;

namespace ModelPulse.UI
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly ICollectorService _collectorService;
        private readonly IConfigService _configService;
        private readonly OverlayViewModel _viewModel;

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        private TaskbarIcon? _trayIcon;

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hwnd, int index);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

        public MainWindow(ICollectorService collectorService, IConfigService configService)
        {
            InitializeComponent();

            _collectorService = collectorService;
            _configService = configService;
            _viewModel = new OverlayViewModel(_collectorService, _configService);
            DataContext = _viewModel;

            // Positioning: bottom right of primary screen
            Left = SystemParameters.PrimaryScreenWidth - 320;
            Top = SystemParameters.PrimaryScreenHeight - 480;

            // Listen to compact mode changes to toggle mouse pass-through
            _viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(OverlayViewModel.IsCompactMode))
                {
                    UpdateMousePassThrough();
                }
            };
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            UpdateMousePassThrough();
        }

        private void UpdateMousePassThrough()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;

            int extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            // Ensure overlay is never transparent to mouse clicks (pass-through disabled)
            // AND ensure it is a ToolWindow to hide it from the Alt-Tab switcher
            SetWindowLong(hwnd, GWL_EXSTYLE, (extendedStyle & ~WS_EX_TRANSPARENT) | WS_EX_TOOLWINDOW);
        }

        private void CompactOverlay_Click(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                if (e.ClickCount == 2)
                {
                    _viewModel.IsCompactMode = false;
                }
                else
                {
                    DragMove();
                }
            }
        }

        private void Collapse_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.IsCompactMode = true;
        }

        private void Hide_Click(object sender, RoutedEventArgs e)
        {
            Hide();
            _trayIcon?.ShowNotification("ModelPulse", "ModelPulse is still running in the system tray. Left-click or right-click the tray icon to manage or exit.", NotificationIcon.Info);
        }

        private bool _isExplicitClose = false;

        public void ExplicitClose()
        {
            _isExplicitClose = true;
            Close();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (!_isExplicitClose)
            {
                e.Cancel = true;
                Hide();
                _trayIcon?.ShowNotification("ModelPulse", "ModelPulse is still running in the system tray. Left-click or right-click the tray icon to manage or exit.", NotificationIcon.Info);
            }
            else
            {
                base.OnClosing(e);
            }
        }

        public void RegisterTrayIcon(FrameworkElement trayIcon)
        {
            this.AddLogicalChild(trayIcon);
            _trayIcon = trayIcon as TaskbarIcon;
        }
    }
}