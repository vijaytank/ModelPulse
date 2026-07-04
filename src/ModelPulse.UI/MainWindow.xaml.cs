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

        // ── Manual drag state ─────────────────────────────────────────────────
        private bool _isDragging;
        private Point _dragStartScreen; // cursor position in screen coords at drag start
        private double _winLeftAtDragStart;
        private double _winTopAtDragStart;

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hwnd, int index);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

        // ─── Global Hotkey Listener ─────────────────────────────────────────
        private const int WM_HOTKEY = 0x0312;
        private const int HOTKEY_TOGGLE_OVERLAY = 1;
        private const int VK_O = 0x4F;
        private const int MOD_CONTROL = 0x0001;
        private const int MOD_ALT = 0x0002;

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private HwndSource? _hwndSource;

        private void RegisterHotkeys()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
            {
                uint modifiers = MOD_CONTROL | MOD_ALT;
                RegisterHotKey(hwnd, HOTKEY_TOGGLE_OVERLAY, modifiers, VK_O);
            }
        }

        private void UnregisterHotkeys()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
            {
                UnregisterHotKey(hwnd, HOTKEY_TOGGLE_OVERLAY);
            }
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_TOGGLE_OVERLAY)
            {
                // Toggle compact mode when hotkey is pressed
                _viewModel.IsCompactMode = !_viewModel.IsCompactMode;
                handled = true;
            }
            return IntPtr.Zero;
        }

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
            
            var hwnd = new WindowInteropHelper(this).Handle;
            _hwndSource = HwndSource.FromHwnd(hwnd);
            _hwndSource?.AddHook(HwndHook);

            RegisterHotkeys();
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

        // ── Compact overlay mouse handlers ────────────────────────────────────

        /// <summary>
        /// MouseLeftButtonDown: begin a manual drag or register a double-click expand.
        /// </summary>
        private void CompactOverlay_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;

            if (e.ClickCount == 2)
            {
                // Double-click expands; cancel any pending drag
                _isDragging = false;
                _viewModel.IsCompactMode = false;
                e.Handled = true;
                return;
            }

            // Begin manual drag
            _isDragging = true;
            _dragStartScreen = PointToScreen(e.GetPosition(this));
            _winLeftAtDragStart = Left;
            _winTopAtDragStart  = Top;
            ((UIElement)sender).CaptureMouse();
            e.Handled = true;
        }

        /// <summary>
        /// MouseMove: update window position while dragging.
        /// </summary>
        private void CompactOverlay_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDragging || e.LeftButton != MouseButtonState.Pressed) return;

            var currentScreen = PointToScreen(e.GetPosition(this));
            var deltaX = currentScreen.X - _dragStartScreen.X;
            var deltaY = currentScreen.Y - _dragStartScreen.Y;

            Left = _winLeftAtDragStart + deltaX;
            Top  = _winTopAtDragStart  + deltaY;
        }

        /// <summary>
        /// MouseLeftButtonUp: end the drag and release mouse capture.
        /// </summary>
        private void CompactOverlay_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isDragging) return;

            _isDragging = false;
            ((UIElement)sender).ReleaseMouseCapture();
            e.Handled = true;
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

        protected override void OnClosed(EventArgs e)
        {
            _hwndSource?.RemoveHook(HwndHook);
            _hwndSource = null;
            UnregisterHotkeys();
            base.OnClosed(e);
        }
    }
}